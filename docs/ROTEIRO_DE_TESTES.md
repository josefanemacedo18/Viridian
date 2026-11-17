# Roteiro de testes no Revit 2027

O código foi compilado contra a API oficial do Revit 2027 e a lógica sem dependência do Revit é coberta por testes automatizados. Este roteiro confere, dentro do Revit, o que só pode ser verificado com o programa aberto. Use um projeto de teste (ou uma cópia de um projeto real).

Se algo falhar, anote a etapa e envie o arquivo mais recente de `%APPDATA%\Viridian\Logs` (Viridian > Sobre abre a pasta).

## 1. Carregamento

1. Instale com `Instalar.cmd` e abra o Revit 2027.
2. Escolha **Sempre carregar** no aviso de segurança.
3. Confira a aba **Viridian** com os painéis Biblioteca, Plantio, Maciços, Identificação, Quantitativos, Verificação e Configurações, todos com ícones.
4. Sem projeto aberto, **Buscar Espécie**, **Importar Família**, **Configurações** e **Sobre** ficam disponíveis; os demais, desativados.

## 2. Biblioteca

1. Buscar Espécie: pesquise "ipe amarelo" (sem acento) e filtre Árvore + Nativa + altura 5 a 8 m.
2. Confira o símbolo nos quatro perfis da ficha.
3. Cadastre uma espécie nova com fotografia; edite, duplique e exclua.

## 3. Plantio

1. Em uma planta 1:100, Buscar Espécie > Ipê-amarelo > **Inserir**: clique três vezes e pressione ESC.
   - Na primeira vez, o Viridian gera a família 2D (alguns segundos).
   - Os exemplares devem receber códigos AR.0001, AR.0002, AR.0003.
2. **Distribuir** um arbusto em uma região preenchida, nos padrões triangular, retangular e natural. Confira afastamento das bordas e a contagem da pré-visualização contra a quantidade inserida.
3. Distribuir com **Respeitar exemplares existentes** marcado sobre uma área que já tem plantas.
4. **Plantio Linear** com um eixo de três pontos.
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

## 8. Biblioteca do escritório

1. Configurações > Biblioteca: aponte para uma pasta de rede.
2. Importe um `.pat` em Configurações > Hachuras e use-o em um maciço.
3. Importar Família: registre uma família de Vegetação própria para uma espécie e aceite atualizar os exemplares do projeto.
4. Feche e reabra o projeto: maciços, etiquetas e códigos devem continuar vinculados.
