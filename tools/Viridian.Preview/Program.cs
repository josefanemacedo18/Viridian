using System.Globalization;
using System.Text;
using Viridian.Core.Geometry;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Model;
using Viridian.Library;

// Ferramenta de desenvolvimento: gera pranchas SVG com os símbolos para conferência visual.
// Uso: Viridian.Preview <pasta de saída>
string outDir = args.Length > 0 ? args[0] : "preview";
Directory.CreateDirectory(outDir);

File.WriteAllText(Path.Combine(outDir, "estilos.svg"), SvgSheets.StyleSheet());
File.WriteAllText(Path.Combine(outDir, "perfis.svg"), SvgSheets.ProfileSheet());
File.WriteAllText(Path.Combine(outDir, "variantes.svg"), SvgSheets.VariantSheet());
File.WriteAllText(Path.Combine(outDir, "hachuras.svg"), SvgSheets.HatchSheet());
File.WriteAllText(Path.Combine(outDir, "distribuicoes.svg"), SvgSheets.DistributionSheet());
File.WriteAllText(Path.Combine(outDir, "cores.svg"), SvgSheets.ColorSheet(args.Length > 1 ? args[1] : null));
foreach ((string name, Viridian.Core.Detailing.Drawing drawing, double k) in SvgSheets.Stage1Drawings())
{
    File.WriteAllText(Path.Combine(outDir, name + ".svg"), SvgSheets.DrawingSvg(drawing, k));
}

