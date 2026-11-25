using System.Collections.Concurrent;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Viridian.Core.Config;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Generation;
using Viridian.Core.Model3D.Illustration;
using Viridian.Core.Model3D.Profiles;
using Viridian.Core.Symbols;
using Viridian.Library;

namespace Viridian.Revit.Illustrated;

/// <summary>Um exemplar a desenhar: ponto de inserção e altura em pés, rotação, imagens da espécie e fase.</summary>
internal sealed record SpriteInstance(Vec3 BaseFeet, double Rotation, PlantSprites Sprites, int Stage, double HeightFeet);

/// <summary>O que uma vista mostra: os exemplares e a caixa que os envolve (pés).</summary>
internal sealed record ViewSnapshot(IReadOnlyList<SpriteInstance> Instances, XYZ Min, XYZ Max, long Signature);

/// <summary>
/// Vegetação ilustrada como RPC: nas vistas 3D, cortes e elevações, cada exemplar das espécies do catálogo é desenhado
/// como uma imagem que fica sempre de frente para a câmera (sem faces aparentes), com cores próprias em qualquer
/// estilo visual. A planta baixa não muda (continua o símbolo 2D). O desenho é feito pelo serviço DirectContext3D do
/// Revit: não cria elementos nem altera o modelo, só o que aparece na tela.
/// Também esconde, nas vistas 3D, as cores da planta baixa (que de outro modo ficariam no chão).
/// </summary>
public static class IllustratedVegetation
{
    /// <summary>Afastamento da imagem para a frente (m): cobre o eixo de seleção da família.</summary>
    public const double ForwardM = 0.12;

    private static readonly ConcurrentDictionary<string, ViewSnapshot> Views = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, PlantSprites> SpriteCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> Warming = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ColorsHiddenIn = new(StringComparer.Ordinal);
    private static SpriteServer? _server;
    private static volatile bool _dirty = true;
    private static volatile bool _ready;
    private static int _pending;
    private static DateTime _lastScan = DateTime.MinValue;
    private static Sources? _sources;
    private static bool _scanErrorLogged;

    /// <summary>Liga o desenho: registra o servidor no Revit e os eventos. Falhas são registradas e não impedem o plugin.</summary>
    public static void Start(UIControlledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        try
        {
            if (ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) is MultiServerService service)
            {
                _server = new SpriteServer();
                service.AddServer(_server);
                IList<Guid> active = service.GetActiveServerIds();
                active.Add(_server.GetServerId());
                service.SetActiveServers(active);
            }
            else
            {
                ViridianLog.Warn("Vegetação ilustrada: o serviço de desenho 3D do Revit não está disponível.");
            }
        }
        catch (Exception ex)
        {
            ViridianLog.Error("Vegetação ilustrada: não foi possível registrar o desenho 3D.", ex);
            _server = null;
        }

