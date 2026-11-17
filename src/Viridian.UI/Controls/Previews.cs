using System.Windows;
using System.Windows.Media;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Core.Hatch;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;

namespace Viridian.UI.Controls;

/// <summary>Desenha o símbolo 2D de uma espécie, no perfil de representação escolhido, com as cores da planta.</summary>
public sealed class SymbolPreview : FrameworkElement
{
    private PlantSymbol? _symbol;
    private DetailProfile _profile = DetailProfile.Detalhado;
    private SymbolPaint? _paint;
    private ViridianSettings? _settings;

    /// <param name="paint">Cores da copa e da floração; nulo desenha só as linhas.</param>
    /// <param name="settings">Configurações de onde vêm as cores e espessuras das linhas; nulo usa o padrão.</param>
    public void Show(PlantSymbol? symbol, DetailProfile profile, SymbolPaint? paint = null, ViridianSettings? settings = null)
    {
        _symbol = symbol;
        _profile = profile;
        _paint = paint;
        _settings = settings;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(Brushes.White, null, bounds);
        if (_symbol is null || ActualWidth < 8 || ActualHeight < 8)
        {
            return;
        }

        double size = Math.Min(ActualWidth, ActualHeight) - 12;
        double scale = size / _symbol.Diameter;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        Point Map(Point2 p) => new(center.X + p.X * scale, center.Y - p.Y * scale);

        DrawColors(dc, Map, scale);

        var pens = new Dictionary<SymbolTier, Pen>();
        foreach (SymbolPrimitive primitive in _symbol.ForProfile(_profile))
        {
            if (!pens.TryGetValue(primitive.Tier, out Pen? pen))
            {
                pen = PenFor(primitive.Tier);
                pens[primitive.Tier] = pen;
            }

            switch (primitive)
            {
                case SymbolLine line:
                    dc.DrawLine(pen, Map(line.A), Map(line.B));
                    break;
                case SymbolArc arc when ArcInfo.TryFromThreePoints(arc.Start, arc.Mid, arc.End, out ArcInfo info):
                {
                    var geometry = new StreamGeometry();
                    using (StreamGeometryContext context = geometry.Open())
                    {
                        context.BeginFigure(Map(arc.Start), false, false);
                        double radius = info.Radius * scale;
                        context.ArcTo(
                            Map(arc.End),
                            new Size(radius, radius),
                            0,
                            Math.Abs(info.Sweep) > Math.PI,
                            info.Sweep > 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise,
                            true,
                            false);
                    }

                    geometry.Freeze();
                    dc.DrawGeometry(null, pen, geometry);
                    break;
                }

                case SymbolArc arc:
                    dc.DrawLine(pen, Map(arc.Start), Map(arc.End));
                    break;
            }
        }
    }

    private void DrawColors(DrawingContext dc, Func<Point2, Point> map, double scale)
    {
        if (_symbol is null || _paint is null)
        {
            return;
        }

        if (_paint.Mode != CrownFillMode.Nenhum && _symbol.FillOutline.Count >= 3 && TierVisibility.IsVisible(SymbolTier.Fill, _profile))
        {
            var crown = new StreamGeometry();
            using (StreamGeometryContext context = crown.Open())
            {
                context.BeginFigure(map(_symbol.FillOutline[0]), true, true);
                for (int i = 1; i < _symbol.FillOutline.Count; i++)
                {
                    context.LineTo(map(_symbol.FillOutline[i]), true, false);
                }
            }

            crown.Freeze();
            Color color = ToColor(_paint.Crown);
            if (_paint.Mode == CrownFillMode.Solido)
            {
                dc.DrawGeometry(Frozen(new SolidColorBrush(color)), null, crown);
            }
            else
            {
                // Reticulado: linhas cruzadas a 45°, recortadas pela copa.
                Rect box = crown.Bounds;
                var pen = new Pen(Frozen(new SolidColorBrush(color)), 0.6);
                pen.Freeze();
                dc.PushClip(crown);
                double step = 4.0;
                double span = box.Width + box.Height;
                for (double d = -span; d <= span; d += step)
                {
                    dc.DrawLine(pen, new Point(box.Left + d, box.Top), new Point(box.Left + d + span, box.Top + span));
                    dc.DrawLine(pen, new Point(box.Left + d, box.Bottom), new Point(box.Left + d + span, box.Bottom - span));
                }

                dc.Pop();
            }
        }

        if (_paint.Flower is Rgb flower && TierVisibility.IsVisible(SymbolTier.Flowers, _profile))
        {
            Brush brush = Frozen(new SolidColorBrush(ToColor(flower)));
            foreach (FlowerSpot spot in _symbol.FlowerSpots)
            {
                double r = Math.Max(1.2, spot.Radius * scale);
                dc.DrawEllipse(brush, null, map(spot.Center), r, r);
            }
        }
    }

