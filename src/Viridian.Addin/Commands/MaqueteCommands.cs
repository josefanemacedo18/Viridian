using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Viridian.Core.Detailing;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Generation;
using Viridian.Core.Model3D.Profiles;
using Viridian.Library;
using Viridian.Revit;
using Viridian.UI.Windows;

namespace Viridian.Addin.Commands;

/// <summary>Fluxo comum da maquete: gerar na transação, abrir a vista e resumir o resultado.</summary>
internal static class MaqueteFlow
{
    public static Result Generate(ViridianContext ctx, MaqueteState state, string title, IReadOnlyList<Plant3DProfile> profiles)
    {
        MaqueteResult result = ctx.Grouped("Viridian: " + title, () =>
            Tx.Run(ctx.Doc, "Viridian: " + title, () =>
                MaqueteService.Build(ctx.Doc, state, ctx.Lookup, ctx.ProjectCode, ctx.Settings, profiles), suppressWarnings: true), _ => true);

        try
        {
            ctx.UiDocument.ActiveView = result.View;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            Viridian.Library.ViridianLog.Warn("Não foi possível abrir a vista da maquete.", ex);
        }

        var lines = new List<string>
        {
            $"Fase: {GrowthModel.Name(state.Stage)} · {(state.IsWholeProject ? "projeto inteiro" : "região escolhida")}",
            $"Geração: {state.Describe()}.",
            $"{result.Plants} exemplar(es) e {result.Beds} maciço(s) em volume.",
        };
        if (result.Engine == MaqueteEngine.Botanico && result.Plants > 0)
        {
            lines.Add($"{N(result.Triangles)} triângulos nos exemplares (média de {N(result.Triangles / Math.Max(1, result.Plants))} por exemplar).");
            if (result.Reduced > 0)
            {
                lines.Add($"{result.Reduced} exemplar(es) com detalhe reduzido para caber no orçamento do nível escolhido.");
            }

            if (state.Month is >= 1 and <= 12)
            {
                var season = new List<string>();
                if (result.Leafless > 0)
                {
                    season.Add($"{result.Leafless} sem folhas");
                }

                if (result.Sparse > 0)
                {
                    season.Add($"{result.Sparse} com folhagem rala");
                }

                if (result.Flowering > 0)
                {
                    season.Add($"{result.Flowering} em flor" + (result.FloweringEstimated > 0 ? $" ({result.FloweringEstimated} com época estimada pelo gênero)" : string.Empty));
                }

                if (result.Fruiting > 0)
                {
                    season.Add($"{result.Fruiting} com frutos (época da ficha ou estimada)");
                }

                lines.Add(season.Count > 0 ? "No mês escolhido: " + string.Join(", ", season) + "." : "No mês escolhido, nenhuma espécie está sem folhas, em flor ou com frutos.");
            }
            else if (result.Fruiting > 0)
            {
                lines.Add($"{result.Fruiting} exemplar(es) com frutos (sem estação: frutos de todas as espécies com dado).");
            }

            if (result.ChosenProfiles > 0)
            {
                lines.Add($"{result.ChosenProfiles} espécie(s) com perfil 3D escolhido na ficha; as demais usam o perfil automático.");
            }
        }

        if (result.OnTerrain > 0)
        {
            lines.Add($"{result.OnTerrain} assentado(s) sobre o terreno ou sobre lajes.");
        }

        if (result.Skipped > 0)
        {
            lines.Add($"{result.Skipped} exemplar(es) ficaram de fora pelo limite de {ctx.Settings.Model3D.MaxPlants} (Configurações > Composição). Use uma região menor ou aumente o limite.");
        }

        if (result.Failed > 0)
        {
            lines.Add($"{result.Failed} elemento(s) não puderam ser criados; o detalhe está no registro de erros (Viridian > Sobre).");
        }

        if (!result.HiddenInPlans)
        {
            lines.Add("Atenção: o Revit não aceitou ocultar a maquete nas plantas. Desligue a categoria Entorno nas vistas em planta, se necessário.");
        }

        if (result.Plants + result.Beds == 0)
        {
            lines.Add("Não há vegetação do catálogo no recorte escolhido.");
        }

        lines.Add(string.Empty);
        lines.Add("A maquete não acompanha as mudanças sozinha: depois de alterar o plantio, use Atualizar Maquete.");
        TaskDialog.Show("Viridian — Maquete 3D", string.Join("\n", lines));
        return Result.Succeeded;
    }

