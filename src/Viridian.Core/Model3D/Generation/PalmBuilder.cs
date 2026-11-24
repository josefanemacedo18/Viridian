using Viridian.Core.Geometry;
using Viridian.Core.Model3D.Profiles;

namespace Viridian.Core.Model3D.Generation;

/// <summary>
/// Palmeiras e cicas: estipe (um ou vários, em touceira), palmito verde opcional, folhas com raque arqueada e folíolos
/// (pinadas, inclusive em vários planos nas plumosas) ou em leque (palmadas) e cachos de frutos sob a copa.
/// </summary>
internal static class PalmBuilder
{
    private static readonly double Golden = Math.PI * (3 - Math.Sqrt(5));
    private static readonly Vec3 Up = new(0, 0, 1);

    public static void Build(BuildContext c)
    {
        Plant3DProfile p = c.Profile;
        PalmProfile palm = p.Palm ?? new PalmProfile();
        // Em touceira, o número de estipes também cede quando é preciso caber no orçamento.
        int stems = Math.Max(1, (int)Math.Round(palm.Stems * Math.Min(1, c.Factor * 1.15)));
        double ring = stems > 1 ? c.R * 0.18 : 0;
        for (int s = 0; s < stems; s++)
        {
            double heightFactor = s == 0 ? 1 : c.Random.Range(0.45, 0.95);
            double angle = Geo.TwoPi * s / stems + c.Random.Range(-0.3, 0.3);
            var basePoint = new Vec3(Math.Cos(angle) * ring * (s == 0 ? 0.3 : 1), Math.Sin(angle) * ring * (s == 0 ? 0.3 : 1), 0);
            Stem(c, palm, basePoint, angle, heightFactor, stems, lite: s > 0 && !c.Detailed);
        }
    }

    /// <param name="lite">Estipes secundários da touceira no Intermediário: folhas em lâmina inteira (mais leves).</param>
    private static void Stem(BuildContext c, PalmProfile palm, Vec3 basePoint, double outwardAngle, double heightFactor, int stems, bool lite = false)
    {
        Plant3DProfile p = c.Profile;
        double h = c.H * heightFactor;
        double frondLength = Math.Max(Math.Min(0.25, c.R * 0.9), palm.FrondLengthRatio * c.R * (stems > 1 ? 1.05 : 1) * Math.Sqrt(heightFactor));
        double stipeTop = Math.Max(h * 0.12, h - frondLength * (palm.Frond == LeafShape.Palmada ? 0.35 : 0.22));
        if (p.Crown.BaseRatio < 0.6)
        {
            stipeTop = Math.Max(0.1, p.Crown.BaseRatio * h); // cicas: caule curto
        }

        double radius = Math.Clamp(palm.StipeRatio * c.H, 0.03, 0.6) * Math.Sqrt(heightFactor);
        double lean = Math.Tan(Geo.DegToRad(c.Random.Range(0, p.Trunk?.LeanDeg ?? 3))) * stipeTop * 0.5;
        var leanOffset = new Vec3(Math.Cos(outwardAngle), Math.Sin(outwardAngle), 0) * lean;
        Vec3 top = basePoint + leanOffset + Up * stipeTop;
        int segments = c.Res(3, 5, 7);
        List<Vec3> path = MeshBuild.Bezier(basePoint, basePoint + Up * (stipeTop * 0.4), basePoint + leanOffset * 0.6 + Up * (stipeTop * 0.75), top, segments);
        double taper = p.Trunk?.Taper ?? 0.8;
        c.Add(MeshBuild.Tube(MeshPart.Tronco, path, path.Select((_, i) => radius * (1 + (taper - 1) * i / segments) * (i == 0 ? 1.12 : 1)).ToList(), c.Res(6, 8, 10)));

        // Palmito (crownshaft) e capitel.
        if (palm.Crownshaft > 0)
        {
            double length = palm.Crownshaft * c.H * heightFactor;
            c.Add(MeshBuild.Tube(MeshPart.FolhagemClara, new List<Vec3> { top, top + Up * length }, new List<double> { radius * taper * 1.08, radius * taper * 0.95 }, c.Res(6, 8, 10)));
            top += Up * length;
        }

        c.Add(MeshBuild.Cluster(MeshPart.Folhagem, top, Math.Max(radius * 1.5, 0.06), 1.2, c.Res(5, 6, 6), c.Res(3, 4, 4), c.NextSeed(), 0.1));

        // Folhas: as novas mais eretas no centro, as velhas abertas e caídas por fora.
        int fronds = c.Simple ? Math.Max(5, (int)Math.Round(palm.FrondsMin * 0.65 / Math.Sqrt(stems)))
            : c.Detailed ? Math.Max(4, palm.FrondsMax)
            : Math.Max(4, c.Random.NextInt(palm.FrondsMin, palm.FrondsMax + 1));
        fronds = Math.Max(stems > 1 ? 3 : 4, (int)Math.Round(fronds * Math.Sqrt(Math.Min(1, c.Factor * 1.15))));
        double start = c.Random.Range(0, Geo.TwoPi);
        for (int f = 0; f < fronds; f++)
        {
            double order = fronds == 1 ? 0.5 : (double)f / (fronds - 1);
            double azimuth = start + f * Golden + c.Random.Range(-0.12, 0.12);
            double elevation = Geo.DegToRad(68 - order * (75 + 30 * palm.Arch) + c.Random.Range(-6, 6));
            double length = frondLength * c.Random.Range(0.85, 1.05) * (1 - 0.25 * (1 - order) * (1 - order));
            MeshPart tone = order < 0.3 ? MeshPart.FolhagemClara : order > 0.78 ? MeshPart.FolhagemEscura : MeshPart.Folhagem; // novas claras, velhas escuras
            Frond(c, palm, top, azimuth, elevation, length, tone, lite);
        }

        // Cacho de frutos sob a copa.
        if (c.Request.Fruits is { } fruit && fruit.Type == FruitType.CachoPalmeira)
        {
            int bunches = c.Simple ? 1 : 2;
            for (int b = 0; b < bunches; b++)
            {
                double a = start + b * 2.3;
                Vec3 at = top + new Vec3(Math.Cos(a), Math.Sin(a), 0) * (radius * 2.2) - Up * (frondLength * 0.12);
                Bunch(c, at, fruit);
            }
        }
    }

