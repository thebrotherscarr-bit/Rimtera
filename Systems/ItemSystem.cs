using Rimtera.World;

namespace Rimtera.Systems;

public enum ItemId : ushort
{
    SoilBlock,
    RockBlock,
    SandBlock,
    WoodBlock,
    CopperOre,
    WoodenPickaxe,
    CopperPickaxe,
    WoodenSword,
    CopperBow,
    Arrow,
    Berry,
    WorkbenchBlock,
    FurnaceBlock,
    AnvilBlock,
    CopperBar,
    SnowBlock,
    DesertGlassBlock,
    JungleRootBlock,
    FungalBlock,
    EmberStoneBlock,
    GloamRockBlock,
    RiftSoilBlock,
    PrismGrassBlock,
    AncientBrickBlock,
    Trailcap,
    Trailcoat,
    Trailboots,
    HearthCharm,
    FestivalMask,
    RestorativeTonic,
    ManaTonic,
    CopperSword,
    CobaltOre,
    ViridiumOre,
    EnergyCrystal,
    PlasmaPistol,
    SniperRifle,
    GrenadeLauncher,
    RocketLauncher,
    MiniTurret,
    HydroponicsTray,
    SolarPanel,
    OxygenUnit,
    GlacialOre,
    UraniumOre,
    PyrociteOre,
    ScrapIronOre,
    FrostCrystal,
    CausticShard,
    ObsidianShard,
    OxidizedShard,
    InsulatedSuit,
    HeatWard,
    RadFilterMask,
    // Blight purification and world loot.
    PurifierSprayer,
    PurifierGel,
    PurifierBeacon,
    LifeCore,
    AshBlock,

    // ---- Content expansion (CONTENT_EXPANSION.md §2). Append-only below this line. ----
    // Currency and progression materials.
    Credits,
    FerriteBar,
    CobaltBar,
    GlacialBar,
    PyrociteBar,
    UraniumRod,
    ViridiumBar,
    LumeniteBar,
    VoidsteelBar,
    AstralBar,
    MeteoriteBar,
    LumeniteOre,
    VoidsteelOre,
    MeteoriteOre,
    AstralOre,
    RubyShard,
    SapphireShard,
    EmeraldShard,
    AmethystShard,
    TopazShard,
    DiamondShard,
    PowerCell,
    BioGel,
    OverseerCircuit,
    HeartFragment,
    VaultKey,
    DroneSpareParts,
    SilkThread,
    ClothRoll,
    CoralShard,
    GlowSpore,
    PrismShard,
    VoidEssence,
    RopeCoil,
    WireTool,

    // Tier 1 weapons.
    FerriteShiv,
    ScrapCleaver,
    BoneClub,
    ReturnBoomerang,
    ScrapPistol,
    FerriteRevolver,
    DartBlower,
    SparkEmitter,
    ScoutDroneRemote,
    // Tier 2.
    CobaltSaber,
    ChainFlail,
    ShockSpear,
    TetherWhip,
    ThrowingDisc,
    YoyoDrone,
    LaserScalpel,
    EnergyCutlassMk2,
    CobaltCarbine,
    NailGun,
    ArcGlove,
    DatapadFlare,
    DatapadSonic,
    HornetDroneRemote,
    RepairDroneRemote,
    MechCompanionKey,
    SignalWhipChain,
    TurretAutogun,
    // Tier 3 (glacial, pyrocite, uranium and biome drops).
    FrostbiteEdge,
    PyrociteMaul,
    UraniumGlaive,
    MushroomMace,
    CoralTrident,
    SpikedChain,
    GlacialCrossbow,
    PyrociteFlamer,
    PumpScattergun,
    HarpoonGun,
    UraniumRailgun,
    FrostProjector,
    EmberRod,
    RadBeamRod,
    CoralWand,
    SporeCenser,
    DatapadSeeker,
    FrostDroneRemote,
    EmberDroneRemote,
    RadSwarmHive,
    CoralJellyFamiliar,
    SporeFamiliar,
    TurretFlame,
    // Tier 4.
    ViridiumKatana,
    ChainsawSword,
    HiveStingerRapier,
    ViridiumMinigun,
    RadMinigun,
    MortarTube,
    DatapadChainLightning,
    DatapadTesla,
    HiveFamiliar,
    DroneController,
    SignalWhipMark,
    TurretMortar,
    // Tier 5.
    LumeniteGreatsword,
    PowerFist,
    LumeniteLongbow,
    MagneticCarbine,
    PrismLensStaff,
    CrystalShardWand,
    DatapadStorm,
    MeteorCaller,
    LumeniteSpriteBeacon,
    SwarmController,
    TurretTesla,
    // Tier 6.
    VoidsteelScythe,
    PlasmaPike,
    VoidsteelSniper,
    ChargeRifle,
    VoidOrbFocus,
    DatapadGravityWell,
    DatapadSwarmMissiles,
    VoidFamiliarCore,
    SignalWhipOverdrive,
    // Tier 7.
    AstralBlade,
    AstralRepeater,
    AstralConductor,
    DatapadNova,
    AstralGuardianCore,

    // Ammunition (Energy cells stay ItemId.Arrow).
    SlugRounds,
    ShotgunShells,
    ToxicDarts,
    CryoRounds,
    FuelCanister,
    MiniRocket,
    ExplosiveCharge,

    // Class accessories and consumables.
    FocusLensCrystal,
    OverchargeCapacitor,
    CommandVisor,
    DroneBattery,
    ReinforcedArmorSuit,
    ChargeCellSmall,
    MedStim,
    ChargeStim,
    SpeedStim,
    ArmorStim,
    RegenPatch,
    SpelunkerSpray,
    RationBar,
    JetBoots,
    EnergyShieldEmitter,
    NightVisionGoggles,
    MagnetRing,
    MeleeModule,
    BallisticsModule,
    TechModule,
    CommandModule,
    TideFish,

    // Tools: drills, cutters, demolishers and utilities.
    FerriteDrill,
    CobaltDrill,
    GlacialDrill,
    ViridiumDrill,
    LumeniteDrill,
    VoidsteelDrill,
    AstralDrill,
    FerriteCutter,
    CobaltCutter,
    GlacialCutter,
    ViridiumCutter,
    LumeniteCutter,
    VoidsteelCutter,
    AstralCutter,
    FerriteDemolisher,
    CobaltDemolisher,
    GlacialDemolisher,
    ViridiumDemolisher,
    LumeniteDemolisher,
    VoidsteelDemolisher,
    AstralDemolisher,
    BugNet,
    FishingRod,
    EmptyBucket,
    BucketWater,
    BucketLava,
    PaintSprayer,

    // Armor: two generic sets, then one set per class per tier from tier 3.
    ScavHelmet, ScavChest, ScavLegs,
    CobaltTrooperHelmet, CobaltTrooperChest, CobaltTrooperLegs,
    PyrociteBulwarkHelmet, PyrociteBulwarkChest, PyrociteBulwarkLegs,
    GlacialRangerHelmet, GlacialRangerChest, GlacialRangerLegs,
    UraniumHazmatHelmet, UraniumHazmatChest, UraniumHazmatLegs,
    GlacialHandlerHelmet, GlacialHandlerChest, GlacialHandlerLegs,
    ViridiumWardenHelmet, ViridiumWardenChest, ViridiumWardenLegs,
    ViridiumStalkerHelmet, ViridiumStalkerChest, ViridiumStalkerLegs,
    ViridiumSageHelmet, ViridiumSageChest, ViridiumSageLegs,
    ViridiumHandlerHelmet, ViridiumHandlerChest, ViridiumHandlerLegs,
    LumenitePaladinHelmet, LumenitePaladinChest, LumenitePaladinLegs,
    LumeniteMarksmanHelmet, LumeniteMarksmanChest, LumeniteMarksmanLegs,
    LumeniteSageHelmet, LumeniteSageChest, LumeniteSageLegs,
    LumeniteHandlerHelmet, LumeniteHandlerChest, LumeniteHandlerLegs,
    VoidsteelReaverHelmet, VoidsteelReaverChest, VoidsteelReaverLegs,
    VoidsteelMarksmanHelmet, VoidsteelMarksmanChest, VoidsteelMarksmanLegs,
    VoidsteelWraithHelmet, VoidsteelWraithChest, VoidsteelWraithLegs,
    VoidsteelHandlerHelmet, VoidsteelHandlerChest, VoidsteelHandlerLegs,
    AstralWardenHelmet, AstralWardenChest, AstralWardenLegs,
    AstralMarksmanHelmet, AstralMarksmanChest, AstralMarksmanLegs,
    AstralSageHelmet, AstralSageChest, AstralSageLegs,
    AstralHandlerHelmet, AstralHandlerChest, AstralHandlerLegs,

    // Crafting stations.
    FieldWorkbench,
    NanoFabricatorMk2,
    OreRefineryMk2,
    PowerForgeMk2,
    AlloyAnvil,
    VoidForge,
    AstralAssembler,
    ChemLab,
    BioLab,
    Loom,
    Sawmill,
    Kiln,
    CookingStation,
    TinkerBench,
    DroneBay,
    AmmoPress,
    ChargeReactor,
    ResearchTerminal,

    // Boss summons (one per boss) and event summons.
    SummonReactorKey,
    SummonSwarmPheromone,
    SummonFaultyMiningChip,
    SummonRottingSeed,
    SummonScoutBeacon,
    SwarmResonator,
    RenegadeCredentials,
    MutagenBloom,
    TaintedTissue,
    WarshipTransponder,
    SunspotLens,
    VoidLure,
    ClaimMarker,
    BountyNotice,
    SummonAstralSigil,
    SporeCrown,
    RoyalNectar,
    ScrapBeacon,
    SummonPirateTransmitter,
    SummonSignalCore,
    FestivalFlare,

    // Critters caught with the bug net.
    GlowGrub,
    SandSkitterer,
    ScavengerBeetle,
    SkyFlier,
    TerraRat,
    SnowHare,
    ScrapRat,
    EmberNewt,
    GlowFirefly,
    PebbleBeetle,
    GoldenCritter,

    // Natural block items.
    MudBlock,
    ClayBlock,
    GravelBlock,
    SiltBlock,
    SaltCrustBlock,
    AcidSandBlock,
    ShellSandBlock,
    CoralReefBlock,
    SlateBlock,
    AlabasterBlock,
    BasaltColumnBlock,
    HiveCombBlock,
    NectarBlock,
    PearlStoneBlock,
    ThinIceBlock,
    MossBlock,
    // Crafted block items.
    FerriteBrickBlock,
    CobaltBrickBlock,
    ViridiumBrickBlock,
    LumeniteBrickBlock,
    VoidsteelBrickBlock,
    AstralBrickBlock,
    SlateBrickBlock,
    AlabasterBrickBlock,
    BasaltBrickBlock,
    PearlBrickBlock,
    HiveBrickBlock,
    CoralBrickBlock,
    GlassBlock,
    ReinforcedGlassBlock,
    HullPlatingGreyBlock,
    HullPlatingBlueBlock,
    HullPlatingGreenBlock,
    HullPlatingRedBlock,
    HullPlatingHazardBlock,
    AstroTimberPlanksBlock,
    CharredPlanksBlock,
    CrystalBlockBlock,
    CircuitPanelBlock,
    ConveyorBlock,
    GrateBlock,
    PlateBlock,
    VentBlock,