    private static string N(long value) => value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    /// <summary>Perfis 3D da biblioteca (com os padrões, se o arquivo não puder ser lido).</summary>
    public static IReadOnlyList<Plant3DProfile> Profiles(ViridianContext ctx)
    {
        try
        {
            return new Plant3DProfileLibrary(ctx.Paths).Profiles;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Viridian.Library.ViridianLog.Warn("Perfis 3D: usando os perfis padrão.", ex);
            return Plant3DProfileDefaults.Create();
        }
    }

    /// <summary>
    /// Confirma uma maquete pesada antes de gerar: oferece o nível que cabe no limite, gerar assim mesmo ou cancelar.
    /// Devolve falso quando o usuário cancela.
    /// </summary>
    public static bool ConfirmWeight(ViridianContext ctx, MaqueteState state, IReadOnlyList<Plant3DProfile> profiles)
    {
        if (state.Engine != MaqueteEngine.Botanico)
        {
            return true;
        }

        MaqueteEstimate estimate = MaqueteService.Estimate(ctx.Doc, state, ctx.Lookup, ctx.Settings, profiles);
        long limit = ctx.Settings.Model3D.WarnTriangles;
        if (estimate.Triangles <= limit)
        {
            return true;
        }

        QualityLevel? lighter = MaqueteCost.Suggest(estimate.Species, profiles, limit);
        var dialog = new TaskDialog("Viridian — Maquete pesada")
        {
            MainInstruction = "A maquete vai ficar pesada",
            MainContent = $"{estimate.Plants} exemplar(es) no nível {ProfileLabels.Of(state.Quality)} somam cerca de {N(estimate.Triangles / 1000)} mil triângulos "
                          + $"(o aviso começa em {N(limit / 1000)} mil). A vista 3D pode ficar lenta para girar e para abrir.",
            CommonButtons = TaskDialogCommonButtons.Cancel,
            AllowCancellation = true,
        };
        if (lighter is QualityLevel q && q != state.Quality)
        {
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Gerar no nível {ProfileLabels.Of(q)}", "Cabe no limite e mantém as mesmas escolhas.");
        }

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Gerar assim mesmo", "Pode demorar alguns minutos.");
        TaskDialogResult choice = dialog.Show();
        if (choice == TaskDialogResult.CommandLink1 && lighter is QualityLevel chosen)
        {
            state.Quality = chosen;
            return true;
        }

        return choice == TaskDialogResult.CommandLink2;
    }
}

/// <summary>Gera a maquete 3D do projeto inteiro ou de uma região.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class MaqueteCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        bool inPlan = ctx.UiDocument.ActiveView is ViewPlan { IsTemplate: false, GenLevel: not null };
        bool exists = MaqueteService.Shapes(ctx.Doc).Count > 0;
        IReadOnlyList<Plant3DProfile> profiles = MaqueteFlow.Profiles(ctx);
        MaqueteEstimate whole = MaqueteService.Estimate(ctx.Doc, new MaqueteState(), ctx.Lookup, ctx.Settings, profiles);
        bool south = MaqueteService.SouthernHemisphere(ctx.Doc, ctx.Settings);
        var window = new MaqueteWindow(ctx.Settings.Model3D, inPlan, exists, whole.Plants, whole.Beds,
            (engine, quality) => engine == MaqueteEngine.Botanico ? MaqueteCost.Triangles(whole.Species, profiles, quality) : whole.Plants * 300L, south);
        if (!ctx.ShowDialog(window))
        {
            return Result.Cancelled;
        }

        // As escolhas ficam como padrão da próxima maquete.
        ctx.Settings.Model3D = window.Settings;
        ctx.SaveSettings();

        MaqueteState state = window.ToState();
        if (!window.WholeProject)
        {
            ViewPlan view = ctx.RequirePlanView();
            RegionBoundary region = ctx.PickRegion("a maquete 3D");
            state.Region = MaqueteState.FromPolygon(region.Polygon);
            state.LevelName = PlantingService.LevelOf(view)?.Name ?? string.Empty;
        }

        if (!MaqueteFlow.ConfirmWeight(ctx, state, profiles))
        {
            return Result.Cancelled;
        }

        return MaqueteFlow.Generate(ctx, state, "maquete 3D", profiles);
    }
}

