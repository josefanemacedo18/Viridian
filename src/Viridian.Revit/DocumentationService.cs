using System.Globalization;
using Autodesk.Revit.DB;
using Viridian.Core.Detailing;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Geometry;
using Viridian.Core.Hatch;
using Viridian.Core.Massing;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Uma linha da legenda de vegetação.</summary>
public sealed class LegendEntry
{
    public string Code { get; set; } = string.Empty;

    public string PopularName { get; set; } = string.Empty;

    public string ScientificName { get; set; } = string.Empty;

    public string Quantity { get; set; } = string.Empty;

    /// <summary>Espécie do catálogo, usada para desenhar o símbolo (nula para famílias não cadastradas).</summary>
    public Species? Species { get; set; }

    /// <summary>Hachura a desenhar no lugar do símbolo (linhas de maciço).</summary>
    public HatchPattern? Hatch { get; set; }
}

/// <summary>Legendas e tabelas: documentação gerada com os recursos nativos do Revit.</summary>
public sealed class DocumentationService
{
    public const string LegendViewName = "Viridian - Legenda de vegetação";
    public const string SpeciesScheduleName = "Viridian - Vegetação por espécie";
    public const string PlantScheduleName = "Viridian - Exemplares de vegetação";

    private readonly ViridianSettings _settings;
    private readonly LabelService _labels;

    /// <summary>Localiza o arquivo da fotografia de uma espécie (vazio quando não há foto).</summary>
    public Func<string, string> PhotoResolver { get; set; } = _ => string.Empty;

    public DocumentationService(ViridianSettings settings, LabelService labels)
    {
        _settings = settings;
        _labels = labels;
    }

    /// <summary>Monta as linhas da legenda a partir dos quantitativos do projeto.</summary>
    public static IReadOnlyList<LegendEntry> BuildLegendEntries(
        IReadOnlyList<PlantRecord> plants,
        IReadOnlyList<MassingInfo> massings,
        Func<string, Species?> lookup,
        ViridianSettings settings)
    {
        var entries = new List<LegendEntry>();
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, PlantRecord> group in plants
                     .Where(p => p.IsRegistered && !p.BelongsToMassing && p.Status != PlantStatus.Removido)
                     .GroupBy(p => p.SpeciesId)
                     .OrderBy(g => g.First().SpeciesCode, StringComparer.OrdinalIgnoreCase))
        {
            PlantRecord first = group.First();
            seen.Add(group.Key);
            entries.Add(new LegendEntry
            {
                Code = first.SpeciesCode,
                PopularName = first.PopularName,
                ScientificName = first.ScientificName,
                Quantity = group.Count().ToString("N0", culture) + " un",
                Species = lookup(group.Key),
            });
        }

        foreach (IGrouping<string, MassingInfo> group in ReportBuilder.DistinctMassings(massings)
                     .GroupBy(m => string.IsNullOrWhiteSpace(m.Record.SpeciesId) ? m.Record.SpeciesCode : m.Record.SpeciesId)
                     .OrderBy(g => g.First().Record.SpeciesCode, StringComparer.OrdinalIgnoreCase))
        {
            MassingRecord first = group.First().Record;
            double area = group.Sum(m => m.Record.AreaM2);
            int quantity = group.Sum(m => Math.Max(0, m.Record.Quantity));
            string amount = SupplyUnits.IsAreaBased(first.SupplyUnit)
                ? area.ToString("N2", culture) + " m²"
                : quantity.ToString("N0", culture) + " " + (string.IsNullOrWhiteSpace(first.SupplyUnit) ? "un" : first.SupplyUnit) + " · " + area.ToString("N2", culture) + " m²";

            HatchPattern? hatch = group
                .Select(m => HatchLibrary.Find(m.Record.HatchPatternKey, settings.CustomHatches))
                .FirstOrDefault(h => h is not null);
            entries.Add(new LegendEntry
            {
                Code = first.SpeciesCode,
                PopularName = first.PopularName,
                ScientificName = first.ScientificName,
                Quantity = amount,
                Species = lookup(first.SpeciesId),
                Hatch = hatch,
            });
        }

