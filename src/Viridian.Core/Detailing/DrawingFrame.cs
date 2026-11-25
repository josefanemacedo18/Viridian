using Viridian.Core.Geometry;

namespace Viridian.Core.Detailing;

/// <summary>Bloco de texto que acompanha um desenho: título do quadro e linhas (já quebradas).</summary>
public sealed record NoteBlock(string Heading, IReadOnlyList<string> Lines);

/// <summary>
/// Apresentação padrão de um desenho na prancha: faixa de título abaixo do desenho (número, título, escala e
/// subtítulo) e, abaixo dela, os quadros de texto (especificação, notas, memória de cálculo) lado a lado ou empilhados.
/// </summary>
public static class DrawingFrame
{
    /// <summary>Caracteres por linha de um quadro de texto com a largura dada (texto de <paramref name="textMm"/> mm).</summary>
    public static int CharsFor(double widthMm, double textMm) => Math.Max(24, (int)(widthMm / (textMm * 0.62)));

    /// <summary>
    /// Acrescenta ao desenho a faixa de título e os quadros. <paramref name="scale"/> é a escala de apresentação
    /// (1:scale) e <paramref name="textMm"/> a altura do texto no papel. Os quadros têm a largura do desenho (no
    /// mínimo 160 mm no papel) e ficam empilhados.
    /// </summary>
    public static void Compose(Drawing drawing, int scale, double textMm, string number, string title, string subtitle, IEnumerable<NoteBlock>? blocks = null)
    {
        double mm = Math.Max(1, scale) / 1000.0;
        Bounds2 e = drawing.Extent();
        double width = Math.Max(e.Width, 160 * mm);
        double y = e.MinY - 12 * mm;
        drawing.TitleBar(new Point2(e.MinX, y), width, mm, number, title,
            ("ESCALA 1:" + scale + (subtitle.Length > 0 ? "  ·  " + subtitle : string.Empty)).Trim());
        y -= 16 * mm;
        if (blocks is null)
        {
            return;
        }

        double lineMm = textMm * 2.1;
        int chars = CharsFor(width / mm - 6, textMm);
        foreach (NoteBlock block in blocks)
        {
            if (block.Lines.Count == 0)
            {
                continue;
            }

            var lines = block.Lines.SelectMany(l => TextLayout.Wrap(l, chars)).ToList();
            double height = drawing.TextPanel(new Point2(e.MinX, y), width, mm, block.Heading, lines, lineMm);
            y -= height + 5 * mm;
        }
    }
}
