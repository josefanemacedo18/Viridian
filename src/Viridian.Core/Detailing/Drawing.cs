using Viridian.Core.Colors;
using Viridian.Core.Geometry;

namespace Viridian.Core.Detailing;

/// <summary>Traço de um desenho 2D gerado (detalhe, corte, crescimento), em metros reais.</summary>
public sealed record DrawLine(Point2 A, Point2 B, bool Dashed = false, bool Heavy = false);

/// <summary>Texto do desenho. <paramref name="Align"/>: -1 esquerda, 0 centro, 1 direita.</summary>
public sealed record DrawText(Point2 At, string Text, int Align = -1, double SizeFactor = 1.0);

/// <summary>Área preenchida (copa, solo, maciço), com cor.</summary>
public sealed record DrawFill(IReadOnlyList<Point2> Outline, Rgb Color);

/// <summary>Desenho 2D independente do Revit: linhas, textos e preenchimentos, em metros reais.</summary>
public sealed class Drawing
{
    public string Title { get; set; } = string.Empty;

    public List<DrawFill> Fills { get; } = new();

    public List<DrawLine> Lines { get; } = new();

    public List<DrawText> Texts { get; } = new();

    public void Line(Point2 a, Point2 b, bool dashed = false, bool heavy = false)
    {
        if (a.DistanceTo(b) > 1e-6)
        {
            Lines.Add(new DrawLine(a, b, dashed, heavy));
        }
    }

    public void Polyline(IReadOnlyList<Point2> points, bool closed, bool dashed = false, bool heavy = false)
    {
        for (int i = 1; i < points.Count; i++)
        {
            Line(points[i - 1], points[i], dashed, heavy);
        }

        if (closed && points.Count > 2)
        {
            Line(points[^1], points[0], dashed, heavy);
        }
    }

    public void Text(Point2 at, string text, int align = -1, double size = 1.0) => Texts.Add(new DrawText(at, text, align, size));

    /// <summary>
    /// Cota: linha com marcas nas pontas e o valor. Em cotas horizontais o valor fica sobre a linha; em cotas
    /// verticais, ao lado dela (texto sempre na horizontal, para leitura direta na prancha).
    /// </summary>
    public void Dimension(Point2 a, Point2 b, double offset, string text)
    {
        Point2 dir = (b - a).Normalized();
        Point2 normal = dir.Perp() * offset;
        Point2 p = a + normal;
        Point2 q = b + normal;
        Point2 outward = normal.Normalized();
        Line(a + outward * Math.Abs(offset) * 0.15, p + outward * Math.Abs(offset) * 0.15);
        Line(b + outward * Math.Abs(offset) * 0.15, q + outward * Math.Abs(offset) * 0.15);
        Line(p, q);
        double tick = Math.Clamp(a.DistanceTo(b) * 0.03, 0.015, 0.05);
        Point2 slash = (dir + dir.Perp()).Normalized() * tick;
        Line(p - slash, p + slash, heavy: true);
        Line(q - slash, q + slash, heavy: true);
        Point2 mid = (p + q) / 2;
        bool vertical = Math.Abs(dir.Y) > Math.Abs(dir.X);
        if (vertical)
        {
            // Texto ao lado da linha de cota, do lado de fora do desenho.
            bool right = outward.X > 0;
            Text(mid + new Point2(right ? tick * 2 : -tick * 2, 0), text, right ? -1 : 1, 0.9);
        }
        else
        {
            Text(mid + (outward.Y >= 0 ? new Point2(0, tick * 2.2) : new Point2(0, -tick * 2.2)), text, 0, 0.9);
        }
    }

    /// <summary>
    /// Coluna de notas com linhas de chamada: cada nota aponta para um ponto do desenho. As notas são
    /// afastadas verticalmente para nunca se sobreporem, mantendo a ordem de cima para baixo.
    /// </summary>
    /// <param name="x">Posição horizontal da coluna de textos.</param>
    /// <param name="gap">Espaço vertical mínimo entre notas (altura de uma linha de texto no modelo).</param>
    public void Notes(IEnumerable<(Point2 Target, string Text)> notes, double x, double gap)
    {
        double? previous = null;
        foreach ((Point2 target, string text) in notes.OrderByDescending(n => n.Target.Y))
        {
            double y = previous is double prev ? Math.Min(target.Y, prev - gap) : target.Y;
            previous = y;
            Point2 elbow = new(x - gap * 0.6, y);
            Line(target, elbow);
            Line(elbow, new Point2(x - gap * 0.15, y));
            Fills.Add(new DrawFill(Ellipse(target, gap * 0.12, gap * 0.12, 12), new Rgb(0x2E, 0x4A, 0x36)));
            Text(new Point2(x, y), text);
        }
    }

    /// <summary>Elipse aproximada por polígono.</summary>
    public static List<Point2> Ellipse(Point2 center, double rx, double ry, int sides = 48)
    {
        var points = new List<Point2>(sides);
        for (int i = 0; i < sides; i++)
        {
            double t = 2 * Math.PI * i / sides;
            points.Add(new Point2(center.X + rx * Math.Cos(t), center.Y + ry * Math.Sin(t)));
        }

        return points;
    }

    public Bounds2 Extent()
    {
        IEnumerable<Point2> points = Lines.SelectMany(l => new[] { l.A, l.B })
            .Concat(Fills.SelectMany(f => f.Outline))
            .Concat(Texts.Select(t => t.At));
        return points.Any() ? Bounds2.FromPoints(points) : new Bounds2(0, 0, 0, 0);
    }
}

/// <summary>Quebra de texto por número de caracteres, sem cortar palavras.</summary>
public static class TextLayout
{
    public static IEnumerable<string> Wrap(string? text, int width)
    {
        width = Math.Max(8, width);
        foreach (string paragraph in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var line = new System.Text.StringBuilder();
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string piece = word;
                while (piece.Length > width)
                {
                    // Palavra maior que a linha (códigos, endereços): corta para não estourar a coluna.
                    if (line.Length > 0)
                    {
                        yield return line.ToString();
                        line.Clear();
                    }

                    yield return piece[..width];
                    piece = piece[width..];
                }

                if (line.Length > 0 && line.Length + 1 + piece.Length > width)
                {
                    yield return line.ToString();
                    line.Clear();
                }

                if (line.Length > 0)
                {
                    line.Append(' ');
                }

                line.Append(piece);
            }

            if (line.Length > 0)
            {
                yield return line.ToString();
            }
        }
    }
}
