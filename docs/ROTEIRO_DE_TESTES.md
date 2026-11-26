# Roteiro de testes no Revit 2027

O código foi compilado contra a API oficial do Revit 2027 e a lógica sem dependência do Revit é coberta por testes automatizados. Este roteiro confere, dentro do Revit, o que só pode ser verificado com o programa aberto. Use um projeto de teste (ou uma cópia de um projeto real).

Se algo falhar, anote a etapa e envie o arquivo mais recente de `%APPDATA%\Viridian\Logs` (Viridian > Sobre abre a pasta).

## Conferência da versão 2.4.1 — estudo solar sem erro de contorno (cerca de 10 minutos)

1. Rode o **Estudo Solar** na mesma planta e região em que aparecia a mensagem "The input curve loops cannot compose a valid boundary": o estudo termina e cria o calendário de luz e os **mapas coloridos** (média do ano e meses escolhidos).
2. Nos mapas, as faixas de cor formam manchas contínuas; com muito zoom aparece uma fresta mínima entre as faixas (some na impressão e no PDF).
3. Marque **cópias da planta com as sombras** (por exemplo 9h, 12h e 15h) e o **quadro de sombras**: tudo é criado sem erro.
4. Gere o **Detalhe de Plantio** de uma palmeira e um **Corte Esquemático** que passe por uma palmeira: todas as folhas aparecem.
5. Se ainda aparecer alguma mensagem de contorno, me envie a imagem e o registro mais recente de `%APPDATA%\Viridian\Logs`.

## Conferência da versão 2.4.0 — desenhos, legenda, memorial e Folha Viridian A1 (cerca de 25 minutos)

1. **Prancha Automática:** numa planta com vegetação, abra a Prancha Automática com a opção **Folha Viridian A1** (primeira da lista). Confira: moldura A1, faixa verde do projeto no alto da coluna direita, notas gerais, rosa dos ventos (girada se o projeto tiver norte indicado), escala gráfica com a escala da planta, revisões e carimbo com os dados de Gerenciar > Informações do projeto e o número da prancha. A planta e a legenda entram **sem o título padrão do Revit** e a planta tem faixa de título própria. Exporte o **PDF**.
2. **Textos e linhas:** nos desenhos abaixo, os títulos saem em negrito, os nomes científicos em itálico, os textos das faixas verdes em branco, e as linhas em três espessuras.
3. **Detalhe de Plantio** (árvore e palmeira): solo, terra vegetal, torrão, brita e cobertura morta com **hachura sobre a cor**; planta com copa, tronco e flores; faixa de título com número e escala e o quadro de especificação embaixo.
4. **Corte Esquemático** e **Crescimento 2D:** figura humana e linhas-guia no corte; nos quatro quadros do crescimento, a linha "altura média · copas cobrem X%".
5. **Legenda:** grupos com subtítulo (Árvores, Palmeiras, Arbustos...), linhas alternadas, símbolo e foto em cada linha. **Quadro de Fotos:** nome popular em negrito e científico em itálico nos cartões; arraste um cartão e confira que a moldura e a linha acompanham.
6. **Quadro de Especificação:** miniatura do símbolo e linha de **total** (mudas e m²). **Jardim de Chuva:** corte com água, camadas hachuradas, setas e o quadro de memória de cálculo. **Gerador de Pisos > detalhe:** amostra da paginação em planta acima do corte.
7. **Estudo Solar:** exporte as imagens: saem em 300 dpi. **Memorial Ilustrado:** capa com faixa verde, mosaico e quadro de dados, sumário e rodapé com número de página.
8. Se algum texto sair sem negrito ou itálico, ou alguma hachura não aparecer, anote o desenho e me envie uma imagem e o registro mais recente de `%APPDATA%\Viridian\Logs`.

## Conferência da versão 2.3.2 — cores no 3D, prancha e faixa nova (cerca de 15 minutos)

Num projeto onde já rodou Vegetação nas Pranchas em versão anterior, rode de novo (as formas e os materiais são refeitos).

1. **Desenho ao vivo:** numa vista 3D com vegetação, passe por **Sombreado**, **Cores consistentes** e **Realista**: as plantas aparecem coloridas nos três. No Realista a cor segue a luz da vista (pode ficar um pouco mais clara).
2. **Prancha:** coloque a vista 3D numa prancha e rode **Maquete 3D > Vegetação nas Pranchas** (fixar nas vistas das pranchas). Abra a vista fixada em Sombreado, Cores consistentes e Realista: as plantas ficam **coloridas, sem preto** (as arestas saem na cor de cada parte). Exporte o **PDF** da prancha: saem coloridas.
3. **Faixa:** nove painéis só com botões grandes. Confira os menus (setinha) de Cadastro, Alterar Plantio, Ajustar Maciço, Modelos de Canteiro, Estudo Solar, Gerador de Pisos, Etiquetar, Legenda, Quadros e Exportação, Detalhes e Memorial, Relatórios, Maquete 3D e Configurar: todas as ferramentas de antes estão lá.
4. Se algo ainda sair preto ou cinza, anote o estilo visual e a vista (ao vivo ou fixada) e me envie uma imagem e o registro mais recente de `%APPDATA%\Viridian\Logs`.

## Conferência da versão 2.3.1 — cores da vegetação no 3D e faixa reorganizada (cerca de 10 minutos)

