using Autodesk.Revit.DB;
using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>
/// Vistas de desenho geradas pelo Viridian: detalhes de plantio com notas de especificação, cortes esquemáticos,
/// simulação de crescimento e calendário de floração. Refazer uma vista substitui o conteúdo anterior.
/// </summary>
public sealed class DesignService
{
    private readonly double _textMm;

    public DesignService(double textMm = 2.0)
    {
        _textMm = textMm;
    }

    /// <summary>Cria (ou refaz) uma vista de desenho com o desenho informado, na escala pedida. Precisa de transação.</summary>
    public ViewDrafting DrawingView(Document document, string name, Drawing drawing, int scale)
    {
        ViewDrafting view = DocumentationService.FindOrCreateDraftingView(document, name);
        DocumentationService.ClearView(document, view);
        if (view.Scale != scale)
        {
            view.Scale = Math.Clamp(scale, 1, 24000);
        }

        new DrawingRenderer(document, view, _textMm).Render(drawing, Point2.Zero);
        return view;
    }

    /// <summary>
    /// Detalhe de plantio de uma espécie (ou de um grupo de espécies do mesmo tipo de plantio), com as notas de
    /// especificação abaixo do desenho. O desenho usa a espécie de maior muda do grupo. Precisa de transação.
    /// </summary>
    public ViewDrafting PlantingDetailView(Document document, IReadOnlyList<(Species Species, string Code)> group, SpecificationSettings specs, ColorSettings colors)
    {
        if (group.Count == 0)
        {
            throw new ViridianException("Nenhuma espécie para o detalhe.");
        }

        (Species main, string mainCode) = group.OrderByDescending(g => PlantingDetails.Dimensions(g.Species).SeedlingHeightM).First();
        DetailKind kind = PlantingDetails.KindFor(main.Category);
        Species labeled = main.Clone();
        labeled.Code = mainCode;
        Drawing drawing = PlantingDetails.Draw(labeled, colors);
        Bounds2 extent = drawing.Extent();
        const int scale = 20;
        double line = _textMm * 1.8 * scale / 1000.0; // altura de uma linha de texto no modelo

        // Título e notas abaixo do desenho.
        double y = extent.MinY - line * 2;
        drawing.Text(new Point2(extent.MinX, extent.MaxY + line * 2), PlantingDetails.Name(kind).ToUpperInvariant() + (group.Count == 1 ? " — " + mainCode + " " + main.PopularName : string.Empty), -1, 1.4);
        drawing.Text(new Point2(extent.MinX, y), "ESPECIFICAÇÃO", -1, 1.15);
        y -= line * 1.5;
        foreach (string paragraph in Wrap(specs.General, 110))
        {
            drawing.Text(new Point2(extent.MinX, y), paragraph);
            y -= line;
        }

        y -= line * 0.5;
        foreach ((Species s, string code) in group.OrderBy(g => g.Code, StringComparer.OrdinalIgnoreCase))
        {
            Species withCode = s.Clone();
            withCode.Code = code;
            foreach (string paragraph in Wrap(SpecificationTemplates.Render(specs, withCode), 110))
            {
                drawing.Text(new Point2(extent.MinX, y), paragraph);
                y -= line;
            }

            y -= line * 0.5;
        }

        drawing.Text(new Point2(extent.MinX, y - line * 0.5), "Dimensões de referência: confira com as normas locais e com o responsável técnico.", -1, 0.85);
        string name = group.Count == 1
            ? $"Viridian - Detalhe - {mainCode} {main.PopularName}"
            : $"Viridian - Detalhe - {PlantingDetails.Name(kind)}";
        return DrawingView(document, name, drawing, scale);
    }

    /// <summary>Quebra o texto em linhas de até <paramref name="width"/> caracteres, sem cortar palavras.</summary>
    public static IEnumerable<string> Wrap(string text, int width)
    {
        foreach (string paragraph in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var line = new System.Text.StringBuilder();
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    yield return line.ToString();
                    line.Clear();
                }

                if (line.Length > 0)
                {
                    line.Append(' ');
                }

                line.Append(word);
            }

            if (line.Length > 0)
            {
                yield return line.ToString();
            }
        }
    }

    /// <summary>Escala usual que faz o desenho caber em cerca de 50 cm de largura no papel.</summary>
    public static int FitScale(Drawing drawing, double paperWidthM = 0.5)
    {
        Bounds2 e = drawing.Extent();
        double width = Math.Max(e.Width, e.Height * 1.4);
        foreach (int s in new[] { 20, 25, 50, 75, 100, 125, 200, 250, 500, 1000, 2000 })
        {
            if (width / s <= paperWidthM)
            {
                return s;
            }
        }

        return 2000;
    }
}
