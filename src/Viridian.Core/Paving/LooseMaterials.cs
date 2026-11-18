using System.Globalization;
using Viridian.Core.Colors;

namespace Viridian.Core.Paving;

/// <summary>Tipo do material solto (organiza a biblioteca de pedras e coberturas).</summary>
public enum LooseKind
{
    Seixo = 0,
    Pedrisco = 1,
    Brita = 2,
    Cascalho = 3,
    PedraTriturada = 4,
    PoDePedra = 5,
    Areia = 6,
    Saibro = 7,
    PedraDeMao = 8,
    ArgilaExpandida = 9,
    CoberturaOrganica = 10,
}

/// <summary>Origem do material.</summary>
public enum LooseOrigin
{
    NaturalDeRio = 0,
    Rolado = 1,
    Britado = 2,
    Triturado = 3,
    Industrializado = 4,
    Organico = 5,
}

/// <summary>Usos do material no projeto (combináveis).</summary>
[Flags]
public enum LooseUse
{
    Nenhum = 0,

    /// <summary>Cobertura decorativa de canteiro.</summary>
    Canteiro = 1,

    /// <summary>Faixa de borda de caminho ou de canteiro.</summary>
    Borda = 2,

    /// <summary>Piso solto de circulação (com contenção e base).</summary>
    PisoSolto = 4,

    /// <summary>Piso compactado (pó de pedra, saibro, bica corrida).</summary>
    PisoCompactado = 8,

    /// <summary>Camada drenante (jardim de chuva, valas, drenos).</summary>
    Drenagem = 16,

    /// <summary>Cobertura morta de canteiro (protege o solo e retém umidade).</summary>
    CoberturaMorta = 32,
}