1. Abra uma vista 3D com vegetação e passe por **Sombreado**, **Cores consistentes** e **Realista**: as plantas aparecem coloridas nos três (nada preto ou cinza). No Realista a cor pode ficar um pouco mais clara ou escura conforme a luz da vista.
2. Coloque a vista 3D numa prancha, rode **Maquete 3D > Vegetação nas Pranchas** e exporte o **PDF**: as plantas saem coloridas.
3. Se no Realista ao vivo as plantas ainda ficarem escuras, anote e me envie uma imagem; para a prancha e o PDF, a fixação (passo 2) já resolve.
4. Confira a faixa: painéis na ordem Biblioteca, Plantio, Maciços, Canteiros, Composição, Pisos e Drenagem, Identificação, Documentação, Detalhamento, Quantitativos, Maquete 3D, Verificação e Configurações. **Floração na Planta** tem o botão *Criar calendário na prancha*; o **Diagnóstico 3D** está em Configurações.

## Conferência da versão 2.3.0 — canteiros, contorno, lista do projeto e floração (cerca de 25 minutos)

1. **Compor Canteiro:** numa planta, escolha uma região de uns 10 × 3 m e clique no lado do muro. Adicione uma palmeira (sugerida como Destaque, 2 un.), um arbusto alto (Fundo), uma herbácea florida (Meio), uma forração em muda (Frente) e uma grama vendida em m² (Base). Confira a prévia: palmeiras espalhadas, faixas na ordem fundo → frente a partir do lado clicado. Mude as porcentagens e o papel de uma espécie e use *Sortear de novo*. Clique em **Plantar**: os exemplares saem com código, a forração vira maciço em hachura e um único Ctrl+Z desfaz tudo.
2. Repita numa região mais arredondada apertando **ESC** no clique do fundo: arranjo em ilha, com o fundo no centro.
3. Na janela de composição, **Salvar como modelo**; feche e abra de novo: o modelo aparece na lista e carrega a receita.
4. **Salvar Canteiro** num canteiro pronto; em **outro projeto**, **Inserir Modelo**: confira a prévia, gire 90° e clique: exemplares, maciços e contorno (linhas de detalhe) aparecem iguais e girados.
5. **Ajustar Contorno:** num maciço em modo símbolos, mova à mão dois exemplares, depois edite o limite da região aumentando um lado e rode o comando: os exemplares movidos continuam onde estavam e só a parte nova recebeu plantas. Diminua o limite e rode de novo: a janela pergunta o que fazer com os de fora; com "Manter e selecionar", eles ficam selecionados.
6. **Substituir** sem nada selecionado > "Trocar uma espécie em todo o projeto": a primeira lista mostra só as espécies do projeto. Troque a espécie de um maciço: os exemplares continuam nas mesmas posições, com os mesmos códigos.
7. **Inserir:** marque **Só do projeto** e confira a coluna **No projeto** (unidades e maciços). Feche o Revit e abra de novo: a escolha fica lembrada.
8. **Floração na Planta** numa planta com maciços e plantas isoladas: cada linha mostra os meses na cor da flor e os meses sem flor. Escolha um mês e **Isolar na vista**: só o que floresce nele fica visível; os óculos da barra da vista voltam ao normal.
9. **Indicar Norte:** num projeto desenhado girado, clique um ponto e a direção do norte; gere o **Estudo Solar** e o quadro de sombras: o sol nasce do lado certo. Rode de novo e apague a indicação.
10. **Cores:** na ficha de uma espécie, clique na amostra da flor: escolha na paleta, use o conta-gotas na foto e confira as cores recentes. Faça o mesmo com a cor de uma pedra.
11. **Planilha:** Buscar Espécie > Planilha > Exportar; no Excel mude um preço e uma cor e acrescente uma linha sem identificador; Importar mostra o resumo antes de gravar e a espécie nova entra com código.
12. **Água:** na ficha, os campos **Tolera seca** e **Tolera encharcamento** são independentes da necessidade de água; o **Jardim de Chuva** sugere para o fundo só plantas com tolerância cadastrada.

## Conferência da versão 2.2.1 — pranchas, impressão e Realista (cerca de 10 minutos)

1. Abra uma vista 3D com vegetação e mude para **Realista**: as plantas continuam aparecendo, com as mesmas cores.
2. Coloque essa vista 3D numa **prancha** e abra a prancha: as plantas aparecem na janela de vista.
3. Rode **Maquete 3D > Vegetação nas Pranchas** e escolha "Fixar nas vistas 3D das pranchas". **Imprima ou exporte a prancha em PDF**: as plantas saem. Confira também uma **renderização** da vista.
4. Abra outra vista 3D (não fixada): as formas fixadas não aparecem nela; o desenho ao vivo continua. Abra uma planta: nada mudou.
5. Mude o plantio ou gire a vista fixada e rode o comando de novo: as formas são refeitas. Na mesma janela, "Desfazer a fixação" volta ao desenho ao vivo.

## Conferência da versão 2.1 — catálogo inteiro, maciços e pedras (cerca de 20 minutos)

1. Num projeto com várias espécies (árvores, palmeiras, arbustos, herbáceas, forrações, trepadeiras), rode **Atualizar Dados** uma vez. Abra a **vista 3D padrão**: todas aparecem ilustradas, sempre de frente para a câmera. Na primeira vez cada espécie pode levar um ou dois segundos para aparecer.
2. **Maciços:** desenhe (ou use) um maciço de gramado, um de forração e um de arbustos em modo hachura. Na vista 3D aparecem um tapete na cor da espécie com textura e, nos de forração e arbustos, plantas ilustradas. Um maciço em modo símbolos aparece pelos próprios exemplares. Ative a caixa de corte e confira que os maciços fora dela somem.
3. **Pedras:** abra Pedras e coberturas, escolha seixo de rio, brita 1 e casca de pinus: a janela mostra a amostra da textura de cada um. Cubra uma região com cada um. Na vista 3D em **Realista**, os pisos mostram a textura na escala real; em Sombreado, a cor média.
4. Gere a **Maquete 3D**: plantas e maciços ilustrados não são duplicados.

