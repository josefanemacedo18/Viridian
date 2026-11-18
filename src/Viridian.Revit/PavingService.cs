using Autodesk.Revit.DB;
using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Hatch;
using Viridian.Core.Paving;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Vínculo entre um tipo de piso do Revit e o tipo da biblioteca Viridian.</summary>
public sealed class PavingLink
{
    public string PavingId { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;
}

/// <summary>
/// Leva os pisos da biblioteca para o Revit: hachura de modelo (escala real), material com a cor e a paginação,
/// tipo de piso com as camadas (revestimento como acabamento e as camadas de base), tipo de região preenchida para
/// plantas e detalhe em corte. Refazer um tipo atualiza os pisos que já o usam.
/// </summary>
public static class PavingService
{
    public const string KindPaving = "Paving";
    public const string TypePrefix = "VIR Piso - ";
    private const string LayerMaterialPrefix = "VIR Camada - ";

    /// <summary>Material do revestimento com a cor e a paginação na superfície. Precisa de transação.</summary>
    public static ElementId EnsureMaterial(Document document, PavingType type)
    {
        HatchPattern pattern = Pattern(type);
        ElementId patternId = HatchService.EnsureFillPattern(document, pattern);
        string name = SafeName(TypePrefix + type.Name);
        Material material = FindMaterial(document, name) ?? (Material)document.GetElement(Material.Create(document, name));
        material.Color = HatchService.ToColor(type.SurfaceRgb);
        material.UseRenderAppearanceForShading = false;
        material.SurfaceForegroundPatternId = patternId;
        material.SurfaceForegroundPatternColor = HatchService.ToColor(type.JointRgb);
        material.CutForegroundPatternId = ElementId.InvalidElementId;
        return material.Id;
    }

    /// <summary>Tipo de piso com as camadas do tipo da biblioteca. Precisa de transação.</summary>
    public static FloorType EnsureFloorType(Document document, PavingType type)
    {
        string name = SafeName(TypePrefix + type.Name);
        using var collector = new FilteredElementCollector(document);
        List<FloorType> types = collector.OfClass(typeof(FloorType)).Cast<FloorType>().ToList();
        FloorType? floorType = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (floorType is null)
        {
            FloorType template = types.FirstOrDefault(t => !t.IsFoundationSlab && t.GetCompoundStructure() is not null)
                                 ?? types.FirstOrDefault()
                                 ?? throw new ViridianException("O projeto não tem nenhum tipo de piso para servir de base.");
            floorType = template.Duplicate(name) as FloorType
                        ?? throw new ViridianException("Não foi possível criar o tipo de piso \"" + name + "\".");
        }

        ElementId surface = EnsureMaterial(document, type);
        var layers = new List<CompoundStructureLayer>
        {
            new(RevitUnits.MmToFeet(type.ThicknessMm), MaterialFunctionAssignment.Finish1, surface),
        };
        foreach (PavingLayer layer in type.Layers)
        {
            ElementId material = EnsureLayerMaterial(document, layer);
            if (layer.Kind == PavingLayerKind.Manta || layer.ThicknessMm <= 0)
            {
                layers.Add(new CompoundStructureLayer(0, MaterialFunctionAssignment.Membrane, material));
                continue;
            }

            MaterialFunctionAssignment function = layer.Kind switch
            {
                PavingLayerKind.Assentamento or PavingLayerKind.Argamassa => MaterialFunctionAssignment.Substrate,
                _ => MaterialFunctionAssignment.Structure,
            };
            layers.Add(new CompoundStructureLayer(RevitUnits.MmToFeet(layer.ThicknessMm), function, material));
        }

        CompoundStructure structure = BuildStructure(document, layers, surface, type);
        floorType.SetCompoundStructure(structure);
        ElementData.Write(floorType, KindPaving, new PavingLink { PavingId = type.Id, Signature = type.Signature });
        return floorType;
    }

