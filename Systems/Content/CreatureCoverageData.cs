using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>A coverage creature's part: a regular unit, or an elite mini-boss that an event or roster brings once.</summary>
public enum CoverageRole : byte { Regular, MiniBoss }

/// <summary>
/// One creature of the coverage pass (docs/CREATURE_COVERAGE.md). <see cref="Slot"/> names where it lives: a biome variant
/// ("sky", "junk-under", "frozen-under", "grave", "dungeon-hard"), a hard biome extra ("mite", "hive"), or an event
/// ("event:Rain", "event:HollowMoon", …). <see cref="Hard"/> marks post-Fracture creatures; <see cref="Analog"/> names the
/// Terraria creature whose role it fills (behavioural evidence only; names, stats and art are original).
/// </summary>
public sealed record CoverageCreature(EnemyKind Kind, string Name, string Slot, bool Hard, float Day, float Night, EnemyAiFamily Family,
    float Width, float Height, float HitPoints, float Damage, bool Flying, int Credits, (ItemId Item, int Count)[] Drops, string Analog,
    CoverageRole Role = CoverageRole.Regular, int Segments = 0, bool Stationary = false, float Speed = 0f);

/// <summary>One coverage boss: tier, body, volley tuning, its summon item and the Terraria boss whose place it takes.</summary>
public sealed record CoverageBoss(EnemyKind Kind, string Name, BossTier Tier, float Width, float Height, float HitPoints, float Damage,
    bool Flying, float ProjectileSpeed, int Volley, int Credits, ItemId Summon, (ItemId Item, int Count)[] Drops, string Analog);

/// <summary>Original Rimtera data for the creature coverage pass. Tier bands follow docs/GEAR_DIFF_ITEMS.md §1.</summary>
public static class CreatureCoverageData
{
    private const EnemyAiFamily G = EnemyAiFamily.Default, H = EnemyAiFamily.Hopper, S = EnemyAiFamily.SkitteringHopper,
        W = EnemyAiFamily.Worm, C = EnemyAiFamily.Caster, D = EnemyAiFamily.DivingFlyer, M = EnemyAiFamily.Mimic;
    private const CoverageRole Mini = CoverageRole.MiniBoss;