## Conferência da versão 2.0 — vegetação ilustrada como RPC (piloto, cerca de 15 minutos)

Nada precisa ser enviado: é só para você ver o resultado. Espécies do piloto: ipê-amarelo, flamboyant, jerivá, palmeira-azul, araucária, agave-dragão, estrelítzia e dracena-de-madagascar.

1. Num projeto de teste, insira algumas espécies do piloto e algumas fora dele. Em um projeto que já tinha essas espécies, rode **Atualizar Dados** antes (Verificar Projeto mostra "símbolo ou vegetação ilustrada" desatualizados).
2. **Planta baixa:** os símbolos continuam iguais.
3. Abra a **vista 3D padrão** (a casinha): as espécies do piloto aparecem como imagens ilustradas. Gire a vista em volta e de cima a baixo: a planta fica sempre de frente, sem faces aparentes; a imagem muda um pouco a cada 22,5° e, olhando bem de cima, passa à vista da copa. Na primeira vez pode levar um ou dois segundos até cada espécie aparecer.
4. Troque o estilo visual: **Sombreado, Cores consistentes e Realista** com as mesmas cores; **Linha oculta** em preto e branco; **Estrutura de arame** só com o contorno. Confira que não há disco colorido no chão sob as plantas.
5. Abra um **corte** e uma **elevação**: as plantas aparecem de frente para a vista.
6. Clique no **tronco** de uma planta na vista 3D: é o mesmo elemento da planta (mesmo código, mesma etiqueta, mesma linha na tabela). Mude **VIR Fase 3D** para 1, 2, 3 e 4 (e 5 no ipê ou flamboyant): a imagem muda de fase. Desmarque **VIR 3D visível**: a imagem some só nesse exemplar.
7. Visibilidade/Gráficos > Vegetação > **VIR Volume 3D** esconde as imagens na vista.
8. Gere a **Maquete 3D**: as espécies do piloto não são duplicadas.
9. Se quiser, confira a **impressão em PDF** de uma vista 3D com as plantas (com processamento como imagem) e anote se as imagens saem.
10. Em **Configurações**, desmarque "Vegetação ilustrada em 3D nas famílias": as imagens somem (rode Atualizar Dados para as famílias voltarem a ser só 2D).

## Teste da versão 1.9 — motor 3D botânico (cerca de 30 minutos)

Faça num projeto de teste (ou numa cópia) com árvores, palmeiras, arbustos, herbáceas, uma bromélia, um maciço de forração e um gramado, de preferência com um toposólido.

### B. Maquete 3D com o motor botânico

1. **Maquete 3D**: a janela nova mostra recorte, motor (Botânico marcado), qualidade, época do ano, fase, flores, frutos, terreno e arestas, com a estimativa de triângulos embaixo. Troque a qualidade e veja a estimativa mudar.
2. Gere no **Intermediário, sem estação, na maturidade**. Confira: árvores com copa em grupos e galhos, ipês com copa irregular, palmeiras com folhas pinadas arqueadas (jerivá) ou em leque, bromélias e agaves em roseta, estrelítzia em leque com a flor, gramíneas em touceira, forração baixa. A vista abre sombreada **sem arestas**. O resumo traz triângulos totais e a média por exemplar.
3. **Plantas baixas sem alteração:** as plantas continuam só com os símbolos 2D.
4. Gere de novo com **Estação ligada, mês de agosto**: ipês **sem folhas e amarelos** (floridos), decíduas sem folhas e semidecíduas com folhagem rala. O resumo informa quantos exemplares estão sem folhas, em flor (e quantos com época estimada pelo gênero) e com frutos.
5. Mês de **janeiro** com **Frutos** marcados: ipês com folhas e sem flores; jerivás e frutíferas com frutos apenas na época (o resumo informa).
6. **Detalhada** numa região pequena: placas de folha nas copas e folíolos mais densos nas palmeiras. **Simplificada** no projeto inteiro: massas leves.
7. **Maquete pesada:** em Configurações o aviso é de 3 milhões de triângulos; num projeto grande no nível Detalhado, o Viridian pede confirmação e oferece o nível que cabe. Escolha "Gerar no nível …" e confira que gerou no nível sugerido.
8. **Atualizar Maquete** depois de mover uma árvore: refaz com as mesmas escolhas (qualidade, mês, frutos).
9. **Maquete antiga:** abra um projeto com maquete feita na 1.8 e use **Atualizar Maquete**: ela continua com o visual antigo (gerador simples). Gerando pela janela com o motor Botânico, passa para o novo.
10. **Ficha da espécie:** Buscar Espécie > editar um ipê: há os campos **Perfil 3D** (Automático, mostrando qual), **Frutificação** e **Cor do fruto**, e uma linha dizendo de onde vêm as épocas (ficha ou estimada pelo gênero). Mude o Perfil 3D de uma espécie (por exemplo, para Árvore colunar), gere a maquete e confira; o resumo informa as espécies com perfil escolhido.
11. **Desfazer** (Ctrl+Z) desfaz a geração inteira; **Remover Maquete** apaga formas, materiais e vista como antes.
12. **Gerador simples:** escolha "Gerador simples" na janela: a maquete fica igual à das versões anteriores (para comparar o desempenho).