/// <summary>
/// Pedra ou cobertura solta: seixos, pedriscos, britas, cascalhos, pedras trituradas, pó de pedra, saibro, argila
/// expandida e coberturas orgânicas. Densidade, porosidade e perdas são valores de referência, a confirmar com o
/// fornecedor do lote.
/// </summary>
public sealed class LooseMaterial
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public LooseKind Kind { get; set; }

    public LooseOrigin Origin { get; set; }

    /// <summary>Rocha ou matéria-prima ("quartzo", "granito", "pinus").</summary>
    public string Rock { get; set; } = string.Empty;

    /// <summary>Menor grão (mm).</summary>
    public double GrainMinMm { get; set; } = 10;

    /// <summary>Maior grão (mm).</summary>
    public double GrainMaxMm { get; set; } = 20;

    /// <summary>Cor predominante (#RRGGBB).</summary>
    public string Color { get; set; } = "#C9C2B4";

    /// <summary>Cor dos grãos no desenho (#RRGGBB): contraste com a cor predominante.</summary>
    public string GrainColor { get; set; } = "#7D7465";

    public LooseUse Uses { get; set; } = LooseUse.Canteiro;

    /// <summary>Espessura usual da camada decorativa ou de piso (mm).</summary>
    public double LayerMm { get; set; } = 50;

    /// <summary>Densidade aparente solta (t/m³), referência.</summary>
    public double DensityTM3 { get; set; } = 1.5;

    /// <summary>Índice de vazios (%), referência para camadas drenantes.</summary>
    public double PorosityPercent { get; set; } = 38;

    /// <summary>Perda, acomodação e compactação (%), somadas ao volume de compra.</summary>
    public double WastePercent { get; set; } = 10;

    /// <summary>Saco de venda em quilos (zero quando vendido por volume ou a granel).</summary>
    public double BagKg { get; set; } = 20;

    /// <summary>Saco de venda em litros (coberturas orgânicas, argila expandida).</summary>
    public double BagLiters { get; set; }

    public string Notes { get; set; } = string.Empty;

    public string References { get; set; } = string.Empty;

    public string PriceItemCode { get; set; } = string.Empty;

    public bool IsUserDefined { get; set; }

    public double MeanGrainMm => (GrainMinMm + GrainMaxMm) / 2;

    public Rgb ColorRgb => Rgb.Parse(Color, new Rgb(0xC9, 0xC2, 0xB4));

    public Rgb GrainRgb => Rgb.Parse(GrainColor, new Rgb(0x7D, 0x74, 0x65));

    public bool IsOrganic => Kind == LooseKind.CoberturaOrganica || Origin == LooseOrigin.Organico;

    /// <summary>Granulometria em texto ("20 a 30 mm"; "até 4,8 mm").</summary>
    public string GrainText
    {
        get
        {
            var pt = CultureInfo.GetCultureInfo("pt-BR");
            string max = GrainMaxMm.ToString("0.#", pt);
            return GrainMinMm <= 0 ? $"até {max} mm" : $"{GrainMinMm.ToString("0.#", pt)} a {max} mm";
        }
    }

    public LooseMaterial Clone() => (LooseMaterial)MemberwiseClone();

    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("Informe o nome do material.");
        }

        if (!(GrainMinMm >= 0 && GrainMaxMm > 0 && GrainMaxMm <= 500 && GrainMinMm <= GrainMaxMm))
        {
            problems.Add("Granulometria: o menor grão deve ser de 0 a 500 mm e não pode passar do maior.");
        }

        if (!Rgb.TryParse(Color, out _) || !Rgb.TryParse(GrainColor, out _))
        {
            problems.Add("Use cores no formato #RRGGBB.");
        }

        if (Uses == LooseUse.Nenhum)
        {
            problems.Add("Marque ao menos um uso.");
        }

        if (!(LayerMm >= 5 && LayerMm <= 1000))
        {
            problems.Add("A camada deve ficar entre 5 e 1000 mm.");
        }

        if (!(DensityTM3 >= 0 && DensityTM3 <= 5) || !(PorosityPercent >= 0 && PorosityPercent <= 90) || !(WastePercent >= 0 && WastePercent <= 100))
        {
            problems.Add("Densidade de 0 a 5 t/m³, vazios de 0 a 90% e perda de 0 a 100%.");
        }

        if (!(BagKg >= 0 && BagKg <= 2000) || !(BagLiters >= 0 && BagLiters <= 2000))
        {
            problems.Add("Saco de venda inválido.");
        }

        return problems;
    }

    public void Normalize()
    {
        Name = (Name ?? string.Empty).Trim();
        Rock = (Rock ?? string.Empty).Trim();
        Notes ??= string.Empty;
        References ??= string.Empty;
        PriceItemCode = (PriceItemCode ?? string.Empty).Trim();
        if (!Rgb.TryParse(Color, out _))
        {
            Color = "#C9C2B4";
        }

        if (!Rgb.TryParse(GrainColor, out _))
        {
            GrainColor = "#7D7465";
        }

        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = Guid.NewGuid().ToString("N");
        }

        static double F(double v, double min, double max, double fallback) => double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
        GrainMaxMm = F(GrainMaxMm, 0.1, 500, 20);
        GrainMinMm = F(GrainMinMm, 0, GrainMaxMm, 0);
        LayerMm = F(LayerMm, 5, 1000, 50);
        DensityTM3 = F(DensityTM3, 0, 5, 1.5);
        PorosityPercent = F(PorosityPercent, 0, 90, 38);
        WastePercent = F(WastePercent, 0, 100, 10);
        BagKg = F(BagKg, 0, 2000, 0);
        BagLiters = F(BagLiters, 0, 2000, 0);
        if (Uses == LooseUse.Nenhum)
        {
            Uses = LooseUse.Canteiro;
        }
    }
}

/// <summary>Nomes em português.</summary>
public static class LooseLabels
{
    public static readonly IReadOnlyList<LooseUse> AllUses = new[]
    {
        LooseUse.Canteiro, LooseUse.Borda, LooseUse.PisoSolto, LooseUse.PisoCompactado, LooseUse.Drenagem, LooseUse.CoberturaMorta,
    };

    public static string Of(LooseKind k) => k switch
    {
        LooseKind.Seixo => "Seixo",
        LooseKind.Pedrisco => "Pedrisco",
        LooseKind.Brita => "Brita",
        LooseKind.Cascalho => "Cascalho",
        LooseKind.PedraTriturada => "Pedra triturada",
        LooseKind.PoDePedra => "Pó de pedra",
        LooseKind.Areia => "Areia",
        LooseKind.Saibro => "Saibro",
        LooseKind.PedraDeMao => "Pedra de mão",
        LooseKind.ArgilaExpandida => "Argila expandida",
        LooseKind.CoberturaOrganica => "Cobertura orgânica",
        _ => k.ToString(),
    };

