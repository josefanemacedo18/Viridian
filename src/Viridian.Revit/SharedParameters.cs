using System.IO;
using Autodesk.Revit.DB;
using Viridian.Library;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Viridian.Revit;

public enum ParamKind
{
    Text,
    Length,
    Integer,
}

/// <summary>Definição de um parâmetro compartilhado do Viridian.</summary>
public sealed record ParamDef(string Name, Guid Guid, ParamKind Kind, bool IsInstance, string Description);

/// <summary>Parâmetros BIM do Viridian (prefixo VIR_), vinculados à categoria Vegetação.</summary>
public static class VirParams
{
    public const string GroupName = "Viridian";

    // ---- parâmetros de tipo
    public static readonly ParamDef SpeciesCode = new("VIR_CodigoEspecie", new Guid("ce0f0dce-67f5-4666-bfd1-2e711a1836c9"), ParamKind.Text, false, "Código da espécie");
    public static readonly ParamDef PopularName = new("VIR_NomePopular", new Guid("56897f68-2713-4a1b-b163-b9a658b3a1cc"), ParamKind.Text, false, "Nome popular");
    public static readonly ParamDef ScientificName = new("VIR_NomeCientifico", new Guid("f12ec74a-66f4-49cf-8084-e24c740a45cc"), ParamKind.Text, false, "Nome científico");
    public static readonly ParamDef Category = new("VIR_Categoria", new Guid("787674bc-2e69-43fd-bbfc-9dc7a5c5bce6"), ParamKind.Text, false, "Categoria botânica");
    public static readonly ParamDef BotanicalFamily = new("VIR_FamiliaBotanica", new Guid("a1c4dbf2-657e-46ea-a0a6-6cbf00b33a27"), ParamKind.Text, false, "Família botânica");
    public static readonly ParamDef Origin = new("VIR_Origem", new Guid("086709e9-e747-4019-8a0c-5f2f9511634f"), ParamKind.Text, false, "Origem: nativa, exótica etc.");
    public static readonly ParamDef AdultHeight = new("VIR_AlturaAdulta", new Guid("cc4214bb-b1b3-4a68-b5a7-f95f2143801c"), ParamKind.Length, false, "Altura na fase adulta");
    public static readonly ParamDef CrownDiameter = new("VIR_CopaAdulta", new Guid("f5d4d977-5130-4abb-8d30-74c7102c5c11"), ParamKind.Length, false, "Diâmetro adulto de copa");
    public static readonly ParamDef Spacing = new("VIR_EspacamentoRecomendado", new Guid("228c07be-853a-4b95-b537-b4d1af729935"), ParamKind.Length, false, "Espaçamento recomendado");
    public static readonly ParamDef SupplyUnit = new("VIR_UnidadeFornecimento", new Guid("a2993b83-d085-446c-88f4-1462e42796eb"), ParamKind.Text, false, "Unidade comercial de fornecimento");

    // ---- parâmetros de instância
    public static readonly ParamDef ProjectCode = new("VIR_CodigoProjeto", new Guid("7581ead4-15ff-41f9-8185-190c6924b159"), ParamKind.Text, true, "Identificação do exemplar no projeto");
    public static readonly ParamDef Status = new("VIR_Status", new Guid("0e9aa96c-3cb9-4354-8b68-70f9a3377ef8"), ParamKind.Text, true, "Novo, Existente, Removido ou Transplantado");
    public static readonly ParamDef Variant = new("VIR_VarianteGrafica", new Guid("888e41ad-92aa-4f31-bdce-b923cc09de1d"), ParamKind.Integer, true, "Variante do símbolo");
    public static readonly ParamDef Stage = new("VIR_EstagioImplantacao", new Guid("81942a26-ac1a-4019-a40b-89118244cd21"), ParamKind.Text, true, "Porte no momento do plantio");
    public static readonly ParamDef Notes = new("VIR_Observacoes", new Guid("5bf435be-e0df-45e0-a1f9-0432189934cf"), ParamKind.Text, true, "Informações complementares");
    public static readonly ParamDef MassingId = new("VIR_IDMacico", new Guid("67098e04-0411-4eee-b384-382988eabd05"), ParamKind.Text, true, "Código do maciço ao qual o exemplar pertence");

