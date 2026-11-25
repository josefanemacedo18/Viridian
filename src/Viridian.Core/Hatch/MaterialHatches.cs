namespace Viridian.Core.Hatch;

/// <summary>
/// Hachuras de material para os desenhos técnicos (detalhes, cortes, pisos, jardim de chuva): terra, solo natural,
/// brita, areia, concreto, cobertura morta, madeira e água. São padrões de desenho (medidas em milímetros no papel),
/// separados da biblioteca de hachuras de vegetação: não aparecem na escolha de hachura dos maciços.
/// </summary>
public static class MaterialHatches
{
    public const string Earth = "mat-terra-vegetal";
    public const string NaturalSoil = "mat-solo-natural";
    public const string Compacted = "mat-solo-compactado";
    public const string Gravel = "mat-brita";
    public const string Sand = "mat-areia";
    public const string Concrete = "mat-concreto";
    public const string Mulch = "mat-cobertura-morta";
    public const string Wood = "mat-madeira";
    public const string Water = "mat-agua";
    public const string Membrane = "mat-manta";
    public const string RootBall = "mat-torrao";

    public static readonly IReadOnlyList<HatchPattern> All = Build();

    public static HatchPattern? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    private static List<HatchPattern> Build() => new()
    {
        // Terra vegetal: pontos e traços curtos espalhados, como grãos de terra fofa.
        Pattern(Earth, "VIR Material - Terra vegetal",
            Grid(0, 0, 0, 1.7, 2.3, 0.25, 3.15),
            Grid(37, 0.8, 0.6, 2.1, 2.9, 0.7, 4.3),
            Grid(118, 1.3, 1.9, 1.3, 3.4, 0.5, 5.1)),

        // Solo natural (terreno): diagonais tracejadas com pontos, o símbolo usual de terreno em corte.
        Pattern(NaturalSoil, "VIR Material - Solo natural",
            Grid(45, 0, 0, 1.5, 3.0, 2.2, 1.0, 0.25, 1.0),
            Grid(135, 1.2, 0, 2.0, 6.0, 0.25, 5.75)),

        // Solo compactado: diagonais contínuas próximas, cruzadas por pontos.
        Pattern(Compacted, "VIR Material - Solo compactado",
            Grid(45, 0, 0, 0, 1.6),
            Grid(0, 0.4, 0.4, 1.0, 2.0, 0.25, 1.75)),

        // Brita: pequenos triângulos (três famílias de traços curtos a 0°, 60° e 120°).
        Pattern(Gravel, "VIR Material - Brita",
            Grid(0, 0, 0, 2.2, 3.8, 1.3, 3.1),
            Grid(60, 0, 0, 2.2, 3.8, 1.3, 3.1),
            Grid(120, 1.3, 0, 2.2, 3.8, 1.3, 3.1)),

        // Areia: pontilhado fino e denso.
        Pattern(Sand, "VIR Material - Areia",
            Grid(0, 0, 0, 0.7, 1.1, 0.2, 1.0),
            Grid(63, 0.5, 0.3, 0.9, 1.6, 0.2, 1.9)),

        // Concreto: agregados (triângulos espaçados) e pontos.
        Pattern(Concrete, "VIR Material - Concreto",
            Grid(0, 0, 0, 3.1, 5.0, 1.1, 6.0),
            Grid(60, 0, 0, 3.1, 5.0, 1.1, 6.0),
            Grid(120, 1.1, 0, 3.1, 5.0, 1.1, 6.0),
            Grid(15, 2.0, 1.4, 1.4, 2.6, 0.25, 2.25)),

        // Cobertura morta: lascas curtas inclinadas para os dois lados.
        Pattern(Mulch, "VIR Material - Cobertura morta",
            Grid(25, 0, 0, 1.5, 1.7, 1.4, 1.9),
            Grid(155, 1.1, 0.8, 1.9, 2.1, 1.2, 2.6)),

        // Madeira: veios horizontais contínuos e tracejados.
        Pattern(Wood, "VIR Material - Madeira",
            Grid(0, 0, 0, 0, 2.0),
            Grid(0, 0, 1.0, 3.0, 2.0, 5.0, 2.5)),

        // Água: ondas (traços horizontais alternados).
        Pattern(Water, "VIR Material - Água",
            Grid(0, 0, 0, 3.0, 2.2, 4.0, 2.5)),

        // Manta geotêxtil: tracejado fino.
        Pattern(Membrane, "VIR Material - Manta",
            Grid(0, 0, 0, 0, 0.8, 1.2, 0.8)),

        // Torrão: trama de pontos e pequenas raízes.
        Pattern(RootBall, "VIR Material - Torrão",
            Grid(0, 0, 0, 0.9, 1.4, 0.25, 1.35),
            Grid(75, 0.6, 0.3, 1.6, 2.6, 0.9, 3.4)),
    };

    private static HatchPattern Pattern(string key, string name, params HatchGrid[] grids) => new()
    {
        Key = key,
        Name = name,
        Application = "Desenhos técnicos",
        Target = HatchTarget.Drafting,
        Grids = grids.ToList(),
        IsBuiltIn = true,
    };

    private static HatchGrid Grid(double angle, double ox, double oy, double shift, double offset, params double[] segments) => new()
    {
        AngleDeg = angle,
        OriginX = ox,
        OriginY = oy,
        Shift = shift,
        Offset = offset,
        Segments = segments.ToList(),
    };
}