SvgSheets.MemorialSample(Path.Combine(outDir, "memorial-exemplo.docx"), Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Viridian.Addin", "Fotos")));
File.WriteAllText(Path.Combine(outDir, "quadro-fotos.svg"), SvgSheets.PhotoBoardSheet(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Viridian.Addin", "Fotos"))));
File.WriteAllText(Path.Combine(outDir, "maquete.svg"), MaquetePreview.Scene(Viridian.Core.Detailing.GrowthStage.Maturidade));
File.WriteAllText(Path.Combine(outDir, "maquete-plantio.svg"), MaquetePreview.Scene(Viridian.Core.Detailing.GrowthStage.Plantio));
File.WriteAllText(Path.Combine(outDir, "pisos.svg"), PavingPreview.Sheet());
foreach (string id in new[] { "vir-paver-10x20-espinha", "vir-deck-cumaru", "vir-drenante-bloco", "vir-fulget-moldado" })
{
    var piso = Viridian.Core.Paving.PavingDefaults.Create().First(t => t.Id == id);
    File.WriteAllText(Path.Combine(outDir, "detalhe-" + id + ".svg"), SvgSheets.DrawingSvg(Viridian.Core.Paving.PavingDetail.Draw(piso), 1100));
}

File.WriteAllText(Path.Combine(outDir, "jardim-de-chuva.svg"), SvgSheets.DrawingSvg(WaterSolarPreview.RainGarden(), 260));
File.WriteAllText(Path.Combine(outDir, "estudo-solar.svg"), WaterSolarPreview.Solar());
File.WriteAllText(Path.Combine(outDir, "macicos.svg"), MassingPreview.Sheet());
Console.WriteLine("Pranchas geradas em " + Path.GetFullPath(outDir));

internal static class SvgSheets
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string StyleSheet()
    {
        var styles = SymbolStyles.All;
        const int cols = 6;
        const double cell = 250;
        int rows = (int)Math.Ceiling(styles.Count / (double)cols);
        var sb = Begin(cols * cell, rows * (cell + 30));
        for (int i = 0; i < styles.Count; i++)
        {
            double cx = (i % cols) * cell + cell / 2;
            double cy = (i / cols) * (cell + 30) + cell / 2;
            double diameter = DefaultDiameter(styles[i]);
            PlantSymbol symbol = SymbolGenerator.Generate(styles[i], diameter, 1234 + i);
            Draw(sb, symbol, DetailProfile.Detalhado, cx, cy, (cell - 40) / diameter);
            Text(sb, cx, cy + cell / 2 + 12, SymbolStyles.DisplayName(styles[i]) + $" · Ø{diameter.ToString("0.#", Inv)} m");
        }

        return End(sb);
    }

    /// <summary>Espécies do catálogo padrão desenhadas com as cores da planta.</summary>
    public static string ColorSheet(string? filter)
    {
        var settings = new ViridianSettings();
        settings.Normalize();
        if (filter == "reticulado")
        {
            settings.Colors.CrownFill = CrownFillMode.Reticulado;
        }

        List<Species> all = SeedCatalog.CreateSpecies();
        var picked = new List<Species>();
        foreach (PlantCategory category in Labels.Categories)
        {
            List<Species> ofCategory = all.Where(s => s.Category == category).ToList();
            int take = category == PlantCategory.Arvore ? 12 : 6;
            int step = Math.Max(1, ofCategory.Count / take);
            picked.AddRange(ofCategory.Where((_, i) => i % step == 0).Take(take));
        }

        const int cols = 8;
        const double cell = 190;
        int rows = (int)Math.Ceiling(picked.Count / (double)cols);
        var sb = Begin(cols * cell, rows * (cell + 34));
        for (int i = 0; i < picked.Count; i++)
        {
            Species species = picked[i];
            double cx = (i % cols) * cell + cell / 2;
            double cy = (i / cols) * (cell + 34) + cell / 2;
            PlantSymbol symbol = SymbolGenerator.Generate(species.SymbolStyle, species.CrownDiameterM, species.EffectiveSymbolSeed);
            Draw(sb, symbol, DetailProfile.Detalhado, cx, cy, (cell - 30) / symbol.Diameter, SymbolPaint.For(species, settings.Colors), settings);
            Text(sb, cx, cy + cell / 2 + 8, species.PopularName);
            Text(sb, cx, cy + cell / 2 + 22, species.ScientificName);
        }

        return End(sb);
    }

    /// <summary>Exemplo do quadro de fotos: canteiro com espécies e cartões ao redor (mesmo algoritmo do plugin).</summary>
    public static string PhotoBoardSheet(string photosFolder)
    {
        var settings = new ViridianSettings();
        settings.Normalize();
        List<Species> all = SeedCatalog.CreateSpecies();
        string[] names = { "Handroanthus chrysotrichus", "Syagrus romanzoffiana", "Plinia cauliflora", "Arachis repens", "Liriope muscari", "Cordyline fruticosa", "Russelia equisetiformis", "Tradescantia pallida", "Zoysia japonica", "Dypsis lutescens" };
        List<Species> picked = names.Select(n => all.FirstOrDefault(s => s.ScientificName == n)).OfType<Species>().ToList();
        var rng = new DeterministicRandom(7);
        var drawing = new Bounds2(0, 0, 40, 18);
        var plants = new List<(Species S, Point2 P)>();
        foreach (Species s in picked)
        {
            int count = s.Category is PlantCategory.Arvore or PlantCategory.Palmeira ? 3 : 6;
            for (int i = 0; i < count; i++)
            {
                plants.Add((s, new Point2(rng.Range(2, 38), rng.Range(2, 16))));
            }
        }

        var items = picked.Select(s => new Viridian.Core.Identification.PhotoBoardItem(s.Id, plants.Where(p => p.S == s).Select(p => p.P).ToList())).ToList();
        var cards = Viridian.Core.Identification.PhotoBoardLayout.Arrange(drawing, items, 6.8, 6.8, 1.2);
        double minX = cards.Min(c => c.Box.MinX) - 1, maxX = cards.Max(c => c.Box.MaxX) + 1, minY = cards.Min(c => c.Box.MinY) - 1, maxY = cards.Max(c => c.Box.MaxY) + 1;
        const double k = 20;
        double W(double x) => (x - minX) * k;
        double H(double y) => (maxY - y) * k;
        var sb = Begin((maxX - minX) * k, (maxY - minY) * k);
        sb.Append(Inv, $"<rect x=\"{W(0):0}\" y=\"{H(18):0}\" width=\"{40 * k:0}\" height=\"{18 * k:0}\" fill=\"#E9F1E4\" stroke=\"#3A4A40\" stroke-width=\"2\"/>");
        foreach ((Species s, Point2 p) in plants.OrderByDescending(x => x.S.CrownDiameterM))
        {
            PlantSymbol symbol = SymbolGenerator.Generate(s.SymbolStyle, s.CrownDiameterM, s.EffectiveSymbolSeed);
            Draw(sb, symbol, DetailProfile.Intermediario, W(p.X), H(p.Y), k, SymbolPaint.For(s, settings.Colors), settings);
        }

        foreach (var card in cards)
        {
            Species s = picked.First(x => x.Id == card.Key);
            sb.Append(Inv, $"<line x1=\"{W(card.Anchor.X):0.#}\" y1=\"{H(card.Anchor.Y):0.#}\" x2=\"{W(card.Target.X):0.#}\" y2=\"{H(card.Target.Y):0.#}\" stroke=\"#333\" stroke-width=\"1\"/>");
            sb.Append(Inv, $"<circle cx=\"{W(card.Target.X):0.#}\" cy=\"{H(card.Target.Y):0.#}\" r=\"3\" fill=\"#333\"/>");
            string photo = Path.Combine(photosFolder, s.PhotoFile);
            double x = W(card.Box.MinX), y = H(card.Box.MaxY), w = card.Box.Width * k;
            sb.Append(Inv, $"<image href=\"file://{photo}\" x=\"{x:0.#}\" y=\"{y:0.#}\" width=\"{w:0.#}\" height=\"{w * 0.72:0.#}\" preserveAspectRatio=\"xMidYMid slice\"/>");
            sb.Append(Inv, $"<text x=\"{x + w / 2:0.#}\" y=\"{y + w * 0.72 + 14:0.#}\" font-family=\"Arial\" font-size=\"11\" font-weight=\"bold\" text-anchor=\"middle\">{System.Net.WebUtility.HtmlEncode((s.Code + " " + s.PopularName).ToUpperInvariant())}</text>");
            sb.Append(Inv, $"<text x=\"{x + w / 2:0.#}\" y=\"{y + w * 0.72 + 27:0.#}\" font-family=\"Arial\" font-size=\"10\" font-style=\"italic\" text-anchor=\"middle\" fill=\"#444\">{System.Net.WebUtility.HtmlEncode(s.ScientificName)}</text>");
        }

        return End(sb);
    }

    /// <summary>Memorial de exemplo com espécies reais do catálogo.</summary>
    public static void MemorialSample(string file, string photos)
    {
        List<Species> all = SeedCatalog.CreateSpecies();
        string[] names = { "Handroanthus chrysotrichus", "Syagrus romanzoffiana", "Plinia cauliflora", "Arachis repens", "Liriope muscari", "Russelia equisetiformis", "Zoysia japonica", "Bauhinia variegata" };
        var species = names.Select(n => all.First(s => s.ScientificName == n))
            .Select((s, i) => new Viridian.Core.Memorial.MemorialSpecies(s, s.Code, s.Category == PlantCategory.Gramado ? 120 : 3 + i, s.SupplyUnit, Path.Combine(photos, s.PhotoFile)))
            .ToList();
        var items = species.Select(m => new Viridian.Core.Maintenance.MaintenanceItem { Species = m.Species, SpeciesCode = m.Code, NewCount = (int)m.Quantity, Reserve = 1 }).ToList();
        var input = new Viridian.Core.Memorial.MemorialInput
        {
            Project = new Viridian.Core.Memorial.MemorialProject { Name = "Residência Jardim das Palmeiras", Number = "PAI-2026-014", Client = "Família Silva", Address = "Rua das Acácias, 120 — Ribeirão Preto/SP", Author = "Arq. Paisagista Responsável" },
            Species = species,
            Maintenance = Viridian.Core.Maintenance.MaintenancePlanner.Build(items, new Viridian.Core.Maintenance.MaintenanceSettings()),
            MassingAreaM2 = 120,
        };
        Viridian.Core.Export.DocxWriter.Write(file, Viridian.Core.Memorial.MemorialBuilder.Build(input), "Memorial");
    }

    /// <summary>Desenhos da etapa 1: detalhes, corte, crescimento e calendário, com espécies reais do catálogo.</summary>
    public static IEnumerable<(string Name, Viridian.Core.Detailing.Drawing Drawing, double Scale)> Stage1Drawings()
    {
        var colors = new ColorSettings();
        List<Species> all = SeedCatalog.CreateSpecies();
        Species Find(string n) => all.First(s => s.ScientificName == n);
        Species ipe = Find("Handroanthus chrysotrichus");
        Species jeriva = Find("Syagrus romanzoffiana");
        Species amendoim = Find("Arachis repens");
        Species pitanga = all.First(s => s.Category == PlantCategory.Arbusto);
        yield return ("detalhe-arvore", Viridian.Core.Detailing.PlantingDetails.Draw(ipe, colors), 180);
        yield return ("detalhe-palmeira", Viridian.Core.Detailing.PlantingDetails.Draw(jeriva, colors), 150);
        yield return ("detalhe-forracao", Viridian.Core.Detailing.PlantingDetails.Draw(amendoim, colors), 250);

        var rng = new DeterministicRandom(3);
        var plants = new List<Viridian.Core.Detailing.SectionPlant>();
        var growth = new List<Viridian.Core.Detailing.GrowthPlant>();
        foreach (Species s in new[] { ipe, jeriva, Find("Plinia cauliflora"), pitanga, ipe, Find("Bauhinia variegata") })
        {
            var p = new Point2(rng.Range(2, 28), rng.Range(-2, 2));
            plants.Add(new(s, s.Code, p, s.AdultHeightM, s.CrownDiameterM));
            growth.Add(new(s, new Point2(p.X, p.Y * 3 + 6)));
        }

        var bed = new Polygon2(new[] { new Point2(10, -1.5), new Point2(18, -1.5), new Point2(18, 1.5), new Point2(10, 1.5) });
        var section = Viridian.Core.Detailing.SchematicSection.Build(new Point2(0, 0), new Point2(30, 0), plants,
            new[] { new Viridian.Core.Detailing.SectionMassing(amendoim, amendoim.Code, bed) }, colors, 3);
        yield return ("corte", section, 30);
        var region = new Polygon2(new[] { new Point2(0, 0), new Point2(30, 0), new Point2(30, 12), new Point2(0, 12) });
        yield return ("crescimento", Viridian.Core.Detailing.GrowthBoard.Build(region, growth,
            new[] { new Viridian.Core.Detailing.GrowthMassing(amendoim, new Polygon2(new[] { new Point2(3, 1), new Point2(12, 1), new Point2(12, 4), new Point2(3, 4) })) }, colors), 9);
        var cal = Viridian.Core.Composition.FloweringCalendar.Build(all.Where(s => !string.IsNullOrEmpty(s.FlowerColor)).Take(14).Select(s => (s, s.Code, 1.0)));
        yield return ("calendario", Viridian.Core.Composition.CalendarDrawing.Build(cal, colors, "Calendário de floração"), 6000);
        var specItems = new[] { ipe, jeriva, Find("Plinia cauliflora"), pitanga, amendoim, Find("Bauhinia variegata") }
            .Select((s, i) => new Viridian.Core.Detailing.SpecificationItem(s, s.Code, 2 + i * 3, s.SupplyUnit)).ToList();
        yield return ("quadro-especificacao", Viridian.Core.Detailing.SpecificationBoard.Build(specItems, new Viridian.Core.Detailing.SpecificationSettings(), 2.0), 5500);
    }

    /// <summary>SVG de um desenho do Viridian (metros × fator em pixels).</summary>
    public static string DrawingSvg(Viridian.Core.Detailing.Drawing d, double k)
    {
        Bounds2 e = d.Extent();
        foreach (var t in d.Texts)
        {
            double w = t.Text.Length * 6.0 * t.SizeFactor / k;
            double x0 = t.Align == 0 ? t.At.X - w / 2 : t.Align > 0 ? t.At.X - w : t.At.X;
            e = new Bounds2(Math.Min(e.MinX, x0), e.MinY, Math.Max(e.MaxX, x0 + w), e.MaxY);
        }

        double pad = Math.Max(e.Width, e.Height) * 0.03 + 0.005;
        e = e.Expand(pad);
        double W(double x) => (x - e.MinX) * k;
        double H(double y) => (e.MaxY - y) * k;
        double text = Math.Max(9, Math.Min(14, 2.0 / 1000 * k * (k > 1000 ? 1 : 100) ));
        var sb = Begin(e.Width * k, e.Height * k);
        foreach (var f in d.Fills)
        {
            sb.Append(Inv, $"<polygon points=\"{string.Join(" ", f.Outline.Select(p => $"{W(p.X):0.#},{H(p.Y):0.#}"))}\" fill=\"{f.Color.Hex}\" stroke=\"none\"/>");
        }

        foreach (var l in d.Lines)
        {
            sb.Append(Inv, $"<line x1=\"{W(l.A.X):0.#}\" y1=\"{H(l.A.Y):0.#}\" x2=\"{W(l.B.X):0.#}\" y2=\"{H(l.B.Y):0.#}\" stroke=\"{(l.Heavy ? "#2E4A36" : "#4A554E")}\" stroke-width=\"{(l.Heavy ? 1.8 : 0.8)}\"{(l.Dashed ? " stroke-dasharray='6 4'" : string.Empty)}/>");
        }

        foreach (var t in d.Texts)
        {
            string anchor = t.Align == 0 ? "middle" : t.Align > 0 ? "end" : "start";
            sb.Append(Inv, $"<text x=\"{W(t.At.X):0.#}\" y=\"{H(t.At.Y) + 4:0.#}\" font-family=\"Arial\" font-size=\"{11 * t.SizeFactor:0.#}\" text-anchor=\"{anchor}\" fill=\"#222\">{System.Net.WebUtility.HtmlEncode(t.Text)}</text>");
        }

        return End(sb);
    }

    public static string ProfileSheet()
    {
        var styles = new[]
        {
            SymbolStyle.ArvoreArredondada, SymbolStyle.ArvoreEspalhada, SymbolStyle.ArvorePiramidal,
            SymbolStyle.Palmeira, SymbolStyle.ArbustoNuvem, SymbolStyle.Herbacea,
        };
        var profiles = Enum.GetValues<DetailProfile>();
        const double cell = 230;
        var sb = Begin(profiles.Length * cell + 20, styles.Length * (cell + 26));
        for (int r = 0; r < styles.Length; r++)
        {
            double diameter = DefaultDiameter(styles[r]);
            PlantSymbol symbol = SymbolGenerator.Generate(styles[r], diameter, 77 + r);
            for (int c = 0; c < profiles.Length; c++)
            {
                double cx = c * cell + cell / 2 + 10;
                double cy = r * (cell + 26) + cell / 2;
                Draw(sb, symbol, profiles[c], cx, cy, (cell - 40) / diameter);
                Text(sb, cx, cy + cell / 2 + 10, $"{ScaleProfileSet.DisplayName(profiles[c])} · {symbol.Count(profiles[c])} linhas");
            }
        }

        return End(sb);
    }

    public static string VariantSheet()
    {
        var styles = new[] { SymbolStyle.ArvoreArredondada, SymbolStyle.ArvoreIrregular, SymbolStyle.Palmeira, SymbolStyle.ArbustoNuvem };
        const double cell = 230;
        var sb = Begin(4 * cell + 20, styles.Length * (cell + 26));
        for (int r = 0; r < styles.Length; r++)
        {
            double diameter = DefaultDiameter(styles[r]);
            for (int v = 0; v < 4; v++)
            {
                PlantSymbol symbol = SymbolGenerator.Generate(styles[r], diameter, 500 + r, v);
                double cx = v * cell + cell / 2 + 10;
                double cy = r * (cell + 26) + cell / 2;
                Draw(sb, symbol, DetailProfile.Detalhado, cx, cy, (cell - 40) / diameter);
                Text(sb, cx, cy + cell / 2 + 10, $"Variante {v + 1}");
            }
        }

        return End(sb);
    }

    public static string DistributionSheet()
    {
        var bed = new Polygon2(new[]
        {
            new Point2(0, 0), new Point2(14, -1), new Point2(18, 6), new Point2(12, 12), new Point2(4, 11), new Point2(-1, 6),
        });
        var path = new[] { new Point2(-1, 1), new Point2(6, 6), new Point2(12, 3), new Point2(18, 10) };
        var cases = new (string Title, Viridian.Core.Distribution.DistributionRequest Request, bool UseRegion)[]
        {
            ("Triangular", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Triangular, SpacingM = 1.2, EdgeMarginM = 0.4 }, true),
            ("Natural", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Natural, SpacingM = 1.2, EdgeMarginM = 0.4, Seed = 4 }, true),
            ("Linear — fileira dupla alternada", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Linear, SpacingM = 1.6, Path = path, Rows = 2, RowArrangement = Viridian.Core.Distribution.RowArrangement.Alternadas, LinearAlignment = Viridian.Core.Distribution.LinearAlignment.Ajustado }, false),
            ("Bordadura — 2 fileiras", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Bordadura, SpacingM = 1.0, EdgeMarginM = 0.4, Rows = 2, RowArrangement = Viridian.Core.Distribution.RowArrangement.Alternadas }, true),
            ("Circular — anéis", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Circular, SpacingM = 1.2, RowSpacingM = 1.3, EdgeMarginM = 0.4 }, true),
            ("Agrupamentos 3–7", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Agrupamentos, SpacingM = 0.8, EdgeMarginM = 0.4, GroupGapM = 1.4, Seed = 9 }, true),
            ("Degradê", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Gradiente, SpacingM = 0.7, EdgeMarginM = 0.3, GradientEndPercent = 5, Seed = 2 }, true),
        };
        const double cell = 300;
        const double scale = 14;
        var sb = Begin(4 * cell, 2 * (cell + 10));
        for (int i = 0; i < cases.Length; i++)
        {
            double ox = (i % 4) * cell + 30;
            double oy = (i / 4) * (cell + 10) + 250;
            var result = Viridian.Core.Distribution.PlantDistributor.Distribute(cases[i].UseRegion ? bed : null, cases[i].Request);
            if (cases[i].UseRegion)
            {
                var ring = bed.Rings[0];
                sb.Append("<polygon points=\"" + string.Join(" ", ring.Select(p => (ox + p.X * scale).ToString("0.#", Inv) + "," + (oy - p.Y * scale).ToString("0.#", Inv))) + "\" fill=\"#EEF5F0\" stroke=\"#2F6B4F\" stroke-width=\"1.2\"/>");
            }
            else
            {
                sb.Append("<polyline points=\"" + string.Join(" ", path.Select(p => (ox + p.X * scale).ToString("0.#", Inv) + "," + (oy - p.Y * scale).ToString("0.#", Inv))) + "\" fill=\"none\" stroke=\"#C88A12\" stroke-width=\"1.2\" stroke-dasharray=\"4 3\"/>");
            }

            foreach (Point2 p in result.Points)
            {
                sb.Append(Inv, $"<circle cx=\"{ox + p.X * scale:0.#}\" cy=\"{oy - p.Y * scale:0.#}\" r=\"{0.45 * scale:0.#}\" fill=\"#9CC48A\" stroke=\"#2F6B4F\" stroke-width=\"0.8\"/>");
            }

            Text(sb, ox + 120, oy + 40, $"{cases[i].Title} · {result.Points.Count} un");
        }

        return End(sb);
    }

    public static string HatchSheet()
    {
        var patterns = Viridian.Core.Hatch.HatchLibrary.BuiltIn;
        const int cols = 4;
        const double cell = 260;
        const double pxPerMm = 4;
        int rows = (int)Math.Ceiling(patterns.Count / (double)cols);
        var sb = Begin(cols * cell, rows * (cell * 0.6 + 30));
        for (int i = 0; i < patterns.Count; i++)
        {
            double x0 = (i % cols) * cell + 10;
            double y0 = (i / cols) * (cell * 0.6 + 30) + 10;
            double w = cell - 20, h = cell * 0.6 - 10;
            var area = new Bounds2(0, 0, w / pxPerMm, h / pxPerMm);
            sb.Append(Inv, $"<rect x=\"{x0:0.#}\" y=\"{y0:0.#}\" width=\"{w:0.#}\" height=\"{h:0.#}\" fill=\"none\" stroke=\"#999\" stroke-width=\"0.6\"/>");
            foreach (var (a, b) in Viridian.Core.Hatch.HatchRasterizer.Segments(patterns[i], area))
            {
                sb.Append(Inv, $"<line x1=\"{x0 + a.X * pxPerMm:0.##}\" y1=\"{y0 + h - a.Y * pxPerMm:0.##}\" x2=\"{x0 + b.X * pxPerMm:0.##}\" y2=\"{y0 + h - b.Y * pxPerMm:0.##}\" stroke=\"black\" stroke-width=\"0.8\" stroke-linecap=\"round\"/>");
            }

            Text(sb, x0 + w / 2, y0 + h + 16, patterns[i].Name + " · " + patterns[i].Application);
        }

        return End(sb);
    }

    private static double DefaultDiameter(SymbolStyle style) => style switch
    {
        SymbolStyle.ArvoreColunar => 3,
        SymbolStyle.ArvorePiramidal => 5,
        SymbolStyle.Palmeira or SymbolStyle.PalmeiraLeque => 4,
        SymbolStyle.ArbustoNuvem or SymbolStyle.ArbustoPontiagudo => 1.2,
        SymbolStyle.Herbacea => 0.6,
        SymbolStyle.Forracao => 0.4,
        SymbolStyle.Gramado => 0.3,
        SymbolStyle.Trepadeira => 1.0,
        SymbolStyle.Aquatica => 0.8,
        SymbolStyle.Simples => 2,
        _ => 8,
    };

    private static StringBuilder Begin(double width, double height)
    {
        var sb = new StringBuilder();
        sb.Append(Inv, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width:0}\" height=\"{height:0}\" viewBox=\"0 0 {width:0} {height:0}\">");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        return sb;
    }

    private static string End(StringBuilder sb) => sb.Append("</svg>").ToString();

    private static void Text(StringBuilder sb, double x, double y, string text) =>
        sb.Append(Inv, $"<text x=\"{x:0.#}\" y=\"{y:0.#}\" font-family=\"Segoe UI, Arial\" font-size=\"11\" text-anchor=\"middle\" fill=\"#333\">{System.Net.WebUtility.HtmlEncode(text)}</text>");

    private static double Weight(SymbolTier tier) => tier switch
    {
        SymbolTier.Outline or SymbolTier.MinimalOutline => 1.5,
        SymbolTier.Center => 1.2,
        SymbolTier.Essential => 1.0,
        SymbolTier.Principal => 0.8,
        _ => 0.55,
    };

    public static void Draw(StringBuilder sb, PlantSymbol symbol, DetailProfile profile, double cx, double cy, double scale, SymbolPaint? paint = null, ViridianSettings? settings = null)
    {
        if (paint is not null && paint.Mode != CrownFillMode.Nenhum && symbol.FillOutline.Count >= 3 && TierVisibility.IsVisible(SymbolTier.Fill, profile))
        {
            string points = string.Join(" ", symbol.FillOutline.Select(p => $"{(cx + p.X * scale).ToString("0.##", Inv)},{(cy - p.Y * scale).ToString("0.##", Inv)}"));
            if (paint.Mode == CrownFillMode.Solido)
            {
                sb.Append(Inv, $"<polygon points=\"{points}\" fill=\"{paint.Crown.Hex}\" stroke=\"none\"/>");
            }
            else
            {
                string id = "h" + sb.Length.ToString(Inv);
                sb.Append(Inv, $"<defs><pattern id=\"{id}\" width=\"5\" height=\"5\" patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(45)\"><path d=\"M0 0 V5 M0 0 H5\" stroke=\"{paint.Crown.Hex}\" stroke-width=\"0.7\"/></pattern></defs>");
                sb.Append(Inv, $"<polygon points=\"{points}\" fill=\"url(#{id})\" stroke=\"none\"/>");
            }
        }

        if (paint?.Flower is Rgb flower && TierVisibility.IsVisible(SymbolTier.Flowers, profile))
        {
            foreach (FlowerSpot spot in symbol.FlowerSpots)
            {
                sb.Append(Inv, $"<circle cx=\"{cx + spot.Center.X * scale:0.##}\" cy=\"{cy - spot.Center.Y * scale:0.##}\" r=\"{Math.Max(0.8, spot.Radius * scale):0.##}\" fill=\"{flower.Hex}\"/>");
            }
        }

        foreach (SymbolPrimitive p in symbol.ForProfile(profile))
        {
            string color = settings is null || paint is null ? "black" : Rgb.Parse(settings.StyleOf(p.Tier).Color, Rgb.Black).Hex;
            string stroke = $"stroke=\"{color}\" stroke-width=\"{Weight(p.Tier).ToString("0.##", Inv)}\" fill=\"none\" stroke-linecap=\"round\"";
            switch (p)
            {
                case SymbolLine l:
                    sb.Append(Inv, $"<line x1=\"{cx + l.A.X * scale:0.##}\" y1=\"{cy - l.A.Y * scale:0.##}\" x2=\"{cx + l.B.X * scale:0.##}\" y2=\"{cy - l.B.Y * scale:0.##}\" {stroke}/>");
                    break;
                case SymbolArc a:
                    if (ArcInfo.TryFromThreePoints(a.Start, a.Mid, a.End, out ArcInfo info))
                    {
                        double radius = info.Radius * scale;
                        int large = Math.Abs(info.Sweep) > Math.PI ? 1 : 0;
                        int sweepFlag = info.Sweep > 0 ? 0 : 1; // eixo Y invertido no SVG
                        sb.Append(Inv, $"<path d=\"M {cx + a.Start.X * scale:0.##} {cy - a.Start.Y * scale:0.##} A {radius:0.##} {radius:0.##} 0 {large} {sweepFlag} {cx + a.End.X * scale:0.##} {cy - a.End.Y * scale:0.##}\" {stroke}/>");
                    }

                    break;
            }
        }
    }
}
