using Viridian.Core.Geometry;
using Viridian.Core.Model3D.Profiles;
using Viridian.Core.Model3D.Seasonality;

namespace Viridian.Core.Model3D.Generation;

/// <summary>Flores, frutos e placas de folha: sempre agrupados e limitados pelo orçamento de cada nível.</summary>
internal static class Ornaments
{
    private static readonly Vec3 Up = new(0, 0, 1);

    /// <summary>
    /// Inflorescências sobre a copa: sobre a folhagem (no alto e por fora) ou, sem folhas, nas pontas dos galhos.
    /// Simplificado: poucas manchas; Intermediário: grupos; Detalhado: flores individuais limitadas.
    /// </summary>
    public static void Flowers(BuildContext c, FlowerProfile f, IReadOnlyList<(Vec3 Center, double Radius, double Tn)> clusters, IReadOnlyList<Vec3> tips, double crownRadius,
        Vec3? crownCenter = null, double flatten = 1)
    {
        double share = c.Leafless ? 0.55 : f.Abundance >= 0.6 ? 0.3 : 0.22;
        int budget = Math.Max(0, (int)(c.Profile.Budget.For(c.Quality) * c.Factor * share));

        // Floração vistosa (ipês, flamboyant, quaresmeiras): a copa fica coberta de cor. Sem folhas, massas de flores
        // ocupam o lugar da folhagem; com folhas, manchas de flores afloram nas faces de cima e de fora da copa.
        if (f.Abundance >= 0.6 && clusters.Count > 0)
        {
            Vec3 middle = crownCenter ?? new Vec3(0, 0, clusters.Average(k => k.Center.Z));
            int used = 0;
            int segments = c.Leafless ? c.Res(5, 6, 6) : 5, rings = c.Leafless ? c.Res(3, 4, 4) : 3;
            foreach ((Vec3 center, double radius, double tn) in clusters)
            {
                if (used >= budget)
                {
                    break;
                }

                MeshPiece mass;
                if (c.Leafless)
                {
                    if (!c.Random.Chance(0.5 + 0.5 * f.Abundance))
                    {
                        continue;
                    }

                    mass = MeshBuild.Cluster(MeshPart.Flor, center, radius * 0.74, flatten * 0.95, segments, rings, c.NextSeed(), 0.22);
                }
                else
                {
                    if (tn < 0.25 || !c.Random.Chance(Math.Min(1, f.Abundance * 1.1)))
                    {
                        continue;
                    }

                    // Até duas manchas por grupo, nas faces de cima e de fora.
                    Vec3 away = center - middle;
                    Vec3 outward = away.Length > 1e-6 ? away.Normalized() : Up;
                    for (int k = 0; k < (f.Abundance >= 0.8 ? 2 : 1); k++)
                    {
                        Vec3 n = (outward + Up * 0.7 + new Vec3(c.Random.Range(-0.5, 0.5), c.Random.Range(-0.5, 0.5), 0)).Normalized();
                        Vec3 at = center + new Vec3(n.X * radius * 0.66, n.Y * radius * 0.66, n.Z * radius * flatten * 0.66);
                        MeshPiece patch = MeshBuild.Cluster(MeshPart.Flor, at, radius * 0.4, 0.6, segments, rings, c.NextSeed(), 0.25);
                        c.Add(patch);
                        used += patch.TriangleCount;
                    }

                    continue;
                }

                c.Add(mass);
                used += mass.TriangleCount;
            }

            budget -= used;
            if (!c.Detailed || budget <= 0)
            {
                return;
            }
        }
        int perGroup = c.Simple ? 1 : c.Detailed ? Math.Max(3, f.PerCluster) : 3;
        int trisPerFlower = 8;
        int groups = Math.Min(c.Count(5 + 6 * f.Abundance, 8 + 22 * f.Abundance, 12 + 40 * f.Abundance), budget / Math.Max(1, perGroup * trisPerFlower));
        if (groups <= 0)
        {
            return;
        }

        double size = c.Fit(f.SizeM * Math.Clamp(crownRadius / 3, 0.7, 1.7) * (c.Simple ? 1.6 : 1), 0.2);
        var anchors = new List<(Vec3 Point, Vec3 Normal)>();
        if (c.Leafless || clusters.Count == 0)
        {
            foreach (Vec3 tip in tips)
            {
                anchors.Add((tip, (new Vec3(tip.X, tip.Y, 0).Normalized() + Up).Normalized()));
            }
        }
        else
        {
            foreach ((Vec3 center, double radius, double tn) in clusters.Where(k => k.Tn > 0.3))
            {
                for (int i = 0; i < 3; i++)
                {
                    Vec3 n = RandomUpper(c, center);
                    anchors.Add((center + new Vec3(n.X * radius * 0.8, n.Y * radius * 0.8, n.Z * radius * flatten * 0.8), n)); // na superfície do grupo
                }
            }
        }

        if (anchors.Count == 0)
        {
            return;
        }

        for (int g = 0; g < groups; g++)
        {
            (Vec3 at, Vec3 normal) = anchors[c.Random.NextInt(0, anchors.Count)];
            Group(c, f.Type, at, normal, size, perGroup);
        }
    }

