using Viridian.Core.Colors;
using Viridian.Core.Geometry;

namespace Viridian.Core.Detailing;

/// <summary>Caneta (espessura e cor) de um traço. Cada uma vira um estilo de linha "VIR Traço ..." no Revit.</summary>
public enum LinePen
{
    /// <summary>Traço fino de desenho (contornos secundários, linhas de chamada).</summary>
    Fina = 0,

    /// <summary>Traço médio (contornos de elementos, divisões de camadas).</summary>
    Media = 1,

    /// <summary>Traço grosso (terreno, cortes, molduras, contornos principais).</summary>
    Grossa = 2,

    /// <summary>Traço leve de textura e sombreamento (folhas, veios, casca).</summary>
    Textura = 3,

    /// <summary>Cotas e réguas.</summary>
    Cota = 4,

    /// <summary>Água e fluxo (setas de escoamento, lâmina).</summary>
    Agua = 5,
}

/// <summary>Estilo de texto: cada um vira um tipo de texto "VIR Desenho ..." no Revit.</summary>
public enum TextStyle
{
    Normal = 0,

    /// <summary>Títulos: negrito, verde-escuro.</summary>
    Titulo = 1,

    /// <summary>Subtítulos e cabeçalhos: negrito.</summary>
    Subtitulo = 2,

    /// <summary>Notas e observações: cinza.</summary>
    Nota = 3,

    /// <summary>Nome científico: itálico (convenção botânica).</summary>
    Cientifico = 4,

    /// <summary>Texto claro sobre faixas escuras: negrito, branco.</summary>
    Claro = 5,
}

/// <summary>Traço de um desenho 2D gerado (detalhe, corte, crescimento), em metros reais.</summary>
/// <param name="Pen">Caneta; nula = fina ou grossa conforme <paramref name="Heavy"/>.</param>
public sealed record DrawLine(Point2 A, Point2 B, bool Dashed = false, bool Heavy = false, LinePen? Pen = null)
{
    public LinePen EffectivePen => Pen ?? (Heavy ? LinePen.Grossa : LinePen.Fina);
}

/// <summary>Texto do desenho. <paramref name="Align"/>: -1 esquerda, 0 centro, 1 direita.</summary>
public sealed record DrawText(Point2 At, string Text, int Align = -1, double SizeFactor = 1.0, TextStyle Style = TextStyle.Normal);