Anote o tempo de geração e a fluidez para girar a vista em cada qualidade (projeto inteiro): esses números entram no ajuste dos orçamentos da 2.1.

## Teste rápido da versão 1.5 (cerca de 20 minutos)

Correções pedidas na 1.4. Faça num projeto de teste; em projetos antigos, rode antes **Atualizar Dados**.

1. **Cores no Revit (prioridade):** Inserir três espécies de cores diferentes (por exemplo, um ipê, uma palmeira e um arbusto florido) numa planta com estilo visual "Linhas ocultas" ou "Sombreado" e nível de detalhe Médio/Fino. Os símbolos aparecem com a copa colorida e as manchas de flor, como na biblioteca, e as linhas do símbolo por cima da cor. Se aparecer o aviso de cores, clique em "Mostrar detalhes" e envie um print da mensagem.
2. **Cores em projeto antigo:** abra um projeto feito na 1.4 com símbolos em preto e branco e rode **Atualizar Dados**: os símbolos são regerados coloridos, sem mudar posição, código ou etiquetas.
3. **Maciços:** crie maciços de quatro espécies diferentes (arbustos e forrações). Nenhum deles traz árvores no meio (o modo padrão é Hachura); cada um tem traçado diferente e as cores da espécie (fundo na cor da folhagem, traços na cor da flor). Um segundo maciço da mesma espécie repete o traçado do primeiro. Árvores e palmeiras continuam podendo usar Símbolos ou Combinado.
4. **Quadro de fotos:** crie o quadro e arraste uma foto para outro lugar: o texto e a linha de ligação acompanham. Mova a planta ligada a ela: a linha segue a planta. Desfazer (Ctrl+Z) volta tudo junto. Quadros criados na 1.4 não acompanham: apague-os e crie de novo.
5. **Gerador de pisos:** aplique um piso da biblioteca a um piso do Revit; o erro "EndCap condition" não aparece mais e o tipo recebe as camadas. Repita com um piso de outro tipo-base (por exemplo, um piso genérico de 300 mm).
6. **Nova Vegetação:** Biblioteca > **Nova Vegetação**, sem projeto aberto: preencha nome popular, científico, categoria, porte e cores e salve. Ela aparece em Buscar Espécie. Com um projeto aberto, a janela oferece Inserir, Distribuir ou Criar maciço; insira e confira as cores no Revit.

## Teste rápido da versão 1.4 (cerca de 30 minutos)

Faça antes de liberar a versão para o escritório, num projeto de teste com vegetação, maciços, pisos e uma edificação.

1. **Carregamento:** a aba mostra os doze painéis, inclusive Pisos e Drenagem; o Estudo Solar aparece no painel Composição e o Quadro de Especificação no painel Documentação.
2. **Ferramentas antigas (sem regressão):** Inserir uma espécie, Distribuir num canteiro, Criar Maciço, Etiquetar, Legenda, Relatórios e Prancha Automática funcionam como antes.
3. **Compras:** Relatórios > aba Compras > "Marcar a lista atual como comprada"; feche; troque uma espécie por outra (Substituir) e reabra: a espécie nova aparece "a comprar" e a antiga "fora do projeto: sobram N".
4. **Quadro de Especificação** com uma prancha aberta: o quadro entra no centro da prancha, com a escavação em centímetros.
5. **Memorial:** a seção 5 traz o quadro "Covas e escavação".
6. **Gerador de Pisos:** escolha "Intertravado 10×20×6 espinha de peixe", selecione um piso do Revit, abra o gerador e use "Aplicar aos pisos selecionados". Confira a paginação na planta e numa vista 3D, e as camadas no tipo do piso.
7. **Jardim de Chuva:** selecione um telhado, rode o comando, escolha um canteiro e crie o detalhe em corte.
8. **Estudo Solar:** rode numa região ao lado da edificação, na média do ano: é criada uma cópia da planta com o mapa; a planta original não muda.
9. **Desligar módulos:** em Configurações > Composição > Módulos, desligue o Gerador de pisos; o botão avisa que o módulo está desativado. Religue.

Se algum módulo novo falhar, desligue-o nas configurações: o restante do plugin continua funcionando. Envie o registro de erros (Viridian > Sobre).

## 1. Carregamento

1. Instale com `Instalar.cmd` e abra o Revit 2027.
2. Escolha **Sempre carregar** no aviso de segurança.
3. Confira a aba **Viridian** com os painéis Biblioteca, Plantio, Maciços, Identificação, Quantitativos, Documentação, Composição, Detalhamento, Pisos e Drenagem, Maquete 3D, Verificação e Configurações, todos com ícones.
4. Sem projeto aberto, **Buscar Espécie**, **Importar Família**, **Ateliê de Paletas**, **Gerador de Pisos**, **Configurações** e **Sobre** ficam disponíveis; os demais, desativados.

## 2. Biblioteca

1. Buscar Espécie: pesquise "ipe amarelo" (sem acento) e filtre Árvore + Nativa + altura 5 a 8 m.
2. Filtre por bioma (Cerrado), por mês de floração (setembro) e "somente com floração vistosa".
3. Confira o símbolo colorido nos quatro perfis da ficha, a fotografia e o crédito (autor e licença) abaixo dela.
4. Cadastre uma espécie nova com fotografia, biomas, cor da folhagem e cor da flor; edite, duplique e exclua. Ao trocar a foto, o crédito da foto do catálogo deve sumir.

## 3. Plantio

