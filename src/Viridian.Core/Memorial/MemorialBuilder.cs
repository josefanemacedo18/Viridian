using System.Globalization;
using Viridian.Core.Colors;
using Viridian.Core.Composition;
using Viridian.Core.Detailing;
using Viridian.Core.Export;
using Viridian.Core.Maintenance;
using Viridian.Core.Model;

namespace Viridian.Core.Memorial;

/// <summary>Dados do projeto para a capa do memorial.</summary>
public sealed class MemorialProject
{
    public string Name { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;

    public string Client { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public DateTime Date { get; set; } = DateTime.Today;
}

/// <summary>Espécie do projeto, com quantidade e fotografia (caminho completo, vazio quando não há).</summary>
public sealed record MemorialSpecies(Species Species, string Code, double Quantity, string Unit, string PhotoFile);

public sealed class MemorialInput
{
    public MemorialProject Project { get; set; } = new();

    public IReadOnlyList<MemorialSpecies> Species { get; set; } = Array.Empty<MemorialSpecies>();

    public IReadOnlyList<MaintenanceTask> Maintenance { get; set; } = Array.Empty<MaintenanceTask>();

    public SpecificationSettings Specifications { get; set; } = new();

    public ColorSettings Colors { get; set; } = new();

    public double MassingAreaM2 { get; set; }
}

/// <summary>
/// Memorial descritivo ilustrado de paisagismo, editável no Word: capa, partido e composição, paleta vegetal,
/// fichas com fotografia, calendário de floração, especificações de plantio, manutenção e créditos das fotos.
/// </summary>
public static class MemorialBuilder
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static IReadOnlyList<DocBlock> Build(MemorialInput input)
    {
        input.Specifications ??= new SpecificationSettings();
        input.Specifications.Normalize();
        List<MemorialSpecies> species = input.Species
            .Where(s => s.Quantity > 0)
            .OrderBy(s => (int)s.Species.Category)
            .ThenBy(s => s.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var blocks = new List<DocBlock>();

        Cover(blocks, input.Project, species);
        blocks.Add(new DocPageBreak());
        Overview(blocks, species, input.MassingAreaM2);
        PaletteTable(blocks, species);
        blocks.Add(new DocPageBreak());
        SpeciesSheets(blocks, species, input.Colors);
        blocks.Add(new DocPageBreak());
        Calendar(blocks, species, input.Colors);
        Specifications(blocks, species, input.Specifications);
        Maintenance(blocks, input.Maintenance);
        Credits(blocks, species);
        return blocks;
    }

    private static void Cover(List<DocBlock> b, MemorialProject p, IReadOnlyList<MemorialSpecies> species)
    {
        b.Add(new DocParagraph("Memorial descritivo de paisagismo", DocStyle.Title));
        b.Add(new DocParagraph(string.IsNullOrWhiteSpace(p.Name) ? "Projeto de paisagismo" : p.Name, DocStyle.Subtitle));

        // Mosaico de fotos da paleta na capa (até seis espécies com foto).
        List<MemorialSpecies> photos = species.Where(s => File.Exists(s.PhotoFile)).Take(6).ToList();
        if (photos.Count > 0)
        {
            var row = photos.Select(s => new DocCell(new DocBlock[] { new DocImage(s.PhotoFile, 2.6, true), new DocParagraph(new[] { new DocRun(s.Species.PopularName, SizePt: 8) }, DocStyle.Caption, true) })).ToList();
            b.Add(new DocTable(photos.Select(_ => 1.0).ToList(), new[] { row }, HeaderRow: false, Borders: false));
        }

        var info = new List<IReadOnlyList<DocCell>>();
        void Line(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                info.Add(new[] { new DocCell(label, bold: true), new DocCell(value) });
            }
        }

        Line("Projeto", p.Name);
        Line("Número", p.Number);
        Line("Cliente", p.Client);
        Line("Endereço", p.Address);
        Line("Responsável técnico", p.Author);
        Line("Data", p.Date.ToString("d 'de' MMMM 'de' yyyy", Pt));
        if (info.Count > 0)
        {
            b.Add(new DocTable(new[] { 1.0, 3.0 }, info, HeaderRow: false, Borders: false));
        }
    }

    private static void Overview(List<DocBlock> b, IReadOnlyList<MemorialSpecies> species, double massingArea)
    {
        b.Add(new DocParagraph("1. Partido e composição", DocStyle.Heading1));
        if (species.Count == 0)
        {
            b.Add(new DocParagraph("O projeto ainda não possui vegetação do catálogo."));
            return;
        }

        int natives = species.Count(s => s.Species.Origin == PlantOrigin.Nativa);
        double units = species.Where(s => !IsArea(s.Unit)).Sum(s => s.Quantity);
        b.Add(new DocParagraph(string.Format(Pt,
            "O projeto reúne {0} espécies, das quais {1} nativas do Brasil ({2:0}%), em {3:N0} exemplares e mudas{4}.",
            species.Count, natives, 100.0 * natives / species.Count, units,
            massingArea > 0 ? string.Format(Pt, " e {0:N1} m² de maciços e forrações", massingArea) : string.Empty)));

        string Group<T>(Func<Species, T> key, Func<T, string> label) => string.Join("; ",
            species.GroupBy(s => key(s.Species)).OrderBy(g => g.Key).Select(g => $"{label(g.Key)}: {g.Count()}"));
        b.Add(new DocParagraph(new[] { new DocRun("Categorias — ", true), new DocRun(Group(s => s.Category, Labels.Of)) }));
        b.Add(new DocParagraph(new[] { new DocRun("Texturas — ", true), new DocRun(Group(s => s.EffectiveTexture, Labels.Of)) }));
        b.Add(new DocParagraph(new[] { new DocRun("Escala visual — ", true), new DocRun(Group(s => s.Scale, Labels.Of)) }));

        FloweringCalendar calendar = FloweringCalendar.Build(species.Select(s => (s.Species, s.Code, s.Quantity)));
        MonthSet gaps = calendar.ShowyGaps;
        string flowering = calendar.Rows.Any(r => r.Showy)
            ? gaps.IsEmpty ? "Há floração vistosa em todos os meses do ano." : $"Floração vistosa ao longo do ano, exceto em: {gaps}."
            : "A composição é baseada em folhagens, sem floração vistosa.";
        b.Add(new DocParagraph(new[] { new DocRun("Floração — ", true), new DocRun(flowering) }));
    }

    private static void PaletteTable(List<DocBlock> b, IReadOnlyList<MemorialSpecies> species)
    {
        b.Add(new DocParagraph("2. Paleta vegetal", DocStyle.Heading1));
        var rows = new List<IReadOnlyList<DocCell>>
        {
            new[] { "Código", "Nome popular", "Nome científico", "Categoria", "Origem", "Altura", "Copa", "Textura", "Qtd." }.Select(h => new DocCell(h)).ToList(),
        };
        foreach (MemorialSpecies s in species)
        {
            rows.Add(new[]
            {
                new DocCell(s.Code),
                new DocCell(s.Species.PopularName),
                new DocCell(new DocBlock[] { new DocParagraph(new[] { new DocRun(s.Species.ScientificName, Italic: true) }) }),
                new DocCell(Labels.Of(s.Species.Category)),
                new DocCell(Labels.Of(s.Species.Origin)),
                new DocCell(Meters(s.Species.AdultHeightM)),
                new DocCell(Meters(s.Species.CrownDiameterM)),
                new DocCell(Labels.Of(s.Species.EffectiveTexture)),
                new DocCell(Quantity(s)),
            });
        }

        b.Add(new DocTable(new[] { 0.9, 1.6, 2.1, 1.15, 0.9, 0.75, 0.75, 0.85, 1.0 }, rows, FontPt: 9));
    }

    private static void SpeciesSheets(List<DocBlock> b, IReadOnlyList<MemorialSpecies> species, ColorSettings colors)
    {
        b.Add(new DocParagraph("3. Fichas das espécies", DocStyle.Heading1));
        foreach (MemorialSpecies m in species)
        {
            Species s = m.Species;
            b.Add(new DocParagraph($"{m.Code} · {s.PopularName}", DocStyle.Heading2));

            var data = new List<DocBlock>
            {
                new DocParagraph(new[] { new DocRun(s.ScientificName, Italic: true), new DocRun(string.IsNullOrWhiteSpace(s.BotanicalFamily) ? string.Empty : " — " + s.BotanicalFamily) }),
                Field("Categoria e origem", $"{Labels.Of(s.Category)}, {Labels.Of(s.Origin).ToLowerInvariant()}" + (s.Biomes == Biome.Nenhum ? string.Empty : $" ({Labels.Of(s.Biomes)})")),
                Field("Porte adulto", $"altura {Meters(s.AdultHeightM)}, copa {Meters(s.CrownDiameterM)} — escala {Labels.Of(s.Scale).ToLowerInvariant()}"),
                Field("Folhagem", $"textura {Labels.Of(s.EffectiveTexture).ToLowerInvariant()}, cor {PaletteStudio.Describe(VegetationPalette.Foliage(s, colors))}"),
                Field("Floração", string.IsNullOrWhiteSpace(s.Flowering) ? "pouco vistosa" : s.Flowering + (Rgb.TryParse(s.FlowerColor, out Rgb f) ? $", cor {PaletteStudio.Describe(f)}" : string.Empty)),
                Field("Cultivo", $"luz {Labels.Of(s.Light).ToLowerInvariant()}, água {Labels.Of(s.Water).ToLowerInvariant()}, crescimento {Labels.Of(s.Growth).ToLowerInvariant()}"),
                Field("No projeto", Quantity(m)),
            };
            if (!string.IsNullOrWhiteSpace(s.Notes))
            {
                data.Add(Field("Observações", s.Notes));
            }

            var photo = new List<DocBlock>();
            if (File.Exists(m.PhotoFile))
            {
                photo.Add(new DocImage(m.PhotoFile, 5.5, true));
                if (!string.IsNullOrWhiteSpace(s.PhotoCredit))
                {
                    photo.Add(new DocParagraph(new[] { new DocRun($"Foto: {s.PhotoCredit}" + (string.IsNullOrWhiteSpace(s.PhotoLicense) ? string.Empty : $" · {s.PhotoLicense}")) }, DocStyle.Caption, true));
                }
            }
            else
            {
                photo.Add(new DocParagraph("(sem fotografia)", DocStyle.Caption, true));
            }

            b.Add(new DocTable(new[] { 1.0, 1.7 }, new[] { new[] { new DocCell(photo), new DocCell(data) } }, HeaderRow: false, Borders: false));
        }
    }

    private static void Calendar(List<DocBlock> b, IReadOnlyList<MemorialSpecies> species, ColorSettings colors)
    {
        b.Add(new DocParagraph("4. Calendário de floração", DocStyle.Heading1));
        FloweringCalendar calendar = FloweringCalendar.Build(species.Select(s => (s.Species, s.Code, s.Quantity)));
        var header = new List<DocCell> { new("Espécie") };
        header.AddRange(MonthSet.Abbreviations.Select(m => new DocCell(m)));
        var rows = new List<IReadOnlyList<DocCell>> { header };
        foreach (CalendarRow row in calendar.Rows.Where(r => !r.Unknown))
        {
            string fill = Rgb.TryParse(row.Species.FlowerColor, out Rgb flower) ? flower.Hex : VegetationPalette.CrownFill(row.Species, colors).Hex;
            var cells = new List<DocCell> { new($"{row.Code} {row.Species.PopularName}") };
            for (int m = 1; m <= 12; m++)
            {
                cells.Add(row.Months.Contains(m) ? new DocCell(string.Empty, fill) : new DocCell(string.Empty));
            }

            rows.Add(cells);
        }

        if (rows.Count == 1)
        {
            b.Add(new DocParagraph("Nenhuma espécie do projeto tem época de floração cadastrada."));
            return;
        }

        b.Add(new DocTable(new[] { 3.6 }.Concat(Enumerable.Repeat(0.5, 12)).ToList(), rows, FontPt: 8.5));
        b.Add(new DocParagraph("Células preenchidas com a cor da flor; espécies sem flor vistosa aparecem na cor da folhagem.", DocStyle.Caption));
        List<CalendarRow> unknown = calendar.Unknown.ToList();
        if (unknown.Count > 0)
        {
            b.Add(new DocParagraph("Sem época de floração cadastrada: " + string.Join(", ", unknown.Select(u => u.Species.PopularName)) + ".", DocStyle.Small));
        }
    }

    private static void Specifications(List<DocBlock> b, IReadOnlyList<MemorialSpecies> species, SpecificationSettings specs)
    {
        b.Add(new DocParagraph("5. Especificações de plantio", DocStyle.Heading1));
        b.Add(new DocParagraph(specs.General));
        foreach (IGrouping<DetailKind, MemorialSpecies> group in species.GroupBy(s => PlantingDetails.KindFor(s.Species.Category)).OrderBy(g => g.Key))
        {
            b.Add(new DocParagraph(PlantingDetails.Name(group.Key), DocStyle.Heading2));
            foreach (MemorialSpecies s in group)
            {
                b.Add(new DocParagraph(SpecificationTemplates.Render(specs, s.Species)));
            }
        }

        Excavation(b, species);
        b.Add(new DocParagraph("Dimensões de covas, tutores e camadas são valores de referência; confira com as normas locais e com o responsável técnico.", DocStyle.Small));
    }

    /// <summary>Quadro de escavação: dimensões da cova em centímetros e volume total por espécie.</summary>
    private static void Excavation(List<DocBlock> b, IReadOnlyList<MemorialSpecies> species)
    {
        if (species.Count == 0)
        {
            return;
        }

        b.Add(new DocParagraph("Covas e escavação", DocStyle.Heading2));
        b.Add(new DocParagraph("Dimensões a escavar em cada ponto de plantio (largura × comprimento × profundidade) ou espessura da camada de solo preparado nos canteiros e gramados."));
        var rows = new List<IReadOnlyList<DocCell>>
        {
            new[] { "Código", "Espécie", "Plantio", "Escavar", "Qtd.", "Volume (m³)" }.Select(h => new DocCell(h)).ToList(),
        };
        double total = 0;
        bool partial = false;
        foreach (MemorialSpecies s in species)
        {
            PlantingDimensions d = PlantingDetails.Dimensions(s.Species);
            double? volume = PlantingDetails.ExcavationM3(d, s.Quantity, Reports.SupplyUnits.IsAreaBased(s.Unit));
            if (volume is double v)
            {
                total += v;
            }
            else
            {
                partial = true;
            }

            rows.Add(new[]
            {
                new DocCell(s.Code),
                new DocCell(s.Species.PopularName),
                new DocCell(PlantingDetails.Name(d.Kind)),
                new DocCell(PlantingDetails.ExcavationText(d)),
                new DocCell(Quantity(s)),
                new DocCell(volume is double vol ? vol.ToString("N2", Pt) : "pela área do canteiro"),
            });
        }

        rows.Add(new[]
        {
            new DocCell("Total", bold: true), new DocCell(string.Empty), new DocCell(string.Empty), new DocCell(string.Empty), new DocCell(string.Empty),
            new DocCell(total.ToString("N2", Pt) + (partial ? " + canteiros" : string.Empty), bold: true),
        });
        b.Add(new DocTable(new[] { 0.8, 2.0, 1.6, 1.9, 1.2, 1.2 }, rows, FontPt: 9));
    }

    private static void Maintenance(List<DocBlock> b, IReadOnlyList<MaintenanceTask> tasks)
    {
        b.Add(new DocParagraph("6. Manutenção", DocStyle.Heading1));
        if (tasks.Count == 0)
        {
            b.Add(new DocParagraph("Cronograma de manutenção não gerado."));
            return;
        }

        var rows = new List<IReadOnlyList<DocCell>> { new[] { "Fase", "Serviço", "Espécies", "Meses", "Frequência" }.Select(h => new DocCell(h)).ToList() };
        foreach (IGrouping<(MaintenancePhase, string, string, string), MaintenanceTask> g in tasks
                     .GroupBy(t => (t.Phase, t.Title, t.Months.ToString(), t.Frequency))
                     .OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2, StringComparer.CurrentCulture))
        {
            rows.Add(new[]
            {
                new DocCell(g.First().PhaseName),
                new DocCell(g.Key.Item2),
                new DocCell(string.Join(", ", g.Select(t => t.SpeciesCode).Distinct())),
                new DocCell(g.Key.Item3),
                new DocCell(g.Key.Item4),
            });
        }

        b.Add(new DocTable(new[] { 1.3, 2.4, 1.8, 1.2, 1.6 }, rows, FontPt: 9));
    }

