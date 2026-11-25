namespace Viridian.Core.Colors;

/// <summary>Uma cor com nome, para escolher com um clique.</summary>
public readonly record struct NamedColor(string Name, Rgb Color);

/// <summary>Para que serve a cor escolhida (define a paleta oferecida).</summary>
public enum ColorPurpose
{
    Flor = 0,
    Folhagem = 1,
    Fruto = 2,
    Pedra = 3,
}

/// <summary>
/// Paletas prontas para o seletor de cores: tons comuns de flores, folhagens, frutos e pedras no paisagismo
/// brasileiro, com nome (ex.: "Amarelo-ipê", "Rosa-azaleia", "Verde-musgo"). A cor pode ser ajustada depois.
/// </summary>
public static class ColorChoices
{
    public static IReadOnlyList<NamedColor> For(ColorPurpose purpose) => purpose switch
    {
        ColorPurpose.Flor => Flowers,
        ColorPurpose.Folhagem => Foliage,
        ColorPurpose.Fruto => Fruits,
        _ => Stones,
    };

    private static NamedColor C(string name, string hex) => new(name, Rgb.TryParse(hex, out Rgb c) ? c : Rgb.Black);

    public static readonly IReadOnlyList<NamedColor> Flowers = new[]
    {
        C("Amarelo-ipê", "#F2C21E"), C("Amarelo-ouro", "#E8A90F"), C("Amarelo-limão", "#EDE35A"), C("Creme", "#F3E6B8"),
        C("Laranja", "#F08A24"), C("Salmão", "#F4A07A"), C("Coral", "#EE6A4F"), C("Vermelho-flamboyant", "#D7301F"),
        C("Vermelho", "#C21E2B"), C("Vinho", "#7E1E35"), C("Rosa-claro", "#F5B6C8"), C("Rosa-azaleia", "#E8608E"),
        C("Pink", "#D9307A"), C("Magenta", "#B2287A"), C("Lilás", "#C3A3D9"), C("Lavanda", "#9C86C9"),
        C("Roxo-ipê", "#B05BA8"), C("Roxo", "#6E3B8E"), C("Azul-agapanto", "#6F86D6"), C("Azul", "#3E5BB0"),
        C("Branco", "#F7F5EF"), C("Branco-esverdeado", "#E6EED6"), C("Verde-claro", "#C4D88A"), C("Marrom-avermelhado", "#8E4A2E"),
    };

    public static readonly IReadOnlyList<NamedColor> Foliage = new[]
    {
        C("Verde-claro", "#8DBE5A"), C("Verde-limão", "#A9C94A"), C("Verde-folha", "#5E9A3C"), C("Verde-médio", "#4C8A3A"),
        C("Verde-escuro", "#2F6B2E"), C("Verde-musgo", "#556B2F"), C("Verde-oliva", "#6F7B3A"), C("Verde-azulado", "#4F8C7A"),
        C("Verde-acinzentado", "#7F9A86"), C("Prateado", "#A8B4AC"), C("Azul-acinzentado", "#7F97A3"), C("Verde-água", "#7FB8A8"),
        C("Amarelo-esverdeado", "#C9C94A"), C("Dourado", "#D2B54A"), C("Variegado creme", "#D8DDA8"), C("Bronze", "#8E6E3A"),
        C("Bordô", "#6E2236"), C("Roxo-escuro", "#4A2A4E"), C("Vermelho", "#A63A2E"), C("Laranja-cróton", "#D2793A"),
    };

    public static readonly IReadOnlyList<NamedColor> Fruits = new[]
    {
        C("Vermelho", "#C3262D"), C("Laranja", "#EE8A1F"), C("Amarelo", "#F0C629"), C("Verde", "#7AA23A"),
        C("Roxo-jabuticaba", "#3A1E3E"), C("Preto", "#24211F"), C("Marrom", "#7A4E2A"), C("Rosa", "#E37A9A"),
        C("Bordô", "#7A1E2E"), C("Creme", "#EEE2B8"),
    };

    public static readonly IReadOnlyList<NamedColor> Stones = new[]
    {
        C("Branco", "#EEECE6"), C("Bege", "#DCCDAE"), C("Areia", "#D8C79E"), C("Cinza-claro", "#B8B3AA"),
        C("Cinza", "#8E8A84"), C("Grafite", "#4A4A48"), C("Preto", "#2E2E2C"), C("Terracota", "#B0603E"),
        C("Vermelho", "#A65E46"), C("Ocre", "#C4943E"), C("Marrom", "#7C5E43"), C("Rosado", "#C9A79A"),
    };

    /// <summary>
    /// Cor representativa de um trecho de imagem (pixels RGBA): a mediana de cada canal, que ignora pontos de
    /// brilho e sombra isolados. Nulo quando não há pixels opacos.
    /// </summary>
    public static Rgb? Sample(IReadOnlyList<(byte R, byte G, byte B, byte A)> pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var opaque = pixels.Where(p => p.A > 128).ToList();
        if (opaque.Count == 0)
        {
            return null;
        }

        static byte Median(IEnumerable<byte> values)
        {
            List<byte> sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        return new Rgb(Median(opaque.Select(p => p.R)), Median(opaque.Select(p => p.G)), Median(opaque.Select(p => p.B)));
    }
}