    private static CompoundStructure BuildStructure(Document document, List<CompoundStructureLayer> layers, ElementId surface, PavingType type)
    {
        // Revestimento na face superior (camada externa) e as demais como núcleo; se o Revit recusar alguma
        // combinação (membrana na borda, por exemplo), cai para a estrutura simples e, por fim, só o revestimento.
        try
        {
            CompoundStructure structure = CompoundStructure.CreateSimpleCompoundStructure(layers);
            if (layers.Count > 1)
            {
                structure.SetNumberOfShellLayers(ShellLayerType.Exterior, 1);
            }

            int core = layers.FindIndex(1, l => l.Function == MaterialFunctionAssignment.Structure);
            if (core > 0 && structure.CanLayerBeStructuralMaterial(core))
            {
                structure.StructuralMaterialIndex = core;
            }

            if (structure.IsValid(document, out _, out _))
            {
                return structure;
            }

            ViridianLog.Warn($"Piso \"{type.Name}\": estrutura em camadas recusada; usando estrutura simples.");
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
        {
            ViridianLog.Warn($"Piso \"{type.Name}\": estrutura em camadas", ex);
        }

        try
        {
            List<CompoundStructureLayer> solid = layers.Where(l => l.Width > 0).ToList();
            CompoundStructure simple = CompoundStructure.CreateSimpleCompoundStructure(solid);
            if (simple.IsValid(document, out _, out _))
            {
                return simple;
            }
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
        {
            ViridianLog.Warn($"Piso \"{type.Name}\": estrutura simples", ex);
        }

        return CompoundStructure.CreateSingleLayerCompoundStructure(MaterialFunctionAssignment.Structure, RevitUnits.MmToFeet(type.ThicknessMm), surface);
    }

    /// <summary>Troca o tipo dos pisos informados. Retorna quantos foram alterados. Precisa de transação.</summary>
    public static int Apply(Document document, FloorType floorType, IEnumerable<ElementId> floors)
    {
        var ids = floors.Where(id => document.GetElement(id) is Floor f && f.GetTypeId() != floorType.Id).ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        Element.ChangeTypeId(document, ids, floorType.Id);
        return ids.Count;
    }

    /// <summary>Tipo de região preenchida com a paginação e a cor do piso, para desenhar pisos em planta. Precisa de transação.</summary>
    public static FilledRegionType EnsureRegionType(Document document, PavingType type) =>
        HatchService.EnsureRegionType(document, Pattern(type), new RegionPaint(type.JointRgb, type.SurfaceRgb));

    /// <summary>Hachura do piso com nome aceito pelo Revit (a assinatura no nome força a recriação quando o desenho muda).</summary>
    public static HatchPattern Pattern(PavingType type) =>
        PavingPatterns.Build(type, SafeName($"VIR Piso {type.Name} {type.Signature[..6]}"));

    /// <summary>Remove os caracteres que o Revit não aceita em nomes de tipos, materiais e padrões.</summary>
    public static string SafeName(string name)
    {
        const string forbidden = "{}[]|;<>?`~\\:";
        string clean = new(name.Where(c => !forbidden.Contains(c) && !char.IsControl(c)).ToArray());
        clean = clean.Trim();
        return clean.Length == 0 ? "VIR Piso" : clean;
    }

    /// <summary>Detalhe em corte do piso numa vista de desenho (1:10). Precisa de transação.</summary>
    public static ViewDrafting DetailView(Document document, PavingType type, double textMm) =>
        new DesignService(textMm).DrawingView(document, SafeName("Viridian - Detalhe de piso - " + type.Name), PavingDetail.Draw(type), 10);

    /// <summary>Áreas dos pisos do projeto por tipo da biblioteca (pelo vínculo gravado no tipo ou pelo nome).</summary>
    public static List<PavingQuantity> Quantities(Document document, IReadOnlyList<PavingType> library)
    {
        var byId = library.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var areas = new Dictionary<string, (double Area, int Count)>(StringComparer.OrdinalIgnoreCase);
        using var collector = new FilteredElementCollector(document);
        foreach (Floor floor in collector.OfClass(typeof(Floor)).Cast<Floor>())
        {
            if (document.GetElement(floor.GetTypeId()) is not FloorType floorType)
            {
                continue;
            }

            string? id = ElementData.TryRead(floorType, KindPaving, out PavingLink link) ? link.PavingId : null;
            if (id is null && floorType.Name.StartsWith(TypePrefix, StringComparison.OrdinalIgnoreCase))
            {
                id = library.FirstOrDefault(t => string.Equals(SafeName(TypePrefix + t.Name), floorType.Name, StringComparison.OrdinalIgnoreCase))?.Id;
            }

            if (id is null || !byId.ContainsKey(id))
            {
                continue;
            }

            double area = floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0;
            (double a, int c) = areas.GetValueOrDefault(id);
            areas[id] = (a + RevitUnits.SquareFeetToSquareMeters(area), c + 1);
        }

        return areas.Select(kv => new PavingQuantity { Type = byId[kv.Key], AreaM2 = kv.Value.Area, Elements = kv.Value.Count })
            .OrderBy(q => q.Type.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static ElementId EnsureLayerMaterial(Document document, PavingLayer layer)
    {
        string name = SafeName(LayerMaterialPrefix + layer.Name);
        Material? material = FindMaterial(document, name);
        if (material is null)
        {
            material = (Material)document.GetElement(Material.Create(document, name));
            material.Color = HatchService.ToColor(PavingLabels.LayerColor(layer.Kind));
            material.UseRenderAppearanceForShading = false;
        }

        return material.Id;
    }

    private static Material? FindMaterial(Document document, string name)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