    /// <summary>Uma inflorescência: forma conforme o tipo, com as flores agrupadas em torno de um eixo.</summary>
    public static void Group(BuildContext c, InflorescenceType type, Vec3 at, Vec3 normal, double size, int count)
    {
        double flower = Math.Max(MeshBuild.MinFeature * 3, size * (count <= 1 ? 0.45 : 0.22));
        Vec3 axis = type is InflorescenceType.Cacho ? (normal * 0.4 - Up).Normalized() : normal;
        for (int i = 0; i < count; i++)
        {
            double t = count == 1 ? 0.5 : (double)i / (count - 1);
            double around = i * 2.399 + c.Random.Range(0, 0.4);
            Vec3 side = axis.Cross(Math.Abs(axis.Z) < 0.9 ? Up : new Vec3(1, 0, 0)).Normalized();
            Vec3 other = axis.Cross(side).Normalized();
            double spread = type switch
            {
                InflorescenceType.Capitulo or InflorescenceType.Isolada => 0.15,
                InflorescenceType.Umbela => 0.5,
                InflorescenceType.Espiga => 0.12,
                _ => 0.35 * (1 - t * 0.6),
            };
            Vec3 p = at + axis * (size * t * (type is InflorescenceType.Umbela or InflorescenceType.Capitulo ? 0.2 : 0.8))
                        + (side * Math.Cos(around) + other * Math.Sin(around)) * (size * spread);
            c.Add(Primitives.Blob(MeshPart.Flor, p, flower * c.Random.Range(0.8, 1.15), around));
        }
    }

