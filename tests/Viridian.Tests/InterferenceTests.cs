using Viridian.Core.Geometry;
using Viridian.Core.Integrity;
using Viridian.Core.Interference;
using Viridian.Core.Model;

namespace Viridian.Tests;

public class InterferenceTests
{
    private static InterferencePlant Tree(double x, double y, double crown = 6, long id = 1) =>
        new(id, "AR-001", "Ipê-amarelo", PlantCategory.Arvore, new Point2(x, y), 0, crown);

    private static InterferenceSettings Settings()
    {
        var s = new InterferenceSettings();
        s.Normalize();
        return s;
    }

    [Fact]
    public void PipeCloserThanMinimum_IsReported_ConsideringPipeDiameter()
    {
        // Tubo de 200 mm a 1,55 m do tronco: face a 1,45 m < 1,50 m.
        var pipe = new NetworkSegment(NetworkKind.Tubulacao, new Point2(-10, 1.55), new Point2(10, 1.55), 0.10, "tubo de esgoto");

        IReadOnlyList<InterferenceFinding> findings = InterferenceChecker.Check(new[] { Tree(0, 0) }, new[] { pipe }, Array.Empty<NetworkPoint>(), Array.Empty<PavementArea>(), Settings());

        InterferenceFinding f = Assert.Single(findings);
        Assert.Equal(NetworkKind.Tubulacao, f.Kind);
        Assert.Equal(1.45, f.DistanceM, 6);
        Assert.Equal(1.5, f.RequiredM, 6);
        Assert.Contains("tubo de esgoto", f.Message);
    }

    [Fact]
    public void FarElements_AreIgnored_AndOnlyNearestPerKindIsReported()
    {
        var segments = new[]
        {
            new NetworkSegment(NetworkKind.Tubulacao, new Point2(-5, 3), new Point2(5, 3), 0, "longe"),
            new NetworkSegment(NetworkKind.Tubulacao, new Point2(-5, 1), new Point2(5, 1), 0, "perto"),
            new NetworkSegment(NetworkKind.Tubulacao, new Point2(-5, 0.5), new Point2(5, 0.5), 0, "mais perto"),
        };

        IReadOnlyList<InterferenceFinding> findings = InterferenceChecker.Check(new[] { Tree(0, 0) }, segments, Array.Empty<NetworkPoint>(), Array.Empty<PavementArea>(), Settings());

        InterferenceFinding f = Assert.Single(findings);
        Assert.Contains("mais perto", f.Message);
    }

    [Fact]
    public void Building_UsesCrownRadius()
    {
        // Copa de 10 m: 5 m × 0,75 = 3,75 m exigidos; parede a 3 m.
        var wall = new NetworkSegment(NetworkKind.Edificacao, new Point2(3.1, -10), new Point2(3.1, 10), 0.1, "parede");
        IReadOnlyList<InterferenceFinding> findings = InterferenceChecker.Check(new[] { Tree(0, 0, crown: 10) }, new[] { wall }, Array.Empty<NetworkPoint>(), Array.Empty<PavementArea>(), Settings());

        InterferenceFinding f = Assert.Single(findings);
        Assert.Equal(3.75, f.RequiredM, 6);
        Assert.Equal(3.0, f.DistanceM, 6);

        // Copa pequena: vale o mínimo de 1,5 m e a parede a 3 m está livre.
        Assert.Empty(InterferenceChecker.Check(new[] { Tree(0, 0, crown: 3) }, new[] { wall }, Array.Empty<NetworkPoint>(), Array.Empty<PavementArea>(), Settings()));
    }

    [Fact]
    public void LightPole_ChecksByCategory()
    {
        var pole = new NetworkPoint(NetworkKind.Iluminacao, new Point2(3, 0), 0.1, "poste");
        var herb = new InterferencePlant(2, "HE-001", "Agapanto", PlantCategory.Herbacea, new Point2(0, 0), 0, 0.5);
        var palm = new InterferencePlant(3, "PA-001", "Jerivá", PlantCategory.Palmeira, new Point2(0, 0), 0, 4);

        IReadOnlyList<InterferenceFinding> findings = InterferenceChecker.Check(new[] { Tree(0, 0), herb, palm }, Array.Empty<NetworkSegment>(), new[] { pole }, Array.Empty<PavementArea>(), Settings());

        Assert.Single(findings);                       // árvore (4 m); palmeira exige 2 m e está a 2,9 m; herbácea não é verificada
        Assert.Equal(1, findings[0].PlantElementId);
    }

    [Fact]
    public void DisabledRule_IsSkipped()
    {
        InterferenceSettings settings = Settings();
        settings.RuleOf(NetworkKind.Tubulacao)!.Enabled = false;
        var pipe = new NetworkSegment(NetworkKind.Tubulacao, new Point2(-1, 0.2), new Point2(1, 0.2), 0, "tubo");
        Assert.Empty(InterferenceChecker.Check(new[] { Tree(0, 0) }, new[] { pipe }, Array.Empty<NetworkPoint>(), Array.Empty<PavementArea>(), settings));
    }

    [Fact]
    public void Pavement_FlagsTreesWithoutPitButNotInsideOpenings()
    {
        var outer = new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) };
        var pit = new[] { new Point2(4, 4), new Point2(4, 6), new Point2(6, 6), new Point2(6, 4) };
        var sidewalk = new PavementArea(new Polygon2(new[] { outer, pit }), 0, "calçada");
        var upperSlab = new PavementArea(new Polygon2(outer), 3.5, "laje do pavimento superior");

        IReadOnlyList<InterferenceFinding> findings = InterferenceChecker.Check(
            new[] { Tree(5, 5, id: 1), Tree(2, 2, id: 2) }, Array.Empty<NetworkSegment>(), Array.Empty<NetworkPoint>(), new[] { sidewalk, upperSlab }, Settings());

        InterferenceFinding f = Assert.Single(findings);
        Assert.Equal(2, f.PlantElementId);             // a árvore no berço está livre
        Assert.Equal(NetworkKind.Pavimento, f.Kind);
        Assert.Contains("calçada", f.Message);         // a laje de outro nível é ignorada
    }

    [Fact]
    public void Findings_BecomeIntegrityIssues()
    {
        var finding = new InterferenceFinding { PlantElementId = 42, Kind = NetworkKind.Tubulacao, Message = "teste" };
        IReadOnlyList<IntegrityIssue> issues = IntegrityChecker.Run(new IntegrityInput { Interferences = new[] { finding } });
        IntegrityIssue issue = Assert.Single(issues, i => i.Kind == IssueKind.Interferencia);
        Assert.Equal(new long[] { 42 }, issue.ElementIds);
        Assert.StartsWith("Tubulações", issue.Message);
    }

    [Fact]
    public void Normalize_RestoresMissingRulesAndClampsValues()
    {
        var settings = new InterferenceSettings { Rules = new List<InterferenceRule> { new() { Kind = NetworkKind.Tubulacao, TreeM = -3, PalmM = double.NaN } } };
        settings.Normalize();
        Assert.Equal(InterferenceSettings.DefaultRules().Count, settings.Rules.Count);
        Assert.Equal(0, settings.RuleOf(NetworkKind.Tubulacao)!.TreeM);
        Assert.Equal(0, settings.RuleOf(NetworkKind.Tubulacao)!.PalmM);
    }
}
