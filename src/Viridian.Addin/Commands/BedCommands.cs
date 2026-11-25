using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Viridian.Core.Composition;
using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Core.Hatch;
using Viridian.Core.Massing;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Library;
using Viridian.Revit;
using Viridian.UI.Windows;

namespace Viridian.Addin.Commands;

internal static class BedFlows
{
    /// <summary>Registro de um maciço novo para a espécie, com os padrões do Criar Maciço.</summary>
    public static MassingRecord NewMassingRecord(ViridianContext ctx, Species species, MassingRecord? settings = null)
    {
        IReadOnlyList<MassingInfo> all = MassingService.ReadMassings(ctx.Doc);
        var used = new HashSet<string>(all.Select(m => m.Record.Id.Trim()), StringComparer.OrdinalIgnoreCase);
        MassingRecord record = settings?.Clone() ?? new MassingRecord
        {
            Mode = MassingMode.Hachura,
            Pattern = DistributionPattern.Triangular,
            SpacingM = SupplyUnits.IsAreaBased(species.SupplyUnit) ? 0 : species.EffectiveSpacingM,
            DensityPerM2 = SupplyUnits.IsAreaBased(species.SupplyUnit) ? 0 : species.DensityPerM2,
        };
        record.Id = MassingCalculator.NextId(used, ctx.Settings.MassingPrefix);
        record.Seed = Random.Shared.NextInt64(1, long.MaxValue);
        record.Boundary = null;
        record.Quantity = 0;
        if (settings is null || HatchLibrary.Find(record.HatchPatternKey, ctx.Settings.CustomHatches) is null)
        {
            record.HatchPatternKey = HatchLibrary.ProjectKey(species, ReportBuilder.DistinctMassings(all).Select(m => (m.Record.SpeciesId, m.Record.HatchPatternKey)));
        }

        return record;
    }

    /// <summary>Exemplares e maciços de um canteiro, para gravar o desenho como modelo.</summary>
    public static TemplateDrawing Capture(ViridianContext ctx, View view, Polygon2 polygon, Point2 origin)
    {
        var drawing = new TemplateDrawing
        {
            Outline = polygon.Rings.Select(r => r.Select(p => p - origin).ToList()).ToList(),
        };

        foreach ((PlantRecord record, Species species) in DesignFlows.Plants(ctx, view, polygon))
        {
            double rotation = ctx.Doc.GetElement(new ElementId(record.ElementId))?.Location is LocationPoint lp ? lp.Rotation : 0;
            Point2 rel = record.Location - origin;
            drawing.Plants.Add(new TemplatePlant
            {
                SpeciesId = species.Id,
                ScientificName = species.ScientificName,
                PopularName = species.PopularName,
                X = rel.X,
                Y = rel.Y,
                Rotation = rotation,
            });
        }

        foreach ((MassingInfo info, Species species) in DesignFlows.Massings(ctx, view, polygon))
        {
            if (info.Boundary is not { IsEmpty: false } boundary)
            {
                continue;
            }

            MassingRecord settings = info.Record.Clone();
            settings.Boundary = null;
            drawing.Massings.Add(new TemplateMassing
            {
                SpeciesId = species.Id,
                ScientificName = species.ScientificName,
                PopularName = species.PopularName,
                Rings = boundary.Rings.Select(r => r.Select(p => p - origin).ToList()).ToList(),
                Settings = settings,
            });
        }

        return drawing;
    }

