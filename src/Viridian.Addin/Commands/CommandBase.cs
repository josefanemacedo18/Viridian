using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Viridian.Library;
using Viridian.Revit;

namespace Viridian.Addin.Commands;

/// <summary>
/// Base de todos os comandos: cria o contexto e transforma qualquer erro em uma mensagem clara,
/// sem deixar o Revit em estado inconsistente (as transações abertas são desfeitas).
/// </summary>
public abstract class CommandBase : IExternalCommand
{
    /// <summary>Exige um projeto aberto (não família).</summary>
    protected virtual bool RequiresProject => true;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            ViridianContext context = ViridianContext.Create(commandData, RequiresProject);
            return Run(context);
        }
        catch (CommandCancelledException)
        {
            return Result.Cancelled;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
        catch (ViridianException ex)
        {
            ShowWarning(ex.Message);
            return Result.Cancelled;
        }
        catch (FamilyTemplateMissingException ex)
        {
            ShowWarning(ex.Message + "\n\nIndique um modelo de família em Configurações > Biblioteca.");
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            ViridianLog.Error("Erro no comando " + GetType().Name, ex);
            var dialog = new TaskDialog("Viridian")
            {
                MainInstruction = "A operação não pôde ser concluída",
                MainContent = ex.Message + "\n\nNenhuma alteração parcial foi mantida no modelo.",
                ExpandedContent = "Detalhes registrados em:\n" + ViridianLog.CurrentFile,
                MainIcon = TaskDialogIcon.TaskDialogIconError,
            };
            dialog.Show();
            return Result.Cancelled;
        }
    }

    internal abstract Result Run(ViridianContext context);

    internal static void ShowWarning(string text)
    {
        var dialog = new TaskDialog("Viridian")
        {
            MainInstruction = text,
            MainIcon = TaskDialogIcon.TaskDialogIconWarning,
        };
        dialog.Show();
    }

    internal static void ShowInfo(string instruction, string content = "")
    {
        var dialog = new TaskDialog("Viridian")
        {
            MainInstruction = instruction,
            MainContent = content,
        };
        dialog.Show();
    }
}

/// <summary>Comando disponível apenas com um projeto aberto.</summary>
public sealed class ProjectAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        Document? document = applicationData?.ActiveUIDocument?.Document;
        return document is not null && !document.IsFamilyDocument;
    }
}

/// <summary>Comando disponível sempre, inclusive sem documento aberto.</summary>
public sealed class AlwaysAvailable : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}
