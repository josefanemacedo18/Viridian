using System.Globalization;
using Viridian.Core.Model;

namespace Viridian.Core.Hatch;

/// <summary>Tipo de padrão de preenchimento do Revit.</summary>
public enum HatchTarget
{
    /// <summary>Padrão de desenho: o espaçamento é mantido em relação à folha, em qualquer escala.</summary>
    Drafting = 0,

    /// <summary>Padrão de modelo: o espaçamento é mantido em relação às dimensões reais.</summary>
    Model = 1,
}

/// <summary>Família de linhas paralelas de um padrão. Todas as medidas em milímetros.</summary>
public sealed class HatchGrid
{
    public double AngleDeg { get; set; }

    public double OriginX { get; set; }

    public double OriginY { get; set; }

    /// <summary>Deslocamento ao longo da linha entre linhas vizinhas.</summary>
    public double Shift { get; set; }

    /// <summary>Distância perpendicular entre linhas vizinhas.</summary>
    public double Offset { get; set; }

    /// <summary>Traços e espaços alternados (traço, espaço, traço...). Vazio = linha contínua.</summary>
    public List<double> Segments { get; set; } = new();

    public HatchGrid Clone() => new()
    {
        AngleDeg = AngleDeg,
        OriginX = OriginX,
        OriginY = OriginY,
        Shift = Shift,
        Offset = Offset,
        Segments = new List<double>(Segments),
    };
}

/// <summary>Definição de uma hachura da biblioteca Viridian.</summary>
public sealed class HatchPattern
{
    /// <summary>Identificador estável usado nos cadastros.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Nome do padrão de preenchimento no Revit.</summary>
    public string Name { get; set; } = string.Empty;

    public string Application { get; set; } = string.Empty;

    public HatchTarget Target { get; set; } = HatchTarget.Drafting;

    public List<HatchGrid> Grids { get; set; } = new();

    public bool IsBuiltIn { get; set; }

    public override string ToString() => Name;

    /// <summary>Verifica se a definição pode ser criada no Revit.</summary>
    public bool IsValid(out string problem)
    {
        problem = string.Empty;
        if (string.IsNullOrWhiteSpace(Key) || string.IsNullOrWhiteSpace(Name))
        {
            problem = "A hachura precisa de identificador e nome.";
            return false;
        }

        if (Grids.Count == 0)
        {
            problem = "A hachura não possui linhas.";
            return false;
        }

        foreach (HatchGrid grid in Grids)
        {
            if (!(Math.Abs(grid.Offset) >= HatchLibrary.MinOffsetMm))
            {
                problem = $"A distância entre linhas deve ser de pelo menos {HatchLibrary.MinOffsetMm:0.##} mm.";
                return false;
            }

            if (grid.Segments.Count % 2 != 0 || grid.Segments.Any(s => !(s > 0)))
            {
                problem = "Traços e espaços devem ser positivos e em pares.";
                return false;
            }

            if (!double.IsFinite(grid.AngleDeg) || !double.IsFinite(grid.Shift) || !double.IsFinite(grid.OriginX) || !double.IsFinite(grid.OriginY))
            {
                problem = "A hachura contém valores inválidos.";
                return false;
            }
        }

        return true;
    }
}

/// <summary>Biblioteca de hachuras do Viridian.</summary>
public static class HatchLibrary
{
    public const double MinOffsetMm = 0.4;

    /// <summary>Menor traço usado para representar um ponto (o Revit não aceita traço de comprimento zero).</summary>
    public const double DotMm = 0.25;

    public const string OrganicDots = "pontilhado-organico";
    public const string DenseDots = "pontilhado-denso";
    public const string DiagonalDashes = "tracos-diagonais";
    public const string Irregular = "padrao-irregular";
    public const string RepeatedSymbols = "simbolos-repetidos";
    public const string GrassTufts = "gramado-tufos";
    public const string Water = "lamina-dagua";
    public const string Simplified = "hachura-simplificada";

    public static readonly IReadOnlyList<HatchPattern> BuiltIn = Build();