1. Em uma planta 1:100, clique em **Inserir**: a janela rápida deve abrir com a pesquisa, as espécies recentes no topo, o símbolo colorido e a foto. Escolha Ipê-amarelo e clique em Inserir; o símbolo colorido deve acompanhar o cursor. Clique três vezes e pressione ESC. **Os exemplares devem aparecer coloridos na vista.**
   - Na primeira vez, o Viridian gera a família 2D (alguns segundos), com a copa preenchida e as manchas amarelas da floração.
   - Os exemplares devem receber códigos AR.0001, AR.0002, AR.0003 e variantes sorteadas.
2. **Distribuir** um arbusto em uma região preenchida em todos os padrões: triangular, retangular, natural, densidade, bordadura (2 fileiras), anéis, agrupamentos e degradê. Confira a contagem da pré-visualização contra a quantidade inserida e se todas as plantas ficam dentro do canteiro.
3. Distribuir com **Respeitar exemplares existentes** e **Girar aleatoriamente** marcados: as plantas devem girar no próprio lugar, sem sair das posições da pré-visualização.
4. **Plantio Linear**, conferindo que todas as plantas ficam sobre o eixo:
   - Linha reta: dois cliques (conclui sozinho), espaçamento 3 m, "plantar nas duas pontas".
   - Polilinha de quatro pontos com 2 fileiras alternadas.
   - Círculo de raio 5 m com 12 plantas (modo Quantidade).
   - Linhas existentes: selecione duas linhas de detalhe que se tocam e uma parede; depois repita selecionando antes de iniciar o comando.
5. **Substituir** dois exemplares por outra espécie: posição e código devem se manter.
6. **Editar**: mude a situação para "Existente" e a variante gráfica.
7. Ctrl+Z deve desfazer cada operação inteira em um passo.

## 4. Escalas

1. Mude a escala da planta para 1:50, 1:200 e 1:500. O detalhamento dos símbolos deve mudar sozinho, com o contorno sempre no mesmo tamanho real.
2. Ligue manualmente uma camada "VIR Detalhe" em Visibilidade/Gráficos a 1:200 e altere outra propriedade da vista: o ajuste manual deve ser respeitado.
3. Escalas > fixar o perfil "Detalhado" em uma vista 1:200.
4. Uma vista controlada por modelo de vista: o perfil deve ser aplicado ao modelo.

## 5. Maciços

1. Criar Maciço com Grama-amendoim (densidade alta → hachura), com um arbusto (densidade média → combinado) e com uma árvore (baixa → símbolos).
2. Crie um maciço a partir de uma região preenchida já existente.
3. Altere o limite de uma região e use **Recalcular**: área e quantidade devem acompanhar.
4. **Editar Maciço** mudando o código e a representação.
5. Grama-batatais (fornecida em m²): a quantidade deve ser a área.

## 6. Identificação e documentação

1. **Etiquetar** a vista inteira com cada modelo, com e sem linha indicadora, e "uma por espécie".
2. **Codificar** > renumerar os códigos de espécie no projeto; as etiquetas devem se atualizar.
3. **Legenda**: confira símbolos coloridos, fotos, hachuras e quantidades.
4. **Quadro de Fotos** em uma planta com várias espécies: cada espécie deve ganhar um cartão com foto, código e nome nas margens do desenho, com linha até um exemplar, sem linhas cruzadas. Rode de novo: o quadro deve ser substituído, não duplicado.
4. **Tabelas Nativas**: tabela por espécie (agrupada, com total) e por exemplar.
5. **Relatórios** > **Exportar para Excel**: abra a planilha e confira as três abas e os totais.

## 7. Verificação

1. Copie um exemplar sobre ele mesmo e duplique um código manualmente.
2. Aproxime duas árvores grandes; exclua um maciço deixando seus símbolos.
3. **Verificar Projeto** deve listar: elemento duplicado, código duplicado, conflito de copa, registro órfão.
4. Use as correções automáticas e verifique novamente.

## 8. Cores

1. Configurações > Cores: troque para "Reticulado colorido", mude a cor-base das árvores e desligue a floração. A pré-visualização da aba deve acompanhar.
2. Atualizar Dados: as famílias devem ser regeradas com as novas cores.
3. Mude a vista para 1:500 (perfil Mínimo): o preenchimento deve sumir; a 1:200 a floração some e a copa permanece.
4. Crie um maciço com hachura: fundo claro e hachura mais escura, na cor da espécie. Confira a legenda colorida.

## 9. Orçamento e manutenção

1. Relatórios > Orçamento > Importar tabela de preços: use uma planilha de insumos do SINAPI salva como `.xlsx` (ou um `.csv` com código, descrição, unidade e preço).
2. Selecione uma espécie e use **Vincular insumo** e **Vincular serviço**; o orçamento deve mostrar a fonte "Tabela", o BDI aplicado e os totais.
3. Mude o BDI e a referência; feche e reabra: os valores devem ser mantidos.
4. Aba Manutenção: troque o mês do plantio e a estação seca; confira rega, podas após a floração e cortes de grama.
5. Exporte para Excel: as abas Orçamento e Manutenção devem estar na planilha.

## 10. Prancha e CAD

1. Com um carimbo A1 carregado, use **Prancha Automática** em uma planta com vegetação: planta à esquerda, legenda e tabela à direita, acima do carimbo.
2. Repita com a mesma planta: deve ser criada uma vista dependente.
3. Com uma região de corte grande, a escala deve ser reduzida para caber (e o aviso aparecer).
4. **Exportar CAD**: exporte a prancha e a planta em DWG 2018 e gere a biblioteca DXF; abra os arquivos em um programa de CAD e confira cores, camadas e escala (1 unidade = 1 m).

