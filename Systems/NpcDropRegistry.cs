using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>When a drop rule may fire (Terraria's ItemDropRule conditions). Combine flags; all must hold.</summary>
[Flags]
public enum DropCondition : byte
{
    None = 0,
    /// <summary>Only after the Fracture (Terraria: hardmode).</summary>
    Fracture = 1,
    /// <summary>Only before the Fracture.</summary>
    PreFracture = 2,
    Night = 4,
    Day = 8,
    /// <summary>Only while the rule's <see cref="NpcDropDefinition.Event"/> is running.</summary>
    DuringEvent = 16
}

/// <summary>The world state drop conditions are checked against.</summary>
public sealed record NpcDropContext(bool Fracture, bool Night, WorldDifficulty Difficulty, IReadOnlySet<WorldEventKind> Events)
{
    /// <summary>A Classic world before the Fracture, by day, with no event running.</summary>
    public static NpcDropContext Default { get; } = new(false, false, WorldDifficulty.Classic, new HashSet<WorldEventKind>());
}

/// <summary>
/// One drop rule: <paramref name="Quantity"/> of <paramref name="Item"/> with a chance of
/// <see cref="Numerator"/>/<see cref="Denominator"/> (chances below 1% are fine: 1/500), when every condition holds.
/// The defaults (1/1, no conditions) are a guaranteed drop.
/// </summary>
public sealed record NpcDropDefinition(EnemyKind Kind, ItemId Item, int Quantity = 1)
{
    public int Numerator { get; init; } = 1;
    public int Denominator { get; init; } = 1;
    public DropCondition Conditions { get; init; }
    /// <summary>The easiest world difficulty the rule fires on (Brutal for Terraria's expert-only drops).</summary>
    public WorldDifficulty MinimumDifficulty { get; init; } = WorldDifficulty.Explorer;
    /// <summary>The event <see cref="DropCondition.DuringEvent"/> refers to.</summary>
    public WorldEventKind? Event { get; init; }

    /// <summary>Probability of the roll alone (conditions aside).</summary>
    public double Chance => Denominator <= 0 ? 0d : Math.Min(1d, (double)Numerator / Denominator);

    /// <summary>A plain guaranteed drop: certain, with no conditions.</summary>
    public bool Guaranteed => Numerator >= Denominator && Conditions == DropCondition.None && MinimumDifficulty == WorldDifficulty.Explorer;

    /// <summary>True when every condition holds in <paramref name="context"/>.</summary>
    public bool Allows(NpcDropContext context) =>
        context.Difficulty >= MinimumDifficulty &&
        (!Conditions.HasFlag(DropCondition.Fracture) || context.Fracture) &&
        (!Conditions.HasFlag(DropCondition.PreFracture) || !context.Fracture) &&
        (!Conditions.HasFlag(DropCondition.Night) || context.Night) &&
        (!Conditions.HasFlag(DropCondition.Day) || !context.Night) &&
        (!Conditions.HasFlag(DropCondition.DuringEvent) || Event is { } kind && context.Events.Contains(kind));

    /// <summary>Rolls the chance alone.</summary>
    public bool RollChance(Random random) => Numerator >= Denominator || Numerator > 0 && random.Next(Denominator) < Numerator;

    /// <summary>The rule with a chance of one in <paramref name="denominator"/>.</summary>
    public NpcDropDefinition OneIn(int denominator) => this with { Numerator = 1, Denominator = Math.Max(1, denominator) };

    /// <summary>The rule fires only while <paramref name="kind"/> is running.</summary>
    public NpcDropDefinition During(WorldEventKind kind) => this with { Conditions = Conditions | DropCondition.DuringEvent, Event = kind };
}

/// <summary>
/// Loot per creature. The first entry is the primary drop; bosses add crystals, alloy, and sometimes a weapon tied to
/// their tier. Rules carry a chance and conditions (<see cref="NpcDropDefinition"/>); most are guaranteed.
/// </summary>
public static class NpcDropRegistry
{
    /// <summary>The rules of <paramref name="kind"/> that pass their conditions and their chance this time.</summary>
    public static IReadOnlyList<NpcDropDefinition> Roll(EnemyKind kind, NpcDropContext context, Random random)
    {
        var rules = GetDrops(kind);
        if (rules.All(rule => rule.Guaranteed))
            return rules;
        return rules.Where(rule => rule.Allows(context) && rule.RollChance(random)).ToArray();
    }
    private static readonly Dictionary<EnemyKind, NpcDropDefinition[]> _drops = new()
    {
        [EnemyKind.MossSlime] = Drops(EnemyKind.MossSlime, (ItemId.Berry, 1)),
        [EnemyKind.CaveMoth] = Drops(EnemyKind.CaveMoth, (ItemId.Berry, 1)),
        [EnemyKind.CaveBat] = Drops(EnemyKind.CaveBat, (ItemId.CopperOre, 1)),
        [EnemyKind.RockGolem] = Drops(EnemyKind.RockGolem, (ItemId.RockBlock, 5)),
        [EnemyKind.Wraith] = Drops(EnemyKind.Wraith, (ItemId.CopperOre, 3)),
        [EnemyKind.RockMite] = Drops(EnemyKind.RockMite, (ItemId.CobaltOre, 2)),
        [EnemyKind.SkyRay] = Drops(EnemyKind.SkyRay, (ItemId.EnergyCrystal, 1)),
        [EnemyKind.SporeRat] = Drops(EnemyKind.SporeRat, (ItemId.RestorativeTonic, 1)),
        [EnemyKind.AstroBeast] = Drops(EnemyKind.AstroBeast, (ItemId.ViridiumOre, 2), (ItemId.CopperBar, 1)),

        [EnemyKind.StoneSentinel] = Drops(EnemyKind.StoneSentinel,
            (ItemId.CopperOre, 20), (ItemId.EnergyCrystal, 4)),

        [EnemyKind.AlphaSkitter] = Drops(EnemyKind.AlphaSkitter,
            (ItemId.EnergyCrystal, 6), (ItemId.CopperBar, 8), (ItemId.CobaltOre, 10)),
        [EnemyKind.DamagedMiningDrone] = Drops(EnemyKind.DamagedMiningDrone,
            (ItemId.CobaltOre, 18), (ItemId.EnergyCrystal, 6), (ItemId.CopperBar, 6)),
        [EnemyKind.PlantOvergrowth] = Drops(EnemyKind.PlantOvergrowth,
            (ItemId.EnergyCrystal, 8), (ItemId.RestorativeTonic, 5), (ItemId.Berry, 10)),
        [EnemyKind.ScoutMech] = Drops(EnemyKind.ScoutMech,
            (ItemId.PlasmaPistol, 1), (ItemId.CopperBar, 10), (ItemId.EnergyCrystal, 8)),

        // The hard trio drop the post-Fracture essences on top of their old loot.
        [EnemyKind.CyberSwarmLeader] = Drops(EnemyKind.CyberSwarmLeader,
            (ItemId.EnergyCrystal, 12), (ItemId.CopperBar, 12), (ItemId.HearthCharm, 1), (ItemId.BlightEssence, 20)),
        [EnemyKind.RenegadeOverseer] = Drops(EnemyKind.RenegadeOverseer,
            (ItemId.SniperRifle, 1), (ItemId.CopperBar, 14), (ItemId.ViridiumOre, 8), (ItemId.PrismEssence, 20)),
        // The bulb boss carries the precursor vault key.
        [EnemyKind.MutatedFlora] = Drops(EnemyKind.MutatedFlora,
            (ItemId.ViridiumOre, 12), (ItemId.EnergyCrystal, 12), (ItemId.ManaTonic, 5), (ItemId.VaultKey, 1)),
        [EnemyKind.FissionTyrant] = Drops(EnemyKind.FissionTyrant,
            (ItemId.CoreBreaker, 1), (ItemId.EnergyCrystal, 10), (ItemId.FissionSlag, 6)),
        // Pre-Fracture avenger of radiated hearts: tier 2-3 loot only.
        [EnemyKind.FalloutHivemind] = Drops(EnemyKind.FalloutHivemind,
            (ItemId.CausticShard, 8), (ItemId.UraniumOre, 14), (ItemId.EnergyCrystal, 8), (ItemId.CobaltOre, 10)),
        [EnemyKind.VoidCantor] = Drops(EnemyKind.VoidCantor,
            (ItemId.VoidEssence, 12), (ItemId.VoidsteelBar, 10), (ItemId.Credits, 400)),
        [EnemyKind.EmberObelisk] = Drops(EnemyKind.EmberObelisk, (ItemId.AstralOre, 12)),
        [EnemyKind.RiftObelisk] = Drops(EnemyKind.RiftObelisk, (ItemId.AstralOre, 12)),
        [EnemyKind.GaleObelisk] = Drops(EnemyKind.GaleObelisk, (ItemId.AstralOre, 12)),
        [EnemyKind.ShardObelisk] = Drops(EnemyKind.ShardObelisk, (ItemId.AstralOre, 12)),
        [EnemyKind.Abomination] = Drops(EnemyKind.Abomination,
            (ItemId.GrenadeLauncher, 1), (ItemId.ViridiumOre, 14), (ItemId.CopperBar, 16)),
        [EnemyKind.WarshipCore] = Drops(EnemyKind.WarshipCore,
            (ItemId.EnergyCrystal, 20), (ItemId.ViridiumOre, 16), (ItemId.MiniTurret, 2), (ItemId.FlightEssence, 20)),

        [EnemyKind.SolarFlareEntity] = Drops(EnemyKind.SolarFlareEntity,
            (ItemId.EnergyCrystal, 18), (ItemId.SolarPanel, 2)),
        [EnemyKind.VoidStalker] = Drops(EnemyKind.VoidStalker,
            (ItemId.ViridiumOre, 10), (ItemId.Trailcap, 1), (ItemId.EnergyCrystal, 8)),
        [EnemyKind.AsteroidMinerMech] = Drops(EnemyKind.AsteroidMinerMech,
            (ItemId.CobaltOre, 25), (ItemId.ViridiumOre, 15), (ItemId.CopperBar, 10)),
        [EnemyKind.CyberPirateCaptain] = Drops(EnemyKind.CyberPirateCaptain,
            (ItemId.GrenadeLauncher, 1), (ItemId.Arrow, 120), (ItemId.CopperBar, 12)),

        [EnemyKind.SnowWolf] = Drops(EnemyKind.SnowWolf, (ItemId.FrostCrystal, 1), (ItemId.Berry, 1)),
        [EnemyKind.Cryopterous] = Drops(EnemyKind.Cryopterous, (ItemId.FrostCrystal, 1)),
        [EnemyKind.SnowMite] = Drops(EnemyKind.SnowMite, (ItemId.GlacialOre, 1)),
        [EnemyKind.GlacialGolem] = Drops(EnemyKind.GlacialGolem, (ItemId.GlacialOre, 6), (ItemId.FrostCrystal, 3)),
        [EnemyKind.RadSkitter] = Drops(EnemyKind.RadSkitter, (ItemId.CausticShard, 1)),
        [EnemyKind.MutantRat] = Drops(EnemyKind.MutantRat, (ItemId.CausticShard, 1)),
        [EnemyKind.GeigerBug] = Drops(EnemyKind.GeigerBug, (ItemId.UraniumOre, 1)),
        [EnemyKind.GlowStalker] = Drops(EnemyKind.GlowStalker, (ItemId.CausticShard, 2), (ItemId.EnergyCrystal, 1)),
        [EnemyKind.RadLeech] = Drops(EnemyKind.RadLeech, (ItemId.UraniumOre, 5), (ItemId.CausticShard, 3)),
        [EnemyKind.PyroSkitter] = Drops(EnemyKind.PyroSkitter, (ItemId.ObsidianShard, 1)),
        [EnemyKind.MagmaTurtle] = Drops(EnemyKind.MagmaTurtle, (ItemId.ObsidianShard, 3), (ItemId.PyrociteOre, 1)),
        [EnemyKind.LavaLeech] = Drops(EnemyKind.LavaLeech, (ItemId.PyrociteOre, 1)),
        [EnemyKind.EmberRat] = Drops(EnemyKind.EmberRat, (ItemId.ObsidianShard, 1)),
        [EnemyKind.BasaltGolem] = Drops(EnemyKind.BasaltGolem, (ItemId.PyrociteOre, 6), (ItemId.ObsidianShard, 4)),
        // Irradiated Core natives (theme pass): core minerals and vitrified shards.
        [EnemyKind.CoreHusk] = Drops(EnemyKind.CoreHusk, (ItemId.PyrociteOre, 4), (ItemId.ObsidianShard, 3), (ItemId.UraniumOre, 2)),
        [EnemyKind.FissionWisp] = Drops(EnemyKind.FissionWisp, (ItemId.UraniumOre, 1), (ItemId.ObsidianShard, 1)),
        [EnemyKind.JunkSkitter] = Drops(EnemyKind.JunkSkitter, (ItemId.OxidizedShard, 1)),
        [EnemyKind.ScavengerCrab] = Drops(EnemyKind.ScavengerCrab, (ItemId.ScrapIronOre, 2), (ItemId.OxidizedShard, 1)),
        [EnemyKind.SalvageDrone] = Drops(EnemyKind.SalvageDrone, (ItemId.ScrapIronOre, 1), (ItemId.Arrow, 6)),
        [EnemyKind.ScrapMite] = Drops(EnemyKind.ScrapMite, (ItemId.ScrapIronOre, 1)),
        [EnemyKind.WastelandBeast] = Drops(EnemyKind.WastelandBeast, (ItemId.ScrapIronOre, 6), (ItemId.OxidizedShard, 3)),
        [EnemyKind.NeoTerraDeer] = Drops(EnemyKind.NeoTerraDeer, (ItemId.Berry, 3)),
        [EnemyKind.SkyHawk] = Drops(EnemyKind.SkyHawk, (ItemId.Berry, 1)),

        [EnemyKind.AstroGod] = Drops(EnemyKind.AstroGod,
            (ItemId.RocketLauncher, 1), (ItemId.EnergyCrystal, 40), (ItemId.ViridiumOre, 30))
    };

    /// <summary>Primary drop, kept for callers that only expect one item.</summary>
    public static NpcDropDefinition? GetDrop(EnemyKind kind) =>
        GetDrops(kind) is { Count: > 0 } drops ? drops[0] : null;

    public static IReadOnlyList<NpcDropDefinition> GetDrops(EnemyKind kind) =>
        _drops.TryGetValue(kind, out var drops) ? drops
        : _contentDrops.TryGetValue(kind, out var content) ? content
        : _eventDrops.TryGetValue(kind, out var invasion) ? invasion
        : ContentFillCreatures.Drops(kind) ?? CreatureCoverage.Drops(kind) ?? Array.Empty<NpcDropDefinition>();

    /// <summary>Content-expansion creature drops (biome materials, boss loot and summons).</summary>
    private static readonly Dictionary<EnemyKind, NpcDropDefinition[]> _contentDrops = new()
    {
        [EnemyKind.AcidSkitterer] = Drops(EnemyKind.AcidSkitterer, (ItemId.CoralShard, 1)),
        [EnemyKind.AcidShark] = Drops(EnemyKind.AcidShark, (ItemId.AcidEel, 1), (ItemId.CoralShard, 2)),
        [EnemyKind.TideLeech] = Drops(EnemyKind.TideLeech, (ItemId.BioGel, 2)),
        [EnemyKind.ReefCrawler] = Drops(EnemyKind.ReefCrawler, (ItemId.CoralShard, 2)),
        [EnemyKind.BrineGolem] = Drops(EnemyKind.BrineGolem, (ItemId.SaltCrustBlock, 8), (ItemId.CoralShard, 3)),
        [EnemyKind.SpittingBarnacle] = Drops(EnemyKind.SpittingBarnacle, (ItemId.ShellSandBlock, 4)),
        [EnemyKind.StormRay] = Drops(EnemyKind.StormRay, (ItemId.PowerCell, 1)),
        [EnemyKind.DrownedDiver] = Drops(EnemyKind.DrownedDiver, (ItemId.TideFish, 2), (ItemId.RebreatherMask, 1)),
        [EnemyKind.CoralSentinel] = Drops(EnemyKind.CoralSentinel, (ItemId.CoralReefBlock, 10), (ItemId.CoralWand, 1)),
        [EnemyKind.SporeZombie] = Drops(EnemyKind.SporeZombie, (ItemId.GlowSpore, 2)),
        [EnemyKind.FungalCrawler] = Drops(EnemyKind.FungalCrawler, (ItemId.GlowSpore, 1)),
        [EnemyKind.PuffDrifter] = Drops(EnemyKind.PuffDrifter, (ItemId.GlowSpore, 2)),
        [EnemyKind.MycelidShaman] = Drops(EnemyKind.MycelidShaman, (ItemId.GlowSpore, 4), (ItemId.SporeCenser, 1)),
        [EnemyKind.ShroomStalker] = Drops(EnemyKind.ShroomStalker, (ItemId.MushroomCapBlock, 4)),
        [EnemyKind.CapTurtle] = Drops(EnemyKind.CapTurtle, (ItemId.MushroomCapBlock, 6)),
        [EnemyKind.LanternBat] = Drops(EnemyKind.LanternBat, (ItemId.GlowSpore, 1)),
        [EnemyKind.SporeGolem] = Drops(EnemyKind.SporeGolem, (ItemId.SporeBlockBlock, 10), (ItemId.MushroomMace, 1)),
        [EnemyKind.PrismKnight] = Drops(EnemyKind.PrismKnight, (ItemId.PrismShard, 2)),
        [EnemyKind.ShardUnicorn] = Drops(EnemyKind.ShardUnicorn, (ItemId.PrismShard, 3), (ItemId.DiamondShard, 1)),
        [EnemyKind.LightMimic] = Drops(EnemyKind.LightMimic, (ItemId.CrystalCrate, 1), (ItemId.Credits, 200)),
        [EnemyKind.CrystalWraith] = Drops(EnemyKind.CrystalWraith, (ItemId.PrismShard, 1), (ItemId.VoidEssence, 1)),
        [EnemyKind.RefractorTurret] = Drops(EnemyKind.RefractorTurret, (ItemId.CrystalBlockBlock, 4)),
        [EnemyKind.HaloBat] = Drops(EnemyKind.HaloBat, (ItemId.PrismShard, 1)),
        [EnemyKind.PearlGolem] = Drops(EnemyKind.PearlGolem, (ItemId.PearlStoneBlock, 12), (ItemId.LumeniteOre, 4)),
        [EnemyKind.SpireDragonfly] = Drops(EnemyKind.SpireDragonfly, (ItemId.PrismShard, 1)),
        [EnemyKind.PollenDroneSwarm] = Drops(EnemyKind.PollenDroneSwarm, (ItemId.NectarBlock, 1)),
        [EnemyKind.ThornLurker] = Drops(EnemyKind.ThornLurker, (ItemId.JungleRootBlock, 3)),
        [EnemyKind.CanopyApe] = Drops(EnemyKind.CanopyApe, (ItemId.Berry, 4), (ItemId.ClothRoll, 1)),
        [EnemyKind.SnapjawPlant] = Drops(EnemyKind.SnapjawPlant, (ItemId.BioGel, 3)),
        [EnemyKind.HiveGuard] = Drops(EnemyKind.HiveGuard, (ItemId.HiveCombBlock, 2), (ItemId.NectarBlock, 1)),
        [EnemyKind.MudCrawler] = Drops(EnemyKind.MudCrawler, (ItemId.MudBlock, 4)),
        [EnemyKind.SpittingOrchid] = Drops(EnemyKind.SpittingOrchid, (ItemId.BioGel, 2)),
        [EnemyKind.JungleStalker] = Drops(EnemyKind.JungleStalker, (ItemId.ViridiumOre, 2)),
        [EnemyKind.VaultSentinel] = Drops(EnemyKind.VaultSentinel, (ItemId.AncientPowerCell, 1)),
        [EnemyKind.GlyphDrone] = Drops(EnemyKind.GlyphDrone, (ItemId.PrecursorGlyphBlock, 2)),
        [EnemyKind.AncientGuardian] = Drops(EnemyKind.AncientGuardian, (ItemId.PrecursorRelic, 1), (ItemId.AncientPowerCell, 2)),
        [EnemyKind.GeodeCrab] = Drops(EnemyKind.GeodeCrab, (ItemId.SapphireShard, 1), (ItemId.AmethystShard, 1)),
        [EnemyKind.AlabasterHoplite] = Drops(EnemyKind.AlabasterHoplite, (ItemId.AlabasterBlock, 6)),
        [EnemyKind.MiteBroodmother] = Drops(EnemyKind.MiteBroodmother, (ItemId.SilkThread, 12), (ItemId.ClimbingClaws, 1)),
        [EnemyKind.SilkSpiderMite] = Drops(EnemyKind.SilkSpiderMite, (ItemId.SilkThread, 3)),
        [EnemyKind.NeoTerraReaver] = Drops(EnemyKind.NeoTerraReaver, (ItemId.ViridiumBar, 2), (ItemId.Credits, 30)),
        [EnemyKind.TerraformerDrone] = Drops(EnemyKind.TerraformerDrone, (ItemId.CircuitBoard, 2), (ItemId.PowerCell, 1)),
        [EnemyKind.BloodfinAmbusher] = Drops(EnemyKind.BloodfinAmbusher, (ItemId.FishFillet, 3)),
        [EnemyKind.VengefulWisp] = Drops(EnemyKind.VengefulWisp, (ItemId.VoidEssence, 2)),
        // Batch B AI-family creatures.
        [EnemyKind.TunnelWyrm] = Drops(EnemyKind.TunnelWyrm, (ItemId.CobaltOre, 6), (ItemId.EnergyCrystal, 2)),
        [EnemyKind.DuneBorer] = Drops(EnemyKind.DuneBorer, (ItemId.SandBlock, 8), (ItemId.CopperOre, 3)),
        [EnemyKind.RiftCaster] = Drops(EnemyKind.RiftCaster, (ItemId.VoidEssence, 1), (ItemId.ChargeCellSmall, 1)),
        [EnemyKind.DiveKite] = Drops(EnemyKind.DiveKite, (ItemId.SilkThread, 2)),
        [EnemyKind.CacheMimic] = Drops(EnemyKind.CacheMimic, (ItemId.ScrapCrate, 1), (ItemId.ChargeCrystal, 1)),
        [EnemyKind.GelHopper] = Drops(EnemyKind.GelHopper, (ItemId.BioGel, 2)),
        [EnemyKind.BoulderHopper] = Drops(EnemyKind.BoulderHopper, (ItemId.RockBlock, 6)),
        [EnemyKind.FleaHopper] = Drops(EnemyKind.FleaHopper, (ItemId.BioGel, 1)),
        [EnemyKind.SporeMonarch] = Drops(EnemyKind.SporeMonarch, (ItemId.SporeFamiliar, 1), (ItemId.GlowSpore, 20), (ItemId.FungalCrate, 1)),
        [EnemyKind.BloomMatriarch] = Drops(EnemyKind.BloomMatriarch, (ItemId.HiveStingerRapier, 1), (ItemId.HiveFamiliar, 1)),
        [EnemyKind.PrismEmpress] = Drops(EnemyKind.PrismEmpress, (ItemId.PrismShard, 25), (ItemId.LumeniteBar, 10), (ItemId.CrystalCrate, 2)),
        [EnemyKind.VaultColossus] = Drops(EnemyKind.VaultColossus, (ItemId.PrecursorRelic, 2), (ItemId.AncientPowerCell, 4), (ItemId.AstralHalo, 1)),
        [EnemyKind.MoonEater] = Drops(EnemyKind.MoonEater, (ItemId.VoidEssence, 6), (ItemId.Credits, 150)),
        [EnemyKind.EclipseLord] = Drops(EnemyKind.EclipseLord, (ItemId.VoidEssence, 10), (ItemId.EclipseOrb, 1)),
        [EnemyKind.ScavengerWarlord] = Drops(EnemyKind.ScavengerWarlord, (ItemId.MechCompanionKey, 1), (ItemId.ScrapCrate, 3)),
        [EnemyKind.PirateDreadnought] = Drops(EnemyKind.PirateDreadnought, (ItemId.AlloyCrate, 3), (ItemId.Credits, 400)),
        [EnemyKind.ProtocolDragon] = Drops(EnemyKind.ProtocolDragon, (ItemId.CircuitBoard, 12), (ItemId.CommandModule, 1)),
        [EnemyKind.MothershipCore] = Drops(EnemyKind.MothershipCore, (ItemId.AncientPowerCell, 4), (ItemId.AstralCrate, 1)),
        [EnemyKind.AbyssLeviathan] = Drops(EnemyKind.AbyssLeviathan, (ItemId.AcidWader, 1), (ItemId.AstralCrate, 1), (ItemId.CoralTrident, 1))
    };

    private static NpcDropDefinition[] Drops(EnemyKind kind, params (ItemId Item, int Quantity)[] entries) =>
        entries.Select(entry => new NpcDropDefinition(kind, entry.Item, entry.Quantity)).ToArray();

    /// <summary>A drop with a one-in-<paramref name="oneIn"/> chance (1 = guaranteed).</summary>
    private static NpcDropDefinition D(EnemyKind kind, ItemId item, int quantity, int oneIn = 1) =>
        new NpcDropDefinition(kind, item, quantity).OneIn(oneIn);

    /// <summary>An event's rare signature item: one in <paramref name="oneIn"/>, only while that event runs.</summary>
    private static NpcDropDefinition Signature(EnemyKind kind, ItemId item, int oneIn, WorldEventKind during) =>
        new NpcDropDefinition(kind, item).OneIn(oneIn).During(during);

    /// <summary>
    /// The event units (D-22, Terraria's invasion enemies drop items): every Event* creature drops its event's materials, and
    /// the invasions whose finale has an arsenal signature item (the Scavenger Warlord's medal, the pirate spyglass, the eclipse
    /// sigil, the protocol shard, the mothership chip) let their units drop it rarely while the invasion runs.
    /// </summary>
    private static readonly Dictionary<EnemyKind, NpcDropDefinition[]> _eventDrops = new NpcDropDefinition[]
    {
        // Raid units (storyteller raids and the hardmode blight roster).
        D(EnemyKind.EventGrunt, ItemId.ScrapIronOre, 2),
        D(EnemyKind.EventScout, ItemId.ScrapIronOre, 1), D(EnemyKind.EventScout, ItemId.ClothRoll, 1, 3),
        D(EnemyKind.EventMarksman, ItemId.SlugRounds, 8),
        D(EnemyKind.EventBrute, ItemId.ScrapIronOre, 4), D(EnemyKind.EventBrute, ItemId.PlateBlock, 3, 2),
        D(EnemyKind.EventCommander, ItemId.CircuitBoard, 2), D(EnemyKind.EventCommander, ItemId.WarlordsMedal, 1, 150),
        D(EnemyKind.EventDrone, ItemId.ScrapIronOre, 1), D(EnemyKind.EventDrone, ItemId.PowerCell, 1, 2),
        D(EnemyKind.EventEnergyVortex, ItemId.EnergyCrystal, 2),
        D(EnemyKind.EventRogueTerraformer, ItemId.CircuitBoard, 2), D(EnemyKind.EventRogueTerraformer, ItemId.PowerCell, 1, 2),
        D(EnemyKind.EventVoidSiphon, ItemId.VoidEssence, 1, 2),
        D(EnemyKind.EventCorruptedSurvivor, ItemId.ClothRoll, 1), D(EnemyKind.EventCorruptedSurvivor, ItemId.BioGel, 1),
        D(EnemyKind.EventAbomination, ItemId.BioGel, 3), D(EnemyKind.EventAbomination, ItemId.CausticShard, 2),
        // Swarm rain.
        D(EnemyKind.EventSwarmer, ItemId.BioGel, 2),
        D(EnemyKind.EventSwarmQueen, ItemId.BioGel, 8), D(EnemyKind.EventSwarmQueen, ItemId.PrismShard, 1, 3),
        // Rust moon.
        D(EnemyKind.EventHusk, ItemId.OxidizedShard, 1),
        D(EnemyKind.EventRustWraith, ItemId.OxidizedShard, 2),
        D(EnemyKind.EventRustHound, ItemId.OxidizedShard, 1),
        D(EnemyKind.EventEyeDrone, ItemId.OxidizedShard, 1), D(EnemyKind.EventEyeDrone, ItemId.PowerCell, 1, 3),
        D(EnemyKind.EventCorrodedMech, ItemId.OxidizedShard, 4), D(EnemyKind.EventCorrodedMech, ItemId.CircuitBoard, 1),
        // Dust storm.
        D(EnemyKind.EventDustDevil, ItemId.SandBlock, 4),
        D(EnemyKind.EventSandRay, ItemId.OxidizedShard, 1),
        D(EnemyKind.EventScrapElemental, ItemId.ScrapIronOre, 5),
        D(EnemyKind.EventDustRaider, ItemId.ScrapIronOre, 2),
        // Acid rain.
        D(EnemyKind.EventAcidSlime, ItemId.BioGel, 2), D(EnemyKind.EventAcidSlime, ItemId.CausticShard, 1, 2),
        D(EnemyKind.EventAcidBird, ItemId.CausticShard, 1),
        D(EnemyKind.EventToxicCloud, ItemId.CausticShard, 1),
        // Calm nights and parties: the harmless units still leave a keepsake.
        D(EnemyKind.EventAuroraWisp, ItemId.GlowSpore, 1),
        D(EnemyKind.EventFestivalBalloonDrone, ItemId.ClothRoll, 1),
        // Meteor fall.
        D(EnemyKind.EventMeteorHead, ItemId.MeteoriteOre, 2),
        D(EnemyKind.EventMeteorCrawler, ItemId.MeteoriteOre, 3),
        // Dark sun (the eclipse).
        D(EnemyKind.EventEclipseShade, ItemId.VoidEssence, 1),
        D(EnemyKind.EventEclipseReaper, ItemId.VoidEssence, 2),
        D(EnemyKind.EventEclipseMothMech, ItemId.PowerCell, 2), D(EnemyKind.EventEclipseMothMech, ItemId.VoidEssence, 1),
        Signature(EnemyKind.EventEclipseShade, ItemId.EclipseSigil, 250, WorldEventKind.DarkSun),
        Signature(EnemyKind.EventEclipseReaper, ItemId.EclipseSigil, 250, WorldEventKind.DarkSun),
        Signature(EnemyKind.EventEclipseMothMech, ItemId.EclipseSigil, 250, WorldEventKind.DarkSun),
        // Scavenger raid.
        D(EnemyKind.EventScavengerGrunt, ItemId.ScrapIronOre, 2),
        D(EnemyKind.EventScavengerBrute, ItemId.ScrapIronOre, 4), D(EnemyKind.EventScavengerBrute, ItemId.PlateBlock, 3, 2),
        D(EnemyKind.EventScavengerSniper, ItemId.SlugRounds, 10),
        D(EnemyKind.EventScavengerRigger, ItemId.CircuitBoard, 1), D(EnemyKind.EventScavengerRigger, ItemId.ScrapIronOre, 2),
        Signature(EnemyKind.EventScavengerGrunt, ItemId.WarlordsMedal, 200, WorldEventKind.ScavengerRaid),
        Signature(EnemyKind.EventScavengerBrute, ItemId.WarlordsMedal, 200, WorldEventKind.ScavengerRaid),
        Signature(EnemyKind.EventScavengerSniper, ItemId.WarlordsMedal, 200, WorldEventKind.ScavengerRaid),
        Signature(EnemyKind.EventScavengerRigger, ItemId.WarlordsMedal, 200, WorldEventKind.ScavengerRaid),
        // Pirate boarding.
        D(EnemyKind.EventPirateDeckhand, ItemId.ClothRoll, 1), D(EnemyKind.EventPirateDeckhand, ItemId.Credits, 10),
        D(EnemyKind.EventPirateGunner, ItemId.SlugRounds, 12),
        D(EnemyKind.EventPirateParrotDrone, ItemId.PowerCell, 1),
        D(EnemyKind.EventPirateCannon, ItemId.ExplosiveCharge, 3), D(EnemyKind.EventPirateCannon, ItemId.FerriteBar, 3),
        Signature(EnemyKind.EventPirateDeckhand, ItemId.PiratesSpyglass, 200, WorldEventKind.PirateBoarding),
        Signature(EnemyKind.EventPirateGunner, ItemId.PiratesSpyglass, 200, WorldEventKind.PirateBoarding),
        Signature(EnemyKind.EventPirateParrotDrone, ItemId.PiratesSpyglass, 200, WorldEventKind.PirateBoarding),
        Signature(EnemyKind.EventPirateCannon, ItemId.PiratesSpyglass, 200, WorldEventKind.PirateBoarding),
        // Defense protocol.
        D(EnemyKind.EventProtocolGrunt, ItemId.CircuitBoard, 1),
        D(EnemyKind.EventProtocolShieldbearer, ItemId.PlateBlock, 4),
        D(EnemyKind.EventProtocolWyvern, ItemId.PowerCell, 1),
        D(EnemyKind.EventProtocolOgre, ItemId.CircuitBoard, 3),
        D(EnemyKind.EventProtocolMage, ItemId.EnergyCrystal, 2),
        Signature(EnemyKind.EventProtocolGrunt, ItemId.ProtocolShard, 250, WorldEventKind.DefenseProtocol),
        Signature(EnemyKind.EventProtocolShieldbearer, ItemId.ProtocolShard, 250, WorldEventKind.DefenseProtocol),
        Signature(EnemyKind.EventProtocolWyvern, ItemId.ProtocolShard, 250, WorldEventKind.DefenseProtocol),
        Signature(EnemyKind.EventProtocolOgre, ItemId.ProtocolShard, 250, WorldEventKind.DefenseProtocol),
        Signature(EnemyKind.EventProtocolMage, ItemId.ProtocolShard, 250, WorldEventKind.DefenseProtocol),
        // Precursor probes (the martian-madness analog).
        D(EnemyKind.EventPrecursorProbe, ItemId.AncientPowerCell, 1, 2),
        D(EnemyKind.EventProbeGunner, ItemId.AncientPowerCell, 1),
        D(EnemyKind.EventProbeTeslaWalker, ItemId.AncientPowerCell, 2), D(EnemyKind.EventProbeTeslaWalker, ItemId.PowerCell, 1),
        D(EnemyKind.EventProbeBrainDrone, ItemId.CircuitBoard, 2),
        Signature(EnemyKind.EventPrecursorProbe, ItemId.MothershipChip, 300, WorldEventKind.PrecursorProbes),
        Signature(EnemyKind.EventProbeGunner, ItemId.MothershipChip, 300, WorldEventKind.PrecursorProbes),
        Signature(EnemyKind.EventProbeTeslaWalker, ItemId.MothershipChip, 300, WorldEventKind.PrecursorProbes),
        Signature(EnemyKind.EventProbeBrainDrone, ItemId.MothershipChip, 300, WorldEventKind.PrecursorProbes),
    }.GroupBy(rule => rule.Kind).ToDictionary(group => group.Key, group => group.ToArray());

    /// <summary>The Event* creatures (invasion and raid units); each has at least one item drop.</summary>
    public static IReadOnlyCollection<EnemyKind> EventUnits => _eventDrops.Keys;
}