    /// <summary>
    /// Receita equivalente a um desenho (para usar o desenho em outro contorno): destaques pela quantidade,
    /// faixas pela área ocupada e a forração de base pelo maior maciço em hachura.
    /// </summary>
    public static List<CompositionItem> DeriveRecipe(ViridianContext ctx, TemplateDrawing drawing)
    {
        var items = new List<CompositionItem>();
        var bandArea = new Dictionary<string, (Species Species, double Area)>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, TemplatePlant> g in drawing.Plants.GroupBy(p => p.SpeciesId, StringComparer.OrdinalIgnoreCase))
        {
            if (ctx.Lookup(g.Key) is not Species species)
            {
                continue;
            }

            CompositionRole role = CompositionWindow.SuggestRole(species);
            if (role == CompositionRole.Destaque)
            {
                CompositionItem item = CompositionItem.For(species, role);
                item.Count = g.Count();
                items.Add(item);
            }
            else
            {
                double s = species.EffectiveSpacingM;
                bandArea[species.Id] = (species, bandArea.GetValueOrDefault(species.Id).Area + g.Count() * s * s);
            }
        }

        TemplateMassing? baseMassing = drawing.Massings
            .Where(m => m.Settings.Mode == MassingMode.Hachura)
            .OrderByDescending(m => new Polygon2(m.Rings).Area)
            .FirstOrDefault();
        foreach (TemplateMassing m in drawing.Massings)
        {
            if (ctx.Lookup(m.SpeciesId) is not Species species)
            {
                continue;
            }

            if (ReferenceEquals(m, baseMassing))
            {
                items.Add(CompositionItem.For(species, CompositionRole.Base));
                continue;
            }

            bandArea[species.Id] = (species, bandArea.GetValueOrDefault(species.Id).Area + new Polygon2(m.Rings).Area);
        }

        double total = bandArea.Values.Sum(v => v.Area);
        foreach ((Species species, double area) in bandArea.Values.OrderByDescending(v => v.Species.AdultHeightM))
        {
            CompositionRole role = CompositionWindow.SuggestRole(species);
            CompositionItem item = CompositionItem.For(species, role is CompositionRole.Fundo or CompositionRole.Meio or CompositionRole.Frente ? role : CompositionRole.Meio);
            item.SharePercent = total > 0 ? Math.Round(area / total * 100, 1) : 0;
            items.Add(item);
        }

        return items;
    }
}

/// <summary>
/// Compõe um canteiro com várias espécies em uma única operação: destaques, faixas de fundo, meio e frente por
/// proporção da área e forração de base, a partir de uma receita montada na hora ou de um modelo do escritório.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ComposeBedCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ViewPlan view = ctx.RequirePlanView();
        RegionBoundary region = ctx.PickRegion("compor o canteiro");
        Point2? back = null;
        try
        {
            XYZ picked = ctx.UiDocument.Selection.PickPoint(
                ObjectSnapTypes.Endpoints | ObjectSnapTypes.Nearest | ObjectSnapTypes.Midpoints,
                "Clique no lado do FUNDO do canteiro (junto ao muro, à parede ou à divisa) — ESC para canteiro em ilha (alto no centro)");
            back = RevitUnits.ToPoint2(picked);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            // Canteiro em ilha.
        }

        var templates = new BedTemplateLibrary(ctx.Paths);
        var window = new CompositionWindow(ctx.Catalog, ctx.Paths, ctx.Settings, templates, region.Polygon, back, region.Source, ctx.ProjectUsage());
        if (!ctx.ShowDialog(window) || window.Plan is not CompositionPlan plan)
        {
            return Result.Cancelled;
        }

        FilledRegion? existing = region.ExistingRegion;
        bool baseOnMassing = plan.BaseSpecies is not null && existing is not null && MassingService.RecordOf(existing) is not null;
        var created = new List<ElementId>();
        string? baseId = null;
        ctx.Grouped("Viridian: compor canteiro", () =>
        {
            Level level = view.GenLevel;
            long seed = Random.Shared.NextInt64(1, int.MaxValue);
            foreach (PlannedGroup group in plan.Groups.Where(g => g.Points.Count > 0))
            {
                IReadOnlyList<FamilySymbol> symbols = ctx.EnsureVariants(group.Species, -1);
                IReadOnlyList<double> rotations = PlantDistributor.RandomRotations(group.Points.Count, seed + created.Count);
                var options = new PlacementOptions { Stage = PlantingService.DefaultStage(group.Species), Seed = seed + created.Count };
                created.AddRange(ctx.Planting.Place(ctx.Doc, level, symbols, group.Species, ctx.ProjectCode(group.Species), group.Points, rotations, options));
                ctx.RememberSpecies(group.Species);
            }

            if (plan.BaseSpecies is Species baseSpecies && !baseOnMassing)
            {
                MassingExecution execution = MassingCommands.BuildExecution(ctx, baseSpecies, BedFlows.NewMassingRecord(ctx, baseSpecies));
                MassingOutcome outcome = ctx.Massings.Create(ctx.Doc, view, region.Polygon, existing, execution);
                created.Add(outcome.Region.Id);
                baseId = outcome.Record.Id;
            }

            return created.Count;
        }, n => n > 0);

        ctx.Select(created);
        var lines = plan.Groups.Where(g => g.Points.Count > 0)
            .Select(g => $"{CompositionWindow.RoleName(g.Item.Role)}: {g.Species.DisplayName} — {g.Points.Count} un.")
            .ToList();
        if (baseId is not null)
        {
            lines.Add($"Base: {plan.BaseSpecies!.DisplayName} — maciço {baseId} em {plan.AreaM2:0.0} m²");
        }
        else if (baseOnMassing)
        {
            lines.Add("A região escolhida já é um maciço: a forração de base não foi criada.");
        }

        ShowInfo($"Canteiro composto: {plan.PlantCount} exemplar(es)" + (window.TemplateName.Length > 0 ? $" (modelo {window.TemplateName})" : string.Empty),
            string.Join("\n", lines) + "\n\nUm único Desfazer (Ctrl+Z) volta toda a composição.");
        return Result.Succeeded;
    }
}

