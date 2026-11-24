namespace Viridian.Core.Model3D.Profiles;

/// <summary>
/// Perfis botânicos 3D padrão: as arquiteturas-base e perfis de referência (ipê, flamboyant, jerivá, agave,
/// estrelítzia) usados para calibrar os geradores. Proporções aproximadas da arquitetura típica de cada grupo.
/// </summary>
public static class Plant3DProfileDefaults
{
    /// <summary>Aumente ao mudar os perfis padrão: os não editados pelo usuário são atualizados.</summary>
    public const int Version = 1;

    // Orçamentos da especificação (item 4.4), no limite superior de cada faixa.
    private static GeometryBudget TreeBudget() => new() { Simplificada = 350, Intermediaria = 1500, Detalhada = 4000 };

    private static GeometryBudget PalmBudget() => new() { Simplificada = 500, Intermediaria = 1800, Detalhada = 5000 };

    private static GeometryBudget ShrubBudget() => new() { Simplificada = 180, Intermediaria = 600, Detalhada = 1500 };

    private static GeometryBudget ClumpBudget() => new() { Simplificada = 150, Intermediaria = 500, Detalhada = 1200 };

    private static GeometryBudget RosetteBudget() => new() { Simplificada = 250, Intermediaria = 900, Detalhada = 2000 };