/// <summary>
/// Área preenchida (copa, solo, maciço): fundo na cor e, opcionalmente, a hachura de material
/// (<see cref="Hatch.MaterialHatches"/>) por cima, na cor <paramref name="PatternColor"/>.
/// </summary>
public sealed record DrawFill(IReadOnlyList<Point2> Outline, Rgb Color, string? Pattern = null, Rgb? PatternColor = null);

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

    public void Text(Point2 at, string text, TextStyle style, int align = -1, double size = 1.0) => Texts.Add(new DrawText(at, text, align, size, style));

    /// <summary>Traço com caneta escolhida.</summary>
    public void Stroke(Point2 a, Point2 b, LinePen pen, bool dashed = false)
    {
        if (a.DistanceTo(b) > 1e-6)
        {
            Lines.Add(new DrawLine(a, b, dashed, pen == LinePen.Grossa, pen));
        }
    }

    public void Strokes(IReadOnlyList<Point2> points, bool closed, LinePen pen, bool dashed = false)
    {
        for (int i = 1; i < points.Count; i++)
        {
            Stroke(points[i - 1], points[i], pen, dashed);
        }

        if (closed && points.Count > 2)
        {
            Stroke(points[^1], points[0], pen, dashed);
        }
    }

    /// <summary>Área com cor de fundo e, se informada, hachura de material por cima.</summary>
    public void Fill(IReadOnlyList<Point2> outline, Rgb color, string? pattern = null, Rgb? patternColor = null)
    {
        if (outline.Count >= 3)
        {
            Fills.Add(new DrawFill(outline, color, pattern, patternColor));
        }
    }

    /// <summary>Retângulo preenchido (e contornado, se <paramref name="edge"/> for informada).</summary>
    public void Box(double x0, double y0, double x1, double y1, Rgb color, string? pattern = null, Rgb? patternColor = null, LinePen? edge = null)
    {
        var outline = new List<Point2> { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) };
        Fill(outline, color, pattern, patternColor);
        if (edge is LinePen pen)
        {
            Strokes(outline, true, pen);
        }
    }

    public void Circle(Point2 center, double radius, LinePen pen, int sides = 36) => Strokes(Ellipse(center, radius, radius, sides), true, pen);

    /// <summary>Seta com ponta cheia.</summary>
    public void Arrow(Point2 from, Point2 to, LinePen pen, double head, Rgb headColor)
    {
        Point2 dir = (to - from).Normalized();
        Point2 back = to - dir * head;
        Stroke(from, back, pen);
        Point2 side = dir.Perp() * head * 0.42;
        Fill(new List<Point2> { to, back + side, back - side }, headColor);
    }

    /// <summary>
    /// Faixa de título do desenho, padrão de prancha: círculo com o número, título em negrito, traço grosso e, abaixo,
    /// a escala e um subtítulo. <paramref name="mm"/> é 1 mm do papel em metros do desenho (escala / 1000).
    /// </summary>
    public void TitleBar(Point2 at, double width, double mm, string number, string title, string subtitle)
    {
        double r = 5.5 * mm;
        Point2 c = at + new Point2(r, 0);
        Fills.Add(new DrawFill(Ellipse(c, r, r, 40), DrawingStyle.Primary));
        Text(c, number, TextStyle.Claro, 0, 1.25);
        double x = at.X + r * 2 + 3 * mm;
        Text(new Point2(x, at.Y + 2.2 * mm), title.ToUpperInvariant(), TextStyle.Titulo, -1, 1.45);
        Stroke(new Point2(x, at.Y - 0.6 * mm), new Point2(at.X + Math.Max(width, r * 2 + 60 * mm), at.Y - 0.6 * mm), LinePen.Grossa);
        if (subtitle.Length > 0)
        {
            Text(new Point2(x, at.Y - 3.6 * mm), subtitle, TextStyle.Nota, -1, 0.95);
        }
    }

    /// <summary>Quadro de texto: título numa faixa e linhas abaixo, com fundo claro e moldura.</summary>
    /// <returns>Altura ocupada (em metros do desenho).</returns>
    public double TextPanel(Point2 topLeft, double width, double mm, string title, IReadOnlyList<string> lines, double lineMm = 4.2)
    {
        double header = 7 * mm;
        double height = header + (lines.Count * lineMm + 4) * mm;
        Box(topLeft.X, topLeft.Y - height, topLeft.X + width, topLeft.Y, DrawingStyle.Paper, edge: LinePen.Media);
        Box(topLeft.X, topLeft.Y - header, topLeft.X + width, topLeft.Y, DrawingStyle.Primary);
        Text(new Point2(topLeft.X + 3 * mm, topLeft.Y - header / 2), title.ToUpperInvariant(), TextStyle.Claro, -1, 1.05);
        double y = topLeft.Y - header - 3.6 * mm;
        foreach (string line in lines)
        {
            Text(new Point2(topLeft.X + 3 * mm, y), line);
            y -= lineMm * mm;
        }

        return height;
    }

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
        Stroke(a + outward * Math.Abs(offset) * 0.15, p + outward * Math.Abs(offset) * 0.15, LinePen.Cota);
        Stroke(b + outward * Math.Abs(offset) * 0.15, q + outward * Math.Abs(offset) * 0.15, LinePen.Cota);
        Stroke(p, q, LinePen.Cota);
        double tick = Math.Clamp(a.DistanceTo(b) * 0.03, 0.015, 0.05);
        Point2 slash = (dir + dir.Perp()).Normalized() * tick;
        Stroke(p - slash, p + slash, LinePen.Media);
        Stroke(q - slash, q + slash, LinePen.Media);
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
            Stroke(target, elbow, LinePen.Fina);
            Stroke(elbow, new Point2(x - gap * 0.15, y), LinePen.Fina);
            Fills.Add(new DrawFill(Ellipse(target, gap * 0.12, gap * 0.12, 12), DrawingStyle.Accent));
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