    // Furniture.
    PlankDoor,
    HullDoor,
    Trapdoor,
    VentDoor,
    PlankChair,
    HullChair,
    Stool,
    Armchair,
    Sofa,
    ObservationChair,
    PlankTable,
    HullTable,
    DiningTable,
    CoffeeTable,
    BunkBed,
    BunkBedMetal,
    ModularBed,
    CryoPodBed,
    StorageCrate,
    WardrobeLocker,
    Shelving,
    ShelfJars,
    StorageContainer,
    FoodLocker,
    WallTorch,
    DeskLamp,
    FloorLamp,
    CeilingLight,
    StandingFloodlight,
    GlowLampAmber,
    GlowLampCyan,
    GlowLampViolet,
    ColonyBanner,
    SignPost,
    PaintingPlanet,
    PlantPot,
    HerbPlanter,
    DisplayPanel,
    ControlConsole,
    PowerHub,
    BatteryCharger,
    ChemicalAnalyzer,
    FoodSynthesizer,
    WaterPurifier,
    AdvancedFabricator,
    CraftingBench,
    MetalFurnace,
    ResearchLab,

    // Wall items (player wall ids 2–19, see WallIds).
    PlankWall,
    PlatingGreyWall,
    PlatingBlueWall,
    GlassWall,
    FerriteBrickWall,
    AlabasterBrickWall,
    LatticeWall,
    CircuitWall,
    PearlWall,
    HiveWall,
    CoralWall,
    BasaltWall,
    MudWall,
    PanelWall,
    WindowWall,
    BlastWall,
    LeadWall,

    // Precursor relics, mobility/utility accessories, foods and late tools.
    AncientPowerCell,
    PrecursorRelic,
    CircuitBoard,
    RocketPack,
    GliderWings,
    GrappleHook,
    DoubleJumpCanister,
    ClimbingClaws,
    LifeMonitor,
    OreScanner,
    DepthMeter,
    CompassChip,
    RegenImplant,
    RebreatherMask,
    LavaWaderBoots,
    FrostGripGloves,
    LuckyCharm,
    ThornPlating,
    AstralHalo,
    NightOwlDrops,
    HunterLens,
    FeatherfallPill,
    WaterBreathCapsule,
    SnowWolfSteak,
    MushroomStew,
    NectarTea,
    CoralSushi,
    NanoBananaSplit,
    GlowSticks,
    TeleportBeacon,
    // Biome blocks with kitbashed art.
    ShellBeachBlock,
    MyceliumStoneBlock,
    SporeBlockBlock,
    MushroomCapBlock,
    CrystalSpireBlock,
    PearlSandBlock,
    PrecursorBrickBlock,
    PrecursorGlyphBlock,
    GeodeCyanBlock,
    GeodeMagentaBlock,
    // Summons for the new bosses.
    PrismCatalyst,
    VaultCore,
    MoonEaterLure,
    EclipseOrb,
    WarlordBanner,
    DreadnoughtFlag,
    ProtocolKey,
    MothershipBeacon,
    // Additional critters (critter_* art).
    ShellCrab,
    GlowJelly,
    BeachSkitter,
    SeaBird,
    GlowSnail,
    SporeMoth,
    CaveFrog,
    TruffleMite,
    PrismButterfly,
    LightWispBird,
    CrystalBunny,
    HaloSnail,
    CanopyParrot,
    LeafFrog,
    PollenBee,
    VineSnake,
    HopBunny,
    GrassSnake,
    PenguinDrone,
    DuneLizard,
    Worm,
    ButterflyBlue,
    ButterflyGold,
    Dragonfly,
    SquirrelBot,
    Owl,
    Duck,
    FishSilver,
    FishGlow,
    CrabSmall,
    Snail,
    LadybugBot,
    Frog,

