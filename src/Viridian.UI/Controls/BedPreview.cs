using System.Windows;
using System.Windows.Media;
using Viridian.Core.Geometry;

namespace Viridian.UI.Controls;

/// <summary>Círculo de uma planta na prévia do canteiro.</summary>
public readonly record struct PreviewPlant(Point2 Center, double RadiusM, Color Fill);

/// <summary>Contorno preenchido na prévia (forração de base ou maciço).</summary>
public sealed record PreviewArea(IReadOnlyList<IReadOnlyList<Point2>> Rings, Color Fill);

/// <summary>
/// Prévia em planta de um canteiro: contorno, áreas preenchidas e as plantas em círculos na cor da folhagem,
/// em escala, com o norte da planta para cima. Usada na composição e nos modelos de canteiro.
/// </summary>
public sealed class BedPreview : FrameworkElement
{
    private IReadOnlyList<IReadOnlyList<Point2>> _outline = Array.Empty<IReadOnlyList<Point2>>();
    private IReadOnlyList<PreviewArea> _areas = Array.Empty<PreviewArea>();
    private IReadOnlyList<PreviewPlant> _plants = Array.Empty<PreviewPlant>();
    private Point2? _marker;
    private string _caption = string.Empty;

    /// <summary>Atualiza o conteúdo. <paramref name="marker"/> indica o lado do fundo (canteiro encostado).</summary>
    public void Show(IReadOnlyList<IReadOnlyList<Point2>> outline, IReadOnlyList<PreviewArea> areas, IReadOnlyList<PreviewPlant> plants, Point2? marker = null, string caption = "")
    {
        _outline = outline;
        _areas = areas;
        _plants = plants;
        _marker = marker;
        _caption = caption;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xFB, 0xFB, 0xF8)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        var points = _outline.SelectMany(r => r).Concat(_areas.SelectMany(a => a.Rings.SelectMany(r => r))).ToList();
        points.AddRange(_plants.Select(p => p.Center));
        if (points.Count == 0 || ActualWidth < 20 || ActualHeight < 20)
        {
            return;
        }

        double maxR = _plants.Count == 0 ? 0 : _plants.Max(p => p.RadiusM);
        double minX = points.Min(p => p.X) - maxR, maxX = points.Max(p => p.X) + maxR;
        double minY = points.Min(p => p.Y) - maxR, maxY = points.Max(p => p.Y) + maxR;
        double w = Math.Max(1e-6, maxX - minX), h = Math.Max(1e-6, maxY - minY);
        const double pad = 14;
        double scale = Math.Min((ActualWidth - 2 * pad) / w, (ActualHeight - 2 * pad - 14) / h);
        double ox = (ActualWidth - w * scale) / 2, oy = (ActualHeight - 14 - h * scale) / 2;
        Point Map(Point2 p) => new(ox + (p.X - minX) * scale, oy + (maxY - p.Y) * scale);

        StreamGeometry Rings(IEnumerable<IReadOnlyList<Point2>> rings)
        {
            var g = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (StreamGeometryContext c = g.Open())
            {
                foreach (IReadOnlyList<Point2> ring in rings.Where(r => r.Count >= 3))
                {
                    c.BeginFigure(Map(ring[0]), true, true);
                    c.PolyLineTo(ring.Skip(1).Select(Map).ToList(), true, false);
                }
            }

            g.Freeze();
            return g;
        }

        foreach (PreviewArea area in _areas)
        {
            var brush = new SolidColorBrush(Color.FromArgb(150, area.Fill.R, area.Fill.G, area.Fill.B));
            dc.DrawGeometry(brush, new Pen(new SolidColorBrush(Color.FromArgb(160, 60, 70, 60)), 0.8), Rings(area.Rings));
        }

        if (_outline.Count > 0)
        {
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0x3C, 0x46, 0x3C)), 1.6), Rings(_outline));
        }

        var edge = new Pen(new SolidColorBrush(Color.FromArgb(150, 30, 40, 30)), 0.6);
        edge.Freeze();
        var brushes = new Dictionary<Color, SolidColorBrush>();
        foreach (PreviewPlant plant in _plants.OrderByDescending(p => p.RadiusM))
        {
            if (!brushes.TryGetValue(plant.Fill, out SolidColorBrush? fill))
            {
                fill = new SolidColorBrush(Color.FromArgb(215, plant.Fill.R, plant.Fill.G, plant.Fill.B));
                fill.Freeze();
                brushes[plant.Fill] = fill;
            }

            double r = Math.Max(1.6, plant.RadiusM * scale);
            dc.DrawEllipse(fill, r > 3 ? edge : null, Map(plant.Center), r, r);
        }

        if (_marker is Point2 marker)
        {
            Point m = Map(new Point2(Math.Clamp(marker.X, minX, maxX), Math.Clamp(marker.Y, minY, maxY)));
            var tri = new StreamGeometry();
            using (StreamGeometryContext c = tri.Open())
            {
                c.BeginFigure(new Point(m.X, m.Y - 7), true, true);
                c.LineTo(new Point(m.X - 6, m.Y + 5), true, false);
                c.LineTo(new Point(m.X + 6, m.Y + 5), true, false);
            }

            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xC0, 0x5A, 0x2B)), null, tri);
            DrawText(dc, "fundo", new Point(m.X + 8, m.Y - 8), 10, Color.FromRgb(0xC0, 0x5A, 0x2B));
        }

        // Escala gráfica de 1 m (ou 5 m em canteiros grandes) e legenda.
        double bar = w > 30 ? 5 : 1;
        double y = ActualHeight - 10;
        var pen = new Pen(Brushes.DimGray, 1.2);
        dc.DrawLine(pen, new Point(pad, y), new Point(pad + bar * scale, y));
        dc.DrawLine(pen, new Point(pad, y - 3), new Point(pad, y + 3));
        dc.DrawLine(pen, new Point(pad + bar * scale, y - 3), new Point(pad + bar * scale, y + 3));
        DrawText(dc, bar + " m", new Point(pad + bar * scale + 4, y - 7), 10, Colors.DimGray);
        if (_caption.Length > 0)
        {
            var text = Text(_caption, 10, Colors.DimGray);
            dc.DrawText(text, new Point(ActualWidth - pad - text.Width, y - 7));
        }
    }

    private FormattedText Text(string text, double size, Color color) =>
        new(text, System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), size,
            new SolidColorBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void DrawText(DrawingContext dc, string text, Point at, double size, Color color) => dc.DrawText(Text(text, size, color), at);
}
