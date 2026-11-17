using Viridian.Core.Model;

namespace Viridian.Core.Maintenance;

/// <summary>Fase em que a atividade acontece.</summary>
public enum MaintenancePhase
{
    /// <summary>Período de implantação e pegamento (primeiro ano após o plantio).</summary>
    Implantacao = 0,

    /// <summary>Manutenção permanente, todos os anos.</summary>
    Permanente = 1,
}

/// <summary>Tipo de atividade de manutenção.</summary>
public enum MaintenanceActivity
{
    Rega,
    Adubacao,
    Poda,
    Limpeza,
    Corte,
    Reposicao,
    Coroamento,
    Tutoramento,
}

/// <summary>Uma linha do cronograma: atividade, espécie, meses e frequência.</summary>
public sealed class MaintenanceTask
{
    public MaintenancePhase Phase { get; set; }

    public MaintenanceActivity Activity { get; set; }

    public string SpeciesCode { get; set; } = string.Empty;

    public string PopularName { get; set; } = string.Empty;

    public string ScientificName { get; set; } = string.Empty;

    /// <summary>Descrição da atividade (por exemplo, "Poda de limpeza após a floração").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Meses em que a atividade acontece.</summary>
    public MonthSet Months { get; set; }

    /// <summary>Frequência dentro dos meses marcados ("semanal", "2 vezes por semana"...).</summary>
    public string Frequency { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public string PhaseName => Phase == MaintenancePhase.Implantacao ? "Implantação (1º ano)" : "Permanente";

    public string ActivityName => MaintenancePlanner.Name(Activity);
}

/// <summary>Espécie presente no projeto, com as quantidades que interessam à manutenção.</summary>
public sealed class MaintenanceItem
{
    public Species Species { get; init; } = new();

    public string SpeciesCode { get; init; } = string.Empty;

    /// <summary>Exemplares novos (implantação).</summary>
    public int NewCount { get; init; }

    /// <summary>Exemplares existentes e transplantados mantidos.</summary>
    public int KeptCount { get; init; }

    /// <summary>Área em maciços, em m².</summary>
    public double AreaM2 { get; init; }

    /// <summary>Mudas de reserva previstas para reposição.</summary>
    public int Reserve { get; init; }
}

/// <summary>Parâmetros regionais do cronograma.</summary>
public sealed class MaintenanceSettings
{
    /// <summary>Mês previsto para o plantio (1 a 12). O padrão é outubro, início das chuvas na maior parte do Brasil.</summary>
    public int PlantingMonth { get; set; } = 10;

    /// <summary>Estação seca da região (rega de manutenção e podas de inverno).</summary>
    public string DrySeason { get; set; } = "mai–set";

    public void Normalize()
    {
        PlantingMonth = PlantingMonth is >= 1 and <= 12 ? PlantingMonth : 10;
        if (MonthSet.Parse(DrySeason).IsEmpty)
        {
            DrySeason = "mai–set";
        }
    }
}

/// <summary>
/// Gera o cronograma de manutenção por espécie e época: rega de implantação e de estiagem, adubação,
/// podas (respeitando a floração), limpeza, corte de gramados, coroamento, tutoramento e reposição de falhas.
/// As recomendações são de boas práticas gerais; o responsável técnico deve ajustá-las ao clima local.
/// </summary>
public static class MaintenancePlanner
{
    public static string Name(MaintenanceActivity activity) => activity switch
    {
        MaintenanceActivity.Rega => "Rega",
        MaintenanceActivity.Adubacao => "Adubação",
        MaintenanceActivity.Poda => "Poda",
        MaintenanceActivity.Limpeza => "Limpeza",
        MaintenanceActivity.Corte => "Corte",
        MaintenanceActivity.Reposicao => "Reposição",
        MaintenanceActivity.Coroamento => "Coroamento",
        MaintenanceActivity.Tutoramento => "Tutoramento",
        _ => activity.ToString(),
    };

