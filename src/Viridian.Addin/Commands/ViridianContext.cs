using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Microsoft.Win32;
using Viridian.Core.Config;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Library;
using Viridian.Revit;
using Viridian.UI;
using Viridian.UI.Windows;

namespace Viridian.Addin.Commands;

/// <summary>Usuário cancelou a operação (nenhuma mensagem é exibida).</summary>
internal sealed class CommandCancelledException : Exception
{
    public CommandCancelledException()
        : base("Operação cancelada.")
    {
    }
}

/// <summary>Tudo o que um comando precisa: documento, configurações, catálogo e serviços.</summary>
internal sealed class ViridianContext
{
    private ProjectData? _project;

    private ViridianContext(UIApplication uiApp)
    {
        UiApp = uiApp;
        UiDoc = uiApp.ActiveUIDocument;
        Settings = SettingsStore.Load();
        UI.ViridianWindow.Accessibility = Settings.Accessibility;
        Paths = new LibraryPaths(Settings.LibraryRoot);
        try
        {
            Paths.EnsureCreated();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Biblioteca em pasta de rede somente leitura: continua apenas com leitura.
            ViridianLog.Warn("Não foi possível criar as pastas da biblioteca em " + Paths.Root, ex);
        }

        Catalog = new CatalogService(new CatalogStore(Paths.CatalogFile));
        Families = new SpeciesFamilyService(Settings, Paths);
        Planting = new PlantingService(Settings);
        Massings = new MassingService(Settings, Planting);
        Scales = new ScaleProfileService(Settings);
        Labels = new LabelService(Settings);
        Documentation = new DocumentationService(Settings, Labels);
    }

    public UIApplication UiApp { get; }

    public UIDocument? UiDoc { get; }

    public ViridianSettings Settings { get; }

    public LibraryPaths Paths { get; }

    public CatalogService Catalog { get; }

    public SpeciesFamilyService Families { get; }

    public PlantingService Planting { get; }

    public MassingService Massings { get; }

    public ScaleProfileService Scales { get; }

    public LabelService Labels { get; }

    public DocumentationService Documentation { get; }

    /// <summary>Documento de projeto ativo (lança exceção quando não há um).</summary>
    public Document Doc => UiDoc?.Document ?? throw new ViridianException("Abra um projeto do Revit para usar este comando.");

    public UIDocument UiDocument => UiDoc ?? throw new ViridianException("Abra um projeto do Revit para usar este comando.");

    public bool HasProject => UiDoc?.Document is { IsFamilyDocument: false };

    /// <summary>Dados do Viridian gravados no projeto (lidos uma vez por comando).</summary>
    public ProjectData Project => _project ??= ElementData.ReadProject(Doc);

    public void InvalidateProject() => _project = null;

    public static ViridianContext Create(ExternalCommandData data, bool requireProject)
    {
        var context = new ViridianContext(data.Application);
        if (requireProject)
        {
            if (context.UiDoc is null)
            {
                throw new ViridianException("Abra um projeto do Revit para usar este comando.");
            }

            if (context.UiDoc.Document.IsFamilyDocument)
            {
                throw new ViridianException("Este comando funciona em projetos, não no editor de famílias.");
            }
        }

        if (!string.IsNullOrEmpty(context.Catalog.LoadWarning))
        {
            TaskDialog.Show("Viridian", context.Catalog.LoadWarning);
        }

        return context;
    }

    public Species? Lookup(string id) => Catalog.Find(id);

    public string ProjectCode(Species species) => SpeciesFamilyService.ProjectCodeOf(Project, species);

    // ------------------------------------------------------------------ janelas

    public bool ShowDialog(ViridianWindow window)
    {
        window.SetOwnerHandle(UiApp.MainWindowHandle);
        return window.ShowDialog() == true;
    }

    /// <summary>Abre a biblioteca para escolher uma espécie. Lança cancelamento quando o usuário fecha a janela.</summary>
    public Species PickSpecies(string caption)
    {
        var browser = new SpeciesBrowserWindow(Catalog, Paths, Settings, caption);
        if (!ShowDialog(browser) || browser.SelectedSpecies is null)
        {
            throw new CommandCancelledException();
        }

        return browser.SelectedSpecies;
    }

