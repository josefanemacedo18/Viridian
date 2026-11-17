using Autodesk.Revit.DB;
using Viridian.Core.Colors;
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
    /// <param name="paint">Cores da hachura e do fundo; nulo para hachura preta sem fundo.</param>
    public static FilledRegionType EnsureRegionType(Document document, HatchPattern? pattern, RegionPaint? paint = null)
    {
        string name = pattern?.Name ?? EmptyRegionTypeName;
        if (paint is not null)
        {
            // Um tipo por combinação de hachura e cores: maciços de espécies diferentes não se repintam.
            name += " · " + paint.Key;
        }

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
        created.ForegroundPatternColor = paint is null ? new Color(0, 0, 0) : ToColor(paint.Pattern);
        created.LineWeight = 1;
        if (paint is not null)
        {
            ElementId solid = SolidDraftingPattern(document);
            if (solid != ElementId.InvalidElementId && created.IsValidBackgroundPatternId(solid))
            {
                created.BackgroundPatternId = solid;
                created.BackgroundPatternColor = ToColor(paint.Background);
            }
        }

        return created;
    }

    /// <summary>Tipo de região sólida em uma cor (amostras de cor da legenda). Precisa de transação aberta.</summary>
    public static FilledRegionType? EnsureSolidRegionType(Document document, Rgb color)
    {
        string name = "VIR Cor " + color.Hex.TrimStart('#');
        using var collector = new FilteredElementCollector(document);
        List<FilledRegionType> types = collector.OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
        FilledRegionType? existing = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        ElementId solid = SolidDraftingPattern(document);
        FilledRegionType? template = types.FirstOrDefault();
        if (solid == ElementId.InvalidElementId || template?.Duplicate(name) is not FilledRegionType created)
        {
            return null;
        }

        created.IsMasking = false;
        created.BackgroundPatternId = ElementId.InvalidElementId;
        created.ForegroundPatternId = solid;
        created.ForegroundPatternColor = ToColor(color);
        created.LineWeight = 1;
        return created;
    }

    /// <summary>Tipo de região com o reticulado da copa (linhas cruzadas finas) na cor informada.</summary>
    public static FilledRegionType? EnsureCrownHatchType(Document document, Rgb color)
    {
        const string patternName = "VIR Reticulado da copa";
        string name = "VIR Copa reticulada " + color.Hex.TrimStart('#');
        using var collector = new FilteredElementCollector(document);
        List<FilledRegionType> types = collector.OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
        FilledRegionType? existing = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        FillPatternElement? pattern = FillPatternElement.GetFillPatternElementByName(document, FillPatternTarget.Drafting, patternName);
        if (pattern is null)
        {
            double spacing = 1.2 / 304.8;
            pattern = FillPatternElement.Create(document, new FillPattern(patternName, FillPatternTarget.Drafting, FillPatternHostOrientation.ToView, Math.PI / 4, spacing, spacing));
        }

        FilledRegionType? template = types.FirstOrDefault();
        if (template?.Duplicate(name) is not FilledRegionType created)
        {
            return null;
        }

        created.IsMasking = false;
        created.BackgroundPatternId = ElementId.InvalidElementId;
        created.ForegroundPatternId = pattern.Id;
        created.ForegroundPatternColor = ToColor(color);
        created.LineWeight = 1;
        return created;
    }

    /// <summary>Estilo de linha invisível para o contorno de regiões, se o documento oferecer.</summary>
    public static ElementId? InvisibleLineStyle(Document document)
    {
        var invisible = new ElementId(BuiltInCategory.OST_InvisibleLines);
        foreach (ElementId id in FilledRegion.GetValidLineStyleIdsForFilledRegion(document))
        {
            if (document.GetElement(id) is GraphicsStyle style && style.GraphicsStyleCategory?.Id == invisible)
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>Padrão de preenchimento sólido (de desenho) do projeto.</summary>
    public static ElementId SolidDraftingPattern(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        FillPatternElement? solid = collector.OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(p => p.GetFillPattern() is { IsSolidFill: true, Target: FillPatternTarget.Drafting })
            ?? new FilteredElementCollector(document).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(p => p.GetFillPattern() is { IsSolidFill: true });
        return solid?.Id ?? ElementId.InvalidElementId;
    }

    public static Color ToColor(Rgb rgb) => new(rgb.R, rgb.G, rgb.B);

    /// <summary>Todas as hachuras disponíveis: as do Viridian e as customizadas do escritório.</summary>
    public static IReadOnlyList<HatchPattern> Available(ViridianSettings settings) =>
        HatchLibrary.BuiltIn.Concat(settings.CustomHatches).ToList();
}