        application.Idling += OnIdling;
        application.ViewActivated += OnViewActivated;
        application.ControlledApplication.DocumentChanged += OnDocumentChanged;
        application.ControlledApplication.DocumentClosing += OnDocumentClosing;
    }

    public static void Stop(UIControlledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.Idling -= OnIdling;
        application.ViewActivated -= OnViewActivated;
        application.ControlledApplication.DocumentChanged -= OnDocumentChanged;
        application.ControlledApplication.DocumentClosing -= OnDocumentClosing;
        try
        {
            if (_server is not null && ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) is MultiServerService service)
            {
                service.RemoveServer(_server.GetServerId());
            }
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Vegetação ilustrada: falha ao encerrar o desenho 3D.", ex);
        }

        _server = null;
        Views.Clear();
    }

    /// <summary>Pede uma releitura (por exemplo, depois de mudar as configurações ou regerar famílias).</summary>
    public static void Invalidate() => _dirty = true;

    internal static bool TryGetSnapshot(View view, out ViewSnapshot snapshot)
    {
        snapshot = null!;
        return view is not null && Views.TryGetValue(view.UniqueId, out snapshot!) && snapshot.Instances.Count > 0;
    }

    /// <summary>Uma imagem foi pedida e ainda não está pronta: calcula a fase inteira em segundo plano.</summary>
    internal static void RequestWarm(PlantSprites sprites, int stage)
    {
        string key = sprites.Hash + "#" + sprites.StageFor(stage).Code;
        if (!Warming.TryAdd(key, 0))
        {
            return;
        }

        Interlocked.Increment(ref _pending);
        Task.Run(() =>
        {
            try
            {
                sprites.Warm(stage);
            }
            catch (Exception ex)
            {
                ViridianLog.Warn("Vegetação ilustrada: falha ao preparar as imagens.", ex);
            }
            finally
            {
                _ready = true;
                Interlocked.Decrement(ref _pending);
            }
        });
    }

    private static void OnDocumentChanged(object? sender, DocumentChangedEventArgs e) => _dirty = true;

    private static void OnViewActivated(object? sender, ViewActivatedEventArgs e) => _dirty = true;

    private static void OnDocumentClosing(object? sender, DocumentClosingEventArgs e)
    {
        try
        {
            using var collector = new FilteredElementCollector(e.Document);
            foreach (View view in collector.OfClass(typeof(View)).Cast<View>())
            {
                Views.TryRemove(view.UniqueId, out _);
            }
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Vegetação ilustrada: limpeza ao fechar o documento.", ex);
        }
    }

    private static void OnIdling(object? sender, IdlingEventArgs e)
    {
        if (sender is not UIApplication uiApp || uiApp.ActiveUIDocument is not UIDocument uiDoc)
        {
            return;
        }

        try
        {
            if (Volatile.Read(ref _pending) > 0)
            {
                e.SetRaiseWithoutDelay();
            }

            bool refresh = false;
            if (_ready)
            {
                _ready = false;
                refresh = true;
            }

            DateTime now = DateTime.UtcNow;
            if (_dirty || (now - _lastScan).TotalSeconds > 1.5)
            {
                _dirty = false;
                _lastScan = now;
                refresh |= Scan(uiDoc);
                HidePlanColors(uiDoc);
            }

            if (refresh && _server is not null)
            {
                uiDoc.RefreshActiveView();
            }
        }
        catch (Exception ex)
        {
            if (!_scanErrorLogged)
            {
                _scanErrorLogged = true;
                ViridianLog.Warn("Vegetação ilustrada: falha ao ler as vistas.", ex);
            }
        }
    }

    /// <summary>Relê os exemplares das vistas abertas. Devolve verdadeiro quando algo mudou.</summary>
    private static bool Scan(UIDocument uiDoc)
    {
        Document doc = uiDoc.Document;
        if (doc.IsFamilyDocument || _server is null)
        {
            return false;
        }

        Sources sources = _sources = Sources.Refresh(_sources);
        if (!sources.Settings.Model3D.IllustratedFamilies)
        {
            bool had = false;
            foreach (UIView uiView in uiDoc.GetOpenUIViews())
            {
                if (doc.GetElement(uiView.ViewId) is View v && Views.TryRemove(v.UniqueId, out _))
                {
                    had = true;
                }
            }

            return had;
        }

        Dictionary<ElementId, PlantSprites> symbols = SymbolSprites(doc, sources);
        bool changed = false;
        foreach (UIView uiView in uiDoc.GetOpenUIViews())
        {
            if (doc.GetElement(uiView.ViewId) is not View view)
            {
                continue;
            }

            ViewSnapshot snapshot = Eligible(view) && symbols.Count > 0 ? Read(doc, view, symbols) : Empty;
            Views.TryGetValue(view.UniqueId, out ViewSnapshot? previous);
            if (previous is null || previous.Signature != snapshot.Signature)
            {
                Views[view.UniqueId] = snapshot;
                changed = true;
            }
        }

        return changed;
    }

    private static readonly ViewSnapshot Empty = new(Array.Empty<SpriteInstance>(), XYZ.Zero, XYZ.Zero, 0);

    private static bool Eligible(View view) => !view.IsTemplate && view.ViewType is ViewType.ThreeD or ViewType.Section or ViewType.Elevation or ViewType.Detail;

    /// <summary>Tipos de vegetação do projeto que têm imagens (espécies sem família personalizada).</summary>
    private static Dictionary<ElementId, PlantSprites> SymbolSprites(Document doc, Sources sources)
    {
        var result = new Dictionary<ElementId, PlantSprites>();
        bool south = true;
        bool southRead = false;
        foreach ((FamilySymbol symbol, SpeciesLink link) in SpeciesFamilyService.LinkedSymbols(doc))
        {
            if (link.IsCustom || sources.Catalog.Find(link.SpeciesId) is not Species species || !PlantSpritesPlanner.IsIllustrated(species))
            {
                continue;
            }

            // Só famílias já preparadas para a vegetação ilustrada (com a marca na assinatura). As geradas antes — só 2D
            // ou com os planos cruzados da prévia — passam a ser desenhadas depois de Atualizar Dados, sem duplicar.
            if (!link.Has3D || !link.GeometryHash.Contains(SpriteHandle.Marker, StringComparison.Ordinal))
            {
                continue;
            }

            if (!southRead)
            {
                south = MaqueteService.SouthernHemisphere(doc, sources.Settings);
                southRead = true;
            }

            result[symbol.Id] = SpritesFor(species, link.Variant, sources, south);
        }

        return result;
    }

    internal static PlantSprites SpritesFor(Species species, int variant, Sources sources, bool south)
    {
        string key = string.Join("#", species.Id, variant, south ? "S" : "N", sources.Settings.Model3D.TrunkColor,
            GeometryHash.Text(ViridianJson.Serialize(species)), GeometryHash.Text(ViridianJson.Serialize(sources.Settings.Colors)));
        return SpriteCache.GetOrAdd(key, _ => PlantSpritesPlanner.Build(species, variant, sources.Profiles, sources.Settings.Colors, sources.Settings.Model3D.TrunkColor, south));
    }

    private static ViewSnapshot Read(Document doc, View view, Dictionary<ElementId, PlantSprites> symbols)
    {
        var planting = new ElementId(BuiltInCategory.OST_Planting);
        if (view.GetCategoryHidden(planting))
        {
            return Empty;
        }

        Category? category = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Planting);
        if (category is not null && category.SubCategories.Contains(SymbolFamilyBuilder.VolumeSubcategory)
            && view.GetCategoryHidden(category.SubCategories.get_Item(SymbolFamilyBuilder.VolumeSubcategory).Id))
        {
            return Empty;
        }

        var instances = new List<SpriteInstance>();
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        long signature = 17;
        using var collector = new FilteredElementCollector(doc, view.Id);
        foreach (FamilyInstance instance in collector.OfCategory(BuiltInCategory.OST_Planting).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
        {
            if (!symbols.TryGetValue(instance.Symbol.Id, out PlantSprites? sprites))
            {
                continue;
            }

            if (instance.LookupParameter(SymbolFamilyBuilder.VisibleParameter) is Parameter visible && visible.StorageType == StorageType.Integer && visible.AsInteger() == 0)
            {
                continue;
            }

            int stage = PlantSpritesPlanner.DefaultStage;
            if (instance.LookupParameter(SymbolFamilyBuilder.StageParameter) is Parameter stageParameter && stageParameter.StorageType == StorageType.Integer)
            {
                stage = stageParameter.AsInteger();
            }

            SpriteStage drawn = sprites.StageFor(stage);
            Transform transform = instance.GetTransform();
            XYZ o = transform.Origin;
            double rotation = Math.Atan2(transform.BasisX.Y, transform.BasisX.X);
            double height = RevitUnits.ToFeet(drawn.HeightM);
            double radius = RevitUnits.ToFeet(Math.Max(drawn.CrownM, drawn.HeightM) / 2 + ForwardM);
            instances.Add(new SpriteInstance(new Vec3(o.X, o.Y, o.Z), rotation, sprites, drawn.Code, height));
            minX = Math.Min(minX, o.X - radius);
            minY = Math.Min(minY, o.Y - radius);
            minZ = Math.Min(minZ, o.Z - radius);
            maxX = Math.Max(maxX, o.X + radius);
            maxY = Math.Max(maxY, o.Y + radius);
            maxZ = Math.Max(maxZ, o.Z + height + radius);
            unchecked
            {
                signature = signature * 31 + instance.Id.Value;
                signature = signature * 31 + BitConverter.DoubleToInt64Bits(Math.Round(o.X, 4) + 3 * Math.Round(o.Y, 4) + 7 * Math.Round(o.Z, 4) + 11 * Math.Round(rotation, 4));
                signature = signature * 31 + drawn.Code;
                signature = signature * 31 + sprites.Hash.GetHashCode(StringComparison.Ordinal);
            }

            if (!sprites.IsComplete(drawn.Code))
            {
                RequestWarm(sprites, drawn.Code);
            }
        }

        return instances.Count == 0
            ? Empty
            : new ViewSnapshot(instances, new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ), signature == 0 ? 1 : signature);
    }

    /// <summary>
    /// As cores da planta baixa (copa e floração) são superfícies finas no chão da família: nas vistas 3D ficariam
    /// como um disco colorido sob a planta. Esconde as duas subcategorias em cada vista 3D aberta, uma vez por sessão
    /// (se o usuário as religar, ficam ligadas). Vistas controladas por modelo de vista não são alteradas.
    /// </summary>
    private static void HidePlanColors(UIDocument uiDoc)
    {
        Document doc = uiDoc.Document;
        if (doc.IsFamilyDocument || doc.IsReadOnly || doc.IsModifiable || _sources is null || !_sources.Settings.Model3D.IllustratedFamilies)
        {
            return;
        }

        if (uiDoc.ActiveGraphicalView is not View3D view || view.IsTemplate || ColorsHiddenIn.Contains(view.UniqueId))
        {
            return;
        }

        Category? planting = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Planting);
        if (planting is null)
        {
            return;
        }

        var ids = new List<ElementId>();
        foreach (string name in new[] { TierVisibility.SubcategoryName(SymbolTier.Fill), TierVisibility.SubcategoryName(SymbolTier.Flowers) })
        {
            if (planting.SubCategories.Contains(name))
            {
                ElementId id = planting.SubCategories.get_Item(name).Id;
                if (view.CanCategoryBeHidden(id) && !view.GetCategoryHidden(id))
                {
                    ids.Add(id);
                }
            }
        }

        if (ids.Count == 0)
        {
            return;
        }

        // A partir daqui a vista é tratada uma vez só nesta sessão.
        ColorsHiddenIn.Add(view.UniqueId);
        if (view.ViewTemplateId != ElementId.InvalidElementId && doc.GetElement(view.ViewTemplateId) is View template
            && template.GetNonControlledTemplateParameterIds().All(p => p.Value != (long)BuiltInParameter.VIS_GRAPHICS_MODEL))
        {
            return;
        }

        try
        {
            using var transaction = new Transaction(doc, "Viridian: cores da planta fora do 3D");
            if (transaction.Start() != TransactionStatus.Started)
            {
                return;
            }

            foreach (ElementId id in ids)
            {
                view.SetCategoryHidden(id, true);
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Vegetação ilustrada: não foi possível esconder as cores da planta na vista 3D " + view.Name + ".", ex);
        }
    }

    /// <summary>Configurações, catálogo e perfis, relidos quando os arquivos mudam.</summary>
    internal sealed class Sources
    {
        private Sources(ViridianSettings settings, CatalogService catalog, IReadOnlyList<Plant3DProfile> profiles, DateTime settingsStamp, DateTime catalogStamp)
        {
            Settings = settings;
            Catalog = catalog;
            Profiles = profiles;
            SettingsStamp = settingsStamp;
            CatalogStamp = catalogStamp;
        }

        public ViridianSettings Settings { get; }

        public CatalogService Catalog { get; }

        public IReadOnlyList<Plant3DProfile> Profiles { get; }

        private DateTime SettingsStamp { get; }

        private DateTime CatalogStamp { get; }

        private static DateTime Stamp(string file)
        {
            try
            {
                return File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return DateTime.MinValue;
            }
        }

        public static Sources Refresh(Sources? current)
        {
            DateTime settingsStamp = Stamp(LibraryPaths.SettingsFile);
            if (current is not null && current.SettingsStamp == settingsStamp)
            {
                var paths = new LibraryPaths(current.Settings.LibraryRoot);
                if (Stamp(paths.CatalogFile) == current.CatalogStamp)
                {
                    return current;
                }
            }

            ViridianSettings settings = SettingsStore.Load();
            var libraryPaths = new LibraryPaths(settings.LibraryRoot);
            var catalog = new CatalogService(new CatalogStore(libraryPaths.CatalogFile));
            IReadOnlyList<Plant3DProfile> profiles;
            try
            {
                profiles = new Plant3DProfileLibrary(libraryPaths).Profiles;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                profiles = Plant3DProfileDefaults.Create();
            }

            return new Sources(settings, catalog, profiles, settingsStamp, Stamp(libraryPaths.CatalogFile));
        }
    }
}
