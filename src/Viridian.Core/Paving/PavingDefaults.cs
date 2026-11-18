namespace Viridian.Core.Paving;

/// <summary>
/// Biblioteca inicial de pisos para paisagismo. As medidas são referências usuais de fabricantes, especificações
/// públicas e normas citadas em cada tipo; devem ser conferidas com o fornecedor e com o responsável técnico.
/// </summary>
public static class PavingDefaults
{
    /// <summary>Aumente ao mudar os tipos padrão: tipos não editados pelo usuário são atualizados.</summary>
    public const int Version = 1;

    private const string RefIntertravado = "ABNT NBR 9781 (peças de concreto); DNIT 066/2004-ES (camada de assentamento de 4 cm; sub-base mín. 15 cm); ABCP (base granular mín. 10 cm).";
    private const string RefDrenante = "ABNT NBR 16416 (pavimentos permeáveis de concreto): base e assentamento sem finos, manta de separação quando necessária; dimensionamento hidráulico conforme o anexo da norma.";
    private const string RefFulget = "Fichas de fabricante (granito lavado moldado com 8 a 10 mm; juntas plásticas em quadros de 1,0 × 1,0 m ou 1,5 × 1,5 m); SINAPI (placas 40×40 e 75×75, e = 2 cm).";
    private const string RefContrapiso = "Contrapiso armado ou com juntas de movimentação (espaçamento máximo de referência de 4 m).";
    private const string RefDeck = "Manuais de fabricantes de deck: barrotes entre 30 e 40 cm de eixo; confirme vão, junta de dilatação e fixação com o fornecedor.";
    private const string RefBorracha = "ABNT NBR 16071-3 (playgrounds): a espessura depende da altura de queda livre, comprovada por laudo do fabricante (ex.: 25 mm ≈ 1,0 m; 40 mm ≈ 1,6 m, conforme fabricante).";
    private const string RefPedra = "Fornecedores de pedras naturais (formatos serrados, filetes, mosaicos e irregulares); exija a ficha de absorção, abrasão e antiderrapância do lote.";