    public static readonly IReadOnlyList<ParamDef> All = new[]
    {
        SpeciesCode, PopularName, ScientificName, Category, BotanicalFamily, Origin, AdultHeight, CrownDiameter, Spacing, SupplyUnit,
        ProjectCode, Status, Variant, Stage, Notes, MassingId,
    };

    // ---- leitura e escrita de valores

    public static string GetString(Element element, ParamDef def)
    {
        Parameter? parameter = element.get_Parameter(def.Guid);
        if (parameter is null || !parameter.HasValue)
        {
            return string.Empty;
        }

        return parameter.StorageType == StorageType.String ? parameter.AsString() ?? string.Empty : parameter.AsValueString() ?? string.Empty;
    }

    public static int GetInt(Element element, ParamDef def)
    {
        Parameter? parameter = element.get_Parameter(def.Guid);
        return parameter is not null && parameter.HasValue && parameter.StorageType == StorageType.Integer ? parameter.AsInteger() : 0;
    }

    /// <summary>Lê um parâmetro de comprimento e devolve o valor em metros.</summary>
    public static double GetMeters(Element element, ParamDef def)
    {
        Parameter? parameter = element.get_Parameter(def.Guid);
        return parameter is not null && parameter.HasValue && parameter.StorageType == StorageType.Double
            ? RevitUnits.ToMeters(parameter.AsDouble())
            : 0;
    }

    /// <summary>Grava texto. Retorna false quando o parâmetro não existe ou é somente leitura.</summary>
    public static bool Set(Element element, ParamDef def, string? value)
    {
        Parameter? parameter = element.get_Parameter(def.Guid);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
        {
            return false;
        }

        string text = value ?? string.Empty;
        if (parameter.HasValue && string.Equals(parameter.AsString() ?? string.Empty, text, StringComparison.Ordinal))
        {
            return true;
        }

        return parameter.Set(text);
    }

    public static bool Set(Element element, ParamDef def, int value)
    {
        Parameter? parameter = element.get_Parameter(def.Guid);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer)
        {
            return false;
        }

        return (parameter.HasValue && parameter.AsInteger() == value) || parameter.Set(value);
    }

    /// <summary>Grava um comprimento informado em metros.</summary>
    public static bool SetMeters(Element element, ParamDef def, double meters)
    {
        Parameter? parameter = element.get_Parameter(def.Guid);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
        {
            return false;
        }

        double feet = RevitUnits.ToFeet(double.IsFinite(meters) ? meters : 0);
        return (parameter.HasValue && Math.Abs(parameter.AsDouble() - feet) < 1e-9) || parameter.Set(feet);
    }

    /// <summary>Grava texto em um parâmetro interno do Revit (Marca, Marca de tipo...).</summary>
    public static void SetBuiltIn(Element element, BuiltInParameter builtIn, string? value)
    {
        Parameter? parameter = element.get_Parameter(builtIn);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
        {
            return;
        }

        string text = value ?? string.Empty;
        if (!string.Equals(parameter.AsString() ?? string.Empty, text, StringComparison.Ordinal))
        {
            parameter.Set(text);
        }
    }
}

/// <summary>Cria e vincula ao projeto os parâmetros compartilhados do Viridian.</summary>
public static class SharedParameterService
{
    /// <summary>Verifica se todos os parâmetros estão vinculados à categoria Vegetação.</summary>
    public static bool AreBound(Document document)
    {
        Category? planting = Category.GetCategory(document, BuiltInCategory.OST_Planting);
        return planting is not null && VirParams.All.All(def => IsBound(document, def, planting));
    }

