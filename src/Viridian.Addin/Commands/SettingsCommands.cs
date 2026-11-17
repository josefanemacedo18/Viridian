using System.Diagnostics;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Viridian.Core.Scale;
using Viridian.Library;
using Viridian.Revit;
using Viridian.UI.Windows;

namespace Viridian.Addin.Commands;

/// <summary>Gerenciador de escalas da vista ativa e de todas as plantas.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ScaleCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        Document doc = ctx.Doc;
        View active = ctx.UiDocument.ActiveView;
        bool applicable = ScaleProfileService.IsApplicable(active);
        View? template = applicable ? ScaleProfileService.ControllingTemplate(doc, active) : null;
        View target = template ?? active;

        var window = new ScaleWindow(
            applicable ? active.Name : string.Empty,
            applicable ? target.Scale : 100,
            applicable ? ScaleProfileService.OverrideOf(target) : null,
            template is not null,
            ctx.Settings);
        if (!ctx.ShowDialog(window))
        {
            return Result.Cancelled;
        }

        if (window.AutoApply != ctx.Settings.AutoApplyScaleProfiles)
        {
            ctx.Settings.AutoApplyScaleProfiles = window.AutoApply;
            ctx.SaveSettings();
        }

        if (ScaleProfileService.TierSubcategories(doc).Count == 0)
        {
            ShowInfo("Nenhum símbolo do Viridian no projeto", "Os perfis de escala passam a valer assim que a primeira espécie for inserida.");
            return Result.Succeeded;
        }

        ScaleApplyResult result = ctx.Grouped("Viridian: perfis de escala", () =>
        {
            ctx.Scales.ApplyGraphicStandards(doc);
            if (window.Action == ScaleAction.ApplyView && applicable)
            {
                return ctx.Scales.SetOverride(doc, target, window.Override);
            }

            return ctx.Scales.Apply(doc, ScaleProfileService.ApplicableViews(doc));
        }, _ => true);

        string text = $"Vistas atualizadas: {result.ViewsChanged}\nVistas já corretas: {result.ViewsUnchanged}";
        if (result.ViewsControlledByTemplate > 0)
        {
            text += $"\nVistas controladas por modelo de vista: {result.ViewsControlledByTemplate} (o perfil vale pelo modelo)";
        }

        if (result.ViewsOwnedByOthers > 0)
        {
            text += $"\nVistas reservadas por outros usuários: {result.ViewsOwnedByOthers}";
        }

        if (result.ViewsFailed > 0)
        {
            text += $"\nVistas não alteradas por erro: {result.ViewsFailed} (detalhes no registro)";
        }

        ShowInfo("Perfis de escala aplicados", text);
        return Result.Succeeded;
    }
}

/// <summary>Configurações do escritório.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SettingsCommand : CommandBase
{
    protected override bool RequiresProject => false;

    internal override Result Run(ViridianContext ctx)
    {
        var window = new SettingsWindow(ctx.Settings, ctx.Paths);
        if (!ctx.ShowDialog(window))
        {
            return Result.Cancelled;
        }

        SettingsStore.Save(window.Settings);
        if (ctx.HasProject)
        {
            // Reaplica padrão gráfico e faixas de escala no projeto aberto com as novas configurações.
            var scales = new ScaleProfileService(window.Settings);
            if (ScaleProfileService.TierSubcategories(ctx.Doc).Count > 0)
            {
                ctx.Grouped("Viridian: aplicar configurações", () =>
                {
                    scales.ApplyGraphicStandards(ctx.Doc);
                    return scales.Apply(ctx.Doc, ScaleProfileService.ApplicableViews(ctx.Doc));
                }, _ => true);
            }
        }

        return Result.Succeeded;
    }
}

/// <summary>Informações do plugin e atalhos para as pastas.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class AboutCommand : CommandBase
{
    protected override bool RequiresProject => false;

    internal override Result Run(ViridianContext ctx)
    {
        Version version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        var dialog = new TaskDialog("Viridian")
        {
            MainInstruction = $"Viridian {version.Major}.{version.Minor}.{version.Build}",
            MainContent = "Documentação técnica 2D de paisagismo para Autodesk Revit 2027.\n\n"
                          + $"Espécies no catálogo: {ctx.Catalog.All.Count}\n"
                          + $"Biblioteca: {ctx.Paths.Root}",
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir a pasta da biblioteca");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Abrir a pasta de configurações");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Abrir a pasta de registros de erro");

        string? folder = dialog.Show() switch
        {
            TaskDialogResult.CommandLink1 => ctx.Paths.Root,
            TaskDialogResult.CommandLink2 => LibraryPaths.ConfigFolder,
            TaskDialogResult.CommandLink3 => LibraryPaths.LogFolder,
            _ => null,
        };

        if (folder is not null)
        {
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                ShowWarning("Não foi possível abrir a pasta:\n" + ex.Message);
            }
        }

        return Result.Succeeded;
    }
}
