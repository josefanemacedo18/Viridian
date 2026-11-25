using System.Globalization;
using Viridian.Core.Massing;

namespace Viridian.Core.Reports;

/// <summary>Uso de uma espécie no projeto aberto: exemplares isolados e maciços.</summary>
public sealed record SpeciesUsage(string SpeciesId, int Plants, int Massings, double MassingAreaM2)
{
    /// <summary>Resumo curto, por exemplo "12 un. · 2 maciços (15,3 m²)".</summary>
    public string Describe()
    {
        var parts = new List<string>(2);
        if (Plants > 0)
        {
            parts.Add(Plants + " un.");
        }

        if (Massings > 0)
        {
            parts.Add((Massings == 1 ? "1 maciço" : Massings + " maciços")
                      + (MassingAreaM2 > 0 ? " (" + MassingAreaM2.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")) + " m²)" : string.Empty));
        }

        return string.Join(" · ", parts);
    }
}

/// <summary>
/// Lista de espécies do projeto, calculada sempre a partir dos exemplares e maciços existentes (nunca uma lista à parte
/// que possa ficar desatualizada). Exemplares de maciços contam no maciço, não como isolados (os de maciços apagados contam como isolados).
/// </summary>
public static class ProjectSpecies
{
    public static IReadOnlyDictionary<string, SpeciesUsage> Build(IEnumerable<PlantRecord> plants, IEnumerable<MassingRecord> massings)
    {
        ArgumentNullException.ThrowIfNull(plants);
        ArgumentNullException.ThrowIfNull(massings);
        List<MassingRecord> massingList = massings.ToList();
        var massingIds = new HashSet<string>(massingList.Select(m => m.Id.Trim()), StringComparer.OrdinalIgnoreCase);
        var isolated = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (PlantRecord plant in plants)
        {
            if (plant.IsRegistered && !(plant.BelongsToMassing && massingIds.Contains(plant.MassingId.Trim())))
            {
                string id = plant.SpeciesId.Trim();
                isolated[id] = isolated.GetValueOrDefault(id) + 1;
            }
        }

        var beds = new Dictionary<string, (int Count, double Area)>(StringComparer.OrdinalIgnoreCase);
        foreach (MassingRecord massing in massingList)
        {
            if (string.IsNullOrWhiteSpace(massing.SpeciesId))
            {
                continue;
            }

            string id = massing.SpeciesId.Trim();
            (int count, double area) = beds.GetValueOrDefault(id);
            beds[id] = (count + 1, area + (double.IsFinite(massing.AreaM2) && massing.AreaM2 > 0 ? massing.AreaM2 : 0));
        }

        var result = new Dictionary<string, SpeciesUsage>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in isolated.Keys.Concat(beds.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            (int count, double area) = beds.GetValueOrDefault(id);
            result[id] = new SpeciesUsage(id, isolated.GetValueOrDefault(id), count, area);
        }

        return result;
    }
}
