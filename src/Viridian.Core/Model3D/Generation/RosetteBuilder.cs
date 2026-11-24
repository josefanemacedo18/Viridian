using Viridian.Core.Geometry;
using Viridian.Core.Model3D.Profiles;

namespace Viridian.Core.Model3D.Generation;

/// <summary>
/// Rosetas (agave, bromélia, cica, samambaia), touceiras e gramíneas, herbáceas de folhas largas (estrelítzia, helicônia,
/// filodendro) e aquáticas: folhas individuais nascendo de um centro, com inclinação, curvatura e seção próprias.
/// </summary>
internal static class RosetteBuilder
{
    private static readonly double Golden = Math.PI * (3 - Math.Sqrt(5));
    private static readonly Vec3 Up = new(0, 0, 1);

    public static void Build(BuildContext c)
    {
        Plant3DProfile p = c.Profile;
        if (p.Architecture == PlantArchitecture.AquaticaFlutuante)
        {
            Floating(c);
            return;
        }

        RosetteProfile rosette = p.Rosette ?? new RosetteProfile { Leaf = LeafShape.Linear, WidthRatio = 0.05, InclinationDeg = 25 };
        Vec3 origin = Vec3.Zero;
        if (rosette.StemRatio > 0.01)
        {
            // Caule sob a roseta (árvore-do-viajante).
            double height = c.H * rosette.StemRatio;
            double radius = Math.Clamp(c.H * 0.025, 0.04, 0.3);
            List<Vec3> path = new() { Vec3.Zero, Up * (height * 0.5), Up * height };
            c.Add(MeshBuild.Tube(MeshPart.Tronco, path, new List<double> { radius * 1.1, radius, radius * 0.85 }, c.Res(6, 8, 10)));
            origin = Up * height;
        }

        double length = LeafLength(c, rosette, origin.Z);
        if (p.Architecture == PlantArchitecture.HerbaceaFolhaLarga && !rosette.Fan)
        {
            // Folhas largas sobre pecíolos: a folha chega à altura prevista e se abre até a borda da touceira.
            double rise = Math.Max(0.05, c.H - origin.Z), reach = c.R * 0.85;
            length = Math.Clamp(Math.Sqrt(rise * rise + reach * reach) * 0.98, 0.05, 6);
            rosette = rosette.Copy();
            rosette.InclinationDeg = Math.Clamp(Geo.RadToDeg(Math.Atan2(reach, rise)) / 1.15, 8, 75);
        }
        int leaves = c.Count(rosette.LeavesMin * 0.55, (rosette.LeavesMin + rosette.LeavesMax) / 2.0, rosette.LeavesMax, 3);
        if (p.Architecture == PlantArchitecture.Graminea || p.Architecture == PlantArchitecture.AquaticaEmergente)
        {
            leaves = c.Count(rosette.LeavesMin * 0.3, rosette.LeavesMin * 0.75, rosette.LeavesMax, 6);
        }

        double baseRadius = p.Architecture is PlantArchitecture.Graminea or PlantArchitecture.AquaticaEmergente ? c.R * 0.18 : c.R * 0.05;
        // Bromélias de tanque: na floração, as folhas do centro ganham a cor da flor (o "coração" vermelho).
        bool tank = p.Flowers is { AboveFoliage: false, Type: InflorescenceType.Isolada } && p.Architecture == PlantArchitecture.RosetaRigida;
        int colored = tank && c.Request.Flowers ? Math.Max(2, leaves / 4) : 0;
        List<(Vec3 Tip, Vec3 Normal)> tips = Leaves(c, rosette, origin, length, leaves, baseRadius, colored);

        if (c.Request.Flowers && p.Flowers is not null && !tank)
        {
            Inflorescences(c, p.Flowers, rosette, origin, length, tips);
        }
        else if (tank && c.Request.Flowers)
        {
            double size = c.Fit(p.Flowers!.SizeM, 0.2);
            c.Add(MeshBuild.Cluster(MeshPart.Flor, origin + Up * (size * 0.6), size * 0.5, 0.7, c.Res(5, 6, 7), 4, c.NextSeed(), 0.25));
        }
    }