    public static readonly CoverageCreature[] Creatures =
    [
        // ---- Sky layer (above any surface biome): Terraria's space layer ----
        new(EnemyKind.StratosRaptor, "Stratos raptor", "sky", false, 0.5f, 0.4f, D, 1f, 0.7f, 48f, 12f, true, 3, [(ItemId.SilkThread, 2)], "Harpy"),
        new(EnemyKind.IonSkate, "Ion skate", "sky", false, 0.4f, 0.3f, G, 1.2f, 0.5f, 36f, 10f, true, 2, [(ItemId.EnergyCrystal, 1)], "Harpy (second sky flier)"),
        new(EnemyKind.StratosWyvern, "Stratos wyvern", "sky", true, 0.3f, 0.4f, D, 2.6f, 1f, 560f, 40f, true, 30, [(ItemId.FlightEssence, 3), (ItemId.PowerCell, 1)], "Wyvern"),
        new(EnemyKind.CirrusSentinel, "Cirrus sentinel", "sky", true, 0.4f, 0.3f, G, 1.2f, 1.2f, 300f, 34f, true, 18, [(ItemId.PowerCell, 2)], "Harpy (hardmode sky)"),
        // ---- Underground junk wasteland: Terraria's underground desert ----
        new(EnemyKind.PitCharger, "Pit charger", "junk-under", false, 0.35f, 0.35f, G, 1.2f, 0.8f, 62f, 14f, false, 4, [(ItemId.ScrapIronOre, 2)], "Antlion Charger"),
        new(EnemyKind.PitSwarmer, "Pit swarmer", "junk-under", false, 0.35f, 0.35f, G, 0.8f, 0.6f, 36f, 11f, true, 2, [(ItemId.OxidizedShard, 1)], "Antlion Swarmer"),
        new(EnemyKind.TombBorer, "Tomb borer", "junk-under", false, 0.2f, 0.2f, W, 0.9f, 0.9f, 140f, 15f, false, 7, [(ItemId.ScrapIronOre, 4)], "Tomb Crawler", Segments: 8),
        new(EnemyKind.DustGhoul, "Dust ghoul", "junk-under", true, 0.35f, 0.35f, G, 1f, 1.8f, 270f, 33f, false, 16, [(ItemId.OxidizedShard, 4)], "Desert Ghoul"),
        new(EnemyKind.RustBasilisk, "Rust basilisk", "junk-under", true, 0.3f, 0.3f, G, 1.8f, 1.1f, 390f, 36f, false, 22, [(ItemId.ScrapIronOre, 8)], "Basilisk"),
        new(EnemyKind.GritSeer, "Grit seer", "junk-under", true, 0.25f, 0.25f, C, 0.9f, 1.7f, 240f, 31f, false, 16, [(ItemId.OxidizedShard, 3), (ItemId.VoidEssence, 1)], "Desert Spirit"),
        // ---- Underground frozen wasteland: Terraria's ice caverns ----
        new(EnemyKind.IcicleBat, "Icicle bat", "frozen-under", false, 0.5f, 0.5f, G, 0.8f, 0.6f, 32f, 10f, true, 2, [(ItemId.FrostCrystal, 1)], "Ice Bat"),
        new(EnemyKind.FrostboundDelver, "Frostbound delver", "frozen-under", false, 0.4f, 0.4f, G, 1f, 1.8f, 80f, 15f, false, 5, [(ItemId.GlacialOre, 2)], "Undead Viking"),
        new(EnemyKind.HoarfrostElemental, "Hoarfrost elemental", "frozen-under", true, 0.45f, 0.45f, C, 1f, 1.6f, 250f, 32f, false, 16, [(ItemId.FrostCrystal, 4)], "Ice Elemental"),
        new(EnemyKind.GlacierTortoise, "Glacier tortoise", "frozen-under", true, 0.4f, 0.4f, G, 1.6f, 1.1f, 430f, 36f, false, 22, [(ItemId.GlacialOre, 8)], "Ice Tortoise"),
        // ---- Grave field (six or more memorial markers near the surface): Terraria's graveyard ----
        new(EnemyKind.GraveWisp, "Grave wisp", "grave", false, 0.3f, 0.45f, G, 0.8f, 1f, 42f, 11f, true, 3, [(ItemId.ClothRoll, 1)], "Ghost"),
        new(EnemyKind.CarrionDrone, "Carrion drone", "grave", false, 0.4f, 0.3f, D, 0.8f, 0.5f, 30f, 9f, true, 2, [(ItemId.SilkThread, 1)], "Raven"),
        new(EnemyKind.TombHusk, "Tomb husk", "grave", false, 0.3f, 0.45f, G, 0.9f, 1.7f, 62f, 13f, false, 4, [(ItemId.ClothRoll, 2)], "Maggot Zombie"),
        new(EnemyKind.MourningShade, "Mourning shade", "grave", true, 0.4f, 0.5f, G, 1f, 1.6f, 270f, 33f, true, 16, [(ItemId.VoidEssence, 2)], "Wraith (graveyard)"),
        // ---- Late dungeon (after the Mutated Flora): Terraria's post-Plantera dungeon ----
        new(EnemyKind.CryptKnight, "Crypt knight", "dungeon-hard", true, 0.3f, 0.3f, G, 1.1f, 1.9f, 430f, 38f, false, 22, [(ItemId.AlabasterBlock, 6)], "Rusty/Blue/Hell Armored Bones"),
        new(EnemyKind.HexNecrotech, "Hex necrotech", "dungeon-hard", true, 0.2f, 0.2f, C, 0.9f, 1.7f, 310f, 36f, false, 20, [(ItemId.VoidEssence, 3)], "Necromancer / Diabolist / Ragged Caster"),
        new(EnemyKind.CryptMarksman, "Crypt marksman", "dungeon-hard", true, 0.2f, 0.2f, G, 0.9f, 1.7f, 290f, 38f, false, 20, [(ItemId.PowerCell, 2)], "Skeleton Sniper / Commando"),
        new(EnemyKind.CryptSpirit, "Crypt spirit", "dungeon-hard", true, 0.2f, 0.25f, G, 0.9f, 1.2f, 250f, 34f, true, 18, [(ItemId.VoidEssence, 2)], "Dungeon Spirit"),
        new(EnemyKind.CryptPaladin, "Crypt paladin", "dungeon-hard", true, 0.04f, 0.04f, G, 1.4f, 2.3f, 1250f, 44f, false, 120,
            [(ItemId.AlabasterBlock, 10), (ItemId.AncientPowerCell, 1)], "Paladin", Mini),
        // ---- Hardmode extras for the thin hard rosters ----
        new(EnemyKind.VenomRecluse, "Venom recluse", "mite", true, 0.25f, 0.25f, G, 1.3f, 0.8f, 330f, 36f, false, 20, [(ItemId.SilkThread, 8)], "Black Recluse"),
        new(EnemyKind.RoyalCombGuard, "Royal comb-guard", "hive", true, 0.25f, 0.25f, G, 1f, 0.9f, 290f, 34f, true, 18, [(ItemId.NectarBlock, 4)], "Moss Hornet"),

        // ---- Rainfall: Terraria's rain enemies ----
        new(EnemyKind.DrizzleGel, "Drizzle gel", "event:Rain", false, 0.4f, 0.3f, H, 0.9f, 0.65f, 30f, 9f, false, 1, [(ItemId.BioGel, 2)], "Umbrella Slime"),
        new(EnemyKind.RainSkipper, "Rain skipper", "event:Rain", false, 0.35f, 0.35f, G, 0.8f, 0.5f, 34f, 10f, true, 2, [(ItemId.TideFish, 1)], "Flying Fish"),
        new(EnemyKind.SlickerHusk, "Slicker husk", "event:Rain", false, 0.1f, 0.4f, G, 0.9f, 1.7f, 56f, 13f, false, 3, [(ItemId.ClothRoll, 1)], "Zombie (raincoat)"),
        new(EnemyKind.Thunderhead, "Thunderhead", "event:Rain", true, 0f, 0f, G, 1.4f, 1f, 310f, 34f, true, 18, [(ItemId.PowerCell, 2)], "Angry Nimbus"),
        // ---- Blizzard: snow-biome rain and the frost golem ----
        new(EnemyKind.WhiteoutWisp, "Whiteout wisp", "event:Blizzard", false, 0.4f, 0.4f, G, 0.8f, 0.8f, 42f, 11f, true, 3, [(ItemId.FrostCrystal, 1)], "Snow Flinx / blizzard spawns"),
        new(EnemyKind.DriftStalker, "Drift stalker", "event:Blizzard", false, 0.3f, 0.3f, G, 1.2f, 0.9f, 72f, 14f, false, 4, [(ItemId.FrostCrystal, 2)], "Wolf (blizzard)"),
        new(EnemyKind.BlizzardColossus, "Blizzard colossus", "event:Blizzard", true, 0f, 0f, G, 2f, 2.6f, 1150f, 42f, false, 100,
            [(ItemId.FrostCrystal, 12), (ItemId.GlacialOre, 10)], "Ice Golem", Mini, Speed: 0.7f),
        // ---- Dust storm (hardmode): Terraria's sandstorm ----
        new(EnemyKind.DuneRazorfin, "Dune razorfin", "event:DustStorm", true, 0f, 0f, G, 1.6f, 0.6f, 310f, 34f, true, 18, [(ItemId.OxidizedShard, 4)], "Sand Shark"),
        new(EnemyKind.GritElemental, "Grit elemental", "event:DustStorm", true, 0f, 0f, G, 1.6f, 2.2f, 1050f, 40f, true, 100,
            [(ItemId.ScrapIronOre, 12), (ItemId.PowerCell, 3)], "Sand Elemental", Mini),
        // ---- Swarm Rain (slime-rain analog) ----
        new(EnemyKind.ArmoredSwarmer, "Armored swarmer", "event:SwarmRain", false, 0f, 0f, H, 0.8f, 0.6f, 44f, 11f, false, 2, [(ItemId.BioGel, 2)], "Spiked Slime (slime rain)"),
        new(EnemyKind.PrismSwarmer, "Prism swarmer", "event:SwarmRain", true, 0f, 0f, H, 0.9f, 0.7f, 260f, 32f, false, 14, [(ItemId.PrismShard, 1), (ItemId.BioGel, 3)], "Rainbow Slime"),
        // ---- Windy day ----
        new(EnemyKind.ThistledownDrifter, "Thistledown drifter", "event:WindyDay", false, 0.6f, 0f, G, 0.8f, 0.8f, 36f, 10f, true, 2, [(ItemId.Berry, 2)], "Angry Dandelion"),
        new(EnemyKind.GustMite, "Gust mite", "event:WindyDay", false, 0.4f, 0f, S, 0.6f, 0.45f, 26f, 9f, false, 1, [(ItemId.SilkThread, 1)], "Windy-day critter swarm"),
        // ---- Rust Moon after the Fracture (blood moon hardmode) ----
        new(EnemyKind.RustJester, "Rust jester", "event:RustMoon", true, 0f, 0f, G, 1f, 1.8f, 310f, 35f, false, 18, [(ItemId.OxidizedShard, 4)], "Clown"),
        new(EnemyKind.CorrosionNautilus, "Corrosion nautilus", "event:RustMoon", true, 0f, 0f, G, 2.2f, 1.8f, 1450f, 44f, true, 140,
            [(ItemId.VoidEssence, 4), (ItemId.OxidizedShard, 10)], "Dreadnautilus", Mini),
        // ---- Dark Sun after the Mutated Flora (post-Plantera eclipse) ----
        new(EnemyKind.EclipseSlasher, "Eclipse slasher", "event:DarkSun", true, 0f, 0f, G, 1f, 1.8f, 350f, 40f, false, 22, [(ItemId.VoidEssence, 2)], "Psycho / Butcher"),
        new(EnemyKind.EclipseSphere, "Eclipse sphere", "event:DarkSun", true, 0f, 0f, G, 0.9f, 0.9f, 310f, 38f, true, 20, [(ItemId.PowerCell, 2)], "Deadly Sphere"),
        // ---- Scavenger Raid after the Fracture: its hex-caster ----
        new(EnemyKind.ScavengerHexer, "Scavenger hexer", "event:ScavengerRaid", true, 0f, 0f, C, 0.9f, 1.8f, 950f, 36f, false, 90,
            [(ItemId.ScrapCrate, 1), (ItemId.CircuitBoard, 4)], "Goblin Summoner", Mini),

        // ---- Hollow Moon (pumpkin-moon analog) ----
        new(EnemyKind.HollowEffigy, "Hollow effigy", "event:HollowMoon", true, 0f, 0f, G, 1f, 1.8f, 270f, 32f, false, 14, [(ItemId.HollowEmber, 1)], "Scarecrow"),
        new(EnemyKind.EmberSplinter, "Ember splinter", "event:HollowMoon", true, 0f, 0f, H, 0.8f, 0.8f, 210f, 30f, false, 12, [(ItemId.HollowEmber, 1), (ItemId.WoodBlock, 4)], "Splinterling"),
        new(EnemyKind.CinderHound, "Cinder hound", "event:HollowMoon", true, 0f, 0f, G, 1.3f, 0.9f, 290f, 34f, false, 16, [(ItemId.HollowEmber, 2)], "Hellhound"),
        new(EnemyKind.StaticPoltergeist, "Static poltergeist", "event:HollowMoon", true, 0f, 0f, G, 0.9f, 1.2f, 250f, 32f, true, 14, [(ItemId.HollowEmber, 1), (ItemId.PowerCell, 1)], "Poltergeist"),
        new(EnemyKind.HollowLancer, "Hollow lancer", "event:HollowMoon", true, 0f, 0f, G, 1.6f, 2.2f, 1350f, 42f, false, 110, [(ItemId.HollowEmber, 8)], "Headless Horseman", Mini, Speed: 1.3f),
        new(EnemyKind.EmberRootwalker, "Ember rootwalker", "event:HollowMoon", true, 0f, 0f, G, 2.2f, 3.2f, 1550f, 40f, false, 130,
            [(ItemId.HollowEmber, 10), (ItemId.WoodBlock, 30)], "Mourning Wood", Mini, Speed: 0.6f),
        // ---- Rime Moon (frost-moon analog) ----
        new(EnemyKind.FrostSapper, "Frost sapper", "event:RimeMoon", true, 0f, 0f, G, 0.9f, 1.6f, 250f, 32f, false, 14, [(ItemId.RimeShard, 1)], "Zombie Elf / Elf Archer"),
        new(EnemyKind.Shellcracker, "Shellcracker", "event:RimeMoon", true, 0f, 0f, G, 1.1f, 1.9f, 310f, 34f, false, 16, [(ItemId.RimeShard, 2)], "Nutcracker"),
        new(EnemyKind.FlurryFlake, "Flurry flake", "event:RimeMoon", true, 0f, 0f, G, 0.9f, 0.9f, 230f, 32f, true, 14, [(ItemId.RimeShard, 1)], "Flocko"),
        new(EnemyKind.CargoMimic, "Cargo mimic", "event:RimeMoon", true, 0f, 0f, M, 1f, 1f, 620f, 40f, false, 60, [(ItemId.RimeShard, 4), (ItemId.Credits, 100)], "Present Mimic"),
        new(EnemyKind.SnowHulk, "Snow hulk", "event:RimeMoon", true, 0f, 0f, G, 1.6f, 2f, 430f, 38f, false, 20, [(ItemId.RimeShard, 2), (ItemId.FrostCrystal, 3)], "Yeti"),
        new(EnemyKind.FrostspirePine, "Frostspire pine", "event:RimeMoon", true, 0f, 0f, G, 2.2f, 3.6f, 1550f, 40f, false, 130, [(ItemId.RimeShard, 10)], "Everscream", Mini, Stationary: true),
        new(EnemyKind.CryoTank, "Cryo tank", "event:RimeMoon", true, 0f, 0f, G, 2.6f, 2f, 1650f, 44f, false, 140, [(ItemId.RimeShard, 8), (ItemId.CircuitBoard, 4)], "Santa-NK1", Mini, Speed: 0.8f),
        // ---- Cryo Brigade (frost-legion analog) ----
        new(EnemyKind.BrigadeTrooper, "Brigade trooper", "event:CryoBrigade", true, 0f, 0f, G, 0.9f, 1.7f, 200f, 30f, false, 10, [(ItemId.RimeShard, 1)], "Snowman Gangsta"),
        new(EnemyKind.BrigadeShiv, "Brigade shiv", "event:CryoBrigade", true, 0f, 0f, G, 0.8f, 1.5f, 160f, 32f, false, 10, [(ItemId.FrostCrystal, 2)], "Mister Stabby", Speed: 1.45f),
        new(EnemyKind.BrigadeLobber, "Brigade lobber", "event:CryoBrigade", true, 0f, 0f, G, 1f, 1.7f, 220f, 30f, false, 10, [(ItemId.SnowBlock, 10)], "Snow Balla"),
        // ---- Obelisk sieges (lunar-pillar analogs): three natives each ----
        new(EnemyKind.CinderImp, "Cinder imp", "event:EmberObelisk", true, 0f, 0f, D, 0.9f, 0.9f, 400f, 44f, true, 24, [(ItemId.AstralOre, 1)], "Corite"),
        new(EnemyKind.SlagCentipede, "Slag centipede", "event:EmberObelisk", true, 0f, 0f, W, 1f, 1f, 680f, 46f, false, 30, [(ItemId.AstralOre, 2)], "Crawltipede", Segments: 10),
        new(EnemyKind.PyreLancer, "Pyre lancer", "event:EmberObelisk", true, 0f, 0f, G, 1.1f, 2f, 540f, 48f, false, 28, [(ItemId.AstralOre, 1), (ItemId.PyrociteOre, 4)], "Selenian / Drakomire Rider"),
        new(EnemyKind.GaleDiver, "Gale diver", "event:GaleObelisk", true, 0f, 0f, D, 1f, 0.7f, 420f, 44f, true, 24, [(ItemId.AstralOre, 1)], "Storm Diver"),
        new(EnemyKind.SquallHornet, "Squall hornet", "event:GaleObelisk", true, 0f, 0f, G, 0.9f, 0.8f, 380f, 42f, true, 24, [(ItemId.AstralOre, 1)], "Alien Hornet"),
        new(EnemyKind.TempestLarva, "Tempest larva", "event:GaleObelisk", true, 0f, 0f, H, 0.8f, 0.6f, 300f, 40f, false, 18, [(ItemId.AstralOre, 1)], "Alien Larva"),
        new(EnemyKind.RiftSeer, "Rift seer", "event:RiftObelisk", true, 0f, 0f, C, 0.9f, 1.8f, 470f, 46f, false, 26, [(ItemId.AstralOre, 1), (ItemId.VoidEssence, 2)], "Predictor"),
        new(EnemyKind.MindLeech, "Mind leech", "event:RiftObelisk", true, 0f, 0f, G, 0.8f, 0.8f, 350f, 42f, true, 22, [(ItemId.AstralOre, 1)], "Brain Suckler"),
        new(EnemyKind.RiftFloater, "Rift floater", "event:RiftObelisk", true, 0f, 0f, G, 1.1f, 1.3f, 500f, 44f, true, 26, [(ItemId.AstralOre, 1)], "Nebula Floater"),
        new(EnemyKind.StarCell, "Star-cell", "event:ShardObelisk", true, 0f, 0f, H, 0.9f, 0.9f, 380f, 42f, false, 22, [(ItemId.AstralOre, 1), (ItemId.PrismShard, 2)], "Star Cell"),
        new(EnemyKind.LatticeWeaver, "Lattice weaver", "event:ShardObelisk", true, 0f, 0f, W, 1f, 1f, 720f, 46f, false, 30, [(ItemId.AstralOre, 2)], "Milkyway Weaver", Segments: 12),
        new(EnemyKind.ShardInvader, "Shard invader", "event:ShardObelisk", true, 0f, 0f, G, 1f, 1f, 440f, 44f, true, 24, [(ItemId.AstralOre, 1)], "Flow Invader"),
        // ---- Lumen Tempest (torch-god analog) ----
        new(EnemyKind.LumenFlare, "Lumen flare", "event:LumenTempest", false, 0f, 0f, G, 0.7f, 0.7f, 70f, 15f, true, 4, [(ItemId.GlowSpore, 2)], "Torch God's fire"),
        new(EnemyKind.GlareMote, "Glare mote", "event:LumenTempest", false, 0f, 0f, D, 0.6f, 0.6f, 50f, 13f, true, 3, [(ItemId.EnergyCrystal, 1)], "Torch God's fire (diving)")
    ];

