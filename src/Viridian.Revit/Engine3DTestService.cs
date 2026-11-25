using System.Diagnostics;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.DB.Visual;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Generation;
using Viridian.Core.Model3D.Profiles;
using Viridian.Core.Model3D.Textures;
using Viridian.Library;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Viridian.Revit;

/// <summary>
/// Provas de conceito do motor 3D, executadas no Revit do usuário e registradas num relatório:
/// Fase 0 — família BIM com símbolo 2D e geometria 3D do motor (forma livre), carregada, inserida e conferida vista a
/// vista; T0 — texturas procedurais do ipê (folhagem com recorte, flor e casca) aplicadas a um ipê na vista realista.
/// </summary>
public static class Engine3DTestService
{
    public const string FamilyName = "Viridian-Teste-Fase0-Ipe";
    public const string TextureViewName = "Viridian - Teste T0 (texturas)";
    public const string ShapeApplicationId = "Viridian.TesteMotor3D";
    private const double TreeHeightM = 8;
    private const double TreeCrownM = 7;

    /// <summary>Pasta dos testes (relatório, família, texturas e imagens), com data e hora.</summary>
    public static string NewFolder()
    {
        string folder = Path.Combine(LibraryPaths.UserRoot, "Testes do motor 3D", DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static void Describe(EngineTestReport report, RevitApplication app, Document document)
    {
        report.Environment["Revit"] = Safe(() => $"{app.VersionName} ({app.VersionNumber}, build {app.VersionBuild}, atualização {app.SubVersionNumber})");
        report.Environment["Idioma do Revit"] = Safe(() => app.Language.ToString());
        report.Environment["Viridian"] = typeof(Engine3DTestService).Assembly.GetName().Version?.ToString() ?? "?";
        report.Environment["Gerador"] = "versão " + PlantGeometryGenerator.Version;
        report.Environment["Projeto com subprojetos"] = document.IsWorkshared ? "sim" : "não";
        report.Environment["Data"] = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
    }

    // ---- Fase 0: família BIM -----------------------------------------------------------------------------------

    /// <summary>
    /// Cria a família de teste (símbolo 2D em planta + ipê 3D em forma livre, oculto em planta) e grava o .rfa. Não pode
    /// haver transação aberta em nenhum documento. Devolve o caminho do arquivo, ou vazio se não foi possível.
    /// </summary>
    public static string BuildFamily(RevitApplication app, ViridianSettings settings, string folder, EngineTestReport report)
    {
        IReadOnlyList<string> templates = FamilyTemplateLocator.Candidates(app, settings.FamilyTemplateFile);
        if (templates.Count == 0)
        {
            report.Add("Fase 0 · modelo de família", TestOutcome.Falha, "Nenhum modelo de família (.rft) encontrado. Confira Configurações > Famílias.");
            return string.Empty;
        }

        Document? family = report.Run("Fase 0 · abrir modelo de família", () =>
        {
            foreach (string template in templates.Take(6))
            {
                try
                {
                    Document doc = app.NewFamilyDocument(template);
                    if (doc is { IsFamilyDocument: true, OwnerFamily: not null } && doc.OwnerFamily.FamilyPlacementType == FamilyPlacementType.OneLevelBased)
                    {
                        return (doc, TestOutcome.Ok, Path.GetFileName(template));
                    }

                    doc?.Close(false);
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or IOException)
                {
                    ViridianLog.Warn("Teste 3D: modelo recusado " + template, ex);
                }
            }

            return (null, TestOutcome.Falha, "Nenhum dos modelos baseados em nível pôde ser aberto.");
        });
        if (family is null)
        {
            return string.Empty;
        }

        string file = Path.Combine(folder, FamilyName + ".rfa");
        try
        {
            var request = new PlantRequest
            {
                Profile = Plant3DProfileDefaults.Create().Single(p => p.Id == "ipe"),
                HeightM = TreeHeightM,
                CrownDiameterM = TreeCrownM,
                Quality = QualityLevel.Intermediaria,
                Seed = 7,
                SolidOnly = true,
                Variation = false,
            };
            GeneratedPlant plant = PlantGeometryGenerator.Generate(request);
            report.Add("Fase 0 · geometria do motor (somente sólidos)", TestOutcome.Info,
                $"{plant.Model.Pieces.Count} sólido(s), {plant.Model.TriangleCount} triângulos; perfil ipê, {TreeHeightM} m × copa {TreeCrownM} m.");

            using (var tx = new Transaction(family, "Viridian: teste Fase 0"))
            {
                tx.Start();
                report.Run<bool>("Fase 0 · categoria Vegetação", () =>
                {
                    var planting = new ElementId(BuiltInCategory.OST_Planting);
                    if (family.OwnerFamily.FamilyCategoryId == planting)
                    {
                        return (true, TestOutcome.Ok, "já era Vegetação");
                    }

                    if (!family.OwnerFamily.IsAppropriateCategoryId(planting))
                    {
                        return (false, TestOutcome.Aviso, "o modelo não aceita Vegetação; mantida a categoria do modelo");
                    }

                    family.OwnerFamily.FamilyCategoryId = planting;
                    return (true, TestOutcome.Ok, string.Empty);
                });

                ElementId foliage = FamilyMaterial(family, "VIR Teste Folhagem", new Rgb(0x4E, 0x7D, 0x3A));
                ElementId wood = FamilyMaterial(family, "VIR Teste Tronco", new Rgb(0x6B, 0x4F, 0x3A));

                report.Run<int>("Fase 0 · símbolo 2D (linhas simbólicas)", () =>
                {
                    SketchPlane plane = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
                    double r = RevitUnits.ToFeet(TreeCrownM / 2);
                    var curves = new List<Curve>
                    {
                        Arc.Create(XYZ.Zero, r, 0, Math.PI, XYZ.BasisX, XYZ.BasisY),
                        Arc.Create(XYZ.Zero, r, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY),
                        Line.CreateBound(new XYZ(-r * 0.15, 0, 0), new XYZ(r * 0.15, 0, 0)),
                        Line.CreateBound(new XYZ(0, -r * 0.15, 0), new XYZ(0, r * 0.15, 0)),
                    };
                    int made = curves.Count(curve => family.FamilyCreate.NewSymbolicCurve(curve, plane) is not null);
                    return (made, made == curves.Count ? TestOutcome.Ok : TestOutcome.Falha, $"{made} de {curves.Count} linhas (copa e marca do tronco)");
                });

                // Visível em elevações, cortes e 3D; oculto nas plantas (o símbolo 2D representa a planta).
                var visibility = new FamilyElementVisibility(FamilyElementVisibilityType.Model)
                {
                    IsShownInCoarse = true,
                    IsShownInMedium = true,
                    IsShownInFine = true,
                    IsShownInFrontBack = true,
                    IsShownInLeftRight = true,
                    IsShownInTopBottom = false,
                    IsShownInPlanRCPCut = false,
                };

                report.Run<bool>("Fase 0 · sólido analítico (tronco extrudado)", () =>
                {
                    double r = RevitUnits.ToFeet(0.18);
                    var loop = new CurveLoop();
                    loop.Append(Arc.Create(XYZ.Zero, r, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
                    loop.Append(Arc.Create(XYZ.Zero, r, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
                    Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, RevitUnits.ToFeet(2.0));
                    FreeFormElement element = FreeFormElement.Create(family, solid);
                    element.SetVisibility(visibility);
                    element.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(wood);
                    return (true, TestOutcome.Ok, "forma livre de 2 m com material de tronco");
                });

                report.Run<int>("Fase 0 · sólidos do motor como forma livre", () =>
                {
                    int ok = 0, failed = 0;
                    var reasons = new List<string>();
                    foreach (MeshPiece piece in plant.Model.Pieces)
                    {
                        ElementId material = piece.Part is MeshPart.Tronco or MeshPart.Galho ? wood : foliage;
                        if (TrySolid(piece, material, out Solid? solid, out string reason) && solid is not null)
                        {
                            try
                            {
                                FreeFormElement element = FreeFormElement.Create(family, solid);
                                element.SetVisibility(visibility);
                                element.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(material);
                                ok++;
                                continue;
                            }
                            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                            {
                                reason = "forma livre recusada: " + ex.Message;
                            }
                        }

                        failed++;
                        if (reasons.Count < 5)
                        {
                            reasons.Add($"{piece.Part}: {reason}");
                        }
                    }

                    TestOutcome outcome = failed == 0 ? TestOutcome.Ok : ok > failed ? TestOutcome.Aviso : TestOutcome.Falha;
                    return (ok, outcome, $"{ok} criado(s), {failed} recusado(s)" + (reasons.Count > 0 ? "\n" + string.Join("\n", reasons) : string.Empty));
                });

                tx.Commit();
            }

            report.Run<bool>("Fase 0 · gravar a família", () =>
            {
                using var options = new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 };
                family.SaveAs(file, options);
                long kb = new FileInfo(file).Length / 1024;
                return (true, kb > 20_000 ? TestOutcome.Aviso : TestOutcome.Ok, $"{kb} KB");
            });
        }
        finally
        {
            try
            {
                family.Close(false);
            }
            catch (Exception ex)
            {
                ViridianLog.Warn("Teste 3D: fechar a família", ex);
            }
        }

        return File.Exists(file) ? file : string.Empty;
    }

    /// <summary>
    /// Carrega a família no projeto, insere um exemplar e uma grade de 25, confere a geometria por vista e por nível de
    /// detalhe e recarrega a família. Abre as próprias transações (o chamador agrupa para manter ou desfazer).
    /// </summary>
    public static void PlaceAndCheck(Document document, string familyFile, View? activeView, EngineTestReport report)
    {
        Family? family = null;
        report.Run<bool>("Fase 0 · carregar no projeto", () =>
        {
            using var tx = new Transaction(document, "Viridian: carregar família de teste");
            tx.Start();
            bool loaded = document.LoadFamily(familyFile, new OverwriteFamilyLoadOptions(), out family);
            tx.Commit();
            return (loaded, loaded && family is not null ? TestOutcome.Ok : TestOutcome.Falha, family?.Name ?? "não carregada");
        });
        if (family is null || family.GetFamilySymbolIds().FirstOrDefault() is not ElementId symbolId || document.GetElement(symbolId) is not FamilySymbol symbol)
        {
            return;
        }

        Level? level = (activeView as ViewPlan)?.GenLevel
                       ?? new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => Math.Abs(l.Elevation)).FirstOrDefault();
        if (level is null)
        {
            report.Add("Fase 0 · nível", TestOutcome.Falha, "O projeto não tem níveis.");
            return;
        }

        FamilyInstance? instance = report.Run("Fase 0 · inserir um exemplar", () =>
        {
            using var tx = new Transaction(document, "Viridian: inserir exemplar de teste");
            tx.Start();
            if (!symbol.IsActive)
            {
                symbol.Activate();
            }

            FamilyInstance created = document.Create.NewFamilyInstance(new XYZ(0, 0, 0), symbol, level, StructuralType.NonStructural);
            tx.Commit();
            return (created, TestOutcome.Ok, $"no nível {level.Name}, na origem do projeto");
        });
        if (instance is null)
        {
            return;
        }

        ViewPlan? plan = activeView as ViewPlan
                         ?? new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                             .FirstOrDefault(v => !v.IsTemplate && v.GenLevel?.Id == level.Id && v.ViewType == ViewType.FloorPlan);
        if (plan is not null)
        {
            report.Run<bool>("Fase 0 · planta: só o símbolo 2D", () =>
            {
                (int solids, int curves) = Count(instance, new Options { View = plan });
                bool ok = solids == 0 && curves > 0;
                return (ok, ok ? TestOutcome.Ok : solids > 0 ? TestOutcome.Falha : TestOutcome.Aviso,
                    $"vista \"{plan.Name}\": {curves} linha(s) e {solids} sólido(s) (esperado: linhas e nenhum sólido)");
            });
        }
        else
        {
            report.Add("Fase 0 · planta: só o símbolo 2D", TestOutcome.Aviso, "Nenhuma planta do nível para conferir.");
        }

        foreach (ViewDetailLevel detail in new[] { ViewDetailLevel.Coarse, ViewDetailLevel.Medium, ViewDetailLevel.Fine })
        {
            report.Run<bool>($"Fase 0 · 3D no nível de detalhe {detail}", () =>
            {
                (int solids, int curves) = Count(instance, new Options { DetailLevel = detail });
                return (solids > 0, solids > 0 ? TestOutcome.Ok : TestOutcome.Falha, $"{solids} sólido(s), {curves} linha(s)");
            });
        }

        report.Run<bool>("Fase 0 · altura do exemplar", () =>
        {
            BoundingBoxXYZ? box = instance.get_BoundingBox(null);
            if (box is null)
            {
                return (false, TestOutcome.Falha, "sem caixa envolvente");
            }

            double height = RevitUnits.ToMeters(box.Max.Z - box.Min.Z);
            bool ok = Math.Abs(height - TreeHeightM) < TreeHeightM * 0.3;
            return (ok, ok ? TestOutcome.Ok : TestOutcome.Aviso, $"{height:0.00} m (esperado cerca de {TreeHeightM} m)");
        });

        report.Run<int>("Fase 0 · 25 exemplares (desempenho)", () =>
        {
            var watch = Stopwatch.StartNew();
            using var tx = new Transaction(document, "Viridian: grade de teste");
            tx.Start();
            int made = 0;
            for (int i = 0; i < 5; i++)
            {
                for (int j = 0; j < 5; j++)
                {
                    if (i == 0 && j == 0)
                    {
                        continue;
                    }

                    document.Create.NewFamilyInstance(new XYZ(RevitUnits.ToFeet(i * 8), RevitUnits.ToFeet(j * 8), 0), symbol, level, StructuralType.NonStructural);
                    made++;
                }
            }

            document.Regenerate();
            tx.Commit();
            return (made + 1, TestOutcome.Ok, $"{made + 1} exemplares em {watch.ElapsedMilliseconds} ms (inclui regenerar)");
        });

        report.Run<bool>("Fase 0 · recarregar a família", () =>
        {
            using var tx = new Transaction(document, "Viridian: recarregar família de teste");
            tx.Start();
            document.LoadFamily(familyFile, new OverwriteFamilyLoadOptions(), out Family reloaded);
            tx.Commit();
            bool alive = instance.IsValidObject && instance.Symbol?.Family?.Name == family.Name;
            return (alive, alive ? TestOutcome.Ok : TestOutcome.Falha, alive ? "exemplares mantidos" : "o exemplar se perdeu ao recarregar");
        });

        report.Add("Fase 0 · exemplares da família no projeto", TestOutcome.Info,
            new FilteredElementCollector(document).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Count(f => f.Symbol?.Family?.Id == family.Id) + " exemplar(es)");
    }

    // ---- T0: texturas ------------------------------------------------------------------------------------------

    /// <summary>
    /// Gera as texturas do ipê na pasta, cria aparências e materiais com elas, um ipê e duas peças de referência, e a
    /// vista realista de conferência (exportada em PNG). Abre as próprias transações.
    /// </summary>
    public static View3D? TextureTest(RevitApplication app, Document document, string folder, EngineTestReport report)
    {
        string textures = Path.Combine(folder, "texturas");
        report.Run<bool>("T0 · texturas procedurais", () =>
        {
            (RgbaImage leaf, RgbaImage leafMask) = ProceduralTextures.Foliage(1024, new Rgb(0x4E, 0x7D, 0x3A), 11);
            PngWriter.Save(leaf, Path.Combine(textures, "ipe-folhagem.png"));
            PngWriter.Save(leafMask, Path.Combine(textures, "ipe-folhagem-recorte.png"));
            (RgbaImage flower, RgbaImage flowerMask) = ProceduralTextures.Flowers(1024, new Rgb(0xF2, 0xC2, 0x1E), 12);
            PngWriter.Save(flower, Path.Combine(textures, "ipe-flor.png"));
            PngWriter.Save(flowerMask, Path.Combine(textures, "ipe-flor-recorte.png"));
            PngWriter.Save(ProceduralTextures.Bark(1024, new Rgb(0x6E, 0x63, 0x58), 13), Path.Combine(textures, "ipe-casca.png"));
            return (true, TestOutcome.Ok, $"5 imagens de 1024 px em {textures} (folhas cobrem {ProceduralTextures.Coverage(leafMask):P0} da máscara)");
        });

        Asset? template = report.Run("T0 · aparência genérica de partida", () =>
        {
            Asset? generic = app.GetAssets(AssetType.Appearance)
                .Where(a => a.FindByName(Generic.GenericDiffuse) is not null)
                .OrderByDescending(a => a.Name.Contains("Generic", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("Genérico", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            return generic is null
                ? (null, TestOutcome.Falha, "A biblioteca de aparências do Revit não tem um material genérico disponível.")
                : (generic, TestOutcome.Ok, generic.Name);
        });

        var materials = new Dictionary<string, ElementId>();
        View3D? view = null;
        using (var tx = new Transaction(document, "Viridian: teste T0"))
        {
            tx.Start();
            materials["folhagem"] = TexturedMaterial(document, template, "Folhagem ipê", new Rgb(0x4E, 0x7D, 0x3A),
                Path.Combine(textures, "ipe-folhagem.png"), Path.Combine(textures, "ipe-folhagem-recorte.png"), 1.2, report);
            materials["flor"] = TexturedMaterial(document, template, "Flor ipê", new Rgb(0xF2, 0xC2, 0x1E),
                Path.Combine(textures, "ipe-flor.png"), Path.Combine(textures, "ipe-flor-recorte.png"), 0.9, report);
            materials["casca"] = TexturedMaterial(document, template, "Casca ipê", new Rgb(0x6E, 0x63, 0x58),
                Path.Combine(textures, "ipe-casca.png"), null, 0.8, report);

            var shapes = new List<ElementId>();
            report.Run<int>("T0 · ipê com texturas (forma direta)", () =>
            {
                var request = new PlantRequest
                {
                    Profile = Plant3DProfileDefaults.Create().Single(p => p.Id == "ipe"),
                    HeightM = TreeHeightM,
                    CrownDiameterM = TreeCrownM,
                    Quality = QualityLevel.Detalhada,
                    Seed = 7,
                    Flowers = true,
                    Variation = false,
                };
                GeneratedPlant plant = PlantGeometryGenerator.Generate(request);
                var offset = new Vec3(-12, 0, 0);
                ElementId Material(MeshPart part) => part switch
                {
                    MeshPart.Flor => materials["flor"],
                    MeshPart.Tronco or MeshPart.Galho => materials["casca"],
                    _ => materials["folhagem"],
                };
                DirectShape? tree = Shape(document, plant.Model.Pieces.Select(p => p.Transform(0, offset)).ToList(), Material, "Teste T0 - ipê");
                if (tree is not null)
                {
                    shapes.Add(tree.Id);
                }

                // Peças de referência: um grupo de folhagem grande e um cubo de casca (para ver a escala e o recorte).
                var cluster = new List<MeshPiece> { MeshBuild.Cluster(MeshPart.Folhagem, new Vec3(-4, 0, 1.6), 1.5, 0.9, 10, 7, 3, 0.12) };
                DirectShape? reference = Shape(document, cluster, Material, "Teste T0 - grupo de folhagem");
                if (reference is not null)
                {
                    shapes.Add(reference.Id);
                }

                var cube = new List<MeshPiece> { Primitives.Frustum(MeshPart.Tronco, new Vec3(-4, 4, 0), 1, 0.7, 0.7, 4) };
                DirectShape? bark = Shape(document, cube, Material, "Teste T0 - bloco de casca");
                if (bark is not null)
                {
                    shapes.Add(bark.Id);
                }

                return (shapes.Count, shapes.Count == 3 ? TestOutcome.Ok : TestOutcome.Falha, $"{shapes.Count} de 3 formas criadas; ipê com {plant.Model.TriangleCount} triângulos");
            });

            view = report.Run("T0 · vista realista", () =>
            {
                ViewFamilyType? type = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                if (type is null)
                {
                    return (null, TestOutcome.Falha, "O projeto não tem tipo de vista 3D.");
                }

                View3D created = View3D.CreateIsometric(document, type.Id);
                created.ViewTemplateId = ElementId.InvalidElementId;
                try
                {
                    created.Name = TextureViewName + " " + DateTime.Now.ToString("HHmmss");
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // nome repetido: fica o padrão do Revit
                }

                created.DetailLevel = ViewDetailLevel.Fine;
                created.DisplayStyle = DisplayStyle.Realistic;
                created.SetSectionBox(new BoundingBoxXYZ
                {
                    Min = new XYZ(RevitUnits.ToFeet(-17), RevitUnits.ToFeet(-5), RevitUnits.ToFeet(-0.5)),
                    Max = new XYZ(RevitUnits.ToFeet(-1), RevitUnits.ToFeet(6), RevitUnits.ToFeet(TreeHeightM + 1)),
                });
                created.IsSectionBoxActive = true;
                return (created, created.DisplayStyle == DisplayStyle.Realistic ? TestOutcome.Ok : TestOutcome.Aviso, created.Name + " (estilo Realista)");
            });

            tx.Commit();
        }

        if (view is not null)
        {
            report.Run<int>("T0 · imagem da vista realista", () =>
            {
                int exported = SolarService.ExportImages(document, new List<View> { view }, folder);
                return (exported, exported > 0 ? TestOutcome.Ok : TestOutcome.Falha, exported > 0 ? "PNG gravado na pasta do teste" : "o Revit não exportou a imagem");
            });
        }

        return view;
    }

    private static ElementId TexturedMaterial(Document document, Asset? template, string label, Rgb color, string bitmap, string? cutout, double sizeM, EngineTestReport report)
    {
        string name = "VIR Teste T0 - " + label;
        ElementId id = Material.Create(document, UniqueMaterialName(document, name));
        var material = (Material)document.GetElement(id);
        material.Color = HatchService.ToColor(color);
        if (template is null)
        {
            report.Add($"T0 · aparência \"{label}\"", TestOutcome.Falha, "sem aparência genérica: material só com cor");
            return id;
        }

        report.Run<bool>($"T0 · aparência \"{label}\"", () =>
        {
            AppearanceAssetElement asset = AppearanceAssetElement.Create(document, UniqueAssetName(document, name), template);
            material.AppearanceAssetId = asset.Id;
            material.UseRenderAppearanceForShading = true;
            var notes = new List<string>();
            using (var scope = new AppearanceAssetEditScope(document))
            {
                Asset editable = scope.Start(asset.Id);
                notes.Add(AppearanceTextures.ConnectBitmap(editable.FindByName(Generic.GenericDiffuse), bitmap, sizeM, "cor (difusa)"));
                if (cutout is not null)
                {
                    notes.Add(AppearanceTextures.ConnectBitmap(editable.FindByName(Generic.GenericCutoutOpacity), cutout, sizeM, "recorte"));
                }

                scope.Commit(true);
            }

            bool failed = notes.Any(n => n.Contains("não", StringComparison.Ordinal));
            return (!failed, failed ? TestOutcome.Aviso : TestOutcome.Ok, string.Join("; ", notes));
        });

        return id;
    }

    private static DirectShape? Shape(Document document, IReadOnlyList<MeshPiece> pieces, Func<MeshPart, ElementId> material, string name)
    {
        var builder = new TessellatedShapeBuilder { Target = TessellatedShapeBuilderTarget.AnyGeometry, Fallback = TessellatedShapeBuilderFallback.Mesh };
        foreach (MeshPiece piece in pieces.Where(p => p.TriangleCount > 0))
        {
            ElementId id = material(piece.Part);
            builder.OpenConnectedFaceSet(piece.Closed);
            foreach (Tri t in piece.Triangles)
            {
                (Vec3 a, Vec3 b, Vec3 c) = piece.Corners(t);
                builder.AddFace(new TessellatedFace(new List<XYZ> { Feet(a), Feet(b), Feet(c) }, id));
            }

            builder.CloseConnectedFaceSet();
        }

        builder.Build();
        using TessellatedShapeBuilderResult result = builder.GetBuildResult();
        if (result.Outcome == TessellatedShapeBuilderOutcome.Nothing || !result.AreObjectsAvailable)
        {
            return null;
        }

        var categoryId = new ElementId(BuiltInCategory.OST_Entourage);
        DirectShape shape = DirectShape.CreateElement(document, DirectShape.IsValidCategoryId(categoryId, document) ? categoryId : new ElementId(BuiltInCategory.OST_GenericModel));
        shape.ApplicationId = ShapeApplicationId;
        shape.ApplicationDataId = name;
        shape.SetShape(result.GetGeometricalObjects());
        shape.SetName(name);
        return shape;
    }

    /// <summary>Malha fechada do motor convertida em sólido do Revit (necessário para a forma livre da família).</summary>
    private static bool TrySolid(MeshPiece piece, ElementId material, out Solid? solid, out string reason)
    {
        solid = null;
        reason = string.Empty;
        if (!piece.Closed)
        {
            reason = "superfície aberta";
            return false;
        }

        try
        {
            var builder = new TessellatedShapeBuilder { Target = TessellatedShapeBuilderTarget.Solid, Fallback = TessellatedShapeBuilderFallback.Abort };
            builder.OpenConnectedFaceSet(true);
            foreach (Tri t in piece.Triangles)
            {
                (Vec3 a, Vec3 b, Vec3 c) = piece.Corners(t);
                builder.AddFace(new TessellatedFace(new List<XYZ> { Feet(a), Feet(b), Feet(c) }, material));
            }

            builder.CloseConnectedFaceSet();
            builder.Build();
            using TessellatedShapeBuilderResult result = builder.GetBuildResult();
            solid = result.AreObjectsAvailable ? result.GetGeometricalObjects().OfType<Solid>().FirstOrDefault(s => s.Volume > 0) : null;
            reason = solid is null ? "não formou sólido (" + result.Outcome + ")" : string.Empty;
            return solid is not null;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    internal static (int Solids, int Curves) Count(Element element, Options options)
    {
        int solids = 0, curves = 0;
        void Walk(GeometryElement? geometry)
        {
            if (geometry is null)
            {
                return;
            }

            foreach (GeometryObject item in geometry)
            {
                switch (item)
                {
                    case Solid s when s.Volume > 0:
                        solids++;
                        break;
                    case Curve:
                        curves++;
                        break;
                    case GeometryInstance instance:
                        Walk(instance.GetInstanceGeometry());
                        break;
                }
            }
        }

        Walk(element.get_Geometry(options));
        return (solids, curves);
    }

    private static ElementId FamilyMaterial(Document family, string name, Rgb color)
    {
        Material? existing = new FilteredElementCollector(family).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => m.Name == name);
        if (existing is not null)
        {
            return existing.Id;
        }

        ElementId id = Material.Create(family, name);
        ((Material)family.GetElement(id)).Color = HatchService.ToColor(color);
        return id;
    }

    private static string UniqueMaterialName(Document document, string name)
    {
        var names = new HashSet<string>(new FilteredElementCollector(document).OfClass(typeof(Material)).Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
        string candidate = name;
        for (int i = 2; names.Contains(candidate) && i < 1000; i++)
        {
            candidate = $"{name} ({i})";
        }

        return candidate;
    }

    private static string UniqueAssetName(Document document, string name)
    {
        string candidate = name;
        for (int i = 2; AppearanceAssetElement.GetAppearanceAssetElementByName(document, candidate) is not null && i < 1000; i++)
        {
            candidate = $"{name} ({i})";
        }

        return candidate;
    }

    private static XYZ Feet(Vec3 v) => new(RevitUnits.ToFeet(v.X), RevitUnits.ToFeet(v.Y), RevitUnits.ToFeet(v.Z));

    private static string Safe(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            return "? (" + ex.GetType().Name + ")";
        }
    }
}
