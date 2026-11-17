# Roteiro de testes no Revit 2027

O código foi compilado contra a API oficial do Revit 2027 e a lógica sem dependência do Revit é coberta por testes automatizados. Este roteiro confere, dentro do Revit, o que só pode ser verificado com o programa aberto. Use um projeto de teste (ou uma cópia de um projeto real).

Se algo falhar, anote a etapa e envie o arquivo mais recente de `%APPDATA%\Viridian\Logs` (Viridian > Sobre abre a pasta).

## 1. Carregamento

1. Instale com `Instalar.cmd` e abra o Revit 2027.
2. Escolha **Sempre carregar** no aviso de segurança.
3. Confira a aba **Viridian** com os painéis Biblioteca, Plantio, Maciços, Identificação, Quantitativos, Documentação, Verificação e Configurações, todos com ícones.
4. Sem projeto aberto, **Buscar Espécie**, **Importar Família**, **Configurações** e **Sobre** ficam disponíveis; os demais, desativados.

## 2. Biblioteca

1. Buscar Espécie: pesquise "ipe amarelo" (sem acento) e filtre Árvore + Nativa + altura 5 a 8 m.
2. Filtre por bioma (Cerrado), por mês de floração (setembro) e "somente com floração vistosa".
3. Confira o símbolo colorido nos quatro perfis da ficha, a fotografia e o crédito (autor e licença) abaixo dela.
4. Cadastre uma espécie nova com fotografia, biomas, cor da folhagem e cor da flor; edite, duplique e exclua. Ao trocar a foto, o crédito da foto do catálogo deve sumir.

## 3. Plantio

1. Em uma planta 1:100, Buscar Espécie > Ipê-amarelo > **Inserir**: o símbolo colorido deve acompanhar o cursor; clique três vezes e pressione ESC.
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
3. **Legenda**: confira símbolos, hachuras e quantidades.
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
