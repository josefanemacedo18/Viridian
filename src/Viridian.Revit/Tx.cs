using Autodesk.Revit.DB;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Execução de alterações no modelo dentro de transações, com desfazimento em caso de erro.</summary>
public static class Tx
{
    /// <summary>Executa a ação em uma transação. Qualquer exceção desfaz todas as alterações da transação.</summary>
    /// <param name="document">Documento a alterar.</param>
    /// <param name="name">Nome exibido no histórico de desfazer do Revit.</param>
    /// <param name="action">Alterações a realizar.</param>
    /// <param name="suppressWarnings">Dispensa avisos do Revit (não erros) para não interromper operações em lote.</param>
    public static void Run(Document document, string name, Action action, bool suppressWarnings = false)
    {
        Run(document, name, () =>
        {
            action();
            return true;
        }, suppressWarnings);
    }

    public static T Run<T>(Document document, string name, Func<T> action, bool suppressWarnings = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.IsReadOnly)
        {
            throw new InvalidOperationException("O documento está em modo somente leitura.");
        }

        if (document.IsModifiable)
        {
            // Já existe uma transação aberta por quem chamou: participa dela.
            return action();
        }

        using var transaction = new Transaction(document, name);
        if (suppressWarnings)
        {
            FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new WarningSwallower());
            transaction.SetFailureHandlingOptions(options);
        }

        if (transaction.Start() != TransactionStatus.Started)
        {
            throw new InvalidOperationException("Não foi possível iniciar a transação \"" + name + "\".");
        }

        try
        {
            T result = action();
            TransactionStatus status = transaction.Commit();
            if (status != TransactionStatus.Committed)
            {
                throw new InvalidOperationException("O Revit não confirmou a operação \"" + name + "\" (situação: " + status + ").");
            }

            return result;
        }
        catch
        {
            if (transaction.HasStarted() && !transaction.HasEnded())
            {
                try
                {
                    transaction.RollBack();
                }
                catch (Exception rollbackError)
                {
                    ViridianLog.Error("Falha ao desfazer a transação " + name, rollbackError);
                }
            }

            throw;
        }
    }

    /// <summary>Remove avisos (nunca erros) gerados durante a transação.</summary>
    private sealed class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
            }

            return FailureProcessingResult.Continue;
        }
    }
}

/// <summary>Erro de operação com mensagem pronta para ser mostrada ao usuário.</summary>
public sealed class ViridianException : Exception
{
    public ViridianException(string message)
        : base(message)
    {
    }

    public ViridianException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
