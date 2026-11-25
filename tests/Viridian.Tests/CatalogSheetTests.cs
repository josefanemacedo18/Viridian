using Viridian.Core.Costs;
using Viridian.Core.Export;
using Viridian.Core.Model;
using Viridian.Library;
using Xunit;

namespace Viridian.Tests;

public class CatalogSheetTests
{
    private static Species Draft(PlantCategory c) => new() { Category = c, CrownDiameterM = 1, SupplyUnit = "muda", Code = "XX-999" };

    /// <summary>Exporta para .xlsx de verdade e lê de volta com o leitor do plugin.</summary>
    private static List<string[]> RoundTrip(IEnumerable<Species> species, Action<List<string[]>>? edit = null)
    {
        string file = Path.Combine(Path.GetTempPath(), "viridian-catalogo-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            XlsxWriter.Write(file, new[] { CatalogSheet.Export(species) });
            List<string[]> rows = PriceTableReader.ReadXlsx(file);
            edit?.Invoke(rows);
            return rows;
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static int Col(List<string[]> rows, string header)
    {
        string[] h = rows.First(r => r.Contains(CatalogSheet.IdHeader));
        return Array.IndexOf(h, header);
    }

    [Fact]
    public void ExportThenImportUnchanged_ChangesNothing()
    {
        List<Species> seed = SeedCatalog.CreateSpecies();
        CatalogImport import = CatalogSheet.Read(RoundTrip(seed), seed, Draft);
        Assert.Empty(import.Problems);
        Assert.False(import.HasChanges);
        Assert.Equal(seed.Count, import.Unchanged);
    }

    [Fact]
    public void EditedCells_UpdateOnlyThoseFields()
    {
        List<Species> seed = SeedCatalog.CreateSpecies().Take(5).ToList();
        Species first = seed.OrderBy(s => s.Code, StringComparer.OrdinalIgnoreCase).First();
        List<string[]> rows = RoundTrip(seed, r =>
        {
            string[] row = r.First(x => x.Length > 0 && x[0] == first.Id);
            row[Col(r, "Preço unitário (R$)")] = "12,50";
            row[Col(r, "Cor da flor")] = "f2c14e";
            row[Col(r, "Tolera seca")] = "alta";
            row[Col(r, "Altura adulta (m)")] = string.Empty; // vazio mantém
        });
        CatalogImport import = CatalogSheet.Read(rows, seed, Draft);
        Assert.Empty(import.Problems);
        Species changed = Assert.Single(import.Updated);
        Assert.Equal(first.Id, changed.Id);
        Assert.Equal(12.5m, changed.UnitPrice);
        Assert.Equal("#F2C14E", changed.FlowerColor);
        Assert.Equal(DroughtTolerance.Alta, changed.Drought);
        Assert.Equal(first.AdultHeightM, changed.AdultHeightM);
        Assert.NotSame(first, changed);
    }

    [Fact]
    public void NewRowsAndInvalidValues_AreReported()
    {
        List<Species> seed = SeedCatalog.CreateSpecies().Take(3).ToList();
        List<string[]> rows = RoundTrip(seed, r =>
        {
            int width = r.First(x => x.Contains(CatalogSheet.IdHeader)).Length;
            string[] fresh = new string[width];
            Array.Fill(fresh, string.Empty);
            fresh[Col(r, "Nome popular")] = "Planta nova";
            fresh[Col(r, "Nome científico")] = "Planta novae";
            fresh[Col(r, "Categoria")] = "Forracao";
            fresh[Col(r, "Copa (m)")] = "0.4";
            r.Add(fresh);

            string[] bad = (string[])r.First(x => x.Length > 0 && x[0] == seed.OrderBy(s => s.Code, StringComparer.OrdinalIgnoreCase).First().Id).Clone();
            r[r.FindIndex(x => x.Length > 0 && x[0] == bad[0])] = bad;
            bad[Col(r, "Água")] = "muita";
            bad[Col(r, "Floração")] = "xyz";

            string[] unknown = (string[])fresh.Clone();
            unknown[0] = "nao-existe";
            r.Add(unknown);
            string[] noCategory = (string[])fresh.Clone();
            noCategory[Col(r, "Categoria")] = string.Empty;
            noCategory[Col(r, "Nome científico")] = "Outra planta";
            r.Add(noCategory);
        });
        CatalogImport import = CatalogSheet.Read(rows, seed, Draft);
        Species added = Assert.Single(import.Added);
        Assert.Equal(PlantCategory.Forracao, added.Category);
        Assert.Equal(0.4, added.CrownDiameterM, 6);
        Assert.Equal(string.Empty, added.Code);
        Assert.Contains(import.Problems, p => p.Contains("Água", StringComparison.Ordinal));
        Assert.Contains(import.Problems, p => p.Contains("Floração", StringComparison.Ordinal));
        Assert.Contains(import.Problems, p => p.Contains("nao-existe", StringComparison.Ordinal));
        Assert.Contains(import.Problems, p => p.Contains("categoria", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingHeader_IsAProblemNotACrash()
    {
        CatalogImport import = CatalogSheet.Read(new List<string[]> { new[] { "a", "b" } }, Array.Empty<Species>(), Draft);
        Assert.False(import.HasChanges);
        Assert.Single(import.Problems);
    }

    [Fact]
    public void CatalogService_ImportAssignsCodesAndSaves()
    {
        string dir = Path.Combine(Path.GetTempPath(), "viridian-cat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var service = new CatalogService(new CatalogStore(Path.Combine(dir, "catalogo.json")));
            int before = service.All.Count;
            Species target = service.All[0];
            var import = new CatalogImport();
            Species edited = target.Clone();
            edited.UnitPrice = 9.9m;
            import.Updated.Add(edited);
            Species fresh = service.NewDraft(PlantCategory.Forracao);
            fresh.Code = string.Empty;
            fresh.PopularName = "Nova";
            fresh.ScientificName = "Nova novae";
            fresh.CrownDiameterM = 0.4;
            import.Added.Add(fresh);
            (int updated, int added, List<string> problems) = service.Import(import);
            Assert.Equal(1, updated);
            Assert.Equal(1, added);
            Assert.Empty(problems);
            Assert.Equal(before + 1, service.All.Count);
            Assert.False(string.IsNullOrWhiteSpace(service.Find(fresh.Id)!.Code));
            Assert.True(service.Find(target.Id)!.IsUserDefined);

            service.Reload();
            Assert.Equal(9.9m, service.Find(target.Id)!.UnitPrice);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