## 11. Interferências

1. Vincule um modelo de instalações com tubulações e luminárias e um modelo de arquitetura.
2. Posicione uma árvore a 1 m de um tubo, outra a 2 m de um poste e uma árvore grande a 2 m de uma parede.
3. **Interferências** deve listar as três ocorrências, com o nome do vínculo e as distâncias.
4. Coloque uma árvore sobre um piso sem abertura e outra dentro de um berço (abertura no piso): só a primeira deve ser apontada.
5. Configurações > Interferências: altere as distâncias e verifique novamente.

## 12. Acessibilidade

1. Configurações > Acessibilidade: tamanho 130%. As janelas seguintes devem abrir ampliadas, sem cortar conteúdo.
2. Marque "Cores de alto contraste" e, depois, ative o alto contraste do Windows: textos e bordas devem ficar nítidos.
3. Percorra a janela de distribuição só com o teclado (Tab, Alt + letra, Enter e Esc).

## 13. Biblioteca do escritório

1. Configurações > Biblioteca: aponte para uma pasta de rede.
2. Importe um `.pat` em Configurações > Hachuras e use-o em um maciço.
3. Importar Família: registre uma família de Vegetação própria para uma espécie e aceite atualizar os exemplares do projeto.
4. Feche e reabra o projeto: maciços, etiquetas e códigos devem continuar vinculados.

## 14. Composição

1. **Ateliê de Paletas** sem projeto aberto: escolha a paleta pronta "Quente tropical"; a lista deve trazer primeiro espécies de flor amarela, laranja ou vermelha, com símbolo e foto.
2. Troque para a harmonia **Complementar** e mova a cor-base: a lista se reordena. Marque só os meses de inverno e "Somente com floração vistosa": aparecem as espécies que florescem nesses meses.
3. Com um projeto aberto em planta, use **Inserir** e **Distribuir** a partir do Ateliê.
4. Buscar Espécie: a ficha mostra **Textura** e **Escala visual**. Edite uma espécie, troque a textura, salve e confira na ficha.
5. **Verificar Canteiro** em um canteiro com uma espécie dominando (por exemplo, 20 plantas de uma e 2 de outra): deve haver aviso de equilíbrio. Em um canteiro só de texturas finas, a observação de contraste de texturas. Clique duplo mostra as plantas citadas; **Verificar novamente** refaz a análise depois de alterar o canteiro.
6. Configurações > Composição: altere a participação máxima de uma espécie e verifique o canteiro de novo; o resultado deve acompanhar o novo limite.
7. **Calendário de Floração**: cria a vista de desenho "Viridian - Calendário de floração" com os doze meses, as cores das flores e os meses sem floração. Rode de novo depois de inserir outra espécie: a mesma vista é atualizada, sem duplicar.

## 15. Detalhamento

1. **Detalhe de Plantio** > Todas as espécies do projeto: uma vista de desenho por tipo de plantio (árvore, palmeira, arbusto, forração, gramado) presente no projeto, em escala 1:20, com cotas legíveis, chamadas sem sobreposição e as notas de especificação de cada espécie abaixo.
2. Detalhe de Plantio > Uma espécie: escolha uma árvore com muda alta; a cova deve ser maior que a de uma muda pequena.
3. Configurações > Composição: edite o modelo de especificação de árvore (por exemplo, acrescente "Irrigar após o plantio."). Um campo inexistente, como `{xyz}`, deve ser recusado ao salvar. Refaça o detalhe e confira a nota.
4. **Corte Esquemático** > Na maturidade: clique em dois pontos atravessando um canteiro com árvores, palmeiras e arbustos. A vista mostra as silhuetas na altura e copa adultas, a régua de alturas e os códigos. Repita com "No plantio": as plantas devem aparecer menores.
5. **Crescimento 2D**: escolha um canteiro. A vista mostra quatro quadros (plantio, 3 anos, 6 anos, maturidade), com as copas crescendo e o canteiro fechando.
6. Coloque as vistas criadas em uma prancha e imprima em PDF: textos, cores e linhas devem sair corretos.
7. **Memorial Ilustrado**: preencha antes Gerenciar > Informações do projeto (nome, número, cliente, endereço, autor). Salve o `.docx` e abra no Word: capa com fotos, partido, paleta, fichas com foto, calendário, especificações, manutenção e créditos. Edite um texto e salve: o arquivo deve continuar íntegro.

## 16. Maquete 3D

Use um projeto com árvores, palmeiras, arbustos, um maciço de forração e um gramado, de preferência com um toposólido inclinado.

