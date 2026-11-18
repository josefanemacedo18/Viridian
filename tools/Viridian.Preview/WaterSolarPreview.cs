using System.Globalization;
using System.Text;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Core.Solar;
using Viridian.Core.Water;
using Viridian.Library;

/// <summary>Prévias do jardim de chuva e de um estudo solar simulado (sem Revit).</summary>
internal static class WaterSolarPreview
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static Drawing RainGarden()
    {
        var s = new RainGardenSettings();
        RainGardenResult r = RainGardenCalculator.Calculate(120, 0, s);
        Drawing d = RainGardenCalculator.Detail(s, r, 2.2);
        Bounds2 e = d.Extent();
        double y = e.MinY - 0.25;
        foreach (string line in RainGardenCalculator.Report(120, r, s))
        {
            foreach (string part in TextLayout.Wrap(line, 120))
            {
                d.Text(new Point2(e.MinX, y), part, -1, 0.9);
                y -= 0.11;
            }
        }

        return d;
    }

    /// <summary>Lote 30 × 20 m com três árvores e um bloco de 9 m de altura a leste, em São Paulo, média do ano.</summary>
    public static string Solar()
    {
        List<Species> all = SeedCatalog.CreateSpecies();
        Species ipe = all.First(s => s.ScientificName == "Handroanthus chrysotrichus");
        var region = new Polygon2(new[] { new Point2(0, 0), new Point2(30, 0), new Point2(30, 20), new Point2(0, 20) });
        var settings = new SolarSettings { GridM = 0.75, MaxPoints = 1200, StepMinutes = 20 };
        var trees = new[] { new Point2(8, 12), new Point2(14, 6), new Point2(20, 14) };
        var crowns = trees.Select((p, i) => CrownObstacle.For("t" + i, ipe, new Vec3(p.X, p.Y, 0), GrowthStage.Maturidade)!).ToList();
        (double x0, double y0, double x1, double y1, double h) building = (24, 2, 34, 10, 9);
        bool Blocked(Vec3 o, Vec3 d)
        {
            // Raio contra a caixa do edifício (método das placas).
            double tmin = 0, tmax = 1e9;
            foreach ((double o1, double d1, double lo, double hi) in new[] { (o.X, d.X, building.x0, building.x1), (o.Y, d.Y, building.y0, building.y1), (o.Z, d.Z, 0.0, building.h) })
            {
                if (Math.Abs(d1) < 1e-12)
                {
                    if (o1 < lo || o1 > hi)
                    {
                        return false;
                    }

                    continue;
                }

                double ta = (lo - o1) / d1, tb = (hi - o1) / d1;
                tmin = Math.Max(tmin, Math.Min(ta, tb));
                tmax = Math.Min(tmax, Math.Max(ta, tb));
            }

            return tmax >= tmin && tmax > 1e-6;
        }

        (List<Point2> grid, double cell) = SolarStudy.Grid(region, settings.GridM, settings.MaxPoints);
        var points = grid.Select(p => new SolarPoint { Position = new Vec3(p.X, p.Y, 0.3), Hours = new double[3] }).ToList();
        SolarStudy.Compute(points, crowns, -23.55, Sun.KeyDates(-23.55), settings, v => v, Blocked);

        const double k = 26;
        var sb = new StringBuilder();
        sb.Append(Inv, $"<svg xmlns='http://www.w3.org/2000/svg' width='{36 * k + 260}' height='{24 * k + 40}'><rect width='100%' height='100%' fill='#fff'/>");
        double Y(double y) => 20 + (22 - y) * k;
        for (int i = 0; i < grid.Count; i++)
        {
            string color = SolarClasses.Bins[SolarClasses.BinOf(points[i].Average)].Color;
            sb.Append(Inv, $"<rect x='{20 + (grid[i].X - cell / 2) * k:0.#}' y='{Y(grid[i].Y + cell / 2):0.#}' width='{cell * k * 0.96:0.#}' height='{cell * k * 0.96:0.#}' fill='{color}' fill-opacity='0.85'/>");
        }

        sb.Append(Inv, $"<rect x='{20 + building.x0 * k}' y='{Y(building.y1)}' width='{(building.x1 - building.x0) * k}' height='{(building.y1 - building.y0) * k}' fill='none' stroke='#222' stroke-width='2'/>");
        foreach (Point2 t in trees)
        {
            sb.Append(Inv, $"<circle cx='{20 + t.X * k:0.#}' cy='{Y(t.Y):0.#}' r='{ipe.CrownDiameterM / 2 * k:0.#}' fill='none' stroke='#1F4D3A' stroke-width='2' stroke-dasharray='6 4'/>");
        }

        sb.Append(Inv, $"<text x='20' y='14' font-family='Arial' font-size='13'>Estudo solar simulado — média do ano, latitude -23,55° (norte para cima); bloco de 9 m a leste</text>");
        double ly = 60;
        foreach ((double from, double to, string color, string label) in SolarClasses.Bins)
        {
            sb.Append(Inv, $"<rect x='{36 * k + 40}' y='{ly}' width='22' height='16' fill='{color}'/><text x='{36 * k + 70}' y='{ly + 12}' font-family='Arial' font-size='12'>{label}</text>");
            ly += 24;
        }

        sb.Append("</svg>");
        return sb.ToString();
    }
}
