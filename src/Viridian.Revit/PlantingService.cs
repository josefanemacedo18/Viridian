using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Viridian.Core.Coding;
using Viridian.Core.Config;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Dados gravados em cada exemplar inserido.</summary>
public sealed class PlacementOptions
{
    public PlantStatus Status { get; set; } = PlantStatus.Novo;

    public string Stage { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    /// <summary>Código do maciço ao qual os exemplares pertencem (vazio para exemplares isolados).</summary>
    public string MassingId { get; set; } = string.Empty;

    /// <summary>Semente usada para sortear variantes gráficas.</summary>
    public long Seed { get; set; } = 1;
}

/// <summary>Inserção, alteração e leitura dos exemplares de vegetação do projeto.</summary>
public sealed class PlantingService
{
    private readonly ViridianSettings _settings;

    public PlantingService(ViridianSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Nível de inserção de uma vista em planta, ou null quando a vista não é uma planta.</summary>
    public static Level? LevelOf(View view) => view is ViewPlan plan ? plan.GenLevel : null;

    /// <summary>Texto padrão do estágio de implantação a partir da altura de muda cadastrada.</summary>
    public static string DefaultStage(Species species) =>
        species.PlantingHeightM > 0
            ? "Muda h = " + species.PlantingHeightM.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " m"
            : string.Empty;

    /// <summary>
    /// Insere exemplares nos pontos informados (em metros) e grava identificação e dados.
    /// </summary>
    /// <param name="document">Projeto.</param>
    /// <param name="level">Nível de inserção.</param>
    /// <param name="variantSymbols">Tipos de família da espécie, um por variante gráfica.</param>
    /// <param name="species">Espécie inserida.</param>
    /// <param name="projectSpeciesCode">Código da espécie neste projeto.</param>
    /// <param name="points">Pontos de inserção em metros.</param>
    /// <param name="rotations">Rotações em radianos (opcional).</param>
    /// <param name="options">Dados dos exemplares.</param>
    public IReadOnlyList<ElementId> Place(
        Document document,
        Level level,
        IReadOnlyList<FamilySymbol> variantSymbols,
        Species species,
        string projectSpeciesCode,
        IReadOnlyList<Point2> points,
        IReadOnlyList<double>? rotations,
        PlacementOptions options)
    {
        if (variantSymbols.Count == 0)
        {
            throw new ViridianException("Nenhum tipo de família disponível para a espécie.");
        }

        if (points.Count == 0)
        {
            return Array.Empty<ElementId>();
        }

        return Tx.Run(document, "Viridian: plantio", () =>
        {
            SharedParameterService.EnsureBound(document);
            foreach (FamilySymbol symbol in variantSymbols)
            {
                if (!symbol.IsActive)
                {
                    symbol.Activate();
                }
            }

            document.Regenerate();

            InstanceCodeAllocator allocator = CreateAllocator(document);
            var rng = new DeterministicRandom(DeterministicRandom.Combine(options.Seed, 5381));
            double offset = RevitUnits.ToFeet(_settings.Planting.ElevationOffsetM);
            var created = new List<ElementId>(points.Count);

            for (int i = 0; i < points.Count; i++)
            {
                int variant = variantSymbols.Count == 1 ? 0 : rng.NextInt(0, variantSymbols.Count);
                XYZ location = RevitUnits.ToXyz(points[i], offset);
                FamilyInstance instance = document.Create.NewFamilyInstance(location, variantSymbols[variant], level, StructuralType.NonStructural);
                if (instance is null)
                {
                    continue;
                }

                SetElevationOffset(instance, offset);

                if (rotations is not null && i < rotations.Count && Math.Abs(rotations[i]) > 1e-9)
                {
                    XYZ origin = (instance.Location as LocationPoint)?.Point ?? location;
                    using Line axis = Line.CreateBound(origin, origin + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(document, instance.Id, axis, rotations[i]);
                }

                string code = allocator.Next(species.Category, projectSpeciesCode);
                WriteInstanceData(instance, code, options, variant);
                created.Add(instance.Id);
            }

            return (IReadOnlyList<ElementId>)created;
        }, suppressWarnings: true);
    }

    /// <summary>Grava identificação e dados em exemplares inseridos pela ferramenta nativa do Revit.</summary>
    public void Initialize(Document document, IEnumerable<ElementId> instanceIds, Species species, string projectSpeciesCode, PlacementOptions options, int variant)
    {
        List<FamilyInstance> instances = instanceIds.Select(id => document.GetElement(id)).OfType<FamilyInstance>().ToList();
        if (instances.Count == 0)
        {
            return;
        }

        Tx.Run(document, "Viridian: identificar exemplares", () =>
        {
            SharedParameterService.EnsureBound(document);
            InstanceCodeAllocator allocator = CreateAllocator(document, instances.Select(i => i.Id));
            foreach (FamilyInstance instance in instances)
            {
                WriteInstanceData(instance, allocator.Next(species.Category, projectSpeciesCode), options, variant);
            }
        });
    }

    /// <summary>Troca a espécie de exemplares existentes preservando posição, código e demais dados.</summary>
    public int Substitute(Document document, IEnumerable<ElementId> instanceIds, IReadOnlyList<FamilySymbol> variantSymbols)
    {
        if (variantSymbols.Count == 0)
        {
            return 0;
        }

        List<FamilyInstance> instances = PlantingInstances(document, instanceIds);
        return Tx.Run(document, "Viridian: substituir espécie", () =>
        {
            SharedParameterService.EnsureBound(document);
            foreach (FamilySymbol symbol in variantSymbols)
            {
                if (!symbol.IsActive)
                {
                    symbol.Activate();
                }
            }

            int changed = 0;
            foreach (FamilyInstance instance in instances)
            {
                int variant = Math.Clamp(VirParams.GetInt(instance, VirParams.Variant) - 1, 0, variantSymbols.Count - 1);
                FamilySymbol target = variantSymbols[variant];
                if (instance.GetTypeId() != target.Id)
                {
                    instance.ChangeTypeId(target.Id);
                    changed++;
                }

                VirParams.Set(instance, VirParams.Variant, variant + 1);
            }

            return changed;
        }, suppressWarnings: true);
    }

    /// <summary>Altera dados de exemplares. Valores nulos são mantidos como estão.</summary>
    public int UpdateData(Document document, IEnumerable<ElementId> instanceIds, PlantStatus? status, string? stage, string? notes)
    {
        List<FamilyInstance> instances = PlantingInstances(document, instanceIds);
        return Tx.Run(document, "Viridian: editar exemplares", () =>
        {
            SharedParameterService.EnsureBound(document);
            foreach (FamilyInstance instance in instances)
            {
                if (status.HasValue)
                {
                    VirParams.Set(instance, VirParams.Status, Labels.Of(status.Value));
                }

                if (stage is not null)
                {
                    VirParams.Set(instance, VirParams.Stage, stage);
                }

                if (notes is not null)
                {
                    VirParams.Set(instance, VirParams.Notes, notes);
                }
            }

            return instances.Count;
        });
    }

    /// <summary>Troca a variante gráfica de exemplares, usando os tipos das variantes da espécie.</summary>
    public int ChangeVariant(Document document, IEnumerable<ElementId> instanceIds, IReadOnlyList<FamilySymbol> variantSymbols, int variant)
    {
        if (variantSymbols.Count == 0)
        {
            return 0;
        }

        variant = Math.Clamp(variant, 0, variantSymbols.Count - 1);
        List<FamilyInstance> instances = PlantingInstances(document, instanceIds);
        return Tx.Run(document, "Viridian: variante gráfica", () =>
        {
            FamilySymbol target = variantSymbols[variant];
            if (!target.IsActive)
            {
                target.Activate();
            }

            foreach (FamilyInstance instance in instances)
            {
                if (instance.GetTypeId() != target.Id)
                {
                    instance.ChangeTypeId(target.Id);
                }

                VirParams.Set(instance, VirParams.Variant, variant + 1);
            }

            return instances.Count;
        }, suppressWarnings: true);
    }

    /// <summary>
    /// Atribui códigos de identificação aos exemplares.
    /// </summary>
    /// <param name="document">Projeto.</param>
    /// <param name="instanceIds">Exemplares a codificar.</param>
    /// <param name="overwrite">Verdadeiro para renumerar também os que já possuem código.</param>
    /// <param name="categoryOf">Categoria botânica de um exemplar (a partir da espécie vinculada).</param>
    public int AssignCodes(Document document, IEnumerable<ElementId> instanceIds, bool overwrite, Func<FamilyInstance, PlantCategory> categoryOf)
    {
        List<FamilyInstance> targets = PlantingInstances(document, instanceIds);
        if (!overwrite)
        {
            targets = targets.Where(i => string.IsNullOrWhiteSpace(VirParams.GetString(i, VirParams.ProjectCode))).ToList();
        }

        if (targets.Count == 0)
        {
            return 0;
        }

        return Tx.Run(document, "Viridian: codificar exemplares", () =>
        {
            SharedParameterService.EnsureBound(document);
            InstanceCodeAllocator allocator = CreateAllocator(document, overwrite ? targets.Select(t => t.Id) : null);

            // Ordem estável: de cima para baixo e da esquerda para a direita, como se lê a prancha.
            foreach (FamilyInstance instance in targets
                         .OrderByDescending(i => Math.Round(LocationOf(i).Y, 2))
                         .ThenBy(i => LocationOf(i).X))
            {
                string speciesCode = VirParams.GetString(instance.Symbol, VirParams.SpeciesCode);
                string code = allocator.Next(categoryOf(instance), speciesCode);
                VirParams.Set(instance, VirParams.ProjectCode, code);
                if (_settings.SyncMarks)
                {
                    VirParams.SetBuiltIn(instance, BuiltInParameter.ALL_MODEL_MARK, code);
                }
            }

            return targets.Count;
        });
    }

    /// <summary>Lê todos os exemplares de vegetação do projeto.</summary>
    public static IReadOnlyList<PlantRecord> ReadPlants(Document document, Func<string, Species?> lookup)
    {
        var result = new List<PlantRecord>();
        var links = new Dictionary<ElementId, SpeciesLink?>();
        var levelNames = new Dictionary<ElementId, string>();

        using var collector = new FilteredElementCollector(document);
        foreach (FamilyInstance instance in collector
                     .OfCategory(BuiltInCategory.OST_Planting)
                     .OfClass(typeof(FamilyInstance))
                     .Cast<FamilyInstance>())
        {
            if (instance.SuperComponent is not null)
            {
                continue; // componentes aninhados são contados pela família que os contém
            }

            FamilySymbol? symbol = instance.Symbol;
            if (symbol is null)
            {
                continue;
            }

            if (!links.TryGetValue(symbol.Id, out SpeciesLink? link))
            {
                link = ElementData.TryRead(symbol, ElementData.KindSpecies, out SpeciesLink read) ? read : null;
                links[symbol.Id] = link;
            }

            if (!levelNames.TryGetValue(instance.LevelId, out string? levelName))
            {
                levelName = instance.LevelId == ElementId.InvalidElementId ? string.Empty : document.GetElement(instance.LevelId)?.Name ?? string.Empty;
                levelNames[instance.LevelId] = levelName;
            }

            Species? species = link is null ? null : lookup(link.SpeciesId);
            result.Add(new PlantRecord
            {
                ElementId = instance.Id.Value,
                UniqueId = instance.UniqueId,
                SpeciesId = link?.SpeciesId ?? string.Empty,
                SpeciesCode = VirParams.GetString(symbol, VirParams.SpeciesCode),
                PopularName = VirParams.GetString(symbol, VirParams.PopularName),
                ScientificName = VirParams.GetString(symbol, VirParams.ScientificName),
                CategoryName = species is not null ? Labels.Of(species.Category) : VirParams.GetString(symbol, VirParams.Category),
                ProjectCode = VirParams.GetString(instance, VirParams.ProjectCode),
                Status = Labels.ParseStatus(VirParams.GetString(instance, VirParams.Status)),
                MassingId = VirParams.GetString(instance, VirParams.MassingId),
                Location = LocationOf(instance),
                CrownDiameterM = VirParams.GetMeters(symbol, VirParams.CrownDiameter),
                RecommendedSpacingM = VirParams.GetMeters(symbol, VirParams.Spacing),
                SupplyUnit = VirParams.GetString(symbol, VirParams.SupplyUnit),
                FamilyName = symbol.FamilyName ?? string.Empty,
                TypeName = symbol.Name ?? string.Empty,
                LevelName = levelName,
            });
        }

        return result;
    }

    /// <summary>Posição em planta de um exemplar, em metros.</summary>
    public static Point2 LocationOf(FamilyInstance instance)
    {
        if (instance.Location is LocationPoint point)
        {
            return RevitUnits.ToPoint2(point.Point);
        }

        BoundingBoxXYZ? box = instance.get_BoundingBox(null);
        return box is null ? Point2.Zero : RevitUnits.ToPoint2((box.Min + box.Max) / 2);
    }

    /// <summary>Exemplares de vegetação (não aninhados) dentre os elementos informados.</summary>
    public static List<FamilyInstance> PlantingInstances(Document document, IEnumerable<ElementId> ids) =>
        ids.Select(id => document.GetElement(id))
            .OfType<FamilyInstance>()
            .Where(i => i.Category is not null && i.Category.BuiltInCategory == BuiltInCategory.OST_Planting && i.SuperComponent is null)
            .ToList();

    /// <summary>Todos os exemplares de vegetação pertencentes a um maciço.</summary>
    public static List<FamilyInstance> InstancesOfMassing(Document document, string massingId)
    {
        using var collector = new FilteredElementCollector(document);
        return collector
            .OfCategory(BuiltInCategory.OST_Planting)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(i => string.Equals(VirParams.GetString(i, VirParams.MassingId).Trim(), massingId.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private InstanceCodeAllocator CreateAllocator(Document document, IEnumerable<ElementId>? ignore = null)
    {
        var ignored = ignore is null ? new HashSet<ElementId>() : new HashSet<ElementId>(ignore);
        var allocator = new InstanceCodeAllocator(_settings.InstanceCode);
        using var collector = new FilteredElementCollector(document);
        foreach (Element element in collector.OfCategory(BuiltInCategory.OST_Planting).OfClass(typeof(FamilyInstance)))
        {
            if (!ignored.Contains(element.Id))
            {
                allocator.Register(VirParams.GetString(element, VirParams.ProjectCode));
            }
        }

        return allocator;
    }

    private void WriteInstanceData(FamilyInstance instance, string code, PlacementOptions options, int variant)
    {
        VirParams.Set(instance, VirParams.ProjectCode, code);
        VirParams.Set(instance, VirParams.Status, Labels.Of(options.Status));
        VirParams.Set(instance, VirParams.Variant, variant + 1);
        VirParams.Set(instance, VirParams.Stage, options.Stage);
        VirParams.Set(instance, VirParams.Notes, options.Notes);
        VirParams.Set(instance, VirParams.MassingId, options.MassingId);
        if (_settings.SyncMarks)
        {
            VirParams.SetBuiltIn(instance, BuiltInParameter.ALL_MODEL_MARK, code);
        }
    }

    /// <summary>Fixa o deslocamento vertical do exemplar em relação ao nível, quando a família permite.</summary>
    private static void SetElevationOffset(FamilyInstance instance, double offsetFeet)
    {
        try
        {
            Parameter? parameter = instance.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM)
                ?? instance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
            if (parameter is not null && !parameter.IsReadOnly && parameter.StorageType == StorageType.Double
                && Math.Abs(parameter.AsDouble() - offsetFeet) > 1e-9)
            {
                parameter.Set(offsetFeet);
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Não foi possível ajustar a elevação do exemplar " + instance.Id, ex);
        }
    }
}
