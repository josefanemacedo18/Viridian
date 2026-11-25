using Autodesk.Revit.DB;
using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>
/// Desenha em uma vista do Revit os desenhos gerados pelo Viridian (detalhes, cortes, crescimento, calendário):
/// áreas coloridas por regiões preenchidas, traços por linhas de detalhe com estilos próprios e textos.
/// Os desenhos vêm em metros reais; a vista define a escala de apresentação.
/// </summary>
public sealed class DrawingRenderer
{
    public const string HeavyStyleName = "VIR Traço grosso";
    public const string ThinStyleName = "VIR Traço fino";

    /// <summary>Estilos de linha por caneta: nome, espessura (1 a 16) e cor na criação.</summary>
    private static readonly Dictionary<LinePen, (string Name, int Weight, Rgb Color)> Pens = new()
    {
        [LinePen.Fina] = (ThinStyleName, 1, new Rgb(0x4A, 0x55, 0x4E)),
        [LinePen.Media] = ("VIR Traço médio", 3, new Rgb(0x34, 0x45, 0x3A)),
        [LinePen.Grossa] = (HeavyStyleName, 5, new Rgb(0x2E, 0x4A, 0x36)),
        [LinePen.Textura] = ("VIR Traço textura", 1, new Rgb(0x7E, 0x8C, 0x7C)),
        [LinePen.Cota] = ("VIR Traço cota", 1, new Rgb(0x55, 0x5B, 0x57)),
        [LinePen.Agua] = ("VIR Traço água", 3, new Rgb(0x3D, 0x7F, 0xB8)),
    };

    private readonly Document _document;
    private readonly View _view;
    private readonly double _textMm;
    private readonly double _shortCurve;
    private readonly Dictionary<LinePen, GraphicsStyle?> _styles = new();
    private bool _stylesReady;
    private readonly Dictionary<(int Size, TextStyle Style), ElementId> _textTypes = new();

    /// <param name="textMm">Altura do texto no papel, em milímetros.</param>
    public DrawingRenderer(Document document, View view, double textMm = 2.0)
    {
        _document = document;
        _view = view;
        _textMm = Math.Clamp(textMm, 1.0, 10.0);
        _shortCurve = document.Application.ShortCurveTolerance * 1.5;
    }

    /// <summary>Cota (pés) do plano de desenho: zero em vistas de desenho; a do nível em plantas.</summary>
    public double ElevationFeet { get; init; }

    /// <summary>Desenha com o ponto (0,0) do desenho na origem informada (metros). Precisa de transação.</summary>
    public int Render(Drawing drawing, Point2 origin)
    {
        int count = 0;
        ElementId? invisible = HatchService.InvisibleLineStyle(_document);
        foreach (DrawFill fill in drawing.Fills)
        {
            count += Fill(fill, origin, invisible) ? 1 : 0;
        }

        EnsureLineStyles();
        double dash = 2.0 * Math.Max(1, _view.Scale) / 1000.0;
        foreach (DrawLine line in drawing.Lines)
        {
            Point2 a = line.A + origin;
            Point2 b = line.B + origin;
            LinePen pen = line.EffectivePen;
            if (!line.Dashed)
            {
                count += Line(a, b, pen) ? 1 : 0;
                continue;
            }

            double length = a.DistanceTo(b);
            Point2 dir = (b - a) / Math.Max(length, 1e-12);
            for (double t = 0; t < length; t += dash * 1.6)
            {
                count += Line(a + dir * t, a + dir * Math.Min(length, t + dash), pen) ? 1 : 0;
            }
        }

        foreach (DrawText text in drawing.Texts)
        {
            count += Text(text, origin) ? 1 : 0;
        }

        return count;
    }

