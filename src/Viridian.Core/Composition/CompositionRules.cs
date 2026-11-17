using System.Globalization;
using Viridian.Core.Model;

namespace Viridian.Core.Composition;

/// <summary>Espécie presente no canteiro.</summary>
/// <param name="Count">Exemplares (ou unidades do maciço).</param>
/// <param name="AreaM2">Área ocupada em maciços (zero para exemplares isolados).</param>
public sealed record BedPlant(Species Species, string Code, int Count, double AreaM2);

/// <summary>Canteiro avaliado.</summary>
public sealed record Bed(string Name, IReadOnlyList<BedPlant> Plants, double AreaM2, double WidthM, double DepthM);

public enum CompositionSeverity
{
    Info = 0,
    Aviso = 1,
}

/// <param name="SpeciesId">Espécie a que a observação se refere (vazio quando vale para o canteiro todo).</param>
public sealed record CompositionNote(CompositionSeverity Severity, string Rule, string Message, string SpeciesId = "");

/// <summary>Configuração das regras de composição (recomendações, nunca bloqueios).</summary>
public sealed class CompositionSettings
{
    /// <summary>Participação máxima de uma espécie no canteiro (por área, ou por quantidade quando não há área).</summary>
    public double MaxSharePercent { get; set; } = 30;

    /// <summary>Quantidade mínima de espécies em canteiros a partir da área informada.</summary>
    public int MinSpecies { get; set; } = 3;

    public double MinAreaForDiversityM2 { get; set; } = 20;

    /// <summary>A copa adulta não deve passar desta fração da menor dimensão do canteiro.</summary>
    public double MaxCrownToBedPercent { get; set; } = 100;

    public bool CheckShare { get; set; } = true;

    public bool CheckDiversity { get; set; } = true;

    public bool CheckTexture { get; set; } = true;

    public bool CheckScale { get; set; } = true;

    public bool CheckPalmUnderstory { get; set; } = true;

    public bool CheckFlowering { get; set; } = true;

    public void Normalize()
    {
        MaxSharePercent = double.IsFinite(MaxSharePercent) ? Math.Clamp(MaxSharePercent, 5, 100) : 30;
        MinSpecies = Math.Clamp(MinSpecies, 1, 20);
        MinAreaForDiversityM2 = double.IsFinite(MinAreaForDiversityM2) ? Math.Clamp(MinAreaForDiversityM2, 0, 10000) : 20;
        MaxCrownToBedPercent = double.IsFinite(MaxCrownToBedPercent) ? Math.Clamp(MaxCrownToBedPercent, 20, 300) : 100;
    }
}

