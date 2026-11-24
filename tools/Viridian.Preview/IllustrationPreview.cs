using System.Diagnostics;
using Viridian.Core.Colors;
using Viridian.Core.Model;
using Viridian.Core.Model3D.Illustration;
using Viridian.Core.Model3D.Profiles;
using Viridian.Library;

/// <summary>Piloto dos cartões ilustrados: para cada espécie, a vista de quina e as 4 faces de cada fase, a partir das regiões achatadas.</summary>
internal static class IllustrationPreview
{
    public static void Write(string folder)
    {
        Directory.CreateDirectory(folder);
        List<Species> catalog = SeedCatalog.CreateSpecies();
        List<Plant3DProfile> profiles = Plant3DProfileDefaults.Create();
        foreach (string name in PlantCardsPlanner.Pilot)
        {
            Species? s = catalog.FirstOrDefault(x => string.Equals(x.ScientificName, name, StringComparison.OrdinalIgnoreCase));
            if (s is null)
            {
                Console.WriteLine("Não está no catálogo: " + name);
                continue;
            }

            var watch = Stopwatch.StartNew();
            PlantCards cards = PlantCardsPlanner.Build(s, 0, profiles, new ColorSettings(), "#6B4F3A");
            string slug = name.ToLowerInvariant().Replace(' ', '-');
            foreach (CardSet set in cards.Sets)
            {
                double plantSize = Math.Max(set.ArtA.Size, set.ArtB.Size);
                string stage = $"fase{set.Code}";
                File.WriteAllText(Path.Combine(folder, $"{slug}-{stage}-quina.svg"), CardSvg.Corner(set.PlaneA, set.PlaneB, plantSize));
                File.WriteAllText(Path.Combine(folder, $"{slug}-{stage}-face1.svg"), CardSvg.Face(set.PlaneA, plantSize));
                File.WriteAllText(Path.Combine(folder, $"{slug}-{stage}-face2.svg"), CardSvg.Face(set.PlaneB, plantSize));
                File.WriteAllText(Path.Combine(folder, $"{slug}-{stage}-face3.svg"), CardSvg.Face(set.PlaneA, plantSize, mirrored: true));
                File.WriteAllText(Path.Combine(folder, $"{slug}-{stage}-face4.svg"), CardSvg.Face(set.PlaneB, plantSize, mirrored: true));
                Console.WriteLine($"{name} · {set.Label}: A {set.PlaneA.Regions.Count} cores / {set.PlaneA.Loops} laços / {set.PlaneA.Edges} arestas; B {set.PlaneB.Edges} arestas; célula {set.PlaneA.Cell * 1000:0.#} mm");
            }

            Console.WriteLine($"{name}: {cards.Sets.Count} fases, {cards.Edges} arestas no total, {watch.ElapsedMilliseconds} ms, hash {cards.Hash}");
        }
    }
}