    /// <summary>Comprimento das folhas para que a planta alcance a altura e a largura previstas.</summary>
    private static double LeafLength(BuildContext c, RosetteProfile rosette, double baseZ)
    {
        double incl = Geo.DegToRad(rosette.InclinationDeg);
        double byHeight = (c.H - baseZ) / Math.Max(0.35, Math.Cos(incl) * (1 - rosette.Curvature * 0.35));
        double byWidth = c.R / Math.Max(0.3, Math.Sin(incl) + rosette.Curvature * 0.35);
        double length = rosette.Fan ? Math.Min(byHeight, byWidth * 1.15) : Math.Min(byHeight, byWidth) * (c.Profile.Architecture == PlantArchitecture.RosetaRigida ? 1.05 : 1);
        return Math.Clamp(length, 0.05, 6);
    }

    /// <summary>
    /// Folhas da roseta a partir de <paramref name="origin"/>: espiral (ângulo áureo) ou leque num plano. Devolve a ponta e
    /// a normal de cada folha (para flores). Usada também pelos tufos em caule.
    /// </summary>
    public static List<(Vec3 Tip, Vec3 Normal)> Leaves(BuildContext c, RosetteProfile rosette, Vec3 origin, double length, int count, double baseRadius, int coloredInner = 0)
    {
        var tips = new List<(Vec3, Vec3)>();
        double start = c.Random.Range(0, Geo.TwoPi);
        int segments = c.Res(3, 5, 7);
        bool solid = c.Request.SolidOnly || rosette.Leaf == LeafShape.Suculenta;
        for (int i = 0; i < count; i++)
        {
            double order = count == 1 ? 0.5 : (double)i / (count - 1); // 0 = centro (nova), 1 = fora (velha)
            double azimuth, inclination;
            if (rosette.Fan)
            {
                // Dísticas: num só plano, abrindo em leque.
                azimuth = start + (i % 2 == 0 ? 0 : Math.PI) + c.Random.Range(-0.12, 0.12);
                inclination = Geo.DegToRad(rosette.InclinationDeg * (0.25 + 0.95 * order) + c.Random.Range(-4, 4));
            }
            else
            {
                azimuth = start + i * Golden + c.Random.Range(-0.08, 0.08);
                inclination = Geo.DegToRad(Math.Clamp(rosette.InclinationDeg * (0.55 + 0.75 * order) + c.Random.Range(-6, 6), 2, 88));
            }

            var outward = new Vec3(Math.Cos(azimuth), Math.Sin(azimuth), 0);
            double leafLength = length * c.Random.Range(0.82, 1.05) * (rosette.Fan ? 1 : 0.85 + 0.15 * order);
            double curvature = rosette.Curvature * (0.6 + 0.6 * order);
            Vec3 start0 = origin + outward * (baseRadius * c.Random.Range(0.3, 1.0));
            Vec3 direction = (Up * Math.Cos(inclination) + outward * Math.Sin(inclination)).Normalized();
            Vec3 p1 = start0 + direction * (leafLength * 0.35);
            Vec3 p2 = start0 + direction * (leafLength * 0.7) + outward * (leafLength * curvature * 0.3);
            Vec3 p3 = start0 + direction * (leafLength * (1 - curvature * 0.25)) + outward * (leafLength * curvature * 0.45) - Up * (leafLength * curvature * 0.55);
            int leafSegments = Math.Clamp((int)(leafLength / 0.012), 2, segments); // folhas miúdas: menos trechos (sem arestas minúsculas)
            List<Vec3> path = MeshBuild.Bezier(start0, p1, p2, p3, leafSegments)
                .Select(v => new Vec3(v.X, v.Y, Math.Max(v.Z, Math.Min(origin.Z, 0) + 0.01))) // pontas pousam no chão
                .ToList();

            // A folha não passa da copa prevista: se a curva abrir demais, ela é recolhida na horizontal (a partir da base).
            double allowed = Math.Max(0.05, c.R * 1.2 - Math.Sqrt(origin.X * origin.X + origin.Y * origin.Y));
            double reach = path.Max(v => Math.Sqrt((v.X - origin.X) * (v.X - origin.X) + (v.Y - origin.Y) * (v.Y - origin.Y)));
            if (reach > allowed)
            {
                double k = allowed / reach;
                path = path.Select(v => new Vec3(origin.X + (v.X - origin.X) * k, origin.Y + (v.Y - origin.Y) * k, v.Z)).ToList();
            }
            double width = leafLength * rosette.WidthRatio;
            // Normal da lâmina: perpendicular à folha e à direção lateral (horizontal), mesmo com a folha quase vertical.
            Vec3 normal = outward.Cross(Up).Normalized().Cross(direction).Normalized();
            if (normal.Length < 0.5)
            {
                normal = outward;
            }
            MeshPart tone = i < coloredInner ? MeshPart.Flor : order < 0.3 ? MeshPart.FolhagemClara : order > 0.8 ? MeshPart.FolhagemEscura : MeshPart.Folhagem;

            switch (rosette.Leaf)
            {
                case LeafShape.Pinada:
                    PinnateLeaf(c, path, outward, width, tone);
                    break;

                case LeafShape.Oval or LeafShape.Cordiforme or LeafShape.Eliptica when rosette.PetioleRatio > 0.05:
                case LeafShape.Lanceolada when rosette.PetioleRatio > 0.05:
                {
                    // Pecíolo e lâmina: a lâmina começa depois do cabo.
                    int split = Math.Max(1, (int)Math.Round(rosette.PetioleRatio * segments));
                    List<Vec3> petiole = path.Take(split + 1).ToList();
                    List<Vec3> blade = path.Skip(split).ToList();
                    // A largura vem do comprimento da lâmina (sem o pecíolo): folhas ovais são proporcionalmente mais largas.
                    double bladeLength = leafLength * (1 - rosette.PetioleRatio);
                    double bladeWidth = bladeLength * rosette.WidthRatio * (rosette.Leaf is LeafShape.Oval or LeafShape.Cordiforme ? 1.4 : 1.0);
                    c.Add(MeshBuild.Ribbon(MeshPart.FolhagemEscura, petiole, petiole.Select(_ => Math.Max(0.008, width * 0.08)).ToList(), normal, 0.3, solid, Math.Max(0.006, width * 0.05)));
                    if (blade.Count >= 2)
                    {
                        double widest = rosette.Leaf == LeafShape.Lanceolada ? 0.45 : 0.4;
                        List<double> widths = MeshBuild.LeafWidths(blade.Count, bladeWidth, widest, 0.08, rosette.Leaf == LeafShape.Cordiforme ? 0.6 : 0.3);
                        c.Add(MeshBuild.Ribbon(tone, blade, widths, normal, 0.12, solid, Math.Max(0.004, width * 0.03)));
                    }

                    break;
                }

                default:
                {
                    (double widest, double tip, double baseWidth) = rosette.Leaf switch
                    {
                        LeafShape.Suculenta => (0.3, 0.05, 0.55),
                        LeafShape.Linear => (0.25, 0.12, 0.75),
                        _ => (0.4, 0.06, 0.35),
                    };
                    List<double> widths = MeshBuild.LeafWidths(path.Count, width, widest, tip, baseWidth);
                    double thickness = rosette.Leaf == LeafShape.Suculenta ? Math.Max(0.008, width * rosette.Thickness) : Math.Max(0.004, width * 0.05);
                    double fold = rosette.Leaf == LeafShape.Linear ? 0.25 : 0.15;
                    c.Add(MeshBuild.Ribbon(tone, path, widths, normal, fold, solid, thickness));
                    break;
                }
            }

            tips.Add((path[^1], normal));
        }

        return tips;
    }

