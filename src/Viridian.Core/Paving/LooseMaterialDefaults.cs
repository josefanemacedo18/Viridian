namespace Viridian.Core.Paving;

/// <summary>
/// Biblioteca inicial de pedras e coberturas soltas usadas no paisagismo brasileiro. Granulometrias pela designação
/// usual do comércio (brita 0 ou pedrisco: 4,8 a 9,5 mm; brita 1: 9,5 a 19 mm; brita 2: 19 a 25 mm; brita 3: 25 a
/// 50 mm). Densidade aparente solta, vazios e perdas são valores típicos de referência: confirme com o fornecedor.
/// </summary>
public static class LooseMaterialDefaults
{
    /// <summary>Aumente ao mudar os materiais padrão: os não editados pelo usuário são atualizados.</summary>
    public const int Version = 1;

    private const string Ref = "Valores de referência (granulometria pela designação comercial usual; densidade aparente solta, vazios e perdas típicos). Confirme com o fornecedor do lote.";

    public static List<LooseMaterial> Create()
    {
        var list = new List<LooseMaterial>();

        void Add(string id, string name, LooseKind kind, LooseOrigin origin, string rock, double min, double max, string color, string grain, LooseUse uses,
            double layer, double density, double voids, double waste, string notes, double bagKg = 20, double bagLiters = 0)
        {
            list.Add(new LooseMaterial
            {
                Id = "vir-" + id,
                Name = name,
                Kind = kind,
                Origin = origin,
                Rock = rock,
                GrainMinMm = min,
                GrainMaxMm = max,
                Color = color,
                GrainColor = grain,
                Uses = uses,
                LayerMm = layer,
                DensityTM3 = density,
                PorosityPercent = voids,
                WastePercent = waste,
                BagKg = bagKg,
                BagLiters = bagLiters,
                Notes = notes,
                References = Ref,
            });
        }

        const LooseUse Decor = LooseUse.Canteiro | LooseUse.Borda;

        // Seixos.
        Add("seixo-branco", "Seixo branco", LooseKind.Seixo, LooseOrigin.Rolado, "quartzo ou mármore", 20, 30, "#EEECE6", "#A9A498", Decor,
            50, 1.55, 38, 10, "Canteiros secos, contornos de vasos e bordas. Reflete luz e calor: evite junto a plantas de sombra em sol forte.");
        Add("seixo-rio", "Seixo natural de rio", LooseKind.Seixo, LooseOrigin.NaturalDeRio, "rochas diversas", 20, 40, "#C9BBA3", "#7C6D58", Decor | LooseUse.Drenagem,
            50, 1.55, 38, 10, "Visual natural, com tons de bege, cinza e marrom. Bom para leitos secos e faixas drenantes.");
        Add("seixo-preto", "Seixo preto", LooseKind.Seixo, LooseOrigin.Rolado, "basalto ou rocha escura polida", 20, 40, "#3B3B3A", "#8E8C87", Decor,
            50, 1.6, 38, 10, "Contraste forte com folhagens claras e pisos claros. Aquece ao sol.");
        Add("seixo-bege", "Seixo bege", LooseKind.Seixo, LooseOrigin.Rolado, "rocha calcária ou quartzo", 20, 30, "#DCCDAE", "#9C8B6B", Decor,
            50, 1.55, 38, 10, "Tom quente e neutro; combina com pisos de pedra São Tomé e madeira.");
        Add("seixo-pequeno", "Seixo rolado pequeno", LooseKind.Seixo, LooseOrigin.NaturalDeRio, "rochas diversas", 10, 20, "#CFC3AE", "#83765F", Decor | LooseUse.PisoSolto | LooseUse.Drenagem,
            50, 1.55, 36, 10, "Grão miúdo: aceita passagem eventual de pedestres.");
        Add("seixo-grande", "Seixo rolado grande", LooseKind.Seixo, LooseOrigin.NaturalDeRio, "rochas diversas", 50, 80, "#BFB4A2", "#6E6454", Decor | LooseUse.Drenagem,
            80, 1.5, 40, 10, "Leitos secos, bocas de descida de água e bordas de contenção; não use em áreas de passagem.");

        // Pedras trituradas.
        Add("dolomita-branca", "Dolomita branca", LooseKind.PedraTriturada, LooseOrigin.Triturado, "dolomita", 6, 12, "#F2F0EA", "#B3AEA3", Decor | LooseUse.PisoSolto,
            40, 1.5, 40, 10, "Branco intenso; pode elevar o pH do solo junto a plantas acidófilas (azaleia, camélia).");
        Add("marmore-triturado", "Mármore triturado", LooseKind.PedraTriturada, LooseOrigin.Triturado, "mármore branco", 9.5, 19, "#F0EEE8", "#ABA69B", Decor,
            50, 1.5, 40, 10, "Canteiros decorativos e jardins secos; mesmo cuidado com o pH da dolomita.");
        Add("quartzo-triturado", "Quartzo triturado", LooseKind.PedraTriturada, LooseOrigin.Triturado, "quartzo", 9.5, 19, "#F4F1EA", "#B9B1A2", Decor,
            50, 1.5, 40, 10, "Brilho cristalino; quimicamente estável (não altera o solo).");

        // Pedriscos (brita 0).
        Add("pedrisco-branco", "Pedrisco branco", LooseKind.Pedrisco, LooseOrigin.Britado, "mármore ou dolomita", 4.8, 9.5, "#EDEBE4", "#A8A398", Decor | LooseUse.PisoSolto,
            40, 1.45, 40, 10, "Caminhos de jardim e canteiros; grão fino, confortável para caminhar.");
        Add("pedrisco-granito", "Pedrisco de granito", LooseKind.Pedrisco, LooseOrigin.Britado, "granito", 4.8, 9.5, "#B8B3AA", "#6D6860", Decor | LooseUse.PisoSolto | LooseUse.Drenagem,
            40, 1.45, 40, 10, "Cinza neutro e resistente; caminhos e assentamento de pisos drenantes.");
        Add("pedrisco-natural", "Pedrisco natural", LooseKind.Pedrisco, LooseOrigin.NaturalDeRio, "rochas diversas", 4.8, 9.5, "#CDBFA6", "#82745D", Decor | LooseUse.PisoSolto,
            40, 1.45, 38, 10, "Tom terroso; caminhos rústicos.");
        Add("pedrisco-basalto", "Pedrisco de basalto", LooseKind.Pedrisco, LooseOrigin.Britado, "basalto", 4.8, 9.5, "#4A4A48", "#9A9893", Decor | LooseUse.PisoSolto,
            40, 1.55, 40, 10, "Escuro e resistente; contraste com folhagens claras.");
        Add("pedrisco-vermelho", "Pedrisco vermelho", LooseKind.Pedrisco, LooseOrigin.Britado, "granito vermelho ou pigmentado", 4.8, 9.5, "#A65E46", "#5E3427", Decor,
            40, 1.45, 40, 10, "Cor quente; confirme com o fornecedor se a cor é natural ou pigmentada (pigmento desbota ao sol).");

        // Britas.
        Add("brita-branca", "Brita branca", LooseKind.Brita, LooseOrigin.Britado, "mármore ou dolomita", 9.5, 19, "#ECEAE3", "#A29D92", Decor | LooseUse.PisoSolto,
            50, 1.45, 40, 10, "Canteiros e áreas de estar com piso solto; grão anguloso trava melhor que o seixo.");
        Add("brita-cinza", "Brita cinza (brita 1)", LooseKind.Brita, LooseOrigin.Britado, "granito ou gnaisse", 9.5, 19, "#A9A7A1", "#5F5D58", Decor | LooseUse.PisoSolto | LooseUse.Drenagem,
            50, 1.45, 42, 10, "Uso geral: drenagem, lastro e piso solto.");
        Add("brita-colorida", "Brita colorida", LooseKind.Brita, LooseOrigin.Britado, "pedra pigmentada", 9.5, 19, "#6F9C6A", "#3E5C3B", Decor,
            50, 1.45, 40, 10, "Cores variadas (verde, azul, vermelho, amarelo): ajuste a cor nesta ficha. Pigmentos desbotam ao sol; confirme a garantia.");
        Add("brita-2", "Brita 2", LooseKind.Brita, LooseOrigin.Britado, "granito ou gnaisse", 19, 25, "#A3A19B", "#595752", LooseUse.Drenagem | LooseUse.Borda,
            150, 1.4, 42, 10, "Camadas drenantes, valas de infiltração e reservatório de jardins de chuva.", bagKg: 0);
        Add("brita-3", "Brita 3", LooseKind.Brita, LooseOrigin.Britado, "granito ou gnaisse", 25, 50, "#9E9C96", "#55534E", LooseUse.Drenagem,
            200, 1.4, 45, 10, "Drenos e poços de infiltração de maior vazão.", bagKg: 0);
        Add("pedra-de-mao", "Pedra de mão (rachão)", LooseKind.PedraDeMao, LooseOrigin.Britado, "granito ou gnaisse", 76, 250, "#9A978F", "#4F4C47", LooseUse.Drenagem | LooseUse.Borda,
            250, 1.45, 45, 10, "Enrocamento, dissipação na saída de água, bordas rústicas e gabiões.", bagKg: 0);

        // Cascalhos.
        Add("cascalho-fino", "Cascalho fino", LooseKind.Cascalho, LooseOrigin.NaturalDeRio, "rochas diversas", 5, 15, "#C7B79C", "#7A6A52", Decor | LooseUse.PisoSolto | LooseUse.Drenagem,
            50, 1.6, 35, 12, "Caminhos e estacionamentos de jardim; mistura grãos rolados e angulosos.", bagKg: 0);
        Add("cascalho-grosso", "Cascalho grosso", LooseKind.Cascalho, LooseOrigin.NaturalDeRio, "rochas diversas", 15, 40, "#BCAD93", "#71634C", LooseUse.Borda | LooseUse.Drenagem | LooseUse.PisoSolto,
            80, 1.6, 36, 12, "Bases, drenagem e acessos rústicos de veículos leves.", bagKg: 0);

        // Finos e compactados.
        Add("po-de-pedra", "Pó de pedra", LooseKind.PoDePedra, LooseOrigin.Britado, "granito", 0, 4.8, "#BDB8AE", "#7F7A71", LooseUse.PisoCompactado | LooseUse.Borda,
            50, 1.55, 20, 25, "Caminhos compactados e assentamento de pedras; compactar úmido. A perda inclui a compactação.", bagKg: 0);
        Add("saibro", "Saibro", LooseKind.Saibro, LooseOrigin.NaturalDeRio, "solo arenoso de alteração de rocha", 0, 4.8, "#C98E5E", "#7E5333", LooseUse.PisoCompactado,
            50, 1.5, 25, 30, "Caminhos e áreas de lazer de piso de terra batida; exige caimento e manutenção após chuvas fortes.", bagKg: 0);
        Add("bica-corrida", "Bica corrida", LooseKind.Brita, LooseOrigin.Britado, "granito", 0, 25, "#B1ACA2", "#67635C", LooseUse.PisoCompactado | LooseUse.Drenagem,
            100, 1.65, 22, 25, "Base compactada de caminhos e pisos; sem finos lavados não serve para camada drenante fina.", bagKg: 0);
        Add("areia-grossa", "Areia grossa lavada", LooseKind.Areia, LooseOrigin.NaturalDeRio, "quartzo", 1.2, 4.8, "#E0CFA6", "#9B8A61", LooseUse.Drenagem,
            100, 1.5, 35, 15, "Substrato filtrante de jardins de chuva (com composto) e assentamento de pisos intertravados.", bagKg: 20);

        // Industrializados.
        Add("argila-expandida", "Argila expandida", LooseKind.ArgilaExpandida, LooseOrigin.Industrializado, "argila queimada", 10, 22, "#A9573B", "#5F2F1F", LooseUse.Drenagem | LooseUse.Canteiro,
            80, 0.5, 40, 5, "Drenagem leve de jardins sobre laje e floreiras; não sobrecarrega a estrutura.", bagKg: 0, bagLiters: 50);

        // Orgânicas.
        Add("casca-pinus-fina", "Casca de pinus fina", LooseKind.CoberturaOrganica, LooseOrigin.Organico, "pinus", 10, 25, "#8A5A3A", "#4E3120", LooseUse.CoberturaMorta | LooseUse.Canteiro,
            50, 0.25, 60, 15, "Cobertura de vasos e canteiros pequenos; decompõe e deve ser reposta a cada 1 a 2 anos.", bagKg: 0, bagLiters: 50);
        Add("casca-pinus-media", "Casca de pinus média", LooseKind.CoberturaOrganica, LooseOrigin.Organico, "pinus", 25, 50, "#83522F", "#47291A", LooseUse.CoberturaMorta | LooseUse.Canteiro | LooseUse.Borda,
            70, 0.25, 60, 15, "Canteiros e maciços; retém umidade e reduz ervas daninhas.", bagKg: 0, bagLiters: 50);
        Add("casca-pinus-grossa", "Casca de pinus grossa", LooseKind.CoberturaOrganica, LooseOrigin.Organico, "pinus", 50, 90, "#7B4B2A", "#422616", LooseUse.CoberturaMorta | LooseUse.Canteiro,
            80, 0.25, 65, 15, "Grandes canteiros; dura mais e o vento leva menos.", bagKg: 0, bagLiters: 50);
        Add("cavaco-madeira", "Cavaco de madeira", LooseKind.CoberturaOrganica, LooseOrigin.Organico, "madeira de reflorestamento", 20, 60, "#A7804F", "#5F4527", LooseUse.CoberturaMorta | LooseUse.PisoSolto,
            80, 0.3, 60, 20, "Cobertura morta e piso macio de playgrounds e trilhas; repor periodicamente.", bagKg: 0, bagLiters: 50);

        return list;
    }
}
