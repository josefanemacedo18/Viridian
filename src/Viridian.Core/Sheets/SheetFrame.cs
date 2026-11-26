using System.Globalization;
using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;

namespace Viridian.Core.Sheets;

/// <summary>Dados impressos no carimbo da Folha Viridian.</summary>
public sealed class SheetFrameInfo
{
    public string Project { get; set; } = string.Empty;

    public string ProjectNumber { get; set; } = string.Empty;

    public string Client { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Office { get; set; } = string.Empty;

    public string SheetName { get; set; } = string.Empty;

    public string SheetNumber { get; set; } = string.Empty;

    public string Date { get; set; } = string.Empty;

    /// <summary>Escala da planta (1:Scale); zero = sem escala gráfica.</summary>
    public int Scale { get; set; }

    /// <summary>Norte verdadeiro em graus anti-horários a partir do alto da folha.</summary>
    public double NorthDeg { get; set; }

    public IReadOnlyList<string> Notes { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Folha Viridian A1 (841 × 594 mm, deitada), desenhada pelo plugin: moldura com as margens da NBR 10068, coluna lateral
/// para legenda e tabela, faixa verde com folhagem, notas gerais, rosa dos ventos com o norte verdadeiro, escala gráfica,
/// quadro de revisões e carimbo com os dados do projeto e o número da prancha em destaque. Medidas em metros do papel.
/// </summary>
public static class SheetFrame
{
    public const double WidthMm = 841, HeightMm = 594;

    /// <summary>Largura mínima da coluna lateral (mm).</summary>
    public const double ColumnMm = 180;

    /// <summary>Altura do bloco inferior da coluna (carimbo, revisões, orientação e notas), em mm.</summary>
    public const double StampBlockMm = 236;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Moldura interna (dentro das margens), em mm.</summary>
    public static PaperRect Inner => new(25, 10, WidthMm - 10, HeightMm - 10);

    /// <summary>Área útil para a planta e a coluna (moldura com um respiro de 5 mm).</summary>
    public static PaperRect Usable => Inner.Inset(5, 5, 5, 5);

    /// <param name="columnMm">Largura da coluna lateral (no mínimo <see cref="ColumnMm"/>).</param>
    public static Drawing Build(SheetFrameInfo info, double columnMm = ColumnMm)
    {
        ArgumentNullException.ThrowIfNull(info);
        var g = new Drawing { Title = "Folha Viridian A1" };
        double M(double mm) => mm / 1000.0;
        Point2 P(double x, double y) => new(M(x), M(y));
        void BoxMm(double x0, double y0, double x1, double y1, Rgb color, LinePen? edge = null) => g.Box(M(x0), M(y0), M(x1), M(y1), color, edge: edge);
        void Line(double x0, double y0, double x1, double y1, LinePen pen) => g.Stroke(P(x0, y0), P(x1, y1), pen);

        PaperRect inner = Inner;
        double col = Math.Max(ColumnMm, columnMm);
        double cx0 = inner.MaxX - col, cx1 = inner.MaxX;

        // Limite da folha (fino) e moldura (grossa).
        g.Strokes(new List<Point2> { P(0, 0), P(WidthMm, 0), P(WidthMm, HeightMm), P(0, HeightMm) }, true, LinePen.Textura);
        g.Strokes(new List<Point2> { P(inner.MinX, inner.MinY), P(inner.MaxX, inner.MinY), P(inner.MaxX, inner.MaxY), P(inner.MinX, inner.MaxY) }, true, LinePen.Grossa);
        Line(cx0, inner.MinY, cx0, inner.MaxY, LinePen.Media);

        // Faixa do alto da coluna: título do projeto sobre o verde, com folhagem.
        double bandTop = inner.MaxY, bandBottom = bandTop - 26;
        BoxMm(cx0, bandBottom, cx1, bandTop, DrawingStyle.Primary);
        Foliage(g, P(cx1 - 26, bandBottom + 13), M(13));
        g.Text(P(cx0 + 5, bandTop - 8), "PROJETO DE PAISAGISMO", TextStyle.Claro, -1, 1.05);
        g.Text(P(cx0 + 5, bandTop - 17), Fit(info.Project.Length > 0 ? info.Project : "Projeto", 34), TextStyle.Claro, -1, 1.6);

        // Bloco inferior (de baixo para cima): carimbo, revisões, orientação e notas.
        double y = inner.MinY;

        // Número da prancha em destaque e campos do carimbo.
        double stampTop = y + 96;
        BoxMm(cx0, y, cx1, stampTop, DrawingStyle.Paper);
        double numberW = 52;
        BoxMm(cx1 - numberW, y, cx1, y + 34, DrawingStyle.Primary);
        g.Text(P(cx1 - numberW / 2, y + 27), "PRANCHA", TextStyle.Claro, 0, 0.85);
        g.Text(P(cx1 - numberW / 2, y + 14), info.SheetNumber.Length > 0 ? info.SheetNumber : "—", TextStyle.Claro, 0, 3.0);
        Field(g, cx0, y + 17, cx1 - numberW - cx0, 17, "ESCALA", info.Scale > 0 ? "1:" + info.Scale.ToString(Pt) : "indicada", 1.2);
        Field(g, cx0, y, (cx1 - numberW - cx0) * 0.5, 17, "DATA", info.Date, 1.0);
        Field(g, cx0 + (cx1 - numberW - cx0) * 0.5, y, (cx1 - numberW - cx0) * 0.5, 17, "FOLHA", "A1", 1.0);
        Field(g, cx0, y + 34, col, 16, "CONTEÚDO", Fit(info.SheetName, 44), 1.15);
        Field(g, cx0, y + 50, col, 12, "RESPONSÁVEL TÉCNICO", Fit(info.Author, 60), 0.95);
        Field(g, cx0, y + 62, col * 0.6, 12, "CLIENTE", Fit(info.Client, 34), 0.95);
        Field(g, cx0 + col * 0.6, y + 62, col * 0.4, 12, "Nº DO PROJETO", Fit(info.ProjectNumber, 20), 0.95);
        Field(g, cx0, y + 74, col, 12, "ENDEREÇO", Fit(info.Address, 60), 0.95);
        Field(g, cx0, y + 86, col, 10, "ESCRITÓRIO", Fit(info.Office.Length > 0 ? info.Office : "—", 60), 0.9);
        Line(cx0, stampTop, cx1, stampTop, LinePen.Grossa);

        // Revisões.
        double revTop = stampTop + 26;
        BoxMm(cx0, revTop - 6, cx1, revTop, DrawingStyle.Tint);
        g.Text(P(cx0 + 3, revTop - 3), "REV.", TextStyle.Subtitulo, -1, 0.8);
        g.Text(P(cx0 + 18, revTop - 3), "DATA", TextStyle.Subtitulo, -1, 0.8);
        g.Text(P(cx0 + 45, revTop - 3), "DESCRIÇÃO", TextStyle.Subtitulo, -1, 0.8);
        for (int i = 1; i <= 3; i++)
        {
            Line(cx0, revTop - 6 - i * 6.6, cx1, revTop - 6 - i * 6.6, LinePen.Textura);
        }

        Line(cx0 + 15, stampTop, cx0 + 15, revTop, LinePen.Textura);
        Line(cx0 + 42, stampTop, cx0 + 42, revTop, LinePen.Textura);
        Line(cx0, revTop, cx1, revTop, LinePen.Media);

        // Orientação: rosa dos ventos e escala gráfica.
        double orientTop = revTop + 52;
        g.Text(P(cx0 + 4, orientTop - 5), "ORIENTAÇÃO E ESCALA", TextStyle.Titulo, -1, 0.9);
        NorthArrow(g, P(cx0 + 26, revTop + 22), M(15), info.NorthDeg);
        if (info.Scale > 0)
        {
            ScaleBar(g, cx0 + 52, revTop + 16, Math.Min(110, col - 60), info.Scale);
        }

        Line(cx0, orientTop, cx1, orientTop, LinePen.Media);

        // Notas gerais.
        double notesTop = inner.MinY + StampBlockMm;
        g.Text(P(cx0 + 4, notesTop - 5), "NOTAS GERAIS", TextStyle.Titulo, -1, 0.9);
        double ny = notesTop - 11;
        int number = 1;
        foreach (string note in info.Notes)
        {
            foreach ((string line, int k) in TextLayout.Wrap(note, (int)(col / 2.0)).Select((l, k) => (l, k)))
            {
                if (ny < orientTop + 3)
                {
                    break;
                }

                g.Text(P(cx0 + 4, ny), (k == 0 ? number + ". " : "    ") + line, TextStyle.Normal, -1, 0.85);
                ny -= 4.2;
            }

            number++;
        }

        Line(cx0, notesTop, cx1, notesTop, LinePen.Media);
        g.Text(P(inner.MinX + 2, inner.MinY - 6), "Viridian — paisagismo para Revit", TextStyle.Nota, -1, 0.75);
        return g;
    }

    /// <summary>Notas gerais usuais da prancha de paisagismo.</summary>
    public static IReadOnlyList<string> DefaultNotes => new[]
    {
        "Medidas em metros, salvo indicação.",
        "Espécies, quantidades e especificações conforme a legenda e o quadro de especificação.",
        "Locar as covas em campo e confirmar as interferências com redes antes do plantio.",
        "Mudas conforme o padrão especificado; substituições só com aprovação do autor do projeto.",
    };

    /// <summary>Unidade da escala gráfica (m) para a escala da planta: 4 divisões com 40 a 100 mm no papel.</summary>
    public static double ScaleUnit(int scale)
    {
        foreach (double u in new[] { 0.5, 1, 2, 2.5, 5, 10, 20, 25, 50, 100, 200, 500 })
        {
            if (u * 4 * 1000 / Math.Max(1, scale) >= 40)
            {
                return u;
            }
        }

        return 1000;
    }

    private static void Field(Drawing g, double x, double y, double w, double h, string label, string value, double size)
    {
        g.Strokes(new List<Point2> { new(x / 1000, y / 1000), new((x + w) / 1000, y / 1000), new((x + w) / 1000, (y + h) / 1000), new(x / 1000, (y + h) / 1000) }, true, LinePen.Textura);
        g.Text(new Point2((x + 2.5) / 1000, (y + h - 3) / 1000), label, TextStyle.Nota, -1, 0.7);
        g.Text(new Point2((x + 2.5) / 1000, (y + Math.Max(3.5, h * 0.35)) / 1000), value.Length > 0 ? value : "—", TextStyle.Subtitulo, -1, size);
    }

    /// <summary>Rosa dos ventos: círculo, seta dividida em duas cores apontando o norte verdadeiro e a letra N.</summary>
    private static void NorthArrow(Drawing g, Point2 c, double r, double northDeg)
    {
        double a = northDeg * Math.PI / 180;
        Point2 Rot(double x, double y) => c + new Point2(x * Math.Cos(a) - y * Math.Sin(a), x * Math.Sin(a) + y * Math.Cos(a));
        g.Fill(Drawing.Ellipse(c, r, r, 48), DrawingStyle.Paper);
        g.Circle(c, r, LinePen.Media, 48);
        g.Circle(c, r * 0.78, LinePen.Textura, 48);
        for (int i = 0; i < 4; i++)
        {
            double t = a + i * Math.PI / 2;
            Point2 dir = new(-Math.Sin(t), Math.Cos(t));
            g.Stroke(c + dir * r * 0.78, c + dir * r * 1.08, LinePen.Media);
        }

        g.Fill(new List<Point2> { Rot(0, r * 0.95), Rot(-r * 0.28, -r * 0.55), Rot(0, -r * 0.3) }, DrawingStyle.Primary);
        g.Fill(new List<Point2> { Rot(0, r * 0.95), Rot(r * 0.28, -r * 0.55), Rot(0, -r * 0.3) }, Rgb.White);
        g.Strokes(new List<Point2> { Rot(0, r * 0.95), Rot(r * 0.28, -r * 0.55), Rot(0, -r * 0.3), Rot(-r * 0.28, -r * 0.55) }, true, LinePen.Media);
        g.Text(Rot(0, r * 1.32), "N", TextStyle.Titulo, 0, 1.3);
    }

    /// <summary>Escala gráfica: quatro divisões alternadas (verde e branco) com os valores em metros.</summary>
    private static void ScaleBar(Drawing g, double x, double y, double maxWidthMm, int scale)
    {
        double unit = ScaleUnit(scale);
        double segment = unit * 1000 / scale;
        if (segment * 4 > maxWidthMm)
        {
            segment = maxWidthMm / 4;
            unit = segment * scale / 1000;
        }

        for (int i = 0; i < 4; i++)
        {
            g.Box((x + i * segment) / 1000, y / 1000, (x + (i + 1) * segment) / 1000, (y + 3) / 1000, i % 2 == 0 ? DrawingStyle.Primary : Rgb.White, edge: LinePen.Fina);
            g.Text(new Point2((x + i * segment) / 1000, (y - 3.5) / 1000), Num(unit * i), TextStyle.Normal, 0, 0.8);
        }

        g.Text(new Point2((x + 4 * segment) / 1000, (y - 3.5) / 1000), Num(unit * 4) + " m", TextStyle.Normal, 0, 0.8);
        g.Text(new Point2(x / 1000, (y + 8) / 1000), "ESCALA GRÁFICA 1:" + scale.ToString(Pt), TextStyle.Nota, -1, 0.8);
    }

    private static string Num(double v) => v.ToString(v % 1 == 0 ? "0" : "0.#", Pt);

    /// <summary>Ramo de folhas claras sobre a faixa verde (identidade de paisagismo).</summary>
    private static void Foliage(Drawing g, Point2 c, double size)
    {
        var light = new Rgb(0xA9, 0xC8, 0x8F);
        var pale = new Rgb(0xE3, 0xEC, 0xDC);
        Leaf(g, c + new Point2(-size * 0.2, -size * 0.15), size * 1.15, size * 0.42, 0.7, light);
        Leaf(g, c + new Point2(size * 0.1, size * 0.05), size * 1.0, size * 0.38, 1.35, pale);
        Leaf(g, c + new Point2(size * 0.35, -size * 0.35), size * 0.85, size * 0.34, -0.15, light.Tint(0.25));
        g.Stroke(c + new Point2(-size * 0.75, -size * 0.85), c + new Point2(size * 0.3, size * 0.2), LinePen.Textura);
    }

    /// <summary>Folha lanceolada com a nervura central.</summary>
    private static void Leaf(Drawing g, Point2 c, double length, double width, double angle, Rgb color)
    {
        var points = new List<Point2>();
        for (int i = 0; i <= 16; i++)
        {
            double t = i / 16.0;
            points.Add(new Point2(-length / 2 + length * t, width / 2 * Math.Sin(Math.PI * t) * (1 - 0.25 * t)));
        }

        for (int i = 15; i >= 1; i--)
        {
            double t = i / 16.0;
            points.Add(new Point2(-length / 2 + length * t, -width / 2 * Math.Sin(Math.PI * t) * (1 - 0.25 * t)));
        }

        Point2 Rot(Point2 p) => c + new Point2(p.X * Math.Cos(angle) - p.Y * Math.Sin(angle), p.X * Math.Sin(angle) + p.Y * Math.Cos(angle));
        g.Fill(points.Select(Rot).ToList(), color);
        g.Stroke(Rot(new Point2(-length / 2, 0)), Rot(new Point2(length / 2, 0)), LinePen.Textura);
    }

    /// <summary>Corta um texto longo com reticências para caber no campo.</summary>
    private static string Fit(string text, int max) => text.Length <= max ? text : text[..Math.Max(1, max - 1)].TrimEnd() + "…";
}
