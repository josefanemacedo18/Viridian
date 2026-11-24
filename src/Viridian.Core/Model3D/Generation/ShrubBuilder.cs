using Viridian.Core.Geometry;
using Viridian.Core.Model3D.Profiles;

namespace Viridian.Core.Model3D.Generation;

/// <summary>
/// Arbustos (globosos ou eretos), trepadeiras, forrações e tufos em caule: vários caules saindo da base e grupos de
/// folhagem sobrepostos dentro da envoltória do hábito — nunca um domo liso único.
/// </summary>
internal static class ShrubBuilder
{
    private static readonly double Golden = Math.PI * (3 - Math.Sqrt(5));
    private static readonly Vec3 Up = new(0, 0, 1);

    public static void Build(BuildContext c)
    {
        switch (c.Profile.Architecture)
        {
            case PlantArchitecture.TufosEmCaule:
                Tufts(c);
                return;
            case PlantArchitecture.Trepadeira:
                Vine(c);
                return;
            default:
                Mound(c);
                return;
        }
    }

    /// <summary>Arbusto globoso, ereto ou forração: caules finos e grupos de folhagem em camadas.</summary>
    private static void Mound(BuildContext c)
    {
        Plant3DProfile p = c.Profile;
        bool erect = p.Architecture == PlantArchitecture.ArbustoEreto;
        bool cover = p.Architecture == PlantArchitecture.Forracao;
        double h = c.H, r = c.R;
        double scale = Math.Clamp(Math.Sqrt(Math.Max(r, h) / 0.8), 0.7, 1.5);
        int count = c.Count((cover ? 5 : 5) * scale, (cover ? 10 : erect ? 13 : 11) * scale, (cover ? 15 : erect ? 20 : 17) * scale, 3);
        if (c.Leafless)
        {
            count = Math.Max(2, count / 4);
        }
        else if (c.Sparse)
        {
            count = Math.Max(3, count * 2 / 3);
        }

        double clusterRadius = (cover ? Math.Max(r * 0.3, h * 0.9) : Math.Min(r, h / 2)) * (c.Simple ? 0.62 : c.Detailed ? 0.42 : 0.5) / Math.Sqrt(scale) * (erect ? 0.95 : 1.2);
        if (erect)
        {
            // Eretos: os grupos se sobrepõem na vertical (sem "bolas empilhadas" soltas).
            clusterRadius = Math.Max(clusterRadius, h * 0.75 / (count * 1.15 + 1));
        }
        clusterRadius = Math.Clamp(clusterRadius, 0.04, Math.Max(0.06, cover ? h * 1.1 + r * 0.2 : Math.Min(r, h) * 0.75));
        double flatten = cover ? 0.5 : erect ? 1.15 : 0.85;
        var clusters = new List<(Vec3 Center, double Radius, double Tn)>();
        double start = c.Random.Range(0, Geo.TwoPi);
        for (int k = 0; k < count; k++)
        {
            double t = (k + 0.5) / count;
            double theta = start + k * Golden;
            double tn, radial;
            if (cover)
            {
                tn = 0.5;
                radial = Math.Sqrt(t) * (r - clusterRadius * 0.6);
            }
            else if (erect)
            {
                tn = 0.18 + 0.75 * t;
                radial = (0.25 + 0.6 * tn) * (r - clusterRadius * 0.5) * c.Random.Range(0.5, 1.0);
            }
            else
            {
                double y = 1 - t; // de cima para baixo, metade superior de um elipsoide
                tn = 0.2 + 0.75 * y;
                radial = Math.Sqrt(Math.Max(0, 1 - Math.Pow(2 * tn - 1, 2))) * (r - clusterRadius * 0.55) * c.Random.Range(0.75, 1.0);
            }

            double z = cover ? Math.Max(clusterRadius * flatten * 0.8, h * 0.45) : Math.Clamp(tn * h, clusterRadius * flatten * 0.7, h - clusterRadius * flatten * 0.75);
            (Vec3 center, double radius) = c.FitInCrown(new Vec3(Math.Cos(theta) * radial, Math.Sin(theta) * radial * p.Crown.WidthAspect, z), clusterRadius * c.Random.Range(0.85, 1.15), 1.1);
            clusters.Add((center, radius, tn));
        }

        // Caules visíveis entre as folhas (Intermediário e Detalhado), da base até grupos de folhagem.
        TrunkProfile trunk = p.Trunk ?? new TrunkProfile { Stems = 3 };
        if (!cover && (!c.Simple || c.Leafless || clusters.Min(k => k.Center.Z - k.Radius * flatten) > h * 0.12))
        {
            // No Simplificado, só um caule, e apenas quando a folhagem não chega perto do chão.
            int stems = c.Simple && !c.Leafless ? 1 : Math.Max(1, Math.Min(trunk.Stems, clusters.Count));
            double stemRadius = Math.Clamp(trunk.RadiusRatio * h, 0.008, 0.06);
            for (int s = 0; s < stems; s++)
            {
                Vec3 target = clusters[(s * 3) % clusters.Count].Center;
                Vec3 b = new(c.Random.Range(-1, 1) * r * 0.08, c.Random.Range(-1, 1) * r * 0.08, 0);
                List<Vec3> path = MeshBuild.Bezier(b, b + Up * (target.Z * 0.4), target * 0.75 + Up * (target.Z * 0.1), target, c.Res(2, 3, 4));
                c.Add(MeshBuild.Tube(MeshPart.Galho, path, path.Select((_, i) => stemRadius * (1 - 0.5 * i / (path.Count - 1))).ToList(), c.Res(4, 5, 5)));
            }
        }

        int segments = c.Res(6, 7, 8), rings = c.Res(4, 4, 5);
        if (erect && !c.Leafless && !c.Sparse)
        {
            // Miolo alongado: une os grupos numa coluna de folhagem (sem aspecto de topiaria em bolas).
            double bottom = Math.Max(h * 0.15, clusterRadius * 0.5);
            double coreR = Math.Max(0.04, Math.Min(r * 0.62, clusterRadius * 1.05));
            c.Add(Primitives.Ellipsoid(MeshPart.FolhagemEscura, new Vec3(0, 0, (bottom + h * 0.92) / 2), coreR, coreR * p.Crown.WidthAspect, (h * 0.92 - bottom) / 2,
                segments, rings, 0.15, c.NextSeed()));
        }

        foreach ((Vec3 center, double radius, double tn) in clusters)
        {
            double outward = Math.Clamp(new Point2(center.X, center.Y).Length / Math.Max(r, 1e-6), 0, 1);
            c.Add(MeshBuild.Cluster(c.FoliageTone(tn, outward), center, radius, flatten, segments, rings, c.NextSeed(), 0.2));
        }

        if (c.Request.Flowers && p.Flowers is not null)
        {
            Ornaments.Flowers(c, p.Flowers, clusters, clusters.Select(k => k.Center + Up * (k.Radius * flatten)).ToList(), Math.Max(r, 0.3), new Vec3(0, 0, h * 0.5), flatten);
        }

        if (c.Request.Fruits is not null)
        {
            Ornaments.Fruits(c, c.Request.Fruits, clusters, new List<Vec3> { Vec3.Zero, Up * (h * 0.3) }, new List<List<Vec3>>());
        }

        // Placas de folha: no Detalhado; nas forrações também no Intermediário (tira o aspecto de "pedra").
        if ((c.Detailed || (cover && !c.Simple)) && !c.Leafless && !c.Request.SolidOnly)
        {
            Ornaments.LeafPlates(c, clusters, Math.Max(r, 0.4));
        }
    }

