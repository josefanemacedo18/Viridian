using Autodesk.Revit.DB;
using Viridian.Core.Colors;
using Viridian.Core.Geometry;
using Viridian.Core.Symbols;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>
/// Desenha, dentro da família de um símbolo, a copa colorida e as manchas de floração como regiões
/// preenchidas no plano do nível. Tudo aqui é complementar: se o modelo de família ou a versão do Revit
/// recusar alguma etapa, o símbolo continua sendo gerado só com as linhas.
/// </summary>
internal static class FamilyFills
{
    private const string CrownTypeName = "VIR Copa";
    private const string FlowerTypeName = "VIR Floração";
    private const string HatchPatternName = "VIR Reticulado da copa";

    /// <summary>Espaçamento do reticulado no papel: 1,2 mm.</summary>
    private const double HatchSpacingFeet = 1.2 / 304.8;

    public static void Draw(
        Document familyDocument,
        PlantSymbol symbol,
        SymbolPaint paint,
        SketchPlane sketchPlane,
        double elevation,
        double tolerance,
        IReadOnlyDictionary<SymbolTier, Category> subcategories)
    {
        try
        {
            // Nunca copiar tipos do projeto: o Revit não permite cópia entre projeto e família.
            FilledRegionType? baseType = FirstRegionType(familyDocument) ?? BootstrapRegionType(familyDocument, sketchPlane, symbol, elevation, tolerance);
            if (baseType is null)
            {
                ViridianLog.Warn("O modelo de família não possui tipo de região preenchida; o símbolo será gerado sem cores.");
                return;
            }

            FillPatternElement? solid = SolidPattern(familyDocument);
            ElementId? boundaryStyle = HatchService.InvisibleLineStyle(familyDocument);

            if (paint.Mode != CrownFillMode.Nenhum && symbol.FillOutline.Count >= 3)
            {
                FilledRegionType? crownType = paint.Mode == CrownFillMode.Solido
                    ? SolidType(familyDocument, baseType, CrownTypeName, solid, paint.Crown)
                    : HatchType(familyDocument, baseType, paint.Crown);
                CurveLoop? loop = PolygonLoop(symbol.FillOutline, elevation, tolerance);
                if (crownType is not null && loop is not null)
                {
                    CreateRegion(familyDocument, crownType, sketchPlane, new List<CurveLoop> { loop }, boundaryStyle, subcategories, SymbolTier.Fill);
                }
            }

            if (paint.Flower is Rgb flower && symbol.FlowerSpots.Count > 0)
            {
                FilledRegionType? flowerType = SolidType(familyDocument, baseType, FlowerTypeName, solid, flower);
                if (flowerType is not null)
                {
                    var loops = symbol.FlowerSpots
                        .Select(spot => CircleLoop(spot, elevation, tolerance))
                        .OfType<CurveLoop>()
                        .ToList();
                    if (loops.Count > 0
                        && CreateRegion(familyDocument, flowerType, sketchPlane, loops, boundaryStyle, subcategories, SymbolTier.Flowers) is null)
                    {
                        // Algum contorno foi recusado: tenta mancha por mancha.
                        foreach (CurveLoop single in loops)
                        {
                            CreateRegion(familyDocument, flowerType, sketchPlane, new List<CurveLoop> { single }, boundaryStyle, subcategories, SymbolTier.Flowers);
                        }
                    }
                }
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Não foi possível colorir o símbolo; ele será gerado só com as linhas.", ex);
        }
    }

    private static FilledRegion? CreateRegion(
        Document document,
        FilledRegionType type,
        SketchPlane sketchPlane,
        IList<CurveLoop> loops,
        ElementId? boundaryStyle,
        IReadOnlyDictionary<SymbolTier, Category> subcategories,
        SymbolTier tier)
    {
        FilledRegion region;
        try
        {
            region = FilledRegion.Create(document, type.Id, sketchPlane, loops);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Região colorida recusada pelo Revit.", ex);
            return null;
        }

        if (boundaryStyle is not null)
        {
            try
            {
                region.SetLineStyleId(boundaryStyle);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Mantém a linha fina padrão.
            }
        }

        // A subcategoria permite ao gerenciador de escalas esconder as cores nas escalas pequenas.
        if (subcategories.TryGetValue(tier, out Category? subcategory))
        {
            try
            {
                Parameter? parameter = region.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
                if (parameter is { IsReadOnly: false, StorageType: StorageType.ElementId })
                {
                    parameter.Set(subcategory.Id);
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Versões que não aceitam subcategoria em regiões: a cor fica sempre visível.
            }
        }

        return region;
    }

    private static FilledRegionType? FirstRegionType(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().FirstOrDefault();
    }

    /// <summary>
    /// Modelos de família sem nenhum tipo de região preenchida: uma região de máscara temporária faz o Revit
    /// criar o tipo padrão, que é então usado como base e a região temporária é apagada.
    /// </summary>
    private static FilledRegionType? BootstrapRegionType(Document document, SketchPlane sketchPlane, PlantSymbol symbol, double elevation, double tolerance)
    {
        CurveLoop? loop = PolygonLoop(symbol.FillOutline, elevation, tolerance);
        if (loop is null)
        {
            return null;
        }

        try
        {
            FilledRegion temporary = FilledRegion.CreateMaskingRegion(document, sketchPlane, new List<CurveLoop> { loop });
            var type = document.GetElement(temporary.GetTypeId()) as FilledRegionType;
            document.Delete(temporary.Id);
            return type;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("O modelo de família não permite regiões preenchidas.", ex);
            return null;
        }
    }

    private static FilledRegionType Named(Document document, FilledRegionType baseType, string name)
    {
        using var collector = new FilteredElementCollector(document);
        FilledRegionType? existing = collector.OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>()
            .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        return existing ?? (FilledRegionType)baseType.Duplicate(name);
    }

    private static FilledRegionType? SolidType(Document document, FilledRegionType baseType, string name, FillPatternElement? solid, Rgb color)
    {
        if (solid is null)
        {
            return null;
        }

        FilledRegionType type = Named(document, baseType, name);

        // Na família, padrão sólido exige região de máscara.
        if (type.IsValidMasking(true))
        {
            type.IsMasking = true;
        }

        if (!type.IsValidForegroundPatternId(solid.Id) || !type.IsValidSolidFillPatternId(solid.Id))
        {
            return null;
        }

        type.BackgroundPatternId = ElementId.InvalidElementId;
        type.ForegroundPatternId = solid.Id;
        type.ForegroundPatternColor = new Color(color.R, color.G, color.B);
        if (FilledRegionType.IsValidLineWeight(1))
        {
            type.LineWeight = 1;
        }

        return type;
    }

    private static FilledRegionType? HatchType(Document document, FilledRegionType baseType, Rgb color)
    {
        FillPatternElement? hatch = HatchPattern(document);
        if (hatch is null)
        {
            return null;
        }

        FilledRegionType type = Named(document, baseType, CrownTypeName);
        type.BackgroundPatternId = ElementId.InvalidElementId;

        // Sem máscara, o que está sob a copa (pisos, divisas) continua visível através do reticulado.
        if (type.IsValidMasking(false))
        {
            type.IsMasking = false;
        }

        if (!type.IsValidForegroundPatternId(hatch.Id))
        {
            return null;
        }

        type.ForegroundPatternId = hatch.Id;
        type.ForegroundPatternColor = new Color(color.R, color.G, color.B);
        return type;
    }

    /// <summary>Padrão sólido; nas famílias o Revit exige padrão de desenho (não de modelo).</summary>
    private static FillPatternElement? SolidPattern(Document document)
    {
        ElementId id = HatchService.SolidDraftingPattern(document);
        return id == ElementId.InvalidElementId ? null : document.GetElement(id) as FillPatternElement;
    }

    private static FillPatternElement? HatchPattern(Document document)
    {
        FillPatternElement? existing = FillPatternElement.GetFillPatternElementByName(document, FillPatternTarget.Drafting, HatchPatternName);
        if (existing is not null)
        {
            return existing;
        }

        // Linhas cruzadas a 45°, orientadas pela vista, como no desenho à mão.
        var pattern = new FillPattern(HatchPatternName, FillPatternTarget.Drafting, FillPatternHostOrientation.ToView, Math.PI / 4, HatchSpacingFeet, HatchSpacingFeet);
        return FillPatternElement.Create(document, pattern);
    }

    private static CurveLoop? PolygonLoop(IReadOnlyList<Point2> outline, double z, double tolerance)
    {
        var points = new List<XYZ>();
        foreach (Point2 p in outline)
        {
            XYZ xyz = RevitUnits.ToXyz(p, z);
            if (points.Count == 0 || points[^1].DistanceTo(xyz) >= tolerance)
            {
                points.Add(xyz);
            }
        }

        while (points.Count > 2 && points[^1].DistanceTo(points[0]) < tolerance)
        {
            points.RemoveAt(points.Count - 1);
        }

        if (points.Count < 3)
        {
            return null;
        }

        try
        {
            var curves = new List<Curve>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                curves.Add(Line.CreateBound(points[i], points[(i + 1) % points.Count]));
            }

            return CurveLoop.Create(curves);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }

    private static CurveLoop? CircleLoop(FlowerSpot spot, double z, double tolerance)
    {
        double radius = RevitUnits.ToFeet(spot.Radius);
        if (radius * Math.PI < tolerance * 2)
        {
            return null;
        }

        try
        {
            XYZ center = RevitUnits.ToXyz(spot.Center, z);
            var loop = new CurveLoop();
            loop.Append(Arc.Create(center, radius, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
            loop.Append(Arc.Create(center, radius, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
            return loop;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }
}