    public static string Of(LooseOrigin o) => o switch
    {
        LooseOrigin.NaturalDeRio => "Natural de rio",
        LooseOrigin.Rolado => "Rolado (polido)",
        LooseOrigin.Britado => "Britado",
        LooseOrigin.Triturado => "Triturado",
        LooseOrigin.Industrializado => "Industrializado",
        LooseOrigin.Organico => "Orgânico",
        _ => o.ToString(),
    };

    public static string Of(LooseUse u) => u switch
    {
        LooseUse.Canteiro => "Cobertura de canteiro",
        LooseUse.Borda => "Borda de caminho",
        LooseUse.PisoSolto => "Piso solto",
        LooseUse.PisoCompactado => "Piso compactado",
        LooseUse.Drenagem => "Camada drenante",
        LooseUse.CoberturaMorta => "Cobertura morta",
        _ => u.ToString(),
    };

    /// <summary>Usos marcados, em texto.</summary>
    public static string UsesText(LooseUse uses) =>
        string.Join(", ", AllUses.Where(u => uses.HasFlag(u)).Select(u => Of(u).ToLowerInvariant()));
}

/// <summary>
/// Converte um material solto em tipo de piso do Viridian para cada uso: espessura, camadas, perdas e observações de
/// execução. Assim o material entra no Revit pelo mesmo caminho dos pisos (tipo de piso com camadas, hachura de
/// planta, detalhe em corte e quantitativo).
/// </summary>
public static class LooseMaterialUse
{
    public const string IdPrefix = "mat-";

    private const string BaseLayer = "Base de bica corrida compactada";

    public static string TypeId(LooseMaterial m, LooseUse use) => $"{IdPrefix}{m.Id}-{use}";

    /// <summary>Espessura da camada do material para o uso (mm).</summary>
    public static double Thickness(LooseMaterial m, LooseUse use) => use switch
    {
        LooseUse.PisoSolto => Math.Max(50, m.LayerMm),
        LooseUse.PisoCompactado => Math.Max(50, Math.Min(m.LayerMm, 100)),
        LooseUse.Drenagem => Math.Max(150, m.LayerMm),
        LooseUse.CoberturaMorta => Math.Max(50, m.LayerMm),
        _ => m.LayerMm,
    };

    /// <summary>Tamanho do grão no desenho (mm): grãos muito pequenos são ampliados para a hachura ficar legível.</summary>
    public static double VisualSizeMm(LooseMaterial m) =>
        m.IsOrganic ? Math.Clamp(m.MeanGrainMm * 1.6, 40, 100) : Math.Clamp(m.MeanGrainMm * 1.6, 28, 160);

