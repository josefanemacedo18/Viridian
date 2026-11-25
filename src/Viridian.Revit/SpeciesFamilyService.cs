using System.IO;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Detailing;
using Viridian.Core.Model;
using Viridian.Core.Model3D.Illustration;
using Viridian.Core.Model3D.Profiles;
using Viridian.Core.Symbols;
using Viridian.Library;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Viridian.Revit;

/// <summary>Resultado da sincronização entre o catálogo e os tipos do projeto.</summary>
public sealed class SyncSummary
{
    public int TypesUpdated { get; set; }

    public int FamiliesRegenerated { get; set; }

    public int MissingSpecies { get; set; }

    public List<string> Problems { get; } = new();
}

/// <summary>
/// Mantém no projeto as famílias de vegetação correspondentes às espécies do catálogo:
/// gera o símbolo 2D sob demanda, reutiliza famílias já geradas e grava os dados botânicos nos tipos.
/// </summary>
public sealed class SpeciesFamilyService
{
    private readonly ViridianSettings _settings;
    private readonly LibraryPaths _paths;

    public SpeciesFamilyService(ViridianSettings settings, LibraryPaths paths)
    {
        _settings = settings;
        _paths = paths;
    }

    private static bool _colorWarningShown;

    /// <summary>
    /// Vegetação ilustrada (RPC) da espécie: a família recebe o eixo de seleção e os parâmetros de exemplar, e a imagem é
    /// desenhada na vista pelo Viridian. Nulo quando a espécie não está no piloto, tem família própria ou o recurso está
    /// desligado nas configurações.
    /// </summary>
    public SpriteHandle? HandleFor(Species species)
    {
        if (!_settings.Model3D.IllustratedFamilies || species.HasCustomPlantingFamily || !PlantSpritesPlanner.IsPilot(species))
        {
            return null;
        }

        (double _, double height) = GrowthModel.Size(species, GrowthStage.Maturidade);
        return new SpriteHandle(Math.Round(height * 0.5, 2));
    }

    /// <summary>Assinatura esperada da família: a do símbolo 2D e, nas espécies ilustradas, a marca do eixo de seleção.</summary>
    public string ExpectedHash(Species species, int variant, ProjectData project) =>
        Combine(GeometryHash(species, variant, _settings.Colors), HandleFor(species));

    private static string Combine(string hash2D, SpriteHandle? handle) =>
        handle is null ? hash2D : hash2D + SpriteHandle.Marker + "-" + handle.AxisHeightM.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Indica que o modelo de família configurado mudou e as configurações devem ser gravadas.</summary>
    public bool TemplateFileChanged { get; private set; }

    /// <summary>Código da espécie neste projeto (o do catálogo, salvo renumeração por projeto).</summary>
    public static string ProjectCodeOf(ProjectData project, Species species) =>
        project.SpeciesCodes.TryGetValue(species.Id, out string? code) && !string.IsNullOrWhiteSpace(code) ? code : species.Code;