    public static List<PavingType> Create()
    {
        var list = new List<PavingType>();

        PavingLayer L(string name, PavingLayerKind kind, double mm, double spacing = 0) => new() { Name = name, Kind = kind, ThicknessMm = mm, SpacingMm = spacing };
        List<PavingLayer> RigidOnSlab() => new()
        {
            L("Argamassa de assentamento", PavingLayerKind.Argamassa, 30),
            L("Contrapiso de concreto", PavingLayerKind.Contrapiso, 60),
            L("Lastro de brita", PavingLayerKind.Base, 50),
        };
        List<PavingLayer> Pavers(bool vehicles) => vehicles
            ? new() { L("Areia de assentamento", PavingLayerKind.Assentamento, 40), L("Base de brita graduada", PavingLayerKind.Base, 100), L("Sub-base granular", PavingLayerKind.SubBase, 150) }
            : new() { L("Areia de assentamento", PavingLayerKind.Assentamento, 40), L("Base de brita graduada", PavingLayerKind.Base, 100) };
        List<PavingLayer> Permeable() => new()
        {
            L("Pedrisco de assentamento (sem finos)", PavingLayerKind.Assentamento, 40),
            L("Base de brita nº 1 (sem finos)", PavingLayerKind.Base, 150),
            L("Manta geotêxtil de separação", PavingLayerKind.Manta, 0),
        };

        void Add(string id, string name, PavingCategory category, string material, LayingPattern pattern, double length, double width, double thickness,
            double joint, string color, string jointColor, List<PavingLayer> layers, string notes, string references, double waste = 10, bool cast = false, double slope = 1.0, double angle = 0)
        {
            list.Add(new PavingType
            {
                Id = "vir-" + id,
                Name = name,
                Category = category,
                Material = material,
                Pattern = pattern,
                LengthMm = length,
                WidthMm = width,
                ThicknessMm = thickness,
                JointMm = joint,
                AngleDeg = angle,
                Color = color,
                JointColor = jointColor,
                Layers = layers,
                Notes = notes,
                References = references,
                WastePercent = waste,
                CastInPlace = cast,
                SlopePercent = slope,
            });
        }

        // Cimentícios e fulget.
        Add("cimenticio-50", "Placa cimentícia 50×50", PavingCategory.Cimenticio, "Placa de concreto prensado, acabamento natural", LayingPattern.Grade, 500, 500, 30, 5,
            "#C9C5BC", "#8E8A82", RigidOnSlab(), "Calçadas, caminhos e áreas de estar. Rejunte cimentício.", RefContrapiso);
        Add("cimenticio-50-amarrado", "Placa cimentícia 50×25 amarrada", PavingCategory.Cimenticio, "Placa de concreto prensado", LayingPattern.AmarracaoMeia, 500, 250, 30, 5,
            "#BFBBB2", "#85817A", RigidOnSlab(), "Caminhos e terraços com fiadas defasadas.", RefContrapiso);
        Add("atermico-50", "Placa atérmica 50×50", PavingCategory.Cimenticio, "Placa cimentícia atérmica, borda boleada", LayingPattern.Grade, 500, 500, 25, 5,
            "#E4DCCB", "#A99F8C", RigidOnSlab(), "Bordas e decks de piscina; superfície que aquece pouco.", RefContrapiso);
        Add("fulget-moldado", "Fulget moldado (granito lavado)", PavingCategory.Fulget, "Cimento, cal e granilha de pedra lavados no local", LayingPattern.Grade, 1000, 1000, 10, 3,
            "#B9B2A6", "#77716A", new() { L("Contrapiso de regularização", PavingLayerKind.Contrapiso, 50), L("Lastro de concreto magro", PavingLayerKind.Base, 50) },
            "Bordas de piscina, calçadas, rampas e varandas. Juntas plásticas em quadros sobre o contrapiso antes da massa.", RefFulget + " " + RefContrapiso, 5, cast: true);
        Add("fulget-placa-40", "Fulget em placas 40×40", PavingCategory.Fulget, "Placa pré-moldada de granito lavado", LayingPattern.Grade, 400, 400, 20, 5,
            "#BDB6AA", "#7A746C", RigidOnSlab(), "Mesma aparência do moldado, com assentamento em placas.", RefFulget);
        Add("fulget-placa-75", "Fulget em placas 75×75", PavingCategory.Fulget, "Placa pré-moldada de granito lavado", LayingPattern.Grade, 750, 750, 20, 5,
            "#B5AEA2", "#756F67", RigidOnSlab(), "Grandes áreas com menos juntas.", RefFulget);

        // Intertravados.
        Add("paver-10x20-espinha", "Intertravado 10×20×6 espinha de peixe", PavingCategory.Intertravado, "Peça de concreto retangular", LayingPattern.EspinhaDePeixe45, 200, 100, 60, 3,
            "#A9A39A", "#6D6861", Pavers(false), "Calçadas e áreas de pedestres. A espinha de peixe a 45° distribui melhor os esforços.", RefIntertravado, 7);
        Add("paver-10x20-amarrado", "Intertravado 10×20×6 amarrado", PavingCategory.Intertravado, "Peça de concreto retangular", LayingPattern.AmarracaoMeia, 200, 100, 60, 3,
            "#B3ADA3", "#726D66", Pavers(false), "Caminhos de pedestres; fiadas perpendiculares ao sentido do tráfego.", RefIntertravado, 7);
        Add("paver-10x20-trama", "Intertravado 10×20×6 trama", PavingCategory.Intertravado, "Peça de concreto retangular", LayingPattern.Trama, 200, 100, 60, 3,
            "#9F978B", "#625D56", Pavers(false), "Pátios e praças; uso só de pedestres.", RefIntertravado, 7);
        Add("paver-10x20x8-veicular", "Intertravado 10×20×8 veicular", PavingCategory.Intertravado, "Peça de concreto retangular para tráfego leve", LayingPattern.EspinhaDePeixe45, 200, 100, 80, 3,
            "#8F8981", "#57534D", Pavers(true), "Acessos e estacionamentos de veículos leves. Confirme a resistência da peça e a base com o projeto de pavimentação.", RefIntertravado, 7);
        Add("paver-hexagonal", "Intertravado hexagonal 25 cm", PavingCategory.Intertravado, "Peça de concreto sextavada", LayingPattern.Hexagonal, 250, 250, 80, 3,
            "#A59F95", "#69645D", Pavers(false), "Praças e caminhos; leitura geométrica marcante.", RefIntertravado, 8);

        // Drenantes.
        Add("drenante-bloco", "Bloco drenante 10×20×8", PavingCategory.Drenante, "Bloco de concreto permeável", LayingPattern.EspinhaDePeixe, 200, 100, 80, 3,
            "#9C9A93", "#62605A", Permeable(), "Estacionamentos e calçadas permeáveis. Não use areia com finos nem rejunte cimentício.", RefDrenante, 7);
        Add("drenante-placa-40", "Placa drenante 40×40", PavingCategory.Drenante, "Placa de concreto permeável", LayingPattern.Grade, 400, 400, 60, 3,
            "#A8A69E", "#6A6862", Permeable(), "Calçadas e praças permeáveis.", RefDrenante, 7);
        Add("drenante-moldado", "Concreto permeável moldado", PavingCategory.Drenante, "Concreto poroso moldado no local", LayingPattern.Grade, 3000, 3000, 100, 5,
            "#A3A19A", "#5E5C57", new() { L("Base de brita nº 2 (sem finos)", PavingLayerKind.Base, 150), L("Manta geotêxtil de separação", PavingLayerKind.Manta, 0) },
            "Pátios e ciclovias permeáveis; juntas serradas em quadros.", RefDrenante, 5, cast: true);

        // Pedras naturais.
        Add("sao-tome-serrada", "Pedra São Tomé serrada 46×46", PavingCategory.PedraNatural, "Quartzito São Tomé serrado, acabamento natural", LayingPattern.Grade, 460, 460, 20, 5,
            "#E8DCC0", "#A8977A", RigidOnSlab(), "Bordas de piscina e áreas externas; clara e antiderrapante.", RefPedra);
        Add("sao-tome-cacos", "Pedra São Tomé irregular (cacos)", PavingCategory.PedraNatural, "Quartzito São Tomé em lascas irregulares", LayingPattern.Cacos, 400, 400, 30, 15,
            "#E2D3B4", "#9E8C6E", RigidOnSlab(), "Caminhos e terraços com visual rústico; rejunte largo.", RefPedra, 15);
        Add("miracema-filete", "Pedra Miracema 11,5×23", PavingCategory.PedraNatural, "Pedra Miracema serrada", LayingPattern.AmarracaoMeia, 230, 115, 20, 5,
            "#8C877C", "#55524C", RigidOnSlab(), "Calçadas, rampas e muros; rústica e resistente.", RefPedra);
        Add("ardosia-40", "Ardósia 40×40", PavingCategory.PedraNatural, "Ardósia cinza, acabamento natural", LayingPattern.Grade, 400, 400, 10, 3,
            "#6E7472", "#454A48", RigidOnSlab(), "Áreas molhadas e varandas. Aplique selador após o rejunte.", RefPedra);
        Add("basalto-40", "Basalto serrado 40×40", PavingCategory.PedraNatural, "Basalto serrado, acabamento levigado ou flameado", LayingPattern.AmarracaoMeia, 400, 400, 20, 5,
            "#4F4E4B", "#2E2D2B", RigidOnSlab(), "Garagens e áreas de tráfego; alta resistência a impacto.", RefPedra);
        Add("granito-flameado-60", "Granito flameado 60×60", PavingCategory.PedraNatural, "Granito cinza flameado", LayingPattern.Grade, 600, 600, 20, 3,
            "#9A9893", "#5C5A56", RigidOnSlab(), "Praças e acessos; superfície antiderrapante pelo flameamento.", RefPedra);
        Add("pedra-goias-opus", "Pedra Goiás opus", PavingCategory.PedraNatural, "Quartzito Goiás em quatro tamanhos", LayingPattern.Opus, 200, 200, 20, 5,
            "#D9CDB2", "#988B70", RigidOnSlab(), "Terraços e caminhos com paginação variada (módulo de 20 cm).", RefPedra, 12);

        // Pedra portuguesa.
        Add("pedra-portuguesa", "Pedra portuguesa (mosaico)", PavingCategory.PedraPortuguesa, "Calcário ou basalto em cubos de 4 a 6 cm", LayingPattern.Mosaico, 50, 50, 50, 5,
            "#ECE8DF", "#8F8A80", new() { L("Assentamento em areia e cimento a seco", PavingLayerKind.Assentamento, 50), L("Base de brita compactada", PavingLayerKind.Base, 80) },
            "Calçadas e desenhos em mosaico; compactação com maço após o assentamento.", RefPedra, 10);

        // Porcelanato e cerâmicos.
        Add("porcelanato-60", "Porcelanato externo 60×60", PavingCategory.Porcelanato, "Porcelanato antiderrapante para área externa", LayingPattern.Grade, 600, 600, 10, 3,
            "#B8B1A5", "#7B756C", new() { L("Argamassa colante AC III", PavingLayerKind.Argamassa, 5), L("Contrapiso de concreto", PavingLayerKind.Contrapiso, 50), L("Lastro de brita", PavingLayerKind.Base, 50) },
            "Varandas, terraços e bordas de piscina. Use peças com especificação antiderrapante para área externa e argamassa adequada.", "Fichas técnicas de fabricantes de porcelanato.", 10);
        Add("porcelanato-60x120", "Porcelanato externo 60×120 amarrado", PavingCategory.Porcelanato, "Porcelanato antiderrapante de grande formato", LayingPattern.AmarracaoTerco, 1200, 600, 10, 3,
            "#A9A398", "#6E6962", new() { L("Argamassa colante AC III (dupla colagem)", PavingLayerKind.Argamassa, 6), L("Contrapiso de concreto", PavingLayerKind.Contrapiso, 50), L("Lastro de brita", PavingLayerKind.Base, 50) },
            "Áreas de estar externas; formatos grandes exigem contrapiso bem nivelado.", "Fichas técnicas de fabricantes de porcelanato.", 12);
        Add("porcelanato-20mm-grama", "Porcelanato 20 mm sobre grama/brita", PavingCategory.Porcelanato, "Porcelanato de 2 cm assentado a seco", LayingPattern.Grade, 600, 600, 20, 30,
            "#BEB6A8", "#6F8F55", new() { L("Pedrisco de assentamento", PavingLayerKind.Assentamento, 30), L("Base de brita compactada", PavingLayerKind.Base, 80), L("Manta geotêxtil", PavingLayerKind.Manta, 0) },
            "Pisadas e caminhos sobre gramado ou brita, sem argamassa.", "Fichas técnicas de fabricantes de porcelanato de 2 cm.", 8);
        Add("tijolo-espinha", "Tijolo maciço espinha de peixe", PavingCategory.Ceramico, "Tijolo cerâmico maciço 10×20", LayingPattern.EspinhaDePeixe45, 200, 100, 50, 10,
            "#A9573B", "#6E3A28", new() { L("Areia de assentamento", PavingLayerKind.Assentamento, 40), L("Base de brita compactada", PavingLayerKind.Base, 80) },
            "Caminhos e pátios com acabamento rústico.", "Fornecedores de tijolos cerâmicos para piso.", 10);

        // Concreto.
        Add("concreto-desempenado", "Concreto desempenado", PavingCategory.Concreto, "Concreto moldado, acabamento desempenado ou vassourado", LayingPattern.Grade, 2000, 2000, 80, 5,
            "#B4B2AC", "#6F6D68", new() { L("Lastro de brita", PavingLayerKind.Base, 50) },
            "Calçadas e acessos; juntas serradas ou de dilatação em quadros de cerca de 2 m.", RefContrapiso, 5, cast: true);
        Add("concreto-veicular", "Concreto armado veicular", PavingCategory.Concreto, "Concreto armado com tela, acabamento vassourado", LayingPattern.Grade, 3000, 3000, 120, 5,
            "#A7A59F", "#63615D", new() { L("Lastro de brita", PavingLayerKind.Base, 80), L("Sub-base granular", PavingLayerKind.SubBase, 150) },
            "Acessos de veículos; armadura e espessura conforme o projeto de pavimentação.", RefContrapiso, 5, cast: true);

        // Decks.
        Add("deck-cumaru", "Deck de madeira (cumaru) 10 cm", PavingCategory.DeckMadeira, "Réguas de madeira de lei (cumaru, ipê ou itaúba) com tratamento", LayingPattern.Reguas, 1500, 100, 21, 5,
            "#9A6A43", "#5A3B23", new() { L("Barrotes 5×5 cm", PavingLayerKind.Estrutura, 50, 400), L("Contrapiso ou sapatas de apoio", PavingLayerKind.Contrapiso, 60) },
            "Áreas de estar e bordas de piscina; manter ventilação sob o deck.", RefDeck, 12, slope: 0.5);
        Add("deck-wpc", "Deck WPC 14,8 cm", PavingCategory.DeckWpc, "Réguas de compósito madeira-plástico", LayingPattern.Reguas, 2200, 148, 29, 5,
            "#7E6250", "#4D3B2F", new() { L("Barrotes de WPC ou alumínio", PavingLayerKind.Estrutura, 40, 350), L("Contrapiso ou sapatas de apoio", PavingLayerKind.Contrapiso, 60) },
            "Áreas de estar e bordas de piscina sem manutenção de verniz.", RefDeck, 8, slope: 0.5);

        // Pedriscos e seixos.
        Add("pedrisco", "Pedrisco", PavingCategory.Pedrisco, "Pedrisco ou brita nº 0 solta", LayingPattern.Granular, 12, 12, 50, 0,
            "#B9B3A7", "#6B665E", new() { L("Manta geotêxtil", PavingLayerKind.Manta, 0) },
            "Caminhos secundários e canteiros secos; camada mantida com reposição periódica.", "Fornecedores e guias de paisagismo (camada de cerca de 5 cm sobre manta).", 15, slope: 0);
        Add("seixo-rolado", "Seixo rolado médio", PavingCategory.Pedrisco, "Seixo rolado de rio, 2 a 3 cm", LayingPattern.Granular, 25, 25, 50, 0,
            "#CFC6B6", "#7D7465", new() { L("Manta geotêxtil", PavingLayerKind.Manta, 0) },
            "Faixas drenantes e áreas de contemplação; seixos menores que 2 cm se soltam em áreas de passagem.", "Fornecedores e guias de paisagismo.", 15, slope: 0);

        // Emborrachados e grama-concreto.
        Add("emborrachado-40", "Piso emborrachado 50×50×40", PavingCategory.Emborrachado, "Placa de borracha reciclada com EPDM", LayingPattern.Grade, 500, 500, 40, 0,
            "#3F7A5A", "#25493A", new() { L("Contrapiso de concreto", PavingLayerKind.Contrapiso, 60), L("Lastro de brita", PavingLayerKind.Base, 50) },
            "Playgrounds e academias ao ar livre. Escolha a espessura pela altura de queda livre do brinquedo.", RefBorracha, 5);
        Add("emborrachado-25", "Piso emborrachado 50×50×25", PavingCategory.Emborrachado, "Placa de borracha reciclada", LayingPattern.Grade, 500, 500, 25, 0,
            "#B5523B", "#6B3023", new() { L("Contrapiso de concreto", PavingLayerKind.Contrapiso, 60), L("Lastro de brita", PavingLayerKind.Base, 50) },
            "Áreas de recreação com quedas baixas.", RefBorracha, 5);
        Add("concregrama", "Grama-concreto 45×45", PavingCategory.GramaConcreto, "Placa de concreto vazada preenchida com terra e grama", LayingPattern.Vazado, 450, 450, 80, 10,
            "#9C9890", "#5F8F4A", new() { L("Areia de assentamento", PavingLayerKind.Assentamento, 30), L("Base de brita compactada", PavingLayerKind.Base, 100) },
            "Estacionamentos e acessos eventuais com cobertura verde. Dimensões variam por fabricante.", "Fichas de fabricantes de placas vazadas.", 5);

        return list;
    }
}
