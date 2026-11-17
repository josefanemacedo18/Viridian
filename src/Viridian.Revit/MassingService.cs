using Autodesk.Revit.DB;
using Viridian.Core.Config;
using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Core.Hatch;
using Viridian.Core.Massing;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Tudo o que é necessário para criar ou recalcular um maciço.</summary>
public sealed class MassingExecution
{
    public MassingRecord Record { get; set; } = new();

    public Species Species { get; set; } = new();

    /// <summary>Código da espécie neste projeto.</summary>
    public string ProjectSpeciesCode { get; set; } = string.Empty;

    /// <summary>Hachura do maciço. Nula quando o modo é somente símbolos.</summary>
    public HatchPattern? Hatch { get; set; }

    /// <summary>Tipos de família Vegetação da espécie (um por variante), para símbolos BIM.</summary>
    public IReadOnlyList<FamilySymbol> PlantingSymbols { get; set; } = Array.Empty<FamilySymbol>();

    /// <summary>Componente de detalhe personalizado, quando os símbolos são apenas gráficos.</summary>
    public FamilySymbol? DetailSymbol { get; set; }
}

public sealed class MassingOutcome
{
    public MassingOutcome(FilledRegion region, MassingRecord record, int symbolsPlaced)
    {
        Region = region;
        Record = record;
        SymbolsPlaced = symbolsPlaced;
    }

    public FilledRegion Region { get; }

    public MassingRecord Record { get; }

    public int SymbolsPlaced { get; }
}

/// <summary>Criação, recálculo e leitura de maciços vegetais (regiões preenchidas com registro de plantio).</summary>
public sealed class MassingService
{
    private readonly ViridianSettings _settings;
    private readonly PlantingService _planting;

    public MassingService(ViridianSettings settings, PlantingService planting)
    {
        _settings = settings;
        _planting = planting;
    }

    /// <summary>Próximo código de maciço livre no projeto.</summary>
    public string NextId(Document document) =>
        MassingCalculator.NextId(ReadMassings(document).Select(m => m.Record.Id), _settings.MassingPrefix);

    /// <summary>
    /// Cria um maciço em uma vista. Quando <paramref name="existingRegion"/> é informada, ela é convertida em maciço;
    /// caso contrário uma nova região é criada com o limite do polígono.
    /// </summary>
    public MassingOutcome Create(Document document, View view, Polygon2 polygon, FilledRegion? existingRegion, MassingExecution execution)
    {
        if (polygon.IsEmpty || !(polygon.Area > 1e-6))
        {
            throw new ViridianException("O limite do maciço não possui área válida.");
        }

        if (polygon.HasSelfIntersections())
        {
            throw new ViridianException("O limite do maciço cruza a si mesmo. Corrija o contorno e tente novamente.");
        }

        return Tx.Run(document, "Viridian: maciço", () =>
        {
            FilledRegionType type = HatchService.EnsureRegionType(document, RegionHatch(execution));
            FilledRegion region;
            if (existingRegion is not null)
            {
                region = existingRegion;
                if (region.GetTypeId() != type.Id)
                {
                    region.ChangeTypeId(type.Id);
                }
            }
            else
            {
                double z = PlantingService.LevelOf(view)?.ProjectElevation ?? 0;
                IList<CurveLoop> loops = BoundaryReader.ToCurveLoops(polygon, z, document.Application.ShortCurveTolerance);
                if (loops.Count == 0)
                {
                    throw new ViridianException("O limite informado é pequeno demais para criar uma região.");
                }

                try
                {
                    region = FilledRegion.Create(document, type.Id, view.Id, loops);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    throw new ViridianException(
                        "O Revit não aceitou o limite do maciço nesta vista (use uma vista em planta e um contorno fechado sem cruzamentos).\n" + ex.Message,
                        ex);
                }
            }

            return Apply(document, view, region, polygon, execution);
        }, suppressWarnings: true);
    }

    /// <summary>
    /// Recalcula um maciço a partir do limite atual da região: área, quantidade e símbolos.
    /// A distribuição é refeita com a semente gravada no registro, mantendo o mesmo padrão.
    /// </summary>
    public MassingOutcome Recalculate(Document document, FilledRegion region, MassingExecution execution)
    {
        View view = document.GetElement(region.OwnerViewId) as View
            ?? throw new ViridianException("A vista do maciço não foi encontrada.");
        Polygon2 polygon = BoundaryReader.FromLoops(region.GetBoundaries());
        if (polygon.IsEmpty)
        {
            throw new ViridianException("A região do maciço não possui um limite fechado válido.");
        }

        return Tx.Run(document, "Viridian: recalcular maciço", () =>
        {
            FilledRegionType type = HatchService.EnsureRegionType(document, RegionHatch(execution));
            if (region.GetTypeId() != type.Id)
            {
                region.ChangeTypeId(type.Id);
            }

            return Apply(document, view, region, polygon, execution);
        }, suppressWarnings: true);
    }