    // Fishing (FISHING_EVENTS_WEATHER.md §3.1): rods, gear, bait, fish, crates, junk and quest fish.
    FerriteFishingRod,
    CobaltFishingRod,
    GlacialFishingRod,
    ViridiumFishingRod,
    LumeniteFishingRod,
    VoidsteelFishingRod,
    AstralFishingRod,
    AcidFishingRod,
    MagmaFishingRod,
    FishingLine,
    FishingLure,
    AcidWader,
    LavaLure,
    CatchScanner,
    AbyssBait,
    FrostCod,
    EmberKoi,
    AcidEel,
    ReefSnapper,
    NectarMinnow,
    BlightCarp,
    PrismTrout,
    SporeGuppy,
    MudSkipper,
    CaveBlindfish,
    CookedFish,
    CookedEel,
    GrilledKoi,
    FishFillet,
    RustedCan,
    TangledWire,
    SoggyDatapad,
    ScrapCrate,
    AlloyCrate,
    AstralCrate,
    CoastCrate,
    FrozenCrate,
    JunkCrate,
    FungalCrate,
    CanopyCrate,
    CrystalCrate,
    BlightCrate,
    MoltenCrate,
    SpinyRustback,
    LanternEel,
    GlassPike,
    TwinTailSnapper,
    MottledGrouper,
    Hexscale,
    BubbleRay,
    CrownedTetra,
    VoidAngler,
    StarfallMinnow,
    // ---- Batch A: world physics items (definitions in Systems/Content/BatchAItems.cs) ----
    Timber, Sapling, CactusChunk, PlankPlatform, MetalPlatform, ClimbingRope, Ladder, ShapingHammer,
    SunpetalHerb, MoonglowHerb, RainreedHerb, FrostbellHerb, EmberrootHerb, SporecapHerb,
    SunpetalSeeds, MoonglowSeeds, RainreedSeeds, FrostbellSeeds, EmberrootSeeds, SporecapSeeds,
    WireSpool, WireCutter, Actuator, Switch, Lever, PressurePlate, Timer1s, Timer3s, Timer5s,
    GateAnd, GateOr, GateNot, GateXor, DartTrap, ExplosiveTrap, TeleporterPad,
    Bomb, Dynamite, StickyBomb, StandingTorch, PrecursorDungeonBrick, ShrineBlade, LivingWood,
    // World generation completion: dye blossoms drop petals for future dyes.
    DyePetals,
    // ---- Batch B: player items (definitions in Systems/Content/PlayerItems.cs; explicit high band) ----
    ChargeCrystal = 5000,
    CargoPod = 5001,
    CargoPodStation = 5002,
    PetDroneChip = 5003,
    PetCrabShell = 5004,
    LightPetWisp = 5005,
    LightPetLantern = 5006,
    HoverBikeKey = 5007,
    RecallCapsule = 5008,
    PhaseRod = 5009,
    MemorialMarker = 5010,
    // ---- Arsenal pass (definitions in Systems/Content/ArsenalItems*.cs; explicit band from 6000) ----
    FerriteLongblade = 6000,
    CobaltLongblade,
    GlacialLongblade,
    PyrociteLongblade,
    UraniumLongblade,
    ViridiumLongblade,
    LumeniteLongblade,
    VoidsteelLongblade,
    AstralLongblade,
    FerriteGreatblade,
    CobaltGreatblade,
    GlacialGreatblade,
    PyrociteGreatblade,
    UraniumGreatblade,
    ViridiumGreatblade,
    LumeniteGreatblade,
    VoidsteelGreatblade,
    AstralGreatblade,
    FerriteArcSaber,
    CobaltArcSaber,
    GlacialArcSaber,
    PyrociteArcSaber,
    UraniumArcSaber,
    ViridiumArcSaber,
    LumeniteArcSaber,
    VoidsteelArcSaber,
    AstralArcSaber,
    RustfangCleaver,
    SentinelEdge,
    SkitterscaleSword,
    ThornvineBlade,
    RimeheartBlade,
    MagmaBrand,
    MyceliumGreatblade,
    WarlordsHeadsman,
    HivequeenCarver,
    PrismRazor,
    EclipseReaver,
    Vaultbreaker,
    StarforgedClaymore,
    FerriteStiletto,
    CobaltStiletto,
    PyrociteStiletto,
    ViridiumStiletto,
    LumeniteStiletto,
    VoidsteelStiletto,
    AstralStiletto,
    CoralFang,
    SpliceNeedle,
    PhaseDirk,
    FerriteArcSpear,
    CobaltArcSpear,
    GlacialArcSpear,
    PyrociteArcSpear,
    UraniumArcSpear,
    ViridiumArcSpear,
    LumeniteArcSpear,
    VoidsteelArcSpear,
    AstralArcSpear,
    HarpoonLance,
    ScoutmechPike,
    OverseerHalberd,
    SolarPartisan,
    ProtocolGlaive,
    LeviathanTine,
    FerriteSpinner,
    CobaltSpinner,
    GlacialSpinner,
    PyrociteSpinner,
    UraniumSpinner,
    MeteoriteSpinner,
    ViridiumSpinner,
    LumeniteSpinner,
    VoidsteelSpinner,
    AstralSpinner,
    Silkspool,
    GeodeGyro,
    SporepuffGyro,
    SwarmGyro,
    FloraGyre,
    HaloGyre,
    CorsairTop,
    SingularitySpinner,
    FerriteGlaiveDisc,
    CobaltGlaiveDisc,
    PyrociteGlaiveDisc,
    ViridiumGlaiveDisc,
    LumeniteGlaiveDisc,
    VoidsteelGlaiveDisc,
    AstralGlaiveDisc,
    ScrapChakram,
    FrostCrescent,
    AuroraRing,
    FerriteChainMace,
    CobaltChainMace,
    GlacialChainMace,
    PyrociteChainMace,
    UraniumChainMace,
    ViridiumChainMace,
    LumeniteChainMace,
    VoidsteelChainMace,
    AstralChainMace,
    WreckingOrb,
    MagmaFlail,
    MeteorChain,
    FerriteSignalLash,
    CobaltSignalLash,
    GlacialSignalLash,
    PyrociteSignalLash,
    UraniumSignalLash,
    ViridiumSignalLash,
    LumeniteSignalLash,
    VoidsteelSignalLash,
    AstralSignalLash,
    VineLash,
    SporeTendril,
    HiveLash,
    PrismRibbon,
    VoidTether,
    Starlash,
    FerriteRailBow,
    CobaltRailBow,
    GlacialRailBow,
    PyrociteRailBow,
    UraniumRailBow,
    ViridiumRailBow,
    LumeniteRailBow,
    VoidsteelRailBow,
    AstralRailBow,
    FerritePulseRepeater,
    CobaltPulseRepeater,
    GlacialPulseRepeater,
    PyrocitePulseRepeater,
    UraniumPulseRepeater,
    ViridiumPulseRepeater,
    LumenitePulseRepeater,
    VoidsteelPulseRepeater,
    AstralPulseRepeater,
    HuntersRecurve,
    Thornshot,
    FrostbiteLongbow,
    CinderRepeater,
    StingerBow,
    CanopyLongbow,
    CrystalHarpbow,
    StormRepeater,
    CorsairCrossbow,
    EclipseLongbow,
    VaultBallista,
    NovaArc,
    FerriteSidearm,
    CobaltSidearm,
    GlacialSidearm,
    PyrociteSidearm,
    UraniumSidearm,
    ViridiumSidearm,
    LumeniteSidearm,
    VoidsteelSidearm,
    AstralSidearm,
    FerriteBattleRifle,
    CobaltBattleRifle,
    PyrociteBattleRifle,
    ViridiumBattleRifle,
    LumeniteBattleRifle,
    VoidsteelBattleRifle,
    AstralBattleRifle,
    SentinelHandcannon,
    ScoutAutopistol,
    GeigerRifle,
    OverseerPistol,
    CaptainsBlaster,
    MothershipRailcannon,
    CobaltScattergun,
    PyrociteScattergun,
    ViridiumScattergun,
    VoidsteelScattergun,
    AstralScattergun,
    Swarmbuster,
    BoardingBlunderbuss,
    AbominationMaw,
    CobaltLauncher,
    PyrociteLauncher,
    MeteoriteLauncher,
    ViridiumLauncher,
    LumeniteLauncher,
    VoidsteelLauncher,
    AstralLauncher,
    DemolitionTube,
    WarshipCannon,
    MinersBlaster,
    DreadnoughtBroadside,
    StarfallLauncher,
    FerriteThrowingKnife,
    CobaltThrowingKnife,
    GlacialThrowingKnife,
    PyrociteThrowingKnife,
    UraniumThrowingKnife,
    ViridiumThrowingKnife,
    LumeniteThrowingKnife,
    VoidsteelThrowingKnife,
    AstralThrowingKnife,
    FerriteJavelin,
    CobaltJavelin,
    GlacialJavelin,
    PyrociteJavelin,
    UraniumJavelin,
    ViridiumJavelin,
    LumeniteJavelin,
    VoidsteelJavelin,
    AstralJavelin,
    FragGrenade,
    RazorStar,
    EmpCharge,
    AcidFlask,
    StickyMine,
    CryoGrenade,
    IncendiaryGrenade,
    SporeBomb,
    PlasmaGrenade,
    ShardCaltrops,
    VoidGrenade,
    StarShuriken,
    FerriteEmitterStaff,
    CobaltEmitterStaff,
    GlacialEmitterStaff,
    PyrociteEmitterStaff,
    UraniumEmitterStaff,
    MeteoriteEmitterStaff,
    ViridiumEmitterStaff,
    LumeniteEmitterStaff,
    VoidsteelEmitterStaff,
    AstralEmitterStaff,
    FerriteFocusRod,
    CobaltFocusRod,
    GlacialFocusRod,
    PyrociteFocusRod,
    UraniumFocusRod,
    MeteoriteFocusRod,
    ViridiumFocusRod,
    LumeniteFocusRod,
    VoidsteelFocusRod,
    AstralFocusRod,
    SentinelSceptre,
    OvergrowthStaff,
    DrillcoreRod,
    CryolanceStaff,
    MyceliumStaff,
    ScrapTeslaRod,
    SwarmSceptre,
    MooneaterStaff,
    BloomWand,
    SolarSceptre,
    WarpStaff,
    ProtocolStaff,
    WispStaff,
    VaultResonator,
    CosmosStaff,
    FerriteCodex,
    CobaltCodex,
    PyrociteCodex,
    ViridiumCodex,
    LumeniteCodex,
    VoidsteelCodex,
    AstralCodex,
    CodexFrostNova,
    CodexEmberSwarm,
    CodexToxicCloud,
    CodexTidalSurge,
    CodexShardStorm,
    CodexPrecursor,
    CodexEventHorizon,
    CodexSupernova,
    CobaltBeamProjector,
    PyrociteBeamProjector,
    ViridiumBeamProjector,
    LumeniteBeamProjector,
    VoidsteelBeamProjector,
    AstralBeamProjector,
    GammaRay,
    PrismLance,
    SolarRay,
    MothershipLance,
    FerriteDroneUplink,
    CobaltDroneUplink,
    GlacialDroneUplink,
    PyrociteDroneUplink,
    UraniumDroneUplink,
    ViridiumDroneUplink,
    LumeniteDroneUplink,
    VoidsteelDroneUplink,
    AstralDroneUplink,
    FerriteFamiliarCore,
    CobaltFamiliarCore,
    GlacialFamiliarCore,
    PyrociteFamiliarCore,
    UraniumFamiliarCore,
    ViridiumFamiliarCore,
    LumeniteFamiliarCore,
    VoidsteelFamiliarCore,
    AstralFamiliarCore,
    CoralBell,
    SporeLantern,
    HiveScepter,
    SwarmCrown,
    CorsairDroneDeck,
    PrismLantern,
    GuardianArray,
    CobaltSentryKit,
    PyrociteSentryKit,
    ViridiumSentryKit,
    LumeniteSentryKit,
    VoidsteelSentryKit,
    AstralSentryKit,
    FurnaceSentryKit,
    OverseerWatchtower,
    WarshipFlakKit,
    MothershipPylon,
    FerritePick,
    CobaltPick,
    GlacialPick,
    PyrocitePick,
    UraniumPick,
    MeteoritePick,
    ViridiumPick,
    LumenitePick,
    VoidsteelPick,
    AstralPick,
    ProspectorPick,
    DronecoreDrill,
    CoralPick,
    ClaimjumperDrill,
    PrismBore,
    VoidAuger,
    FerriteHatchet,
    CobaltHatchet,
    GlacialHatchet,
    PyrociteHatchet,
    UraniumHatchet,
    MeteoriteHatchet,
    ViridiumHatchet,
    LumeniteHatchet,
    VoidsteelHatchet,
    AstralHatchet,
    OvergrowthReaper,
    ChainCutter,
    PlasmaSaw,
    GlacialBreaker,
    PyrociteBreaker,
    UraniumBreaker,
    MeteoriteBreaker,
    WreckersMaul,
    SeismicHammer,
    GravityBreaker,
    AstralSledge,
    FerriteBolts,
    CobaltBolts,
    GlacialBolts,
    PyrociteBolts,
    UraniumBolts,
    SeekerBolts,
    ViridiumBolts,
    ShardBolts,
    LumeniteBolts,
    VoidsteelBolts,
    AstralBolts,
    TracerRounds,
    CobaltRounds,
    HollowPointRounds,
    IncendiaryRounds,
    VenomRounds,
    MeteoriteRounds,
    PurifierRounds,
    ViridiumRounds,
    FlechetteRounds,
    LumeniteRounds,
    ExplosiveRounds,
    SeekerRounds,
    VoidsteelRounds,
    AstralRounds,
    CryoSlugs,
    FragRocket,
    IncendiaryRocket,
    CryoRocket,
    ClusterRocket,
    BreacherRocket,
    VoidRocket,
    AstralRocket,
    BarbedDarts,
    CryoDarts,
    EmberDarts,
    VenomDarts,
    CrystalDarts,
    VoidDarts,
    GelFuel,
    PyroGel,
    CryoCoolant,
    PlasmaFuel,
    VoidFuel,
    AstralFuel,
    OverchargedCell,
    PrismCell,
    ScrapSalvagerMask,
    ScrapSalvagerSuit,
    ScrapSalvagerBoots,
    FerriteBrawlerHelm,
    FerriteBrawlerCuirass,
    FerriteBrawlerGreaves,
    FerriteGunnerVisor,
    FerriteGunnerVest,
    FerriteGunnerLeggings,
    FerriteTinkerHood,
    FerriteTinkerCoat,
    FerriteTinkerTreads,
    FerriteWranglerHeadset,
    FerriteWranglerMantle,
    FerriteWranglerBoots,
    CobaltVanguardHelm,
    CobaltVanguardCuirass,
    CobaltVanguardGreaves,
    CobaltSharpshooterVisor,
    CobaltSharpshooterVest,
    CobaltSharpshooterLeggings,
    CobaltArcanistHood,
    CobaltArcanistCoat,
    CobaltArcanistTreads,
    CobaltConductorHeadset,
    CobaltConductorMantle,
    CobaltConductorBoots,
    CoralDiverMask,
    CoralDiverSuit,
    CoralDiverBoots,
    MeteoriteStrikerHelm,
    MeteoriteStrikerCuirass,
    MeteoriteStrikerGreaves,
    PyrociteGunslingerVisor,
    PyrociteGunslingerVest,
    PyrociteGunslingerLeggings,
    GlacialCryomancerHood,
    GlacialCryomancerCoat,
    GlacialCryomancerTreads,
    UraniumSwarmlordHeadset,
    UraniumSwarmlordMantle,
    UraniumSwarmlordBoots,
    HiveSentinelHelm,
    HiveSentinelCuirass,
    HiveSentinelGreaves,
    CanopyStalkerVisor,
    CanopyStalkerVest,
    CanopyStalkerLeggings,
    SporeMysticHood,
    SporeMysticCoat,
    SporeMysticTreads,
    CoralTidecallerHeadset,
    CoralTidecallerMantle,
    CoralTidecallerBoots,
    PrismKnightHelm,
    PrismKnightCuirass,
    PrismKnightGreaves,
    CrystalDeadeyeVisor,
    CrystalDeadeyeVest,
    CrystalDeadeyeLeggings,
    SolarAdeptHood,
    SolarAdeptCoat,
    SolarAdeptTreads,
    StormMarshalHeadset,
    StormMarshalMantle,
    StormMarshalBoots,
    EclipseRevenantHelm,
    EclipseRevenantCuirass,
    EclipseRevenantGreaves,
    DreadnoughtCorsairVisor,
    DreadnoughtCorsairVest,
    DreadnoughtCorsairLeggings,
    RiftOracleHood,
    RiftOracleCoat,
    RiftOracleTreads,
    MothershipOverseerHeadset,
    MothershipOverseerMantle,
    MothershipOverseerBoots,
    AstralParagonMask,
    AstralParagonSuit,
    AstralParagonBoots,
    CloseQuartersEmblemMkI,
    CloseQuartersEmblemMkII,
    CloseQuartersEmblemMkIII,
    BallisticsEmblemMkI,
    BallisticsEmblemMkII,
    BallisticsEmblemMkIII,
    TechEmblemMkI,
    TechEmblemMkII,
    TechEmblemMkIII,
    CommandEmblemMkI,
    CommandEmblemMkII,
    CommandEmblemMkIII,
    TargetingLensMkI,
    TargetingLensMkII,
    TargetingLensMkIII,
    KineticPlateMkI,
    KineticPlateMkII,
    KineticPlateMkIII,
    SprintServosMkI,
    SprintServosMkII,
    SprintServosMkIII,
    ExtenderArm,
    TelescopicArm,
    MinersGauntlet,
    ExcavatorRig,
    Headlamp,
    FloodVisor,
    LumenHalo,
    GravAnchor,
    BulwarkCore,
    ThermalSleeve,
    MagmaSkin,
    HydroSkimmers,
    GillImplant,
    BlinkModule,
    PhaseCloak,
    BarbMesh,
    SpikeLattice,
    LeechCoil,
    VampiricChip,
    SiphonCoil,
    CapacitorTap,
    GeoScanner,
    ThreatRadar,
    PowerGlove,
    ServoGrip,
    NanoMender,
    BioWeave,
    CapacitorBank,
    ChargeCondenser,
    ArcBattery,
    UplinkAntenna,
    SentryBeacon,
    SwarmProcessor,
    VitalBooster,
    HeartPump,
    AblativeCore,
    ShockAbsorber,
    GravDamper,
    OxygenTank,
    ThermalRegulator,
    FilterImplant,
    TractorCoil,
    AnglersVisor,
    TackleKit,
    ReflexChip,
    BerserkerRig,
    DuelistHarness,
    GunnersHarness,
    SniperScopeRig,
    ArcanistsCirclet,
    OverloadMatrix,
    WardensUplink,
    HivemindProcessor,
    PathfinderBoots,
    InfernoTreads,
    BulwarkAegis,
    PaladinsWard,
    SurveySuite,
    MinersHelmetRig,
    ExcavationSuite,
    ArchitectsFrame,
    SanguineReactor,
    StormCapacitor,
    GhostCloak,
    RetributionMesh,
    DiversKit,
    EnvironmentalSuit,
    VitalCore,
    MobilityRig,
    HuntersEye,
    FortuneMatrix,
    OmniEmblem,
    MasterTackleKit,
    JuggernautCore,
    NovaHeart,
    CryoHeart,
    EmberHeart,
    RadBattery,
    CoralPendant,
    SporeSac,
    HivecombAmulet,
    PrismBrooch,
    VoidEye,
    AstralCompass,
    WarlordsMedal,
    PiratesSpyglass,
    OverseersMonocle,
    MooneaterFang,
    EclipseSigil,
    LeviathanScale,
    ProtocolShard,
    MothershipChip,
    EmpressTiara,
    FerriteWingpack,
    CobaltWingpack,
    GlacialWingpack,
    PyrociteWingpack,
    UraniumWingpack,
    MeteoriteWingpack,
    ViridiumWingpack,
    LumeniteWingpack,
    VoidsteelWingpack,
    AstralWingpack,
    MothsilkWings,
    ScrapWings,
    LeafWings,
    BatsailWings,
    FrostfeatherWings,
    EmberWings,
    SporeWings,
    CoralFinWings,
    SkyrayWings,
    HiveWings,
    PrismWings,
    SolarWings,
    StormWings,
    VoidWings,
    EclipseWings,
    DreadnoughtWings,
    ProtocolWings,
    MothershipWings,
    NovaWings,
    CloseQuartersTonic,
    BallisticsTonic,
    TechTonic,
    CommandTonic,
    PrecisionDraught,
    FortifyStim,
    VitalitySerum,
    RejuvenationGel,
    SprintStim,
    MinersDraught,
    GlowDraught,
    AnglersBrew,
    SurfaceTensionSerum,
    ThornskinSerum,
    EnduranceStim,
    AmmoReserveTonic,
    WrathDraught,
    DroneLinkStim,
    SentryLinkStim,
    OverchargeCell,
    BuildersBrew,
    MagnetismTonic,
    FireproofSalve,
    DangerSenseDrops,
    LifeforceSerum,
    InertiaStim,
    EvasionSerum,
    NightSightDrops,
    GillDraught,
    DeepScanVapor,
    GreaterCloseQuartersTonic,
    GreaterBallisticsTonic,
    GreaterTechTonic,
    GreaterCommandTonic,
    HeavyFortifyStim,
    MarathonStim,
    HuntersFocus,
    FeatherfallMist,
    GuardSerum,
    GrilledSkitterLeg,
    SporeBread,
    RootStew,
    ProteinRation,
    GlowshroomSkewer,
    CactusJerky,
    FrostberryPie,
    EmberChili,
    CoralChowder,
    NectarDumplings,
    HiveHoneycake,
    FrostCodFillet,
    AstroBurger,
    VoidNoodles,
    PrismSorbet,
    KoiFlambe,
    StarfruitTart,
    ColonyFeast,
    // ---- Neo-Terra theme pass (append-only) ----
    /// <summary>A bucket of fuel scooped from the fuel sea.</summary>
    BucketFuel,
    // ---- Hardmode progression (definitions in Systems/Content/HardmodeContent.cs; explicit band from 7000) ----
    /// <summary>The Fission Tyrant's hammer-drill: the only class of tool that can smash blight altars.</summary>
    CoreBreaker = 7000,
    /// <summary>Summons the Fission Tyrant inside the Irradiated Core.</summary>
    CoreIgniter = 7001,
    /// <summary>Essence of the prism biome (underground prism creatures after the Fracture).</summary>
    PrismEssence = 7002,
    /// <summary>Essence of the blight (underground blight creatures after the Fracture).</summary>
    BlightEssence = 7003,
    /// <summary>Essence of flight (sky creatures after the Fracture).</summary>
    FlightEssence = 7004,
    /// <summary>Summons the Void Cantor (only once the Vault Colossus has fallen).</summary>
    VoidHymnal = 7005,
    // ---- Travel and mounts (Systems/GameSimulation.Travel.cs; explicit band 8000–8199) ----
    GlideWalk = 8000,
    ExpressWalk = 8001,
    // 8002–8003 retired (the rail bumper and ore cart before rails became glide-walks).
    ColonyPylon = 8004,
    FrostPylon = 8005,
    ScrapPylon = 8006,
    CanopyPylon = 8007,
    CavernPylon = 8008,
    CoastPylon = 8009,
    StriderWhistle = 8010,
    RotorSkiffKey = 8011,
    TideRayCharm = 8012,
    BurrowerKey = 8013,
    SpringHopperKey = 8014,
    // ---- Player & combat systems (Systems/GameSimulation.PlayerSystems.cs; band 8200–8399; boss caches, trophies,
    // relics and core-chips use dynamic ids 8240+ from Systems/Content/PlayerSystemsContent.cs) ----
    Sifter = 8200, DyeVat = 8201, FortuneTonic = 8202, CloverCircuit = 8203, BeaconHarness = 8204, ShroudCloak = 8205,
    CrimsonDye = 8210, AzureDye = 8211, VerdantDye = 8212, AmberDye = 8213, VioletDye = 8214, TealDye = 8215, ShadowDye = 8216,
    PrismaticDye = 8217, DyeSolvent = 8218,
    // ---- World & leisure (Systems/GameSimulation.Leisure.cs and friends; explicit band 8400–8599) ----
    HollowLantern = 8400, RimeBeacon = 8401, CryoOrders = 8402, HollowEmber = 8403, RimeShard = 8404,
    DeltaKite = 8410, BoxKite = 8411, RibbonKite = 8412,
    PurrbotPermit = 8420, RoverPermit = 8421, HoplingPermit = 8422,
    GolfBall = 8430, GolfPutter = 8431, GolfIron = 8432, GolfDriver = 8433, GolfTee = 8434, GolfCup = 8435,
    PaintSignalRed = 8440, PaintHazardOrange = 8441, PaintSolarYellow = 8442, PaintMossLime = 8443, PaintCanopyGreen = 8444,
    PaintCoolantTeal = 8445, PaintIonCyan = 8446, PaintSkyBlue = 8447, PaintDeepBlue = 8448, PaintVoidViolet = 8449,
    PaintNeonPink = 8450, PaintShadowBlack = 8451, Paintbrush = 8452, PaintRoller = 8453, PaintScraper = 8454, GlowCoating = 8455,
    AudioDeck = 8460, DeckGrassland = 8461, DeckUnderground = 8462, DeckFrozen = 8463, DeckJunk = 8464, DeckMolten = 8465,
    DeckRadiated = 8466, DeckOvergrowth = 8467, DeckFungal = 8468, DeckCrystal = 8469, DeckCanopy = 8470, DeckCoast = 8471, DeckBoss = 8472,
    // ---- Content fill (append-only; explicit band 8600-8999; definitions in Systems/Content/ContentFillItems.cs) ----
    // Catch items: same names as their critters (the bug net parses the creature name).
    RegolithVole = 8600, SunbeamMoth = 8601, ThistleFinch = 8602, MeadowCricket = 8603, PuffballTortoise = 8604, DuskGlider = 8605,
    LampWorm = 8606, MoonhopToad = 8607, StarMoth = 8608, PondMinnow = 8609, BubbleTadpole = 8610, BurrowMole = 8611,
    RootGrub = 8612, GravelSnail = 8613, GemTick = 8614, EchoFlitter = 8615, BlindCavefish = 8616, CaveSalamander = 8617,
    GlowSlug = 8618, FrostVole = 8619, IcicleBeetle = 8620, SnowOwlet = 8621, RimeFish = 8622, RustScarab = 8623,
    WireMouse = 8624, SprocketFly = 8625, TideFlea = 8626, HermitBot = 8627, FuelSeahorse = 8628, ReefStar = 8629,
    CanopyGecko = 8630, LeafbackBeetle = 8631, OrchidMoth = 8632, MireSkipper = 8633, PuffcapHopper = 8634, MyceliumWorm = 8635,
    SporeFirefly = 8636, ShardMoth = 8637, LumenFish = 8638, MutantRoach = 8639, GlowToad = 8640, Rootworm = 8641,
    IsotopeBeetle = 8642, FissionMoth = 8643, GildedVole = 8644, GildedCricket = 8645, GildedFinch = 8646, GildedMinnow = 8647,
    GildedGecko = 8648, GildedScarab = 8649, GildedHopBunny = 8650, GildedSquirrelBot = 8651, GildedFrog = 8652,
    // Biome timbers (felled biome trees) and harvestable surface plants.
    Vinewood = 8700, Palmwood = 8701, Stalkwood = 8702, Prismwood = 8703, Blightwood = 8704, Gemwood = 8705,
    PlantFiber = 8720, FrostLichen = 8721, ScrapThistle = 8722, SeaFennel = 8723, CanopyPod = 8724, GlowcapCluster = 8725,
    PrismBloom = 8726, BlightBulb = 8727,
    // ---- Progression chain (explicit band 9000-9099; docs/PROGRESSION_DIFF.md G4) ----
    FissionSlag = 9000, FalloutHiveCell = 9001,
    // ---- Town & modes (explicit band 9300-9399; Systems/Content/TownContent.cs; docs/PROGRESSION_DIFF.md G13) ----
    CompanionPermit = 9300,
    // ---- Character growth (explicit band 9100-9299; Systems/Content/GrowthItems.cs; docs/PROGRESSION_DIFF.md G3, G7-G10) ----
    LifebloomPod = 9100, AuxiliarySocket = 9101, NaniteMarrow = 9102, CoolantPearl = 9103, FortuneLattice = 9104,
    BaitGland = 9105, ServoGrease = 9106, FabricatorManual = 9107, LumenAccord = 9108, SalvagedThrusterFins = 9109,
    ScrapTether = 9110, TwinLash = 9111, PhaseTether = 9112,
    // ---- Creature coverage (explicit band 9400-9499; Systems/CreatureCoverage.cs; docs/CREATURE_COVERAGE.md) ----
    // Boss summons and event starters.
    FrostbittenLure = 9400, PrismGelLattice = 9401, RegentWick = 9402, SeraphFrostCore = 9403, CorrodedFlare = 9404, UmbralLens = 9405,
    // Boss and mini-boss loot: arsenal rows (Systems/Content/ArsenalItems.Coverage.cs), so they sit in the arsenal band (6000-6999).
    RegentScythe = 6900, EmberLanternStaff = 6901, SeraphIcicleBow = 6902, FrostChoirStaff = 6903, BehemothMaul = 6904,
    HoarfrostFang = 6905, SovereignGyro = 6906, GelburstLauncher = 6907, PaladinWarhammer = 6908, ColossusFrostlance = 6909,
    GritStormRod = 6910, NautilusHarpoonGun = 6911, HexerSceptre = 6912, LancerJoust = 6913, RootwalkerBranch = 6914,
    PineNeedleRepeater = 6915, CryoTankCannon = 6916,
    // Kill banners use dynamic ids: PlayerItems.BannerBase + (int)EnemyKind.
    // ---- Combat core: sustain (explicit band 9500-9599; definitions in Systems/Content/ContentItems.cs, docs/roadmap/0.1.9.md C1-C3) ----
    // The healing ladder above the Med stim (100 / 150 / 200), the restoration stim (90, shorter sickness) and the charge
    // ladder above the small charge cell (100 / 200 / 300).
    TraumaStim = 9500, NaniteStim = 9501, PhoenixStim = 9502, RestorationStim = 9503,
    ChargeCellMedium = 9504, ChargeCellLarge = 9505, ChargeCellPeak = 9506,
    // Instant pickups dropped by kills: applied on touch, never enter the inventory.
    HeartPickup = 9507, ChargeOrb = 9508,
}