    /// <summary>
    /// Garante que o tipo de família da espécie exista no projeto, com os dados atualizados.
    /// Não pode haver transação aberta: famílias são geradas e carregadas fora de transações.
    /// </summary>
    public FamilySymbol EnsureSymbol(Document document, Species species, int variant, ProjectData project)
    {
        if (document.IsModifiable)
        {
            throw new InvalidOperationException("EnsureSymbol não pode ser chamado com uma transação aberta.");
        }

        variant = Math.Clamp(variant, 0, species.EffectiveVariants - 1);
        bool custom = species.HasCustomPlantingFamily;
        SpriteHandle? volume = custom ? null : HandleFor(species);
        string hash = custom ? string.Empty : Combine(GeometryHash(species, variant, _settings.Colors), volume);
        FamilySymbol symbol;
        if (custom)
        {
            symbol = EnsureCustom(document, species);
        }
        else
        {
            var volumeResult = new VolumeBuildResult();
            symbol = EnsureGenerated(document, species, variant, hash, volume, volumeResult)
                     ?? throw new ViridianException($"Não foi possível gerar a família de {species.DisplayName}.");
            if (volume is not null && volumeResult.Attempted && !volumeResult.Usable)
            {
                // A imagem ilustrada aparece mesmo sem o eixo; só a seleção nas vistas 3D fica pelo símbolo.
                ViridianLog.Warn($"Vegetação ilustrada: eixo de seleção de {species.DisplayName} não criado ({string.Join("; ", volumeResult.Reasons.Concat(volumeResult.Notes).Take(3))}).");
            }
        }

        bool has3D = volume is not null;
        Tx.Run(document, "Viridian: dados da espécie", () =>
        {
            SharedParameterService.EnsureBound(document);
            if (!symbol.IsActive)
            {
                symbol.Activate();
            }

            WriteTypeData(symbol, species, ProjectCodeOf(project, species));
            ElementData.Write(symbol, ElementData.KindSpecies, new SpeciesLink
            {
                SpeciesId = species.Id,
                Variant = custom ? 0 : variant,
                GeometryHash = hash,
                IsCustom = custom,
                Has3D = has3D,
            });
        });

        return symbol;
    }

    /// <summary>Carrega o componente de detalhe personalizado da espécie, usado como símbolo gráfico de maciços.</summary>
    public FamilySymbol EnsureDetailSymbol(Document document, Species species)
    {
        if (!species.HasCustomDetailFamily)
        {
            throw new ViridianException($"A espécie {species.DisplayName} não possui símbolo de detalhe personalizado.");
        }

        string file = _paths.Resolve(species.CustomDetailFamilyFile, _paths.CustomSymbolsFolder);
        Family family = LoadFamilyFile(document, file);
        FamilySymbol symbol = PickSymbol(document, family, species.CustomDetailTypeName);
        if (!symbol.IsActive)
        {
            Tx.Run(document, "Viridian: ativar símbolo", () => symbol.Activate());
        }

        return symbol;
    }

    /// <summary>Todos os tipos de vegetação do projeto vinculados a espécies do catálogo.</summary>
    public static IReadOnlyList<(FamilySymbol Symbol, SpeciesLink Link)> LinkedSymbols(Document document)
    {
        var result = new List<(FamilySymbol, SpeciesLink)>();
        using var collector = new FilteredElementCollector(document);
        foreach (FamilySymbol symbol in collector.OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_Planting).Cast<FamilySymbol>())
        {
            if (ElementData.TryRead(symbol, ElementData.KindSpecies, out SpeciesLink link) && !string.IsNullOrWhiteSpace(link.SpeciesId))
            {
                result.Add((symbol, link));
            }
        }

