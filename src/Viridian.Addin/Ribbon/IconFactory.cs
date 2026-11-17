using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Viridian.Addin.Ribbon;

/// <summary>
/// Desenha os ícones da faixa de opções em tempo de execução, em 16 e 32 pixels.
/// Todos são vetoriais, na mesma linguagem gráfica (traço verde sobre fundo transparente).
/// </summary>
internal static class IconFactory
{
    private static readonly Color Green = Color.FromRgb(0x2F, 0x6B, 0x4F);
    private static readonly Color DarkGreen = Color.FromRgb(0x1F, 0x4D, 0x3A);
    private static readonly Color LightGreen = Color.FromRgb(0xCF, 0xE3, 0xD6);
    private static readonly Color Accent = Color.FromRgb(0xC8, 0x8A, 0x12);

    public static ImageSource Large(string key) => Render(key, 32);

    public static ImageSource Small(string key) => Render(key, 16);

    private static BitmapSource Render(string key, int size)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 32.0, size / 32.0));
            Draw(dc, key, size);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Pen Stroke(Color color, double width, double size)
    {
        // Traços mais grossos no ícone pequeno para que não desapareçam.
        double w = size <= 16 ? width * 1.5 : width;
        var pen = new Pen(new SolidColorBrush(color), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    private static Brush Fill(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Copa em planta: contorno em gomos com marca de tronco.</summary>
    private static void Crown(DrawingContext dc, Point center, double radius, int lobes, Pen pen, Brush? fill)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            for (int i = 0; i <= lobes; i++)
            {
                double a = 2 * Math.PI * i / lobes;
                var p = new Point(center.X + radius * 0.88 * Math.Cos(a), center.Y + radius * 0.88 * Math.Sin(a));
                if (i == 0)
                {
                    g.BeginFigure(p, fill is not null, true);
                }
                else
                {
                    g.ArcTo(p, new Size(radius * 0.32, radius * 0.32), 0, false, SweepDirection.Clockwise, true, true);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(fill, pen, geometry);
    }

    private static void Text(DrawingContext dc, string text, Point center, double emSize, Color color, int size)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            emSize,
            Fill(color),
            1.0);
        dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
    }

    private static void Arrow(DrawingContext dc, Point from, Point to, Pen pen)
    {
        dc.DrawLine(pen, from, to);
        Vector d = to - from;
        d.Normalize();
        var n = new Vector(-d.Y, d.X);
        dc.DrawLine(pen, to, to - d * 4 + n * 3);
        dc.DrawLine(pen, to, to - d * 4 - n * 3);
    }

    private static void CircularArrows(DrawingContext dc, Point c, double r, Pen pen)
    {
        var top = new StreamGeometry();
        using (StreamGeometryContext g = top.Open())
        {
            g.BeginFigure(new Point(c.X - r, c.Y), false, false);
            g.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            g.BeginFigure(new Point(c.X + r, c.Y), false, false);
            g.ArcTo(new Point(c.X - r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
        }

        top.Freeze();
        dc.DrawGeometry(null, pen, top);
        dc.DrawLine(pen, new Point(c.X + r, c.Y), new Point(c.X + r - 3.5, c.Y - 3.5));
        dc.DrawLine(pen, new Point(c.X + r, c.Y), new Point(c.X + r + 3.5, c.Y - 3.5));
        dc.DrawLine(pen, new Point(c.X - r, c.Y), new Point(c.X - r - 3.5, c.Y + 3.5));
        dc.DrawLine(pen, new Point(c.X - r, c.Y), new Point(c.X - r + 3.5, c.Y + 3.5));
    }

    private static void Blob(DrawingContext dc, Pen pen, Brush fill)
    {
        Geometry geometry = Geometry.Parse("M 5,18 C 3,9 13,4 19,6 C 27,8 30,15 27,22 C 24,29 13,29 8,26 C 6,24 5,21 5,18 Z");
        dc.DrawGeometry(fill, pen, geometry);
    }

    private static void Draw(DrawingContext dc, string key, int size)
    {
        Pen main = Stroke(Green, 2.0, size);
        Pen thin = Stroke(Green, 1.3, size);
        Pen dark = Stroke(DarkGreen, 2.0, size);
        Pen accent = Stroke(Accent, 2.2, size);
        Brush light = Fill(LightGreen);
        Brush green = Fill(Green);

        switch (key)
        {
            case "library":
                dc.DrawRoundedRectangle(light, thin, new Rect(4, 4, 18, 24), 2, 2);
                dc.DrawLine(thin, new Point(8, 4), new Point(8, 28));
                Crown(dc, new Point(21, 18), 9, 7, main, Fill(Colors.White));
                dc.DrawEllipse(green, null, new Point(21, 18), 1.6, 1.6);
                break;

            case "import":
                dc.DrawRoundedRectangle(light, thin, new Rect(4, 13, 24, 15), 2, 2);
                Arrow(dc, new Point(16, 3), new Point(16, 21), accent);
                break;

            case "sync":
                CircularArrows(dc, new Point(16, 16), 10, main);
                dc.DrawEllipse(green, null, new Point(16, 16), 2.5, 2.5);
                break;

            case "insert":
                Crown(dc, new Point(15, 17), 12, 9, main, light);
                dc.DrawEllipse(Fill(Colors.White), main, new Point(15, 17), 2.2, 2.2);
                dc.DrawLine(accent, new Point(26, 3), new Point(26, 11));
                dc.DrawLine(accent, new Point(22, 7), new Point(30, 7));
                break;

            case "distribute":
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        double x = 7 + col * 9 + (row % 2 == 1 ? 4.5 : 0);
                        if (x > 28)
                        {
                            continue;
                        }

                        dc.DrawEllipse(light, thin, new Point(x, 7 + row * 9), 3.6, 3.6);
                        dc.DrawEllipse(green, null, new Point(x, 7 + row * 9), 1.1, 1.1);
                    }
                }

                break;

            case "linear":
                dc.DrawLine(Stroke(Accent, 1.4, size), new Point(3, 27), new Point(29, 5));
                foreach (double t in new[] { 0.1, 0.5, 0.9 })
                {
                    var p = new Point(3 + 26 * t, 27 - 22 * t);
                    dc.DrawEllipse(light, thin, p, 4.2, 4.2);
                    dc.DrawEllipse(green, null, p, 1.2, 1.2);
                }

                break;

            case "substitute":
                Crown(dc, new Point(9, 10), 7, 7, thin, light);
                Crown(dc, new Point(23, 22), 7, 7, main, Fill(Colors.White));
                Arrow(dc, new Point(17, 6), new Point(26, 12), accent);
                Arrow(dc, new Point(15, 26), new Point(6, 20), accent);
                break;

            case "edit":
                Crown(dc, new Point(13, 15), 10, 8, thin, light);
                dc.DrawLine(Stroke(Accent, 3.2, size), new Point(19, 27), new Point(29, 17));
                dc.DrawLine(Stroke(DarkGreen, 1.4, size), new Point(17, 29), new Point(19, 27));
                break;

            case "massing":
                Blob(dc, main, light);
                for (int i = 0; i < 4; i++)
                {
                    dc.DrawLine(thin, new Point(9 + i * 5, 23), new Point(14 + i * 5, 12));
                }

                break;

            case "massing-edit":
                Blob(dc, thin, light);
                dc.DrawLine(Stroke(Accent, 3.2, size), new Point(17, 27), new Point(28, 16));
                dc.DrawLine(Stroke(DarkGreen, 1.4, size), new Point(15, 29), new Point(17, 27));
                break;

            case "recalc":
                Blob(dc, thin, light);
                CircularArrows(dc, new Point(16, 17), 6.5, Stroke(Accent, 1.8, size));
                break;

            case "code":
                dc.DrawRoundedRectangle(light, thin, new Rect(3, 8, 26, 16), 3, 3);
                Text(dc, "AR", new Point(16, 16), 11, DarkGreen, size);
                break;

            case "label":
                dc.DrawGeometry(light, main, Geometry.Parse("M 4,9 L 20,9 L 28,16 L 20,23 L 4,23 Z"));
                dc.DrawEllipse(green, null, new Point(20, 16), 1.8, 1.8);
                dc.DrawLine(thin, new Point(7, 14), new Point(15, 14));
                dc.DrawLine(thin, new Point(7, 18), new Point(13, 18));
                break;

            case "label-update":
                dc.DrawGeometry(light, thin, Geometry.Parse("M 3,5 L 15,5 L 21,11 L 15,17 L 3,17 Z"));
                CircularArrows(dc, new Point(21, 22), 6, Stroke(Accent, 1.8, size));
                break;

            case "legend":
                for (int i = 0; i < 3; i++)
                {
                    double y = 7 + i * 9;
                    Crown(dc, new Point(8, y), 4, 6, thin, light);
                    dc.DrawLine(main, new Point(15, y), new Point(28, y));
                }

                break;

            case "reports":
                dc.DrawRoundedRectangle(Fill(Colors.White), main, new Rect(4, 4, 24, 24), 2, 2);
                dc.DrawRectangle(green, null, new Rect(4, 4, 24, 6));
                dc.DrawLine(thin, new Point(4, 16), new Point(28, 16));
                dc.DrawLine(thin, new Point(4, 22), new Point(28, 22));
                dc.DrawLine(thin, new Point(13, 10), new Point(13, 28));
                break;

            case "schedule":
                dc.DrawRoundedRectangle(light, main, new Rect(4, 4, 24, 24), 2, 2);
                for (int i = 1; i < 4; i++)
                {
                    dc.DrawLine(thin, new Point(4, 4 + i * 6), new Point(28, 4 + i * 6));
                }

                dc.DrawLine(thin, new Point(12, 4), new Point(12, 28));
                dc.DrawLine(thin, new Point(20, 4), new Point(20, 28));
                break;

            case "excel":
                dc.DrawRoundedRectangle(light, main, new Rect(4, 6, 18, 22), 2, 2);
                dc.DrawLine(thin, new Point(4, 13), new Point(22, 13));
                dc.DrawLine(thin, new Point(4, 20), new Point(22, 20));
                dc.DrawLine(thin, new Point(12, 6), new Point(12, 28));
                Arrow(dc, new Point(18, 17), new Point(30, 17), accent);
                break;

            case "check":
                dc.DrawEllipse(light, main, new Point(16, 16), 12, 12);
                dc.DrawGeometry(null, Stroke(DarkGreen, 3, size), Geometry.Parse("M 10,16 L 14.5,21 L 23,11"));
                break;

            case "scale":
                dc.DrawRoundedRectangle(light, main, new Rect(3, 11, 26, 10), 1.5, 1.5);
                for (int i = 0; i <= 6; i++)
                {
                    double x = 6 + i * 3.4;
                    dc.DrawLine(thin, new Point(x, 11), new Point(x, i % 2 == 0 ? 17 : 14.5));
                }

                break;

            case "settings":
            {
                var center = new Point(16, 16);
                for (int i = 0; i < 8; i++)
                {
                    double a = Math.PI * i / 4;
                    dc.DrawLine(Stroke(Green, 4.2, size), new Point(center.X + 8 * Math.Cos(a), center.Y + 8 * Math.Sin(a)), new Point(center.X + 12 * Math.Cos(a), center.Y + 12 * Math.Sin(a)));
                }

                dc.DrawEllipse(light, Stroke(Green, 3, size), center, 8.5, 8.5);
                dc.DrawEllipse(Fill(Colors.White), thin, center, 3.5, 3.5);
                break;
            }

            case "about":
                dc.DrawEllipse(light, main, new Point(16, 16), 12, 12);
                Text(dc, "i", new Point(16, 16.5), 16, DarkGreen, size);
                break;

            default:
                Crown(dc, new Point(16, 16), 12, 9, main, light);
                break;
        }
    }
}
