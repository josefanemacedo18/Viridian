using System.Globalization;
using System.Text;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Symbols;

namespace Viridian.Core.Export;

/// <summary>
/// Gravador de DXF R12 (AC1009), o formato mais aceito por programas de CAD. Gera blocos, linhas, arcos,
/// círculos, sólidos (preenchimento) e textos, com camadas e cores do índice do AutoCAD (ACI).
/// </summary>
public sealed class DxfWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly StringBuilder _sb = new();

    /// <summary>Camadas: nome e cor ACI.</summary>
    public Dictionary<string, int> Layers { get; } = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<(string Name, List<Action<DxfWriter>> Entities)> _blocks = new();
    private readonly List<Action<DxfWriter>> _entities = new();

    public void AddBlock(string name, List<Action<DxfWriter>> entities) => _blocks.Add((name, entities));

    public void AddEntity(Action<DxfWriter> entity) => _entities.Add(entity);

    /// <summary>Texto completo do arquivo.</summary>
    public string Build()
    {
        _sb.Clear();
        Pair(0, "SECTION");
        Pair(2, "HEADER");
        Pair(9, "$ACADVER");
        Pair(1, "AC1009");
        Pair(9, "$DWGCODEPAGE");
        Pair(3, "ANSI_1252");
        Pair(9, "$INSBASE");
        Point(10, 0, 0);
        Pair(9, "$EXTMIN");
        Point(10, -1, -1);
        Pair(9, "$EXTMAX");
        Point(10, 100, 100);
        Pair(0, "ENDSEC");

        Pair(0, "SECTION");
        Pair(2, "TABLES");
        Pair(0, "TABLE");
        Pair(2, "LTYPE");
        Pair(70, "1");
        Pair(0, "LTYPE");
        Pair(2, "CONTINUOUS");
        Pair(70, "0");
        Pair(3, "Solid line");
        Pair(72, "65");
        Pair(73, "0");
        Pair(40, "0.0");
        Pair(0, "ENDTAB");
        Pair(0, "TABLE");
        Pair(2, "LAYER");
        Pair(70, (Layers.Count + 1).ToString(Inv));
        WriteLayer("0", 7);
        foreach ((string name, int color) in Layers)
        {
            WriteLayer(name, color);
        }

        Pair(0, "ENDTAB");
        Pair(0, "ENDSEC");

        Pair(0, "SECTION");
        Pair(2, "BLOCKS");
        foreach ((string name, List<Action<DxfWriter>> entities) in _blocks)
        {
            Pair(0, "BLOCK");
            Pair(8, "0");
            Pair(2, name);
            Pair(70, "0");
            Point(10, 0, 0);
            Pair(3, name);
            foreach (Action<DxfWriter> e in entities)
            {
                e(this);
            }

            Pair(0, "ENDBLK");
            Pair(8, "0");
        }

        Pair(0, "ENDSEC");

        Pair(0, "SECTION");
        Pair(2, "ENTITIES");
        foreach (Action<DxfWriter> e in _entities)
        {
            e(this);
        }

        Pair(0, "ENDSEC");
        Pair(0, "EOF");
        return _sb.ToString();
    }

    /// <summary>Grava o arquivo em Windows-1252 (letras acentuadas do português).</summary>
    public void Save(string file)
    {
        string text = Build();
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bytes[i] = c < 256 ? (byte)c : (byte)'?';
        }

        File.WriteAllBytes(file, bytes);
    }

    public void Line(string layer, Point2 a, Point2 b)
    {
        Pair(0, "LINE");
        Pair(8, layer);
        Point(10, a.X, a.Y);
        Point(11, b.X, b.Y);
    }

    /// <summary>Arco no sentido anti-horário de <paramref name="startDeg"/> a <paramref name="endDeg"/>.</summary>
    public void Arc(string layer, Point2 center, double radius, double startDeg, double endDeg)
    {
        Pair(0, "ARC");
        Pair(8, layer);
        Point(10, center.X, center.Y);
        Pair(40, Num(radius));
        Pair(50, Num(Normalize(startDeg)));
        Pair(51, Num(Normalize(endDeg)));
    }

    public void Circle(string layer, Point2 center, double radius, int? color = null)
    {
        Pair(0, "CIRCLE");
        Pair(8, layer);
        if (color.HasValue)
        {
            Pair(62, color.Value.ToString(Inv));
        }

        Point(10, center.X, center.Y);
        Pair(40, Num(radius));
    }

    /// <summary>Triângulo preenchido (entidade SOLID).</summary>
    public void Solid(string layer, Point2 a, Point2 b, Point2 c, int? color = null)
    {
        Pair(0, "SOLID");
        Pair(8, layer);
        if (color.HasValue)
        {
            Pair(62, color.Value.ToString(Inv));
        }

        Point(10, a.X, a.Y);
        Point(11, b.X, b.Y);
        Point(12, c.X, c.Y);
        Point(13, c.X, c.Y);
    }

    /// <summary>Texto centralizado no ponto informado.</summary>
    public void Text(string layer, Point2 at, double height, string text)
    {
        Pair(0, "TEXT");
        Pair(8, layer);
        Point(10, at.X, at.Y);
        Pair(40, Num(height));
        Pair(1, Clean(text));
        Pair(72, "1");
        Point(11, at.X, at.Y);
    }

    public void Insert(string layer, string block, Point2 at, double scale = 1)
    {
        Pair(0, "INSERT");
        Pair(8, layer);
        Pair(2, block);
        Point(10, at.X, at.Y);
        if (Math.Abs(scale - 1) > 1e-12)
        {
            Pair(41, Num(scale));
            Pair(42, Num(scale));
            Pair(43, Num(scale));
        }
    }

    /// <summary>Cor do índice do AutoCAD mais próxima de uma cor RGB (sem 7 e sem cinzas puros, que mudam com o fundo).</summary>
    public static int NearestAci(Rgb color)
    {
        int best = 1;
        double bestDistance = double.MaxValue;
        for (int i = 1; i <= 255; i++)
        {
            if (i == 7)
            {
                continue;
            }

            Rgb candidate = AciColor(i);
            double dr = candidate.R - color.R;
            double dg = candidate.G - color.G;
            double db = candidate.B - color.B;
            double distance = 0.3 * dr * dr + 0.59 * dg * dg + 0.11 * db * db;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Paleta padrão do AutoCAD (ACI 1 a 255).</summary>
    public static Rgb AciColor(int index)
    {
        switch (index)
        {
            case 1: return new Rgb(255, 0, 0);
            case 2: return new Rgb(255, 255, 0);
            case 3: return new Rgb(0, 255, 0);
            case 4: return new Rgb(0, 255, 255);
            case 5: return new Rgb(0, 0, 255);
            case 6: return new Rgb(255, 0, 255);
            case 7: return new Rgb(255, 255, 255);
            case 8: return new Rgb(128, 128, 128);
            case 9: return new Rgb(192, 192, 192);
        }

        if (index >= 250)
        {
            byte[] grays = { 51, 80, 105, 130, 190, 255 };
            return new Rgb(grays[index - 250], grays[index - 250], grays[index - 250]);
        }

        // 10 a 249: 24 matizes de 15°, cada uma com 5 luminosidades, cheia (par) e esmaecida (ímpar).
        double hue = (index / 10 - 1) * 15.0;
        int step = index % 10;
        double[] values = { 1.0, 0.65, 0.5, 0.3, 0.15 };
        double value = values[step / 2];
        double saturation = step % 2 == 0 ? 1.0 : 0.5;
        return FromHsv(hue, saturation, value);
    }

    private static Rgb FromHsv(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static byte B(double t) => (byte)Math.Clamp(Math.Floor(t * 255 + 1e-9), 0, 255); // como a tabela do AutoCAD (127, 165, 76...)
        return new Rgb(B(r + m), B(g + m), B(b + m));
    }

    private void WriteLayer(string name, int color)
    {
        Pair(0, "LAYER");
        Pair(2, name);
        Pair(70, "0");
        Pair(62, Math.Clamp(color, 1, 255).ToString(Inv));
        Pair(6, "CONTINUOUS");
    }

    private void Pair(int code, string value)
    {
        _sb.Append(code.ToString(Inv).PadLeft(3)).Append("\r\n").Append(value).Append("\r\n");
    }

    private void Point(int code, double x, double y)
    {
        Pair(code, Num(x));
        Pair(code + 10, Num(y));
        Pair(code + 20, "0.0");
    }

    private static string Num(double value) => double.IsFinite(value) ? value.ToString("0.0#####", Inv) : "0.0";

    private static double Normalize(double degrees)
    {
        double d = degrees % 360;
        return d < 0 ? d + 360 : d;
    }

    private static string Clean(string text) => (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
}

/// <summary>Biblioteca de blocos DXF com os símbolos das espécies, para uso em programas de CAD.</summary>
public static class SymbolDxf
{
    public const string LayerOutline = "VIR-CONTORNO";
    public const string LayerStructure = "VIR-ESTRUTURA";
    public const string LayerDetail = "VIR-DETALHE";
    public const string LayerFill = "VIR-PREENCHIMENTO";
    public const string LayerFlowers = "VIR-FLORACAO";
    public const string LayerText = "VIR-TEXTO";

    /// <summary>
    /// Monta a biblioteca: um bloco por espécie (em metros, tamanho real, perfil Detalhado) e uma folha de
    /// conferência com os blocos inseridos em grade, identificados por código e nome.
    /// </summary>
    public static DxfWriter Build(IReadOnlyList<(Species Species, string Code)> species, ViridianSettings settings, bool includeFill)
    {
        var dxf = new DxfWriter();
        dxf.Layers[LayerOutline] = DxfWriter.NearestAci(Rgb.Parse(settings.StyleOf(SymbolTier.Outline).Color, new Rgb(46, 74, 54)));
        dxf.Layers[LayerStructure] = DxfWriter.NearestAci(Rgb.Parse(settings.StyleOf(SymbolTier.Essential).Color, new Rgb(94, 74, 54)));
        dxf.Layers[LayerDetail] = DxfWriter.NearestAci(Rgb.Parse(settings.StyleOf(SymbolTier.Detail).Color, new Rgb(79, 107, 85)));
        dxf.Layers[LayerFill] = 3;
        dxf.Layers[LayerFlowers] = 1;
        dxf.Layers[LayerText] = 8;

        double cell = Math.Max(2, species.Count == 0 ? 2 : species.Max(s => s.Species.CrownDiameterM) * 1.25);
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(species.Count)));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < species.Count; i++)
        {
            (Species s, string code) = species[i];
            string name = UniqueName(BlockName(code, s), names);
            PlantSymbol symbol = SymbolGenerator.Generate(s.SymbolStyle, s.CrownDiameterM, s.EffectiveSymbolSeed);
            dxf.AddBlock(name, Entities(symbol, SymbolPaint.For(s, settings.Colors), includeFill));

            var at = new Point2(i % columns * cell, -(i / columns) * (cell + 1.2));
            double textHeight = Math.Clamp(cell * 0.05, 0.1, 0.6);
            dxf.AddEntity(w => w.Insert(LayerOutline, name, at));
            dxf.AddEntity(w => w.Text(LayerText, at + new Point2(0, -s.CrownDiameterM / 2 - textHeight * 1.6), textHeight, code));
            dxf.AddEntity(w => w.Text(LayerText, at + new Point2(0, -s.CrownDiameterM / 2 - textHeight * 3.2), textHeight * 0.8, s.PopularName + " - " + s.ScientificName));
        }

        return dxf;
    }

    /// <summary>Nome de bloco válido em qualquer versão de CAD: VIR_AR-001.</summary>
    public static string BlockName(string code, Species species)
    {
        string source = string.IsNullOrWhiteSpace(code) ? species.ScientificName : code;
        var sb = new StringBuilder("VIR_");
        foreach (char c in source.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? char.ToUpperInvariant(c) : '_');
        }

        return sb.Length > 31 ? sb.ToString(0, 31) : sb.ToString();
    }

    private static string UniqueName(string name, HashSet<string> used)
    {
        string candidate = name;
        for (int i = 2; !used.Add(candidate); i++)
        {
            string suffix = "_" + i.ToString(CultureInfo.InvariantCulture);
            candidate = (name.Length + suffix.Length > 31 ? name[..(31 - suffix.Length)] : name) + suffix;
        }

        return candidate;
    }

    private static List<Action<DxfWriter>> Entities(PlantSymbol symbol, SymbolPaint paint, bool includeFill)
    {
        var list = new List<Action<DxfWriter>>();
        if (includeFill && paint.Mode != CrownFillMode.Nenhum && symbol.FillOutline.Count >= 3)
        {
            // O contorno de preenchimento é radial em torno do centro: o leque de triângulos cobre a copa.
            int color = DxfWriter.NearestAci(paint.Crown);
            IReadOnlyList<Point2> o = symbol.FillOutline;
            Point2 center = default;
            for (int i = 0; i < o.Count; i++)
            {
                Point2 a = o[i];
                Point2 b = o[(i + 1) % o.Count];
                list.Add(w => w.Solid(LayerFill, center, a, b, color));
            }
        }

        if (includeFill && paint.Flower is Rgb flower)
        {
            int color = DxfWriter.NearestAci(flower);
            foreach (FlowerSpot spot in symbol.FlowerSpots)
            {
                list.Add(w => w.Circle(LayerFlowers, spot.Center, spot.Radius, color));
            }
        }

        foreach (SymbolPrimitive primitive in symbol.ForProfile(Scale.DetailProfile.Detalhado))
        {
            string layer = primitive.Tier switch
            {
                SymbolTier.Outline or SymbolTier.MinimalOutline or SymbolTier.Center => LayerOutline,
                SymbolTier.Essential or SymbolTier.Principal => LayerStructure,
                _ => LayerDetail,
            };
            switch (primitive)
            {
                case SymbolLine line:
                    list.Add(w => w.Line(layer, line.A, line.B));
                    break;
                case SymbolArc arc when ArcInfo.TryFromThreePoints(arc.Start, arc.Mid, arc.End, out ArcInfo info):
                {
                    double start = info.StartAngle * 180 / Math.PI;
                    double end = (info.StartAngle + info.Sweep) * 180 / Math.PI;
                    (double from, double to) = info.Sweep >= 0 ? (start, end) : (end, start);
                    if (Math.Abs(info.Sweep) >= 2 * Math.PI - 1e-9)
                    {
                        list.Add(w => w.Circle(layer, info.Center, info.Radius));
                    }
                    else
                    {
                        list.Add(w => w.Arc(layer, info.Center, info.Radius, from, to));
                    }

                    break;
                }

                case SymbolArc arc:
                    list.Add(w => w.Line(layer, arc.Start, arc.End));
                    break;
            }
        }

        return list;
    }
}