    private bool Fill(DrawFill fill, Point2 origin, ElementId? invisible)
    {
        var points = new List<XYZ>();
        foreach (Point2 p in fill.Outline)
        {
            XYZ xyz = RevitUnits.ToXyz(p + origin, ElevationFeet);
            if (points.Count == 0 || points[^1].DistanceTo(xyz) >= _shortCurve)
            {
                points.Add(xyz);
            }
        }

        while (points.Count > 2 && points[^1].DistanceTo(points[0]) < _shortCurve)
        {
            points.RemoveAt(points.Count - 1);
        }

        if (points.Count < 3 || RegionType(fill) is not FilledRegionType type)
        {
            return false;
        }

        try
        {
            var curves = new List<Curve>();
            for (int i = 0; i < points.Count; i++)
            {
                curves.Add(Autodesk.Revit.DB.Line.CreateBound(points[i], points[(i + 1) % points.Count]));
            }

            FilledRegion region = FilledRegion.Create(_document, type.Id, _view.Id, new List<CurveLoop> { CurveLoop.Create(curves) });
            if (invisible is not null)
            {
                region.SetLineStyleId(invisible);
            }

            return true;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Área colorida ignorada no desenho.", ex);
            return false;
        }
    }

    /// <summary>Tipo de região: cor lisa ou, com hachura de material, a hachura sobre o fundo na cor.</summary>
    private FilledRegionType? RegionType(DrawFill fill)
    {
        if (Core.Hatch.MaterialHatches.Find(fill.Pattern) is Core.Hatch.HatchPattern pattern)
        {
            try
            {
                return HatchService.EnsureRegionType(_document, pattern, new RegionPaint(fill.PatternColor ?? DrawingStyle.HatchInk(fill.Color), fill.Color));
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ViridianException)
            {
                ViridianLog.Warn("Hachura de material indisponível; usando a cor lisa.", ex);
            }
        }

        return HatchService.EnsureSolidRegionType(_document, fill.Color);
    }

    private bool Line(Point2 a, Point2 b, LinePen pen)
    {
        XYZ p = RevitUnits.ToXyz(a, ElevationFeet);
        XYZ q = RevitUnits.ToXyz(b, ElevationFeet);
        if (p.DistanceTo(q) < _shortCurve)
        {
            return false;
        }

        try
        {
            DetailCurve curve = _document.Create.NewDetailCurve(_view, Autodesk.Revit.DB.Line.CreateBound(p, q));
            GraphicsStyle? style = _styles.GetValueOrDefault(pen) ?? _styles.GetValueOrDefault(LinePen.Fina);
            if (style is not null)
            {
                curve.LineStyle = style;
            }

            return true;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return false;
        }
    }

    private bool Text(DrawText text, Point2 origin)
    {
        if (string.IsNullOrWhiteSpace(text.Text))
        {
            return false;
        }

        try
        {
            using var options = new TextNoteOptions(TextType(text.SizeFactor, text.Style))
            {
                HorizontalAlignment = text.Align switch
                {
                    0 => HorizontalTextAlignment.Center,
                    > 0 => HorizontalTextAlignment.Right,
                    _ => HorizontalTextAlignment.Left,
                },
                VerticalAlignment = VerticalTextAlignment.Middle,
            };
            TextNote.Create(_document, _view.Id, RevitUnits.ToXyz(text.At + origin, ElevationFeet), LabelService.ToRevitText(text.Text), options);
            return true;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Texto ignorado no desenho.", ex);
            return false;
        }
    }

