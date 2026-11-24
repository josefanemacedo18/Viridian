using System.Globalization;
using System.Text;

namespace Viridian.Core.Model3D;

public enum TestOutcome
{
    Ok,
    Aviso,
    Falha,
    Info,
}

/// <summary>Uma etapa de um teste do motor 3D no Revit.</summary>
public sealed record TestStep(string Name, TestOutcome Outcome, string Detail, long Milliseconds);

/// <summary>
/// Relatório dos testes do motor 3D (Fase 0: família BIM; T0: texturas), em texto simples para o usuário enviar.
/// </summary>
public sealed class EngineTestReport
{
    public EngineTestReport(string title)
    {
        Title = title;
    }

    public string Title { get; }

    public List<TestStep> Steps { get; } = new();

    public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);

    public bool Failed => Steps.Any(s => s.Outcome == TestOutcome.Falha);

    public int Count(TestOutcome outcome) => Steps.Count(s => s.Outcome == outcome);

    public void Add(string name, TestOutcome outcome, string detail = "", long milliseconds = 0) => Steps.Add(new TestStep(name, outcome, detail, milliseconds));

    /// <summary>Executa uma etapa medindo o tempo; exceções viram falha com a mensagem (o teste segue nas próximas).</summary>
    public T? Run<T>(string name, Func<(T? Value, TestOutcome Outcome, string Detail)> step)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            (T? value, TestOutcome outcome, string detail) = step();
            Add(name, outcome, detail, watch.ElapsedMilliseconds);
            return value;
        }
        catch (Exception ex)
        {
            Add(name, TestOutcome.Falha, ex.GetType().Name + ": " + ex.Message, watch.ElapsedMilliseconds);
            return default;
        }
    }

    public string Summary() =>
        $"{Count(TestOutcome.Ok)} etapa(s) aprovada(s), {Count(TestOutcome.Aviso)} aviso(s), {Count(TestOutcome.Falha)} falha(s).";

    public string ToText()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(Title);
        sb.AppendLine(new string('=', Math.Min(80, Title.Length)));
        foreach ((string key, string value) in Environment.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"{key}: {value}");
        }

        sb.AppendLine();
        foreach (TestStep step in Steps)
        {
            string mark = step.Outcome switch { TestOutcome.Ok => "[OK]   ", TestOutcome.Aviso => "[AVISO]", TestOutcome.Falha => "[FALHA]", _ => "[INFO] " };
            sb.Append(mark).Append(' ').Append(step.Name);
            if (step.Milliseconds > 0)
            {
                sb.Append(inv, $" ({step.Milliseconds} ms)");
            }

            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(step.Detail))
            {
                foreach (string line in step.Detail.Split('\n'))
                {
                    sb.AppendLine("        " + line.TrimEnd());
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine(Summary());
        return sb.ToString();
    }
}
