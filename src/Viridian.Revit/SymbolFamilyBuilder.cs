using System.IO;
using Autodesk.Revit.DB;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Geometry;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Illustration;
using Viridian.Core.Symbols;
using Viridian.Library;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Viridian.Revit;

/// <summary>O modelo de família aberto não serve para gerar símbolos de vegetação.</summary>
public sealed class TemplateRejectedException : Exception
{
    public TemplateRejectedException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Cria famílias da categoria Vegetação contendo somente elementos 2D de planta: linhas simbólicas
/// e regiões preenchidas (copa colorida e manchas de floração).
/// Cada camada do símbolo vai para uma subcategoria, o que permite ao gerenciador de escalas
/// ligar e desligar o detalhamento por vista sem alterar a família nem o exemplar.
/// </summary>
public static class SymbolFamilyBuilder
{
    /// <summary>Versão da montagem da família. Ao aumentá-la, as famílias geradas são refeitas.</summary>
    public const int Version = 6;

    /// <summary>Espécies cujas cores o Revit recusou nesta sessão (para avisar o usuário uma única vez).</summary>
    public static List<string> ColorFailures { get; } = new();

    /// <summary>Motivos da última recusa das cores (mostrados no aviso ao usuário).</summary>
    public static string LastColorFailureReason { get; private set; } = string.Empty;

    /// <summary>Gera o arquivo .rfa de um símbolo. Não pode haver transação aberta em nenhum documento.</summary>
    /// <param name="paint">Cores da copa e da floração.</param>
    /// <param name="typeName">Nome do tipo gravado na família (mantém o mesmo tipo quando a família é recarregada).</param>
    /// <returns>Quantidade de linhas que o Revit recusou (zero em condições normais).</returns>
    /// <param name="volume">Cartões ilustrados da vegetação 3D (nulo: só o símbolo 2D).</param>
    /// <param name="volumeResult">Recebe o que aconteceu com os cartões (quando informado).</param>
    public static int Build(RevitApplication app, string templateFile, PlantSymbol symbol, SymbolPaint paint, string outputFile, ViridianSettings settings, string typeName,
        PlantCards? volume = null, VolumeBuildResult? volumeResult = null)
    {
        Document familyDocument;
        try
        {
            familyDocument = app.NewFamilyDocument(templateFile);
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or IOException)
        {
            throw new TemplateRejectedException("O modelo não pôde ser aberto: " + ex.Message);
        }

        if (familyDocument is null)
        {
            throw new TemplateRejectedException("O modelo não pôde ser aberto.");
        }

        try
        {
            int skipped;
            try
            {
                skipped = Draw(app, familyDocument, symbol, paint, settings, typeName);
                if (volume is not null)
                {
                    DrawCards(familyDocument, volume, volumeResult ?? new VolumeBuildResult());
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                // Qualquer recusa do Revit ao montar o símbolo invalida apenas este modelo; o próximo é tentado.
                throw new TemplateRejectedException(ex.Message);
            }

            string? folder = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using var options = new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 };
            familyDocument.SaveAs(outputFile, options);
            return skipped;
        }
        finally
        {
            try
            {
                familyDocument.Close(false);
            }
            catch (Exception ex)
            {
                ViridianLog.Warn("Falha ao fechar o documento de família temporário.", ex);
            }
        }
    }