    public static HatchPattern? Find(string? key, IEnumerable<HatchPattern>? custom = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return BuiltIn.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? custom?.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Hachura sugerida para a categoria de vegetação.</summary>
    public static string DefaultKeyFor(PlantCategory category) => category switch
    {
        PlantCategory.Forracao => OrganicDots,
        PlantCategory.Gramado => DenseDots,
        PlantCategory.Herbacea => DiagonalDashes,
        PlantCategory.Trepadeira => DiagonalDashes,
        PlantCategory.Arbusto => Irregular,
        PlantCategory.Aquatica => Water,
        _ => RepeatedSymbols,
    };

    private static List<HatchPattern> Build()
    {
        const double sqrt2 = 1.4142135623730951;
        const double tuft = 6.0;

        var list = new List<HatchPattern>
        {
            new()
            {
                Key = OrganicDots,
                Name = "VIR Pontilhado orgânico",
                Application = "Forrações",
                Grids =
                {
                    Grid(37.5, 0, 0, 2.85, 3.98, DotMm, 3.86, DotMm, 4.32, DotMm, 4.13),
                    Grid(7.5, 0, 0, 5.39, 6.52, DotMm, 2.08, DotMm, 3.48, DotMm, 1.33),
                    Grid(-32.5, -3.12, 0, 6.66, 4.26, DotMm, 1.27, DotMm, 4.57, DotMm, 5.97),
                    Grid(-42.5, -3.12, 0, 4.12, 6.80, DotMm, 0.64, DotMm, 3.00, DotMm, 3.43),
                },
            },
            new()
            {
                Key = DenseDots,
                Name = "VIR Pontilhado denso",
                Application = "Gramados e coberturas compactas",
                Grids =
                {
                    Grid(0, 0, 0, 0.75, 1.3, DotMm, 1.25),
                },
            },
            new()
            {
                Key = DiagonalDashes,
                Name = "VIR Traços diagonais",
                Application = "Áreas de vegetação rasteira",
                Grids =
                {
                    Grid(45, 0, 0, 2.0, 2.2, 2.4, 1.6),
                },
            },
            new()
            {
                Key = Irregular,
                Name = "VIR Padrão irregular",
                Application = "Maciços arbustivos",
                Grids =
                {
                    Grid(20, 0, 0, 3.1, 4.7, 1.6, 5.9),
                    Grid(80, 1.2, 0.7, 2.3, 5.3, 1.4, 6.7),
                    Grid(140, 0.5, 1.9, 3.7, 4.9, 1.5, 7.3),
                },
            },
            new()
            {
                Key = RepeatedSymbols,
                Name = "VIR Símbolos repetidos",
                Application = "Plantios individualizados",
                Grids =
                {
                    // Cruzes em malha alternada: traços horizontais e verticais que se encontram no centro.
                    Grid(0, 0, 0, 2.0, 4.0, 1.2, 2.8),
                    Grid(90, 0.6, -0.6, 4.0, 2.0, 1.2, 6.8),
                },
            },
            new()
            {
                Key = GrassTufts,
                Name = "VIR Gramado em tufos",
                Application = "Gramados",
                Grids =
                {
                    // Três traços saindo do mesmo ponto formam cada tufo; duas malhas intercaladas.
                    Grid(90, 0, 0, 0, tuft, 1.4, tuft - 1.4),
                    Grid(45, 0, 0, tuft / sqrt2, tuft / sqrt2, 1.2, tuft * sqrt2 - 1.2),
                    Grid(135, 0, 0, tuft / sqrt2, tuft / sqrt2, 1.2, tuft * sqrt2 - 1.2),
                    Grid(90, tuft / 2, tuft / 2, 0, tuft, 1.4, tuft - 1.4),
                    Grid(45, tuft / 2, tuft / 2, tuft / sqrt2, tuft / sqrt2, 1.2, tuft * sqrt2 - 1.2),
                    Grid(135, tuft / 2, tuft / 2, tuft / sqrt2, tuft / sqrt2, 1.2, tuft * sqrt2 - 1.2),
                },
            },
            new()
            {
                Key = Water,
                Name = "VIR Lâmina d'água",
                Application = "Plantas aquáticas",
                Grids =
                {
                    Grid(0, 0, 0, 3.5, 2.5, 5.0, 2.0),
                },
            },
            new()
            {
                Key = Simplified,
                Name = "VIR Hachura simplificada",
                Application = "Maciços de densidade muito alta",
                Grids =
                {
                    Grid(45, 0, 0, 0, 3.5),
                },
            },
        };

        foreach (HatchPattern pattern in list)
        {
            pattern.IsBuiltIn = true;
            pattern.Target = HatchTarget.Drafting;
        }

        return list;
    }

    private static HatchGrid Grid(double angle, double ox, double oy, double shift, double offset, params double[] segments) => new()
    {
        AngleDeg = angle,
        OriginX = ox,
        OriginY = oy,
        Shift = shift,
        Offset = offset,
        Segments = segments.ToList(),
    };
}

/// <summary>Leitor de arquivos de hachura .pat (formato AutoCAD/Revit), para padrões customizados.</summary>
public static class PatFile
{
    public static IReadOnlyList<HatchPattern> Parse(string text, out IReadOnlyList<string> warnings)
    {
        var result = new List<HatchPattern>();
        var notes = new List<string>();
        warnings = notes;
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        double unit = 25.4; // sem indicação de unidade, o formato assume polegadas
        HatchTarget target = HatchTarget.Drafting;
        HatchPattern? current = null;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(';'))
            {
                string directive = line.TrimStart(';').Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
                if (directive.StartsWith("%UNITS=", StringComparison.Ordinal))
                {
                    unit = directive.EndsWith("MM", StringComparison.Ordinal) ? 1.0 : 25.4;
                }
                else if (directive.StartsWith("%TYPE=", StringComparison.Ordinal))
                {
                    target = directive.EndsWith("MODEL", StringComparison.Ordinal) ? HatchTarget.Model : HatchTarget.Drafting;
                    if (current is not null && current.Grids.Count == 0)
                    {
                        current.Target = target;
                    }
                }

                continue;
            }

