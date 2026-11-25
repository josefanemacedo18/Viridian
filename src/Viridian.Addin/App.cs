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

        // Quadro de fotos: texto e linha acompanham a foto arrastada (e a planta movida).
        Services.PhotoBoardUpdater.Register(application.ActiveAddInId);

        // Vegetação ilustrada: imagem sempre de frente para a câmera nas vistas 3D, cortes e elevações.
        try
        {
            Viridian.Revit.Illustrated.IllustratedVegetation.Start(application);
        }
        catch (Exception ex)
        {
            ViridianLog.Error("Falha ao iniciar a vegetação ilustrada.", ex);
        }

        ViridianLog.Info("Viridian carregado.");
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            _scaleUpdater?.Detach();
            Services.PhotoBoardUpdater.Unregister(application.ActiveAddInId);
            Viridian.Revit.Illustrated.IllustratedVegetation.Stop(application);
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
            "Biblioteca botânica completa: filtros projetuais, ficha técnica com foto e cadastro de espécies (nova, editar, duplicar, excluir).",
            "Pesquise por nome, categoria, origem, porte, copa, água, luz, crescimento, bioma e floração. Use para consultar e manter o catálogo; para plantar rapidamente, use Inserir.");
        Stack(library,
            Small<ImportFamilyCommand, AlwaysAvailable>(assembly, "Importar Família", "import",
                "Registra uma família personalizada (Vegetação ou componente de detalhe) e a associa a uma espécie."),
            Small<SyncCommand, ProjectAvailability>(assembly, "Atualizar Dados", "sync",
                "Atualiza tipos, símbolos, maciços e etiquetas do projeto a partir do catálogo."),
            Small<NewSpeciesCommand, AlwaysAvailable>(assembly, "Nova Vegetação", "new-species",
                "Cadastre uma vegetação própria: nomes, categoria, porte, símbolo 2D, cores da folhagem e da flor, foto, floração e dados de plantio. Ela passa a valer em todas as ferramentas."));

        RibbonPanel planting = app.CreateRibbonPanel(TabName, "Plantio");
        Large<InsertCommand, ProjectAvailability>(planting, assembly, "Inserir", "insert",
            "Inserção rápida: escolha a espécie (as recentes aparecem primeiro) e clique na vista; o símbolo colorido acompanha o cursor.",
            "Espécie e opções em uma única janela. Cada exemplar recebe código, situação e estágio de implantação. Pressione ESC para concluir.");
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
        Large<PhotoBoardCommand, ProjectAvailability>(identification, assembly, "Quadro de\nFotos", "photos",
            "Coloca ao redor da planta a fotografia de cada espécie, com código e nome, ligada por linha a um exemplar.",
            "Como nas pranchas de paisagismo ilustradas. Com região de corte ativa, o recorte é ampliado para mostrar os cartões. Rodar de novo substitui o quadro da vista.");
        Stack(identification,
            Small<CodeCommand, ProjectAvailability>(assembly, "Codificar", "code",
                "Atribui códigos aos exemplares e renumera os códigos de espécie no projeto."),
            Small<UpdateLabelsCommand, ProjectAvailability>(assembly, "Atualizar Etiquetas", "label-update",
                "Atualiza o texto de todas as etiquetas a partir dos dados atuais."),
            Small<LegendCommand, ProjectAvailability>(assembly, "Legenda", "legend",
                "Cria a vista de legenda com símbolos coloridos, fotografias, códigos, nomes e quantidades."));

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

        RibbonPanel documentation = app.CreateRibbonPanel(TabName, "Documentação");
        Large<SheetCommand, ProjectAvailability>(documentation, assembly, "Prancha\nAutomática", "sheet",
            "Cria a prancha de paisagismo com a planta ativa, a legenda e a tabela de quantitativos diagramadas sobre o carimbo.",
            "A planta fica à esquerda; legenda e tabela na coluna da direita, acima do carimbo. A escala é reduzida automaticamente se a planta não couber.");
        Stack(documentation,
            Small<CadExportCommand, ProjectAvailability>(assembly, "Exportar CAD", "cad",
                "Exporta vistas e pranchas para DWG (com cores e preenchimentos) e gera a biblioteca de blocos DXF dos símbolos."),
            Small<SchedulesCommand, ProjectAvailability>(assembly, "Tabelas Nativas", "schedule",
                "Cria as tabelas de vegetação do Revit (por espécie e por exemplar)."),
            Small<SpecificationBoardCommand, ProjectAvailability>(assembly, "Quadro de Especificação", "spec",
                "Tabela com código, espécie, quantidade, muda, escavação em centímetros e a especificação de cada espécie, para a prancha. Com uma prancha aberta, o quadro é colocado nela."));

        RibbonPanel composition = app.CreateRibbonPanel(TabName, "Composição");
        Large<PaletteCommand, AlwaysAvailable>(composition, assembly, "Ateliê de\nPaletas", "palette",
            "Escolha espécies por harmonia de cores (monocromática, análoga, complementar, tríade ou paletas prontas) e pela época de floração.",
            "As espécies aparecem em ordem de afinidade pela cor da flor e da folhagem; dali é possível inserir ou distribuir.");
        Stack(composition,
            Small<BedCheckCommand, ProjectAvailability>(assembly, "Verificar Canteiro", "composition",
                "Avalia a composição de um canteiro: equilíbrio entre espécies, diversidade, contraste de texturas, escala, sub-bosque de palmeiras e floração ao longo do ano."),
            Small<FloweringCalendarCommand, ProjectAvailability>(assembly, "Calendário de Floração", "calendar",
                "Quadro com o que está florido em cada mês do ano no projeto, nas cores das flores, e os meses sem floração."),
            Small<SolarStudyCommand, ProjectAvailability>(assembly, "Estudo Solar", "solar",
                "Horas de sol direto mês a mês numa região, com a sombra das copas adultas, das edificações e do terreno, pelas coordenadas da obra: calendário de luz, mapas coloridos em cópias da planta e plantas que não recebem a luz que pedem, com sugestões de espécies."));

        RibbonPanel detailing = app.CreateRibbonPanel(TabName, "Detalhamento");
        Large<MemorialCommand, ProjectAvailability>(detailing, assembly, "Memorial\nIlustrado", "memorial",
            "Memorial descritivo em Word editável: capa, partido, paleta vegetal, fichas com foto, calendário de floração, especificações, manutenção e créditos.",
            "Os dados do projeto (nome, número, cliente, endereço, autor) vêm de Gerenciar > Informações do projeto.");
        Stack(detailing,
            Small<PlantingDetailCommand, ProjectAvailability>(assembly, "Detalhe de Plantio", "detail",
                "Cova de árvore, berço de palmeira, cova de arbusto, canteiro de forração e gramado, em corte com cotas e notas de especificação."),
            Small<SectionCommand, ProjectAvailability>(assembly, "Corte Esquemático", "section",
                "Desenhe uma linha na planta: as plantas cortadas aparecem em silhueta com altura e copa reais, no plantio, aos 3 ou 6 anos ou na maturidade."),
            Small<GrowthCommand, ProjectAvailability>(assembly, "Crescimento 2D", "growth",
                "O mesmo canteiro no plantio, aos 3 e 6 anos e na maturidade, com as copas estimadas pela taxa de crescimento."));

        RibbonPanel paving = app.CreateRibbonPanel(TabName, "Pisos e Drenagem");
        Large<PavingCommand, AlwaysAvailable>(paving, assembly, "Gerador\nde Pisos", "paving",
            "Biblioteca de pisos (cimentícios, fulget, intertravados, drenantes, pedras, porcelanatos, concreto, decks, pedriscos, emborrachados e grama-concreto) com paginação, juntas, cores e camadas personalizáveis.",
            "Aplica o piso a pisos do Revit (tipo com camadas e material com a paginação em escala real), cria a hachura para plantas, o detalhe em corte e o quantitativo.");
        Large<StoneCommand, AlwaysAvailable>(paving, assembly, "Pedras e\nCoberturas", "stones",
            "Seixos, pedriscos, britas, cascalhos, pedras trituradas, pó de pedra, saibro, argila expandida e coberturas orgânicas, com granulometria, cor, usos e valores de referência para a compra.",
            "Cobre canteiros, cria bordas de caminho, pisos soltos ou compactados e camadas drenantes como pisos do Revit; calcula volume, peso e sacos.");
        Large<RainGardenCommand, ProjectAvailability>(paving, assembly, "Jardim\nde Chuva", "raingarden",
            "Dimensiona o jardim de chuva pelo volume a reter, sugere plantas para fundo, taludes e bordas e cria o detalhe em corte com o memorial de cálculo.",
            "Selecione telhados ou pisos antes do comando para somar a área de contribuição.");

        RibbonPanel model = app.CreateRibbonPanel(TabName, "Maquete 3D");
        Large<MaqueteCommand, ProjectAvailability>(model, assembly, "Maquete\n3D", "model3d",
            "Vista 3D do projeto com a vegetação em volume: copas, troncos, palmeiras, arbustos e maciços nas cores das espécies, assentados no terreno.",
            "Motor botânico: forma de cada tipo de planta (árvores, palmeiras, arbustos, rosetas, herbáceas), qualidade Simplificada, Intermediária ou Detalhada, folhagem, flores e frutos conforme o mês. Do projeto inteiro ou de uma região, no plantio, aos 3 ou 6 anos ou na maturidade. A planta baixa não muda: a maquete só aparece nas vistas 3D, cortes e elevações.");
        Stack(model,
            Small<MaqueteUpdateCommand, ProjectAvailability>(assembly, "Atualizar Maquete", "model3d-update",
                "Refaz a maquete com o mesmo recorte e a mesma fase, depois de mudanças no plantio."),
            Small<MaqueteRemoveCommand, ProjectAvailability>(assembly, "Remover Maquete", "model3d-remove",
                "Apaga a maquete 3D e os materiais criados para ela. A vegetação 2D não é alterada."),
            Small<Engine3DTestCommand, ProjectAvailability>(assembly, "Diagnóstico 3D", "model3d-test",
                "Opcional: confere no seu Revit uma família com símbolo 2D e volume 3D e as texturas na vista realista, e guarda um relatório para consulta. Não é preciso enviar nada."));

        Large<FixVegetationCommand, ProjectAvailability>(model, assembly, "Vegetação\nnas Pranchas", "model3d-update",
            "Fixa a vegetação ilustrada como formas reais nas vistas 3D das pranchas (ou na vista 3D ativa): sai na impressão, no PDF, nas imagens, no estilo Realista e na renderização.",
            "O desenho ao vivo aparece só na tela. Fixada, cada vista recebe as formas de frente para a sua câmera, ocultas nas outras vistas e nas plantas. Rode de novo depois de mudar o plantio ou girar a vista; a mesma janela desfaz a fixação.");

        RibbonPanel check = app.CreateRibbonPanel(TabName, "Verificação");
        Large<CheckCommand, ProjectAvailability>(check, assembly, "Verificar\nProjeto", "check",
            "Encontra exemplares sem código, códigos repetidos, famílias não cadastradas, maciços inválidos, registros órfãos, conflitos de copa e de espaçamento.",
            "Clique duas vezes em uma ocorrência para mostrar os elementos. Correções automáticas disponíveis para códigos, dados desatualizados e registros órfãos.");
        Large<InterferenceCommand, ProjectAvailability>(check, assembly, "Interfe-\nrências", "interference",
            "Distâncias entre o plantio e tubulações, eletrodutos, dutos, postes, paredes e pisos, inclusive nos modelos vinculados.",
            "As distâncias mínimas por tipo de elemento e de planta ficam em Configurações, aba Interferências.");

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