    /// <summary>Folha pinada pequena (samambaia, cica sem caule): raque e folíolos.</summary>
    private static void PinnateLeaf(BuildContext c, List<Vec3> path, Vec3 outward, double width, MeshPart tone)
    {
        c.Add(MeshBuild.Ribbon(tone, path, path.Select(_ => Math.Max(0.006, width * 0.08)).ToList(), Up, 0, c.Request.SolidOnly, 0.006));
        if (c.Request.SolidOnly)
        {
            return;
        }

        Vec3 side = outward.Cross(Up).Normalized();
        int perSide = c.Res(5, 10, 16);
        var leaflets = new MeshPiece(tone, closed: false);
        for (int k = 0; k < perSide; k++)
        {
            double u = 0.1 + 0.88 * (k + 0.5) / perSide;
            int index = Math.Min(path.Count - 2, (int)Math.Floor(u * (path.Count - 1)));
            Vec3 at = path[index] + (path[index + 1] - path[index]) * (u * (path.Count - 1) - index);
            Vec3 tangent = (path[index + 1] - path[index]).Normalized();
            double l = width * 0.55 * Math.Sin(Math.PI * (0.15 + 0.85 * u));
            for (int s = -1; s <= 1; s += 2)
            {
                Vec3 direction = (side * s + tangent * 0.45 - Up * 0.1).Normalized();
                MeshBuild.LeafPlate(leaflets, at, at + direction * l, Math.Max(0.006, l * 0.3), Up, 0.05);
            }
        }

        c.Add(leaflets);
    }