public enum ItemUseKind : byte
{
    Material,
    PlaceTile,
    Pickaxe,
    Melee,
    Ranged,
    Ammunition,
    Food,
    Equipment,
    Vanity,
    Consumable,
    // Content expansion (append-only).
    /// <summary>Charge-powered Tech weapon (emitters, beam rods, datapads, arc gloves).</summary>
    Tech,
    /// <summary>Command item: drone remote (minion) or turret kit (sentry).</summary>
    Command,
    /// <summary>Places a background wall (<see cref="ItemDefinition.PlacedWall"/>).</summary>
    PlaceWall,
    /// <summary>Wall hammer: removes background walls.</summary>
    Demolisher,
    /// <summary>Cutter (axe): fells organic tiles quickly.</summary>
    Cutter,
    /// <summary>Catches passive critters into items.</summary>
    BugNet,
    /// <summary>A caught critter; using it releases the creature.</summary>
    Critter,
    /// <summary>Utility tools (buckets, fishing rod, paint sprayer, wire tool).</summary>
    Utility
}

/// <summary>Combat class of a weapon (CONTENT_EXPANSION.md §2.1).</summary>
public enum DamageClass : byte
{
    None,
    /// <summary>Close Quarters.</summary>
    Melee,
    /// <summary>Ballistics.</summary>
    Ranged,
    /// <summary>Tech: draws Charge.</summary>
    Tech,
    /// <summary>Command: drones and sentries.</summary>
    Command
}

