using Viridian.Core.Geometry;
using Viridian.Core.Model3D.Profiles;

namespace Viridian.Core.Model3D.Generation;

/// <summary>
/// Árvores: tronco com colo e conicidade, galhos primários e secundários (tubos curvos), copa feita de grupos de
/// folhagem posicionados nas pontas dos galhos e na envoltória do perfil, placas de folha no nível Detalhado, flores em
/// inflorescências e frutos pendentes. Sem folhas (decíduas), a estrutura de galhos aparece.
/// </summary>
internal static class TreeBuilder
{
    private static readonly double Golden = Math.PI * (3 - Math.Sqrt(5));

    public static void Build(BuildContext c)
    {
        Plant3DProfile p = c.Profile;
        double h = c.H, r = c.R;
        double crownBase = Math.Clamp(p.Crown.BaseRatio, 0.02, 0.8) * h;
        crownBase = Math.Min(crownBase, h * 0.75);
        double crownHeight = h - crownBase;
        CrownEnvelope envelope = p.Crown.Envelope;
        bool excurrent = envelope is CrownEnvelope.Conica or CrownEnvelope.Colunar;
        double irregular = p.Crown.Irregularity;
        double phase1 = c.Random.Range(0, Geo.TwoPi), phase2 = c.Random.Range(0, Geo.TwoPi);
        double Lobe(double theta) => 1 + irregular * (0.22 * Math.Sin(2 * theta + phase1) + 0.14 * Math.Sin(3 * theta + phase2));
        double aspect = p.Crown.WidthAspect;
        Vec3 Envelope(double tn, double theta, double fraction)
        {
            double e = Env(envelope, tn) * Lobe(theta) * fraction * r;
            return new Vec3(Math.Cos(theta) * e, Math.Sin(theta) * e * aspect, crownBase + tn * crownHeight);
        }

        // ---- Tronco ----------------------------------------------------------------------------------------------
        bool showBranches = !c.Simple || c.Leafless || c.Request.Foliage == Seasonality.FoliageState.Rala || p.Crown.Density < 0.6;
        TrunkProfile trunk = p.Trunk ?? new TrunkProfile();
        double baseRadius = Math.Clamp(trunk.RadiusRatio * h, 0.025, 1.0);
        baseRadius = Math.Min(baseRadius, Math.Max(0.02, r * 0.22));
        double trunkTop = excurrent ? h * (envelope == CrownEnvelope.Conica ? 0.78 : 0.93) : crownBase + crownHeight * (envelope == CrownEnvelope.GuardaChuva ? 0.12 : 0.3);
        if (!showBranches && !excurrent)
        {
            trunkTop = crownBase + crownHeight * (envelope == CrownEnvelope.GuardaChuva ? 0.6 : 0.5); // sem galhos, o tronco entra na copa
        }
        double leanAzimuth = c.Random.Range(0, Geo.TwoPi);
        double lean = Math.Tan(Geo.DegToRad(c.Random.Range(0, trunk.LeanDeg))) * trunkTop * 0.5;
        var leanOffset = new Vec3(Math.Cos(leanAzimuth) * lean, Math.Sin(leanAzimuth) * lean, 0);
        int trunkSegments = c.Res(3, 5, 7);
        List<Vec3> trunkPath = MeshBuild.Bezier(Vec3.Zero, new Vec3(0, 0, trunkTop * 0.35), leanOffset * 0.5 + new Vec3(0, 0, trunkTop * 0.7), leanOffset + new Vec3(0, 0, trunkTop), trunkSegments);
        var trunkRadii = trunkPath.Select((_, i) =>
        {
            double t = (double)i / trunkSegments;
            double flare = 1 + trunk.Flare * Math.Max(0, 1 - t * 5);
            return baseRadius * flare * (1 + (trunk.Taper - 1) * t);
        }).ToList();
        c.Add(MeshBuild.Tube(MeshPart.Tronco, trunkPath, trunkRadii, c.Res(6, 8, 10)));
        Vec3 TrunkAt(double z)
        {
            double t = Math.Clamp(z / Math.Max(trunkTop, 1e-6), 0, 1);
            return leanOffset * (t * t) + new Vec3(0, 0, z);
        }

        // ---- Galhos ----------------------------------------------------------------------------------------------
        BranchProfile branches = p.Branches ?? new BranchProfile();
        int primaries = Math.Max(2, (int)Math.Round((c.Random.NextInt(branches.PrimaryMin, branches.PrimaryMax + 1) - (c.Simple ? 1 : 0)) * Math.Min(1, c.Factor * 1.1)));
        double primaryRadius = Math.Max(MeshBuild.MinFeature * 2, trunkRadii[^1] * (excurrent ? 0.45 : 0.75));
        var tips = new List<(Vec3 Point, double Tn, double Theta)>();
        var branchPaths = new List<(List<Vec3> Path, double Radius)>();
        var primaryTips = new List<(Vec3 Point, double Tn)>();
        var primaryPaths = new List<List<Vec3>>();
        double start = c.Random.Range(0, Geo.TwoPi);
        for (int i = 0; i < primaries; i++)
        {
            double theta = start + i * (excurrent ? Golden : Geo.TwoPi / primaries) + c.Random.Range(-0.35, 0.35);
            double startZ, endTn;
            if (excurrent)
            {
                double level = (i + 0.5) / primaries;
                startZ = crownBase + level * crownHeight * 0.85;
                endTn = Math.Min(0.95, level * 0.85 + 0.08);
            }
            else
            {
                startZ = trunkTop - c.Random.Range(0, 0.22) * crownHeight;
                endTn = envelope switch
                {
                    CrownEnvelope.GuardaChuva or CrownEnvelope.Achatada => c.Random.Range(0.5, 0.82),
                    _ => c.Random.Range(0.38, 0.78),
                };
            }

            Vec3 s = TrunkAt(Math.Min(startZ, trunkTop));
            Vec3 tip = Envelope(endTn, theta, excurrent ? 0.85 : 0.72);
            tip = new Vec3(tip.X, tip.Y, Math.Max(tip.Z, s.Z + crownHeight * 0.05));
            List<Vec3> path = BranchPath(c, s, tip, branches.AngleDeg, branches.Droop, c.Res(3, 4, 6));
            branchPaths.Add((path, primaryRadius * c.Random.Range(0.8, 1.0)));
            tips.Add((tip, endTn, theta));
            primaryTips.Add((tip, endTn));
            primaryPaths.Add(path);

            int secondaries = c.Simple ? 0 : c.Detailed || c.Leafless ? branches.SecondaryPerPrimary : (branches.SecondaryPerPrimary + 1) / 2;
            secondaries = (int)Math.Round(secondaries * Math.Min(1, c.Factor * 1.1));
            for (int k = 0; k < secondaries; k++)
            {
                double u = c.Random.Range(0.42, 0.78);
                Vec3 from = path[(int)Math.Round(u * (path.Count - 1))];
                double t2 = theta + (k % 2 == 0 ? 1 : -1) * c.Random.Range(0.35, 0.75);
                double tn2 = Math.Clamp(endTn + c.Random.Range(-0.12, 0.18), 0.15, 0.95);
                Vec3 tip2 = Envelope(tn2, t2, 0.78);
                tip2 = new Vec3(tip2.X, tip2.Y, Math.Max(tip2.Z, from.Z));
                List<Vec3> path2 = BranchPath(c, from, tip2, branches.AngleDeg * 0.8, branches.Droop, c.Res(2, 3, 4));
                branchPaths.Add((path2, primaryRadius * 0.45));
                tips.Add((tip2, tn2, t2));

                // Raminhos finos: só sem folhas, para a estrutura não parecer podada.
                if (c.Leafless && !c.Simple)
                {
                    for (int j = 0; j < (c.Detailed && c.Factor > 0.6 ? 2 : 1); j++)
                    {
                        Vec3 twigFrom = path2[path2.Count / 2 + j % Math.Max(1, path2.Count / 2)];
                        Vec3 twigTip = Envelope(Math.Clamp(tn2 + c.Random.Range(-0.05, 0.15), 0.1, 0.98), t2 + c.Random.Range(-0.5, 0.5), 0.92);
                        branchPaths.Add((MeshBuild.Bezier(twigFrom, twigFrom + (twigTip - twigFrom) * 0.4 + new Vec3(0, 0, crownHeight * 0.05), twigTip - (twigTip - twigFrom) * 0.25, twigTip, 2), primaryRadius * 0.2));
                        tips.Add((twigTip, tn2, t2));
                    }
                }
            }
        }

        if (showBranches)
        {
            foreach ((List<Vec3> path, double radius) in branchPaths)
            {
                double r0 = radius, r1 = Math.Max(MeshBuild.MinFeature * 1.5, radius * 0.3);
                c.Add(MeshBuild.Tube(MeshPart.Galho, path, path.Select((_, i) => r0 + (r1 - r0) * i / (path.Count - 1)).ToList(), c.Res(4, 5, 6)));
            }
        }

        // ---- Copa: posições dos grupos de folhagem -------------------------------------------------------------
        // Calculadas sempre (também sem folhas): flores e frutos de árvores nuas ocupam os mesmos lugares da copa.
        // Raio dos grupos pela área da envoltória dividida entre eles, com sobreposição: a copa vira uma massa contínua.
        // Copas grandes (ou altas e estreitas) recebem mais grupos, não grupos maiores.
        double ea = r * 0.78, ec = crownHeight / 2 * 0.8;
        double area = 4 * Math.PI * Math.Pow((Math.Pow(ea * ea, 1.6) + 2 * Math.Pow(ea * ec, 1.6)) / 3, 1 / 1.6);
        double scale = Math.Clamp(Math.Sqrt(area / 60), 0.7, 1.5);
        int count = c.Count(6 * scale, 14 * scale, 22 * scale, 4);
        double clusterRadius = Math.Sqrt(area / count) * 0.64 * Math.Clamp(p.Crown.ClusterScale / 0.35, 0.75, 1.3) * (c.Simple ? 1.12 : 1);
        clusterRadius = Math.Clamp(clusterRadius, 0.08, Math.Max(0.12, Math.Min(r * (excurrent ? 0.62 : 0.8), crownHeight / 2 * 0.8)));
        double flatten = envelope == CrownEnvelope.GuardaChuva ? 0.62 : envelope == CrownEnvelope.Colunar ? 1.05 : 0.82;
        double gaps = (1 - p.Crown.Density) * 0.6;
        var slots = new List<(Vec3 Center, double Radius, double Tn)>();

        if (excurrent)
        {
            // Cônicas e colunares: grupos em andares (verticilos), largos embaixo e afinando para o topo.
            int tiers = Math.Clamp((int)Math.Round(crownHeight / Math.Max(0.1, r * (envelope == CrownEnvelope.Conica ? 0.7 : 1.1))), 3, Math.Max(3, count / 2));
            double perTier = Math.Max(2, (double)count / tiers * 1.25);
            for (int t = 0; t < tiers; t++)
            {
                double tn = 0.06 + 0.84 * t / (tiers - 1);
                double env = Env(envelope, tn);
                int n = Math.Max(2, (int)Math.Round(perTier * Math.Clamp(env, 0.35, 1)));
                double rr = Math.Clamp(env * r * 0.55, clusterRadius * 0.35, clusterRadius);
                for (int j = 0; j < n; j++)
                {
                    double theta = start + t * Golden + j * Geo.TwoPi / n + c.Random.Range(-0.15, 0.15);
                    if (c.Random.Chance(gaps))
                    {
                        continue;
                    }

                    // Na superfície do cone interno (cônicas) ou junto à coluna (colunares).
                    double depth = envelope == CrownEnvelope.Conica ? c.Random.Range(0.8, 0.92) : 0.62;
                    slots.Add((Envelope(tn, theta, depth), rr * c.Random.Range(0.9, 1.1), tn));
                }
            }
        }
        else
        {
            // Nas pontas dos galhos primários (a copa segue a estrutura)...
            foreach ((Vec3 tip, double tn) in primaryTips.Take(Math.Max(1, count / 3)))
            {
                slots.Add((tip + new Vec3(0, 0, clusterRadius * 0.2), clusterRadius * c.Random.Range(0.9, 1.1), tn));
            }
        }

        // ...e distribuídos por igual na envoltória (espiral de Fibonacci), com falhas conforme a densidade.
        int fill = excurrent ? 0 : Math.Max(0, count - slots.Count);
        double tnMin = envelope == CrownEnvelope.GuardaChuva ? 0.35 : 0.1, tnMax = 0.94;
        for (int k = 0; k < fill; k++)
        {
            double u = (k + 0.5) / fill;
            double tn = tnMin + (tnMax - tnMin) * u;
            double theta = k * Golden + start;
            if (k > 0 && c.Random.Chance(gaps))
            {
                continue;
            }

            double local = Math.Clamp(Env(envelope, tn) / 0.85, envelope == CrownEnvelope.Conica ? 0.3 : 0.6, 1.0);
            Vec3 center = Envelope(tn, theta, c.Random.Range(0.6, 0.8));
            slots.Add((center, clusterRadius * local * c.Random.Range(0.85, 1.12), tn));
        }

        // Topo fechado (pequeno na ponta de uma copa cônica).
        double topRadius = clusterRadius * (envelope == CrownEnvelope.Conica ? 0.3 : 0.9);
        if (envelope != CrownEnvelope.Conica || p.Crown.Density < 0.6)
        {
            slots.Add((new Vec3(0, 0, h - topRadius * 0.75) + leanOffset, topRadius, 1)); // no cone, a ponta já fecha a copa
        }
        for (int k = 0; k < slots.Count; k++)
        {
            (Vec3 inside, double radius) = c.FitInCrown(slots[k].Center, slots[k].Radius);
            double top = Math.Min(inside.Z + radius * flatten * 0.95, h);
            slots[k] = (new Vec3(inside.X, inside.Y, top - radius * flatten * 0.95), radius, slots[k].Tn);
        }

        var clusters = new List<(Vec3 Center, double Radius, double Tn)>();
        int segments = c.Res(6, 7, 8), rings = c.Res(4, 5, 5);
        if (!c.Leafless)
        {
            bool sparse = c.Request.Foliage == Seasonality.FoliageState.Rala;
            foreach ((Vec3 center, double radius, double tn) in slots)
            {
                if (sparse && c.Random.Chance(0.45))
                {
                    continue;
                }

                clusters.Add((center, sparse ? radius * 0.8 : radius, tn));
            }

            // Miolo: massa interna que fecha a copa por dentro (copas densas, sem folhagem rala).
            if (!sparse && p.Crown.Density >= 0.6 && envelope == CrownEnvelope.Conica)
            {
                // Cone interno: a silhueta piramidal não depende só dos grupos.
                double coneBase = crownBase + crownHeight * 0.04;
                c.Add(Primitives.Frustum(MeshPart.FolhagemEscura, new Vec3(0, 0, coneBase) + leanOffset * 0.5, (h - coneBase) * 0.9, r * 0.7, Math.Max(MeshBuild.MinFeature * 3, r * 0.04), c.Res(6, 8, 10)));
            }
            else if (!sparse && p.Crown.Density >= 0.6)
            {
                double coreTn = envelope switch { CrownEnvelope.Conica => 0.33, CrownEnvelope.GuardaChuva => 0.68, _ => 0.5 };
                double coreR = r * Env(envelope, coreTn) * 0.6;
                Vec3 coreCenter = new Vec3(0, 0, crownBase + crownHeight * coreTn) + leanOffset * 0.8;
                c.Add(Primitives.Ellipsoid(MeshPart.FolhagemEscura, coreCenter, coreR, coreR * aspect, Math.Max(coreR * 0.5, crownHeight * (excurrent ? 0.36 : 0.3)),
                    c.Res(6, 7, 8), c.Res(4, 5, 5), 0.12, c.NextSeed()));
            }

            foreach ((Vec3 center, double radius, double tn) in clusters)
            {
                double outward = Math.Clamp(new Point2(center.X, center.Y).Length / Math.Max(r, 1e-6), 0, 1);
                c.Add(MeshBuild.Cluster(c.FoliageTone(tn, outward), center, radius, flatten, segments, rings, c.NextSeed()));
            }
        }

        // ---- Flores e frutos ------------------------------------------------------------------------------------
        var crownCenter = new Vec3(0, 0, crownBase + crownHeight * 0.5) + leanOffset;
        if (c.Request.Flowers && p.Flowers is not null)
        {
            Ornaments.Flowers(c, p.Flowers, c.Leafless ? slots : clusters, tips.Select(t => t.Point).ToList(), r, crownCenter, flatten);
        }

        if (c.Request.Fruits is not null)
        {
            Ornaments.Fruits(c, c.Request.Fruits, c.Leafless ? slots : clusters, trunkPath, primaryPaths);
        }

        // ---- Placas de folha (Detalhado): rugosidade da silhueta com o que sobra do orçamento ----------------
        if (c.Detailed && !c.Leafless && clusters.Count > 0 && !c.Request.SolidOnly)
        {
            Ornaments.LeafPlates(c, clusters.Select(k => (k.Center, k.Radius, k.Tn)).ToList(), r);
        }
    }