    /// <summary>Uma folha: raque arqueada e folíolos (pinada) ou leque (palmada), conforme o nível.</summary>
    private static void Frond(BuildContext c, PalmProfile palm, Vec3 origin, double azimuth, double elevation, double length, MeshPart tone, bool lite = false)
    {
        var outward = new Vec3(Math.Cos(azimuth), Math.Sin(azimuth), 0);
        Vec3 side = outward.Cross(Up).Normalized();
        int segments = c.Res(4, 6, 8);
        double gravity = 0.25 + palm.Arch * 0.55 * (1 - palm.Rigidity * 0.5);
        var path = new List<Vec3>(segments + 1);
        double reach = palm.Frond == LeafShape.Palmada ? 0.58 : 1.0; // palmada: o pecíolo vai até o leque
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments * reach;
            double horizontal = length * t * Math.Cos(elevation);
            double vertical = length * t * Math.Sin(elevation) - gravity * length * t * t;
            path.Add(origin + outward * horizontal + Up * vertical);
        }

        if (palm.Frond == LeafShape.Palmada)
        {
            c.Add(MeshBuild.Ribbon(MeshPart.Galho, path, path.Select(_ => Math.Max(0.02, length * 0.03)).ToList(), Up, 0, c.Request.SolidOnly, 0.01));
            Fan(c, path[^1], outward, side, length * 0.5, tone);
            return;
        }

        if (c.Simple || lite)
        {
            // Folha inteira como uma lâmina dobrada em V: silhueta de pena com poucos triângulos.
            List<double> widths = MeshBuild.LeafWidths(path.Count, length * 0.3, 0.35, 0.1, 0.35);
            c.Add(MeshBuild.Ribbon(tone, path, widths, Up, palm.Rigidity * 0.35 - 0.1, c.Request.SolidOnly, 0.01));
            return;
        }

        // Raque fina e folíolos dos dois lados.
        c.Add(MeshBuild.Ribbon(tone, path, path.Select((_, i) => Math.Max(0.012, length * 0.025 * (1 - 0.7 * i / segments))).ToList(), Up, 0, c.Request.SolidOnly, 0.01));
        if (c.Request.SolidOnly)
        {
            return; // folíolos são superfícies: na família, a folha fica com a lâmina simplificada
        }

