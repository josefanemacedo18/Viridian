using System.Globalization;
using Autodesk.Revit.DB;
using Viridian.Core.Config;
using Viridian.Core.Geometry;
using Viridian.Core.Identification;
using Viridian.Core.Integrity;
using Viridian.Core.Massing;
using Viridian.Library;

namespace Viridian.Revit;

public sealed class LabelRequest
{
    public LabelModel Model { get; set; } = LabelModel.CodigoSimples;

    public bool Leader { get; set; }

    /// <summary>Uma etiqueta por espécie, com a quantidade de exemplares (padrão em plantas de plantio).</summary>
    public bool GroupBySpecies { get; set; }

    /// <summary>Tipo de identificador nativo (tag) a usar no lugar da etiqueta de texto. Inválido = etiqueta de texto.</summary>
    public ElementId NativeTagTypeId { get; set; } = ElementId.InvalidElementId;
}

public sealed class LabelUpdateResult
{
    public int Updated { get; set; }

    public int Unchanged { get; set; }

    public int Orphans { get; set; }
}

/// <summary>
/// Identificação gráfica de exemplares e maciços. As etiquetas de texto ficam vinculadas aos elementos
/// e são atualizadas a partir dos dados do modelo; exemplares também aceitam identificadores nativos.
/// </summary>
public sealed class LabelService
{
    private readonly ViridianSettings _settings;