    private static void Credits(List<DocBlock> b, IReadOnlyList<MemorialSpecies> species)
    {
        List<MemorialSpecies> credited = species.Where(s => File.Exists(s.PhotoFile) && !string.IsNullOrWhiteSpace(s.Species.PhotoCredit)).ToList();
        if (credited.Count == 0)
        {
            return;
        }

        b.Add(new DocParagraph("7. Créditos das fotografias", DocStyle.Heading1));
        b.Add(new DocParagraph("Fotografias usadas conforme as licenças escolhidas pelos autores (Creative Commons). Fotos CC BY-SA permanecem sob a mesma licença.", DocStyle.Small));
        foreach (MemorialSpecies s in credited)
        {
            b.Add(new DocParagraph(new[]
            {
                new DocRun(s.Species.ScientificName + ": ", Italic: true),
                new DocRun($"{s.Species.PhotoCredit} · {s.Species.PhotoLicense} · {s.Species.PhotoSource}"),
            }, DocStyle.Small));
        }
    }

    private static DocParagraph Field(string label, string value) => new(new[] { new DocRun(label + ": ", true), new DocRun(value) });

    private static string Meters(double m) => m > 0 ? m.ToString("0.0#", Pt) + " m" : "—";

    private static bool IsArea(string unit) => unit.Contains('²') || unit.Equals("m2", StringComparison.OrdinalIgnoreCase);

    private static string Quantity(MemorialSpecies s)
    {
        if (IsArea(s.Unit))
        {
            return s.Quantity.ToString("N1", Pt) + " m²";
        }

        string unit = string.IsNullOrWhiteSpace(s.Unit) ? "un" : s.Unit.Trim();
        return s.Quantity.ToString("N0", Pt) + " " + Plural(unit, s.Quantity);
    }

    /// <summary>Plural simples das unidades comerciais (muda → mudas, unidade → unidades, vaso → vasos).</summary>
    public static string Plural(string unit, double quantity)
    {
        if (quantity <= 1 || unit.Length < 2 || unit == "un" || unit.EndsWith('s'))
        {
            return unit;
        }

        return "aeiou".Contains(char.ToLowerInvariant(unit[^1])) ? unit + "s" : unit;
    }
}