    /// <summary>Flores de herbáceas: em hastes acima da folhagem (conforme o tipo) ou entre as folhas.</summary>
    private static void Inflorescences(BuildContext c, FlowerProfile f, RosetteProfile rosette, Vec3 origin, double length, List<(Vec3 Tip, Vec3 Normal)> tips)
    {
        int scapes = f.Type == InflorescenceType.Espiga && c.Profile.Architecture == PlantArchitecture.RosetaRigida ? 1 : c.Count(1 + 2 * f.Abundance, 2 + 4 * f.Abundance, 3 + 7 * f.Abundance);
        double size = c.Fit(Math.Max(0.03, f.SizeM), 0.35);
        for (int s = 0; s < scapes; s++)
        {
            double a = c.Random.Range(0, Geo.TwoPi);
            double lean = c.Profile.Architecture == PlantArchitecture.RosetaRigida && scapes == 1 ? 0 : c.Random.Range(0.05, 0.25);
            double scapeHeight = f.AboveFoliage ? Math.Max(c.H * c.Random.Range(0.95, 1.2), origin.Z + length * 0.5) : c.H * 0.6;
            if (f.Type == InflorescenceType.AveDoParaiso)
            {
                scapeHeight = Math.Max(origin.Z + 0.05, c.H * c.Random.Range(0.72, 0.9)); // flores na altura das folhas, não acima
            }

            if (c.Profile.Architecture == PlantArchitecture.RosetaRigida && f.Type == InflorescenceType.Espiga)
            {
                scapeHeight = Math.Max(c.H * 0.7, origin.Z + size * 1.2); // bromélia: haste curta no centro
            }

            // A planta florida não passa muito do porte: a haste para antes, conforme o tamanho da inflorescência.
            double extent = f.Type switch
            {
                InflorescenceType.Espiga or InflorescenceType.Bracteas => size,
                InflorescenceType.Plumas => size * 0.9,
                InflorescenceType.AveDoParaiso => size * 0.6,
                _ => size * 0.8,
            };
            scapeHeight = Math.Max(origin.Z + 0.02, Math.Min(scapeHeight, c.H * c.Random.Range(1.0, 1.12) - extent));

            Vec3 top = origin + new Vec3(Math.Cos(a) * lean * c.R, Math.Sin(a) * lean * c.R, 0) + Up * (scapeHeight - origin.Z);
            if (f.AboveFoliage)
            {
                List<Vec3> stalk = MeshBuild.Bezier(origin, origin + Up * (scapeHeight * 0.4), top - Up * (scapeHeight * 0.2), top, 2);
                c.Add(MeshBuild.Tube(MeshPart.Galho, stalk, stalk.Select(_ => Math.Clamp(size * 0.06, 0.005, 0.03)).ToList(), 4));
            }
            else if (tips.Count > 0)
            {
                top = tips[c.Random.NextInt(0, tips.Count)].Tip;
            }

            switch (f.Type)
            {
                case InflorescenceType.AveDoParaiso:
                    BirdOfParadise(c, top, a, size);
                    break;
                case InflorescenceType.Plumas:
                    c.Add(Primitives.Ellipsoid(MeshPart.Flor, top + Up * (size * 0.4), size * 0.16, size * 0.12, size * 0.5, c.Res(5, 6, 7), 4, 0.1, c.NextSeed()));
                    break;
                case InflorescenceType.Espiga:
                    c.Add(MeshBuild.Tube(MeshPart.Flor, new List<Vec3> { top, top + Up * (size * 0.5), top + Up * size }, new List<double> { size * 0.11, size * 0.12, size * 0.05 }, c.Res(5, 6, 8)));
                    break;
                case InflorescenceType.Bracteas:
                {
                    // Brácteas alternadas em zigue-zague (helicônias).
                    int count = c.Res(3, 5, 7);
                    var mesh = new MeshPiece(MeshPart.Flor, closed: false);
                    for (int k = 0; k < count; k++)
                    {
                        Vec3 at = top + Up * (size * k / count);
                        double side = k % 2 == 0 ? 1 : -1;
                        Vec3 tip = at + new Vec3(Math.Cos(a) * side, Math.Sin(a) * side, 0.35) * (size * 0.55);
                        MeshBuild.LeafPlate(mesh, at, tip, size * 0.28, Up, 0.35);
                    }

                    c.Add(mesh);
                    break;
                }

                default:
                    Ornaments.Group(c, f.Type, top, Up, size, c.Simple ? 1 : c.Detailed ? Math.Max(3, f.PerCluster) : 4);
                    break;
            }
        }
    }