/// <summary>Rarity tier shown by the UI; follows the progression tier.</summary>
/// <summary>
/// How a weapon attacks. None keeps the older generic behaviour of its <see cref="ItemUseKind"/>.
/// Each style has its own attack logic (see GameSimulation.WeaponStyles).
/// </summary>
public enum WeaponStyle : byte
{
    None,
    /// <summary>Wide overhead arc in front of the user.</summary>
    Broadsword,
    /// <summary>Fast short stab toward the cursor.</summary>
    Shortsword,
    /// <summary>Long thrust that extends and retracts along the aim line.</summary>
    Spear,
    /// <summary>Held on a tether toward the cursor while the button is held; hits repeatedly.</summary>
    Yoyo,
    /// <summary>Thrown out and flies back to the thrower, hitting on both legs.</summary>
    Boomerang,
    /// <summary>Spun ball on a chain, launched toward the cursor and reeled back.</summary>
    Flail,
    /// <summary>Long lash that marks the target so drones and turrets deal bonus damage.</summary>
    Whip,
    /// <summary>Fires ammunition arrows; draws bowstring.</summary>
    Bow,
    /// <summary>Fires bullet ammunition.</summary>
    Gun,
    /// <summary>Fires a spread of bullets.</summary>
    Shotgun,
    /// <summary>Fires explosive rockets that break nothing but hurt in a radius.</summary>
    Launcher,
    /// <summary>Consumed when thrown: knives, stars, javelins, grenades.</summary>
    Thrown,
    /// <summary>Charge-powered bolt caster.</summary>
    Staff,
    /// <summary>Charge-powered tome: bursts of several bolts.</summary>
    Tome,
    /// <summary>Continuous beam while held, draining charge.</summary>
    Beam,
    /// <summary>Summons a drone that follows and fights (uses a drone slot).</summary>
    SummonStaff,
    /// <summary>Places a stationary turret (uses a sentry slot).</summary>
    SentryStaff
}

/// <summary>Ammunition families. A launcher uses any ammo item of its family (first one in the inventory).</summary>
public enum AmmoClass : byte
{
    None,
    Arrow,
    Bullet,
    Rocket,
    Dart,
    /// <summary>Gel-like fuel for flamethrowers and sprayers.</summary>
    Fuel,
    /// <summary>Energy cells (the original ranged ammo).</summary>
    Cell
}

public enum ItemRarity : byte
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
    Mythic,
    Astral
}