    /// <summary>Remove os símbolos de um maciço (exemplares e componentes de detalhe). Precisa de transação.</summary>
    public static int RemoveSymbols(Document document, string massingId)
    {
        if (string.IsNullOrWhiteSpace(massingId))
        {
            return 0;
        }

        var ids = new List<ElementId>();
        ids.AddRange(PlantingService.InstancesOfMassing(document, massingId).Select(i => i.Id));
        foreach (Element element in ElementData.ElementsOfKind(document, ElementData.KindMassSymbol))
        {
            if (ElementData.TryRead(element, ElementData.KindMassSymbol, out MassSymbolLink link)
                && string.Equals(link.MassingId.Trim(), massingId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(element.Id);
            }
        }

        if (ids.Count > 0)
        {
            document.Delete(ids);
        }

        return ids.Count;
    }

    /// <summary>Lê todos os maciços do projeto.</summary>
    public static IReadOnlyList<MassingInfo> ReadMassings(Document document)
    {
        var result = new List<MassingInfo>();
        var viewNames = new Dictionary<ElementId, string>();
        foreach (Element element in ElementData.ElementsOfKind(document, ElementData.KindMassing))
        {
            if (element is not FilledRegion region || !ElementData.TryRead(region, ElementData.KindMassing, out MassingRecord record))
            {
                continue;
            }

            if (!viewNames.TryGetValue(region.OwnerViewId, out string? viewName))
            {
                viewName = document.GetElement(region.OwnerViewId)?.Name ?? string.Empty;
                viewNames[region.OwnerViewId] = viewName;
            }

            Polygon2? boundary = null;
            try
            {
                boundary = BoundaryReader.FromLoops(region.GetBoundaries());
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                ViridianLog.Warn("Não foi possível ler o limite do maciço " + record.Id, ex);
            }

            result.Add(new MassingInfo
            {
                ElementId = region.Id.Value,
                ViewId = region.OwnerViewId.Value,
                ViewName = viewName,
                Record = record,
                Boundary = boundary,
            });
        }

        return result;
    }

    /// <summary>
    /// Atualiza código, nomes e unidade gravados nos maciços a partir do catálogo, sem refazer a distribuição.
    /// Retorna a quantidade de maciços alterados.
    /// </summary>
    public static int RefreshRecords(Document document, Func<string, Species?> lookup, ProjectData project)
    {
        var changes = new List<(Element Region, MassingRecord Record)>();
        foreach (Element element in ElementData.ElementsOfKind(document, ElementData.KindMassing))
        {
            if (!ElementData.TryRead(element, ElementData.KindMassing, out MassingRecord record))
            {
                continue;
            }

            Species? species = lookup(record.SpeciesId);
            if (species is null)
            {
                continue;
            }

            string code = SpeciesFamilyService.ProjectCodeOf(project, species);
            if (record.SpeciesCode != code || record.PopularName != species.PopularName
                || record.ScientificName != species.ScientificName || record.SupplyUnit != species.SupplyUnit)
            {
                record.SpeciesCode = code;
                record.PopularName = species.PopularName;
                record.ScientificName = species.ScientificName;
                record.SupplyUnit = species.SupplyUnit;
                changes.Add((element, record));
            }
        }

        if (changes.Count > 0)
        {
            Tx.Run(document, "Viridian: atualizar maciços", () =>
            {
                foreach ((Element region, MassingRecord record) in changes)
                {
                    ElementData.Write(region, ElementData.KindMassing, record);
                }
            });
        }

        return changes.Count;
    }

    /// <summary>Desvincula exemplares que apontam para maciços inexistentes. Retorna a quantidade alterada.</summary>
    public static int DetachOrphanMembers(Document document)
    {
        var ids = new HashSet<string>(
            ReadMassings(document).Select(m => m.Record.Id.Trim()).Where(id => id.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        using var collector = new FilteredElementCollector(document);
        List<FamilyInstance> orphans = collector
            .OfCategory(BuiltInCategory.OST_Planting)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(i =>
            {
                string id = VirParams.GetString(i, VirParams.MassingId).Trim();
                return id.Length > 0 && !ids.Contains(id);
            })
            .ToList();
        if (orphans.Count == 0)
        {
            return 0;
        }

        Tx.Run(document, "Viridian: desvincular exemplares", () =>
        {
            foreach (FamilyInstance instance in orphans)
            {
                VirParams.Set(instance, VirParams.MassingId, string.Empty);
            }
        });
        return orphans.Count;
    }

    /// <summary>Lê o registro de maciço de uma região, se houver.</summary>
    public static MassingRecord? RecordOf(Element region) =>
        ElementData.TryRead(region, ElementData.KindMassing, out MassingRecord record) ? record : null;

    /// <summary>Área líquida da região em m²: a calculada pelo Revit, ou a do polígono quando indisponível.</summary>
    public static double AreaOf(FilledRegion region, Polygon2 polygon)
    {
        Parameter? parameter = region.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
        if (parameter is not null && parameter.HasValue && parameter.StorageType == StorageType.Double)
        {
            double area = RevitUnits.SquareFeetToSquareMeters(parameter.AsDouble());
            if (area > 0)
            {
                return area;
            }
        }

        return polygon.Area;
    }

    private static HatchPattern? RegionHatch(MassingExecution execution) =>
        execution.Record.Mode == MassingMode.Simbolos ? null : execution.Hatch;

    private MassingOutcome Apply(Document document, View view, FilledRegion region, Polygon2 polygon, MassingExecution execution)
    {
        MassingRecord record = execution.Record;
        Species species = execution.Species;
        if (string.IsNullOrWhiteSpace(record.Id))
        {
            record.Id = NextId(document);
        }

        document.Regenerate();
        record.AreaM2 = AreaOf(region, polygon);
        record.SpeciesId = species.Id;
        record.SpeciesCode = execution.ProjectSpeciesCode;
        record.PopularName = species.PopularName;
        record.ScientificName = species.ScientificName;
        record.SupplyUnit = species.SupplyUnit;
        record.HatchPatternKey = execution.Hatch?.Key ?? string.Empty;

        RemoveSymbols(document, record.Id);

        bool areaBased = SupplyUnits.IsAreaBased(species.SupplyUnit);
        int placed = 0;
        if (record.Mode != MassingMode.Hachura && !areaBased)
        {
            placed = PlaceSymbols(document, view, polygon, execution);
        }

        if (areaBased)
        {
            record.Quantity = (int)Math.Min(int.MaxValue, Math.Ceiling(record.AreaM2 - 1e-9));
        }
        else if (record.Mode != MassingMode.Hachura)
        {
            record.Quantity = placed;
        }
        else
        {
            record.Quantity = MassingCalculator.Quantity(record.AreaM2, MassingCalculator.EffectiveDensity(record));
        }

        ElementData.Write(region, ElementData.KindMassing, record);
        return new MassingOutcome(region, record, placed);
    }

    private int PlaceSymbols(Document document, View view, Polygon2 polygon, MassingExecution execution)
    {
        MassingRecord record = execution.Record;
        var request = new DistributionRequest
        {
            Pattern = record.Pattern,
            SpacingM = record.SpacingM,
            DensityPerM2 = record.DensityPerM2,
            EdgeMarginM = record.EdgeMarginM,
            JitterPercent = record.JitterPercent,
            AngleDeg = record.AngleDeg,
            MaxCount = record.MaxCount,
            Seed = record.Seed,
        };

        if (request.Pattern is DistributionPattern.Linear or DistributionPattern.Manual)
        {
            request.Pattern = DistributionPattern.Triangular;
        }

        if (request.Pattern != DistributionPattern.Densidade && !(request.SpacingM > 0) && request.DensityPerM2 > 0)
        {
            request.SpacingM = MassingCalculator.SpacingFromDensity(request.DensityPerM2, request.Pattern);
        }

        DistributionResult result = PlantDistributor.Distribute(polygon, request);
        if (result.Exceeded)
        {
            throw new ViridianException(result.Message + "\nUse o modo Hachura para maciços muito densos.");
        }

        if (result.Points.Count == 0)
        {
            return 0;
        }

        IReadOnlyList<double>? rotations = record.RandomRotation ? PlantDistributor.RandomRotations(result.Points.Count, record.Seed) : null;

        if (record.SymbolKind == MassingSymbolKind.DetailItem)
        {
            FamilySymbol detail = execution.DetailSymbol
                ?? throw new ViridianException("O símbolo de detalhe personalizado não foi carregado.");
            return PlaceDetailSymbols(document, view, detail, result.Points, rotations, record.Id);
        }

        Level level = PlantingService.LevelOf(view)
            ?? throw new ViridianException("Os símbolos de maciço só podem ser inseridos em vistas em planta.");
        var options = new PlacementOptions
        {
            MassingId = record.Id,
            Stage = PlantingService.DefaultStage(execution.Species),
            Seed = record.Seed,
        };
        return _planting.Place(document, level, execution.PlantingSymbols, execution.Species, execution.ProjectSpeciesCode, result.Points, rotations, options).Count;
    }

    private static int PlaceDetailSymbols(Document document, View view, FamilySymbol symbol, IReadOnlyList<Point2> points, IReadOnlyList<double>? rotations, string massingId)
    {
        if (!symbol.IsActive)
        {
            symbol.Activate();
            document.Regenerate();
        }

        double z = PlantingService.LevelOf(view)?.ProjectElevation ?? 0;
        var link = new MassSymbolLink { MassingId = massingId };
        int count = 0;
        for (int i = 0; i < points.Count; i++)
        {
            XYZ location = RevitUnits.ToXyz(points[i], z);
            FamilyInstance instance = document.Create.NewFamilyInstance(location, symbol, view);
            if (instance is null)
            {
                continue;
            }

            if (rotations is not null && i < rotations.Count && Math.Abs(rotations[i]) > 1e-9)
            {
                using Line axis = Line.CreateBound(location, location + view.ViewDirection);
                ElementTransformUtils.RotateElement(document, instance.Id, axis, rotations[i]);
            }

            ElementData.Write(instance, ElementData.KindMassSymbol, link);
            count++;
        }

        return count;
    }
}