    public static List<Plant3DProfile> Create()
    {
        var list = new List<Plant3DProfile>();

        Plant3DProfile Tree(string id, string name, PlantArchitecture a, CrownEnvelope env, double baseRatio, double irregular, double density, double cluster,
            int pmin, int pmax, double angle, double droop, double trunkRatio, string description, double aspect = 0.95)
        {
            var p = new Plant3DProfile
            {
                Id = id,
                Name = name,
                Architecture = a,
                Description = description,
                Crown = new CrownProfile { Envelope = env, BaseRatio = baseRatio, Irregularity = irregular, Density = density, ClusterScale = cluster, WidthAspect = aspect },
                Trunk = new TrunkProfile { RadiusRatio = trunkRatio, Taper = 0.5, Flare = 0.3, LeanDeg = 3 },
                Branches = new BranchProfile { PrimaryMin = pmin, PrimaryMax = pmax, AngleDeg = angle, Droop = droop, SecondaryPerPrimary = 2 },
                Foliage = new FoliageProfile { Shape = LeafShape.Eliptica, LeafLengthM = 0.12, WidthRatio = 0.45 },
                Flowers = new FlowerProfile { Type = InflorescenceType.Panicula, SizeM = 0.25, PerCluster = 7, Abundance = 0.45 },
                Fruits = new FruitProfile { Type = FruitType.Drupa, SizeM = 0.04, PerCluster = 3, Abundance = 0.3 },
                Budget = TreeBudget(),
            };
            list.Add(p);
            return p;
        }

        // --- Árvores (base) ---------------------------------------------------------------------------------------
        Tree("arvore-arredondada", "Árvore de copa arredondada", PlantArchitecture.ArvoreArredondada, CrownEnvelope.Elipsoide, 0.33, 0.3, 0.7, 0.38, 4, 6, 42, 0.12, 0.018,
            "Copa globosa formada por vários agrupamentos de folhagem sobre galhos primários.");
        Tree("arvore-espalhada", "Árvore de copa espalhada", PlantArchitecture.ArvoreEspalhada, CrownEnvelope.Achatada, 0.42, 0.35, 0.6, 0.34, 5, 7, 62, 0.18, 0.02,
            "Copa larga e baixa, galhos abertos quase horizontais.");
        Tree("arvore-colunar", "Árvore colunar", PlantArchitecture.ArvoreColunar, CrownEnvelope.Colunar, 0.12, 0.15, 0.85, 0.45, 6, 9, 18, 0.05, 0.016,
            "Copa estreita e alta, galhos ascendentes junto ao tronco.", 1.0);
        Tree("arvore-conica", "Árvore cônica", PlantArchitecture.ArvoreConica, CrownEnvelope.Conica, 0.1, 0.12, 0.85, 0.4, 7, 10, 70, 0.1, 0.017,
            "Copa piramidal com galhos em camadas, mais curtos para o topo.", 1.0);
        Tree("arvore-irregular", "Árvore de copa irregular", PlantArchitecture.ArvoreIrregular, CrownEnvelope.Irregular, 0.35, 0.75, 0.55, 0.33, 3, 5, 50, 0.15, 0.02,
            "Copa assimétrica, com massas de folhagem separadas por galhos longos.");

        // --- Árvores de referência --------------------------------------------------------------------------------
        Plant3DProfile ipe = Tree("ipe", "Ipê (referência)", PlantArchitecture.ArvoreIrregular, CrownEnvelope.Irregular, 0.38, 0.55, 0.5, 0.3, 4, 6, 40, 0.08, 0.022,
            "Ipês: tronco reto, galhos ascendentes em forquilha e copa aberta e irregular; floração em cachos densos nas pontas, muitas vezes sem folhas.");
        ipe.Foliage = new FoliageProfile { Shape = LeafShape.Palmada, LeafLengthM = 0.18, WidthRatio = 0.6, ToneVariation = 0.14 };
        ipe.Flowers = new FlowerProfile { Type = InflorescenceType.Cacho, SizeM = 0.3, PerCluster = 9, Abundance = 0.85 };
        ipe.Fruits = new FruitProfile { Type = FruitType.Vagem, SizeM = 0.25, PerCluster = 2, Abundance = 0.2 };
        ipe.Tags.AddRange(new[] { "Handroanthus", "Tabebuia", "Cybistax", "referência" });

        Plant3DProfile flamboyant = Tree("flamboyant", "Flamboyant (referência)", PlantArchitecture.ArvoreEspalhada, CrownEnvelope.GuardaChuva, 0.45, 0.3, 0.6, 0.3, 5, 7, 72, 0.12, 0.024,
            "Copa ampla em guarda-chuva, galhos primários longos e abertos; folhas compostas finas; floração em cachos sobre a copa.", 0.92);
        flamboyant.Foliage = new FoliageProfile { Shape = LeafShape.Pinada, LeafLengthM = 0.35, WidthRatio = 0.35, ToneVariation = 0.1 };
        flamboyant.Flowers = new FlowerProfile { Type = InflorescenceType.Cacho, SizeM = 0.35, PerCluster = 8, Abundance = 0.75 };
        flamboyant.Fruits = new FruitProfile { Type = FruitType.Vagem, SizeM = 0.45, PerCluster = 2, Abundance = 0.35 };
        flamboyant.Tags.AddRange(new[] { "Delonix", "referência" });

        Plant3DProfile araucaria = Tree("araucaria", "Araucária", PlantArchitecture.ArvoreEspalhada, CrownEnvelope.GuardaChuva, 0.62, 0.2, 0.75, 0.26, 7, 9, 78, 0.0, 0.02,
            "Tronco alto e reto com galhos em verticilo no topo, formando a taça (candelabro) típica.", 1.0);
        araucaria.Foliage = new FoliageProfile { Shape = LeafShape.Agulha, LeafLengthM = 0.05, WidthRatio = 0.15 };
        araucaria.Branches!.SecondaryPerPrimary = 1;
        araucaria.Flowers = null;
        araucaria.Tags.Add("Araucaria");

        Plant3DProfile conifer = Tree("conifera", "Conífera", PlantArchitecture.ArvoreConica, CrownEnvelope.Conica, 0.08, 0.1, 0.9, 0.42, 8, 11, 72, 0.15, 0.016,
            "Copa cônica densa de folhas em agulha ou escamas (pinheiros, ciprestes, tuias).", 1.0);
        conifer.Foliage = new FoliageProfile { Shape = LeafShape.Agulha, LeafLengthM = 0.04, WidthRatio = 0.15 };
        conifer.Flowers = null;
        conifer.Tags.AddRange(new[] { "Pinus", "Cupressus", "Podocarpus", "Juniperus", "Platycladus", "Casuarina" });

        // --- Palmeiras --------------------------------------------------------------------------------------------
        Plant3DProfile Palm(string id, string name, PlantArchitecture a, int fmin, int fmax, LeafShape frond, double length, double arch, double stipe, double crownshaft,
            int stems, double rigidity, string description)
        {
            var p = new Plant3DProfile
            {
                Id = id,
                Name = name,
                Architecture = a,
                Description = description,
                Crown = new CrownProfile { Envelope = CrownEnvelope.Elipsoide, BaseRatio = 0.8, Irregularity = 0.2, Density = 0.6 },
                Trunk = new TrunkProfile { RadiusRatio = stipe, Taper = 0.8, Flare = 0.15, Stems = stems, LeanDeg = a == PlantArchitecture.PalmeiraTouceira ? 9 : 2, Color = "#8C8577" },
                Foliage = new FoliageProfile { Shape = frond, LeafLengthM = 0.4, WidthRatio = 0.06, ToneVariation = 0.1 },
                Palm = new PalmProfile { FrondsMin = fmin, FrondsMax = fmax, Frond = frond, FrondLengthRatio = length, Arch = arch, StipeRatio = stipe, Crownshaft = crownshaft, Stems = stems, Rigidity = rigidity },
                Flowers = new FlowerProfile { Type = InflorescenceType.Panicula, SizeM = 0.5, PerCluster = 6, Abundance = 0.3 },
                Fruits = new FruitProfile { Type = FruitType.CachoPalmeira, SizeM = 0.025, PerCluster = 30, Abundance = 0.5 },
                Budget = PalmBudget(),
            };
            list.Add(p);
            return p;
        }

        Palm("palmeira-estipe", "Palmeira de estipe único", PlantArchitecture.PalmeiraEstipeUnico, 11, 15, LeafShape.Pinada, 1.05, 0.6, 0.02, 0, 1, 0.4,
            "Estipe único com folhas pinadas arqueadas e folíolos ao longo da raque.");
        Plant3DProfile jeriva = Palm("jeriva", "Jerivá (referência)", PlantArchitecture.PalmeiraEstipeUnico, 12, 16, LeafShape.Pinada, 1.1, 0.75, 0.022, 0, 1, 0.25,
            "Estipe cinza e anelado, folhas pinadas longas e bem arqueadas, folíolos em vários planos (aspecto plumoso).");
        jeriva.Tags.AddRange(new[] { "Syagrus romanzoffiana", "referência" });
        Palm("palmeira-imperial", "Palmeira-imperial", PlantArchitecture.PalmeiraEstipeUnico, 14, 18, LeafShape.Pinada, 0.95, 0.55, 0.025, 0.12, 1, 0.45,
            "Estipe liso e claro com palmito verde e folhas pinadas longas.").Tags.Add("Roystonea");
        Palm("palmeira-leque", "Palmeira de folhas em leque", PlantArchitecture.PalmeiraEstipeUnico, 12, 18, LeafShape.Palmada, 0.85, 0.35, 0.026, 0, 1, 0.6,
            "Folhas palmadas (em leque) com pecíolos longos.").Tags.AddRange(new[] { "Livistona", "Washingtonia", "Bismarckia", "Copernicia", "Mauritia", "Licuala" });
        Palm("palmeira-touceira", "Palmeira de touceira", PlantArchitecture.PalmeiraTouceira, 6, 8, LeafShape.Pinada, 0.75, 0.55, 0.012, 0.06, 6, 0.35,
            "Vários estipes finos de alturas diferentes saindo da base (areca, ráfis, pupunha).");
        Plant3DProfile cica = Palm("cica", "Cica", PlantArchitecture.PalmeiraEstipeUnico, 18, 26, LeafShape.Pinada, 1.0, 0.35, 0.09, 0, 1, 0.9,
            "Caule curto e grosso com coroa de folhas pinadas rígidas.");
        cica.Crown.BaseRatio = 0.35;
        cica.Trunk!.Color = "#5E4B33";
        cica.Fruits = null;
        cica.Tags.Add("Cycas");

        // --- Arbustos ---------------------------------------------------------------------------------------------
        Plant3DProfile Shrub(string id, string name, PlantArchitecture a, CrownEnvelope env, int stems, double density, double cluster, string description)
        {
            var p = new Plant3DProfile
            {
                Id = id,
                Name = name,
                Architecture = a,
                Description = description,
                Crown = new CrownProfile { Envelope = env, BaseRatio = 0.08, Irregularity = 0.35, Density = density, ClusterScale = cluster },
                Trunk = new TrunkProfile { RadiusRatio = 0.012, Taper = 0.5, Flare = 0, Stems = stems, LeanDeg = 14 },
                Branches = new BranchProfile { PrimaryMin = 0, PrimaryMax = 0, AngleDeg = 30, SecondaryPerPrimary = 0 },
                Foliage = new FoliageProfile { Shape = LeafShape.Oval, LeafLengthM = 0.06, WidthRatio = 0.5 },
                Flowers = new FlowerProfile { Type = InflorescenceType.Isolada, SizeM = 0.06, PerCluster = 3, Abundance = 0.5 },
                Fruits = new FruitProfile { Type = FruitType.Baga, SizeM = 0.015, PerCluster = 5, Abundance = 0.3 },
                Budget = ShrubBudget(),
            };
            list.Add(p);
            return p;
        }

        Shrub("arbusto-arredondado", "Arbusto arredondado", PlantArchitecture.ArbustoArredondado, CrownEnvelope.Elipsoide, 4, 0.75, 0.45,
            "Vários caules saindo da base e grupos de folhagem sobrepostos formando um volume globoso irregular.");
        Shrub("arbusto-ereto", "Arbusto ereto", PlantArchitecture.ArbustoEreto, CrownEnvelope.Colunar, 5, 0.7, 0.42,
            "Caules verticais e folhagem alongada para cima.");
        Plant3DProfile tufos = Shrub("tufos-em-caule", "Tufos em caule", PlantArchitecture.TufosEmCaule, CrownEnvelope.Elipsoide, 3, 0.6, 0.3,
            "Caules finos com tufos de folhas em fita no topo (dracenas, cordilines, iúcas).");
        tufos.Rosette = new RosetteProfile { LeavesMin = 16, LeavesMax = 24, Leaf = LeafShape.Linear, WidthRatio = 0.08, Thickness = 0.05, InclinationDeg = 50, Curvature = 0.45, StemRatio = 0.7 };
        tufos.Budget = RosetteBudget();
        tufos.Flowers = null;
        tufos.Tags.AddRange(new[] { "Dracaena", "Cordyline", "Yucca" });

        Plant3DProfile vine = Shrub("trepadeira", "Trepadeira", PlantArchitecture.Trepadeira, CrownEnvelope.Colunar, 3, 0.65, 0.33,
            "Massa vegetal ascendente sobre ramos finos em leque; não inventa suporte.");
        vine.Flowers = new FlowerProfile { Type = InflorescenceType.Cacho, SizeM = 0.12, PerCluster = 6, Abundance = 0.6 };

        // --- Touceiras, rosetas e herbáceas ----------------------------------------------------------------------
        Plant3DProfile Rosette(string id, string name, PlantArchitecture a, int lmin, int lmax, LeafShape leaf, double width, double thickness, double incl, double curv,
            string description, GeometryBudget budget)
        {
            var p = new Plant3DProfile
            {
                Id = id,
                Name = name,
                Architecture = a,
                Description = description,
                Crown = new CrownProfile { Envelope = CrownEnvelope.Elipsoide, BaseRatio = 0, Irregularity = 0.2, Density = 0.7 },
                Foliage = new FoliageProfile { Shape = leaf, LeafLengthM = 0.4, WidthRatio = width },
                Rosette = new RosetteProfile { LeavesMin = lmin, LeavesMax = lmax, Leaf = leaf, WidthRatio = width, Thickness = thickness, InclinationDeg = incl, Curvature = curv },
                Budget = budget,
            };
            list.Add(p);
            return p;
        }

        Plant3DProfile grass = Rosette("graminea", "Gramínea ou touceira", PlantArchitecture.Graminea, 40, 70, LeafShape.Linear, 0.03, 0.04, 30, 0.6,
            "Lâminas finas e arqueadas nascendo de uma base compacta.", ClumpBudget());
        grass.Flowers = new FlowerProfile { Type = InflorescenceType.Plumas, SizeM = 0.3, PerCluster = 1, Abundance = 0.6, AboveFoliage = true };
        Plant3DProfile strap = Rosette("touceira-fita", "Touceira de folhas em fita", PlantArchitecture.Graminea, 18, 28, LeafShape.Linear, 0.07, 0.05, 32, 0.5,
            "Folhas em fita mais largas (agapanto, moreia, hemerocale) e hastes florais acima da folhagem.", ClumpBudget());
        strap.Flowers = new FlowerProfile { Type = InflorescenceType.Umbela, SizeM = 0.12, PerCluster = 8, Abundance = 0.6, AboveFoliage = true };
        Rosette("roseta-rigida", "Roseta de folhas rígidas", PlantArchitecture.RosetaRigida, 16, 24, LeafShape.Lanceolada, 0.12, 0.1, 40, 0.3,
            "Folhas rígidas em espiral a partir de um centro (iúcas baixas, espadas, fórmio).", RosetteBudget());
        Plant3DProfile agave = Rosette("agave", "Agave (referência)", PlantArchitecture.RosetaRigida, 18, 26, LeafShape.Suculenta, 0.3, 0.18, 52, 0.45,
            "Roseta suculenta: folhas largas, espessas e arqueadas saindo de um centro, em espiral.", RosetteBudget());
        agave.Tags.AddRange(new[] { "Agave", "referência" });
        Plant3DProfile bromelia = Rosette("bromelia", "Bromélia", PlantArchitecture.RosetaRigida, 14, 22, LeafShape.Linear, 0.16, 0.06, 45, 0.55,
            "Folhas em fita largas sobrepostas formando um vaso central; inflorescência no centro.", RosetteBudget());
        bromelia.Flowers = new FlowerProfile { Type = InflorescenceType.Espiga, SizeM = 0.25, PerCluster = 1, Abundance = 1, AboveFoliage = true };
        bromelia.Tags.AddRange(new[] { "Alcantarea", "Aechmea", "Vriesea", "Billbergia", "Ananas" });
        Plant3DProfile tank = Rosette("bromelia-tanque", "Bromélia de tanque", PlantArchitecture.RosetaRigida, 16, 24, LeafShape.Linear, 0.17, 0.06, 50, 0.5,
            "Roseta aberta em vaso; na floração, as folhas do centro ficam coloridas e as flores ficam escondidas no tanque (neoregélias, nidulários).", RosetteBudget());
        tank.Flowers = new FlowerProfile { Type = InflorescenceType.Isolada, SizeM = 0.1, PerCluster = 1, Abundance = 1, AboveFoliage = false };
        tank.Tags.AddRange(new[] { "Neoregelia", "Nidularium" });
        Plant3DProfile fern = Rosette("samambaia", "Samambaia", PlantArchitecture.RosetaRigida, 14, 22, LeafShape.Pinada, 0.25, 0.03, 42, 0.5,
            "Frondes pinadas arqueadas saindo do centro.", RosetteBudget());
        fern.Tags.AddRange(new[] { "Nephrolepis", "Rumohra" });

        Plant3DProfile broad = Rosette("herbacea-folha-larga", "Herbácea de folhas largas", PlantArchitecture.HerbaceaFolhaLarga, 7, 11, LeafShape.Oval, 0.42, 0.04, 30, 0.35,
            "Folhas grandes com pecíolo, em touceira (filodendros, alocásias, helicônias, antúrios).", RosetteBudget());
        broad.Rosette!.PetioleRatio = 0.45;
        broad.Flowers = new FlowerProfile { Type = InflorescenceType.Bracteas, SizeM = 0.2, PerCluster = 4, Abundance = 0.5, AboveFoliage = true };
        Plant3DProfile strelitzia = Rosette("estrelitzia", "Estrelítzia (referência)", PlantArchitecture.HerbaceaFolhaLarga, 9, 13, LeafShape.Lanceolada, 0.3, 0.04, 22, 0.25,
            "Folhas largas e alongadas com pecíolos longos dispostas em leque; flor ave-do-paraíso sobre haste.", RosetteBudget());
        strelitzia.Rosette!.PetioleRatio = 0.55;
        strelitzia.Rosette.Fan = true;
        strelitzia.Flowers = new FlowerProfile { Type = InflorescenceType.AveDoParaiso, SizeM = 0.18, PerCluster = 1, Abundance = 0.5, AboveFoliage = true };
        strelitzia.Tags.AddRange(new[] { "Strelitzia", "referência" });

        Plant3DProfile ravenala = Rosette("ravenala", "Árvore-do-viajante", PlantArchitecture.HerbaceaFolhaLarga, 14, 20, LeafShape.Lanceolada, 0.28, 0.04, 18, 0.2,
            "Leque de folhas de bananeira sobre um caule, todas num mesmo plano.", RosetteBudget());
        ravenala.Rosette!.PetioleRatio = 0.4;
        ravenala.Rosette.Fan = true;
        ravenala.Rosette.StemRatio = 0.45;
        ravenala.Tags.Add("Ravenala");

        Plant3DProfile cover = Rosette("forracao", "Forração", PlantArchitecture.Forracao, 0, 0, LeafShape.Oval, 0.5, 0.05, 70, 0.3,
            "Tapete baixo de pequenos grupos de folhagem; em maciço, o volume acompanha o terreno.", ClumpBudget());
        cover.Rosette = null;
        cover.Crown = new CrownProfile { Envelope = CrownEnvelope.Achatada, BaseRatio = 0, Irregularity = 0.4, Density = 0.8, ClusterScale = 0.35 };
        cover.Flowers = new FlowerProfile { Type = InflorescenceType.Isolada, SizeM = 0.03, PerCluster = 1, Abundance = 0.5 };
        Plant3DProfile lawn = Rosette("gramado", "Gramado", PlantArchitecture.Gramado, 0, 0, LeafShape.Linear, 0.05, 0.02, 10, 0.2,
            "Superfície baixa; em grandes áreas, nunca lâminas individuais.", ClumpBudget());
        lawn.Rosette = null;
        Plant3DProfile emergent = Rosette("aquatica-emergente", "Aquática emergente", PlantArchitecture.AquaticaEmergente, 18, 30, LeafShape.Linear, 0.05, 0.04, 14, 0.25,
            "Folhas eretas saindo da água (taboa, papiro, íris, tália).", ClumpBudget());
        emergent.Flowers = new FlowerProfile { Type = InflorescenceType.Espiga, SizeM = 0.2, PerCluster = 1, Abundance = 0.5, AboveFoliage = true };
        Plant3DProfile floating = Rosette("aquatica-flutuante", "Aquática flutuante", PlantArchitecture.AquaticaFlutuante, 5, 9, LeafShape.Cordiforme, 1.0, 0.03, 88, 0,
            "Folhas arredondadas sobre a água (ninfeias, vitória-régia, aguapé) e flores na superfície.", RosetteBudget());
        floating.Flowers = new FlowerProfile { Type = InflorescenceType.Isolada, SizeM = 0.12, PerCluster = 1, Abundance = 0.5 };

        foreach (Plant3DProfile p in list)
        {
            p.Normalize();
        }

        return list;
    }
}
