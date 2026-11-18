using System.IO;
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
    internal const string CrownTypeName = "VIR Copa";
    internal const string FlowerTypeName = "VIR Floração";
    private const string HatchPatternName = "VIR Reticulado da copa";

    /// <summary>Espaçamento do reticulado no papel: 1,2 mm.</summary>
    private const double HatchSpacingFeet = 1.2 / 304.8;

    /// <summary>
    /// Nome do tipo de região pela cor. No projeto, tipos de mesmo nome vindos de famílias diferentes viram um só (o
    /// primeiro carregado): com a cor no nome, cada cor fica com o seu tipo e nenhuma espécie herda a cor de outra.
    /// </summary>
    public static string ColorTypeName(string prefix, Rgb color) => prefix + " " + color.Hex.TrimStart('#');

    /// <summary>Quantidade de regiões preenchidas no documento (para conferir se as cores foram de fato criadas).</summary>
    public static int CountRegions(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfClass(typeof(FilledRegion)).GetElementCount();
    }

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

            ElementId? boundaryStyle = HatchService.InvisibleLineStyle(familyDocument);

            if (paint.Mode != CrownFillMode.Nenhum && symbol.FillOutline.Count >= 3)
            {
                FilledRegionType? crownType = paint.Mode == CrownFillMode.Solido
                    ? SolidType(familyDocument, baseType, ColorTypeName(CrownTypeName, paint.Crown), paint.Crown)
                    : HatchType(familyDocument, baseType, paint.Crown);
                CurveLoop? loop = PolygonLoop(symbol.FillOutline, elevation, tolerance);
                if (crownType is not null && loop is not null)
                {
                    CreateRegion(familyDocument, crownType, sketchPlane, new List<CurveLoop> { loop }, boundaryStyle, subcategories, SymbolTier.Fill);
                }
            }

            if (paint.Flower is Rgb flower && symbol.FlowerSpots.Count > 0)
            {
                FilledRegionType? flowerType = SolidType(familyDocument, baseType, ColorTypeName(FlowerTypeName, flower), flower);
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

    internal static FilledRegionType? FirstRegionType(Document document)
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

    /// <summary>
    /// Tipo de região sólido na cor informada. A documentação do Revit é ambígua sobre qual padrão sólido
    /// (de desenho ou de modelo) e qual máscara são aceitos dentro de famílias; por isso cada combinação é
    /// tentada de verdade, na ordem mais provável, e a primeira aceita é usada.
    /// </summary>
    internal static FilledRegionType? SolidType(Document document, FilledRegionType baseType, string name, Rgb color)
    {
        List<FillPatternElement> solids = SolidPatterns(document);
        if (solids.Count == 0)
        {
            ViridianLog.Warn("Cores do símbolo: o modelo de família não tem padrão de preenchimento sólido.");
            return null;
        }

        FilledRegionType type = Named(document, baseType, name);
        foreach (bool masking in new[] { false, true })
        {
            foreach (FillPatternElement solid in solids)
            {
                try
                {
                    type.BackgroundPatternId = ElementId.InvalidElementId;
                    TrySet(() => type.IsMasking = masking);
                    type.ForegroundPatternId = solid.Id;
                    type.ForegroundPatternColor = new Color(color.R, color.G, color.B);
                    TrySet(() => type.LineWeight = 1);
                    ViridianLog.Info($"Cores do símbolo: tipo \"{name}\" com padrão \"{solid.Name}\" ({solid.GetFillPattern().Target}), máscara {type.IsMasking}.");
                    return type;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    ViridianLog.Info($"Cores do símbolo: padrão \"{solid.Name}\" ({solid.GetFillPattern().Target}) com máscara {masking} recusado: {ex.Message}");
                }
            }
        }

        return null;
    }

    internal static FilledRegionType? HatchType(Document document, FilledRegionType baseType, Rgb color)
    {
        FillPatternElement? hatch = HatchPattern(document);
        if (hatch is null)
        {
            return null;
        }

        FilledRegionType type = Named(document, baseType, ColorTypeName(CrownTypeName + " reticulado", color));
        foreach (bool masking in new[] { false, true })
        {
            try
            {
                type.BackgroundPatternId = ElementId.InvalidElementId;
                TrySet(() => type.IsMasking = masking);
                type.ForegroundPatternId = hatch.Id;
                type.ForegroundPatternColor = new Color(color.R, color.G, color.B);
                return type;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                ViridianLog.Info("Cores do símbolo: reticulado recusado: " + ex.Message);
            }
        }

        return null;
    }

    private static void TrySet(Action set)
    {
        try
        {
            set();
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Propriedade não aceita neste contexto: segue com o valor atual.
        }
    }

    /// <summary>Padrões sólidos do documento: primeiro os de desenho, depois os de modelo.</summary>
    private static List<FillPatternElement> SolidPatterns(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .Where(p => p.GetFillPattern() is { IsSolidFill: true })
            .OrderBy(p => p.GetFillPattern().Target == FillPatternTarget.Drafting ? 0 : 1)
            .ToList();
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

    internal static CurveLoop? PolygonLoop(IReadOnlyList<Point2> outline, double z, double tolerance)
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

    internal static CurveLoop? CircleLoop(FlowerSpot spot, double z, double tolerance)
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

/// <summary>
/// Cores do símbolo como componente de detalhe 2D aninhado na família de vegetação: a técnica consagrada para
/// símbolos de planta coloridos em famílias de modelo. O componente tem regiões preenchidas comuns (de vista), que o
/// Revit mostra nas plantas em qualquer versão; ele é colocado na vista em planta da família.
/// </summary>
internal static class NestedColorComponent
{
    public static bool TryDraw(
        Autodesk.Revit.ApplicationServices.Application app,
        Document familyDocument,
        PlantSymbol symbol,
        SymbolPaint paint,
        double elevation,
        double tolerance)
    {
        IReadOnlyList<string> templates = FamilyTemplateLocator.DetailCandidates(app);
        if (templates.Count == 0)
        {
            ViridianLog.Warn("Cores do símbolo: nenhum modelo de componente de detalhe encontrado nas pastas de modelos do Revit.");
            return false;
        }

        string folder = Path.Combine(Path.GetTempPath(), "Viridian", "Cores");
        Directory.CreateDirectory(folder);
        string name = "VIR Cores " + Signature(symbol, paint);
        string file = Path.Combine(folder, name + ".rfa");
        foreach (string template in templates.Take(4))
        {
            try
            {
                if (!BuildDetailFamily(app, template, symbol, paint, tolerance, file))
                {
                    continue;
                }

                if (Nest(familyDocument, file, elevation))
                {
                    ViridianLog.Info($"Cores do símbolo: componente de detalhe aninhado ({Path.GetFileName(template)}).");
                    return true;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // As cores são opcionais: qualquer recusa aqui leva ao método alternativo, nunca interrompe o símbolo.
                ViridianLog.Warn("Cores do símbolo: componente de detalhe com o modelo " + template, ex);
            }
            finally
            {
                TryDelete(file);
            }
        }

        return false;
    }

    private static bool BuildDetailFamily(Autodesk.Revit.ApplicationServices.Application app, string template, PlantSymbol symbol, SymbolPaint paint, double tolerance, string file)
    {
        Document detail = app.NewFamilyDocument(template);
        if (detail is null)
        {
            return false;
        }

        try
        {
            View? view = PlanView(detail);
            if (view is null)
            {
                ViridianLog.Warn("Cores do símbolo: o modelo de detalhe não tem vista para desenho: " + template);
                return false;
            }

            int created = 0;
            using (var transaction = new Transaction(detail, "Viridian: cores"))
            {
                FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
                options.SetForcedModalHandling(false);
                options.SetClearAfterRollback(true);
                transaction.SetFailureHandlingOptions(options);
                transaction.Start();
                FilledRegionType? baseType = FamilyFills.FirstRegionType(detail) ?? Bootstrap(detail, view, symbol, tolerance);
                if (baseType is null)
                {
                    transaction.RollBack();
                    return false;
                }

                ElementId? invisible = HatchService.InvisibleLineStyle(detail);
                if (paint.Mode != CrownFillMode.Nenhum && symbol.FillOutline.Count >= 3)
                {
                    FilledRegionType? crown = paint.Mode == CrownFillMode.Solido
                        ? FamilyFills.SolidType(detail, baseType, FamilyFills.ColorTypeName(FamilyFills.CrownTypeName, paint.Crown), paint.Crown)
                        : FamilyFills.HatchType(detail, baseType, paint.Crown);
                    CurveLoop? loop = FamilyFills.PolygonLoop(symbol.FillOutline, 0, tolerance);
                    if (crown is not null && loop is not null)
                    {
                        created += Region(detail, crown, view, new List<CurveLoop> { loop }, invisible) ? 1 : 0;
                    }
                }

                if (paint.Flower is Rgb flower && symbol.FlowerSpots.Count > 0)
                {
                    FilledRegionType? flowerType = FamilyFills.SolidType(detail, baseType, FamilyFills.ColorTypeName(FamilyFills.FlowerTypeName, flower), flower);
                    if (flowerType is not null)
                    {
                        foreach (FlowerSpot spot in symbol.FlowerSpots)
                        {
                            if (FamilyFills.CircleLoop(spot, 0, tolerance) is CurveLoop loop)
                            {
                                created += Region(detail, flowerType, view, new List<CurveLoop> { loop }, invisible) ? 1 : 0;
                            }
                        }
                    }
                }

                if (created == 0 || transaction.Commit() != TransactionStatus.Committed)
                {
                    if (transaction.HasStarted() && !transaction.HasEnded())
                    {
                        transaction.RollBack();
                    }

                    return false;
                }
            }

            using var save = new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 };
            detail.SaveAs(file, save);
            return File.Exists(file);
        }
        finally
        {
            try
            {
                detail.Close(false);
            }
            catch (Exception ex)
            {
                ViridianLog.Warn("Falha ao fechar o componente de detalhe temporário.", ex);
            }
        }
    }

    /// <summary>Carrega o componente na família de vegetação e o coloca na vista em planta, no ponto de inserção.</summary>
    private static bool Nest(Document familyDocument, string file, double elevation)
    {
        ViewPlan? plan;
        using (var collector = new FilteredElementCollector(familyDocument))
        {
            plan = collector.OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => !v.IsTemplate);
        }

        if (plan is null)
        {
            ViridianLog.Warn("Cores do símbolo: a família de vegetação não tem vista em planta.");
            return false;
        }

        using var transaction = new Transaction(familyDocument, "Viridian: cores aninhadas");
        FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
        options.SetForcedModalHandling(false);
        options.SetClearAfterRollback(true);
        transaction.SetFailureHandlingOptions(options);
        transaction.Start();
        try
        {
            if (!familyDocument.LoadFamily(file, out Family family) || family is null)
            {
                transaction.RollBack();
                return false;
            }

            ElementId? symbolId = family.GetFamilySymbolIds().FirstOrDefault();
            if (symbolId is null || familyDocument.GetElement(symbolId) is not FamilySymbol detailSymbol)
            {
                transaction.RollBack();
                return false;
            }

            if (!detailSymbol.IsActive)
            {
                detailSymbol.Activate();
                familyDocument.Regenerate();
            }

            FamilyInstance? instance = familyDocument.FamilyCreate.NewFamilyInstance(new XYZ(0, 0, elevation), detailSymbol, plan);
            if (instance is null)
            {
                transaction.RollBack();
                return false;
            }

            return transaction.Commit() == TransactionStatus.Committed;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            if (transaction.HasStarted() && !transaction.HasEnded())
            {
                transaction.RollBack();
            }

            ViridianLog.Warn("Cores do símbolo: o componente de detalhe não pôde ser aninhado.", ex);
            return false;
        }
    }

    private static View? PlanView(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        List<View> views = collector.OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).ToList();
        return views.FirstOrDefault(v => v.ViewType is ViewType.FloorPlan or ViewType.DraftingView or ViewType.Detail)
               ?? views.FirstOrDefault(v => v.ViewType != ViewType.ThreeD && v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.SystemBrowser);
    }

    private static FilledRegionType? Bootstrap(Document document, View view, PlantSymbol symbol, double tolerance)
    {
        CurveLoop? loop = FamilyFills.PolygonLoop(symbol.FillOutline, 0, tolerance);
        if (loop is null)
        {
            return null;
        }

        try
        {
            FilledRegion temporary = FilledRegion.CreateMaskingRegion(document, view.Id, new List<CurveLoop> { loop });
            var type = document.GetElement(temporary.GetTypeId()) as FilledRegionType;
            document.Delete(temporary.Id);
            return type;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Cores do símbolo: o modelo de detalhe não permite regiões preenchidas.", ex);
            return null;
        }
    }

    private static bool Region(Document document, FilledRegionType type, View view, IList<CurveLoop> loops, ElementId? invisible)
    {
        try
        {
            FilledRegion region = FilledRegion.Create(document, type.Id, view.Id, loops);
            if (invisible is not null)
            {
                try
                {
                    region.SetLineStyleId(invisible);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // fica a linha fina padrão
                }
            }

            return true;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Cores do símbolo: região recusada no componente de detalhe.", ex);
            return false;
        }
    }

    /// <summary>Nome único por desenho e cores: famílias aninhadas de mesmo nome não se misturam no projeto.</summary>
    private static string Signature(PlantSymbol symbol, SymbolPaint paint)
    {
        var text = new System.Text.StringBuilder(paint.Signature);
        foreach (Point2 p in symbol.FillOutline)
        {
            text.Append(';').Append(p.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',').Append(p.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        foreach (FlowerSpot f in symbol.FlowerSpots)
        {
            text.Append('|').Append(f.Center.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',').Append(f.Center.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexString(hash, 0, 6);
    }

    private static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // arquivo temporário: será sobrescrito na próxima vez
        }
    }
}
