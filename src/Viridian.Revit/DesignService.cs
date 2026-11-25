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
        const int scale = 20;
        var specLines = new List<string> { specs.General };
        foreach ((Species s, string code) in group.OrderBy(g => g.Code, StringComparer.OrdinalIgnoreCase))
        {
            Species withCode = s.Clone();
            withCode.Code = code;
            specLines.Add(SpecificationTemplates.Render(specs, withCode));
        }

        string title = PlantingDetails.Name(kind) + (group.Count == 1 ? " — " + mainCode + " " + main.PopularName : string.Empty);
        PlantingDimensions dims = PlantingDetails.Dimensions(main);
        DrawingFrame.Compose(drawing, scale, _textMm, ((int)kind + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), title,
            "escavação " + PlantingDetails.ExcavationText(dims),
            new[]
            {
                new NoteBlock("Especificação", specLines),
                new NoteBlock("Observação", new[] { "Dimensões de referência: confira com as normas locais e com o responsável técnico." }),
            });
        string name = group.Count == 1
            ? $"Viridian - Detalhe - {mainCode} {main.PopularName}"
            : $"Viridian - Detalhe - {PlantingDetails.Name(kind)}";
        return DrawingView(document, name, drawing, scale);
    }

    /// <summary>Quebra o texto em linhas de até <paramref name="width"/> caracteres, sem cortar palavras.</summary>
    public static IEnumerable<string> Wrap(string text, int width) => TextLayout.Wrap(text, width);

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
