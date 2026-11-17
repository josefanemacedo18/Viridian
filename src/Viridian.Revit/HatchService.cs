using Autodesk.Revit.DB;
using Viridian.Core.Config;
using Viridian.Core.Hatch;

namespace Viridian.Revit;

/// <summary>Cria no projeto os padrões de preenchimento e os tipos de região preenchida do Viridian.</summary>
public static class HatchService
{
    public const string EmptyRegionTypeName = "VIR Maciço sem hachura";

    /// <summary>Garante o padrão de preenchimento no projeto. Precisa de transação aberta.</summary>
    public static ElementId EnsureFillPattern(Document document, HatchPattern pattern)
    {
        if (!pattern.IsValid(out string problem))
        {
            throw new ViridianException($"A hachura \"{pattern.Name}\" é inválida: {problem}");
        }

        FillPatternTarget target = pattern.Target == HatchTarget.Model ? FillPatternTarget.Model : FillPatternTarget.Drafting;
        FillPatternElement? existing = FillPatternElement.GetFillPatternElementByName(document, target, pattern.Name);
        if (existing is not null)
        {
            return existing.Id;
        }

        var fillPattern = new FillPattern(pattern.Name, target, FillPatternHostOrientation.ToView);
        var grids = new List<FillGrid>(pattern.Grids.Count);
        foreach (HatchGrid grid in pattern.Grids)
        {
            var fillGrid = new FillGrid
            {
                Angle = grid.AngleDeg * Math.PI / 180.0,
                Origin = new UV(RevitUnits.MmToFeet(grid.OriginX), RevitUnits.MmToFeet(grid.OriginY)),
                Shift = RevitUnits.MmToFeet(grid.Shift),
                Offset = RevitUnits.MmToFeet(grid.Offset),
            };
            if (grid.Segments.Count > 0)
            {
                fillGrid.SetSegments(grid.Segments.Select(RevitUnits.MmToFeet).ToList());
            }

            grids.Add(fillGrid);
        }

        fillPattern.SetFillGrids(grids);
        try
        {
            return FillPatternElement.Create(document, fillPattern).Id;
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException ex)
        {
            throw new ViridianException($"O Revit recusou a hachura \"{pattern.Name}\": {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Garante o tipo de região preenchida correspondente à hachura (ou sem hachura, quando nula).
    /// Precisa de transação aberta.
    /// </summary>
    public static FilledRegionType EnsureRegionType(Document document, HatchPattern? pattern)
    {
        string name = pattern?.Name ?? EmptyRegionTypeName;
        using var collector = new FilteredElementCollector(document);
        List<FilledRegionType> types = collector.OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
        FilledRegionType? existing = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        FilledRegionType template = types.FirstOrDefault()
            ?? throw new ViridianException("O projeto não possui nenhum tipo de região preenchida para servir de base.");
        if (template.Duplicate(name) is not FilledRegionType created)
        {
            throw new ViridianException("Não foi possível criar o tipo de região preenchida \"" + name + "\".");
        }

        created.IsMasking = false;
        created.BackgroundPatternId = ElementId.InvalidElementId;
        created.ForegroundPatternId = pattern is null ? ElementId.InvalidElementId : EnsureFillPattern(document, pattern);
        created.ForegroundPatternColor = new Color(0, 0, 0);
        created.LineWeight = 1;
        return created;
    }

    /// <summary>Todas as hachuras disponíveis: as do Viridian e as customizadas do escritório.</summary>
    public static IReadOnlyList<HatchPattern> Available(ViridianSettings settings) =>
        HatchLibrary.BuiltIn.Concat(settings.CustomHatches).ToList();
}