            if (line.StartsWith('*'))
            {
                string header = line[1..];
                int comma = header.IndexOf(',');
                string name = (comma >= 0 ? header[..comma] : header).Trim();
                string description = comma >= 0 ? header[(comma + 1)..].Trim() : string.Empty;
                if (name.Length == 0)
                {
                    current = null;
                    continue;
                }

                current = new HatchPattern
                {
                    Key = "custom-" + Slug(name),
                    Name = "VIR " + name,
                    Application = description.Length > 0 ? description : "Padrão customizado",
                    Target = target,
                };
                result.Add(current);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            HatchGrid? grid = ParseGrid(line, unit);
            if (grid is null)
            {
                notes.Add($"Linha ignorada em \"{current.Name}\": {line}");
            }
            else
            {
                current.Grids.Add(grid);
            }
        }

        for (int i = result.Count - 1; i >= 0; i--)
        {
            if (!result[i].IsValid(out string problem))
            {
                notes.Add($"Padrão \"{result[i].Name}\" descartado: {problem}");
                result.RemoveAt(i);
            }
        }

        return result;
    }

    private static HatchGrid? ParseGrid(string line, double unit)
    {
        string content = line;
        int comment = content.IndexOf(';');
        if (comment >= 0)
        {
            content = content[..comment];
        }

        string[] parts = content.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 5)
        {
            return null;
        }

        var values = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
            {
                return null;
            }
        }

        var grid = new HatchGrid
        {
            AngleDeg = values[0],
            OriginX = values[1] * unit,
            OriginY = values[2] * unit,
            Shift = values[3] * unit,
            Offset = values[4] * unit,
        };

        // No formato .pat, valores positivos são traços, negativos são espaços e zero é ponto.
        var runs = new List<(bool Dash, double Length)>();
        for (int i = 5; i < values.Length; i++)
        {
            bool dash = values[i] >= 0;
            double length = dash ? Math.Max(values[i] * unit, HatchLibrary.DotMm) : -values[i] * unit;
            if (runs.Count > 0 && runs[^1].Dash == dash)
            {
                runs[^1] = (dash, runs[^1].Length + length);
            }
            else
            {
                runs.Add((dash, length));
            }
        }

        if (runs.Count > 0 && !runs[0].Dash)
        {
            // Começa por espaço: desloca a origem e leva o espaço para o fim da sequência.
            (bool _, double gap) = runs[0];
            runs.RemoveAt(0);
            double radians = grid.AngleDeg * Math.PI / 180.0;
            grid.OriginX += Math.Cos(radians) * gap;
            grid.OriginY += Math.Sin(radians) * gap;
            if (runs.Count > 0 && !runs[^1].Dash)
            {
                runs[^1] = (false, runs[^1].Length + gap);
            }
            else
            {
                runs.Add((false, gap));
            }
        }

        if (runs.Count == 1)
        {
            // Apenas traço, sem espaço: equivale a linha contínua.
            runs.Clear();
        }
        else if (runs.Count % 2 != 0)
        {
            return null;
        }

        grid.Segments = runs.Select(r => r.Length).ToList();
        return grid;
    }

    private static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        string slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Length == 0 ? "padrao" : slug;
    }
}