    private static int Draw(RevitApplication app, Document familyDocument, PlantSymbol symbol, SymbolPaint paint, ViridianSettings settings, string typeName)
    {
        if (!familyDocument.IsFamilyDocument || familyDocument.OwnerFamily is null)
        {
            throw new TemplateRejectedException("O arquivo não é um modelo de família.");
        }

        // Etapa 1: categoria Vegetação e subcategorias das camadas.
        var subcategories = new Dictionary<SymbolTier, Category>();
        RunSilently(familyDocument, "Viridian: camadas", required: true, () =>
        {
            Family owner = familyDocument.OwnerFamily;
            if (owner.FamilyPlacementType != FamilyPlacementType.OneLevelBased)
            {
                throw new TemplateRejectedException("O modelo não é baseado em nível.");
            }

            var plantingId = new ElementId(BuiltInCategory.OST_Planting);
            if (owner.FamilyCategoryId != plantingId)
            {
                if (!owner.IsAppropriateCategoryId(plantingId))
                {
                    throw new TemplateRejectedException("O modelo não aceita a categoria Vegetação.");
                }

                owner.FamilyCategoryId = plantingId;
                familyDocument.Regenerate();
            }

            Category planting = owner.FamilyCategory
                ?? throw new TemplateRejectedException("A categoria Vegetação não está disponível no modelo.");
            foreach (SymbolTier tier in TierVisibility.AllTiers)
            {
                Category subcategory = GetOrCreateSubcategory(familyDocument, planting, TierVisibility.SubcategoryName(tier));
                if (tier < SymbolTier.Fill)
                {
                    ApplyStyle(subcategory, settings.StyleOf(tier));
                }
            }
        });

        // Os objetos de categoria são relidos depois da confirmação da etapa.
        Category plantingCategory = familyDocument.OwnerFamily.FamilyCategory!;
        foreach (SymbolTier tier in TierVisibility.AllTiers)
        {
            string name = TierVisibility.SubcategoryName(tier);
            if (plantingCategory.SubCategories.Contains(name))
            {
                subcategories[tier] = plantingCategory.SubCategories.get_Item(name);
            }
        }

        double elevation = ReferenceLevelElevation(familyDocument);
        double tolerance = app.ShortCurveTolerance * 1.05;
        Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, elevation));

        // Etapa 2: cores (copa e floração). Três métodos, do mais ao menos robusto: (1) superfícies finas com material
        // colorido (só extrusão e material, aceitos por qualquer família de modelo); (2) componente de detalhe aninhado
        // com regiões preenchidas; (3) regiões preenchidas no plano do nível. São opcionais: se todos falharem, o
        // símbolo segue só com as linhas e o usuário é avisado com o motivo.
        if (paint.HasFill)
        {
            var reasons = new List<string>();
            bool colored = RunSilently(familyDocument, "Viridian: cores do símbolo (superfícies)", required: false, () =>
            {
                if (SurfaceColors.Draw(familyDocument, symbol, paint, elevation, tolerance, subcategories, reasons) == 0)
                {
                    throw new ViridianException("nenhuma superfície colorida foi criada");
                }
            }, reasons);

            if (!colored)
            {
                colored = NestedColorComponent.TryDraw(app, familyDocument, symbol, paint, elevation, tolerance, reasons);
            }

            if (!colored)
            {
                int before = FamilyFills.CountRegions(familyDocument);
                colored = RunSilently(familyDocument, "Viridian: cores do símbolo (regiões)", required: false, () =>
                {
                    SketchPlane fillPlane = SketchPlane.Create(familyDocument, plane);
                    FamilyFills.Draw(familyDocument, symbol, paint, fillPlane, elevation, tolerance, subcategories);
                }, reasons) && FamilyFills.CountRegions(familyDocument) > before;
            }

            if (!colored)
            {
                string why = string.Join("; ", reasons.Distinct().Take(6));
                ViridianLog.Warn($"O símbolo \"{typeName}\" foi gerado sem as cores. Motivos: {why}");
                lock (ColorFailures)
                {
                    ColorFailures.Add(typeName);
                    LastColorFailureReason = why;
                }
            }
        }