        return entries;
    }

    /// <summary>
    /// Cria (ou refaz) a vista de desenho com a legenda de vegetação: símbolo ou hachura, código, nomes e quantidade.
    /// </summary>
    public ViewDrafting CreateLegend(Document document, IReadOnlyList<LegendEntry> entries, string projectTitle)
    {
        if (entries.Count == 0)
        {
            throw new ViridianException("Não há vegetação no projeto para compor a legenda.");
        }

        return Tx.Run(document, "Viridian: legenda", () =>
        {
            ViewDrafting view = FindOrCreateDraftingView(document, LegendViewName);
            ClearView(document, view);
            const int scale = 100;
            if (view.Scale != scale)
            {
                view.Scale = scale;
            }

            // Linhas agrupadas por categoria (maciços à parte), na ordem das categorias.
            var photos = entries.ToDictionary(e => e, e => e.Species is null ? string.Empty : PhotoResolver(e.Species.PhotoFile));
            bool withPhotos = photos.Values.Any(p => p.Length > 0);
            List<LegendEntry> ordered = entries
                .Select((e, i) => (Entry: e, Index: i))
                .OrderBy(x => GroupOrder(x.Entry))
                .ThenBy(x => x.Index)
                .Select(x => x.Entry)
                .ToList();
            List<LegendRow> rows = ordered.Select(e => new LegendRow(e.Code, e.PopularName, e.ScientificName, e.Quantity, GroupName(e))).ToList();
            double unit = scale / 1000.0;
            string title = "Legenda de vegetação" + (string.IsNullOrWhiteSpace(projectTitle) ? string.Empty : " — " + projectTitle);
            LegendLayoutResult layout = LegendLayout.Build(rows, withPhotos, title, unit);
            new DrawingRenderer(document, view, _settings.Labels.TextSizeMm).Render(layout.Drawing, Point2.Zero);

            // Símbolos, hachuras e fotos nativos, por cima, nas posições da diagramação.
            foreach (LegendSlot slot in layout.Slots)
            {
                LegendEntry entry = ordered[slot.Row];
                Bounds2 b = slot.Symbol;
                if (entry.Hatch is not null)
                {
                    RegionPaint? paint = entry.Species is null ? null : VegetationPalette.Massing(entry.Species, _settings.Colors);
                    DrawHatchSample(document, view, entry.Hatch, paint, RevitUnits.ToFeet(b.MinX), RevitUnits.ToFeet(b.MinY), RevitUnits.ToFeet(b.MaxX), RevitUnits.ToFeet(b.MaxY));
                }
                else if (entry.Species is not null)
                {
                    DrawSymbol(document, view, entry.Species, SymbolPaint.For(entry.Species, _settings.Colors),
                        RevitUnits.ToFeet(b.Center.X), RevitUnits.ToFeet(b.Center.Y), RevitUnits.ToFeet(Math.Min(b.Width, b.Height)));
                }

                if (slot.Photo is Bounds2 p && photos[entry].Length > 0 && PhotoService.EnsureImageType(document, photos[entry]) is ImageType photo)
                {
                    PhotoService.PlaceImage(document, view, photo, new XYZ(RevitUnits.ToFeet(p.MinX), RevitUnits.ToFeet(p.MaxY), 0), RevitUnits.ToFeet(p.Width), RevitUnits.ToFeet(p.Height));
                }
            }

            return view;
        }, suppressWarnings: true);
    }

    private static int GroupOrder(LegendEntry e) => e.Hatch is not null ? 100 : e.Species is null ? 200 : (int)e.Species.Category;

    private static string GroupName(LegendEntry e) => e.Hatch is not null ? "Maciços" : e.Species is null ? "Outros" : e.Species.Category switch
    {
        PlantCategory.Arvore => "Árvores",
        PlantCategory.Palmeira => "Palmeiras",
        PlantCategory.Arbusto => "Arbustos",
        PlantCategory.Herbacea => "Herbáceas",
        PlantCategory.Forracao => "Forrações",
        PlantCategory.Gramado => "Gramados",
        PlantCategory.Trepadeira => "Trepadeiras",
        PlantCategory.Aquatica => "Aquáticas",
        _ => "Outros",
    };

    /// <summary>Cria a tabela nativa de vegetação agrupada por espécie (ou devolve a existente).</summary>
    public ViewSchedule CreateSpeciesSchedule(Document document)
    {
        return Tx.Run(document, "Viridian: tabela por espécie", () =>
        {
            SharedParameterService.EnsureBound(document);
            ViewSchedule? existing = FindSchedule(document, SpeciesScheduleName);
            if (existing is not null)
            {
                return existing;
            }

            ViewSchedule schedule = ViewSchedule.CreateSchedule(document, new ElementId(BuiltInCategory.OST_Planting));
            schedule.Name = SpeciesScheduleName;
            ScheduleDefinition definition = schedule.Definition;

            ScheduleField? code = AddParameterField(document, definition, VirParams.SpeciesCode, "Código");
            AddParameterField(document, definition, VirParams.PopularName, "Nome popular");
            AddParameterField(document, definition, VirParams.ScientificName, "Nome científico");
            AddParameterField(document, definition, VirParams.Category, "Categoria");
            AddParameterField(document, definition, VirParams.SupplyUnit, "Unidade");
            ScheduleField count = definition.AddField(ScheduleFieldType.Count);
            count.ColumnHeading = "Quantidade";

            if (code is not null)
            {
                definition.AddSortGroupField(new ScheduleSortGroupField(code.FieldId));
            }

            definition.IsItemized = false;
            definition.ShowGrandTotal = true;
            definition.ShowGrandTotalCount = true;
            return schedule;
        });
    }

    /// <summary>Cria a tabela nativa de exemplares, um por linha (ou devolve a existente).</summary>
    public ViewSchedule CreatePlantSchedule(Document document)
    {
        return Tx.Run(document, "Viridian: tabela de exemplares", () =>
        {
            SharedParameterService.EnsureBound(document);
            ViewSchedule? existing = FindSchedule(document, PlantScheduleName);
            if (existing is not null)
            {
                return existing;
            }

            ViewSchedule schedule = ViewSchedule.CreateSchedule(document, new ElementId(BuiltInCategory.OST_Planting));
            schedule.Name = PlantScheduleName;
            ScheduleDefinition definition = schedule.Definition;

            ScheduleField? projectCode = AddParameterField(document, definition, VirParams.ProjectCode, "Código do exemplar");
            AddParameterField(document, definition, VirParams.SpeciesCode, "Espécie");
            AddParameterField(document, definition, VirParams.PopularName, "Nome popular");
            AddParameterField(document, definition, VirParams.Status, "Situação");
            AddParameterField(document, definition, VirParams.Stage, "Estágio de implantação");
            AddParameterField(document, definition, VirParams.MassingId, "Maciço");
            AddParameterField(document, definition, VirParams.Notes, "Observações");

            if (projectCode is not null)
            {
                definition.AddSortGroupField(new ScheduleSortGroupField(projectCode.FieldId));
            }

            definition.IsItemized = true;
            return schedule;
        });
    }

    // ------------------------------------------------------------------ internos

    private static ScheduleField? AddParameterField(Document document, ScheduleDefinition definition, ParamDef def, string heading)
    {
        SharedParameterElement? parameter = SharedParameterElement.Lookup(document, def.Guid);
        if (parameter is null)
        {
            return null;
        }

        SchedulableField? schedulable = definition.GetSchedulableFields().FirstOrDefault(f => f.ParameterId == parameter.Id);
        if (schedulable is null)
        {
            ViridianLog.Warn("O parâmetro " + def.Name + " não está disponível para tabelas de vegetação.");
            return null;
        }

        ScheduleField field = definition.AddField(schedulable);
        field.ColumnHeading = heading;
        return field;
    }

    private static ViewSchedule? FindSchedule(Document document, string name)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
            .FirstOrDefault(s => !s.IsTemplate && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static ViewDrafting FindOrCreateDraftingView(Document document, string name)
    {
        using (var views = new FilteredElementCollector(document))
        {
            ViewDrafting? existing = views.OfClass(typeof(ViewDrafting)).Cast<ViewDrafting>()
                .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }
        }

        using var types = new FilteredElementCollector(document);
        ViewFamilyType type = types.OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                                  .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting)
                              ?? throw new ViridianException("O projeto não possui tipo de vista de desenho para criar a legenda.");
        ViewDrafting view = ViewDrafting.Create(document, type.Id);
        for (int attempt = 1; attempt <= 50; attempt++)
        {
            try
            {
                view.Name = attempt == 1 ? name : $"{name} ({attempt})";
                break;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Nome já usado por outra vista: tenta o próximo.
            }
        }

        return view;
    }

    /// <summary>Remove o conteúdo gerado anteriormente na vista da legenda.</summary>
    public static void ClearView(Document document, View view)
    {
        using var collector = new FilteredElementCollector(document, view.Id);
        List<ElementId> ids = collector
            .WhereElementIsNotElementType()
            .Where(e => e is DetailCurve or TextNote or FilledRegion or ImageInstance)
            .Select(e => e.Id)
            .ToList();
        if (ids.Count > 0)
        {
            document.Delete(ids);
        }
    }

    private static void AddText(Document document, View view, ElementId textType, string text, double x, double y)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        TextNote.Create(document, view.Id, new XYZ(x, y, 0), LabelService.ToRevitText(text), textType);
    }

    private static void AddLine(Document document, View view, double x1, double y1, double x2, double y2)
    {
        var a = new XYZ(x1, y1, 0);
        var b = new XYZ(x2, y2, 0);
        if (a.DistanceTo(b) > document.Application.ShortCurveTolerance * 2)
        {
            document.Create.NewDetailCurve(view, Line.CreateBound(a, b));
        }
    }

    /// <summary>Desenha o símbolo da espécie reduzido para caber na célula da legenda.</summary>
    private static void DrawSymbol(Document document, View view, Species species, SymbolPaint paint, double centerX, double centerY, double sizeFeet)
    {
        PlantSymbol symbol = SymbolGenerator.Generate(species.SymbolStyle, species.CrownDiameterM, species.EffectiveSymbolSeed);
        double factor = sizeFeet / RevitUnits.ToFeet(symbol.Diameter);
        double tolerance = document.Application.ShortCurveTolerance * 1.5;

        XYZ Map(Point2 p) => new(centerX + RevitUnits.ToFeet(p.X) * factor, centerY + RevitUnits.ToFeet(p.Y) * factor, 0);

        // Cores primeiro (ficam por baixo dos traços), como na planta.
        DrawSymbolColors(document, view, symbol, paint, Map, factor, tolerance);

        foreach (SymbolPrimitive primitive in symbol.ForProfile(DetailProfile.Intermediario))
        {
            try
            {
                Curve? curve = null;
                switch (primitive)
                {
                    case SymbolLine line:
                    {
                        XYZ a = Map(line.A);
                        XYZ b = Map(line.B);
                        if (a.DistanceTo(b) >= tolerance)
                        {
                            curve = Line.CreateBound(a, b);
                        }

                        break;
                    }

                    case SymbolArc arc:
                    {
                        XYZ start = Map(arc.Start);
                        XYZ mid = Map(arc.Mid);
                        XYZ end = Map(arc.End);
                        if (start.DistanceTo(end) >= tolerance && start.DistanceTo(mid) >= tolerance && mid.DistanceTo(end) >= tolerance)
                        {
                            curve = Arc.Create(start, end, mid);
                        }

                        break;
                    }
                }

                if (curve is not null)
                {
                    document.Create.NewDetailCurve(view, curve);
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Um traço miúdo demais na escala da legenda é simplesmente omitido.
            }
        }
    }

    private static void DrawSymbolColors(Document document, View view, PlantSymbol symbol, SymbolPaint paint, Func<Point2, XYZ> map, double factor, double tolerance)
    {
        try
        {
            if (paint.Mode != CrownFillMode.Nenhum && symbol.FillOutline.Count >= 3)
            {
                FilledRegionType? crownType = paint.Mode == CrownFillMode.Solido
                    ? HatchService.EnsureSolidRegionType(document, paint.Crown)
                    : HatchService.EnsureCrownHatchType(document, paint.Crown);
                CurveLoop? loop = PolygonLoop(symbol.FillOutline.Select(map).ToList(), tolerance);
                if (crownType is not null && loop is not null)
                {
                    CreateBorderless(document, view, crownType, new List<CurveLoop> { loop });
                }
            }

            if (paint.Flower is Rgb flower && symbol.FlowerSpots.Count > 0)
            {
                FilledRegionType? flowerType = HatchService.EnsureSolidRegionType(document, flower);
                if (flowerType is not null)
                {
                    foreach (FlowerSpot spot in symbol.FlowerSpots)
                    {
                        double radius = RevitUnits.ToFeet(spot.Radius) * factor;
                        if (radius * Math.PI < tolerance * 2)
                        {
                            continue;
                        }

                        XYZ center = map(spot.Center);
                        var loop = new CurveLoop();
                        loop.Append(Arc.Create(center, radius, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
                        loop.Append(Arc.Create(center, radius, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
                        CreateBorderless(document, view, flowerType, new List<CurveLoop> { loop });
                    }
                }
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Não foi possível colorir o símbolo da legenda.", ex);
        }
    }

    private static void CreateBorderless(Document document, View view, FilledRegionType type, IList<CurveLoop> loops)
    {
        FilledRegion region = FilledRegion.Create(document, type.Id, view.Id, loops);
        ElementId? invisible = HatchService.InvisibleLineStyle(document);
        if (invisible is not null)
        {
            region.SetLineStyleId(invisible);
        }
    }

    private static CurveLoop? PolygonLoop(IReadOnlyList<XYZ> raw, double tolerance)
    {
        var points = new List<XYZ>();
        foreach (XYZ p in raw)
        {
            if (points.Count == 0 || points[^1].DistanceTo(p) >= tolerance)
            {
                points.Add(p);
            }
        }

        while (points.Count > 2 && points[^1].DistanceTo(points[0]) < tolerance)
        {
            points.RemoveAt(points.Count - 1);
        }

        if (points.Count < 3)
        {
            return null;
        }

        var curves = new List<Curve>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            curves.Add(Line.CreateBound(points[i], points[(i + 1) % points.Count]));
        }

        return CurveLoop.Create(curves);
    }

    private static void DrawHatchSample(Document document, View view, HatchPattern hatch, RegionPaint? paint, double x1, double y1, double x2, double y2)
    {
        try
        {
            FilledRegionType type = HatchService.EnsureRegionType(document, hatch, paint);
            var corners = new[] { new XYZ(x1, y1, 0), new XYZ(x2, y1, 0), new XYZ(x2, y2, 0), new XYZ(x1, y2, 0) };
            var curves = new List<Curve>();
            for (int i = 0; i < corners.Length; i++)
            {
                curves.Add(Line.CreateBound(corners[i], corners[(i + 1) % corners.Length]));
            }

            FilledRegion.Create(document, type.Id, view.Id, new List<CurveLoop> { CurveLoop.Create(curves) });
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ViridianException)
        {
            ViridianLog.Warn("Não foi possível desenhar a amostra da hachura " + hatch.Name, ex);
        }
    }
}