    public static readonly CoverageBoss[] Bosses =
    [
        new(EnemyKind.HoarfrostBehemoth, "Hoarfrost Behemoth", BossTier.Easy, 3.2f, 3f, 1000f, 22f, false, 9f, 4, 100, ItemId.FrostbittenLure,
            [(ItemId.FrostCrystal, 14), (ItemId.GlacialOre, 18), (ItemId.EnergyCrystal, 6)], "Deerclops"),
        new(EnemyKind.GelSovereign, "Gel Sovereign", BossTier.Hard, 3f, 2.6f, 2300f, 30f, false, 11f, 6, 200, ItemId.PrismGelLattice,
            [(ItemId.BioGel, 30), (ItemId.PrismShard, 12), (ItemId.PrismEssence, 10)], "Queen Slime"),
        new(EnemyKind.HollowRegent, "Hollow Regent", BossTier.Event, 3f, 3f, 3400f, 40f, true, 13f, 6, 300, ItemId.RegentWick,
            [(ItemId.HollowEmber, 20), (ItemId.LumeniteBar, 6), (ItemId.VoidEssence, 6)], "Pumpking"),
        new(EnemyKind.RimeSeraph, "Rime Seraph", BossTier.Event, 2.8f, 3.2f, 3600f, 42f, true, 14f, 7, 320, ItemId.SeraphFrostCore,
            [(ItemId.RimeShard, 20), (ItemId.LumeniteBar, 6), (ItemId.FrostCrystal, 12)], "Ice Queen")
    ];
}