/// <summary>Grava um canteiro pronto (exemplares, maciços e contorno) como modelo do escritório, para reusar em outros projetos.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SaveBedTemplateCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ViewPlan view = ctx.RequirePlanView();
        RegionBoundary region = ctx.PickRegion("salvar o canteiro como modelo");
        Point2 origin = region.Polygon.Bounds.Center;
        TemplateDrawing drawing = BedFlows.Capture(ctx, view, region.Polygon, origin);
        if (drawing.IsEmpty)
        {
            throw new ViridianException("Não há exemplares nem maciços do catálogo dentro desse contorno.");
        }

        var templates = new BedTemplateLibrary(ctx.Paths);
        string project = ProjectReader.ProjectTitle(ctx.Doc);
        var prompt = new TextPromptWindow("Salvar canteiro como modelo", "Nome do modelo", string.Empty,
            $"{drawing.Plants.Count} exemplar(es) e {drawing.Massings.Count} maciço(s). O modelo fica na biblioteca do escritório: "
            + "insira o desenho igual em outro projeto (Inserir Modelo) ou use-o como receita em outro contorno (Compor Canteiro).");
        if (!ctx.ShowDialog(prompt))
        {
            return Result.Cancelled;
        }

        var template = new BedTemplate
        {
            Name = prompt.Value,
            Description = prompt.Description,
            Source = project,
            Drawing = drawing,
            Items = BedFlows.DeriveRecipe(ctx, drawing),
        };
        try
        {
            templates.Save(template);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new ViridianException("Não foi possível gravar o modelo na biblioteca:\n" + ex.Message, ex);
        }

        ShowInfo($"Modelo \"{template.Name}\" gravado", template.Summary + "\n\n" + templates.File);
        return Result.Succeeded;
    }
}

