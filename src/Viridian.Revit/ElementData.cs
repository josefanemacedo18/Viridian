using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Viridian.Core.Config;
using Viridian.Core.Scale;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Vínculo entre um tipo de família do projeto e uma espécie do catálogo.</summary>
public sealed class SpeciesLink
{
    public string SpeciesId { get; set; } = string.Empty;

    /// <summary>Índice da variante gráfica (0, 1, 2...).</summary>
    public int Variant { get; set; }

    /// <summary>Assinatura da geometria gerada; quando muda, a família precisa ser regerada.</summary>
    public string GeometryHash { get; set; } = string.Empty;

    /// <summary>Verdadeiro quando a família é personalizada (não gerada pelo plugin).</summary>
    public bool IsCustom { get; set; }

    /// <summary>A família tem a vegetação ilustrada em 3D (cartões cruzados).</summary>
    public bool Has3D { get; set; }
}

/// <summary>Vínculo entre uma etiqueta de texto e o elemento identificado.</summary>
public sealed class LabelLink
{
    /// <summary>UniqueId dos elementos identificados (exemplares ou região do maciço).</summary>
    public List<string> TargetUniqueIds { get; set; } = new();

    public int Model { get; set; }

    public bool Grouped { get; set; }
}

/// <summary>Perfil de representação fixado manualmente em uma vista.</summary>
public sealed class ViewProfileOverride
{
    public DetailProfile Profile { get; set; }
}

/// <summary>Símbolo gráfico (componente de detalhe) pertencente a um maciço.</summary>
public sealed class MassSymbolLink
{
    public string MassingId { get; set; } = string.Empty;
}

/// <summary>Dados do Viridian gravados no projeto como um todo.</summary>
public sealed class ProjectData
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Código da espécie neste projeto, por identificador de espécie (codificação por projeto).</summary>
    public Dictionary<string, string> SpeciesCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Quantidades já compradas (comparativo de aquisição).</summary>
    public Viridian.Core.Reports.PurchaseLedger Purchases { get; set; } = new();
}

/// <summary>
/// Leitura e gravação de dados do Viridian nos elementos do Revit (Extensible Storage).
/// Cada registro é um JSON identificado por um tipo, o que permite evoluir os dados sem trocar de esquema.
/// </summary>
public static class ElementData
{
    public const string KindSpecies = "Species";
    public const string KindMassing = "Massing";
    public const string KindLabel = "Label";
    public const string KindViewProfile = "ViewProfile";
    public const string KindMassSymbol = "MassSymbol";
    public const string KindProject = "Project";
    public const string KindPhotoBoard = "PhotoBoard";

    public static readonly Guid SchemaGuid = new("a510a2f0-55af-469f-8ea5-f20528788096");

    private const string FieldKind = "Kind";
    private const string FieldVersion = "Version";
    private const string FieldJson = "Json";

    public static Schema GetSchema()
    {
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is not null)
        {
            return schema;
        }

        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName("ViridianData");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.SetVendorId("VIRIDIAN");
        builder.SetDocumentation("Dados do plugin Viridian para projetos de paisagismo.");
        builder.AddSimpleField(FieldKind, typeof(string));
        builder.AddSimpleField(FieldVersion, typeof(int));
        builder.AddSimpleField(FieldJson, typeof(string));
        return builder.Finish();
    }

    /// <summary>Tipo do registro gravado no elemento, ou vazio quando não há dados do Viridian.</summary>
    public static string KindOf(Element element)
    {
        try
        {
            Entity entity = element.GetEntity(GetSchema());
            return entity is not null && entity.IsValid() ? entity.Get<string>(FieldKind) ?? string.Empty : string.Empty;
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Falha ao ler dados do elemento " + element.Id, ex);
            return string.Empty;
        }
    }

    public static bool TryRead<T>(Element element, string kind, out T value)
        where T : class
    {
        value = null!;
        try
        {
            Entity entity = element.GetEntity(GetSchema());
            if (entity is null || !entity.IsValid())
            {
                return false;
            }

            if (!string.Equals(entity.Get<string>(FieldKind), kind, StringComparison.Ordinal))
            {
                return false;
            }

            string json = entity.Get<string>(FieldJson);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            T? parsed = ViridianJson.Deserialize<T>(json);
            if (parsed is null)
            {
                return false;
            }

            value = parsed;
            return true;
        }
        catch (Exception ex)
        {
            ViridianLog.Warn($"Falha ao ler dados \"{kind}\" do elemento {element.Id}", ex);
            return false;
        }
    }

    /// <summary>Grava o registro no elemento. Precisa de transação aberta.</summary>
    public static void Write<T>(Element element, string kind, T value)
        where T : class
    {
        var entity = new Entity(GetSchema());
        entity.Set(FieldKind, kind);
        entity.Set(FieldVersion, 1);
        entity.Set(FieldJson, System.Text.Json.JsonSerializer.Serialize(value, CompactOptions));
        element.SetEntity(entity);
    }

    /// <summary>Remove os dados do Viridian do elemento. Precisa de transação aberta.</summary>
    public static void Clear(Element element)
    {
        Schema schema = GetSchema();
        Entity entity = element.GetEntity(schema);
        if (entity is not null && entity.IsValid())
        {
            element.DeleteEntity(schema);
        }
    }

    /// <summary>Todos os elementos do documento que possuem dados do Viridian do tipo informado.</summary>
    public static IEnumerable<Element> ElementsOfKind(Document document, string kind)
    {
        GetSchema();
        using var collector = new FilteredElementCollector(document);
        foreach (Element element in collector.WherePasses(new ExtensibleStorageFilter(SchemaGuid)).ToElements())
        {
            if (string.Equals(KindOf(element), kind, StringComparison.Ordinal))
            {
                yield return element;
            }
        }
    }

    // ---- dados do projeto

    public static ProjectData ReadProject(Document document)
    {
        foreach (Element element in ElementsOfKind(document, KindProject))
        {
            if (TryRead(element, KindProject, out ProjectData data))
            {
                data.SpeciesCodes = new Dictionary<string, string>(
                    data.SpeciesCodes ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                data.Purchases ??= new Viridian.Core.Reports.PurchaseLedger();
                data.Purchases.Items ??= new List<Viridian.Core.Reports.PurchaseRecord>();
                return data;
            }
        }

        return new ProjectData();
    }

    /// <summary>Grava os dados do projeto. Precisa de transação aberta.</summary>
    public static void WriteProject(Document document, ProjectData data)
    {
        Element? holder = ElementsOfKind(document, KindProject).FirstOrDefault();
        holder ??= DataStorage.Create(document);
        Write(holder, KindProject, data);
    }

    private static readonly System.Text.Json.JsonSerializerOptions CompactOptions = CreateCompactOptions();

    private static System.Text.Json.JsonSerializerOptions CreateCompactOptions()
    {
        var options = new System.Text.Json.JsonSerializerOptions(ViridianJson.Options) { WriteIndented = false };
        return options;
    }
}