public sealed record ItemDefinition(
    ItemId Id,
    string Name,
    ItemUseKind UseKind,
    int MaxStack)
{
    public bool IsMaterial { get; init; }
    public int Damage { get; init; }
    public float Knockback { get; init; }
    public int UseCooldownTicks { get; init; }
    public int ToolPower { get; init; }
    public TileId PlacedTile { get; init; } = TileId.Air;
    public ItemId? AmmoType { get; init; }
    public float ProjectileSpeed { get; init; }
    public float FoodRestore { get; init; }
    public EquipmentSlot? EquipmentSlot { get; init; }
    public bool IsVanity { get; init; }
    public int Defense { get; init; }
    public int SetBonusDefense { get; init; }
    public float MaximumLifeBonus { get; init; }
    public float MaximumManaBonus { get; init; }
    public float LifeRegenerationPerHour { get; init; }
    public float ManaRegenerationPerHour { get; init; }
    public float DamageMultiplier { get; init; } = 1f;
    public float KnockbackMultiplier { get; init; } = 1f;
    public float MoveSpeedMultiplier { get; init; } = 1f;
    public string? ArmorSetId { get; init; }
    public IReadOnlySet<string> ImmunityTags { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public StatusId? GrantsStatus { get; init; }
    public float StatusDurationHours { get; init; }
    public int ShopPrice { get; init; }
    /// <summary>Ranged shots that convert blight back to natural ground in this radius on impact.</summary>
    public float PurifyRadius { get; init; }

    // ---- Content expansion: classes, charge, drones, tiers, value ----
    /// <summary>Explicit combat class; <see cref="EffectiveClass"/> derives one for legacy weapons.</summary>
    public DamageClass Class { get; init; }
    /// <summary>Progression tier 1–7 (0 = untiered legacy/basic content).</summary>
    public int Tier { get; init; }
    public ItemRarity Rarity { get; init; }
    /// <summary>Base value in credits (shops sell at value × price multiplier, buy back at a fraction).</summary>
    public int Value { get; init; }
    /// <summary>Critical-hit chance in percent.</summary>
    public int CritChance { get; init; }
    /// <summary>Charge drawn per use (Tech weapons).</summary>
    public int ChargeCost { get; init; }
    /// <summary>Melee reach override in tiles (spears, tethers, whips); 0 uses the default reach.</summary>
    public float Reach { get; init; }
    /// <summary>Melee weapons thrown as a projectile (discs, boomerangs, yo-yo drones).</summary>
    public bool Thrown { get; init; }
    /// <summary>Projectiles per shot (scatterguns, swarm datapads).</summary>
    public int ProjectileCount { get; init; } = 1;
    /// <summary>Spread between multiple projectiles in radians.</summary>
    public float ProjectileSpread { get; init; } = 0.12f;
    public float ProjectileLifetime { get; init; } = 3f;
    /// <summary>Enemies a projectile passes through before it stops.</summary>
    public int Pierce { get; init; }
    /// <summary>Extra damage an ammunition item adds to the gun firing it.</summary>
    public int AmmoDamage { get; init; }
    /// <summary>Command items: the drone or turret this item deploys.</summary>
    public MinionKind? Summons { get; init; }
    /// <summary>Signal whips: on hit, drones focus the target and deal bonus damage.</summary>
    public bool SignalWhip { get; init; }
    /// <summary>How the weapon attacks (melee shape, projectile family); None for non-weapons and legacy weapons.</summary>
    public WeaponStyle Style { get; init; }
    /// <summary>Ammo family this weapon fires (any item whose <see cref="AmmoKind"/> matches); None uses <see cref="AmmoType"/> as before.</summary>
    public AmmoClass UsesAmmo { get; init; }
    /// <summary>For ammunition items: the family it belongs to.</summary>
    public AmmoClass AmmoKind { get; init; }
    /// <summary>Wings: seconds of powered flight per take-off (added to rocket fuel; the best equipped wing counts).</summary>
    public float WingFlightSeconds { get; init; }
    /// <summary>Ammo/weapon effect: steering rate (radians per second) toward the nearest hostile; 0 flies straight.</summary>
    public float AmmoHoming { get; init; }
    /// <summary>Ammo/weapon effect: blast radius in tiles on impact (launchers default to a tier-scaled radius).</summary>
    public float ExplosionRadius { get; init; }
    // Accessory/armor class bonuses (multiplicative damage, additive slots).
    public float MeleeDamageBonus { get; init; }
    public float RangedDamageBonus { get; init; }
    public float TechDamageBonus { get; init; }
    public float CommandDamageBonus { get; init; }
    public int CritBonus { get; init; }
    public int DroneSlotBonus { get; init; }
    public int SentrySlotBonus { get; init; }
    public float ChargeRegenBonus { get; init; }
    /// <summary>Placed background wall id (<see cref="ItemUseKind.PlaceWall"/>).</summary>
    public ushort PlacedWall { get; init; }
    /// <summary>Boss this consumable summons.</summary>
    public Rimtera.Entities.EnemyKind? SummonsBoss { get; init; }
    /// <summary>World event this consumable starts.</summary>
    public WorldEventKind? StartsEvent { get; init; }
    /// <summary>Critter released when a caught critter item is used.</summary>
    public Rimtera.Entities.EnemyKind? ReleasesCritter { get; init; }
    /// <summary>Hit points restored when consumed (stims).</summary>
    public float HealAmount { get; init; }
    /// <summary>Charge restored when consumed (charge cells).</summary>
    public float ChargeRestore { get; init; }
    /// <summary>Healing items: potion sickness in real seconds (0 = the standard <see cref="GameSimulation.PotionSicknessSeconds"/>).</summary>
    public float PotionSicknessSeconds { get; init; }
    /// <summary>Cooked food: hit points restored when eaten (a deliberate use, so decision 3 holds).</summary>
    public float FoodHeal { get; init; }
    /// <summary>Hearts and charge orbs: applied the moment they are touched; they never enter the inventory.</summary>
    public bool IsInstantPickup { get; init; }
    /// <summary>Instant pickups: hit points healed on touch.</summary>
    public float PickupHeal { get; init; }
    /// <summary>Instant pickups: charge restored on touch.</summary>
    public float PickupCharge { get; init; }
    /// <summary>Fishing rods: base fishing power; accessories and buffs add to it.</summary>
    public int FishingPower { get; init; }
    /// <summary>Bait strength (caught critters and special bait); 0 = not bait.</summary>
    public int BaitPower { get; init; }
    /// <summary>Opening this item rolls the named crate loot table.</summary>
    public bool IsCrate { get; init; }
    /// <summary>Quest fish wanted by the Tide-Warden.</summary>
    public bool IsQuestFish { get; init; }
    /// <summary>Weapons that may roll a calibration when crafted.</summary>
    public bool Calibratable => EffectiveClass != DamageClass.None &&
        UseKind is ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Tech or ItemUseKind.Command;

    /// <summary>The class used for damage scaling: explicit, else derived from the use kind.</summary>
    public DamageClass EffectiveClass => Class != DamageClass.None ? Class : UseKind switch
    {
        ItemUseKind.Melee => DamageClass.Melee,
        ItemUseKind.Ranged => DamageClass.Ranged,
        ItemUseKind.Tech => DamageClass.Tech,
        ItemUseKind.Command => DamageClass.Command,
        _ => DamageClass.None
    };

    /// <summary>Credit value used by shops: explicit value, then the legacy shop price, then a tier estimate.</summary>
    public int CreditValue => Value > 0 ? Value : ShopPrice > 0 ? ShopPrice : Math.Max(1, Tier * Tier * 10);
}

/// <summary>Project-owned item definitions. Numeric values here are original game data.</summary>
public sealed class ItemCatalog
{
    public ItemCatalog()
    {
        ContentItems.RegisterAll(_items);
        BatchAItems.RegisterAll(_items);
        PlayerItems.RegisterAll(_items);
        TravelContent.RegisterItems(_items);
        ArsenalItems.RegisterAll(_items);
        HardmodeContent.RegisterItems(_items);
        ContentFillItems.RegisterAll(_items); // content fill: catch items, biome timbers, wild-plant materials
        PlayerSystemsContent.RegisterAll(_items);
        LeisureContent.RegisterItems(_items);
        TownContent.RegisterItems(_items); // town & modes: companion permit (Systems/Content/TownContent.cs)
        CreatureCoverage.RegisterItems(_items); // creature coverage: boss summons and event starters (Systems/CreatureCoverage.cs)
        WeaponStyleAssignments.Apply(_items);
        DefenseBalance.Apply(_items);
        ToolCombat.Apply(_items); // tools are melee weapons too: tier damage, knockback and the melee class
        ContentItems.ApplySustain(_items); // cooked food heals; the potion-sickness accessory tag (0.1.9 C1)
        ContentItems.ApplyStackSizes(_items); // one stack-size pass: 9999 for every stackable (0.1.9 C5)
    }

    private readonly Dictionary<ItemId, ItemDefinition> _items = new()
    {
        [ItemId.SoilBlock] = new(ItemId.SoilBlock, "Regolith", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.Soil, IsMaterial = true },
        [ItemId.RockBlock] = new(ItemId.RockBlock, "Basalt", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.Rock, IsMaterial = true },
        [ItemId.SandBlock] = new(ItemId.SandBlock, "Silica sand", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.Sand, IsMaterial = true },
        [ItemId.WoodBlock] = new(ItemId.WoodBlock, "Hull panel", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.Wood, IsMaterial = true },
        [ItemId.CopperOre] = new(ItemId.CopperOre, "Ferrite ore", ItemUseKind.Material, 999) { IsMaterial = true },
        [ItemId.WoodenPickaxe] = new(ItemId.WoodenPickaxe, "Survey drill", ItemUseKind.Pickaxe, 1)
            { ToolPower = 20, UseCooldownTicks = 20 }, // weaker and slower than the copper-tier ferrite drill
        // Starting drill: Terraria's copper pickaxe (pick 35, useTime 15 → GearCurve tier 1).
        [ItemId.CopperPickaxe] = new(ItemId.CopperPickaxe, "Ferrite drill", ItemUseKind.Pickaxe, 1)
            { ToolPower = GearCurve.DrillPower(1), UseCooldownTicks = GearCurve.DrillCooldown(1) },
        [ItemId.WoodenSword] = new(ItemId.WoodenSword, "Shock baton", ItemUseKind.Melee, 1)
            { Damage = 12, Knockback = 2.2f, UseCooldownTicks = 14 },
        [ItemId.CopperBow] = new(ItemId.CopperBow, "Laser rifle", ItemUseKind.Ranged, 1)
            { Damage = 9, Knockback = 1.4f, UseCooldownTicks = 20, AmmoType = ItemId.Arrow, ProjectileSpeed = 14f },
        [ItemId.Arrow] = new(ItemId.Arrow, "Energy cell", ItemUseKind.Ammunition, 999),
        [ItemId.Berry] = new(ItemId.Berry, "Nano-banana", ItemUseKind.Food, 99) { FoodRestore = 0.22f },
        [ItemId.WorkbenchBlock] = new(ItemId.WorkbenchBlock, "Nano-Fabricator", ItemUseKind.PlaceTile, 99) { PlacedTile = TileId.Workbench },
        [ItemId.FurnaceBlock] = new(ItemId.FurnaceBlock, "Ore Refinery", ItemUseKind.PlaceTile, 99) { PlacedTile = TileId.Furnace },
        [ItemId.AnvilBlock] = new(ItemId.AnvilBlock, "Power Forge", ItemUseKind.PlaceTile, 99) { PlacedTile = TileId.Anvil },
        [ItemId.CopperBar] = new(ItemId.CopperBar, "Metal alloy", ItemUseKind.Material, 999) { IsMaterial = true },
        [ItemId.SnowBlock] = new(ItemId.SnowBlock, "Ice block", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.Snow, IsMaterial = true },
        [ItemId.DesertGlassBlock] = new(ItemId.DesertGlassBlock, "Dune block", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.DesertSand, IsMaterial = true },
        [ItemId.JungleRootBlock] = new(ItemId.JungleRootBlock, "Vine mat", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.JungleGrass, IsMaterial = true },
        [ItemId.FungalBlock] = new(ItemId.FungalBlock, "Spore mat", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.FungalSoil, IsMaterial = true },
        [ItemId.EmberStoneBlock] = new(ItemId.EmberStoneBlock, "Irradiated core rock", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.IrradiatedRock, IsMaterial = true },
        [ItemId.GloamRockBlock] = new(ItemId.GloamRockBlock, "Void rock", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.CorruptRock, IsMaterial = true },
        [ItemId.RiftSoilBlock] = new(ItemId.RiftSoilBlock, "Rift soil", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.CrimsonSoil, IsMaterial = true },
        [ItemId.PrismGrassBlock] = new(ItemId.PrismGrassBlock, "Prism moss", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.HallowGrass, IsMaterial = true },
        [ItemId.AncientBrickBlock] = new(ItemId.AncientBrickBlock, "Derelict plating", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.DungeonBrick, IsMaterial = true },
        [ItemId.Trailcap] = new(ItemId.Trailcap, "Void-proof helmet", ItemUseKind.Equipment, 1)
            { EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Head, Defense = 1, LifeRegenerationPerHour = 0.08f, ArmorSetId = "trail", SetBonusDefense = 2, ShopPrice = 28 },
        [ItemId.Trailcoat] = new(ItemId.Trailcoat, "Heavy astro-suit", ItemUseKind.Equipment, 1)
            { EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Body, Defense = 2, MaximumLifeBonus = 8f, ArmorSetId = "trail", SetBonusDefense = 2, ShopPrice = 42 },
        [ItemId.Trailboots] = new(ItemId.Trailboots, "Scout boots", ItemUseKind.Equipment, 1)
            { EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Legs, Defense = 1, MoveSpeedMultiplier = 1.08f, ArmorSetId = "trail", SetBonusDefense = 2, ShopPrice = 34 },
        [ItemId.HearthCharm] = new(ItemId.HearthCharm, "Cyber-swarm gloves", ItemUseKind.Equipment, 1)
            { EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Accessory, Defense = 1, MaximumManaBonus = 8f,
              ImmunityTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cold" }, ShopPrice = 55 },
        [ItemId.FestivalMask] = new(ItemId.FestivalMask, "Nano-banana uniform", ItemUseKind.Vanity, 1)
            { EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Head, IsVanity = true, ShopPrice = 20 },
        [ItemId.RestorativeTonic] = new(ItemId.RestorativeTonic, "Luminous spore tonic", ItemUseKind.Consumable, 30)
            { GrantsStatus = StatusId.Regeneration, StatusDurationHours = 5f, ShopPrice = 12 },
        [ItemId.ManaTonic] = new(ItemId.ManaTonic, "Crystal tonic", ItemUseKind.Consumable, 30)
            { GrantsStatus = StatusId.ManaFocus, StatusDurationHours = 5f, ShopPrice = 14 },
        [ItemId.CopperSword] = new(ItemId.CopperSword, "Energy cutlass", ItemUseKind.Melee, 1)
            { Damage = 17, Knockback = 2.6f, UseCooldownTicks = 12 },
        [ItemId.CobaltOre] = new(ItemId.CobaltOre, "Cobalt ore", ItemUseKind.Material, 999) { IsMaterial = true },
        [ItemId.ViridiumOre] = new(ItemId.ViridiumOre, "Viridium ore", ItemUseKind.Material, 999) { IsMaterial = true },
        [ItemId.EnergyCrystal] = new(ItemId.EnergyCrystal, "Energy crystal", ItemUseKind.Material, 999) { IsMaterial = true },
        // Guns all draw Energy cells; they trade rate of fire against punch and projectile speed.
        [ItemId.PlasmaPistol] = new(ItemId.PlasmaPistol, "Plasma pistol", ItemUseKind.Ranged, 1)
            { Damage = 7, Knockback = 0.8f, UseCooldownTicks = 9, AmmoType = ItemId.Arrow, ProjectileSpeed = 16f },
        [ItemId.SniperRifle] = new(ItemId.SniperRifle, "Sniper rifle", ItemUseKind.Ranged, 1)
            { Damage = 34, Knockback = 2.4f, UseCooldownTicks = 48, AmmoType = ItemId.Arrow, ProjectileSpeed = 30f },
        [ItemId.GrenadeLauncher] = new(ItemId.GrenadeLauncher, "Grenade launcher", ItemUseKind.Ranged, 1)
            { Damage = 42, Knockback = 6.5f, UseCooldownTicks = 40, AmmoType = ItemId.Arrow, ProjectileSpeed = 10f },
        [ItemId.RocketLauncher] = new(ItemId.RocketLauncher, "Rocket launcher", ItemUseKind.Ranged, 1)
            { Damage = 60, Knockback = 8f, UseCooldownTicks = 54, AmmoType = ItemId.Arrow, ProjectileSpeed = 13f },
        [ItemId.MiniTurret] = new(ItemId.MiniTurret, "Mini-turret", ItemUseKind.PlaceTile, 99) { PlacedTile = TileId.MiniTurret },
        [ItemId.HydroponicsTray] = new(ItemId.HydroponicsTray, "Hydroponics tray", ItemUseKind.PlaceTile, 99) { PlacedTile = TileId.HydroponicsTray },
        [ItemId.SolarPanel] = new(ItemId.SolarPanel, "Solar panel", ItemUseKind.PlaceTile, 99) { PlacedTile = TileId.SolarPanel },
        [ItemId.OxygenUnit] = new(ItemId.OxygenUnit, "Oxygen unit", ItemUseKind.PlaceTile, 99) { PlacedTile = TileId.OxygenUnit },
        // Biome ores and the shards their creatures and veins shed.
        [ItemId.GlacialOre] = new(ItemId.GlacialOre, "Glacial ore", ItemUseKind.Material, 999) { IsMaterial = true },
        [ItemId.UraniumOre] = new(ItemId.UraniumOre, "Uranium ore", ItemUseKind.Material, 999) { IsMaterial = true },
        [ItemId.PyrociteOre] = new(ItemId.PyrociteOre, "Pyrocite ore", ItemUseKind.Material, 999) { IsMaterial = true },
        [ItemId.ScrapIronOre] = new(ItemId.ScrapIronOre, "Scrap-iron ore", ItemUseKind.Material, 999) { IsMaterial = true },
        [ItemId.FrostCrystal] = new(ItemId.FrostCrystal, "Frost crystal", ItemUseKind.Material, 999) { IsMaterial = true },
        [ItemId.CausticShard] = new(ItemId.CausticShard, "Caustic shard", ItemUseKind.Material, 999) { IsMaterial = true },
        [ItemId.ObsidianShard] = new(ItemId.ObsidianShard, "Vitrified shard", ItemUseKind.Material, 999) { IsMaterial = true },
        [ItemId.OxidizedShard] = new(ItemId.OxidizedShard, "Oxidized shard", ItemUseKind.Material, 999) { IsMaterial = true },
        // Environmental gear: each piece widens the wearer's safe temperature band or blocks fallout.
        [ItemId.InsulatedSuit] = new(ItemId.InsulatedSuit, "Insulated suit", ItemUseKind.Equipment, 1)
            { EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Body, Defense = 3,
              ImmunityTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cold" }, ShopPrice = 60 },
        [ItemId.HeatWard] = new(ItemId.HeatWard, "Vitrified heat ward", ItemUseKind.Equipment, 1)
            { EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Accessory, Defense = 2,
              ImmunityTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "heat" }, ShopPrice = 64 },
        [ItemId.RadFilterMask] = new(ItemId.RadFilterMask, "Rad-filter mask", ItemUseKind.Equipment, 1)
            { EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Head, Defense = 1,
              ImmunityTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "poison", "toxic" }, ShopPrice = 52 },
        // Purification: the sprayer fires gel that scrubs blight from the ground it splashes.
        [ItemId.PurifierSprayer] = new(ItemId.PurifierSprayer, "Purifier sprayer", ItemUseKind.Ranged, 1)
            { Damage = 3, Knockback = 0.4f, UseCooldownTicks = 12, AmmoType = ItemId.PurifierGel, ProjectileSpeed = 11f, PurifyRadius = 3f, ShopPrice = 70 },
        [ItemId.PurifierGel] = new(ItemId.PurifierGel, "Purifier gel", ItemUseKind.Ammunition, 999) { ShopPrice = 1 },
        [ItemId.PurifierBeacon] = new(ItemId.PurifierBeacon, "Purifier beacon", ItemUseKind.PlaceTile, 99)
            { PlacedTile = TileId.PurifierBeacon, ShopPrice = 90 },
        // Using a life core permanently raises maximum hit points (see PlayerCharacter.LifeCoresUsed).
        [ItemId.LifeCore] = new(ItemId.LifeCore, "Life core", ItemUseKind.Consumable, 99) { ShopPrice = 120 },
        [ItemId.AshBlock] = new(ItemId.AshBlock, "Core crust", ItemUseKind.PlaceTile, 999) { PlacedTile = TileId.CoreCrust, IsMaterial = true }
    };

    public ItemDefinition this[ItemId id] => _items.TryGetValue(id, out var item)
        ? item
        : throw new KeyNotFoundException($"No item definition is registered for {id}.");

    public IEnumerable<ItemDefinition> All => _items.Values.OrderBy(item => item.Id);

    public bool Contains(ItemId id) => _items.ContainsKey(id);
}

/// <summary>
/// One inventory stack. <paramref name="Calibration"/> is the stack-level prefix rolled when a weapon
/// is crafted (0 = none); it defaults to none so older saves and two-argument callers are unchanged.
/// </summary>
public readonly record struct ItemStack(ItemId ItemId, int Count, Calibration Calibration = Calibration.None, bool Favorite = false)
{
    public bool IsEmpty => Count <= 0;
}

/// <summary>Fixed-slot inventory with deterministic stacking and explicit consumption.</summary>
public sealed class Inventory
{
    private readonly ItemStack[] _slots;
    private readonly IReadOnlyList<ItemStack> _readOnlySlots;
    private readonly ItemCatalog _catalog;

    /// <summary>
    /// A bag of <paramref name="slotCount"/> general slots, followed by <paramref name="coinSlots"/> slots that only take
    /// credits and <paramref name="ammoSlots"/> that only take ammunition (Terraria's slots 50–53 and 54–57).
    /// </summary>
    public Inventory(ItemCatalog catalog, int slotCount = 24, int coinSlots = 0, int ammoSlots = 0)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (slotCount < 1 || coinSlots < 0 || ammoSlots < 0)
            throw new ArgumentOutOfRangeException(nameof(slotCount));
        _catalog = catalog;
        BagSlots = slotCount;
        CoinSlots = coinSlots;
        AmmoSlots = ammoSlots;
        _slots = new ItemStack[slotCount + coinSlots + ammoSlots];
        _readOnlySlots = Array.AsReadOnly(_slots);
    }

    public ItemCatalog Catalog => _catalog;
    /// <summary>Every slot: the bag, then the coin slots, then the ammo slots.</summary>
    public int SlotCount => _slots.Length;
    /// <summary>The general slots (the hotbar and the bag grid); sort, deposit and quick stack work only here.</summary>
    public int BagSlots { get; }
    public int CoinSlots { get; }
    public int AmmoSlots { get; }
    public int CoinSlotStart => BagSlots;
    public int AmmoSlotStart => BagSlots + CoinSlots;

    public bool IsCoinSlot(int slot) => slot >= CoinSlotStart && slot < AmmoSlotStart;
    public bool IsAmmoSlot(int slot) => slot >= AmmoSlotStart && slot < _slots.Length;

    public static bool IsCoin(ItemId item) => item == ItemId.Credits;

    public bool IsAmmo(ItemId item) =>
        _catalog.Contains(item) && _catalog[item] is var definition &&
        (definition.UseKind == ItemUseKind.Ammunition || definition.AmmoKind != AmmoClass.None);

    /// <summary>True when <paramref name="item"/> may sit in <paramref name="slot"/>: coin slots take credits, ammo slots ammunition.</summary>
    public bool Accepts(int slot, ItemId item) =>
        IsCoinSlot(slot) ? IsCoin(item) : !IsAmmoSlot(slot) || IsAmmo(item);

    /// <summary>
    /// The order slots fill in when <paramref name="item"/> is picked up: credits go to the coin slots first and ammunition
    /// to the ammo slots first, as in Terraria; everything else goes only to the bag.
    /// </summary>
    private IEnumerable<int> FillOrder(ItemId item)
    {
        if (CoinSlots > 0 && IsCoin(item))
            for (var i = CoinSlotStart; i < AmmoSlotStart; i++)
                yield return i;
        else if (AmmoSlots > 0 && IsAmmo(item))
            for (var i = AmmoSlotStart; i < _slots.Length; i++)
                yield return i;
        for (var i = 0; i < BagSlots; i++)
            yield return i;
    }

    /// <summary>
    /// The order slots are spent in: the ammo slots first (Terraria fires from them before the bag), then the rest in order.
    /// </summary>
    public IEnumerable<int> UseOrder()
    {
        for (var i = AmmoSlotStart; i < _slots.Length; i++)
            yield return i;
        for (var i = 0; i < AmmoSlotStart; i++)
            yield return i;
    }
    public int SelectedSlot { get; private set; }

    /// <summary>
    /// The held item: the stack on the cursor while there is one (Terraria's mouse item, which <c>PickItemSelectionOverride</c>
    /// makes the selected item, slot 58), otherwise the selected slot.
    /// </summary>
    public ItemStack SelectedStack => Cursor.IsEmpty ? _slots[SelectedSlot] : Cursor;
    public ItemDefinition? SelectedItem => SelectedStack.IsEmpty ? null : _catalog[SelectedStack.ItemId];
    public IReadOnlyList<ItemStack> Slots => _readOnlySlots;

    /// <summary>
    /// The stack carried on the mouse cursor while the inventory is open (Terraria's <c>Main.mouseItem</c>). While it holds
    /// something it is the item in hand: using an item consumes from it first. The host returns it to the bag when the
    /// inventory closes (<see cref="ReturnCursor"/>).
    /// </summary>
    public ItemStack Cursor { get; private set; }

    /// <summary>True while a stack rides on the cursor.</summary>
    public bool HoldingCursor => !Cursor.IsEmpty;

    public void SetCursor(ItemStack stack)
    {
        if (!stack.IsEmpty && (!_catalog.Contains(stack.ItemId) || stack.Count > _catalog[stack.ItemId].MaxStack))
            throw new ArgumentOutOfRangeException(nameof(stack));
        Cursor = stack.IsEmpty ? default : stack;
    }

    /// <summary>
    /// Puts the cursor stack back into the bag, merging into matching stacks first and then the first empty slot, as
    /// Terraria's <c>GetOrDropItem(mouseItem, ReturnItemFromSlot)</c> does. Returns what did not fit (the caller drops it)
    /// and leaves the cursor empty.
    /// </summary>
    public ItemStack ReturnCursor()
    {
        var held = Cursor;
        Cursor = default;
        if (held.IsEmpty)
            return default;
        var remaining = AddInto(_slots, held);
        return remaining <= 0 ? default : held with { Count = remaining };
    }

    /// <summary>The slots as a save should record them: a stack still on the cursor is folded back in where it would return.</summary>
    public ItemStack[] SlotsWithCursorReturned()
    {
        var copy = _slots.ToArray();
        if (!Cursor.IsEmpty)
            AddInto(copy, Cursor);
        return copy;
    }

    /// <summary>Adds a stack (keeping its calibration and favourite flag) into matching stacks, then empty slots; returns the leftover count.</summary>
    private int AddInto(ItemStack[] slots, ItemStack stack)
    {
        var maxStack = _catalog[stack.ItemId].MaxStack;
        var remaining = stack.Count;
        var order = FillOrder(stack.ItemId).ToArray();
        foreach (var i in order)
        {
            if (remaining <= 0)
                break;
            var current = slots[i];
            if (current.IsEmpty || current.ItemId != stack.ItemId || current.Calibration != stack.Calibration || current.Count >= maxStack)
                continue;
            var moved = Math.Min(remaining, maxStack - current.Count);
            slots[i] = current with { Count = current.Count + moved };
            remaining -= moved;
        }
        foreach (var i in order)
        {
            if (remaining <= 0)
                break;
            if (!slots[i].IsEmpty)
                continue;
            var moved = Math.Min(remaining, maxStack);
            slots[i] = stack with { Count = moved };
            remaining -= moved;
        }
        return remaining;
    }

    public bool Select(int slot)
    {
        if ((uint)slot >= (uint)_slots.Length)
            return false;
        SelectedSlot = slot;
        return true;
    }

    /// <summary>Removes up to <paramref name="quantity"/> items from one slot.</summary>
    public ItemStack TakeFromSlot(int slot, int quantity = int.MaxValue)
    {
        ValidateSlot(slot);
        if (quantity <= 0)
            return default;

        var stack = _slots[slot];
        if (stack.IsEmpty)
            return default;

        var taken = Math.Min(stack.Count, quantity);
        _slots[slot] = stack with { Count = stack.Count - taken };
        return stack with { Count = taken };
    }

    /// <summary>Places as many held items as possible into a compatible slot.</summary>
    public ItemStack PlaceIntoSlot(int slot, ItemStack offered)
    {
        ValidateSlot(slot);
        if (offered.IsEmpty)
            return default;

        var maxStack = _catalog[offered.ItemId].MaxStack;
        var current = _slots[slot];
        if (!Accepts(slot, offered.ItemId) ||
            !current.IsEmpty && (current.ItemId != offered.ItemId || current.Calibration != offered.Calibration))
            return offered;

        var capacity = current.IsEmpty ? maxStack : Math.Max(0, maxStack - current.Count);
        var moved = Math.Min(offered.Count, capacity);
        if (moved <= 0)
            return offered;

        _slots[slot] = current.IsEmpty
            ? offered with { Count = moved }
            : current with { Count = current.Count + moved };
        return offered with { Count = offered.Count - moved };
    }

    /// <summary>Swaps one inventory slot with a UI cursor stack (refused when the slot does not take the cursor's item).</summary>
    public void SwapWithCursor(int slot, ref ItemStack cursorStack)
    {
        ValidateSlot(slot);
        if (!cursorStack.IsEmpty && !Accepts(slot, cursorStack.ItemId))
            return;
        (cursorStack, _slots[slot]) = (_slots[slot], cursorStack);
    }

    /// <summary>
    /// Moves a stack between inventories. A full destination stack can be swapped;
    /// compatible stacks are merged up to their catalog limit.
    /// </summary>
    public int TransferSlotTo(Inventory destination, int sourceSlot, int destinationSlot,
        int quantity = int.MaxValue, bool allowSwap = true)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateSlot(sourceSlot);
        destination.ValidateSlot(destinationSlot);
        if (ReferenceEquals(this, destination) && sourceSlot == destinationSlot)
            return 0;
        if (quantity <= 0)
            return 0;

        var source = _slots[sourceSlot];
        if (source.IsEmpty || !destination.Accepts(destinationSlot, source.ItemId))
            return 0;
        var target = destination._slots[destinationSlot];

        if (!target.IsEmpty && (target.ItemId != source.ItemId || target.Calibration != source.Calibration))
        {
            if (!allowSwap || quantity < source.Count || !Accepts(sourceSlot, target.ItemId))
                return 0;
            destination._slots[destinationSlot] = source;
            _slots[sourceSlot] = target;
            return source.Count;
        }

        var maxStack = destination._catalog[source.ItemId].MaxStack;
        var room = target.IsEmpty ? maxStack : Math.Max(0, maxStack - target.Count);
        var moved = Math.Min(Math.Min(source.Count, quantity), room);
        if (moved <= 0)
            return 0;

        destination._slots[destinationSlot] = target.IsEmpty
            ? source with { Count = moved }
            : target with { Count = target.Count + moved };
        _slots[sourceSlot] = source with { Count = source.Count - moved };
        return moved;
    }

    public bool RestoreState(IReadOnlyList<ItemStack> stacks, int selectedSlot)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        if (stacks.Count != _slots.Length || (uint)selectedSlot >= (uint)_slots.Length)
            return false;
        foreach (var stack in stacks)
        {
            if ((!Enum.IsDefined(stack.ItemId) && !_catalog.Contains(stack.ItemId)) || stack.Count < 0 || !Enum.IsDefined(stack.Calibration) ||
                (!stack.IsEmpty && stack.Count > _catalog[stack.ItemId].MaxStack))
                return false;
        }
        for (var i = 0; i < stacks.Count; i++)
            _slots[i] = stacks[i].IsEmpty ? default : stacks[i];
        SelectedSlot = selectedSlot;
        return true;
    }

    public int Count(ItemId itemId) => _slots
        .Where(stack => !stack.IsEmpty && stack.ItemId == itemId)
        .Sum(stack => stack.Count);

    /// <summary>Room for <paramref name="itemId"/>: free space on matching stacks (same calibration) plus empty slots.</summary>
    public int GetAddableCount(ItemId itemId, Calibration calibration = Calibration.None)
    {
        var maxStack = _catalog[itemId].MaxStack;
        var capacity = 0;
        foreach (var slot in FillOrder(itemId))
            if (_slots[slot] is var stack)
                capacity += stack.IsEmpty ? maxStack
                : stack.ItemId == itemId && stack.Calibration == calibration ? Math.Max(0, maxStack - stack.Count) : 0;
        return capacity;
    }

    public bool CanAdd(ItemId itemId, int quantity) => quantity >= 0 && GetAddableCount(itemId) >= quantity;

    public int FindSlot(ItemId itemId)
    {
        foreach (var i in UseOrder())
        {
            if (!_slots[i].IsEmpty && _slots[i].ItemId == itemId)
                return i;
        }
        return -1;
    }

    public int Add(ItemId itemId, int quantity) => Add(itemId, quantity, Calibration.None);

    /// <summary>Adds items carrying a calibration; calibrated stacks only merge with the same calibration.</summary>
    public int Add(ItemId itemId, int quantity, Calibration calibration)
    {
        if (quantity <= 0)
            return 0;
        return quantity - AddInto(_slots, new ItemStack(itemId, quantity, calibration));
    }

    public bool TryConsume(ItemId itemId, int quantity = 1)
    {
        var onCursor = !Cursor.IsEmpty && Cursor.ItemId == itemId ? Cursor.Count : 0;
        if (quantity <= 0 || Count(itemId) + onCursor < quantity)
            return false;

        var remaining = quantity;
        // The stack on the cursor is the one in hand, so a use spends it first (Terraria uses the mouse item itself).
        if (onCursor > 0)
        {
            var spent = Math.Min(remaining, onCursor);
            Cursor = onCursor - spent > 0 ? Cursor with { Count = onCursor - spent } : default;
            remaining -= spent;
        }
        foreach (var i in UseOrder())
        {
            if (remaining <= 0)
                break;
            var stack = _slots[i];
            if (stack.IsEmpty || stack.ItemId != itemId)
                continue;
            var consumed = Math.Min(remaining, stack.Count);
            _slots[i] = stack with { Count = stack.Count - consumed };
            remaining -= consumed;
        }

        return true;
    }

    /// <summary>Applies a recipe input/output exchange without partial inventory mutation.</summary>
    public bool TryApplyTransaction(
        IReadOnlyDictionary<ItemId, int> consumed,
        ItemId outputItemId,
        int outputQuantity,
        Calibration calibration = Calibration.None)
    {
        ArgumentNullException.ThrowIfNull(consumed);
        if (outputQuantity <= 0)
            return false;

        var outputDefinition = _catalog[outputItemId];
        var working = _slots.ToArray();
        foreach (var (itemId, quantity) in consumed)
        {
            _ = _catalog[itemId];
            if (quantity <= 0 || Count(working, itemId) < quantity)
                return false;
            Consume(working, itemId, quantity);
        }

        _ = outputDefinition;
        var remaining = AddInto(working, new ItemStack(outputItemId, outputQuantity, calibration));
        if (remaining > 0)
            return false;

        Array.Copy(working, _slots, working.Length);
        return true;
    }

    public bool TryConsumeSelected(int quantity = 1) =>
        !SelectedStack.IsEmpty && TryConsume(SelectedStack.ItemId, quantity);

    private static int Count(IEnumerable<ItemStack> stacks, ItemId itemId) =>
        stacks.Where(stack => !stack.IsEmpty && stack.ItemId == itemId).Sum(stack => stack.Count);

    private static void Consume(ItemStack[] stacks, ItemId itemId, int quantity)
    {
        var remaining = quantity;
        for (var i = 0; i < stacks.Length && remaining > 0; i++)
        {
            var stack = stacks[i];
            if (stack.IsEmpty || stack.ItemId != itemId)
                continue;
            var consumed = Math.Min(remaining, stack.Count);
            stacks[i] = stack with { Count = stack.Count - consumed };
            remaining -= consumed;
        }
    }

    // ---- Batch B: favorites, sort, loot/deposit and quick stack ----

    /// <summary>Hotbar slots (the first ten); sort and quick stack leave them alone.</summary>
    public const int HotbarSlots = 10;

    public bool IsFavorite(int slot) => (uint)slot < (uint)_slots.Length && !_slots[slot].IsEmpty && _slots[slot].Favorite;

    /// <summary>Flags or unflags a stack as a favorite (kept out of sort, quick stack, deposit and trash).</summary>
    public bool ToggleFavorite(int slot)
    {
        ValidateSlot(slot);
        if (_slots[slot].IsEmpty)
            return false;
        _slots[slot] = _slots[slot] with { Favorite = !_slots[slot].Favorite };
        return true;
    }

    /// <summary>
    /// Merges partial stacks and orders every non-favorite stack from <paramref name="firstSlot"/> on:
    /// weapons, tools, gear, consumables, food, ammunition, placeables, then materials; rarer first.
    /// </summary>
    public void Sort(int firstSlot = HotbarSlots)
    {
        firstSlot = Math.Clamp(firstSlot, 0, BagSlots);
        var movable = new List<int>();
        var loose = new List<ItemStack>();
        for (var i = firstSlot; i < BagSlots; i++)
        {
            if (_slots[i].IsEmpty)
            {
                movable.Add(i);
                _slots[i] = default;
                continue;
            }
            if (_slots[i].Favorite)
                continue;
            movable.Add(i);
            loose.Add(_slots[i]);
            _slots[i] = default;
        }
        // Merge identical stacks up to their limit.
        var merged = new List<ItemStack>();
        foreach (var group in loose.GroupBy(stack => (stack.ItemId, stack.Calibration)))
        {
            var max = _catalog[group.Key.ItemId].MaxStack;
            var total = group.Sum(stack => stack.Count);
            while (total > 0)
            {
                var take = Math.Min(max, total);
                merged.Add(new ItemStack(group.Key.ItemId, take, group.Key.Calibration));
                total -= take;
            }
        }
        var ordered = merged
            .OrderBy(stack => SortRank(_catalog[stack.ItemId]))
            .ThenByDescending(stack => _catalog[stack.ItemId].Rarity)
            .ThenBy(stack => stack.ItemId)
            .ThenBy(stack => stack.Calibration)
            .ThenByDescending(stack => stack.Count)
            .ToList();
        for (var i = 0; i < ordered.Count && i < movable.Count; i++)
            _slots[movable[i]] = ordered[i];
    }

    private static int SortRank(ItemDefinition item) => item.UseKind switch
    {
        ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Tech or ItemUseKind.Command => 0,
        ItemUseKind.Pickaxe or ItemUseKind.Cutter or ItemUseKind.Demolisher or ItemUseKind.BugNet or ItemUseKind.Utility => 1,
        ItemUseKind.Equipment or ItemUseKind.Vanity => 2,
        ItemUseKind.Consumable => 3,
        ItemUseKind.Food => 4,
        ItemUseKind.Ammunition => 5,
        ItemUseKind.PlaceTile or ItemUseKind.PlaceWall => 6,
        ItemUseKind.Critter => 7,
        _ => 8
    };

    /// <summary>
    /// Moves every bag stack from <paramref name="firstSlot"/> on (favorites, coin and ammo slots skipped) into <paramref name="destination"/>,
    /// merging into matching stacks first. With <paramref name="onlyMatching"/> only items the destination already
    /// holds move (quick stack). Returns the number of items moved.
    /// </summary>
    public int MoveAllTo(Inventory destination, int firstSlot = 0, bool onlyMatching = false)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (ReferenceEquals(destination, this))
            return 0;
        var moved = 0;
        for (var i = Math.Max(0, firstSlot); i < BagSlots; i++)
        {
            var stack = _slots[i];
            if (stack.IsEmpty || stack.Favorite)
                continue;
            if (onlyMatching && destination.Count(stack.ItemId) == 0)
                continue;
            var added = destination.Add(stack.ItemId, stack.Count, stack.Calibration);
            if (added <= 0)
                continue;
            moved += added;
            _slots[i] = stack.Count - added > 0 ? stack with { Count = stack.Count - added } : default;
        }
        return moved;
    }

    private void ValidateSlot(int slot)
    {
        if ((uint)slot >= (uint)_slots.Length)
            throw new ArgumentOutOfRangeException(nameof(slot));
    }
}