        int perSide = c.Detailed ? 26 : 14;
        perSide = Math.Max(6, (int)Math.Round(perSide * Math.Min(1, c.Factor * 1.1)));
        var leaflets = new MeshPiece(tone, closed: false);
        bool plumose = palm.Rigidity < 0.35;
        for (int k = 0; k < perSide; k++)
        {
            double u = 0.08 + 0.9 * (k + 0.5) / perSide;
            int index = (int)Math.Floor(u * segments);
            double frac = u * segments - index;
            Vec3 at = path[Math.Min(index, segments)] + (path[Math.Min(index + 1, segments)] - path[Math.Min(index, segments)]) * frac;
            Vec3 tangent = (path[Math.Min(index + 1, segments)] - path[Math.Min(index, segments)]).Normalized();
            Vec3 normal = side.Cross(tangent).Normalized();
            double leafletLength = length * 0.34 * Math.Sin(Math.PI * (0.15 + 0.85 * u)) * c.Random.Range(0.9, 1.05);
            for (int sideSign = -1; sideSign <= 1; sideSign += 2)
            {
                // Plumosas (jerivá): folíolos alternam para cima e para baixo; rígidas: em V aberto para cima.
                double lift = plumose ? (k % 2 == 0 ? 0.55 : -0.35) : 0.25 + palm.Rigidity * 0.25;
                double droop = 0.25 + (1 - palm.Rigidity) * 0.35;
                Vec3 direction = (side * sideSign + normal * lift + tangent * 0.35 - Up * droop).Normalized();
                MeshBuild.LeafPlate(leaflets, at, at + direction * leafletLength, Math.Max(0.012, leafletLength * 0.14), normal, 0.05);
            }
        }

        c.Add(leaflets);
    }

    /// <summary>
    /// Folha em leque: segmentos plissados em torno da ponta do pecíolo, abertos para fora e para cima (a face da
    /// folha fica visível de fora da copa), com dedos soltos no Detalhado.
    /// </summary>
    private static void Fan(BuildContext c, Vec3 center, Vec3 outward, Vec3 side, double radius, MeshPart tone)
    {
        int segments = c.Res(6, 14, 20);
        double span = Geo.DegToRad(c.Random.Range(180, 230));
        Vec3 forward = (outward * 0.75 + Up * 0.66).Normalized();
        Vec3 normal = side.Cross(forward).Normalized();
        var mesh = new MeshPiece(tone, closed: false);
        int hub = mesh.Add(center);
        int? previous = null;
        for (int i = 0; i <= segments; i++)
        {
            double a = -span / 2 + span * i / segments;
            Vec3 radial = forward * Math.Cos(a) + side * Math.Sin(a);
            double pleat = (i % 2 == 0 ? 1 : -1) * radius * 0.06;
            Vec3 droop = -Up * (radius * 0.18 * Math.Abs(Math.Sin(a)));
            int rim = mesh.Add(center + radial * (radius * (c.Detailed ? 0.78 : 1)) + normal * pleat + droop);
            if (previous is int prev)
            {
                mesh.Triangle(hub, prev, rim);
            }

            if (c.Detailed)
            {
                // Dedo do segmento: ponta solta além da borda.
                int tip = mesh.Add(center + radial * radius + normal * pleat - Up * (radius * 0.12) + droop * 1.3);
                if (previous is int p2)
                {
                    mesh.Triangle(p2, tip, rim);
                }
            }

            previous = rim;
        }

        c.Add(mesh);
    }

    /// <summary>Cacho de frutos: cone pendente de pequenos frutos (Simplificado: uma massa na cor dos frutos).</summary>
    private static void Bunch(BuildContext c, Vec3 at, Seasonality.FruitTraits fruit)
    {
        double size = c.Fit(Math.Max(fruit.SizeM, 0.02), 0.1);
        if (c.Simple)
        {
            c.Add(MeshBuild.Cluster(MeshPart.Fruto, at - Up * (size * 4), size * 5, 1.4, 6, 4, c.NextSeed(), 0.1));
            return;
        }

        int count = c.Detailed ? 36 : 16;
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / count;
            double a = i * Golden;
            double rr = size * 4 * (1 - t * 0.7);
            Vec3 p = at - Up * (size * 10 * t) + new Vec3(Math.Cos(a) * rr, Math.Sin(a) * rr, 0);
            c.Add(c.Detailed ? MeshBuild.Sphere(MeshPart.Fruto, p, size / 2, 5, 3) : Primitives.Blob(MeshPart.Fruto, p, size / 2));
        }
    }
}