    /// <summary>Flor de estrelítzia: espata horizontal em forma de bico e pétalas eretas.</summary>
    private static void BirdOfParadise(BuildContext c, Vec3 at, double azimuth, double size)
    {
        var dir = new Vec3(Math.Cos(azimuth), Math.Sin(azimuth), 0);
        Vec3 side = dir.Cross(Up).Normalized();
        List<Vec3> spathe = MeshBuild.Bezier(at - dir * (size * 0.2), at + dir * (size * 0.2), at + dir * (size * 0.6) + Up * (size * 0.05), at + dir * (size * 0.9) + Up * (size * 0.12), 3);
        c.Add(MeshBuild.Ribbon(MeshPart.FolhagemEscura, spathe, MeshBuild.LeafWidths(spathe.Count, size * 0.22, 0.3, 0.15, 0.5), side, 0.4, c.Request.SolidOnly, 0.01));
        var petals = new MeshPiece(MeshPart.Flor, closed: false);
        for (int k = 0; k < (c.Simple ? 2 : 3); k++)
        {
            Vec3 from = at + dir * (size * (0.1 + 0.12 * k)) + Up * (size * 0.06);
            Vec3 to = from + (Up * 1.0 + dir * (0.2 - 0.2 * k) + side * (k - 1) * 0.25).Normalized() * (size * 0.55);
            MeshBuild.LeafPlate(petals, from, to, size * 0.12, side, 0.2);
        }

        c.Add(petals);
    }

    /// <summary>Aquáticas flutuantes: folhas arredondadas sobre a água (com recorte) e flores na superfície.</summary>
    private static void Floating(BuildContext c)
    {
        RosetteProfile rosette = c.Profile.Rosette ?? new RosetteProfile { LeavesMin = 5, LeavesMax = 9 };
        int pads = c.Count(rosette.LeavesMin, (rosette.LeavesMin + rosette.LeavesMax) / 2.0, rosette.LeavesMax * 1.4, 2);
        int segments = c.Res(8, 14, 20);
        var mesh = new MeshPiece(MeshPart.Folhagem, closed: false);
        var anchors = new List<Vec3>();
        for (int i = 0; i < pads; i++)
        {
            double t = (i + 0.5) / pads;
            double a = i * Golden;
            double radius = Math.Max(0.04, c.R * c.Random.Range(0.22, 0.42) * (1 - 0.3 * t));
            double distance = Math.Sqrt(t) * Math.Max(0, c.R - radius);
            var center = new Vec3(Math.Cos(a) * distance, Math.Sin(a) * distance, Math.Min(c.H, 0.03) + 0.002 * i);
            double notch = c.Random.Range(0, Geo.TwoPi);
            int hub = mesh.Add(center - Up * (radius * 0.03));
            int? previous = null;
            for (int s = 0; s <= segments; s++)
            {
                double angle = notch + Geo.DegToRad(12) + (Geo.TwoPi - Geo.DegToRad(24)) * s / segments; // recorte em V
                int rim = mesh.Add(center + new Vec3(Math.Cos(angle) * radius, Math.Sin(angle) * radius, 0));
                if (previous is int prev)
                {
                    mesh.Triangle(hub, prev, rim);
                }

                previous = rim;
            }

            anchors.Add(center);
        }

        if (c.Request.SolidOnly)
        {
            foreach (Vec3 a in anchors)
            {
                c.Add(Primitives.Ellipsoid(MeshPart.Folhagem, a, c.R * 0.3, c.R * 0.3, 0.015, segments, 3));
            }
        }
        else
        {
            c.Add(mesh);
        }

        if (c.Request.Flowers && c.Profile.Flowers is { } f)
        {
            int flowers = c.Count(1, 2, 4);
            double radius = c.Fit(Math.Max(0.04, f.SizeM * 0.5), 0.3);
            for (int i = 0; i < flowers && anchors.Count > 0; i++)
            {
                Vec3 at = anchors[(i * 3 + 1) % anchors.Count] + Up * Math.Min(0.05, c.H * 0.4);
                c.Add(MeshBuild.Cluster(MeshPart.Flor, at, radius, 0.8, c.Res(6, 7, 8), 4, c.NextSeed(), 0.25));
            }
        }
    }
}