/// <summary>Refaz a maquete com o mesmo recorte e as mesmas escolhas (maquetes antigas continuam no gerador simples).</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class MaqueteUpdateCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        MaqueteState state = MaqueteService.ReadState(MaqueteService.FindView(ctx.Doc), ctx.Settings.Model3D.ShowFlowers)
                             ?? throw new ViridianException("Ainda não há maquete neste projeto. Use Maquete 3D para criá-la.");
        IReadOnlyList<Plant3DProfile> profiles = MaqueteFlow.Profiles(ctx);
        if (!MaqueteFlow.ConfirmWeight(ctx, state, profiles))
        {
            return Result.Cancelled;
        }

        return MaqueteFlow.Generate(ctx, state, "atualizar maquete 3D", profiles);
    }
}

/// <summary>Remove a maquete 3D (formas, materiais e vista), sem tocar na vegetação 2D.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class MaqueteRemoveCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        int count = MaqueteService.Shapes(ctx.Doc).Count;
        View3D? view = MaqueteService.FindView(ctx.Doc);
        if (count == 0 && view is null)
        {
            TaskDialog.Show("Viridian", "Não há maquete 3D neste projeto.");
            return Result.Cancelled;
        }

        var confirm = new TaskDialog("Viridian — Remover maquete")
        {
            MainInstruction = "Remover a maquete 3D?",
            MainContent = $"{count} forma(s) da maquete e os materiais criados para ela serão apagados. A vista \"{view?.Name ?? MaqueteService.ViewName}\" também, se não estiver em uma prancha.\nA vegetação 2D, os maciços e as tabelas não são alterados.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No,
        };
        if (confirm.Show() != TaskDialogResult.Yes)
        {
            return Result.Cancelled;
        }

        // A vista ativa não pode ser apagada: troca para outra vista aberta antes.
        if (view is not null && ctx.UiDocument.ActiveView?.Id == view.Id)
        {
            View? other = ctx.UiDocument.GetOpenUIViews().Select(u => ctx.Doc.GetElement(u.ViewId)).OfType<View>().FirstOrDefault(v => v.Id != view.Id)
                          ?? new FilteredElementCollector(ctx.Doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => !v.IsTemplate);
            if (other is not null)
            {
                try
                {
                    ctx.UiDocument.ActiveView = other;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    Viridian.Library.ViridianLog.Warn("Não foi possível trocar de vista antes de remover a maquete.", ex);
                }
            }
        }

        (int shapes, bool viewDeleted) = ctx.Grouped("Viridian: remover maquete 3D", () =>
            Tx.Run(ctx.Doc, "Viridian: remover maquete 3D", () => MaqueteService.Remove(ctx.Doc), suppressWarnings: true), _ => true);
        TaskDialog.Show("Viridian", $"Maquete removida: {shapes} forma(s)." + (viewDeleted ? " A vista também foi apagada." : " A vista foi mantida (está em uma prancha ou aberta) e pode ser apagada no Navegador de projeto."));
        return Result.Succeeded;
    }
}