        return result;
    }

    /// <summary>Compara os tipos do projeto com o catálogo e aponta os que estão desatualizados.</summary>
    public IReadOnlyList<(FamilySymbol Symbol, string Reason)> FindOutdated(Document document, Func<string, Species?> lookup, ProjectData project)
    {
        var result = new List<(FamilySymbol, string)>();
        foreach ((FamilySymbol symbol, SpeciesLink link) in LinkedSymbols(document))
        {
            Species? species = lookup(link.SpeciesId);
            if (species is null)
            {
                continue;
            }

            var reasons = new List<string>();
            if (VirParams.GetString(symbol, VirParams.SpeciesCode) != ProjectCodeOf(project, species))
            {
                reasons.Add("código");
            }

            if (VirParams.GetString(symbol, VirParams.PopularName) != species.PopularName
                || VirParams.GetString(symbol, VirParams.ScientificName) != species.ScientificName)
            {
                reasons.Add("nome");
            }

            if (Math.Abs(VirParams.GetMeters(symbol, VirParams.CrownDiameter) - species.CrownDiameterM) > 1e-6
                || Math.Abs(VirParams.GetMeters(symbol, VirParams.AdultHeight) - species.AdultHeightM) > 1e-6)
            {
                reasons.Add("dimensões");
            }

            if (!link.IsCustom && !species.HasCustomPlantingFamily && link.GeometryHash != ExpectedHash(species, link.Variant, project))
            {
                reasons.Add(HandleFor(species) is null ? "símbolo" : "símbolo ou vegetação ilustrada");
            }

            if (reasons.Count > 0)
            {
                result.Add((symbol, string.Join(", ", reasons)));
            }
        }

        return result;
    }

    /// <summary>
    /// Atualiza todos os tipos do projeto a partir do catálogo, regerando os símbolos cuja geometria mudou.
    /// Não pode haver transação aberta.
    /// </summary>
    public SyncSummary SyncAll(Document document, Func<string, Species?> lookup, ProjectData project)
    {
        var summary = new SyncSummary();
        foreach ((FamilySymbol symbol, SpeciesLink link) in LinkedSymbols(document))
        {
            Species? species = lookup(link.SpeciesId);
            if (species is null)
            {
                summary.MissingSpecies++;
                continue;
            }

            try
            {
                bool regenerate = !link.IsCustom
                    && !species.HasCustomPlantingFamily
                    && link.GeometryHash != ExpectedHash(species, link.Variant, project);
                if (regenerate)
                {
                    EnsureSymbol(document, species, link.Variant, project);
                    summary.FamiliesRegenerated++;
                }
                else
                {
                    Tx.Run(document, "Viridian: atualizar dados", () =>
                    {
                        SharedParameterService.EnsureBound(document);
                        WriteTypeData(symbol, species, ProjectCodeOf(project, species));
                    });
                }

                summary.TypesUpdated++;
            }
            catch (FamilyTemplateMissingException)
            {
                throw;
            }
            catch (Exception ex) when (ex is ViridianException or Autodesk.Revit.Exceptions.ApplicationException or IOException or InvalidOperationException)
            {
                ViridianLog.Error("Falha ao atualizar " + species.DisplayName, ex);
                summary.Problems.Add($"{species.DisplayName}: {ex.Message}");
            }
        }

        return summary;
    }

    /// <summary>Assinatura da geometria do símbolo: muda quando o desenho precisa ser regerado.</summary>
    public static string GeometryHash(Species species, int variant, ColorSettings colors)
    {
        string text = string.Join("|",
            SymbolGenerator.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SymbolFamilyBuilder.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            species.SymbolStyle.ToString(),
            species.CrownDiameterM.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            species.EffectiveSymbolSeed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            variant.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SymbolPaint.For(species, colors).Signature);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 8);
    }

    /// <summary>Nome padrão da família gerada para uma espécie e variante.</summary>
    public static string FamilyNameOf(Species species, int variant)
    {
        string name = string.IsNullOrWhiteSpace(species.ScientificName) ? species.PopularName : species.ScientificName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = species.Id;
        }

        return SafeFile.SanitizeName($"VIR_{Labels.Prefix(species.Category)}_{name}", 70) + "_V" + (variant + 1);
    }

    // ------------------------------------------------------------------ famílias geradas

    /// <summary>Gera ou reaproveita a família (com o eixo da vegetação ilustrada, quando pedido).</summary>
    private FamilySymbol? EnsureGenerated(Document document, Species species, int variant, string hash, SpriteHandle? volume, VolumeBuildResult? volumeResult)
    {
        (FamilySymbol Symbol, SpeciesLink Link)? linked = LinkedSymbols(document)
            .Where(x => !x.Link.IsCustom && x.Link.Variant == variant
                        && string.Equals(x.Link.SpeciesId, species.Id, StringComparison.OrdinalIgnoreCase))
            .Select(x => ((FamilySymbol, SpeciesLink)?)x)
            .FirstOrDefault();

        if (linked.HasValue && linked.Value.Link.GeometryHash == hash)
        {
            return linked.Value.Symbol;
        }

        // Regerar com o mesmo nome faz o Revit substituir a família e atualizar os exemplares existentes.
        string standardName = FamilyNameOf(species, variant);
        string familyName = linked.HasValue ? linked.Value.Symbol.Family.Name : UniqueFamilyName(document, standardName, species.Id);
        string typeName = TypeNameOf(species);
        string file = EnsureFamilyFile(document.Application, species, variant, familyName, standardName, hash, typeName, volume, volumeResult);
        Family family = LoadFamilyFile(document, file);

        // Ao regerar, o tipo já usado pelos exemplares continua sendo o tipo da espécie.
        if (linked.HasValue && linked.Value.Symbol.IsValidObject && linked.Value.Symbol.Family?.Id == family.Id)
        {
            RemoveUnusedTypes(document, family, linked.Value.Symbol.Id);
            return linked.Value.Symbol;
        }

        return PickSymbol(document, family, typeName);
    }

    /// <summary>Remove tipos sem exemplares acrescentados à família por uma recarga (mantém o tipo informado).</summary>
    private static void RemoveUnusedTypes(Document document, Family family, ElementId keep)
    {
        var others = family.GetFamilySymbolIds().Where(id => id != keep).ToList();
        if (others.Count == 0)
        {
            return;
        }

        var used = new HashSet<ElementId>();
        using (var collector = new FilteredElementCollector(document))
        {
            foreach (Element instance in collector.OfClass(typeof(FamilyInstance)).OfCategory(BuiltInCategory.OST_Planting))
            {
                used.Add(instance.GetTypeId());
            }
        }

        List<ElementId> unused = others.Where(id => !used.Contains(id)).ToList();
        if (unused.Count == 0)
        {
            return;
        }

        try
        {
            Tx.Run(document, "Viridian: limpar tipos", () => document.Delete(unused));
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or InvalidOperationException)
        {
            ViridianLog.Warn("Não foi possível remover tipos sem uso da família " + family.Name, ex);
        }
    }

    /// <summary>Nome do tipo da família gerada: o nome popular da espécie.</summary>
    public static string TypeNameOf(Species species) =>
        SafeFile.SanitizeName(string.IsNullOrWhiteSpace(species.PopularName) ? species.ScientificName : species.PopularName, 60);

    private static string UniqueFamilyName(Document document, string standardName, string speciesId)
    {
        Family? existing = FindFamily(document, standardName);
        if (existing is null)
        {
            return standardName;
        }

        // O nome já pertence a outra espécie (ou a uma família sem vínculo): acrescenta um sufixo estável.
        bool sameSpecies = existing.GetFamilySymbolIds()
            .Select(id => document.GetElement(id))
            .OfType<FamilySymbol>()
            .Any(s => ElementData.TryRead(s, ElementData.KindSpecies, out SpeciesLink link)
                      && string.Equals(link.SpeciesId, speciesId, StringComparison.OrdinalIgnoreCase));
        if (sameSpecies)
        {
            return standardName;
        }

        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(speciesId)), 0, 2);
        return standardName + "_" + suffix;
    }

    private string EnsureFamilyFile(RevitApplication app, Species species, int variant, string familyName, string standardName, string hash, string typeName,
        SpriteHandle? volume, VolumeBuildResult? volumeResult)
    {
        // As famílias da vegetação ilustrada ficam numa subpasta própria (o nome do arquivo é o nome da família no projeto),
        // para projetos com e sem a vegetação ilustrada não regerarem a mesma família um do outro.
        bool cacheable = string.Equals(familyName, standardName, StringComparison.OrdinalIgnoreCase);
        string folder = volume is null ? _paths.GeneratedFolder(species.Category) : Path.Combine(_paths.GeneratedFolder(species.Category), "3D");
        string cacheFile = Path.Combine(folder, familyName + ".rfa");
        string indexKey = (volume is null ? string.Empty : "3D|") + familyName;
        Dictionary<string, string> index = ReadIndex();
        string cacheKey = hash + "|" + typeName;
        if (cacheable && File.Exists(cacheFile) && index.TryGetValue(indexKey, out string? cachedKey) && cachedKey == cacheKey)
        {
            return cacheFile;
        }

        PlantSymbol symbol = SymbolGenerator.Generate(species.SymbolStyle, species.CrownDiameterM, species.EffectiveSymbolSeed, variant);
        SymbolPaint paint = SymbolPaint.For(species, _settings.Colors);
        string temporary = Path.Combine(Path.GetTempPath(), "Viridian", "Familias", volume is null ? string.Empty : "3D", familyName + ".rfa");
        string target = cacheable ? cacheFile : temporary;
        if (volumeResult is not null && volume is not null)
        {
            volumeResult.Attempted = true;
        }

        try
        {
            BuildWithAnyTemplate(app, symbol, paint, target, typeName, volume, volumeResult);
        }
        catch (Exception ex) when (cacheable && ex is IOException or UnauthorizedAccessException or Autodesk.Revit.Exceptions.FileAccessException)
        {
            // Biblioteca somente leitura ou arquivo em uso: gera em pasta temporária.
            ViridianLog.Warn("Não foi possível gravar a família na biblioteca; usando pasta temporária.", ex);
            BuildWithAnyTemplate(app, symbol, paint, temporary, typeName, volume, volumeResult);
            return temporary;
        }

        if (cacheable)
        {
            index[indexKey] = cacheKey;
            WriteIndex(index);
        }

        return target;
    }

    private void BuildWithAnyTemplate(RevitApplication app, PlantSymbol symbol, SymbolPaint paint, string target, string typeName,
        SpriteHandle? volume = null, VolumeBuildResult? volumeResult = null)
    {
        IReadOnlyList<string> candidates = FamilyTemplateLocator.Candidates(app, _settings.FamilyTemplateFile);
        var rejections = new List<string>();
        foreach (string template in candidates)
        {
            try
            {
                int failuresBefore = SymbolFamilyBuilder.ColorFailures.Count;
                int skipped = SymbolFamilyBuilder.Build(app, template, symbol, paint, target, _settings, typeName, volume, volumeResult);
                if (skipped > 0)
                {
                    ViridianLog.Warn($"{skipped} linha(s) do símbolo foram recusadas pelo Revit em {Path.GetFileName(target)}.");
                }

                if (SymbolFamilyBuilder.ColorFailures.Count > failuresBefore && !_colorWarningShown)
                {
                    _colorWarningShown = true;
                    var dialog = new Autodesk.Revit.UI.TaskDialog("Viridian — cores do símbolo")
                    {
                        MainInstruction = $"O Revit não aceitou as cores do símbolo de \"{typeName}\".",
                        MainContent = "O símbolo foi inserido só com as linhas. Envie esta mensagem (ou o registro de erros, em Viridian > Sobre) "
                                      + "para a correção. Modelo de família usado: " + Path.GetFileName(template) + ".",
                        ExpandedContent = "Motivos informados pelo Revit:\n" + SymbolFamilyBuilder.LastColorFailureReason,
                    };
                    dialog.Show();
                }

                if (!string.Equals(_settings.FamilyTemplateFile, template, StringComparison.OrdinalIgnoreCase))
                {
                    _settings.FamilyTemplateFile = template;
                    TemplateFileChanged = true;
                }

                return;
            }
            catch (TemplateRejectedException ex)
            {
                rejections.Add(Path.GetFileName(template) + ": " + ex.Message);
            }
        }

        ViridianLog.Warn("Nenhum modelo de família serviu. " + string.Join(" | ", rejections));
        throw new FamilyTemplateMissingException(
            candidates.Count == 0
                ? "Nenhum modelo de família (.rft) foi encontrado na instalação do Revit."
                : "Nenhum dos modelos de família encontrados pôde ser usado para gerar símbolos de vegetação.");
    }

    private Dictionary<string, string> ReadIndex()
    {
        try
        {
            if (File.Exists(_paths.GeneratedIndexFile))
            {
                Dictionary<string, string>? index = ViridianJson.Deserialize<Dictionary<string, string>>(File.ReadAllText(_paths.GeneratedIndexFile));
                if (index is not null)
                {
                    return new Dictionary<string, string>(index, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ViridianLog.Warn("Índice de famílias geradas ilegível; será recriado.", ex);
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private void WriteIndex(Dictionary<string, string> index)
    {
        try
        {
            SafeFile.WriteAllTextAtomic(_paths.GeneratedIndexFile, ViridianJson.Serialize(index));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViridianLog.Warn("Não foi possível gravar o índice de famílias geradas.", ex);
        }
    }

    // ------------------------------------------------------------------ famílias personalizadas

    private FamilySymbol EnsureCustom(Document document, Species species)
    {
        string file = _paths.Resolve(species.CustomPlantingFamilyFile, _paths.CustomFamiliesFolder);
        Family family = LoadFamilyFile(document, file);
        if (family.FamilyCategory is null || family.FamilyCategory.BuiltInCategory != BuiltInCategory.OST_Planting)
        {
            throw new ViridianException($"A família personalizada \"{family.Name}\" não pertence à categoria Vegetação.");
        }

        return PickSymbol(document, family, species.CustomPlantingTypeName);
    }

    // ------------------------------------------------------------------ utilitários

    /// <summary>Carrega (ou recarrega) um arquivo de família no projeto.</summary>
    public static Family LoadFamilyFile(Document document, string file)
    {
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            throw new ViridianException("Arquivo de família não encontrado:\n" + file);
        }

        string familyName = Path.GetFileNameWithoutExtension(file);
        Family? family = null;
        Tx.Run(document, "Viridian: carregar família", () =>
        {
            document.LoadFamily(file, new OverwriteFamilyLoadOptions(), out family);
        });

        family ??= FindFamily(document, familyName);
        return family ?? throw new ViridianException($"O Revit não carregou a família \"{familyName}\".");
    }

    public static Family? FindFamily(Document document, string name)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfClass(typeof(Family)).Cast<Family>()
            .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static FamilySymbol PickSymbol(Document document, Family family, string typeName)
    {
        List<FamilySymbol> symbols = family.GetFamilySymbolIds()
            .Select(id => document.GetElement(id))
            .OfType<FamilySymbol>()
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (symbols.Count == 0)
        {
            throw new ViridianException($"A família \"{family.Name}\" não possui tipos.");
        }

        return symbols.FirstOrDefault(s => string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase)) ?? symbols[0];
    }

    private void WriteTypeData(FamilySymbol symbol, Species species, string projectCode)
    {
        VirParams.Set(symbol, VirParams.SpeciesCode, projectCode);
        VirParams.Set(symbol, VirParams.PopularName, species.PopularName);
        VirParams.Set(symbol, VirParams.ScientificName, species.ScientificName);
        VirParams.Set(symbol, VirParams.Category, Labels.Of(species.Category));
        VirParams.Set(symbol, VirParams.BotanicalFamily, species.BotanicalFamily);
        VirParams.Set(symbol, VirParams.Origin, Labels.Of(species.Origin));
        VirParams.SetMeters(symbol, VirParams.AdultHeight, species.AdultHeightM);
        VirParams.SetMeters(symbol, VirParams.CrownDiameter, species.CrownDiameterM);
        VirParams.SetMeters(symbol, VirParams.Spacing, species.RecommendedSpacingM);
        VirParams.Set(symbol, VirParams.SupplyUnit, species.SupplyUnit);
        if (_settings.SyncMarks)
        {
            VirParams.SetBuiltIn(symbol, BuiltInParameter.ALL_MODEL_TYPE_MARK, projectCode);
            VirParams.SetBuiltIn(symbol, BuiltInParameter.ALL_MODEL_DESCRIPTION, $"{species.PopularName} ({species.ScientificName})");
        }
    }
}