1. **Maquete 3D** > na janela, Projeto inteiro, Gerador simples, Adulta (maturidade): abre a vista "Viridian - Maquete 3D" com sombreamento e sombras. (O motor botânico está no teste da versão 1.9, no início deste roteiro.) Cada planta aparece em volume, nas cores das espécies, com tronco marrom e manchas de flor nas espécies floridas. O resumo informa quantos exemplares e maciços foram criados.
2. **Plantas baixas sem alteração:** abra as vistas em planta do projeto. Devem mostrar apenas os símbolos 2D, como antes; nenhum volume da maquete pode aparecer. Se aparecer, anote: o resumo da maquete também avisa quando o Revit não aceita ocultá-la nas plantas.
3. **Terreno:** com o toposólido inclinado, as plantas e os maciços devem ficar apoiados no terreno, sem flutuar nem afundar. Um vaso ou jardim sobre laje (piso no nível superior) deve ficar sobre a laje.
4. **Maciços:** o maciço de forração e o gramado aparecem como volume baixo que acompanha o contorno, inclusive recortes; maciços de árvores aparecem como árvores individuais.
5. **Região:** em uma planta, Maquete 3D > Uma região da planta > No plantio (mudas) > Gerar maquete > desenhe um contorno. A maquete anterior é substituída, só a vegetação da região aparece, menor (porte do plantio), e a vista é recortada em torno dela.
6. **Atualizar Maquete:** mova uma árvore e acrescente outra na planta; Atualizar Maquete refaz com a mesma região e a mesma fase, já com as mudanças.
7. **Cortes e elevações:** abra um corte que atravesse o jardim; as árvores aparecem em volume (em projetos com subprojetos, só depois de ligar o subprojeto "VIR Maquete 3D" na vista).
8. **Sem efeito colateral:** rode Relatórios, Tabelas Nativas e Verificar Projeto: as quantidades devem ser as mesmas de antes da maquete.
9. **Desfazer:** Ctrl+Z desfaz a geração inteira em um passo.
10. **Remover Maquete** com a vista da maquete aberta: o Viridian troca de vista, apaga as formas, os materiais "VIR Maquete" e a vista. Coloque a vista em uma prancha antes de remover: nesse caso a vista é mantida.
11. Configurações > Composição > Maquete 3D: reduza o máximo de exemplares para 10 e gere de novo; o resumo informa quantos ficaram de fora. Desmarque as manchas de flor e confira.

## 17. Compras e quadro de especificação

1. Relatórios > **Compras**: digite quantidades na coluna Comprado (inclusive com ponto de milhar, como 1.200); valores inválidos (texto, negativos) são recusados.
2. "Completar só o que falta" ajusta apenas os itens em falta. "Zerar compras" pede confirmação.
3. Feche e reabra o projeto: as compras continuam gravadas. Exporte para Excel: há a aba **Compras**.
4. Desfazer (Ctrl+Z) depois de fechar os relatórios desfaz o registro das compras.
5. **Quadro de Especificação** sem prancha aberta: cria a vista de desenho "Viridian - Quadro de especificação". Rode de novo depois de mudar uma nota em Configurações > Composição: a mesma vista é atualizada.
6. Com a vista já colocada em outra prancha, rode com uma prancha diferente aberta: o Viridian avisa que o quadro já está em outra prancha.

## 18. Gerador de pisos

1. Sem projeto aberto, o gerador abre: edite, duplique, crie e exclua pisos; os botões que dependem do Revit ficam desativados.
2. Mude comprimento, junta, rotação e paginação: a prévia acompanha em escala real (amostra de 2 m). Valores inválidos aparecem em vermelho e não são gravados.
3. Edite um piso padrão, salve e use "Restaurar": volta aos valores do Viridian.
4. Camadas: acrescente, remova, suba e desça; uma camada com espessura zero só é aceita como manta.
5. **Criar tipo de piso**: confira em Editar tipo > Estrutura o revestimento como acabamento na face superior e as camadas abaixo. Mude a junta no gerador e crie de novo: o mesmo tipo é atualizado (pisos que já o usam acompanham).
6. **Aplicar aos pisos selecionados**: os pisos trocam de tipo; Ctrl+Z desfaz em um passo.
7. **Hachura para plantas**: Anotar > Região preenchida, escolha o tipo "VIR Piso ..." e desenhe; a paginação aparece em escala real, também ao mudar a escala da vista.
8. **Detalhe em corte**: vista de desenho 1:10 com as camadas, cotas e chamadas.
9. Quantitativo: com pisos aplicados, a lista do gerador mostra a área, a compra com perda e as peças; exporte para Excel e confira as camadas em m³ (mantas em m²).
10. Biblioteca na rede: aponte Configurações > Biblioteca para uma pasta compartilhada e confira que outro computador vê os pisos criados.

## 19. Jardim de chuva e tolerância ao encharcamento

1. Buscar Espécie: a ficha mostra "Tolera encharcamento" (com "estimativa" quando não cadastrada). No cadastro, escolha um valor e confira na ficha.
2. Selecione um ou mais telhados e rode o Jardim de Chuva: a área de contribuição vem preenchida.
3. Escolha o canteiro na planta: a área do jardim vem preenchida e o resultado diz se é suficiente. Com área insuficiente, aparece o aviso.
4. Informe a infiltração do solo (por exemplo, 25 mm/h): aparece o tempo de esvaziamento; com 0, o resultado pede o ensaio e prevê dreno de fundo.
5. As listas de espécies sugeridas (fundo, taludes, bordas) não trazem invasoras.
6. "Salvar como padrão" e reabra: os valores voltam. "Criar detalhe em corte" cria a vista com o corte e o memorial de cálculo.
7. Desligue o módulo em Configurações > Módulos e confira o aviso ao clicar no botão.

## 20. Estudo solar

