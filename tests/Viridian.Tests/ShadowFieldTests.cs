using Viridian.Core.Geometry;
using Viridian.Core.Model3D;
using Viridian.Core.Solar;
using Xunit;

namespace Viridian.Tests;

/// <summary>Motor de sombras realista: geometria das edificações, copas, troncos e contornos suaves.</summary>
public class ShadowFieldTests
{
    /// <summary>Caixa (prisma) em triângulos.</summary>
    private static IEnumerable<(Tri3, bool)> Box(double x0, double y0, double x1, double y1, double z0, double z1)
    {
        Vec3[] c =
        {
            new(x0, y0, z0), new(x1, y0, z0), new(x1, y1, z0), new(x0, y1, z0),
            new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1),
        };
        int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
        foreach (int[] f in faces)
        {
            yield return (new Tri3(c[f[0]], c[f[1]], c[f[2]]), false);
            yield return (new Tri3(c[f[0]], c[f[2]], c[f[3]]), false);
        }
    }

    private static Vec3 SunFrom(double azimuthFromPlusXDeg, double altitudeDeg)
    {
        double az = azimuthFromPlusXDeg * Math.PI / 180, alt = altitudeDeg * Math.PI / 180;
        return new Vec3(Math.Cos(az) * Math.Cos(alt), Math.Sin(az) * Math.Cos(alt), Math.Sin(alt));
    }

    private static Polygon2 Rect(double x0, double y0, double x1, double y1) =>
        new(new[] { new Point2(x0, y0), new Point2(x1, y0), new Point2(x1, y1), new Point2(x0, y1) });

    private static double At(LightField f, double x, double y) =>
        f[(int)Math.Round((x - f.X0) / f.Resolution), (int)Math.Round((y - f.Y0) / f.Resolution)];

    [Fact]
    public void Mesh_RayIsBlockedOnlyTowardTheWall()
    {
        var mesh = new ObstacleMesh(Box(0, 0, 0.2, 10, 0, 3));
        var p = new Vec3(-2, 5, 0.3);
        Assert.True(mesh.Blocks(p, SunFrom(0, 30)));      // sol atrás do muro (a leste): bloqueado
        Assert.False(mesh.Blocks(p, SunFrom(180, 30)));   // sol do outro lado
        Assert.False(mesh.Blocks(p, SunFrom(0, 70)));     // sol alto: passa por cima (2 m × tan70 > 3 m)
        Assert.False(mesh.Blocks(new Vec3(-2, 15, 0.3), SunFrom(0, 30))); // além do fim do muro
    }

    [Fact]
    public void WallShadow_HasTheLengthGivenByHeightAndSunAltitude()
    {
        // Muro de 3 m; sol a 45° vindo de +X: sombra de 3 m para -X.
        var mesh = new ObstacleMesh(Box(0, 0, 0.2, 10, 0, 3));
        Polygon2 region = Rect(-6, 0, 2, 10);
        List<List<Vec3>> casters = mesh.Above(ShadowField.MinCasterHeightM, region.Bounds.Expand(80));
        LightField f = ShadowField.Compute(region, 0.05, 0, SunFrom(0, 45), 6, Array.Empty<CrownObstacle>(), casters);
        Assert.Equal(0, At(f, -1.5, 5), 6);
        Assert.Equal(0, At(f, -2.7, 5), 6);
        Assert.Equal(1, At(f, -3.4, 5), 6);
        Assert.Equal(1, At(f, 1.0, 5), 6);                       // lado do sol
        Assert.Equal(LightField.BuildingValue, At(f, 0.1, 5), 6); // sob o muro
    }

    [Fact]
    public void Crown_CentreLetsThroughOneMinusOpacityAndTheEdgeAlmostAll()
    {
        var crown = new CrownObstacle("c", new Vec3(0, 0, 5), 2, 1.5, 0.85, 0);
        var down = new Vec3(0, 0, 1);
        Assert.Equal(0.15, crown.Transmission(new Vec3(0, 0, 0), down, 6), 6);
        Assert.InRange(crown.Transmission(new Vec3(1.98, 0, 0), down, 6), 0.75, 1);
        Assert.Equal(1, crown.Transmission(new Vec3(2.5, 0, 0), down, 6));
        double mid = crown.Transmission(new Vec3(1.2, 0, 0), down, 6);
        Assert.InRange(mid, 0.15, 0.5);
    }

    [Fact]
    public void TreeShadow_IsOffsetFromTheTrunkByItsHeight()
    {
        // Árvore de 8 m: copa a ~5 m; sol a 45° vindo de +Y: a sombra da copa cai ~5 m para -Y, não sob a árvore.
        var crown = new CrownObstacle("ipê", new Vec3(0, 0, 5.3), 2.5, 2.7, 0.85, 0, 0, 1, 0.15);
        Polygon2 region = Rect(-6, -12, 6, 4);
        LightField f = ShadowField.Compute(region, 0.05, 0, SunFrom(90, 45), 6, new[] { crown }, null);
        Assert.True(At(f, 0, -5.3) < 0.35, "centro da sombra da copa");
        Assert.True(At(f, 3.5, -5.3) > 0.75, "fora da sombra, ao lado");
        Assert.True(At(f, 0, 2.5) > 0.99, "do lado do sol");
        Assert.True(At(f, 0, -1.0) < 0.01, "sombra do tronco");
    }

    [Fact]
    public void Rings_PaintedLargestFirst_ReproduceTheField()
    {
        var mesh = new ObstacleMesh(Box(4, 2, 4.3, 9, 0, 2.5).Concat(Box(-5, 6, -2, 9, 0, 4)));
        var crowns = new List<CrownObstacle>
        {
            new("a", new Vec3(-1, 0, 5), 2.5, 2.5, 0.85, 0, 0, 1, 0.15),
            new("b", new Vec3(2, -4, 3), 1.5, 1.2, 0.55, 0),
        };
        Polygon2 region = new(new[] { new Point2(-8, -9), new Point2(8, -9), new Point2(8, 10), new Point2(0, 10), new Point2(-8, 3) });
        Vec3 sun = SunFrom(50, 38);
        LightField f = ShadowField.Compute(region, 0.08, 0, sun, 6, crowns, mesh.Above(0.2, region.Bounds.Expand(80)));
        List<ShadeRing> rings = ShadowField.Rings(f);
        Assert.Contains(rings, r => r.Fill == ShadeClass.Sombra);
        Assert.Contains(rings, r => r.Fill == ShadeClass.MeiaLuz);
        Assert.Contains(rings, r => r.Fill == ShadeClass.Edificacao);
        for (int i = 1; i < rings.Count; i++)
        {
            Assert.True(rings[i - 1].Area >= rings[i].Area);
        }

        // Pinta por cima (do maior ao menor) e compara com a classe do campo em pontos longe das bordas.
        var random = new Random(7);
        int checkedPoints = 0, agree = 0;
        for (int n = 0; n < 4000; n++)
        {
            int i = random.Next(1, f.Nx - 1), j = random.Next(1, f.Ny - 1);
            double v = f[i, j];
            ShadeClass expected = LightField.ClassOf(v);
            bool nearEdge = false;
            for (int di = -1; di <= 1 && !nearEdge; di++)
            {
                for (int dj = -1; dj <= 1; dj++)
                {
                    if (LightField.ClassOf(f[i + di, j + dj]) != expected)
                    {
                        nearEdge = true;
                        break;
                    }
                }
            }

            if (nearEdge)
            {
                continue;
            }

            Point2 p = f.Node(i, j);
            ShadeClass painted = ShadeClass.Sol;
            foreach (ShadeRing r in rings)
            {
                if (Geo.PointInRing(r.Points, p))
                {
                    painted = r.Fill;
                }
            }

            checkedPoints++;
            agree += painted == expected ? 1 : 0;
        }

        Assert.True(checkedPoints > 1000);
        Assert.True(agree >= checkedPoints * 0.99, $"{agree} de {checkedPoints}");
    }

    [Fact]
    public void Rings_AreSimpleAndCountTheShadedGround()
    {
        var crown = new CrownObstacle("c", new Vec3(0, 0, 4), 2, 2, 0.85, 0);
        LightField f = ShadowField.Compute(Rect(-5, -5, 5, 5), 0.05, 0, new Vec3(0, 0, 1), 6, new[] { crown }, null);
        List<ShadeRing> rings = ShadowField.Rings(f);
        Assert.All(rings, r => Assert.False(new Polygon2(r.Points).HasSelfIntersections()));
        ShadeRing dark = rings.Single(r => r.Fill == ShadeClass.Sombra);
        Assert.True(dark.DarkInside);
        // Sol a pino: sombra escura num círculo de raio ~0,83 × 2 m (luz < 35%).
        Assert.InRange(dark.Area, Math.PI * 1.5 * 1.5, Math.PI * 1.85 * 1.85);
        Assert.InRange(f.ShadedFraction(), Math.PI * 1.5 * 1.5 / 100, Math.PI * 2 * 2 / 100);
    }
}