        // Etapa 3: linhas simbólicas e tipo.
        int skipped = 0;
        // As linhas ficam logo acima das camadas de cor (6 mm), para nunca serem encobertas por elas em planta.
        Plane linePlane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, elevation + 3 * SurfaceColors.LayerFeet));
        RunSilently(familyDocument, "Viridian: símbolo 2D", required: true, () =>
        {
            SketchPlane sketchPlane = SketchPlane.Create(familyDocument, linePlane);
            foreach (SymbolPrimitive primitive in symbol.Primitives)
            {
                Curve? curve = ToCurve(primitive, elevation + 3 * SurfaceColors.LayerFeet, tolerance);
                if (curve is null)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    SymbolicCurve symbolic = familyDocument.FamilyCreate.NewSymbolicCurve(curve, sketchPlane);
                    if (subcategories.TryGetValue(primitive.Tier, out Category? subcategory)
                        && subcategory.GetGraphicsStyle(GraphicsStyleType.Projection) is GraphicsStyle style)
                    {
                        symbolic.Subcategory = style;
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    skipped++;
                }
            }

            if (skipped > symbol.Primitives.Count / 2)
            {
                throw new TemplateRejectedException("O modelo recusou a maior parte das linhas do símbolo.");
            }

            SetSingleType(familyDocument, typeName);
        });

        return skipped;
    }

    /// <summary>Subcategoria do volume 3D (liga e desliga o 3D por vista em Visibilidade/Gráficos).</summary>
    public const string VolumeSubcategory = "VIR Volume 3D";

    /// <summary>Parâmetro de exemplar que mostra ou oculta o volume 3D (a maquete o desliga onde usa forma direta).</summary>
    public const string VisibleParameter = "VIR 3D visível";

    /// <summary>Parâmetro de exemplar com a fase mostrada em 3D (1 plantio, 2 3 anos, 3 6 anos, 4 adulta, 5 adulta sem folhas).</summary>
    public const string StageParameter = "VIR Fase 3D";

    /// <summary>
    /// Espessura do cartão (m). O contorno preto fica um pouco mais fino (recuado) e cada cor ganha um décimo de
    /// milímetro a mais conforme a ordem: onde duas regiões se encostam com sobreposição mínima, sempre há uma na
    /// frente, sem faces coplanares disputando a mesma posição.
    /// </summary>
    private static double CardThicknessM(int colorIndex) => colorIndex == CardArt.Black ? 0.003 : 0.004 + 0.0002 * Math.Min(colorIndex, 30);

    /// <summary>
    /// Etapa opcional da vegetação ilustrada: para cada fase, os dois planos cruzados viram extrusões finas, uma por
    /// cor (o contorno preto é uma delas), na subcategoria do volume 3D, ocultas em planta e visíveis em 3D, cortes e
    /// elevações. A fase de cada exemplar é escolhida pelo parâmetro "VIR Fase 3D". Se o Revit recusar tudo, a família
    /// fica só com o símbolo 2D, como antes.
    /// </summary>
    private static void DrawCards(Document familyDocument, PlantCards cards, VolumeBuildResult result)
    {
        var reasons = new List<string>();
        bool committed = RunSilently(familyDocument, "Viridian: cartões 3D", required: false, () =>
        {
            Category planting = familyDocument.OwnerFamily.FamilyCategory ?? throw new ViridianException("família sem categoria");
            Category subcategory = GetOrCreateSubcategory(familyDocument, planting, VolumeSubcategory);
            FamilyManager manager = familyDocument.FamilyManager;
            FamilyParameter? visible = InstanceParameter(manager, VisibleParameter, SpecTypeId.Boolean.YesNo, 1, result);
            FamilyParameter? stage = InstanceParameter(manager, StageParameter, SpecTypeId.Int.Integer, PlantCardsPlanner.DefaultStage, result);
            double elevation = ReferenceLevelElevation(familyDocument);
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
            var materials = new Dictionary<string, ElementId>(StringComparer.Ordinal);
            ElementId MaterialOf(Rgb color)
            {
                string name = "VIR Cartão " + color.Hex;
                if (!materials.TryGetValue(name, out ElementId? id))
                {
                    Material? existing = new FilteredElementCollector(familyDocument).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => m.Name == name);
                    id = existing?.Id ?? Material.Create(familyDocument, name);
                    if (existing is null && familyDocument.GetElement(id) is Material created)
                    {
                        created.Color = new Color(color.R, color.G, color.B);
                        created.Shininess = 0;
                        created.Smoothness = 0;
                    }

                    materials[name] = id;
                }

                return id;
            }

            var forms = new List<GenericForm>();
            foreach (CardSet set in cards.Sets)
            {
                FamilyParameter? shown = SetParameter(manager, set, visible, stage, result);
                foreach ((CardRegions regions, XYZ right, XYZ normal) in new[]
                         {
                             (set.PlaneA, XYZ.BasisY, XYZ.BasisX),
                             (set.PlaneB, -XYZ.BasisX, XYZ.BasisY),
                         })
                {
                    foreach (ColorRegion region in regions.Regions)
                    {
                        ElementId material = MaterialOf(region.Color);
                        foreach (Extrusion form in Extrude(familyDocument, region.Loops, right, normal, elevation, CardThicknessM(region.ColorIndex), result))
                        {
                            try
                            {
                                form.SetVisibility(visibility);
                                form.Subcategory = subcategory;
                                form.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(material);
                                if (shown is not null && form.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM) is Parameter isVisible && manager.CanElementParameterBeAssociated(isVisible))
                                {
                                    manager.AssociateElementParameterToFamilyParameter(isVisible, shown);
                                    result.Associated++;
                                }

                                forms.Add(form);
                            }
                            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                            {
                                AddReason(result, "ajuste do cartão recusado: " + ex.Message);
                            }
                        }
                    }
                }
            }

            if (forms.Count == 0)
            {
                throw new ViridianException("nenhum cartão aceito (" + string.Join("; ", result.Reasons.Take(3)) + ")");
            }

            result.HiddenInPlan = forms.All(f => f.GetVisibility() is { IsShownInTopBottom: false, IsShownInPlanRCPCut: false });
            result.Created = forms.Count;
            result.HasVisibleParameter = visible is not null;
        }, reasons);

        if (!committed)
        {
            result.Created = 0;
            result.HiddenInPlan = false;
            foreach (string reason in reasons)
            {
                AddReason(result, reason);
            }
        }
    }

    private static FamilyParameter? InstanceParameter(FamilyManager manager, string name, ForgeTypeId spec, int value, VolumeBuildResult result)
    {
        try
        {
            FamilyParameter parameter = manager.get_Parameter(name) ?? manager.AddParameter(name, GroupTypeId.Graphics, spec, true);
            if (manager.CurrentType is not null)
            {
                manager.Set(parameter, value);
            }

            return parameter;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            result.Notes.Add($"parâmetro \"{name}\" não criado: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Visibilidade de uma fase: "VIR 3D &lt;fase&gt;" = e(VIR 3D visível; VIR Fase 3D = código). Sem fórmula (se o Revit
    /// recusar), fica um sim/não simples, ligado só na fase adulta.
    /// </summary>
    private static FamilyParameter? SetParameter(FamilyManager manager, CardSet set, FamilyParameter? visible, FamilyParameter? stage, VolumeBuildResult result)
    {
        string name = "VIR 3D " + set.Label;
        FamilyParameter? parameter = null;
        try
        {
            parameter = manager.get_Parameter(name) ?? manager.AddParameter(name, GroupTypeId.Graphics, SpecTypeId.Boolean.YesNo, true);
            if (visible is not null && stage is not null)
            {
                manager.SetFormula(parameter, $"and({VisibleParameter}, {StageParameter} = {set.Code})");
                return parameter;
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            result.Notes.Add($"fórmula da fase \"{set.Label}\" recusada: {ex.Message}");
        }

        if (parameter is not null && manager.CurrentType is not null)
        {
            try
            {
                manager.Set(parameter, set.Code == PlantCardsPlanner.DefaultStage ? 1 : 0);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Notes.Add($"fase \"{set.Label}\": {ex.Message}");
            }
        }

        return parameter;
    }

    /// <summary>
    /// Extrusão fina de uma região (todos os contornos num só perfil). Se o Revit recusar o perfil inteiro, tenta cada
    /// contorno externo com os seus furos; e, por último, só o contorno externo.
    /// </summary>
    private static List<Extrusion> Extrude(Document familyDocument, List<List<Point2>> loops, XYZ right, XYZ normal, double elevation, double thicknessM, VolumeBuildResult result)
    {
        double half = RevitUnits.ToFeet(thicknessM) / 2;
        XYZ offset = normal * -half + new XYZ(0, 0, elevation);
        SketchPlane plane = SketchPlane.Create(familyDocument, Plane.CreateByNormalAndOrigin(normal, offset));
        XYZ At(Point2 p) => right * RevitUnits.ToFeet(p.X) + XYZ.BasisZ * RevitUnits.ToFeet(p.Y) + offset;
        Extrusion? Make(IEnumerable<List<Point2>> group)
        {
            var profile = new CurveArrArray();
            foreach (List<Point2> loop in group)
            {
                var curves = new CurveArray();
                for (int i = 0; i < loop.Count; i++)
                {
                    curves.Append(Line.CreateBound(At(loop[i]), At(loop[(i + 1) % loop.Count])));
                }

                profile.Append(curves);
            }

            try
            {
                return familyDocument.FamilyCreate.NewExtrusion(true, profile, plane, 2 * half);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                AddReason(result, "extrusão recusada: " + ex.Message);
                result.Failed++;
                return null;
            }
        }

        var made = new List<Extrusion>();
        if (Make(loops) is Extrusion whole)
        {
            made.Add(whole);
            return made;
        }

        // Externos (anti-horários) com os furos que estão dentro deles.
        List<List<Point2>> outers = loops.Where(l => SignedArea(l) > 0).ToList();
        List<List<Point2>> holes = loops.Where(l => SignedArea(l) <= 0).ToList();
        foreach (List<Point2> outer in outers)
        {
            var group = new List<List<Point2>> { outer };
            group.AddRange(holes.Where(h => Inside(outer, h[0])));
            Extrusion? piece = Make(group) ?? Make(new[] { outer });
            if (piece is not null)
            {
                made.Add(piece);
            }
        }

        return made;
    }

    private static double SignedArea(List<Point2> loop)
    {
        double a = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            Point2 p = loop[i], q = loop[(i + 1) % loop.Count];
            a += p.X * q.Y - q.X * p.Y;
        }

        return a / 2;
    }

    private static bool Inside(List<Point2> polygon, Point2 point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)
                && point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static void AddReason(VolumeBuildResult result, string reason)
    {
        if (!string.IsNullOrWhiteSpace(reason) && result.Reasons.Count < 8 && !result.Reasons.Contains(reason))
        {
            result.Reasons.Add(reason);
        }
    }

    /// <summary>
    /// Executa uma etapa em transação própria, sem nunca abrir caixa de erro do Revit: avisos são descartados e,
    /// se houver erro, a etapa é desfeita. Nas etapas obrigatórias o erro rejeita o modelo (o próximo é tentado);
    /// nas opcionais apenas devolve falso.
    /// </summary>
    private static bool RunSilently(Document document, string name, bool required, Action action, List<string>? reasons = null)
    {
        var failures = new SilentFailures();
        using var transaction = new Transaction(document, name);
        FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(failures);
        options.SetClearAfterRollback(true);
        options.SetForcedModalHandling(false);
        transaction.SetFailureHandlingOptions(options);
        transaction.Start();
        try
        {
            action();
        }
        catch (Exception ex) when (!required && ex is Autodesk.Revit.Exceptions.ApplicationException or ViridianException or InvalidOperationException)
        {
            transaction.RollBack();
            ViridianLog.Warn(name + ": etapa desfeita.", ex);
            reasons?.Add(ex.Message);
            return false;
        }
        catch
        {
            if (transaction.HasStarted() && !transaction.HasEnded())
            {
                transaction.RollBack();
            }

            throw;
        }

        if (transaction.Commit() == TransactionStatus.Committed)
        {
            return true;
        }

        string reason = failures.Errors.Count > 0 ? string.Join(" ", failures.Errors) : "o Revit não confirmou a etapa";
        if (required)
        {
            throw new TemplateRejectedException(reason);
        }

        ViridianLog.Warn(name + ": etapa desfeita (" + reason + ").");
        reasons?.Add(reason);
        return false;
    }

    /// <summary>Descarta avisos e, havendo erros, desfaz a transação sem mostrar mensagem.</summary>
    private sealed class SilentFailures : IFailuresPreprocessor
    {
        public List<string> Errors { get; } = new();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            bool hasError = false;
            foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                else
                {
                    hasError = true;
                    Errors.Add(failure.GetDescriptionText());
                }
            }

            return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }

    /// <summary>Deixa a família com um único tipo, com o nome informado.</summary>
    private static void SetSingleType(Document familyDocument, string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return;
        }

        FamilyManager manager = familyDocument.FamilyManager;
        if (manager.CurrentType is null || manager.Types.IsEmpty)
        {
            manager.NewType(typeName);
            return;
        }

        if (!string.Equals(manager.CurrentType.Name, typeName, StringComparison.Ordinal))
        {
            manager.RenameCurrentType(typeName);
        }
    }

    private static Category GetOrCreateSubcategory(Document document, Category parent, string name)
    {
        if (parent.SubCategories.Contains(name))
        {
            return parent.SubCategories.get_Item(name);
        }

        return document.Settings.Categories.NewSubcategory(parent, name);
    }

    /// <summary>Aplica espessura e cor de linha a uma subcategoria.</summary>
    public static void ApplyStyle(Category subcategory, TierStyle style)
    {
        subcategory.SetLineWeight(Math.Clamp(style.LineWeight, 1, 16), GraphicsStyleType.Projection);
        if (ColorHex.TryParse(style.Color, out byte r, out byte g, out byte b))
        {
            subcategory.LineColor = new Color(r, g, b);
        }
    }

    private static double ReferenceLevelElevation(Document familyDocument)
    {
        using var collector = new FilteredElementCollector(familyDocument);
        Level? level = collector.OfClass(typeof(Level)).Cast<Level>().OrderBy(l => Math.Abs(l.Elevation)).FirstOrDefault();
        return level?.Elevation ?? 0;
    }

    private static Curve? ToCurve(SymbolPrimitive primitive, double z, double tolerance)
    {
        try
        {
            switch (primitive)
            {
                case SymbolLine line:
                {
                    XYZ a = RevitUnits.ToXyz(line.A, z);
                    XYZ b = RevitUnits.ToXyz(line.B, z);
                    return a.DistanceTo(b) < tolerance ? null : Line.CreateBound(a, b);
                }

                case SymbolArc arc:
                {
                    XYZ start = RevitUnits.ToXyz(arc.Start, z);
                    XYZ mid = RevitUnits.ToXyz(arc.Mid, z);
                    XYZ end = RevitUnits.ToXyz(arc.End, z);
                    if (start.DistanceTo(end) < tolerance || start.DistanceTo(mid) < tolerance || mid.DistanceTo(end) < tolerance)
                    {
                        return null;
                    }

                    // Arcos quase retos geram raios enormes; nesses casos uma reta representa o mesmo traço.
                    return ArcInfo.TryFromThreePoints(arc.Start, arc.Mid, arc.End, out ArcInfo info) && RevitUnits.ToFeet(info.Radius) < 10000
                        ? Arc.Create(start, end, mid)
                        : Line.CreateBound(start, end);
                }

                default:
                    return null;
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }
}

/// <summary>O que aconteceu com os cartões 3D ao montar a família.</summary>
public sealed class VolumeBuildResult
{
    /// <summary>A família foi montada com volume nesta chamada (falso quando veio do cache).</summary>
    public bool Attempted { get; set; }

    /// <summary>Formas livres criadas.</summary>
    public int Created { get; set; }

    public int Failed { get; set; }

    /// <summary>Formas ligadas ao parâmetro "VIR 3D visível".</summary>
    public int Associated { get; set; }

    /// <summary>A visibilidade gravada oculta o volume nas plantas.</summary>
    public bool HiddenInPlan { get; set; }

    public bool HasVisibleParameter { get; set; }

    public List<string> Reasons { get; } = new();

    public List<string> Notes { get; } = new();

    /// <summary>O volume pode ser usado: há formas e elas ficam ocultas em planta.</summary>
    public bool Usable => Created > 0 && HiddenInPlan;
}

/// <summary>Opções de carregamento: substitui a família existente e preserva os valores dos tipos do projeto.</summary>
public sealed class OverwriteFamilyLoadOptions : IFamilyLoadOptions
{
    public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
    {
        overwriteParameterValues = false;
        return true;
    }

    public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
    {
        source = FamilySource.Family;
        overwriteParameterValues = false;
        return true;
    }
}