    /// <summary>
    /// Garante a existência e o vínculo dos parâmetros. Precisa ser chamado dentro de uma transação.
    /// </summary>
    public static void EnsureBound(Document document)
    {
        Category planting = Category.GetCategory(document, BuiltInCategory.OST_Planting)
            ?? throw new ViridianException("A categoria Vegetação não está disponível neste documento.");

        List<ParamDef> missing = VirParams.All.Where(def => !IsBound(document, def, planting)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        RevitApplication app = document.Application;
        string previousFile = string.Empty;
        try
        {
            previousFile = app.SharedParametersFilename ?? string.Empty;
        }
        catch (Exception)
        {
            previousFile = string.Empty;
        }

        try
        {
            DefinitionGroup group = OpenGroup(app, recreate: false);
            if (missing.Any(def => HasWrongGuid(group, def)))
            {
                // O arquivo foi alterado manualmente: refaz a partir do zero.
                group = OpenGroup(app, recreate: true);
            }

            foreach (ParamDef def in missing)
            {
                Definition definition = group.Definitions.get_Item(def.Name) ?? group.Definitions.Create(
                    new ExternalDefinitionCreationOptions(def.Name, SpecOf(def.Kind))
                    {
                        GUID = def.Guid,
                        Description = def.Description,
                        UserModifiable = true,
                        Visible = true,
                    });

                Bind(document, app, definition, def, planting);
            }
        }
        finally
        {
            try
            {
                app.SharedParametersFilename = previousFile;
            }
            catch (Exception ex)
            {
                ViridianLog.Warn("Não foi possível restaurar o arquivo de parâmetros compartilhados do usuário.", ex);
            }
        }
    }

    private static bool IsBound(Document document, ParamDef def, Category planting)
    {
        SharedParameterElement? element = SharedParameterElement.Lookup(document, def.Guid);
        if (element is null)
        {
            return false;
        }

        Binding? binding = document.ParameterBindings.get_Item(element.GetDefinition());
        if (binding is not ElementBinding elementBinding || elementBinding.Categories is null)
        {
            return false;
        }

        bool rightKind = def.IsInstance ? binding is InstanceBinding : binding is TypeBinding;
        return rightKind && elementBinding.Categories.Contains(planting);
    }

    private static void Bind(Document document, RevitApplication app, Definition definition, ParamDef def, Category planting)
    {
        CategorySet categories = app.Create.NewCategorySet();
        categories.Insert(planting);

        // Preserva categorias adicionais que o escritório já tenha vinculado ao mesmo parâmetro.
        SharedParameterElement? existing = SharedParameterElement.Lookup(document, def.Guid);
        if (existing is not null
            && document.ParameterBindings.get_Item(existing.GetDefinition()) is ElementBinding current
            && current.Categories is not null)
        {
            foreach (Category category in current.Categories)
            {
                if (category is not null && category.AllowsBoundParameters)
                {
                    categories.Insert(category);
                }
            }
        }

        Binding binding = def.IsInstance
            ? app.Create.NewInstanceBinding(categories)
            : app.Create.NewTypeBinding(categories);

        if (!document.ParameterBindings.Insert(definition, binding, GroupTypeId.IdentityData)
            && !document.ParameterBindings.ReInsert(definition, binding, GroupTypeId.IdentityData))
        {
            throw new ViridianException($"Não foi possível vincular o parâmetro {def.Name} à categoria Vegetação.");
        }
    }

    private static bool HasWrongGuid(DefinitionGroup group, ParamDef def) =>
        group.Definitions.get_Item(def.Name) is ExternalDefinition external && external.GUID != def.Guid;

    private static DefinitionGroup OpenGroup(RevitApplication app, bool recreate)
    {
        string file = LibraryPaths.SharedParametersFile;
        Directory.CreateDirectory(LibraryPaths.ConfigFolder);
        if (recreate && File.Exists(file))
        {
            File.Delete(file);
        }

        if (!File.Exists(file))
        {
            // Um arquivo vazio é aceito pelo Revit, que grava o cabeçalho ao criar o primeiro grupo.
            File.WriteAllText(file, string.Empty);
        }

        app.SharedParametersFilename = file;
        DefinitionFile definitionFile = app.OpenSharedParameterFile()
            ?? throw new ViridianException("Não foi possível abrir o arquivo de parâmetros compartilhados do Viridian:\n" + file);
        return definitionFile.Groups.get_Item(VirParams.GroupName) ?? definitionFile.Groups.Create(VirParams.GroupName);
    }

    private static ForgeTypeId SpecOf(ParamKind kind) => kind switch
    {
        ParamKind.Length => SpecTypeId.Length,
        ParamKind.Integer => SpecTypeId.Int.Integer,
        _ => SpecTypeId.String.Text,
    };
}
