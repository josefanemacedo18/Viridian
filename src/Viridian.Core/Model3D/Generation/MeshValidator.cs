namespace Viridian.Core.Model3D.Generation;

/// <summary>Problema encontrado numa malha gerada.</summary>
public sealed record MeshIssue(string Code, string Message);

/// <summary>
/// Conferência das malhas antes de irem para o Revit: números válidos, triângulos sem área, arestas curtas demais,
/// sólidos fechados e com volume, caixa compatível com o porte e orçamento de triângulos.
/// </summary>
public static class MeshValidator
{
    /// <summary>Menor aresta aceita (m). O Revit descarta curvas abaixo de ~0,8 mm; a folga evita surpresas.</summary>
    public const double ShortEdge = 0.002;

    /// <summary>Menor área de triângulo aceita (m²).</summary>
    public const double MinArea = 1e-8;

    public static List<MeshIssue> Check(PlantModel model, int budget = 0)
    {
        ArgumentNullException.ThrowIfNull(model);
        var issues = new List<MeshIssue>();
        if (model.Pieces.Count == 0 || model.TriangleCount == 0)
        {
            issues.Add(new MeshIssue("vazio", "A planta não tem geometria."));
            return issues;
        }

        foreach (MeshPiece piece in model.Pieces)
        {
            string name = piece.Part + (piece.Closed ? " (sólido)" : " (superfície)");
            if (piece.Vertices.Any(v => !double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z)))
            {
                issues.Add(new MeshIssue("numero", $"{name}: coordenada inválida."));
                continue;
            }

            if (piece.Triangles.Any(t => t.A < 0 || t.B < 0 || t.C < 0 || t.A >= piece.Vertices.Count || t.B >= piece.Vertices.Count || t.C >= piece.Vertices.Count))
            {
                issues.Add(new MeshIssue("indice", $"{name}: triângulo aponta para vértice inexistente."));
                continue;
            }

            int degenerate = piece.Triangles.Count(t => Area(piece, t) < MinArea);
            if (degenerate > 0)
            {
                issues.Add(new MeshIssue("degenerado", $"{name}: {degenerate} triângulo(s) sem área."));
            }

            double shortest = piece.ShortestEdge();
            if (shortest < ShortEdge)
            {
                issues.Add(new MeshIssue("aresta-curta", $"{name}: aresta de {shortest * 1000:0.##} mm."));
            }

            if (piece.Closed)
            {
                if (!piece.IsWatertight())
                {
                    issues.Add(new MeshIssue("aberto", $"{name}: o sólido não está fechado."));
                }
                else if (piece.SignedVolume() <= 0)
                {
                    issues.Add(new MeshIssue("invertido", $"{name}: volume nulo ou faces invertidas."));
                }
            }
        }

        (Vec3 min, Vec3 max) = Bounds(model);
        double height = Math.Max(0.02, model.HeightM);
        double radius = Math.Max(0.05, model.CrownDiameterM / 2);
        if (max.Z > height * 1.3 + 0.05)
        {
            issues.Add(new MeshIssue("alta", $"Altura de {max.Z:0.##} m para porte de {height:0.##} m."));
        }

        if (min.Z < -0.03)
        {
            issues.Add(new MeshIssue("abaixo", $"Geometria {(-min.Z) * 100:0.#} cm abaixo do chão."));
        }

        double reach = model.Pieces.SelectMany(p => p.Vertices).Max(v => Math.Sqrt(v.X * v.X + v.Y * v.Y));
        if (reach > radius * 1.5 + 0.1)
        {
            issues.Add(new MeshIssue("larga", $"Alcance de {reach:0.##} m para copa de raio {radius:0.##} m."));
        }

        if (budget > 0 && model.TriangleCount > budget)
        {
            issues.Add(new MeshIssue("orcamento", $"{model.TriangleCount} triângulos para orçamento de {budget}."));
        }

        return issues;
    }

    /// <summary>
    /// Limpeza segura antes do Revit: descarta peças com coordenadas inválidas e triângulos sem área das superfícies
    /// abertas (nos sólidos, remover triângulos abriria a malha; o Revit decide e, se recusar, a peça vira malha).
    /// </summary>
    public static int Sanitize(PlantModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        int removed = model.Pieces.RemoveAll(p => p.Vertices.Any(v => !double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z)));
        foreach (MeshPiece piece in model.Pieces.Where(p => !p.Closed))
        {
            removed += piece.Triangles.RemoveAll(t => Area(piece, t) < MinArea);
        }

        model.Pieces.RemoveAll(p => p.TriangleCount == 0);
        return removed;
    }

    public static (Vec3 Min, Vec3 Max) Bounds(PlantModel model)
    {
        var all = model.Pieces.SelectMany(p => p.Vertices).ToList();
        if (all.Count == 0)
        {
            return (Vec3.Zero, Vec3.Zero);
        }

        return (new Vec3(all.Min(v => v.X), all.Min(v => v.Y), all.Min(v => v.Z)), new Vec3(all.Max(v => v.X), all.Max(v => v.Y), all.Max(v => v.Z)));
    }

    private static double Area(MeshPiece piece, Tri t)
    {
        (Vec3 a, Vec3 b, Vec3 c) = piece.Corners(t);
        return (b - a).Cross(c - a).Length / 2;
    }
}