    private Pen PenFor(SymbolTier tier)
    {
        double thickness = tier switch
        {
            SymbolTier.Outline or SymbolTier.MinimalOutline => 1.4,
            SymbolTier.Center or SymbolTier.Essential => 1.0,
            _ => 0.6,
        };
        TierStyle style = (_settings ?? DefaultSettings).StyleOf(tier);
        Color color = _paint is null ? Colors.Black : ToColor(Rgb.Parse(style.Color, Rgb.Black));
        var pen = new Pen(Frozen(new SolidColorBrush(color)), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }

    private static readonly ViridianSettings DefaultSettings = new();

    internal static Color ToColor(Rgb rgb) => Color.FromRgb(rgb.R, rgb.G, rgb.B);

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>Amostra de uma hachura, em escala de papel ampliada.</summary>
public sealed class HatchPreview : FrameworkElement
{
    private HatchPattern? _pattern;

    /// <summary>Pixels por milímetro de papel.</summary>
    public double PixelsPerMm { get; set; } = 3.0;

    public void Show(HatchPattern? pattern)
    {
        _pattern = pattern;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(Brushes.White, null, bounds);
        if (_pattern is null || ActualWidth < 4 || ActualHeight < 4)
        {
            return;
        }

        var area = new Bounds2(0, 0, ActualWidth / PixelsPerMm, ActualHeight / PixelsPerMm);
        var pen = new Pen(Brushes.Black, 0.9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.PushClip(new RectangleGeometry(bounds));
        foreach ((Point2 a, Point2 b) in HatchRasterizer.Segments(_pattern, area, 6000))
        {
            dc.DrawLine(pen, new Point(a.X * PixelsPerMm, ActualHeight - a.Y * PixelsPerMm), new Point(b.X * PixelsPerMm, ActualHeight - b.Y * PixelsPerMm));
        }

        dc.Pop();
    }
}

/// <summary>Pré-visualização em planta de uma região de plantio e dos pontos calculados.</summary>
public sealed class PlanPreview : FrameworkElement
{
    private const int MaxDrawnPoints = 6000;
    private Polygon2? _region;
    private IReadOnlyList<Point2> _points = Array.Empty<Point2>();
    private IReadOnlyList<PlantingPath> _paths = Array.Empty<PlantingPath>();
    private double _plantDiameter;
    private bool _hatched;
    private Rgb? _color;

    /// <summary>Atualiza o desenho.</summary>
    /// <param name="region">Limite da região (opcional).</param>
    /// <param name="points">Pontos de plantio calculados.</param>
    /// <param name="plantDiameterM">Diâmetro de copa dos exemplares, para desenhá-los em proporção.</param>
    /// <param name="hatched">Indica preenchimento por hachura.</param>
    /// <param name="paths">Eixos do plantio linear (opcional).</param>
    /// <param name="color">Cor da espécie (opcional), para desenhar as copas e o maciço na cor da planta.</param>
    public void Show(Polygon2? region, IReadOnlyList<Point2>? points, double plantDiameterM, bool hatched, IReadOnlyList<PlantingPath>? paths = null, Rgb? color = null)
    {
        _color = color;
        _region = region;
        _points = points ?? Array.Empty<Point2>();
        _plantDiameter = plantDiameterM;
        _hatched = hatched;
        _paths = paths ?? Array.Empty<PlantingPath>();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (ActualWidth < 16 || ActualHeight < 16)
        {
            return;
        }

        var all = new List<Point2>();
        if (_region is not null && !_region.IsEmpty)
        {
            all.AddRange(_region.Rings.SelectMany(r => r));
        }

        all.AddRange(_paths.SelectMany(p => p.Points));
        if (all.Count == 0)
        {
            all.AddRange(_points);
        }

        if (all.Count == 0)
        {
            return;
        }

        Bounds2 bounds = Bounds2.FromPoints(all);
        double width = Math.Max(bounds.Width, 0.01);
        double height = Math.Max(bounds.Height, 0.01);
        const double margin = 12;
        double scale = Math.Min((ActualWidth - 2 * margin) / width, (ActualHeight - 2 * margin) / height);
        double offsetX = (ActualWidth - width * scale) / 2;
        double offsetY = (ActualHeight - height * scale) / 2;
        Point Map(Point2 p) => new(offsetX + (p.X - bounds.MinX) * scale, ActualHeight - offsetY - (p.Y - bounds.MinY) * scale);

        if (_region is not null && !_region.IsEmpty)
        {
            var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (StreamGeometryContext context = geometry.Open())
            {
                foreach (IReadOnlyList<Point2> ring in _region.Rings)
                {
                    context.BeginFigure(Map(ring[0]), true, true);
                    for (int i = 1; i < ring.Count; i++)
                    {
                        context.LineTo(Map(ring[i]), true, false);
                    }
                }
            }

            geometry.Freeze();
            Brush fill = _hatched ? HatchFill : RegionFill;
            if (_color is Rgb species && _hatched)
            {
                fill = new SolidColorBrush(SymbolPreview.ToColor(species.Tint(0.6)));
                fill.Freeze();
            }

            dc.DrawGeometry(fill, RegionPen, geometry);
        }

        foreach (PlantingPath path in _paths)
        {
            for (int i = 1; i < path.Points.Count; i++)
            {
                dc.DrawLine(PathPen, Map(path.Points[i - 1]), Map(path.Points[i]));
            }

            if (path.Closed && path.Points.Count > 2)
            {
                dc.DrawLine(PathPen, Map(path.Points[^1]), Map(path.Points[0]));
            }

            if (path.Points.Count > 1 && !path.Closed)
            {
                // Marca o início do eixo (referência para deslocamento lateral e alinhamento).
                dc.DrawEllipse(null, PathPen, Map(path.Points[0]), 4, 4);
            }
        }

        if (_points.Count > 0)
        {
            Brush crownFill = CrownFill;
            Pen crownPen = CrownPen;
            Brush pointFill = PointFill;
            if (_color is Rgb species)
            {
                Rgb tint = species.Tint(0.28);
                crownFill = new SolidColorBrush(Color.FromArgb(0xC8, tint.R, tint.G, tint.B));
                crownFill.Freeze();
                crownPen = FrozenPen(SymbolPreview.ToColor(species.Shade(0.35)), 0.8);
                pointFill = new SolidColorBrush(SymbolPreview.ToColor(species.Shade(0.45)));
                pointFill.Freeze();
            }

            double radius = Math.Max(1.5, _plantDiameter * scale / 2);
            bool outlineOnly = radius > 4;
            int step = Math.Max(1, _points.Count / MaxDrawnPoints);
            for (int i = 0; i < _points.Count; i += step)
            {
                Point p = Map(_points[i]);
                if (outlineOnly)
                {
                    dc.DrawEllipse(crownFill, crownPen, p, radius, radius);
                    dc.DrawEllipse(pointFill, null, p, 1.5, 1.5);
                }
                else
                {
                    dc.DrawEllipse(pointFill, null, p, radius, radius);
                }
            }
        }
    }

    private static readonly Brush RegionFill = Frozen(new SolidColorBrush(Color.FromRgb(0xEE, 0xF5, 0xF0)));
    private static readonly Brush HatchFill = Frozen(new SolidColorBrush(Color.FromRgb(0xD3, 0xE6, 0xDA)));
    private static readonly Brush PointFill = Frozen(new SolidColorBrush(Color.FromRgb(0x1F, 0x4D, 0x3A)));
    private static readonly Brush CrownFill = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x2F, 0x6B, 0x4F)));
    private static readonly Pen RegionPen = FrozenPen(Color.FromRgb(0x2F, 0x6B, 0x4F), 1.2);
    private static readonly Pen CrownPen = FrozenPen(Color.FromRgb(0x2F, 0x6B, 0x4F), 0.8);
    private static readonly Pen PathPen = FrozenPen(Color.FromRgb(0x9A, 0x5B, 0x00), 1.4);

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
