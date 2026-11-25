using Autodesk.Revit.DB;
using Viridian.Core.Colors;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Illustration;
using Viridian.Library;

namespace Viridian.Revit.Illustrated;

/// <summary>Resultado de fixar a vegetação em vistas 3D.</summary>
public sealed class FixedVegetationResult
{
    public List<string> Views { get; } = new();

    public int Plants { get; set; }

    public int Carpets { get; set; }

    public int Failed { get; set; }

    public int Materials { get; set; }
}

/// <summary>
/// Vegetação ilustrada como geometria real numa vista 3D: o desenho de frente para a câmera da vista é gravado como
/// formas diretas coloridas (materiais com cor e aparência), que saem na impressão, no PDF, na exportação de imagens,
/// no estilo Realista e na renderização — o desenho ao vivo (DirectContext3D) só aparece na tela. Cada forma fica
/// visível somente na sua vista (filtros de vista escondem as formas das outras vistas e as plantas baixas não as
/// mostram). Fixar de novo atualiza: as formas antigas da vista são apagadas. Precisa de transação.
/// </summary>
public static class FixedVegetation
{
    public const string ApplicationId = "Viridian.VegetacaoFixa";

    /// <summary>Início do comentário das formas: "VIR Vegetação fixa &lt;id da vista&gt;".</summary>
    public const string CommentPrefix = "VIR Vegetação fixa ";

    private const string MaterialPrefix = "VIR Ilustração ";
    private const string HideAllFilter = "VIR Vegetação fixa - outras vistas";
    private const string OwnFilterPrefix = "VIR Vegetação fixa - ";

    public static string TagOf(View view) => CommentPrefix + view.Id.Value;

