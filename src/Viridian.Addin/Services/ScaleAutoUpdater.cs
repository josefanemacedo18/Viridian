using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Viridian.Core.Config;
using Viridian.Library;
using Viridian.Revit;

namespace Viridian.Addin.Services;

/// <summary>
/// Reaplica o perfil de representação quando a escala de uma vista em planta muda.
/// O evento DocumentChanged é somente leitura, por isso as vistas afetadas são enfileiradas e
/// a alteração é feita depois, em contexto válido, por um ExternalEvent.
/// </summary>
internal sealed class ScaleAutoUpdater : IExternalEventHandler
{
    private const string OwnTransactionName = "Viridian: perfil de escala";

    private readonly object _gate = new();
    private readonly Dictionary<Document, HashSet<ElementId>> _pending = new();
    private readonly Dictionary<Document, Dictionary<ElementId, int>> _knownScales = new();
    private ExternalEvent? _event;
    private ControlledApplication? _application;
    private ViridianSettings? _settings;
    private DateTime _settingsLoadedAt = DateTime.MinValue;

    public void Attach(UIControlledApplication application)
    {
        _event = ExternalEvent.Create(this);
        _application = application.ControlledApplication;
        _application.DocumentChanged += OnDocumentChanged;
        _application.DocumentClosing += OnDocumentClosing;
        _application.DocumentOpened += OnDocumentOpened;
        _application.DocumentCreated += OnDocumentCreated;
    }

    public void Detach()
    {
        if (_application is not null)
        {
            _application.DocumentChanged -= OnDocumentChanged;
            _application.DocumentClosing -= OnDocumentClosing;
            _application.DocumentOpened -= OnDocumentOpened;
            _application.DocumentCreated -= OnDocumentCreated;
            _application = null;
        }

        _event?.Dispose();
        _event = null;
    }

    public string GetName() => "Viridian: perfis de escala";

    private ViridianSettings Settings
    {
        get
        {
            // Relê as configurações no máximo a cada 10 segundos (podem ter sido alteradas em outro computador).
            if (_settings is null || DateTime.UtcNow - _settingsLoadedAt > TimeSpan.FromSeconds(10))
            {
                _settings = SettingsStore.Load();
                _settingsLoadedAt = DateTime.UtcNow;
            }

            return _settings;
        }
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        try
        {
            // Desfazer e refazer não reaplicam o perfil: senão o usuário nunca conseguiria desfazer a mudança de escala.
            if (e.Operation != UndoOperation.TransactionCommitted)
            {
                return;
            }

            Document document = e.GetDocument();
            if (document is null || document.IsFamilyDocument || document.IsLinked)
            {
                return;
            }

            if (e.GetTransactionNames().Any(n => string.Equals(n, OwnTransactionName, StringComparison.Ordinal)))
            {
                return;
            }

            var filter = new ElementClassFilter(typeof(ViewPlan));
            var candidates = e.GetModifiedElementIds(filter).Concat(e.GetAddedElementIds(filter)).ToList();
            if (candidates.Count == 0 || !Settings.AutoApplyScaleProfiles)
            {
                return;
            }

            var service = new ScaleProfileService(Settings);
            var tiers = ScaleProfileService.TierSubcategories(document);
            if (tiers.Count == 0)
            {
                return;
            }

            var added = new HashSet<ElementId>(e.GetAddedElementIds(filter));
            Dictionary<ElementId, int> known = KnownScales(document);
            var needed = new List<ElementId>();
            foreach (ElementId id in candidates)
            {
                if (document.GetElement(id) is not View view || !ScaleProfileService.IsApplicable(view))
                {
                    continue;
                }

                // Só reage a vistas novas e a mudanças reais de escala: ajustes manuais de visibilidade são respeitados.
                int scale = view.Scale;
                bool relevant = added.Contains(id) || (known.TryGetValue(id, out int previous) && previous != scale);
                known[id] = scale;
                if (relevant && service.NeedsUpdate(document, view, tiers))
                {
                    needed.Add(id);
                }
            }

            if (needed.Count == 0)
            {
                return;
            }

            lock (_gate)
            {
                if (!_pending.TryGetValue(document, out HashSet<ElementId>? set))
                {
                    set = new HashSet<ElementId>();
                    _pending[document] = set;
                }

                set.UnionWith(needed);
            }

            _event?.Raise();
        }
        catch (Exception ex)
        {
            // Um erro aqui nunca pode interromper o trabalho do usuário.
            ViridianLog.Warn("Falha ao observar mudanças de escala.", ex);
        }
    }

    private void OnDocumentClosing(object? sender, DocumentClosingEventArgs e)
    {
        lock (_gate)
        {
            _pending.Remove(e.Document);
            _knownScales.Remove(e.Document);
        }
    }

    private void OnDocumentOpened(object? sender, DocumentOpenedEventArgs e) => Snapshot(e.Document);

    private void OnDocumentCreated(object? sender, DocumentCreatedEventArgs e) => Snapshot(e.Document);

    /// <summary>Registra a escala atual de cada planta para detectar mudanças depois.</summary>
    private void Snapshot(Document? document)
    {
        try
        {
            if (document is null || document.IsFamilyDocument || document.IsLinked)
            {
                return;
            }

            KnownScales(document);
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Falha ao registrar as escalas das vistas.", ex);
        }
    }

    private Dictionary<ElementId, int> KnownScales(Document document)
    {
        lock (_gate)
        {
            if (!_knownScales.TryGetValue(document, out Dictionary<ElementId, int>? scales))
            {
                scales = new Dictionary<ElementId, int>();
                foreach (View view in ScaleProfileService.ApplicableViews(document))
                {
                    scales[view.Id] = view.Scale;
                }

                _knownScales[document] = scales;
            }

            return scales;
        }
    }

    public void Execute(UIApplication app)
    {
        List<KeyValuePair<Document, HashSet<ElementId>>> work;
        lock (_gate)
        {
            work = _pending.ToList();
            _pending.Clear();
        }

        foreach ((Document document, HashSet<ElementId> ids) in work)
        {
            try
            {
                if (!document.IsValidObject || document.IsReadOnly || document.IsModifiable || !Settings.AutoApplyScaleProfiles)
                {
                    continue;
                }

                List<View> views = ids.Select(id => document.GetElement(id)).OfType<View>().ToList();
                if (views.Count > 0)
                {
                    new ScaleProfileService(Settings).Apply(document, views);
                }
            }
            catch (Exception ex)
            {
                ViridianLog.Warn("Não foi possível aplicar automaticamente o perfil de escala.", ex);
            }
        }
    }
}