    /// <summary>
    /// Trepadeira conduzida (em tela, muro ou pérgola): ramos em leque saindo da base e uma cortina de folhagem
    /// achatada cobrindo o leque, com flores na face. O suporte não é desenhado.
    /// </summary>
    private static void Vine(BuildContext c)
    {
        Plant3DProfile p = c.Profile;
        double h = c.H, r = c.R;
        int stems = Math.Max(2, p.Trunk?.Stems ?? 3);
        double stemRadius = Math.Clamp((p.Trunk?.RadiusRatio ?? 0.01) * h, 0.008, 0.05);
        double Width(double z) => r * (0.35 + 0.6 * Math.Clamp(z / h, 0, 1)); // o leque abre para cima
        var tips = new List<Vec3>();
        for (int s = 0; s < stems; s++)
        {
            double spread = stems == 1 ? 0 : (double)s / (stems - 1) * 2 - 1;
            double z = h * c.Random.Range(0.82, 0.97);
            Vec3 top = new(spread * Width(z) * 0.85, c.Random.Range(-0.05, 0.05) * r, z);
            List<Vec3> path = MeshBuild.Bezier(Vec3.Zero, Up * (h * 0.3), top * 0.55 + Up * (h * 0.15), top, c.Res(3, 4, 6));
            tips.Add(top);
            if (!c.Simple || c.Leafless || s == stems / 2)
            {
                c.Add(MeshBuild.Tube(MeshPart.Galho, path, path.Select((_, i) => stemRadius * (1 - 0.6 * i / (path.Count - 1))).ToList(), c.Res(4, 5, 5)));
            }
        }

        var clusters = new List<(Vec3 Center, double Radius, double Tn)>();
        if (!c.Leafless)
        {
            int total = c.Count(6, 12, 18, 4);
            if (c.Sparse)
            {
                total = Math.Max(4, total * 2 / 3);
            }

            double area = 2 * r * h * 0.72;
            double radius = Math.Clamp(Math.Sqrt(area / total) * 0.72, 0.06, Math.Max(0.08, Math.Min(r, h) * 0.6));
            for (int k = 0; k < total; k++)
            {
                double v = (k + 0.5) / total;
                double z = h * 0.25 + (h * 0.75 - radius * 0.7) * v;
                double across = (k * 0.6180339887 + c.Random.Range(-0.08, 0.08)) % 1.0;
                double x = (across * 2 - 1) * Math.Max(0, Width(z) - radius * 0.6);
                (Vec3 center, double rr) = c.FitInCrown(new Vec3(x, c.Random.Range(-0.12, 0.12) * radius, z), radius * c.Random.Range(0.85, 1.15), 1.1);
                clusters.Add((center, rr, z / h));
            }

            foreach ((Vec3 center, double rr, double tn) in clusters)
            {
                MeshPiece cluster = MeshBuild.Cluster(c.FoliageTone(tn, 0.6), center, rr, 0.9, c.Res(6, 7, 8), c.Res(4, 4, 5), c.NextSeed(), 0.2);
                for (int i = 0; i < cluster.Vertices.Count; i++)
                {
                    Vec3 vtx = cluster.Vertices[i];
                    cluster.Vertices[i] = new Vec3(vtx.X, center.Y + (vtx.Y - center.Y) * 0.5, vtx.Z); // massa achatada, como sobre um muro ou tela
                }

                c.Add(cluster);
            }
        }

        if (c.Request.Flowers && p.Flowers is not null)
        {
            Ornaments.Flowers(c, p.Flowers, clusters, tips, Math.Max(r, 0.5), new Vec3(0, 0, h * 0.6), 0.9);
        }

        if (c.Detailed && !c.Leafless && !c.Request.SolidOnly)
        {
            Ornaments.LeafPlates(c, clusters, Math.Max(r, 0.5));
        }
    }