    public LabelService(ViridianSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Tipos de identificador (tag) de vegetação carregados no projeto.</summary>
    public static IReadOnlyList<FamilySymbol> PlantingTagTypes(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_PlantingTags)
            .Cast<FamilySymbol>()
            .OrderBy(s => s.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Etiqueta exemplares na vista. Retorna a quantidade de etiquetas criadas.</summary>
    public int LabelPlants(Document document, View view, IReadOnlyList<FamilyInstance> plants, LabelRequest request)
    {
        if (plants.Count == 0)
        {
            return 0;
        }

        return Tx.Run(document, "Viridian: etiquetar", () =>
        {
            if (request.NativeTagTypeId != ElementId.InvalidElementId)
            {
                return CreateNativeTags(document, view, plants, request);
            }

            ElementId textType = EnsureTextType(document).Id;
            ILookup<string, LabelEntry> existing = ExistingLabels(document, view);
            int created = 0;

            IEnumerable<List<FamilyInstance>> groups = request.GroupBySpecies
                ? plants.GroupBy(p => p.GetTypeId()).Select(g => g.ToList())
                : plants.Select(p => new List<FamilyInstance> { p });

            foreach (List<FamilyInstance> group in groups)
            {
                FamilyInstance anchor = AnchorOf(group);
                FamilySymbol symbol = anchor.Symbol;
                string text = LabelText.ForPlants(
                    request.Model,
                    VirParams.GetString(symbol, VirParams.SpeciesCode),
                    VirParams.GetString(symbol, VirParams.PopularName),
                    VirParams.GetString(symbol, VirParams.ScientificName),
                    group.Count,
                    VirParams.GetString(anchor, VirParams.ProjectCode));

                var link = new LabelLink
                {
                    TargetUniqueIds = group.Select(p => p.UniqueId).ToList(),
                    Model = (int)request.Model,
                    Grouped = request.GroupBySpecies,
                };

                RemoveEquivalent(document, existing, link);
                Point2 location = PlantingService.LocationOf(anchor);
                double radius = Math.Max(0.05, VirParams.GetMeters(symbol, VirParams.CrownDiameter) / 2);
                CreateNote(document, view, textType, text, location, radius, request.Leader || LabelText.ForcesLeader(request.Model), link);
                created++;
            }

            return created;
        });
    }

    /// <summary>Etiqueta maciços na vista. Retorna a quantidade de etiquetas criadas.</summary>
    public int LabelMassings(Document document, View view, IReadOnlyList<FilledRegion> regions, LabelRequest request)
    {
        if (regions.Count == 0)
        {
            return 0;
        }

        return Tx.Run(document, "Viridian: etiquetar maciços", () =>
        {
            ElementId textType = EnsureTextType(document).Id;
            ILookup<string, LabelEntry> existing = ExistingLabels(document, view);
            int created = 0;
            foreach (FilledRegion region in regions)
            {
                MassingRecord? record = MassingService.RecordOf(region);
                if (record is null)
                {
                    continue;
                }

                Polygon2 polygon = BoundaryReader.FromLoops(region.GetBoundaries());
                if (polygon.IsEmpty)
                {
                    continue;
                }

                var link = new LabelLink { TargetUniqueIds = { region.UniqueId }, Model = (int)request.Model };
                RemoveEquivalent(document, existing, link);
                bool leader = request.Leader || LabelText.ForcesLeader(request.Model);

                // Com linha indicadora a etiqueta fica fora do maciço; sem ela, no interior.
                Point2 interior = polygon.InteriorPoint();
                if (leader)
                {
                    double reach = Math.Max(polygon.Bounds.Width, polygon.Bounds.Height) / 2;
                    CreateNote(document, view, textType, LabelText.ForMassing(request.Model, record), interior, reach, true, link);
                }
                else
                {
                    CreateNote(document, view, textType, LabelText.ForMassing(request.Model, record), interior, 0, false, link, centered: true);
                }

                created++;
            }

            return created;
        });
    }

    /// <summary>Atualiza o texto de todas as etiquetas vinculadas do projeto.</summary>
    public LabelUpdateResult UpdateAll(Document document)
    {
        var result = new LabelUpdateResult();
        var changes = new List<(TextNote Note, string Text)>();
        foreach (Element element in ElementData.ElementsOfKind(document, ElementData.KindLabel))
        {
            if (element is not TextNote note || !ElementData.TryRead(note, ElementData.KindLabel, out LabelLink link))
            {
                continue;
            }

            string? text = ComposeText(document, link);
            if (text is null)
            {
                result.Orphans++;
            }
            else if (!string.Equals(Normalize(note.Text), Normalize(text), StringComparison.Ordinal))
            {
                changes.Add((note, text));
            }
            else
            {
                result.Unchanged++;
            }
        }

        if (changes.Count > 0)
        {
            Tx.Run(document, "Viridian: atualizar etiquetas", () =>
            {
                foreach ((TextNote note, string text) in changes)
                {
                    note.Text = ToRevitText(text);
                    result.Updated++;
                }
            });
        }

        return result;
    }

    /// <summary>Etiquetas cujo elemento identificado não existe mais.</summary>
    public static IReadOnlyList<OrphanLabel> FindOrphans(Document document)
    {
        var result = new List<OrphanLabel>();
        foreach (Element element in ElementData.ElementsOfKind(document, ElementData.KindLabel))
        {
            if (element is TextNote note
                && ElementData.TryRead(note, ElementData.KindLabel, out LabelLink link)
                && !link.TargetUniqueIds.Any(id => document.GetElement(id) is not null))
            {
                result.Add(new OrphanLabel { ElementId = note.Id.Value, Text = note.Text ?? string.Empty });
            }
        }

        return result;
    }

    /// <summary>Exclui as etiquetas órfãs. Retorna a quantidade excluída.</summary>
    public static int DeleteOrphans(Document document)
    {
        List<ElementId> ids = FindOrphans(document).Select(o => new ElementId(o.ElementId)).ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        Tx.Run(document, "Viridian: excluir etiquetas órfãs", () => document.Delete(ids));
        return ids.Count;
    }

    /// <summary>Tipo de texto das etiquetas, com a altura configurada. Precisa de transação quando ainda não existe.</summary>
    public TextNoteType EnsureTextType(Document document)
    {
        string name = "VIR Etiqueta " + _settings.Labels.TextSizeMm.ToString("0.0", CultureInfo.InvariantCulture) + " mm";
        using var collector = new FilteredElementCollector(document);
        List<TextNoteType> types = collector.OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
        TextNoteType? existing = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        TextNoteType? source = document.GetElement(document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)) as TextNoteType ?? types.FirstOrDefault();
        if (source is null)
        {
            throw new ViridianException("O projeto não possui nenhum tipo de texto para servir de base às etiquetas.");
        }

        if (source.Duplicate(name) is not TextNoteType created)
        {
            return source;
        }

        SetDouble(created, BuiltInParameter.TEXT_SIZE, RevitUnits.MmToFeet(_settings.Labels.TextSizeMm));
        SetInt(created, BuiltInParameter.TEXT_BACKGROUND, 1); // fundo transparente
        SetInt(created, BuiltInParameter.TEXT_BOX_VISIBILITY, 0);
        return created;
    }

    // ------------------------------------------------------------------ internos

    private sealed record LabelEntry(TextNote Note, LabelLink Link);

    private static string KeyOf(LabelLink link) =>
        link.Model.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(",", link.TargetUniqueIds.OrderBy(x => x, StringComparer.Ordinal));

    private static ILookup<string, LabelEntry> ExistingLabels(Document document, View view)
    {
        var entries = new List<LabelEntry>();
        foreach (Element element in ElementData.ElementsOfKind(document, ElementData.KindLabel))
        {
            if (element is TextNote note && note.OwnerViewId == view.Id && ElementData.TryRead(note, ElementData.KindLabel, out LabelLink link))
            {
                entries.Add(new LabelEntry(note, link));
            }
        }

        return entries.ToLookup(e => KeyOf(e.Link), StringComparer.Ordinal);
    }

    /// <summary>Evita etiquetas repetidas: remove a etiqueta anterior do mesmo modelo para os mesmos elementos.</summary>
    private static void RemoveEquivalent(Document document, ILookup<string, LabelEntry> existing, LabelLink link)
    {
        foreach (LabelEntry entry in existing[KeyOf(link)])
        {
            if (entry.Note.IsValidObject)
            {
                document.Delete(entry.Note.Id);
            }
        }
    }

    private static FamilyInstance AnchorOf(List<FamilyInstance> group)
    {
        if (group.Count == 1)
        {
            return group[0];
        }

        // O exemplar mais próximo do centro do grupo recebe a etiqueta.
        List<Point2> points = group.Select(PlantingService.LocationOf).ToList();
        var center = new Point2(points.Average(p => p.X), points.Average(p => p.Y));
        int best = 0;
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].DistanceTo(center) < points[best].DistanceTo(center))
            {
                best = i;
            }
        }

        return group[best];
    }

    private void CreateNote(
        Document document,
        View view,
        ElementId textType,
        string text,
        Point2 target,
        double clearanceM,
        bool leader,
        LabelLink link,
        bool centered = false)
    {
        double z = PlantingService.LevelOf(view)?.ProjectElevation ?? 0;
        double scale = Math.Max(1, view.Scale);
        double offsetM = clearanceM * 0.75 + _settings.Labels.OffsetMm * scale / 1000.0;
        Point2 position = centered ? target : target + new Point2(offsetM, offsetM);

        using var options = new TextNoteOptions(textType)
        {
            HorizontalAlignment = centered ? HorizontalTextAlignment.Center : HorizontalTextAlignment.Left,
        };
        TextNote note = TextNote.Create(document, view.Id, RevitUnits.ToXyz(position, z), ToRevitText(text), options);
        ElementData.Write(note, ElementData.KindLabel, link);

        if (!leader || centered)
        {
            return;
        }

        try
        {
            Leader created = note.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L);
            document.Regenerate();
            XYZ end = RevitUnits.ToXyz(target, created.End.Z);
            if (end.DistanceTo(created.Anchor) > 1e-6 && end.DistanceTo(created.Elbow) > 1e-6)
            {
                created.End = end;
            }
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or InvalidOperationException)
        {
            // A etiqueta permanece válida sem a linha indicadora.
            ViridianLog.Warn("Não foi possível posicionar a linha indicadora da etiqueta.", ex);
        }
    }

    private int CreateNativeTags(Document document, View view, IReadOnlyList<FamilyInstance> plants, LabelRequest request)
    {
        if (document.GetElement(request.NativeTagTypeId) is not FamilySymbol tagType)
        {
            throw new ViridianException("O tipo de identificador escolhido não existe mais no projeto.");
        }

        if (!tagType.IsActive)
        {
            tagType.Activate();
            document.Regenerate();
        }

        double z = PlantingService.LevelOf(view)?.ProjectElevation ?? 0;
        double scale = Math.Max(1, view.Scale);
        int created = 0;
        foreach (FamilyInstance plant in plants)
        {
            Point2 location = PlantingService.LocationOf(plant);
            double radius = Math.Max(0.05, VirParams.GetMeters(plant.Symbol, VirParams.CrownDiameter) / 2);
            double offset = radius * 0.75 + _settings.Labels.OffsetMm * scale / 1000.0;
            XYZ head = RevitUnits.ToXyz(location + new Point2(offset, offset), z);
            try
            {
                IndependentTag.Create(document, tagType.Id, view.Id, new Reference(plant), request.Leader, TagOrientation.Horizontal, head);
                created++;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                ViridianLog.Warn("Não foi possível criar o identificador do exemplar " + plant.Id, ex);
            }
        }

        return created;
    }

    /// <summary>Texto atual de uma etiqueta, ou null quando nenhum dos elementos identificados existe mais.</summary>
    private static string? ComposeText(Document document, LabelLink link)
    {
        List<Element> targets = link.TargetUniqueIds
            .Select(id => document.GetElement(id))
            .Where(e => e is not null)
            .ToList()!;
        if (targets.Count == 0)
        {
            return null;
        }

        LabelModel model = Enum.IsDefined((LabelModel)link.Model) ? (LabelModel)link.Model : LabelModel.CodigoSimples;
        if (targets[0] is FilledRegion region)
        {
            MassingRecord? record = MassingService.RecordOf(region);
            return record is null ? null : LabelText.ForMassing(model, record);
        }

        List<FamilyInstance> plants = targets.OfType<FamilyInstance>().ToList();
        if (plants.Count == 0)
        {
            return null;
        }

        FamilyInstance anchor = plants[0];
        FamilySymbol symbol = anchor.Symbol;
        return LabelText.ForPlants(
            model,
            VirParams.GetString(symbol, VirParams.SpeciesCode),
            VirParams.GetString(symbol, VirParams.PopularName),
            VirParams.GetString(symbol, VirParams.ScientificName),
            link.Grouped ? plants.Count : 1,
            VirParams.GetString(anchor, VirParams.ProjectCode));
    }

    /// <summary>O Revit usa retorno de carro como quebra de parágrafo em notas de texto.</summary>
    public static string ToRevitText(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\n', '\r');

    private static string Normalize(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    private static void SetDouble(Element element, BuiltInParameter builtIn, double value)
    {
        Parameter? parameter = element.get_Parameter(builtIn);
        if (parameter is not null && !parameter.IsReadOnly && parameter.StorageType == StorageType.Double)
        {
            parameter.Set(value);
        }
    }

    private static void SetInt(Element element, BuiltInParameter builtIn, int value)
    {
        Parameter? parameter = element.get_Parameter(builtIn);
        if (parameter is not null && !parameter.IsReadOnly && parameter.StorageType == StorageType.Integer)
        {
            parameter.Set(value);
        }
    }
}