    public static IReadOnlyList<MaintenanceTask> Build(IEnumerable<MaintenanceItem> items, MaintenanceSettings settings)
    {
        settings ??= new MaintenanceSettings();
        settings.Normalize();
        MonthSet dry = MonthSet.Parse(settings.DrySeason);
        MonthSet rainy = new(MonthSet.AllMask & ~dry.Mask);
        int pm = settings.PlantingMonth;
        MonthSet firstQuarter = MonthSet.Range(pm, Month(pm, 2));    // 3 meses a partir do plantio
        MonthSet firstYear = MonthSet.All;
        var tasks = new List<MaintenanceTask>();

        foreach (MaintenanceItem item in items.Where(i => i.NewCount > 0 || i.KeptCount > 0 || i.AreaM2 > 0)
                     .OrderBy(i => i.SpeciesCode, StringComparer.OrdinalIgnoreCase))
        {
            Species s = item.Species;
            bool isNew = item.NewCount > 0 || item.AreaM2 > 0;
            MonthSet flowering = MonthSet.Parse(s.Flowering);
            bool flowers = !flowering.IsEmpty && flowering.Count < 12;

            void Add(MaintenancePhase phase, MaintenanceActivity activity, string title, MonthSet months, string frequency, string notes = "")
            {
                if (months.IsEmpty)
                {
                    return;
                }

                tasks.Add(new MaintenanceTask
                {
                    Phase = phase,
                    Activity = activity,
                    SpeciesCode = item.SpeciesCode,
                    PopularName = s.PopularName,
                    ScientificName = s.ScientificName,
                    Title = title,
                    Months = months,
                    Frequency = frequency,
                    Notes = notes,
                });
            }

            // ---------------------------------------------------------------- implantação
            if (isNew)
            {
                (string start, string after) = s.Water switch
                {
                    WaterNeed.Alta => ("diária", "3 vezes por semana"),
                    WaterNeed.Baixa => ("2 vezes por semana", "semanal"),
                    _ => ("3 vezes por semana", "2 vezes por semana"),
                };
                if (s.Category == PlantCategory.Aquatica)
                {
                    Add(MaintenancePhase.Implantacao, MaintenanceActivity.Rega, "Conferência do nível d'água", firstQuarter, "semanal",
                        "Manter a lâmina d'água indicada para a espécie; repor a água evaporada.");
                }
                else
                {
                    Add(MaintenancePhase.Implantacao, MaintenanceActivity.Rega, "Rega de implantação", firstQuarter,
                        $"{start} no 1º mês; depois {after}",
                        "Regar no início da manhã ou no fim da tarde, molhando todo o torrão. Suspender em dias de chuva.");
                }

                Add(MaintenancePhase.Implantacao, MaintenanceActivity.Reposicao, "Vistoria e reposição de falhas",
                    MonthSet.None.With(Month(pm, 1)).With(Month(pm, 3)), "aos 30 e aos 90 dias",
                    item.Reserve > 0
                        ? $"Repor mudas mortas ou debilitadas com a reserva prevista ({item.Reserve} {(SupplyIsArea(s) ? "m²" : "un")})."
                        : "Repor mudas mortas ou debilitadas por outras da mesma espécie e porte.");

                if (s.Category is PlantCategory.Arvore or PlantCategory.Palmeira)
                {
                    Add(MaintenancePhase.Implantacao, MaintenanceActivity.Tutoramento, "Conferência de tutores e amarrios", MonthSet.None.With(Month(pm, 2)).With(Month(pm, 5)).With(Month(pm, 8)).With(Month(pm, 11)),
                        "trimestral", "Afrouxar amarrios que estejam estrangulando o tronco; retirar o tutor quando a planta se sustentar (12 a 24 meses).");
                    Add(MaintenancePhase.Implantacao, MaintenanceActivity.Coroamento, "Coroamento", firstYear, "mensal",
                        "Manter um círculo de cerca de 1 m de diâmetro livre de mato ao redor do tronco, com cobertura morta.");
                }
                else if (s.Category is PlantCategory.Arbusto or PlantCategory.Herbacea or PlantCategory.Forracao or PlantCategory.Trepadeira)
                {
                    Add(MaintenancePhase.Implantacao, MaintenanceActivity.Coroamento, "Capina manual dos canteiros", firstYear, "mensal",
                        "Retirar plantas invasoras até o fechamento do canteiro; repor a cobertura morta.");
                }
            }

            // ---------------------------------------------------------------- rega de estiagem
            if (s.Category != PlantCategory.Aquatica && s.Water != WaterNeed.Baixa)
            {
                string frequency = s.Water == WaterNeed.Alta ? "2 a 3 vezes por semana" : "semanal";
                if (s.Category is PlantCategory.Arvore or PlantCategory.Palmeira)
                {
                    frequency = s.Water == WaterNeed.Alta ? "semanal" : "quinzenal";
                }

                Add(MaintenancePhase.Permanente, MaintenanceActivity.Rega, "Rega na estiagem", dry, frequency,
                    "Ajustar à chuva do período; árvores e palmeiras adultas costumam dispensar rega.");
            }

            // ---------------------------------------------------------------- adubação
            MonthSet fertilizing = s.Category switch
            {
                PlantCategory.Arvore or PlantCategory.Palmeira => MonthSet.None.With(First(rainy)).With(Month(Last(rainy), 0)),
                PlantCategory.Gramado => MonthSet.None.With(First(rainy)).With(Month(First(rainy), 2)).With(Month(Last(rainy), 0)),
                PlantCategory.Aquatica => MonthSet.None,
                _ => MonthSet.None.With(First(rainy)).With(Month(First(rainy), 3)).With(Month(Last(rainy), 0)),
            };
            if (flowers && s.Category != PlantCategory.Gramado)
            {
                // Adubação de pré-floração: um mês antes de começar a florir.
                fertilizing = fertilizing.With(Month(FirstOf(flowering), -1));
            }

            Add(MaintenancePhase.Permanente, MaintenanceActivity.Adubacao, flowers ? "Adubação de cobertura e de pré-floração" : "Adubação de cobertura", fertilizing,
                "1 vez em cada mês marcado",
                s.Category == PlantCategory.Gramado
                    ? "Adubo NPK para gramados, com o solo úmido."
                    : "Adubo orgânico ou NPK na projeção da copa, incorporado levemente e seguido de rega.");

            // ---------------------------------------------------------------- podas, cortes e limpeza
            MonthSet afterFlowering = flowers ? flowering.Shift(1) : MonthSet.None;
            MonthSet afterFloweringFirst = flowers ? MonthSet.None.With(Month(LastOf(flowering), 1)) : MonthSet.None;
            switch (s.Category)
            {
                case PlantCategory.Arvore:
                    if (isNew)
                    {
                        Add(MaintenancePhase.Implantacao, MaintenanceActivity.Poda, "Poda de formação", MonthSet.None.With(Middle(dry)),
                            "anual nos 3 primeiros anos", "Retirar brotações baixas e galhos mal posicionados, mantendo uma haste principal. Nunca retirar mais de 25% da copa.");
                    }

                    Add(MaintenancePhase.Permanente, MaintenanceActivity.Poda, flowers ? "Poda de limpeza (após a floração)" : "Poda de limpeza", flowers ? afterFloweringFirst : MonthSet.None.With(Middle(dry)),
                        "anual", "Galhos secos, doentes ou que conflitem com redes, iluminação e circulação. Poda por profissional habilitado e conforme a legislação municipal.");
                    break;

                case PlantCategory.Palmeira:
                    Add(MaintenancePhase.Permanente, MaintenanceActivity.Limpeza, "Retirada de folhas secas e cachos", MonthSet.None.With(First(rainy)).With(Month(First(rainy), 6)),
                        "semestral", "Retirar somente folhas totalmente secas; não cortar o palmito (ponteira).");
                    break;

                case PlantCategory.Arbusto:
                    Add(MaintenancePhase.Permanente, MaintenanceActivity.Poda, flowers ? "Poda de condução (após a floração)" : "Poda de condução", flowers ? afterFloweringFirst.Union(MonthSet.None.With(Month(First(rainy), 3))) : MonthSet.None.With(First(rainy)).With(Month(First(rainy), 3)).With(Month(First(rainy), 6)),
                        flowers ? "após cada florada" : "3 vezes por ano", "Manter forma e altura de projeto; em cercas vivas, podar a base mais larga que o topo.");
                    break;

                case PlantCategory.Herbacea:
                    if (flowers)
                    {
                        Add(MaintenancePhase.Permanente, MaintenanceActivity.Limpeza, "Retirada de flores murchas e folhas secas", flowering.Union(afterFlowering), "quinzenal",
                            "Prolonga a floração e mantém o canteiro limpo.");
                    }
                    else
                    {
                        Add(MaintenancePhase.Permanente, MaintenanceActivity.Limpeza, "Retirada de folhas secas", MonthSet.None.With(First(rainy)).With(Month(First(rainy), 4)).With(Month(First(rainy), 8)), "quadrimestral");
                    }

                    Add(MaintenancePhase.Permanente, MaintenanceActivity.Poda, "Divisão e replantio de touceiras", MonthSet.None.With(First(dry)), "a cada 2 ou 3 anos",
                        "Quando as touceiras ficarem adensadas ou com o centro falhado.");
                    break;

                case PlantCategory.Forracao:
                    Add(MaintenancePhase.Permanente, MaintenanceActivity.Poda, "Acabamento de bordas e limpeza", MonthSet.None.With(First(rainy)).With(Month(First(rainy), 3)).With(Month(First(rainy), 6)).With(Month(First(rainy), 9)),
                        "trimestral", "Conter o avanço sobre caminhos e canteiros vizinhos.");
                    break;

                case PlantCategory.Gramado:
                    Add(MaintenancePhase.Permanente, MaintenanceActivity.Corte, "Corte do gramado (crescimento ativo)", rainy, "quinzenal",
                        "Não retirar mais de 1/3 da altura das folhas por corte; recolher as aparas.");
                    Add(MaintenancePhase.Permanente, MaintenanceActivity.Corte, "Corte do gramado (estiagem)", dry, "mensal",
                        "Manter o gramado um pouco mais alto no período seco.");
                    break;

                case PlantCategory.Trepadeira:
                    if (isNew)
                    {
                        Add(MaintenancePhase.Implantacao, MaintenanceActivity.Tutoramento, "Condução e amarração nos suportes", firstYear, "mensal",
                            "Direcionar os ramos para a estrutura até a cobertura se firmar.");
                    }

                    Add(MaintenancePhase.Permanente, MaintenanceActivity.Poda, flowers ? "Poda de limpeza (após a floração)" : "Poda de limpeza", flowers ? afterFloweringFirst : MonthSet.None.With(Middle(dry)),
                        "anual", "Retirar ramos secos e o excesso que pese sobre a estrutura.");
                    break;

                case PlantCategory.Aquatica:
                    Add(MaintenancePhase.Permanente, MaintenanceActivity.Limpeza, "Retirada de folhas velhas e controle do excesso", rainy, "mensal",
                        "Não deixar as plantas cobrirem mais de 2/3 do espelho d'água.");
                    break;
            }
        }

        return tasks
            .OrderBy(t => t.Phase)
            .ThenBy(t => t.Activity)
            .ThenBy(t => t.SpeciesCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool SupplyIsArea(Species s) => s.SupplyUnit.Contains('²') || s.SupplyUnit.Equals("m2", StringComparison.OrdinalIgnoreCase);

    /// <summary>Mês deslocado circularmente (1 a 12).</summary>
    public static int Month(int month, int offset) => ((month - 1 + offset) % 12 + 12) % 12 + 1;

    private static int FirstOf(MonthSet set) => Enumerable.Range(1, 12).FirstOrDefault(m => set.Contains(m) && !set.Contains(Month(m, -1)), Enumerable.Range(1, 12).FirstOrDefault(set.Contains, 1));

    private static int LastOf(MonthSet set) => Enumerable.Range(1, 12).FirstOrDefault(m => set.Contains(m) && !set.Contains(Month(m, 1)), Enumerable.Range(1, 12).LastOrDefault(set.Contains, 12));

    private static int First(MonthSet season) => FirstOf(season);

    private static int Last(MonthSet season) => LastOf(season);

    /// <summary>Mês do meio de uma estação (por exemplo, julho em mai–set).</summary>
    private static int Middle(MonthSet season) => Month(FirstOf(season), season.Count / 2);
}