    public void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViridianLog.Error("Falha ao gravar as configurações.", ex);
            TaskDialog.Show("Viridian", "Não foi possível gravar as configurações:\n" + ex.Message);
        }
    }

    // ------------------------------------------------------------------ vistas e seleção

    /// <summary>Vista em planta ativa, exigida pelos comandos de plantio.</summary>
    public ViewPlan RequirePlanView()
    {
        if (UiDocument.ActiveView is ViewPlan plan && !plan.IsTemplate && plan.GenLevel is not null && ScaleProfileService.IsApplicable(plan))
        {
            return plan;
        }

        throw new ViridianException("Abra uma vista em planta (planta de piso, de forro ou de área) para usar este comando.");
    }

    /// <summary>Elementos selecionados no momento.</summary>
    public IReadOnlyList<Element> SelectedElements() =>
        UiDocument.Selection.GetElementIds().Select(id => Doc.GetElement(id)).Where(e => e is not null).ToList();

    public IReadOnlyList<FamilyInstance> SelectedPlants() =>
        PlantingService.PlantingInstances(Doc, UiDocument.Selection.GetElementIds());

    public IReadOnlyList<FilledRegion> SelectedMassings() =>
        SelectedElements().OfType<FilledRegion>().Where(r => ElementData.KindOf(r) == ElementData.KindMassing).ToList();

    /// <summary>Exemplares selecionados ou, sem seleção, escolhidos pelo usuário na vista.</summary>
    public IReadOnlyList<FamilyInstance> SelectedOrPickedPlants(string prompt)
    {
        IReadOnlyList<FamilyInstance> selected = SelectedPlants();
        if (selected.Count > 0)
        {
            return selected;
        }

        IList<Reference> picked = UiDocument.Selection.PickObjects(ObjectType.Element, new PlantingInstanceFilter(), prompt);
        return PlantingService.PlantingInstances(Doc, picked.Select(r => r.ElementId));
    }

    /// <summary>Seleciona e mostra elementos no Revit.</summary>
    public void ShowElements(IEnumerable<long> ids)
    {
        var existing = ids.Select(id => new ElementId(id)).Where(id => Doc.GetElement(id) is not null).ToList();
        if (existing.Count == 0)
        {
            TaskDialog.Show("Viridian", "Os elementos desta ocorrência não existem mais no projeto.");
            return;
        }

        UiDocument.Selection.SetElementIds(existing);
        try
        {
            UiDocument.ShowElements(existing);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Elementos sem vista adequada aberta: ficam apenas selecionados.
        }
    }

    public void Select(IEnumerable<ElementId> ids)
    {
        try
        {
            UiDocument.Selection.SetElementIds(ids.ToList());
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Não foi possível selecionar os elementos criados.", ex);
        }
    }

    /// <summary>
    /// Obtém o limite de uma região de plantio: elemento selecionado, elemento escolhido na vista ou contorno desenhado.
    /// </summary>
    public RegionBoundary PickRegion(string purpose)
    {
        List<Element> candidates = SelectedElements().Where(BoundaryReader.IsSupported).ToList();
        if (candidates.Count == 1)
        {
            return ValidRegion(BoundaryReader.Read(candidates[0]));
        }

        var dialog = new TaskDialog("Viridian")
        {
            MainInstruction = "Região para " + purpose,
            MainContent = "Escolha como definir o limite do canteiro.",
            CommonButtons = TaskDialogCommonButtons.Cancel,
            AllowCancellation = true,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Selecionar um elemento existente", "Região preenchida, piso, toposólido, área ou ambiente.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Desenhar o contorno", "Clique nos vértices na vista; ESC conclui.");
        TaskDialogResult choice = dialog.Show();

        if (choice == TaskDialogResult.CommandLink1)
        {
            Reference reference = UiDocument.Selection.PickObject(ObjectType.Element, new BoundaryElementFilter(), "Selecione o limite do canteiro");
            return ValidRegion(BoundaryReader.Read(Doc.GetElement(reference)));
        }

        if (choice == TaskDialogResult.CommandLink2)
        {
            Polygon2? polygon = RegionPicker.PickPolygon(UiDocument, "Clique nos vértices do canteiro");
            if (polygon is null)
            {
                throw new CommandCancelledException();
            }

            return ValidRegion(new RegionBoundary(polygon, "contorno desenhado"));
        }

        throw new CommandCancelledException();
    }

    private static RegionBoundary ValidRegion(RegionBoundary region)
    {
        if (region.Polygon.IsEmpty || !(region.Polygon.Area > 1e-4))
        {
            throw new ViridianException($"O {region.Source} selecionado não possui um limite fechado com área válida.");
        }

        if (region.Polygon.HasSelfIntersections())
        {
            throw new ViridianException($"O limite do {region.Source} cruza a si mesmo. Corrija o contorno e tente novamente.");
        }

        return region;
    }

    // ------------------------------------------------------------------ famílias

    /// <summary>
    /// Garante no projeto os tipos de família da espécie: a variante informada, ou todas quando <paramref name="variant"/> é negativa.
    /// Quando nenhum modelo de família é encontrado, pede ao usuário que indique um arquivo .rft.
    /// </summary>
    public IReadOnlyList<FamilySymbol> EnsureVariants(Species species, int variant)
    {
        int[] indices = species.HasCustomPlantingFamily
            ? new[] { 0 }
            : variant >= 0 ? new[] { Math.Min(variant, species.EffectiveVariants - 1) } : Enumerable.Range(0, species.EffectiveVariants).ToArray();

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var symbols = new List<FamilySymbol>();
                foreach (int index in indices)
                {
                    FamilySymbol symbol = Families.EnsureSymbol(Doc, species, index, Project);
                    if (symbols.All(s => s.Id != symbol.Id))
                    {
                        symbols.Add(symbol);
                    }
                }

                if (Families.TemplateFileChanged)
                {
                    SaveSettings();
                }

                AfterSymbolsLoaded();
                return symbols;
            }
            catch (FamilyTemplateMissingException ex) when (attempt == 0)
            {
                if (!AskForTemplate(ex.Message))
                {
                    throw new CommandCancelledException();
                }
            }
        }
    }

    /// <summary>Aplica padrão gráfico e perfis de escala depois que novas famílias entram no projeto.</summary>
    public void AfterSymbolsLoaded()
    {
        try
        {
            Scales.ApplyGraphicStandards(Doc);
            Scales.Apply(Doc, ScaleProfileService.ApplicableViews(Doc));
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or InvalidOperationException)
        {
            ViridianLog.Warn("Não foi possível aplicar os perfis de escala após carregar famílias.", ex);
        }
    }

    private bool AskForTemplate(string reason)
    {
        var dialog = new TaskDialog("Viridian")
        {
            MainInstruction = "Modelo de família necessário",
            MainContent = reason + "\n\nIndique um modelo de família de Vegetação ou de Modelo genérico métrico (.rft). "
                          + "O Viridian vai lembrar dessa escolha.",
            CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
        };
        if (dialog.Show() != TaskDialogResult.Ok)
        {
            return false;
        }

        var open = new OpenFileDialog
        {
            Title = "Modelo de família (.rft)",
            Filter = "Modelos de família do Revit (*.rft)|*.rft",
        };
        if (open.ShowDialog() != true)
        {
            return false;
        }

        Settings.FamilyTemplateFile = open.FileName;
        SaveSettings();
        return true;
    }

    // ------------------------------------------------------------------ transações

    /// <summary>
    /// Executa um conjunto de operações como um único passo de desfazer. Em caso de erro, desfaz tudo.
    /// </summary>
    public T Grouped<T>(string name, Func<T> work, Func<T, bool> keep)
    {
        using var group = new TransactionGroup(Doc, name);
        if (group.Start() != TransactionStatus.Started)
        {
            throw new ViridianException("Não foi possível iniciar a operação \"" + name + "\".");
        }

        try
        {
            T result = work();
            if (keep(result))
            {
                group.Assimilate();
            }
            else
            {
                group.RollBack();
            }

            return result;
        }
        catch
        {
            if (group.HasStarted() && !group.HasEnded())
            {
                group.RollBack();
            }

            throw;
        }
    }
}