    public static PavingType ToPaving(LooseMaterial m, LooseUse use)
    {
        double layer = Thickness(m, use);
        List<PavingLayer> layers = use switch
        {
            LooseUse.PisoSolto => new()
            {
                new() { Name = "Manta geotêxtil (bloqueia ervas e separa a base)", Kind = PavingLayerKind.Manta },
                new() { Name = BaseLayer, Kind = PavingLayerKind.Base, ThicknessMm = 100 },
            },
            LooseUse.PisoCompactado => new()
            {
                new() { Name = BaseLayer, Kind = PavingLayerKind.Base, ThicknessMm = 100 },
            },
            LooseUse.Drenagem => new()
            {
                new() { Name = "Manta geotêxtil envolvendo a camada", Kind = PavingLayerKind.Manta },
            },
            LooseUse.CoberturaMorta => new(),
            _ => new()
            {
                new() { Name = "Manta geotêxtil (bloqueia ervas)", Kind = PavingLayerKind.Manta },
            },
        };

        string notes = use switch
        {
            LooseUse.Canteiro => $"Cobertura decorativa de canteiro, camada de {Cm(layer)} sobre manta geotêxtil. Abra a manta em cruz para o plantio das mudas.",
            LooseUse.Borda => $"Faixa de borda de caminho ou canteiro, camada de {Cm(layer)} sobre manta. Contenção lateral com guia, perfil metálico ou pedras maiores para o material não migrar.",
            LooseUse.PisoSolto => $"Piso solto de circulação: camada de {Cm(layer)} sobre manta e base compactada de 10 cm, com contenção lateral. "
                                  + (m.MeanGrainMm > 25 ? "Grãos acima de 2,5 cm dificultam o caminhar e o uso de cadeira de rodas; prefira em áreas de pouca passagem." : "Repor o material periodicamente."),
            LooseUse.PisoCompactado => $"Piso compactado: espalhar em camadas, umedecer e compactar com placa vibratória até {Cm(layer)}, sobre base compactada. Caimento de 1 a 2% para escoar a água.",
            LooseUse.Drenagem => $"Camada drenante de {Cm(layer)}, com {m.PorosityPercent.ToString("0", CultureInfo.InvariantCulture)}% de vazios de referência; envolver com manta geotêxtil para não colmatar.",
            LooseUse.CoberturaMorta => $"Cobertura morta de {Cm(layer)} sobre o solo, afastada 5 cm do colo das plantas. Repor quando a camada baixar.",
            _ => string.Empty,
        };

        return new PavingType
        {
            Id = TypeId(m, use),
            Name = $"{m.Name} — {LooseLabels.Of(use).ToLowerInvariant()}",
            Category = PavingCategory.Cobertura,
            Material = Description(m),
            Pattern = PatternFor(m),
            LengthMm = VisualSizeMm(m),
            WidthMm = VisualSizeMm(m),
            ThicknessMm = layer,
            JointMm = 0,
            AngleDeg = Math.Abs(Geometry.DeterministicRandom.StableHash(m.Id) % 60),
            Color = m.Color,
            JointColor = m.GrainColor,
            SlopePercent = use == LooseUse.PisoCompactado ? 1.5 : 0,
            WastePercent = m.WastePercent,
            Layers = layers,
            Notes = (notes + " " + m.Notes).Trim(),
            References = m.References,
            PriceItemCode = m.PriceItemCode,
            DensityTM3 = m.DensityTM3,
            BagKg = m.BagKg,
            BagLiters = m.BagLiters,
        };
    }

    /// <summary>Desenho dos grãos: seixos arredondados, britas angulosas, lascas para coberturas orgânicas e pontilhado para finos.</summary>
    public static LayingPattern PatternFor(LooseMaterial m) => m.IsOrganic ? LayingPattern.Lascas : m.Kind switch
    {
        LooseKind.Seixo or LooseKind.ArgilaExpandida => LayingPattern.Seixos,
        LooseKind.PoDePedra or LooseKind.Areia or LooseKind.Saibro => LayingPattern.Granular,
        _ when m.GrainMaxMm <= 5 => LayingPattern.Granular,
        _ => LayingPattern.Britas,
    };

    /// <summary>Especificação do material ("Seixo rolado branco (quartzo), 20 a 30 mm").</summary>
    public static string Description(LooseMaterial m)
    {
        string rock = string.IsNullOrWhiteSpace(m.Rock) ? string.Empty : $" ({m.Rock})";
        return $"{m.Name}{rock}, {LooseLabels.Of(m.Origin).ToLowerInvariant()}, {m.GrainText}";
    }

    /// <summary>Quantidade de compra para uma área: volume com perda (m³), peso (t) e sacos.</summary>
    public static (double VolumeM3, double? WeightT, int? Bags) Purchase(LooseMaterial m, LooseUse use, double areaM2)
    {
        double volume = Math.Max(0, areaM2) * Thickness(m, use) / 1000 * (1 + m.WastePercent / 100);
        double? weight = m.DensityTM3 > 0 ? volume * m.DensityTM3 : null;
        int? bags = m.BagKg > 0 && weight is double t ? (int)Math.Ceiling(t * 1000 / m.BagKg - 1e-9)
            : m.BagLiters > 0 ? (int)Math.Ceiling(volume * 1000 / m.BagLiters - 1e-9)
            : null;
        return (volume, weight, bags);
    }

    private static string Cm(double mm) => (mm / 10).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")) + " cm";
}
