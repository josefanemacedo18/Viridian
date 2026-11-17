using System.IO;
using Autodesk.Revit.DB;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Geometry;
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
    public const int Version = 2;

    /// <summary>Gera o arquivo .rfa de um símbolo. Não pode haver transação aberta em nenhum documento.</summary>
    /// <param name="paint">Cores da copa e da floração.</param>
    /// <param name="typeName">Nome do tipo gravado na família (mantém o mesmo tipo quando a família é recarregada).</param>
    /// <param name="source">Projeto de onde copiar um tipo de região preenchida, se o modelo de família não tiver nenhum.</param>
    /// <returns>Quantidade de linhas que o Revit recusou (zero em condições normais).</returns>
    public static int Build(RevitApplication app, string templateFile, PlantSymbol symbol, SymbolPaint paint, string outputFile, ViridianSettings settings, string typeName, Document? source)
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
                skipped = Draw(app, familyDocument, symbol, paint, settings, typeName, source);
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

    private static int Draw(RevitApplication app, Document familyDocument, PlantSymbol symbol, SymbolPaint paint, ViridianSettings settings, string typeName, Document? source)
    {
        if (!familyDocument.IsFamilyDocument || familyDocument.OwnerFamily is null)
        {
            throw new TemplateRejectedException("O arquivo não é um modelo de família.");
        }

        int skipped = 0;
        using var transaction = new Transaction(familyDocument, "Viridian: símbolo 2D");
        transaction.Start();
        try
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

            var styles = new Dictionary<SymbolTier, GraphicsStyle>();
            var subcategories = new Dictionary<SymbolTier, Category>();
            foreach (SymbolTier tier in TierVisibility.AllTiers)
            {
                Category subcategory = GetOrCreateSubcategory(familyDocument, planting, TierVisibility.SubcategoryName(tier));
                if (tier < SymbolTier.Fill)
                {
                    ApplyStyle(subcategory, settings.StyleOf(tier));
                }

                styles[tier] = subcategory.GetGraphicsStyle(GraphicsStyleType.Projection);
                subcategories[tier] = subcategory;
            }

            double elevation = ReferenceLevelElevation(familyDocument);
            SketchPlane sketchPlane = SketchPlane.Create(
                familyDocument,
                Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, elevation)));

            double tolerance = app.ShortCurveTolerance * 1.05;

            // As regiões coloridas vêm antes das linhas, para ficarem por baixo delas.
            if (paint.HasFill)
            {
                FamilyFills.Draw(familyDocument, symbol, paint, sketchPlane, elevation, tolerance, subcategories, source);
            }
            foreach (SymbolPrimitive primitive in symbol.Primitives)
            {
                Curve? curve = ToCurve(primitive, elevation, tolerance);
                if (curve is null)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    SymbolicCurve symbolic = familyDocument.FamilyCreate.NewSymbolicCurve(curve, sketchPlane);
                    if (styles.TryGetValue(primitive.Tier, out GraphicsStyle? style) && style is not null)
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

            if (transaction.Commit() != TransactionStatus.Committed)
            {
                throw new TemplateRejectedException("O Revit não confirmou a criação do símbolo.");
            }
        }
        catch
        {
            if (transaction.HasStarted() && !transaction.HasEnded())
            {
                transaction.RollBack();
            }

            throw;
        }

        return skipped;
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