    /// <summary>
    /// Vistas com a vegetação fixada (o desenho ao vivo não é feito nelas, para não duplicar): as que têm o filtro
    /// próprio "VIR Vegetação fixa - &lt;id&gt;" (a marca fica no filtro, sem ocupar os dados da vista).
    /// </summary>
    public static HashSet<long> FixedViewIds(Document document)
    {
        var result = new HashSet<long>();
        using var collector = new FilteredElementCollector(document);
        foreach (ParameterFilterElement filter in collector.OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>())
        {
            if (filter.Name.StartsWith(OwnFilterPrefix, StringComparison.Ordinal)
                && long.TryParse(filter.Name.AsSpan(OwnFilterPrefix.Length), out long id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    /// <summary>Vistas 3D que estão em alguma prancha.</summary>
    public static List<View3D> ViewsOnSheets(Document document)
    {
        var ids = new HashSet<ElementId>();
        using var collector = new FilteredElementCollector(document);
        foreach (ViewSheet sheet in collector.OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
        {
            foreach (ElementId id in sheet.GetAllPlacedViews())
            {
                ids.Add(id);
            }
        }

        return ids.Select(document.GetElement).OfType<View3D>().Where(v => !v.IsTemplate).ToList();
    }

    /// <summary>Fixa a vegetação na vista (substitui a fixação anterior). Precisa de transação.</summary>
    public static (int Plants, int Carpets, int Failed) Fix(Document document, View3D view, Dictionary<Rgb, ElementId> materials)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(view);
        Remove(document, view);
        if (!IllustratedVegetation.TryBuildSnapshot(document, view, out ViewSnapshot snapshot))
        {
            HideOthers(document, view);
            return (0, 0, 0);
        }

        ViewOrientation3D orientation = view.GetOrientation();
        XYZ eye = orientation.EyePosition, forward = orientation.ForwardDirection, up = orientation.UpDirection;
        bool perspective = view.IsPerspective;
        double forwardFeet = RevitUnits.ToFeet(IllustratedVegetation.ForwardM);
        ElementId category = Category(document);
        string tag = TagOf(view);
        int plants = 0, carpets = 0, failed = 0;
        foreach (SpriteInstance instance in snapshot.Instances)
        {
            SpriteFrame frame = SpriteBillboard.Place(instance.BaseFeet, instance.Rotation, instance.HeightFeet, ToVec(eye), ToVec(forward), ToVec(up), perspective, forwardFeet);
            SpriteMesh mesh = instance.Sprites.Get(instance.Stage, frame.View);
            double k = RevitUnits.ToFeet(1);
            XYZ At(SpriteVertex v)
            {
                Vec3 w = frame.ToWorld(v.X * k, v.Y * k, v.Z * k);
                return new XYZ(w.X, w.Y, w.Z);
            }

            if (Shape(document, category, tag, mesh.Vertices, mesh.Triangles, At, materials, ToXyz(instance.BaseFeet)))
            {
                plants++;
            }
            else
            {
                failed++;
            }
        }

        foreach (CarpetItem carpet in snapshot.Carpets)
        {
            double k = RevitUnits.ToFeet(1);
            XYZ At(SpriteVertex v) => new(v.X * k, v.Y * k, v.Z * k + carpet.BaseFeet);
            SpriteVertex first = carpet.Mesh.Vertices.Count > 0 ? carpet.Mesh.Vertices[0] : default;
            if (Shape(document, category, tag, carpet.Mesh.Vertices, carpet.Mesh.Triangles, At, materials, At(first)))
            {
                carpets++;
            }
            else
            {
                failed++;
            }
        }

        HideOthers(document, view);
        return (plants, carpets, failed);
    }

    /// <summary>
    /// Deixa a vista limpa para a prancha: esconde os eixos de seleção (subcategoria do volume) e as cores da planta
    /// baixa, que apareceriam como traços e discos. Precisa de transação.
    /// </summary>
    public static void CleanView(Document document, View view)
    {
        Category? planting = document.Settings.Categories.get_Item(BuiltInCategory.OST_Planting);
        if (planting is null)
        {
            return;
        }

        foreach (string name in new[] { SymbolFamilyBuilder.VolumeSubcategory, Core.Symbols.TierVisibility.SubcategoryName(Core.Symbols.SymbolTier.Fill), Core.Symbols.TierVisibility.SubcategoryName(Core.Symbols.SymbolTier.Flowers) })
        {
            try
            {
                if (planting.SubCategories.Contains(name))
                {
                    ElementId id = planting.SubCategories.get_Item(name).Id;
                    if (view.CanCategoryBeHidden(id))
                    {
                        view.SetCategoryHidden(id, true);
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                ViridianLog.Warn("Vegetação fixa: subcategoria " + name + " na vista " + view.Name, ex);
            }
        }
    }

    /// <summary>Apaga a vegetação fixada da vista e o filtro próprio (a vista volta ao desenho ao vivo). Precisa de transação.</summary>
    public static int Remove(Document document, View3D view)
    {
        string tag = TagOf(view);
        List<ElementId> ids = Shapes(document).Where(s => string.Equals(Comment(s), tag, StringComparison.Ordinal)).Select(s => s.Id).ToList();
        if (ids.Count > 0)
        {
            document.Delete(ids);
        }

        if (FindFilter(document, OwnFilterPrefix + view.Id.Value) is ParameterFilterElement own)
        {
            document.Delete(own.Id);
        }

        return ids.Count;
    }

    public static List<DirectShape> Shapes(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfClass(typeof(DirectShape)).Cast<DirectShape>()
            .Where(d => string.Equals(d.ApplicationId, ApplicationId, StringComparison.Ordinal)).ToList();
    }

    private static string Comment(Element e) => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? string.Empty;

    /// <summary>
    /// Filtros: um que esconde todas as formas fixadas (aplicado em toda vista que não é a dona delas) e, na vista
    /// dona, um que esconde só as das outras vistas. Vistas criadas depois recebem o filtro geral automaticamente.
    /// </summary>
    public static void HideOthers(Document document, View3D owner)
    {
        ElementId category = Category(document);
        ElementId comments = new(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        ParameterFilterElement all = EnsureFilter(document, HideAllFilter, category,
            new ElementParameterFilter(ParameterFilterRuleFactory.CreateBeginsWithRule(comments, CommentPrefix)));
        string tag = TagOf(owner);
        ParameterFilterElement own = EnsureFilter(document, OwnFilterPrefix + owner.Id.Value, category,
            new LogicalAndFilter(
                new ElementParameterFilter(ParameterFilterRuleFactory.CreateBeginsWithRule(comments, CommentPrefix)),
                new ElementParameterFilter(ParameterFilterRuleFactory.CreateNotEqualsRule(comments, tag))));

        Apply(owner, own.Id);
        TryRemoveFilter(owner, all.Id);
        HashSet<long> fixedViews = FixedViewIds(document);
        using var collector = new FilteredElementCollector(document);
        foreach (View view in collector.OfClass(typeof(View)).Cast<View>())
        {
            if (view.Id == owner.Id || view.IsTemplate || !view.AreGraphicsOverridesAllowed() || fixedViews.Contains(view.Id.Value))
            {
                continue;
            }

            Apply(view, all.Id);
        }
    }

    /// <summary>Aplica o filtro geral às vistas novas (chamado ao ler as vistas). Devolve quantas mudaram.</summary>
    public static int HideInNewViews(Document document, IEnumerable<View> views)
    {
        ParameterFilterElement? all = FindFilter(document, HideAllFilter);
        if (all is null)
        {
            return 0;
        }

        HashSet<long> fixedViews = FixedViewIds(document);
        int changed = 0;
        foreach (View view in views)
        {
            if (view.IsTemplate || fixedViews.Contains(view.Id.Value) || !view.AreGraphicsOverridesAllowed() || view.GetFilters().Contains(all.Id))
            {
                continue;
            }

            if (Apply(view, all.Id))
            {
                changed++;
            }
        }

        return changed;
    }

    private static bool Apply(View view, ElementId filterId)
    {
        try
        {
            if (view.ViewTemplateId != ElementId.InvalidElementId)
            {
                // Com modelo de vista que controla os filtros, a vista não aceita filtros próprios.
                if (view.Document.GetElement(view.ViewTemplateId) is View template
                    && !template.GetNonControlledTemplateParameterIds().Contains(new ElementId(BuiltInParameter.VIS_GRAPHICS_FILTERS)))
                {
                    return false;
                }
            }

            if (!view.GetFilters().Contains(filterId))
            {
                view.AddFilter(filterId);
            }

            view.SetFilterVisibility(filterId, false);
            return true;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Vegetação fixa: filtro na vista " + view.Name, ex);
            return false;
        }
    }

    private static void TryRemoveFilter(View view, ElementId filterId)
    {
        try
        {
            if (view.GetFilters().Contains(filterId))
            {
                view.RemoveFilter(filterId);
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Vegetação fixa: filtro na vista " + view.Name, ex);
        }
    }

    private static ParameterFilterElement? FindFilter(Document document, string name)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>().FirstOrDefault(f => f.Name == name);
    }

    private static ParameterFilterElement EnsureFilter(Document document, string name, ElementId category, ElementFilter filter)
    {
        ParameterFilterElement? existing = FindFilter(document, name);
        if (existing is not null)
        {
            existing.SetElementFilter(filter);
            return existing;
        }

        return ParameterFilterElement.Create(document, name, new List<ElementId> { category }, filter);
    }

    private static ElementId Category(Document document)
    {
        var entourage = new ElementId(BuiltInCategory.OST_Entourage);
        return DirectShape.IsValidCategoryId(entourage, document) ? entourage : new ElementId(BuiltInCategory.OST_GenericModel);
    }

    private static bool Shape(Document document, ElementId category, string tag, IReadOnlyList<SpriteVertex> vertices, IReadOnlyList<int> triangles,
        Func<SpriteVertex, XYZ> at, Dictionary<Rgb, ElementId> materials, XYZ anchor)
    {
        if (triangles.Count < 3)
        {
            return false;
        }

        var builder = new TessellatedShapeBuilder { Target = TessellatedShapeBuilderTarget.AnyGeometry, Fallback = TessellatedShapeBuilderFallback.Mesh };
        builder.OpenConnectedFaceSet(false);
        double tolerance = document.Application.ShortCurveTolerance;
        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            SpriteVertex va = vertices[triangles[i]], vb = vertices[triangles[i + 1]], vc = vertices[triangles[i + 2]];
            XYZ a = at(va), b = at(vb), c = at(vc);
            if (a.DistanceTo(b) < tolerance || b.DistanceTo(c) < tolerance || c.DistanceTo(a) < tolerance || (b - a).CrossProduct(c - a).GetLength() < tolerance * tolerance)
            {
                continue;
            }

            builder.AddFace(new TessellatedFace(new List<XYZ> { a, b, c }, MaterialOf(document, Quantize(va.Color), materials)));
        }

        builder.CloseConnectedFaceSet();
        builder.Build();
        using TessellatedShapeBuilderResult built = builder.GetBuildResult();
        if (built.Outcome == TessellatedShapeBuilderOutcome.Nothing || !built.AreObjectsAvailable)
        {
            return false;
        }

        DirectShape shape = DirectShape.CreateElement(document, category);
        shape.ApplicationId = ApplicationId;
        shape.ApplicationDataId = tag;
        shape.SetShape(built.GetGeometricalObjects());
        try
        {
            var plan = new List<GeometryObject> { Point.Create(anchor) };
            if (shape.IsValidShape(plan, DirectShapeTargetViewType.Plan))
            {
                shape.SetShape(plan, DirectShapeTargetViewType.Plan);
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Vegetação fixa: representação em planta", ex);
        }

        shape.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(tag);
        return true;
    }

    /// <summary>Cores arredondadas (passo de 8): a paleta das imagens vira algumas centenas de materiais, não milhares.</summary>
    private static Rgb Quantize(Rgb c)
    {
        static byte Q(byte v) => (byte)Math.Min(255, (v + 4) / 8 * 8);
        return new Rgb(Q(c.R), Q(c.G), Q(c.B));
    }

    /// <summary>Material da cor: cor de sombreamento e aparência com a mesma cor (para o Realista e a renderização).</summary>
    private static ElementId MaterialOf(Document document, Rgb color, Dictionary<Rgb, ElementId> materials)
    {
        if (materials.TryGetValue(color, out ElementId? id))
        {
            return id;
        }

        string name = MaterialPrefix + color.Hex;
        using (var collector = new FilteredElementCollector(document))
        {
            id = collector.OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => m.Name == name)?.Id;
        }

        if (id is null)
        {
            id = Material.Create(document, name);
            if (document.GetElement(id) is Material material)
            {
                material.Color = new Color(color.R, color.G, color.B);
                material.Shininess = 0;
                material.Smoothness = 0;
                material.UseRenderAppearanceForShading = false;
                AppearanceTextures.SetPlainColor(document, material, color);
            }
        }

        materials[color] = id;
        return id;
    }

    private static Vec3 ToVec(XYZ p) => new(p.X, p.Y, p.Z);

    private static XYZ ToXyz(Vec3 v) => new(v.X, v.Y, v.Z);
}