    /// <summary>Raio relativo da envoltória (0 a 1) na altura relativa <paramref name="tn"/> da copa.</summary>
    internal static double Env(CrownEnvelope envelope, double tn)
    {
        tn = Math.Clamp(tn, 0, 1);
        double x = 2 * tn - 1;
        return envelope switch
        {
            CrownEnvelope.Achatada => Math.Pow(Math.Max(0, 1 - Math.Pow(Math.Abs(x), 3)), 1 / 3.0),
            CrownEnvelope.GuardaChuva => tn < 0.6 ? 0.3 + 0.7 * Math.Sin(tn / 0.6 * Math.PI / 2) : Math.Sqrt(Math.Max(0, 1 - Math.Pow((tn - 0.6) / 0.42, 2))),
            CrownEnvelope.Colunar => Math.Pow(Math.Max(0, 1 - Math.Pow(Math.Abs(x), 4)), 0.25),
            CrownEnvelope.Conica => tn < 0.06 ? 0.75 + tn * 4 : Math.Max(0.05, 1 - Math.Pow(tn, 1.1)),
            CrownEnvelope.Pendente => Math.Sqrt(Math.Max(0, 1 - x * x)) * (1 + 0.15 * (1 - tn)),
            _ => Math.Sqrt(Math.Max(0, 1 - x * x)),
        };
    }

    /// <summary>Galho curvo do ponto de inserção à ponta, saindo no ângulo do perfil e caindo com o peso.</summary>
    private static List<Vec3> BranchPath(BuildContext c, Vec3 from, Vec3 to, double angleDeg, double droop, int segments)
    {
        Vec3 span = to - from;
        double length = span.Length;
        var horizontal = new Vec3(span.X, span.Y, 0);
        Vec3 outward = horizontal.Length > 1e-6 ? horizontal.Normalized() : new Vec3(1, 0, 0);
        double a = Geo.DegToRad(angleDeg);
        Vec3 initial = (new Vec3(0, 0, 1) * Math.Cos(a) + outward * Math.Sin(a)).Normalized();
        Vec3 p1 = from + initial * (length * 0.38);
        Vec3 p2 = to - span * 0.25 + new Vec3(0, 0, length * (0.12 - droop * 0.2));
        Vec3 sag = new(0, 0, -droop * length * 0.12 * c.Random.Range(0.6, 1.2));
        return MeshBuild.Bezier(from, p1, p2, to + sag, segments);
    }
}