/// <summary>
/// Regras de composição do canteiro, do ponto de vista do paisagista: equilíbrio entre espécies, diversidade,
/// contraste de textura, escala em relação ao canteiro, sub-bosque de palmeiras e floração ao longo do ano.
/// </summary>
public static class CompositionRules
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static IReadOnlyList<CompositionNote> Evaluate(Bed bed, CompositionSettings settings)
    {
        settings ??= new CompositionSettings();
        settings.Normalize();
        var notes = new List<CompositionNote>();
        List<BedPlant> plants = bed.Plants.Where(p => p.Count > 0 || p.AreaM2 > 0).ToList();
        if (plants.Count == 0)
        {
            notes.Add(new(CompositionSeverity.Info, "Canteiro", "Não há vegetação do catálogo dentro do canteiro."));
            return notes;
        }

        // Participação de cada espécie: pela área quando há maciços, senão pela quantidade.
        double totalArea = plants.Sum(p => p.AreaM2);
        double totalCount = plants.Sum(p => p.Count);
        if (settings.CheckShare && plants.Count > 1 && (totalArea > 0 || totalCount >= 6))
        {
            // Com poucas espécies, a divisão igual já passaria do limite: tolera 1,5 vez a parte igual.
            double limit = Math.Max(settings.MaxSharePercent, 150.0 / plants.Count);
            foreach (BedPlant p in plants)
            {
                double share = totalArea > 0 ? p.AreaM2 / totalArea : p.Count / Math.Max(1, totalCount);
                if (share * 100 > limit + 1e-9 && (totalArea > 0 ? p.AreaM2 > 0 : p.Count > 1))
                {
                    notes.Add(new(CompositionSeverity.Aviso, "Equilíbrio",
                        string.Format(Pt, "{0} ocupa {1:0}% do canteiro (recomendado até {2:0}%). Considere dividir o espaço com outras espécies.",
                            Name(p), share * 100, limit), p.Species.Id));
                }
            }
        }

        if (settings.CheckDiversity && bed.AreaM2 >= settings.MinAreaForDiversityM2 && plants.Count < settings.MinSpecies)
        {
            notes.Add(new(CompositionSeverity.Aviso, "Diversidade",
                string.Format(Pt, "Canteiro de {0:0} m² com {1} espécie(s). Recomendado ao menos {2} para dar riqueza e resiliência.",
                    bed.AreaM2, plants.Count, settings.MinSpecies)));
        }

        if (settings.CheckTexture && plants.Count >= 3)
        {
            var textures = plants.Select(p => p.Species.EffectiveTexture).Where(t => t != Texture.NaoInformada).Distinct().ToList();
            if (textures.Count == 1)
            {
                notes.Add(new(CompositionSeverity.Aviso, "Textura",
                    $"Todas as espécies têm textura {Labels.Of(textures[0]).ToLowerInvariant()}. Inclua folhagens de textura diferente para criar contraste."));
            }
        }

        if (settings.CheckScale && bed.WidthM > 0 && bed.DepthM > 0)
        {
            double smaller = Math.Min(bed.WidthM, bed.DepthM);
            foreach (BedPlant p in plants.Where(p => p.Species.Category is PlantCategory.Arvore or PlantCategory.Palmeira or PlantCategory.Arbusto))
            {
                if (p.Species.CrownDiameterM > smaller * settings.MaxCrownToBedPercent / 100 + 1e-9)
                {
                    notes.Add(new(CompositionSeverity.Aviso, "Escala",
                        string.Format(Pt, "{0}: copa adulta de {1:0.0} m em canteiro com {2:0.0} m na menor dimensão. A copa vai avançar sobre o entorno.",
                            Name(p), p.Species.CrownDiameterM, smaller), p.Species.Id));
                }
            }

            bool monumental = plants.Any(p => p.Species.Scale == VisualScale.Monumental);
            if (monumental && bed.AreaM2 > 0 && bed.AreaM2 < 25)
            {
                notes.Add(new(CompositionSeverity.Info, "Escala",
                    "Há espécie de porte monumental em canteiro pequeno: confira a relação com edificações e o espaço para as raízes."));
            }
        }

        if (settings.CheckPalmUnderstory)
        {
            bool palms = plants.Any(p => p.Species.Category == PlantCategory.Palmeira);
            bool ground = plants.Any(p => p.Species.Category is PlantCategory.Arbusto or PlantCategory.Herbacea or PlantCategory.Forracao or PlantCategory.Gramado);
            if (palms && !ground)
            {
                notes.Add(new(CompositionSeverity.Aviso, "Sub-bosque",
                    "Palmeiras sem plantio de base: complete com forração, herbáceas ou arbustos para assentar o conjunto e proteger o solo."));
            }
        }

        if (settings.CheckFlowering && plants.Count >= 3)
        {
            FloweringCalendar calendar = FloweringCalendar.Build(plants.Select(p => (p.Species, p.Code, (double)p.Count)));
            MonthSet gaps = calendar.ShowyGaps;
            if (calendar.Rows.Any(r => r.Showy) && !gaps.IsEmpty && gaps.Count < 12)
            {
                notes.Add(new(CompositionSeverity.Info, "Floração", $"Sem floração vistosa em: {gaps}. Considere espécies que floresçam nesses meses."));
            }
            else if (!calendar.Rows.Any(r => r.Showy))
            {
                notes.Add(new(CompositionSeverity.Info, "Floração", "Nenhuma espécie de floração vistosa: o canteiro é só de folhagens."));
            }
        }

        return notes;
    }

    private static string Name(BedPlant p) => string.IsNullOrWhiteSpace(p.Code) ? p.Species.PopularName : $"{p.Code} {p.Species.PopularName}";
}