    /// <summary>Frutos conforme o tipo: pendentes sob a folhagem, no tronco (caulinares) ou em vagens.</summary>
    public static void Fruits(BuildContext c, FruitTraits fruit, IReadOnlyList<(Vec3 Center, double Radius, double Tn)> clusters, IReadOnlyList<Vec3> trunk, IReadOnlyList<List<Vec3>> primaries)
    {
        int budget = Math.Max(0, (int)(c.Profile.Budget.For(c.Quality) * c.Factor * 0.15));
        bool sphere = c.Detailed;
        int tris = sphere ? 20 : 8;
        double abundance = c.Profile.Fruits?.Abundance ?? 0.35;
        int count = Math.Min(c.Count(4 + 6 * abundance, 10 + 20 * abundance, 20 + 50 * abundance), budget / tris);
        double size = c.Fit(Math.Max(fruit.SizeM, c.Simple ? 0.06 : 0.025), 0.12);
        for (int i = 0; i < count; i++)
        {
            Vec3 p;
            if (fruit.Type == Profiles.FruitType.Caulinar || clusters.Count == 0)
            {
                // No tronco e nos galhos principais.
                List<Vec3> path = primaries.Count > 0 && c.Random.Chance(0.5) ? primaries[c.Random.NextInt(0, primaries.Count)] : trunk.ToList();
                int index = c.Random.NextInt(path.Count > 2 ? 1 : 0, path.Count);
                double angle = c.Random.Range(0, Geo.TwoPi);
                p = path[index] + new Vec3(Math.Cos(angle), Math.Sin(angle), 0) * (size * 1.6);
            }
            else
            {
                (Vec3 center, double radius, double _) = clusters[c.Random.NextInt(0, clusters.Count)];
                double angle = c.Random.Range(0, Geo.TwoPi);
                p = center + new Vec3(Math.Cos(angle) * radius * 0.7, Math.Sin(angle) * radius * 0.7, -radius * c.Random.Range(0.45, 0.8));
            }

            p = new Vec3(p.X, p.Y, Math.Max(p.Z, size * 1.4)); // frutos não ficam enterrados
            switch (fruit.Type)
            {
                case Profiles.FruitType.Vagem:
                    c.Add(Primitives.Ellipsoid(MeshPart.Fruto, p - Up * (size * 0.45), Math.Max(MeshBuild.MinFeature * 2, size * 0.06), Math.Max(MeshBuild.MinFeature * 2, size * 0.04), size * 0.5, 4, 4));
                    break;
                case Profiles.FruitType.Grande:
                    c.Add(Primitives.Ellipsoid(MeshPart.Fruto, p - Up * (size * 0.6), size * 0.45, size * 0.42, size * 0.55, sphere ? 6 : 5, 4));
                    break;
                default:
                    if (sphere)
                    {
                        c.Add(MeshBuild.Sphere(MeshPart.Fruto, p, size / 2, 5, 3));
                    }
                    else
                    {
                        c.Add(Primitives.Blob(MeshPart.Fruto, p, size / 2));
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Placas de folha nas faces de cima e de fora dos grupos de folhagem, até o orçamento restante: dão rugosidade à
    /// silhueta sem milhares de folhas. Folhas em agulha viram placas estreitas; compostas, placas alongadas.
    /// </summary>
    public static void LeafPlates(BuildContext c, IReadOnlyList<(Vec3 Center, double Radius, double Tn)> clusters, double crownRadius)
    {
        FoliageProfile f = c.Profile.Foliage;
        int available = c.Remaining - 20;
        int plates = Math.Min(available / 2, (int)(clusters.Count * 26 * c.Factor));
        if (plates <= 0)
        {
            return;
        }

        double length = c.Fit(Math.Clamp(Math.Max(f.LeafLengthM * 2.2, crownRadius * 0.07), 0.08, 0.6), 0.25);
        double width = length * f.Shape switch
        {
            LeafShape.Agulha or LeafShape.Linear => 0.18,
            LeafShape.Pinada => 0.3,
            LeafShape.Palmada or LeafShape.Cordiforme => 0.75,
            _ => Math.Clamp(f.WidthRatio * 1.3, 0.3, 0.8),
        };
        var meshes = new Dictionary<MeshPart, MeshPiece>();
        for (int i = 0; i < plates; i++)
        {
            (Vec3 center, double radius, double tn) = clusters[i % clusters.Count];
            Vec3 n = RandomUpper(c, center);
            Vec3 basePoint = center + n * (radius * c.Random.Range(0.78, 0.95));
            Vec3 tangent = n.Cross(new Vec3(c.Random.Range(-1, 1), c.Random.Range(-1, 1), c.Random.Range(-0.3, 0.3))).Normalized();
            Vec3 tip = basePoint + (n * 0.55 + tangent * 0.85 - Up * 0.15).Normalized() * (length * c.Random.Range(0.8, 1.2));
            MeshPart part = c.FoliageTone(tn, 1);
            if (!meshes.TryGetValue(part, out MeshPiece? mesh))
            {
                mesh = new MeshPiece(part, closed: false);
                meshes[part] = mesh;
            }

            MeshBuild.LeafPlate(mesh, basePoint, tip, width, n);
        }

        foreach (MeshPiece mesh in meshes.Values)
        {
            c.Add(mesh);
        }
    }

    /// <summary>Direção aleatória na metade de cima e voltada para fora do centro da copa.</summary>
    private static Vec3 RandomUpper(BuildContext c, Vec3 center)
    {
        var outward = new Vec3(center.X, center.Y, 0);
        Vec3 o = outward.Length > 1e-6 ? outward.Normalized() : new Vec3(1, 0, 0);
        double a = c.Random.Range(0, Geo.TwoPi);
        var random = new Vec3(Math.Cos(a), Math.Sin(a), c.Random.Range(0.1, 1.0));
        return (random.Normalized() + o * 0.6 + Up * 0.3).Normalized();
    }
}
