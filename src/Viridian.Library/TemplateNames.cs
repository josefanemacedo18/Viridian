namespace Viridian.Library;

/// <summary>
/// Classifica nomes de modelos de família (.rft) do Revit para localizar um modelo adequado à
/// geração dos símbolos de vegetação, independentemente do idioma da instalação.
/// </summary>
public static class TemplateNames
{
    private static readonly string[] Excluded =
    {
        "based", "basead", "basad", "base sur", "basiert", "basato", "hosted", "hosped", "face", "wall", "parede",
        "ceiling", "forro", "floor", "piso", "roof", "telhado", "line", "linha", "pattern", "padrao", "adaptive",
        "adaptativ", "two level", "dois niv", "rpc", "curtain", "cortina", "profile", "perfil", "mass", "annotation",
        "anotac", "tag", "identificador", "detail", "detalhe", "title", "margem", "column", "pilar", "door", "porta",
        "window", "janela", "lighting", "lumin", "electrical", "eletric", "mechanical", "mecanic", "plumbing",
        "hidraul", "structural", "estrutur", "baluster", "balaustre", "rebar", "armadura", "truss", "trelica",
        "symbol", "simbolo", "section", "corte", "elevation", "elevac", "callout", "chamada", "level head", "grid head",
    };

    private static readonly string[] Planting = { "planting", "plantio", "vegeta", "plantacion", "plantation", "pflanz", "piant", "paisag" };

    private static readonly string[] Generic = { "generic model", "modelo generico", "modele generique", "modello generico", "allgemeines modell" };

    private static readonly string[] DetailExcluded =
    {
        "line based", "baseado em linha", "baseada em linha", "linear", "tag", "identificador", "profile", "perfil",
        "annotation", "anotac", "repeating", "repetiv", "title", "margem", "symbol", "simbolo",
    };

    /// <summary>Pontuação de modelos de componente de detalhe 2D (usados para as cores dos símbolos).</summary>
    public static int DetailScore(string? fileNameWithoutExtension)
    {
        string name = TextSearch.Normalize(fileNameWithoutExtension).Replace('_', ' ').Replace('-', ' ');
        if (name.Length == 0 || DetailExcluded.Any(word => name.Contains(word, StringComparison.Ordinal)))
        {
            return 0;
        }

        if (name.Contains("detail item", StringComparison.Ordinal) || name.Contains("item de detalhe", StringComparison.Ordinal)
            || name.Contains("detail component", StringComparison.Ordinal) || name.Contains("componente de detalhe", StringComparison.Ordinal))
        {
            return 100;
        }

        return name.Contains("detail", StringComparison.Ordinal) || name.Contains("detalhe", StringComparison.Ordinal) ? 50 : 0;
    }

    /// <summary>Pontuação do modelo: zero significa "não usar"; quanto maior, mais adequado.</summary>
    public static int Score(string? fileNameWithoutExtension)
    {
        string name = TextSearch.Normalize(fileNameWithoutExtension).Replace('_', ' ').Replace('-', ' ');
        if (name.Length == 0 || Excluded.Any(word => name.Contains(word, StringComparison.Ordinal)))
        {
            return 0;
        }

        int score = 0;
        if (Planting.Any(word => name.Contains(word, StringComparison.Ordinal)))
        {
            score = 100;
        }
        else if (Generic.Any(word => name.Contains(word, StringComparison.Ordinal))
                 || (name.Contains("generi", StringComparison.Ordinal) && name.Contains("model", StringComparison.Ordinal)))
        {
            score = 80;
        }
        else if (name.Contains("site", StringComparison.Ordinal) || name.Contains("terreno", StringComparison.Ordinal))
        {
            score = 40;
        }
        else if (name.Contains("entourage", StringComparison.Ordinal) || name.Contains("entorno", StringComparison.Ordinal))
        {
            score = 30;
        }

        if (score > 0 && (name.Contains("metric", StringComparison.Ordinal) || name.Contains("metrique", StringComparison.Ordinal)))
        {
            score += 3;
        }

        return score;
    }
}
