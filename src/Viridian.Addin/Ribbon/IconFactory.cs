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

            case "palette":
            {
                dc.DrawEllipse(Fill(Colors.White), main, new Point(16, 16), 13, 11);
                Color[] dots = { Color.FromRgb(0x9B, 0x6B, 0xD3), Color.FromRgb(0xF2, 0xC2, 0x30), Color.FromRgb(0xE5, 0x60, 0x9A), Color.FromRgb(0x4E, 0x8A, 0x3E) };
                Point[] at = { new(10, 12), new(16, 9), new(22, 12), new(22, 19) };
                for (int i = 0; i < dots.Length; i++)
                {
                    dc.DrawEllipse(Fill(dots[i]), null, at[i], 3, 3);
                }

                dc.DrawEllipse(Fill(Colors.White), thin, new Point(11, 20), 2.5, 2.5);
                break;
            }

            case "composition":
                dc.DrawRoundedRectangle(light, main, new Rect(3, 6, 26, 20), 4, 4);
                Crown(dc, new Point(11, 14), 5, 7, thin, Fill(Color.FromRgb(0x6E, 0x9E, 0x36)));
                Crown(dc, new Point(21, 18), 4, 6, thin, Fill(Color.FromRgb(0x9B, 0x6B, 0xD3)));
                dc.DrawEllipse(Fill(Accent), null, new Point(25, 9), 3.5, 3.5);
                break;

            case "calendar":
                dc.DrawRoundedRectangle(Fill(Colors.White), main, new Rect(3, 5, 26, 23), 2, 2);
                dc.DrawRectangle(green, null, new Rect(3, 5, 26, 5));
                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        bool on = (r + c) % 3 == 0;
                        dc.DrawRectangle(on ? Fill(Color.FromRgb(0xE5, 0x60, 0x9A)) : light, null, new Rect(5 + c * 6, 12 + r * 5, 4.5, 3.5));
                    }
                }

                break;

            case "memorial":
                dc.DrawRectangle(Fill(Colors.White), main, new Rect(6, 3, 20, 26));
                dc.DrawRectangle(light, thin, new Rect(9, 6, 14, 9));
                Crown(dc, new Point(16, 10.5), 3.5, 6, thin, Fill(Color.FromRgb(0x6E, 0x9E, 0x36)));
                for (int i = 0; i < 4; i++)
                {
                    dc.DrawLine(thin, new Point(9, 18 + i * 2.6), new Point(i == 3 ? 17 : 23, 18 + i * 2.6));
                }

                break;

            case "detail":
                dc.DrawLine(main, new Point(2, 16), new Point(30, 16));
                dc.DrawRectangle(Fill(Color.FromRgb(0xE3, 0xD3, 0xB8)), main, new Rect(9, 16, 14, 12));
                dc.DrawEllipse(Fill(Color.FromRgb(0xA9, 0x8A, 0x63)), null, new Point(16, 20), 4, 3);
                dc.DrawLine(main, new Point(16, 17), new Point(16, 8));
                Crown(dc, new Point(16, 7), 5, 6, thin, light);
                break;

            case "section":
                dc.DrawLine(main, new Point(2, 27), new Point(30, 27));
                dc.DrawLine(main, new Point(9, 27), new Point(9, 17));
                dc.DrawEllipse(light, main, new Point(9, 12), 6, 7);
                dc.DrawLine(main, new Point(23, 27), new Point(23, 22));
                dc.DrawEllipse(Fill(Color.FromRgb(0x9B, 0x6B, 0xD3)), thin, new Point(23, 19), 5, 4);
                break;

            case "growth":
                dc.DrawLine(thin, new Point(2, 27), new Point(30, 27));
                dc.DrawEllipse(light, thin, new Point(6, 24), 2.5, 2.5);
                dc.DrawEllipse(light, thin, new Point(14, 21), 4.5, 4.5);
                dc.DrawEllipse(light, main, new Point(24, 16), 7, 8);
                Arrow(dc, new Point(4, 9), new Point(14, 4), accent);
                break;

            case "new-species":
                Crown(dc, new Point(13, 18), 10, 8, main, light);
                dc.DrawEllipse(green, null, new Point(13, 18), 1.8, 1.8);
                dc.DrawLine(accent, new Point(25, 3), new Point(25, 13));
                dc.DrawLine(accent, new Point(20, 8), new Point(30, 8));
                break;

            case "spec":
                dc.DrawRectangle(Fill(Colors.White), main, new Rect(3, 4, 26, 24));
                dc.DrawLine(main, new Point(3, 10), new Point(29, 10));
                dc.DrawLine(thin, new Point(10, 4), new Point(10, 28));
                dc.DrawLine(thin, new Point(13, 15), new Point(26, 15));
                dc.DrawLine(thin, new Point(13, 20), new Point(26, 20));
                dc.DrawLine(thin, new Point(13, 25), new Point(22, 25));
                dc.DrawEllipse(green, null, new Point(6.5, 16), 2, 2);
                break;

            case "paving":
            {
                Brush stone = Fill(Color.FromRgb(0xCF, 0xC8, 0xBA));
                dc.DrawRectangle(stone, main, new Rect(3, 4, 26, 24));
                dc.DrawLine(thin, new Point(3, 12), new Point(29, 12));
                dc.DrawLine(thin, new Point(3, 20), new Point(29, 20));
                dc.DrawLine(thin, new Point(12, 4), new Point(12, 12));
                dc.DrawLine(thin, new Point(21, 4), new Point(21, 12));
                dc.DrawLine(thin, new Point(7, 12), new Point(7, 20));
                dc.DrawLine(thin, new Point(16, 12), new Point(16, 20));
                dc.DrawLine(thin, new Point(25, 12), new Point(25, 20));
                dc.DrawLine(thin, new Point(12, 20), new Point(12, 28));
                dc.DrawLine(thin, new Point(21, 20), new Point(21, 28));
                break;
            }

            case "stones":
            {
                // Pedras arredondadas de tons diferentes.
                (double X, double Y, double R, byte Gray)[] stones =
                {
                    (9, 22, 6, 0xCF), (20, 23, 6.5, 0x9E), (14, 13, 5.5, 0xE6), (24, 12, 4.5, 0xB8), (7, 9, 3.5, 0x8A), (27, 26, 3, 0xD9),
                };
                foreach ((double x, double y, double r, byte g) in stones)
                {
                    dc.DrawEllipse(Fill(Color.FromRgb(g, (byte)(g - 6), (byte)(g - 14))), thin, new Point(x, y), r, r * 0.8);
                }

                break;
            }

            case "raingarden":
            {
                Brush water = Fill(Color.FromRgb(0x8E, 0xC3, 0xE6));
                dc.DrawGeometry(Fill(Color.FromRgb(0xA9, 0x8A, 0x63)), thin, Geometry.Parse("M 2,14 L 8,14 L 11,24 L 21,24 L 24,14 L 30,14 L 30,28 L 2,28 Z"));
                dc.DrawGeometry(water, null, Geometry.Parse("M 9,18 L 23,18 L 21,24 L 11,24 Z"));
                dc.DrawLine(main, new Point(16, 18), new Point(16, 9));
                dc.DrawLine(main, new Point(16, 12), new Point(12, 7));
                dc.DrawLine(main, new Point(16, 12), new Point(20, 7));
                dc.DrawLine(Stroke(Color.FromRgb(0x3A, 0x7B, 0xB8), 1.3, size), new Point(6, 3), new Point(5, 7));
                dc.DrawLine(Stroke(Color.FromRgb(0x3A, 0x7B, 0xB8), 1.3, size), new Point(26, 3), new Point(25, 7));
                break;
            }

            case "solar":
            {
                Brush sun = Fill(Color.FromRgb(0xF2, 0xB8, 0x2E));
                dc.DrawEllipse(sun, accent, new Point(10, 10), 5, 5);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    dc.DrawLine(accent, new Point(10 + Math.Cos(a) * 7, 10 + Math.Sin(a) * 7), new Point(10 + Math.Cos(a) * 9, 10 + Math.Sin(a) * 9));
                }

                dc.DrawGeometry(Fill(Color.FromArgb(0x70, 0x34, 0x45, 0x7A)), null, Geometry.Parse("M 18,28 L 30,28 L 30,22 L 24,19 Z"));
                dc.DrawLine(main, new Point(21, 28), new Point(21, 20));
                dc.DrawEllipse(light, main, new Point(21, 16), 5, 5);
                break;
            }

            case "model3d":
            {
                // Caixa isométrica com uma árvore em volume.
                Brush side = Fill(Color.FromRgb(0xE3, 0xD3, 0xB8));
                dc.DrawGeometry(side, thin, Geometry.Parse("M 3,22 L 16,28 L 29,22 L 16,16 Z"));
                dc.DrawLine(main, new Point(16, 22), new Point(16, 13));
                dc.DrawEllipse(green, dark, new Point(16, 10), 7, 7.5);
                dc.DrawEllipse(Fill(Color.FromRgb(0x6F, 0xA8, 0x7E)), null, new Point(13.5, 7.5), 2.6, 2.6);
                break;
            }

            case "model3d-update":
                dc.DrawLine(main, new Point(13, 26), new Point(13, 17));
                dc.DrawEllipse(light, main, new Point(13, 12), 7, 7);
                CircularArrows(dc, new Point(24, 23), 6, accent);
                break;

            case "model3d-test":
                // Árvore com uma prancheta de verificação.
                dc.DrawLine(main, new Point(11, 26), new Point(11, 17));
                dc.DrawEllipse(light, main, new Point(11, 12), 6.5, 6.5);
                dc.DrawRoundedRectangle(Fill(Colors.White), main, new Rect(18, 15, 11, 13), 1.5, 1.5);
                dc.DrawLine(accent, new Point(20, 21), new Point(22.5, 24));
                dc.DrawLine(accent, new Point(22.5, 24), new Point(27, 18));
                break;

            case "model3d-remove":
                dc.DrawLine(main, new Point(13, 26), new Point(13, 17));
                dc.DrawEllipse(light, main, new Point(13, 12), 7, 7);
                dc.DrawLine(accent, new Point(20, 19), new Point(29, 28));
                dc.DrawLine(accent, new Point(29, 19), new Point(20, 28));
                break;

            case "photos":
                dc.DrawRectangle(Fill(Colors.White), main, new Rect(3, 5, 15, 12));
                dc.DrawGeometry(light, thin, Geometry.Parse("M 4,16 L 8,11 L 11,14 L 14,10 L 17,16 Z"));
                dc.DrawRectangle(Fill(Colors.White), main, new Rect(14, 17, 15, 12));
                Crown(dc, new Point(21.5, 23), 4, 6, thin, light);
                dc.DrawLine(accent, new Point(10.5, 17), new Point(14, 21));
                break;

            case "budget":
                dc.DrawRoundedRectangle(Fill(Colors.White), main, new Rect(4, 4, 20, 24), 2, 2);
                dc.DrawLine(thin, new Point(8, 10), new Point(20, 10));
                dc.DrawLine(thin, new Point(8, 15), new Point(16, 15));
                dc.DrawEllipse(Fill(Accent), null, new Point(22, 22), 8, 8);
                Text(dc, "$", new Point(22, 22.5), 12, Colors.White, size);
                break;

            case "maintenance":
                dc.DrawRoundedRectangle(Fill(Colors.White), main, new Rect(3, 6, 22, 22), 2, 2);
                dc.DrawRectangle(green, null, new Rect(3, 6, 22, 5));
                dc.DrawLine(main, new Point(8, 3), new Point(8, 8));
                dc.DrawLine(main, new Point(20, 3), new Point(20, 8));
                for (int r = 0; r < 2; r++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        dc.DrawEllipse(light, null, new Point(8 + c * 6, 16 + r * 6), 1.6, 1.6);
                    }
                }

                Crown(dc, new Point(24, 23), 6, 6, main, light);
                break;

            case "sheet":
                dc.DrawRectangle(Fill(Colors.White), main, new Rect(2, 6, 28, 20));
                dc.DrawRectangle(null, thin, new Rect(4, 8, 16, 16));
                Crown(dc, new Point(12, 16), 5, 6, thin, light);
                dc.DrawLine(thin, new Point(22, 8), new Point(22, 24));
                dc.DrawLine(thin, new Point(22, 20), new Point(28, 20));
                dc.DrawLine(thin, new Point(23.5, 11), new Point(27, 11));
                dc.DrawLine(thin, new Point(23.5, 14), new Point(27, 14));
                break;

            case "cad":
                dc.DrawRoundedRectangle(light, main, new Rect(3, 4, 20, 24), 2, 2);
                Text(dc, "DWG", new Point(13, 16.5), 7.5, DarkGreen, size);
                Arrow(dc, new Point(18, 22), new Point(30, 22), accent);
                break;

            case "interference":
                Crown(dc, new Point(13, 14), 10, 8, main, light);
                dc.DrawLine(Stroke(Accent, 3, size), new Point(2, 26), new Point(30, 18));
                dc.DrawEllipse(Fill(Accent), null, new Point(24, 8), 5, 5);
                Text(dc, "!", new Point(24, 8.5), 8, Colors.White, size);
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