/// <summary>
/// Provas de conceito do motor 3D (Fase 0: família BIM; T0: texturas do ipê), com relatório para enviar. Por padrão o
/// projeto volta a ficar como estava; a família, as texturas, as imagens e o relatório ficam numa pasta própria.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class Engine3DTestCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        var dialog = new TaskDialog("Viridian — Testes do motor 3D")
        {
            MainInstruction = "Quais testes executar?",
            MainContent = "Os testes criam elementos temporários para medir o que o seu Revit aceita. Ao final, um relatório (texto) e as imagens ficam numa pasta para você enviar.",
            CommonButtons = TaskDialogCommonButtons.Cancel,
            AllowCancellation = true,
            VerificationText = "Manter no projeto os elementos de teste (para ver no Revit)",
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Os dois testes (recomendado)", "Fase 0 (família BIM) e T0 (texturas do ipê).");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Fase 0 — família BIM", "Símbolo 2D em planta e árvore 3D em forma livre na mesma família; inserção, vistas, níveis de detalhe, 25 exemplares e recarga.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "T0 — texturas do ipê", "Folhagem com recorte, flor e casca geradas pelo Viridian, aplicadas a um ipê na vista realista (com imagem exportada).");
        TaskDialogResult choice = dialog.Show();
        if (choice is not (TaskDialogResult.CommandLink1 or TaskDialogResult.CommandLink2 or TaskDialogResult.CommandLink3))
        {
            return Result.Cancelled;
        }

        bool keep = dialog.WasVerificationChecked();
        bool phase0 = choice != TaskDialogResult.CommandLink3;
        bool t0 = choice != TaskDialogResult.CommandLink2;
        string folder = Engine3DTestService.NewFolder();
        var report = new EngineTestReport("Viridian — testes do motor 3D");
        Engine3DTestService.Describe(report, ctx.UiApp.Application, ctx.Doc);
        report.Environment["Testes"] = (phase0 ? "Fase 0" : string.Empty) + (phase0 && t0 ? " e " : string.Empty) + (t0 ? "T0" : string.Empty);
        report.Environment["Elementos mantidos no projeto"] = keep ? "sim" : "não (desfeitos ao final)";

        // A família é montada antes, sem transação aberta no projeto.
        string familyFile = phase0 ? Engine3DTestService.BuildFamily(ctx.UiApp.Application, ctx.Settings, folder, report) : string.Empty;
        View? active = ctx.UiDocument.ActiveView;
        View3D? textureView = ctx.Grouped("Viridian: testes do motor 3D", () =>
        {
            if (familyFile.Length > 0)
            {
                Engine3DTestService.PlaceAndCheck(ctx.Doc, familyFile, active, report);
            }

            return t0 ? Engine3DTestService.TextureTest(ctx.UiApp.Application, ctx.Doc, folder, report) : null;
        }, _ => keep);

        if (keep && textureView is { IsValidObject: true })
        {
            if (report.Steps.Any(s => s.Name.StartsWith("T0 · imagem", StringComparison.Ordinal) && s.Outcome == TestOutcome.Falha))
            {
                int exported = SolarService.ExportImages(ctx.Doc, new List<View> { textureView }, folder);
                report.Add("T0 · imagem (nova tentativa, fora do grupo)", exported > 0 ? TestOutcome.Ok : TestOutcome.Falha);
            }

            try
            {
                ctx.UiDocument.ActiveView = textureView;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                ViridianLog.Warn("Teste T0: abrir a vista", ex);
            }
        }

        string reportFile = System.IO.Path.Combine(folder, "relatorio-motor-3d.txt");
        System.IO.File.WriteAllText(reportFile, report.ToText(), new System.Text.UTF8Encoding(true));
        var done = new TaskDialog("Viridian — Testes do motor 3D")
        {
            MainInstruction = report.Failed ? "Testes concluídos, com falhas" : "Testes concluídos",
            MainContent = report.Summary() + "\n\nEnvie a pasta (ou pelo menos o relatório e as imagens):\n" + folder
                          + (keep ? "\n\nOs elementos de teste ficaram no projeto: família \"" + Engine3DTestService.FamilyName + "\" na origem, materiais \"VIR Teste\" e a vista \"" + Engine3DTestService.TextureViewName + "\"."
                                  : "\n\nO projeto voltou a ficar como estava."),
            ExpandedContent = report.ToText(),
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        done.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir a pasta dos testes");
        if (done.Show() == TaskDialogResult.CommandLink1)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                ViridianLog.Warn("Abrir a pasta dos testes", ex);
            }
        }

        return Result.Succeeded;
    }
}