public static class TileDropTable
{
    public static ItemId? GetDrop(TileId tileId) => tileId switch
    {
        TileId.Soil or TileId.Grass => ItemId.SoilBlock,
        TileId.Rock or TileId.Bedrock => tileId == TileId.Bedrock ? null : ItemId.RockBlock,
        TileId.CopperOre => ItemId.CopperOre,
        TileId.Sand => ItemId.SandBlock,
        TileId.Wood => ItemId.WoodBlock,
        TileId.Workbench => ItemId.WorkbenchBlock,
        TileId.Furnace => ItemId.FurnaceBlock,
        TileId.Anvil => ItemId.AnvilBlock,
        TileId.CobaltOre => ItemId.CobaltOre,
        TileId.ViridiumOre => ItemId.ViridiumOre,
        TileId.EnergyCrystal => ItemId.EnergyCrystal,
        TileId.MiniTurret => ItemId.MiniTurret,
        TileId.HydroponicsTray => ItemId.HydroponicsTray,
        TileId.SolarPanel => ItemId.SolarPanel,
        TileId.OxygenUnit => ItemId.OxygenUnit,
        TileId.Snow => ItemId.SnowBlock,
        TileId.DesertSand => ItemId.DesertGlassBlock,
        TileId.JungleGrass => ItemId.JungleRootBlock,
        TileId.FungalSoil => ItemId.FungalBlock,
        TileId.EmberRock => ItemId.EmberStoneBlock,
        TileId.CorruptRock => ItemId.GloamRockBlock,
        TileId.CrimsonSoil => ItemId.RiftSoilBlock,
        TileId.HallowGrass => ItemId.PrismGrassBlock,
        TileId.DungeonBrick => ItemId.AncientBrickBlock,
        TileId.GlacialOre => ItemId.GlacialOre,
        TileId.UraniumOre => ItemId.UraniumOre,
        TileId.PyrociteOre => ItemId.PyrociteOre,
        TileId.ScrapIronOre => ItemId.ScrapIronOre,
        TileId.RadiatedSand or TileId.OvergrowthSand => ItemId.SandBlock,
        TileId.RadiatedIce or TileId.OvergrowthIce => ItemId.SnowBlock,
        TileId.OvergrowthSoil => ItemId.SoilBlock,
        TileId.OvergrowthRock => ItemId.RockBlock,
        TileId.LifeCore => ItemId.LifeCore,
        TileId.PurifierBeacon => ItemId.PurifierBeacon,
        TileId.Ash => ItemId.AshBlock,
        TileId.Obsidian => ItemId.ObsidianShard,
        TileId.ShipDoor => ItemId.WoodBlock,
        TileId.Tombstone => ItemId.MemorialMarker,
        TileId.CargoPodStation => ItemId.CargoPodStation,
        TileId.KillBanner => null,
        _ when TravelContent.TileDrop(tileId) is { } travelDrop => travelDrop,
        _ => LeisureContent.TileDrop(tileId) ?? ContentTiles.GetDrop(tileId)
    };

    /// <summary>Occasional shard shed by a biome ore vein when it breaks.</summary>
    public static ItemId? GetByproduct(TileId tileId) => tileId switch
    {
        TileId.GlacialOre => ItemId.FrostCrystal,
        TileId.UraniumOre => ItemId.CausticShard,
        TileId.PyrociteOre => ItemId.ObsidianShard,
        TileId.ScrapIronOre => ItemId.OxidizedShard,
        _ => null
    };
}
