using System.Windows;
using System.Windows.Media;
using Viridian.Core.Geometry;
using Viridian.Core.Hatch;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;

namespace Viridian.UI.Controls;

/// <summary>Desenha o símbolo 2D de uma espécie, no perfil de representação escolhido.</summary>
public sealed class SymbolPreview : FrameworkElement
{
    private PlantSymbol? _symbol;
    private DetailProfile _profile = DetailProfile.Detalhado;

    public void Show(PlantSymbol? symbol, DetailProfile profile)
    {
        _symbol = symbol;
        _profile = profile;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_symbol is null || ActualWidth < 8 || ActualHeight < 8)
        {
            return;
        }

        double size = Math.Min(ActualWidth, ActualHeight) - 12;
        double scale = size / _symbol.Diameter;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        Point Map(Point2 p) => new(center.X + p.X * scale, center.Y - p.Y * scale);

        foreach (SymbolPrimitive primitive in _symbol.ForProfile(_profile))
        {
            Pen pen = PenFor(primitive.Tier);
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

    private static readonly Pen Heavy = CreatePen(1.4);
    private static readonly Pen Medium = CreatePen(1.0);
    private static readonly Pen Light = CreatePen(0.6);

    private static Pen PenFor(SymbolTier tier) => tier switch
    {
        SymbolTier.Outline or SymbolTier.MinimalOutline => Heavy,
        SymbolTier.Center or SymbolTier.Essential => Medium,
        _ => Light,
    };

    private static Pen CreatePen(double thickness)
    {
        var pen = new Pen(Brushes.Black, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
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
    private IReadOnlyList<Point2> _path = Array.Empty<Point2>();
    private double _plantDiameter;
    private bool _hatched;

    /// <summary>Atualiza o desenho.</summary>
    /// <param name="region">Limite da região (opcional).</param>
    /// <param name="points">Pontos de plantio calculados.</param>
    /// <param name="plantDiameterM">Diâmetro de copa dos exemplares, para desenhá-los em proporção.</param>
    /// <param name="hatched">Indica preenchimento por hachura.</param>
    /// <param name="path">Eixo do plantio linear (opcional).</param>
    public void Show(Polygon2? region, IReadOnlyList<Point2>? points, double plantDiameterM, bool hatched, IReadOnlyList<Point2>? path = null)
    {
        _region = region;
        _points = points ?? Array.Empty<Point2>();
        _plantDiameter = plantDiameterM;
        _hatched = hatched;
        _path = path ?? Array.Empty<Point2>();
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

        all.AddRange(_path);
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
            dc.DrawGeometry(fill, RegionPen, geometry);
        }

        if (_path.Count > 1)
        {
            for (int i = 1; i < _path.Count; i++)
            {
                dc.DrawLine(PathPen, Map(_path[i - 1]), Map(_path[i]));
            }
        }

        if (_points.Count > 0)
        {
            double radius = Math.Max(1.5, _plantDiameter * scale / 2);
            bool outlineOnly = radius > 4;
            int step = Math.Max(1, _points.Count / MaxDrawnPoints);
            for (int i = 0; i < _points.Count; i += step)
            {
                Point p = Map(_points[i]);
                if (outlineOnly)
                {
                    dc.DrawEllipse(CrownFill, CrownPen, p, radius, radius);
                    dc.DrawEllipse(PointFill, null, p, 1.5, 1.5);
                }
                else
                {
                    dc.DrawEllipse(PointFill, null, p, radius, radius);
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
