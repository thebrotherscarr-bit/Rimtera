using Rimtera.Entities;

namespace Rimtera.Systems;

public sealed record NpcDropDefinition(EnemyKind Kind, ItemId Item, int Quantity = 1);

/// <summary>
/// Guaranteed loot per creature. The first entry is the primary drop; bosses add
/// crystals, alloy, and sometimes a weapon tied to their tier.
/// </summary>
public static class NpcDropRegistry
{
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
}