0. **Projeto sem localização:** num projeto novo, rode o Estudo Solar. A janela avisa que o projeto não tem localização e recomenda informar as coordenadas. No Google Maps, clique com o botão direito no terreno, copie os números, cole em "Colar coordenadas" e clique em Usar: latitude, longitude e fuso são preenchidos. Deixe "Gravar estas coordenadas" marcado; depois do estudo, Gerenciar > Localização mostra as mesmas coordenadas.
1. Gerenciar > Localização: defina a cidade do projeto. Rode o Estudo Solar **mês a mês** numa região com árvores e uma edificação: a janela mostra as coordenadas do projeto e o resumo mostra o local usado.
1a. **Calendário de luz:** a vista de desenho "Viridian - Calendário de luz - ..." mostra nascer e pôr do sol de cada mês (compare com um site de efemérides da cidade: diferença de poucos minutos), as horas da região e de cada espécie, com × nos meses em que não atende e a coluna "atende". Rodar de novo na mesma planta atualiza o mesmo calendário.
1b. **Mapas:** marque "Média do ano", jun e dez: são criadas três cópias da planta, cada uma com a legenda das cinco faixas e o período.
1c. **Faixas:** mude "Sol pleno a partir de" para 5 h: a legenda e as marcações mudam. "Restaurar faixas padrão" volta a 6/3/1 h.
1d. **Sugestões:** numa planta de sol pleno à sombra da edificação, a lista mostra "não atende em N de 12 meses" e sugere espécies de sombra ou meia-sombra da mesma categoria. Nada é trocado no projeto.
1e. **Sombras hora a hora:** com Junho e Dezembro marcados, horário das 7 às 17 h e "Sombras na planta às" 9, 12, 15: são criados dois quadros "Viridian - Sombras por horário - ..." e seis cópias da planta. No quadro de junho (hemisfério sul), as sombras das árvores ficam a oeste às 7–9 h, ao sul e curtas ao meio-dia e a leste à tarde. Confira uma das plantas com sombras numa vista 3D do Revit com sombras ligadas, no mesmo dia e hora (Configurações do sol): a direção e o comprimento devem ser parecidos.
1f. **Porte:** rode de novo com "No plantio (mudas)": as sombras ficam muito menores. **Decíduas:** um ipê em julho faz sombra rala (meia-luz); em janeiro, sombra cheia. Na ficha da espécie, o campo Folhagem e os meses sem folhas mudam esse comportamento.
1g. **Imagens:** marque "Exportar imagens (PNG)": as imagens ficam na pasta "Viridian - Estudo solar", ao lado do arquivo do projeto.
2. **Norte:** gire o norte verdadeiro do projeto (Gerenciar > Posição > Girar norte verdadeiro) e rode de novo no solstício de inverno. Ligue as sombras do Revit numa vista 3D com o mesmo dia ao meio-dia: as áreas de sombra do mapa devem ficar do mesmo lado que as sombras do Revit. Se ficarem espelhadas ou giradas, ajuste "Ajuste do norte" em Configurações > Composição e anote o caso.
3. O mapa é criado numa cópia da planta ("Viridian - Estudo solar - ..."), com legenda; a planta original não muda.
4. A lista aponta plantas que não recebem a luz que pedem; clique duplo mostra a planta.
5. Desligue "Considerar edificações e terreno": o cálculo fica mais rápido e só as copas fazem sombra.
6. Ctrl+Z desfaz o estudo inteiro (vista e mapa) em um passo.

## 21. Fotos das espécies

0. As fotos do catálogo foram revisadas para mostrar a planta inteira (porte, copa, touceira). Percorra algumas fichas em Buscar Espécie; se alguma não agradar, troque-a como abaixo.

1. Buscar Espécie > ficha > **Trocar foto...**: escolha um JPG ou PNG grande; a ficha mostra a foto nova na hora, com o crédito "Foto do escritório".
2. No navegador, clique com o botão direito numa foto e escolha "Copiar imagem"; na ficha, **Colar imagem**.
3. Arraste um arquivo de imagem do Explorador para cima da foto da ficha.
4. Editar espécie: o campo **Crédito da foto** aceita o autor ou a fonte; confira na ficha, na legenda e no memorial.
5. **Foto do catálogo** volta à foto original. A foto trocada aparece também na legenda, no quadro de fotos e no memorial.
6. Com a biblioteca numa pasta de rede, a foto trocada aparece nos outros computadores do escritório.

## 22. Pedras e coberturas

1. Pisos e Drenagem > **Pedras e Coberturas**, sem projeto aberto: a janela abre com 31 materiais. Filtre por tipo (Seixo) e por uso (Borda de caminho). Edite a cor de "Brita colorida", salve, feche e reabra: a cor ficou gravada. Use **Novo** para criar uma pedra própria.
2. **Quanto comprar:** com "Seixo branco" e uso Cobertura de canteiro, digite 10 m²: o resultado mostra cerca de 0,55 m³, 0,85 t e 43 sacos de 20 kg.
3. **Cobrir região:** numa planta, escolha "Casca de pinus média", uso Cobertura morta, e clique em Cobrir região; escolha um canteiro (região preenchida, piso ou contorno desenhado). É criado um piso com a cor e as lascas da casca; as plantas continuam visíveis por cima.
4. **Borda de caminho:** selecione um piso de caminho, escolha "Seixo preto", uso Borda de caminho, largura 30 cm, lado "para fora", e clique em Borda de caminho: a faixa contorna o caminho por fora. Repita com linhas desenhadas ao longo de um caminho e com a opção Desenhar a borda. Se sair do lado errado, Ctrl+Z e escolha o outro lado.
5. **Aplicar:** selecione pisos existentes, abra a janela e aplique "Pedrisco de granito" como piso solto: o tipo recebe a manta e a base de 10 cm.
6. **Quantitativo:** abra o Gerador de Pisos: os pisos de pedra aparecem na lista com volume, peso e sacos; exporte para Excel.
7. **Jardim de chuva:** escolha "Brita 2" como camada drenante (a porosidade muda para 42%) e "Casca de pinus média" como cobertura morta; o detalhe em corte mostra os dois nomes.
8. Em planta 1:100 e 1:50, confira se o desenho dos grãos fica legível. Se ficar denso demais, aumente o grão máximo do material ou use a hachura para plantas.