    /// <summary>Tipo de texto do desenho na altura pedida (arredondada a 0,5 mm) e no estilo.</summary>
    private ElementId TextType(double factor, TextStyle style)
    {
        double mm = Math.Round(_textMm * Math.Clamp(factor, 0.6, 3.0) * 2) / 2;
        int size = (int)(mm * 10);
        if (_textTypes.TryGetValue((size, style), out ElementId? cached))
        {
            return cached;
        }

        string suffix = style switch
        {
            TextStyle.Titulo => " título",
            TextStyle.Subtitulo => " negrito",
            TextStyle.Nota => " nota",
            TextStyle.Cientifico => " itálico",
            TextStyle.Claro => " claro",
            _ => string.Empty,
        };
        string name = "VIR Desenho " + mm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " mm" + suffix;
        using var collector = new FilteredElementCollector(_document);
        List<TextNoteType> types = collector.OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
        TextNoteType? type = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (type is null)
        {
            TextNoteType source = _document.GetElement(_document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)) as TextNoteType
                                  ?? types.FirstOrDefault()
                                  ?? throw new ViridianException("O projeto não possui nenhum tipo de texto.");
            type = source.Duplicate(name) as TextNoteType ?? source;
            Set(type, BuiltInParameter.TEXT_SIZE, RevitUnits.MmToFeet(mm));
            Set(type, BuiltInParameter.TEXT_BACKGROUND, 1);
            Set(type, BuiltInParameter.TEXT_BOX_VISIBILITY, 0);
            Set(type, BuiltInParameter.TEXT_STYLE_BOLD, style is TextStyle.Titulo or TextStyle.Subtitulo or TextStyle.Claro ? 1 : 0);
            Set(type, BuiltInParameter.TEXT_STYLE_ITALIC, style == TextStyle.Cientifico ? 1 : 0);
            Rgb color = style switch
            {
                TextStyle.Titulo => DrawingStyle.Primary,
                TextStyle.Nota => DrawingStyle.Muted,
                TextStyle.Claro => Rgb.White,
                _ => DrawingStyle.Ink,
            };
            Set(type, BuiltInParameter.LINE_COLOR, color.R + color.G * 256 + color.B * 65536);
        }

        _textTypes[(size, style)] = type.Id;
        return type.Id;
    }

    private static void Set(Element element, BuiltInParameter id, double value)
    {
        Parameter? p = element.get_Parameter(id);
        if (p is { IsReadOnly: false, StorageType: StorageType.Double })
        {
            p.Set(value);
        }
    }

    private static void Set(Element element, BuiltInParameter id, int value)
    {
        Parameter? p = element.get_Parameter(id);
        if (p is { IsReadOnly: false, StorageType: StorageType.Integer })
        {
            p.Set(value);
        }
    }

    /// <summary>Estilo de linha de uma caneta do Viridian (criado se faltar), para outras ferramentas. Precisa de transação.</summary>
    public static GraphicsStyle? PenStyle(Document document, View view, LinePen pen)
    {
        var renderer = new DrawingRenderer(document, view);
        renderer.EnsureLineStyles();
        return renderer._styles.GetValueOrDefault(pen);
    }

    /// <summary>Estilos de linha do Viridian (subcategorias de Linhas), um por caneta.</summary>
    private void EnsureLineStyles()
    {
        if (_stylesReady)
        {
            return;
        }

        _stylesReady = true;
        Category lines;
        try
        {
            lines = Category.GetCategory(_document, BuiltInCategory.OST_Lines);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Estilos de linha do desenho indisponíveis; usando o estilo padrão.", ex);
            return;
        }

        foreach ((LinePen pen, (string name, int weight, Rgb color)) in Pens)
        {
            try
            {
                _styles[pen] = Subcategory(lines, name, weight, color);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                ViridianLog.Warn("Estilo de linha " + name + " indisponível.", ex);
            }
        }
    }

    private GraphicsStyle? Subcategory(Category parent, string name, int weight, Rgb color)
    {
        bool exists = parent.SubCategories.Contains(name);
        Category sub = exists ? parent.SubCategories.get_Item(name) : _document.Settings.Categories.NewSubcategory(parent, name);
        if (!exists)
        {
            // Só na criação: depois o escritório pode ajustar espessura e cor em Estilos de objeto.
            sub.SetLineWeight(weight, GraphicsStyleType.Projection);
            sub.LineColor = new Color(color.R, color.G, color.B);
        }

        return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
    }
}
