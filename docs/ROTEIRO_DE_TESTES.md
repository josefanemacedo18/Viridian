# Roteiro de testes no Revit 2027

O código foi compilado contra a API oficial do Revit 2027 e a lógica sem dependência do Revit é coberta por testes automatizados. Este roteiro confere, dentro do Revit, o que só pode ser verificado com o programa aberto. Use um projeto de teste (ou uma cópia de um projeto real).

Se algo falhar, anote a etapa e envie o arquivo mais recente de `%APPDATA%\Viridian\Logs` (Viridian > Sobre abre a pasta).

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

1. **Maquete 3D** > Projeto inteiro > Na maturidade: abre a vista "Viridian - Maquete 3D" com sombreamento e sombras. Cada planta aparece em volume, nas cores das espécies, com tronco marrom e manchas de flor nas espécies floridas. O resumo informa quantos exemplares e maciços foram criados.
2. **Plantas baixas sem alteração:** abra as vistas em planta do projeto. Devem mostrar apenas os símbolos 2D, como antes; nenhum volume da maquete pode aparecer. Se aparecer, anote: o resumo da maquete também avisa quando o Revit não aceita ocultá-la nas plantas.
3. **Terreno:** com o toposólido inclinado, as plantas e os maciços devem ficar apoiados no terreno, sem flutuar nem afundar. Um vaso ou jardim sobre laje (piso no nível superior) deve ficar sobre a laje.
4. **Maciços:** o maciço de forração e o gramado aparecem como volume baixo que acompanha o contorno, inclusive recortes; maciços de árvores aparecem como árvores individuais.
5. **Região:** em uma planta, Maquete 3D > Uma região da planta > desenhe um contorno > No plantio. A maquete anterior é substituída, só a vegetação da região aparece, menor (porte do plantio), e a vista é recortada em torno dela.
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

1. Gerenciar > Localização: defina a cidade do projeto. Rode o Estudo Solar numa região com árvores e uma edificação: o resumo mostra a latitude usada.
2. **Norte:** gire o norte verdadeiro do projeto (Gerenciar > Posição > Girar norte verdadeiro) e rode de novo no solstício de inverno. Ligue as sombras do Revit numa vista 3D com o mesmo dia ao meio-dia: as áreas de sombra do mapa devem ficar do mesmo lado que as sombras do Revit. Se ficarem espelhadas ou giradas, ajuste "Ajuste do norte" em Configurações > Composição e anote o caso.
3. O mapa é criado numa cópia da planta ("Viridian - Estudo solar - ..."), com legenda; a planta original não muda.
4. A lista aponta plantas que não recebem a luz que pedem; clique duplo mostra a planta.
5. Desligue "Considerar edificações e terreno": o cálculo fica mais rápido e só as copas fazem sombra.
6. Ctrl+Z desfaz o estudo inteiro (vista e mapa) em um passo.

## 21. Fotos das espécies

1. Buscar Espécie > ficha > **Trocar foto...**: escolha um JPG ou PNG grande; a ficha mostra a foto nova na hora, com o crédito "Foto do escritório".
2. No navegador, clique com o botão direito numa foto e escolha "Copiar imagem"; na ficha, **Colar imagem**.
3. Arraste um arquivo de imagem do Explorador para cima da foto da ficha.
4. Editar espécie: o campo **Crédito da foto** aceita o autor ou a fonte; confira na ficha, na legenda e no memorial.
5. **Foto do catálogo** volta à foto original. A foto trocada aparece também na legenda, no quadro de fotos e no memorial.
6. Com a biblioteca numa pasta de rede, a foto trocada aparece nos outros computadores do escritório.
