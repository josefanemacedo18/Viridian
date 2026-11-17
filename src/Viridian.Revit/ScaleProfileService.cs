using Autodesk.Revit.DB;
using Viridian.Core.Config;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;
using Viridian.Library;

namespace Viridian.Revit;

public sealed class ScaleApplyResult
{
    public int ViewsChanged { get; set; }

    public int ViewsUnchanged { get; set; }

    /// <summary>Vistas cuja visibilidade é controlada por um modelo de vista e por isso não foram alteradas.</summary>
    public int ViewsControlledByTemplate { get; set; }

    public int ViewsFailed { get; set; }

    /// <summary>Vistas reservadas por outro usuário em um modelo compartilhado.</summary>
    public int ViewsOwnedByOthers { get; set; }
}

/// <summary>
/// Gerenciador de escalas: liga e desliga as camadas dos símbolos em cada vista conforme o perfil de
/// representação da escala. A dimensão real dos símbolos nunca muda; só o nível de detalhe exibido.
/// </summary>
public sealed class ScaleProfileService
{
    private readonly ViridianSettings _settings;

    public ScaleProfileService(ViridianSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Vistas em planta (e modelos de vista de planta) recebem perfis de representação.</summary>
    public static bool IsApplicable(View? view) =>
        view is ViewPlan
        && view.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.AreaPlan or ViewType.EngineeringPlan;

    public static IReadOnlyList<View> ApplicableViews(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfClass(typeof(ViewPlan)).Cast<View>().Where(IsApplicable).ToList();
    }

    /// <summary>Perfil fixado manualmente na vista, se houver.</summary>
    public static DetailProfile? OverrideOf(View view) =>
        ElementData.TryRead(view, ElementData.KindViewProfile, out ViewProfileOverride data) && Enum.IsDefined(data.Profile)
            ? data.Profile
            : null;

    /// <summary>Perfil que a vista deve exibir: o fixado manualmente ou o da escala.</summary>
    public DetailProfile ProfileFor(View view) => OverrideOf(view) ?? _settings.Scales.Resolve(view.Scale);

    /// <summary>Subcategorias de camada existentes no projeto (aparecem depois do primeiro símbolo carregado).</summary>
    public static IReadOnlyDictionary<SymbolTier, ElementId> TierSubcategories(Document document)
    {
        var result = new Dictionary<SymbolTier, ElementId>();
        Category? planting = Category.GetCategory(document, BuiltInCategory.OST_Planting);
        if (planting?.SubCategories is null)
        {
            return result;
        }

        foreach (SymbolTier tier in TierVisibility.AllTiers)
        {
            string name = TierVisibility.SubcategoryName(tier);
            if (planting.SubCategories.Contains(name))
            {
                result[tier] = planting.SubCategories.get_Item(name).Id;
            }
        }

        return result;
    }

    /// <summary>Indica se a vista exibe um perfil diferente do que deveria. Não altera o modelo.</summary>
    public bool NeedsUpdate(Document document, View view, IReadOnlyDictionary<SymbolTier, ElementId>? tiers = null)
    {
        if (!IsApplicable(view) || IsControlledByTemplate(document, view))
        {
            return false;
        }

        tiers ??= TierSubcategories(document);
        if (tiers.Count == 0)
        {
            return false;
        }

        try
        {
            DetailProfile profile = ProfileFor(view);
            foreach ((SymbolTier tier, ElementId id) in tiers)
            {
                if (view.CanCategoryBeHidden(id) && view.GetCategoryHidden(id) != !TierVisibility.IsVisible(tier, profile))
                {
                    return true;
                }
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Falha ao verificar o perfil da vista " + view.Name, ex);
        }

        return false;
    }

    /// <summary>Aplica o perfil de representação às vistas informadas. Só abre transação se algo precisar mudar.</summary>
    public ScaleApplyResult Apply(Document document, IEnumerable<View> views)
    {
        var result = new ScaleApplyResult();
        IReadOnlyDictionary<SymbolTier, ElementId> tiers = TierSubcategories(document);
        if (tiers.Count == 0)
        {
            return result;
        }

        var pending = new List<View>();
        foreach (View view in views.Where(IsApplicable))
        {
            if (IsControlledByTemplate(document, view))
            {
                result.ViewsControlledByTemplate++;
            }
            else if (IsOwnedByOther(document, view))
            {
                result.ViewsOwnedByOthers++;
            }
            else if (NeedsUpdate(document, view, tiers))
            {
                pending.Add(view);
            }
            else
            {
                result.ViewsUnchanged++;
            }
        }

        if (pending.Count == 0)
        {
            return result;
        }

        Tx.Run(document, "Viridian: perfil de escala", () =>
        {
            foreach (View view in pending)
            {
                try
                {
                    DetailProfile profile = ProfileFor(view);
                    foreach ((SymbolTier tier, ElementId id) in tiers)
                    {
                        bool hide = !TierVisibility.IsVisible(tier, profile);
                        if (view.CanCategoryBeHidden(id) && view.GetCategoryHidden(id) != hide)
                        {
                            view.SetCategoryHidden(id, hide);
                        }
                    }

                    result.ViewsChanged++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    result.ViewsFailed++;
                    ViridianLog.Warn("Não foi possível aplicar o perfil à vista " + view.Name, ex);
                }
            }
        });

        return result;
    }

    /// <summary>Fixa (ou libera, quando nulo) o perfil de representação de uma vista e o aplica.</summary>
    public ScaleApplyResult SetOverride(Document document, View view, DetailProfile? profile)
    {
        Tx.Run(document, "Viridian: perfil da vista", () =>
        {
            if (profile.HasValue)
            {
                ElementData.Write(view, ElementData.KindViewProfile, new ViewProfileOverride { Profile = profile.Value });
            }
            else if (ElementData.KindOf(view) == ElementData.KindViewProfile)
            {
                ElementData.Clear(view);
            }
        });

        return Apply(document, new[] { view });
    }

    /// <summary>Aplica espessuras e cores do padrão gráfico do escritório às camadas do projeto.</summary>
    public int ApplyGraphicStandards(Document document)
    {
        IReadOnlyDictionary<SymbolTier, ElementId> tiers = TierSubcategories(document);
        if (tiers.Count == 0)
        {
            return 0;
        }

        var pending = new List<(Category Category, TierStyle Style)>();
        foreach ((SymbolTier tier, ElementId id) in tiers)
        {
            Category? category = Category.GetCategory(document, id);
            TierStyle style = _settings.StyleOf(tier);
            if (category is not null && !Matches(category, style))
            {
                pending.Add((category, style));
            }
        }

        if (pending.Count == 0)
        {
            return 0;
        }

        return Tx.Run(document, "Viridian: padrão gráfico", () =>
        {
            foreach ((Category category, TierStyle style) in pending)
            {
                SymbolFamilyBuilder.ApplyStyle(category, style);
            }

            return pending.Count;
        });
    }

    private static bool Matches(Category category, TierStyle style)
    {
        int? weight = category.GetLineWeight(GraphicsStyleType.Projection);
        if (weight != Math.Clamp(style.LineWeight, 1, 16))
        {
            return false;
        }

        if (!ColorHex.TryParse(style.Color, out byte r, out byte g, out byte b))
        {
            return true;
        }

        Color color = category.LineColor;
        return color is not null && color.IsValid && color.Red == r && color.Green == g && color.Blue == b;
    }

    /// <summary>Em modelos compartilhados, vistas reservadas por outro usuário não podem ser alteradas.</summary>
    private static bool IsOwnedByOther(Document document, View view)
    {
        if (!document.IsWorkshared)
        {
            return false;
        }

        try
        {
            return WorksharingUtils.GetCheckoutStatus(document, view.Id) == CheckoutStatus.OwnedByOtherUser;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return false;
        }
    }

    /// <summary>Modelo de vista que controla a visibilidade da vista, ou nulo.</summary>
    public static View? ControllingTemplate(Document document, View view) =>
        IsControlledByTemplate(document, view) ? document.GetElement(view.ViewTemplateId) as View : null;

    /// <summary>Verdadeiro quando a visibilidade de categorias da vista é ditada por um modelo de vista.</summary>
    public static bool IsControlledByTemplate(Document document, View view)
    {
        if (view.IsTemplate || view.ViewTemplateId is null || view.ViewTemplateId == ElementId.InvalidElementId)
        {
            return false;
        }

        if (document.GetElement(view.ViewTemplateId) is not View template)
        {
            return false;
        }

        try
        {
            ICollection<ElementId> free = template.GetNonControlledTemplateParameterIds();
            return !free.Contains(new ElementId(BuiltInParameter.VIS_GRAPHICS_MODEL));
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return true;
        }
    }
}
