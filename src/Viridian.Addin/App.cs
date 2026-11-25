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
        library.AddItem(Data<LibraryCommand, AlwaysAvailable>(assembly, "Buscar\nEspécie", "library", "Biblioteca botânica completa: filtros projetuais, ficha técnica com foto e cadastro de espécies (nova, editar, duplicar, excluir).", "Pesquise por nome, categoria, origem, porte, copa, água, luz, crescimento, bioma e floração. Use para consultar e manter o catálogo; para plantar rapidamente, use Inserir."));
        Pulldown(library, "Cadastro", "Cadastro", "new-species", "Nova vegetação, importar família e atualizar os dados do projeto a partir do catálogo.", Data<NewSpeciesCommand, AlwaysAvailable>(assembly, "Nova Vegetação", "new-species", "Cadastre uma vegetação própria: nomes, categoria, porte, símbolo 2D, cores da folhagem e da flor, foto, floração e dados de plantio. Ela passa a valer em todas as ferramentas.", null), Data<ImportFamilyCommand, AlwaysAvailable>(assembly, "Importar Família", "import", "Registra uma família personalizada (Vegetação ou componente de detalhe) e a associa a uma espécie.", null), Data<SyncCommand, ProjectAvailability>(assembly, "Atualizar Dados", "sync", "Atualiza tipos, símbolos, maciços e etiquetas do projeto a partir do catálogo.", null));

        RibbonPanel planting = app.CreateRibbonPanel(TabName, "Plantio");
        planting.AddItem(Data<InsertCommand, ProjectAvailability>(assembly, "Inserir", "insert", "Inserção rápida: escolha a espécie (as recentes aparecem primeiro) e clique na vista; o símbolo colorido acompanha o cursor.", "Espécie e opções em uma única janela. Cada exemplar recebe código, situação e estágio de implantação. Pressione ESC para concluir."));
        planting.AddItem(Data<DistributeCommand, ProjectAvailability>(assembly, "Distribuir", "distribute", "Distribui exemplares em uma região: triangular, retangular, natural ou por densidade.", "Use uma região preenchida, piso, toposólido, área ou ambiente existente, ou desenhe o contorno. Respeita bordas, espaçamento, exemplares existentes e quantidade máxima."));
        planting.AddItem(Data<LinearCommand, ProjectAvailability>(assembly, "Plantio\nLinear", "linear", "Distribui exemplares ao longo de um eixo (alamedas, renques e cercas vivas).", null));
        Pulldown(planting, "AlterarPlantio", "Alterar\nPlantio", "edit", "Substituir a espécie e editar os dados dos exemplares.", Data<SubstituteCommand, ProjectAvailability>(assembly, "Substituir", "substitute", "Troca a espécie de exemplares e maciços preservando posição, código e dados. Sem nada selecionado, troca uma espécie em lote: em todo o projeto ou só na vista.", null), Data<EditPlantsCommand, ProjectAvailability>(assembly, "Editar", "edit", "Altera situação, estágio de implantação, observações e variante gráfica dos exemplares selecionados.", null));

        RibbonPanel beds = app.CreateRibbonPanel(TabName, "Maciços e Canteiros");
        beds.AddItem(Data<MassingCreateCommand, ProjectAvailability>(assembly, "Criar\nMaciço", "massing", "Cria um maciço vegetal com hachura, símbolos ou ambos, calculando a quantidade pela área.", "A representação é sugerida pela densidade (configurável). Os dados do maciço ficam gravados na região e alimentam os quantitativos."));
        Pulldown(beds, "AjustarMacico", "Ajustar\nMaciço", "massing-edit", "Editar o maciço, ajustar ao contorno editado ou recalcular toda a distribuição.", Data<MassingEditCommand, ProjectAvailability>(assembly, "Editar Maciço", "massing-edit", "Altera densidade, espaçamento, representação e reserva de um maciço.", null), Data<MassingAdjustCommand, ProjectAvailability>(assembly, "Ajustar Contorno", "massing-edit", "Depois de editar o limite do maciço: mantém os exemplares que continuam dentro (posição, código e dados), planta só a parte acrescentada e indica ou remove os que ficaram fora.", null), Data<MassingRecalculateCommand, ProjectAvailability>(assembly, "Recalcular", "recalc", "Refaz toda a distribuição dos maciços selecionados (ou de todos): área, quantidade e símbolos. Exemplares movidos à mão voltam ao padrão.", null));
        beds.AddItem(Data<ComposeBedCommand, ProjectAvailability>(assembly, "Compor\nCanteiro", "composition", "Várias espécies em uma única operação: destaques, faixas de fundo, meio e frente por proporção da área e forração de base, com prévia.", "Escolha a região, clique no lado do fundo (ou ESC para canteiro em ilha) e monte a receita ou use um modelo do escritório. Um único Desfazer volta tudo."));
        Pulldown(beds, "ModelosCanteiro", "Modelos de\nCanteiro", "palette", "Salvar um canteiro pronto como modelo do escritório ou inserir um modelo salvo.", Data<SaveBedTemplateCommand, ProjectAvailability>(assembly, "Salvar Canteiro", "palette", "Grava um canteiro pronto (exemplares, maciços e contorno) como modelo do escritório, para reusar o desenho em outros projetos ou como receita em outro contorno.", null), Data<InsertBedTemplateCommand, ProjectAvailability>(assembly, "Inserir Modelo", "insert", "Insere um canteiro salvo, igual ao original, girado e posicionado com um clique; serve para qualquer projeto.", null));

        RibbonPanel composition = app.CreateRibbonPanel(TabName, "Composição");
        composition.AddItem(Data<PaletteCommand, AlwaysAvailable>(assembly, "Ateliê de\nPaletas", "palette", "Escolha espécies por harmonia de cores (monocromática, análoga, complementar, tríade ou paletas prontas) e pela época de floração.", "As espécies aparecem em ordem de afinidade pela cor da flor e da folhagem; dali é possível inserir ou distribuir."));
        composition.AddItem(Data<FloweringPanelCommand, ProjectAvailability>(assembly, "Floração", "calendar", "Painel de consulta da floração: por maciço e por espécie isolada da vista, os meses floridos nas cores das flores e os meses sem flor. Também cria o quadro de calendário para a prancha.", "Escolha um mês para ver o que está florido e isolar temporariamente na vista só o que floresce nele (Redefinir ocultar/isolar temporário volta ao normal). Na mesma janela, Criar calendário na prancha gera o quadro de janeiro a dezembro."));
        Split(composition, "EstudoSolar", Data<SolarStudyCommand, ProjectAvailability>(assembly, "Estudo\nSolar", "solar", "Horas de sol direto mês a mês numa região, com a sombra das copas adultas, das edificações e do terreno, pelas coordenadas da obra: calendário de luz, mapas coloridos em cópias da planta e plantas que não recebem a luz que pedem, com sugestões de espécies.", null), Data<IndicateNorthCommand, ProjectAvailability>(assembly, "Indicar Norte", "solar", "Indique o norte verdadeiro clicando na planta: um ponto qualquer e depois a direção do norte. O estudo solar e as sombras seguem essa direção, mesmo com o projeto desenhado girado.", "Fica gravado no projeto (só no Viridian) ou, se preferir, gira também o norte verdadeiro do Revit (Gerenciar > Posição)."));
        composition.AddItem(Data<BedCheckCommand, ProjectAvailability>(assembly, "Verificar\nCanteiro", "composition", "Avalia a composição de um canteiro: equilíbrio entre espécies, diversidade, contraste de texturas, escala, sub-bosque de palmeiras e floração ao longo do ano.", null));

        RibbonPanel paving = app.CreateRibbonPanel(TabName, "Pisos e Drenagem");
        Split(paving, "Pisos", Data<PavingCommand, AlwaysAvailable>(assembly, "Gerador\nde Pisos", "paving", "Biblioteca de pisos (cimentícios, fulget, intertravados, drenantes, pedras, porcelanatos, concreto, decks, pedriscos, emborrachados e grama-concreto) com paginação, juntas, cores e camadas personalizáveis.", "Aplica o piso a pisos do Revit (tipo com camadas e material com a paginação em escala real), cria a hachura para plantas, o detalhe em corte e o quantitativo."), Data<StoneCommand, AlwaysAvailable>(assembly, "Pedras e Coberturas", "stones", "Seixos, pedriscos, britas, cascalhos, pedras trituradas, pó de pedra, saibro, argila expandida e coberturas orgânicas, com granulometria, cor, usos e valores de referência para a compra.", "Cobre canteiros, cria bordas de caminho, pisos soltos ou compactados e camadas drenantes como pisos do Revit; calcula volume, peso e sacos."));
        paving.AddItem(Data<RainGardenCommand, ProjectAvailability>(assembly, "Jardim\nde Chuva", "raingarden", "Dimensiona o jardim de chuva pelo volume a reter, sugere plantas para fundo, taludes e bordas e cria o detalhe em corte com o memorial de cálculo.", "Selecione telhados ou pisos antes do comando para somar a área de contribuição."));

        RibbonPanel documentation = app.CreateRibbonPanel(TabName, "Documentação");
        Split(documentation, "Etiquetas", Data<LabelCommand, ProjectAvailability>(assembly, "Etiquetar", "label", "Identifica exemplares e maciços com código, nome popular, nome científico, área e quantidade.", null), Data<UpdateLabelsCommand, ProjectAvailability>(assembly, "Atualizar Etiquetas", "label-update", "Atualiza o texto de todas as etiquetas a partir dos dados atuais.", null), Data<CodeCommand, ProjectAvailability>(assembly, "Codificar", "code", "Atribui códigos aos exemplares e renumera os códigos de espécie no projeto.", null));
        Split(documentation, "Legendas", Data<LegendCommand, ProjectAvailability>(assembly, "Legenda", "legend", "Cria a vista de legenda com símbolos coloridos, fotografias, códigos, nomes e quantidades.", null), Data<PhotoBoardCommand, ProjectAvailability>(assembly, "Quadro de Fotos", "photos", "Coloca ao redor da planta a fotografia de cada espécie, com código e nome, ligada por linha a um exemplar.", "Como nas pranchas de paisagismo ilustradas. Com região de corte ativa, o recorte é ampliado para mostrar os cartões. Rodar de novo substitui o quadro da vista."));
        documentation.AddItem(Data<SheetCommand, ProjectAvailability>(assembly, "Prancha\nAutomática", "sheet", "Cria a prancha de paisagismo com a planta ativa, a legenda e a tabela de quantitativos diagramadas sobre o carimbo.", "A planta fica à esquerda; legenda e tabela na coluna da direita, acima do carimbo. A escala é reduzida automaticamente se a planta não couber."));
        Pulldown(documentation, "Quadros", "Quadros e\nExportação", "spec", "Quadro de especificação, tabelas nativas do Revit e exportação para CAD.", Data<SpecificationBoardCommand, ProjectAvailability>(assembly, "Quadro de Especificação", "spec", "Tabela com código, espécie, quantidade, muda, escavação em centímetros e a especificação de cada espécie, para a prancha. Com uma prancha aberta, o quadro é colocado nela.", null), Data<SchedulesCommand, ProjectAvailability>(assembly, "Tabelas Nativas", "schedule", "Cria as tabelas de vegetação do Revit (por espécie e por exemplar).", null), Data<CadExportCommand, ProjectAvailability>(assembly, "Exportar CAD", "cad", "Exporta vistas e pranchas para DWG (com cores e preenchimentos) e gera a biblioteca de blocos DXF dos símbolos.", null));
        Pulldown(documentation, "Detalhes", "Detalhes e\nMemorial", "detail", "Memorial ilustrado, detalhe de plantio, corte esquemático e crescimento 2D.", Data<MemorialCommand, ProjectAvailability>(assembly, "Memorial Ilustrado", "memorial", "Memorial descritivo em Word editável: capa, partido, paleta vegetal, fichas com foto, calendário de floração, especificações, manutenção e créditos.", "Os dados do projeto (nome, número, cliente, endereço, autor) vêm de Gerenciar > Informações do projeto."), Data<PlantingDetailCommand, ProjectAvailability>(assembly, "Detalhe de Plantio", "detail", "Cova de árvore, berço de palmeira, cova de arbusto, canteiro de forração e gramado, em corte com cotas e notas de especificação.", null), Data<SectionCommand, ProjectAvailability>(assembly, "Corte Esquemático", "section", "Desenhe uma linha na planta: as plantas cortadas aparecem em silhueta com altura e copa reais, no plantio, aos 3 ou 6 anos ou na maturidade.", null), Data<GrowthCommand, ProjectAvailability>(assembly, "Crescimento 2D", "growth", "O mesmo canteiro no plantio, aos 3 e 6 anos e na maturidade, com as copas estimadas pela taxa de crescimento.", null));

        RibbonPanel review = app.CreateRibbonPanel(TabName, "Quantitativos e Verificação");
        Split(review, "Relatorios", Data<ReportsCommand, ProjectAvailability>(assembly, "Relatórios", "reports", "Relatórios por espécie, por maciço e de aquisição, com exportação para Excel.", null), Data<AcquisitionCommand, ProjectAvailability>(assembly, "Aquisição", "excel", "Lista de compra com reserva de reposição, unidade comercial e preço cadastrado.", null), Data<BudgetCommand, ProjectAvailability>(assembly, "Orçamento", "budget", "Orçamento de fornecimento e plantio com tabela de preços importada (SINAPI ou própria) e BDI.", null), Data<MaintenanceCommand, ProjectAvailability>(assembly, "Manutenção", "maintenance", "Cronograma de manutenção por espécie e época: rega, adubação, podas, cortes e reposição de falhas.", null));
        review.AddItem(Data<CheckCommand, ProjectAvailability>(assembly, "Verificar\nProjeto", "check", "Encontra exemplares sem código, códigos repetidos, famílias não cadastradas, maciços inválidos, registros órfãos, conflitos de copa e de espaçamento.", "Clique duas vezes em uma ocorrência para mostrar os elementos. Correções automáticas disponíveis para códigos, dados desatualizados e registros órfãos."));
        review.AddItem(Data<InterferenceCommand, ProjectAvailability>(assembly, "Interfe-\nrências", "interference", "Distâncias entre o plantio e tubulações, eletrodutos, dutos, postes, paredes e pisos, inclusive nos modelos vinculados.", "As distâncias mínimas por tipo de elemento e de planta ficam em Configurações, aba Interferências."));

        RibbonPanel model = app.CreateRibbonPanel(TabName, "Maquete 3D");
        Split(model, "Maquete", Data<MaqueteCommand, ProjectAvailability>(assembly, "Maquete\n3D", "model3d", "Vista 3D do projeto com a vegetação em volume: copas, troncos, palmeiras, arbustos e maciços nas cores das espécies, assentados no terreno.", "Motor botânico: forma de cada tipo de planta (árvores, palmeiras, arbustos, rosetas, herbáceas), qualidade Simplificada, Intermediária ou Detalhada, folhagem, flores e frutos conforme o mês. Do projeto inteiro ou de uma região, no plantio, aos 3 ou 6 anos ou na maturidade. A planta baixa não muda: a maquete só aparece nas vistas 3D, cortes e elevações."), Data<MaqueteUpdateCommand, ProjectAvailability>(assembly, "Atualizar Maquete", "model3d-update", "Refaz a maquete com o mesmo recorte e a mesma fase, depois de mudanças no plantio.", null), Data<MaqueteRemoveCommand, ProjectAvailability>(assembly, "Remover Maquete", "model3d-remove", "Apaga a maquete 3D e os materiais criados para ela. A vegetação 2D não é alterada.", null));
        model.AddItem(Data<FixVegetationCommand, ProjectAvailability>(assembly, "Vegetação\nnas Pranchas", "model3d-update", "Fixa a vegetação ilustrada como formas reais nas vistas 3D das pranchas (ou na vista 3D ativa): sai na impressão, no PDF, nas imagens, no estilo Realista e na renderização.", "O desenho ao vivo aparece só na tela. Fixada, cada vista recebe as formas de frente para a sua câmera, ocultas nas outras vistas e nas plantas. Rode de novo depois de mudar o plantio ou girar a vista; a mesma janela desfaz a fixação."));

        RibbonPanel settings = app.CreateRibbonPanel(TabName, "Configurações");
        Pulldown(settings, "Ajustes", "Configurar", "settings", "Configurações do escritório, escalas de representação, versão e diagnóstico 3D.", Data<SettingsCommand, AlwaysAvailable>(assembly, "Configurações", "settings", "Biblioteca, escalas, padrão gráfico, maciços, codificação, análise, etiquetas e hachuras do escritório.", null), Data<ScaleCommand, ProjectAvailability>(assembly, "Escalas", "scale", "Perfis de representação por escala: detalhado, intermediário, simplificado e mínimo.", "A dimensão real dos símbolos nunca muda; apenas o detalhamento exibido em cada vista."), Data<AboutCommand, AlwaysAvailable>(assembly, "Sobre", "about", "Versão do Viridian e atalhos para as pastas da biblioteca e de configurações.", null), Data<Engine3DTestCommand, ProjectAvailability>(assembly, "Diagnóstico 3D", "model3d-test", "Opcional, para suporte: confere no seu Revit uma família com símbolo 2D e volume 3D e as texturas na vista realista, e guarda um relatório para consulta.", null));
    }

    private static void Pulldown(RibbonPanel panel, string name, string text, string icon, string tooltip, params PushButtonData[] items)
    {
        var data = new PulldownButtonData("VIR_PD_" + name, text)
        {
            ToolTip = tooltip,
            LargeImage = IconFactory.Large(icon),
            Image = IconFactory.Small(icon),
        };
        if (panel.AddItem(data) is PulldownButton button)
        {
            foreach (PushButtonData item in items)
            {
                button.AddPushButton(item);
            }
        }
    }

    /// <summary>Botão com a ação principal e as relacionadas na setinha; o botão mostra sempre a principal.</summary>
    private static void Split(RibbonPanel panel, string name, PushButtonData main, params PushButtonData[] others)
    {
        var data = new SplitButtonData("VIR_SB_" + name, main.Text);
        if (panel.AddItem(data) is SplitButton button)
        {
            button.IsSynchronizedWithCurrentItem = false;
            button.AddPushButton(main);
            foreach (PushButtonData item in others)
            {
                button.AddPushButton(item);
            }
        }
    }

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
}