/// <summary>Insere no projeto um desenho de canteiro do escritório, girado e posicionado por um clique.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class InsertBedTemplateCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ViewPlan view = ctx.RequirePlanView();
        var templates = new BedTemplateLibrary(ctx.Paths);
        var window = new BedTemplatesWindow(ctx.Catalog, ctx.Settings, templates);
        if (!ctx.ShowDialog(window) || window.Selected?.Drawing is not TemplateDrawing drawing)
        {
            return Result.Cancelled;
        }

        BedTemplate template = window.Selected;
        double angle = window.AngleDeg * Math.PI / 180;
        XYZ picked = ctx.UiDocument.Selection.PickPoint(
            ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections | ObjectSnapTypes.Nearest | ObjectSnapTypes.Midpoints | ObjectSnapTypes.Centers,
            $"Clique onde fica o centro do canteiro \"{template.Name}\"");
        Point2 origin = RevitUnits.ToPoint2(picked);
        Point2 Place(Point2 p) => TemplateDrawing.Place(p, origin, angle);

        var missing = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var created = new List<ElementId>();
        int massings = 0;
        ctx.Grouped("Viridian: inserir modelo " + template.Name, () =>
        {
            Level level = view.GenLevel;
            long seed = Random.Shared.NextInt64(1, int.MaxValue);
            foreach (IGrouping<string, TemplatePlant> g in drawing.Plants.GroupBy(p => p.SpeciesId, StringComparer.OrdinalIgnoreCase))
            {
                TemplatePlant first = g.First();
                Species? species = BedTemplateLibrary.Resolve(ctx.Catalog, first.SpeciesId, first.ScientificName);
                if (species is null)
                {
                    missing.Add(first.PopularName);
                    continue;
                }

                List<TemplatePlant> plants = g.ToList();
                IReadOnlyList<FamilySymbol> symbols = ctx.EnsureVariants(species, -1);
                var options = new PlacementOptions { Stage = PlantingService.DefaultStage(species), Seed = seed + created.Count };
                created.AddRange(ctx.Planting.Place(ctx.Doc, level, symbols, species, ctx.ProjectCode(species),
                    plants.Select(p => Place(new Point2(p.X, p.Y))).ToList(),
                    plants.Select(p => p.Rotation + angle).ToList(), options));
            }

            foreach (TemplateMassing m in drawing.Massings)
            {
                Species? species = BedTemplateLibrary.Resolve(ctx.Catalog, m.SpeciesId, m.ScientificName);
                if (species is null)
                {
                    missing.Add(m.PopularName);
                    continue;
                }

                var polygon = new Polygon2(m.Rings.Select(r => r.Select(Place)));
                MassingExecution execution = MassingCommands.BuildExecution(ctx, species, BedFlows.NewMassingRecord(ctx, species, m.Settings));
                created.Add(ctx.Massings.Create(ctx.Doc, view, polygon, null, execution).Region.Id);
                massings++;
            }

            // Contorno do canteiro como linhas de detalhe (quando não coincide com um maciço inserido).
            if (drawing.Outline.Count > 0 && !drawing.Massings.Any(m => MassingAdjust.SameBoundary(new Polygon2(m.Rings), new Polygon2(drawing.Outline))))
            {
                Tx.Run(ctx.Doc, "Viridian: contorno do canteiro", () =>
                {
                    double z = PlantingService.LevelOf(view)?.ProjectElevation ?? 0;
                    double tolerance = ctx.Doc.Application.ShortCurveTolerance;
                    foreach (List<Point2> ring in drawing.Outline)
                    {
                        for (int i = 0; i < ring.Count; i++)
                        {
                            XYZ a = RevitUnits.ToXyz(Place(ring[i]), z), b = RevitUnits.ToXyz(Place(ring[(i + 1) % ring.Count]), z);
                            if (a.DistanceTo(b) > tolerance)
                            {
                                created.Add(ctx.Doc.Create.NewDetailCurve(view, Line.CreateBound(a, b)).Id);
                            }
                        }
                    }
                });
            }

            return created.Count;
        }, n => n > 0);

        ctx.Select(created);
        ShowInfo($"Modelo \"{template.Name}\" inserido",
            $"{created.Count} elemento(s), {massings} maciço(s)." + (missing.Count == 0 ? string.Empty
                : "\n\nFora do catálogo deste computador (não inseridas): " + string.Join(", ", missing)));
        return Result.Succeeded;
    }
}
