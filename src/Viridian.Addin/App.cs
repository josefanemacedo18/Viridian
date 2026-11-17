using System.Reflection;
using Autodesk.Revit.UI;
using Viridian.Addin.Commands;
using Viridian.Addin.Ribbon;
using Viridian.Addin.Services;
using Viridian.Library;

namespace Viridian.Addin;

/// <summary>Ponto de entrada do Viridian no Revit: cria a aba na faixa de opções e os serviços automáticos.</summary>
public sealed class App : IExternalApplication
{
    public const string TabName = "Viridian";

    private ScaleAutoUpdater? _scaleUpdater;

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            BuildRibbon(application);
        }
        catch (Exception ex)
        {
            ViridianLog.Error("Falha ao criar a faixa de opções do Viridian.", ex);
            return Result.Failed;
        }

        try
        {
            _scaleUpdater = new ScaleAutoUpdater();
            _scaleUpdater.Attach(application);
        }
        catch (Exception ex)
        {
            // Sem a atualização automática o plugin continua funcionando (o comando Escalas aplica os perfis).
            ViridianLog.Error("Falha ao iniciar a atualização automática de escalas.", ex);
            _scaleUpdater = null;
        }

        ViridianLog.Info("Viridian carregado.");
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            _scaleUpdater?.Detach();
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Falha ao encerrar serviços do Viridian.", ex);
        }

        return Result.Succeeded;
    }

    private static void BuildRibbon(UIControlledApplication app)
    {
        try
        {
            app.CreateRibbonTab(TabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // A aba já existe (por exemplo, recarga do add-in): reutiliza.
        }

        string assembly = Assembly.GetExecutingAssembly().Location;

        RibbonPanel library = app.CreateRibbonPanel(TabName, "Biblioteca");
        Large<LibraryCommand, AlwaysAvailable>(library, assembly, "Buscar\nEspécie", "library",
            "Biblioteca botânica: pesquisa com filtros projetuais, ficha da espécie e cadastro.",
            "Pesquise por nome, categoria, origem, porte, copa, água, luz e crescimento. A partir da ficha é possível inserir, distribuir ou criar maciços.");
        Stack(library,
            Small<ImportFamilyCommand, AlwaysAvailable>(assembly, "Importar Família", "import",
                "Registra uma família personalizada (Vegetação ou componente de detalhe) e a associa a uma espécie."),
            Small<SyncCommand, ProjectAvailability>(assembly, "Atualizar Dados", "sync",
                "Atualiza tipos, símbolos, maciços e etiquetas do projeto a partir do catálogo."));

        RibbonPanel planting = app.CreateRibbonPanel(TabName, "Plantio");
        Large<InsertCommand, ProjectAvailability>(planting, assembly, "Inserir", "insert",
            "Insere exemplares individuais clicando na vista em planta.",
            "Cada exemplar recebe código, situação e estágio de implantação. Pressione ESC para concluir.");
        Large<DistributeCommand, ProjectAvailability>(planting, assembly, "Distribuir", "distribute",
            "Distribui exemplares em uma região: triangular, retangular, natural ou por densidade.",
            "Use uma região preenchida, piso, toposólido, área ou ambiente existente, ou desenhe o contorno. Respeita bordas, espaçamento, exemplares existentes e quantidade máxima.");
        Stack(planting,
            Small<LinearCommand, ProjectAvailability>(assembly, "Plantio Linear", "linear",
                "Distribui exemplares ao longo de um eixo (alamedas, renques e cercas vivas)."),
            Small<SubstituteCommand, ProjectAvailability>(assembly, "Substituir", "substitute",
                "Troca a espécie de exemplares e maciços preservando posição, código e dados."),
            Small<EditPlantsCommand, ProjectAvailability>(assembly, "Editar", "edit",
                "Altera situação, estágio de implantação, observações e variante gráfica dos exemplares selecionados."));

        RibbonPanel massing = app.CreateRibbonPanel(TabName, "Maciços");
        Large<MassingCreateCommand, ProjectAvailability>(massing, assembly, "Criar\nMaciço", "massing",
            "Cria um maciço vegetal com hachura, símbolos ou ambos, calculando a quantidade pela área.",
            "A representação é sugerida pela densidade (configurável). Os dados do maciço ficam gravados na região e alimentam os quantitativos.");
        Stack(massing,
            Small<MassingEditCommand, ProjectAvailability>(assembly, "Editar Maciço", "massing-edit",
                "Altera densidade, espaçamento, representação e reserva de um maciço."),
            Small<MassingRecalculateCommand, ProjectAvailability>(assembly, "Recalcular", "recalc",
                "Recalcula área, quantidade e símbolos dos maciços selecionados (ou de todos) após mudar os limites."));

        RibbonPanel identification = app.CreateRibbonPanel(TabName, "Identificação");
        Large<LabelCommand, ProjectAvailability>(identification, assembly, "Etiquetar", "label",
            "Identifica exemplares e maciços com código, nome popular, nome científico, área e quantidade.");
        Stack(identification,
            Small<CodeCommand, ProjectAvailability>(assembly, "Codificar", "code",
                "Atribui códigos aos exemplares e renumera os códigos de espécie no projeto."),
            Small<UpdateLabelsCommand, ProjectAvailability>(assembly, "Atualizar Etiquetas", "label-update",
                "Atualiza o texto de todas as etiquetas a partir dos dados atuais."),
            Small<LegendCommand, ProjectAvailability>(assembly, "Legenda", "legend",
                "Cria a vista de legenda com símbolos, códigos, nomes e quantidades."));

        RibbonPanel quantities = app.CreateRibbonPanel(TabName, "Quantitativos");
        Large<ReportsCommand, ProjectAvailability>(quantities, assembly, "Relatórios", "reports",
            "Relatórios por espécie, por maciço e de aquisição, com exportação para Excel.");
        Stack(quantities,
            Small<AcquisitionCommand, ProjectAvailability>(assembly, "Aquisição", "excel",
                "Lista de compra com reserva de reposição, unidade comercial e preço cadastrado."),
            Small<BudgetCommand, ProjectAvailability>(assembly, "Orçamento", "budget",
                "Orçamento de fornecimento e plantio com tabela de preços importada (SINAPI ou própria) e BDI."),
            Small<MaintenanceCommand, ProjectAvailability>(assembly, "Manutenção", "maintenance",
                "Cronograma de manutenção por espécie e época: rega, adubação, podas, cortes e reposição de falhas."));

        RibbonPanel check = app.CreateRibbonPanel(TabName, "Verificação");
        Large<CheckCommand, ProjectAvailability>(check, assembly, "Verificar\nProjeto", "check",
            "Encontra exemplares sem código, códigos repetidos, famílias não cadastradas, maciços inválidos, registros órfãos, conflitos de copa e de espaçamento.",
            "Clique duas vezes em uma ocorrência para mostrar os elementos. Correções automáticas disponíveis para códigos, dados desatualizados e registros órfãos.");

        RibbonPanel settings = app.CreateRibbonPanel(TabName, "Configurações");
        Large<ScaleCommand, ProjectAvailability>(settings, assembly, "Escalas", "scale",
            "Perfis de representação por escala: detalhado, intermediário, simplificado e mínimo.",
            "A dimensão real dos símbolos nunca muda; apenas o detalhamento exibido em cada vista.");
        Stack(settings,
            Small<SettingsCommand, AlwaysAvailable>(assembly, "Configurações", "settings",
                "Biblioteca, escalas, padrão gráfico, maciços, codificação, análise, etiquetas e hachuras do escritório."),
            Small<AboutCommand, AlwaysAvailable>(assembly, "Sobre", "about",
                "Versão do Viridian e atalhos para as pastas da biblioteca e de configurações."));
    }

    private static void Large<TCommand, TAvailability>(RibbonPanel panel, string assembly, string text, string icon, string tooltip, string? longDescription = null)
    {
        PushButtonData data = Data<TCommand, TAvailability>(assembly, text, icon, tooltip, longDescription);
        panel.AddItem(data);
    }

    private static PushButtonData Small<TCommand, TAvailability>(string assembly, string text, string icon, string tooltip) =>
        Data<TCommand, TAvailability>(assembly, text, icon, tooltip, null);

    private static PushButtonData Data<TCommand, TAvailability>(string assembly, string text, string icon, string tooltip, string? longDescription)
    {
        string className = typeof(TCommand).FullName!;
        var data = new PushButtonData("VIR_" + typeof(TCommand).Name, text, assembly, className)
        {
            ToolTip = tooltip,
            LargeImage = IconFactory.Large(icon),
            Image = IconFactory.Small(icon),
            AvailabilityClassName = typeof(TAvailability).FullName,
        };
        if (!string.IsNullOrWhiteSpace(longDescription))
        {
            data.LongDescription = longDescription;
        }

        return data;
    }

    private static void Stack(RibbonPanel panel, PushButtonData first, PushButtonData second, PushButtonData? third = null)
    {
        if (third is null)
        {
            panel.AddStackedItems(first, second);
        }
        else
        {
            panel.AddStackedItems(first, second, third);
        }
    }
}
