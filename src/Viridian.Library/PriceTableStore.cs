using Viridian.Core.Config;
using Viridian.Core.Costs;

namespace Viridian.Library;

/// <summary>Guarda a tabela de preços importada na pasta de dados da biblioteca.</summary>
public static class PriceTableStore
{
    public static PriceTable? Load(LibraryPaths paths)
    {
        try
        {
            if (File.Exists(paths.PriceTableFile))
            {
                PriceTable? table = ViridianJson.Deserialize<PriceTable>(File.ReadAllText(paths.PriceTableFile));
                if (table is { Items.Count: > 0 })
                {
                    return table;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ViridianLog.Warn("Tabela de preços ilegível: " + paths.PriceTableFile, ex);
        }

        return null;
    }

    public static void Save(LibraryPaths paths, PriceTable table) =>
        SafeFile.WriteAllTextAtomic(paths.PriceTableFile, ViridianJson.Serialize(table));

    public static void Delete(LibraryPaths paths)
    {
        if (File.Exists(paths.PriceTableFile))
        {
            File.Delete(paths.PriceTableFile);
        }
    }
}