    /// <summary>Tufos em caule (dracenas, cordilines, iúcas): caules curvos com rosetas de folhas em fita no topo.</summary>
    private static void Tufts(BuildContext c)
    {
        Plant3DProfile p = c.Profile;
        RosetteProfile rosette = p.Rosette ?? new RosetteProfile { Leaf = LeafShape.Linear, WidthRatio = 0.08, StemRatio = 0.7 };
        int stems = Math.Max(1, p.Trunk?.Stems ?? 3);
        double stemRadius = Math.Clamp((p.Trunk?.RadiusRatio ?? 0.012) * c.H, 0.015, 0.15);
        double leafLength = Math.Clamp(Math.Min(c.R * 0.7, Math.Max(c.R * 0.45, c.H * (1 - rosette.StemRatio) * 1.3)), 0.1, 2.5);
        for (int s = 0; s < stems; s++)
        {
            double a = Geo.TwoPi * s / stems + c.Random.Range(-0.4, 0.4);
            double height = Math.Max(0.15, c.H * rosette.StemRatio * (s == 0 ? 1 : c.Random.Range(0.55, 0.95)));
            double spread = stems == 1 ? 0 : c.R * 0.35 * c.Random.Range(0.6, 1.0);
            Vec3 top = new(Math.Cos(a) * spread, Math.Sin(a) * spread, height);
            List<Vec3> path = MeshBuild.Bezier(Vec3.Zero, Up * (height * 0.45), top * 0.55 + Up * (height * 0.3), top, c.Res(2, 4, 5));
            c.Add(MeshBuild.Tube(MeshPart.Tronco, path, path.Select((_, i) => stemRadius * (1 - 0.3 * i / (path.Count - 1))).ToList(), c.Res(5, 6, 7)));
            RosetteBuilder.Leaves(c, rosette, top, leafLength, Math.Max(4, c.Count(rosette.LeavesMin * 0.5, rosette.LeavesMin, rosette.LeavesMax) / Math.Max(1, stems / 2)), 0.06);
        }
    }
}
