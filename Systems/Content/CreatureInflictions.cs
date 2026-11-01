using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// Debuffs creatures put on the player when their contact or their shots land (0.1.9 IC-3), filled in after the
/// rosters are registered so the hand-authored, generated and coverage tables stay untouched. Chosen by what the
/// name and biome imply, in the spirit of Terraria's creatures: spiders, hornets and spore things poison (the big
/// recluses carry venom), frost creatures chill or frostburn, fire creatures set you burning, void creatures darken
/// your sight or burn you with cursed fire, and a few others bleed, confuse, weaken, crack armour, slow or silence.
/// Chance is per landed hit; seconds are real seconds (converted to vital hours when applied).
/// </summary>
public static class CreatureInflictions
{
    private static readonly Dictionary<EnemyKind, (StatusId Status, float Chance, float Seconds)[]> Table = new()
    {
        // Spiders and mites: poison, the large recluses venom.
        [EnemyKind.SilkSpiderMite] = [(StatusId.Poison, 0.5f, 6f)],
        [EnemyKind.BurrowSpider] = [(StatusId.Poison, 0.5f, 6f)],
        [EnemyKind.WebLurker] = [(StatusId.Poison, 0.6f, 8f)],
        [EnemyKind.MiteBroodmother] = [(StatusId.Poison, 0.5f, 8f)],
        [EnemyKind.VenomRecluse] = [(StatusId.Venom, 0.5f, 4f)],
        [EnemyKind.BroodTyrant] = [(StatusId.Venom, 0.33f, 5f)],
        // Stingers and toxins: poison.
        [EnemyKind.HornetDrone] = [(StatusId.Poison, 0.33f, 5f)],
        [EnemyKind.MossHornet] = [(StatusId.Poison, 0.33f, 6f)],
        [EnemyKind.SquallHornet] = [(StatusId.Poison, 0.33f, 6f)],
        [EnemyKind.PollenDroneSwarm] = [(StatusId.Poison, 0.25f, 5f)],
        [EnemyKind.HiveGuard] = [(StatusId.Poison, 0.25f, 5f)],
        [EnemyKind.RoyalCombGuard] = [(StatusId.Poison, 0.33f, 6f)],
        [EnemyKind.HiveWarden] = [(StatusId.Poison, 0.33f, 6f)],
        [EnemyKind.SpittingOrchid] = [(StatusId.Poison, 0.33f, 6f)],
        [EnemyKind.EventToxicCloud] = [(StatusId.Poison, 1f, 4f)],
        [EnemyKind.EventAcidSlime] = [(StatusId.Poison, 0.5f, 5f)],
        // Frost: chilled (slower) or frostburn (damage over time).
        [EnemyKind.Cryopterous] = [(StatusId.Chilled, 0.5f, 4f)],
        [EnemyKind.SnowMite] = [(StatusId.Chilled, 0.33f, 3f)],
        [EnemyKind.GlacialGolem] = [(StatusId.Chilled, 0.5f, 5f)],
        [EnemyKind.RimeGel] = [(StatusId.Chilled, 0.5f, 4f)],
        [EnemyKind.FrostFlitter] = [(StatusId.Chilled, 0.5f, 4f)],
        [EnemyKind.PermafrostBorer] = [(StatusId.Chilled, 0.5f, 4f)],
        [EnemyKind.RimeDiver] = [(StatusId.Chilled, 0.5f, 4f)],
        [EnemyKind.IcicleBat] = [(StatusId.Chilled, 0.5f, 3f)],
        [EnemyKind.GlacierTortoise] = [(StatusId.Chilled, 0.5f, 5f)],
        [EnemyKind.WhiteoutWisp] = [(StatusId.Chilled, 0.5f, 4f)],
        [EnemyKind.FlurryFlake] = [(StatusId.Chilled, 0.5f, 3f)],
        [EnemyKind.SnowHulk] = [(StatusId.Chilled, 0.5f, 5f)],
        [EnemyKind.FrostspirePine] = [(StatusId.Chilled, 0.5f, 4f)],
        [EnemyKind.HoarfrostBehemoth] = [(StatusId.Chilled, 0.5f, 6f)],
        [EnemyKind.CryoCaster] = [(StatusId.Frostburn, 0.5f, 5f)],
        [EnemyKind.GlacierTitan] = [(StatusId.Frostburn, 0.5f, 5f)],
        [EnemyKind.BlizzardWraith] = [(StatusId.Frostburn, 0.5f, 5f)],
        [EnemyKind.FrostboundDelver] = [(StatusId.Frostburn, 0.33f, 5f)],
        [EnemyKind.HoarfrostElemental] = [(StatusId.Frostburn, 0.5f, 5f)],
        [EnemyKind.BlizzardColossus] = [(StatusId.Frostburn, 0.5f, 6f)],
        [EnemyKind.FrostSapper] = [(StatusId.Frostburn, 0.33f, 5f)],
        [EnemyKind.CryoTank] = [(StatusId.Frostburn, 0.5f, 5f)],
        [EnemyKind.RimeSeraph] = [(StatusId.Frostburn, 0.5f, 6f)],
        // Fire: burning.
        [EnemyKind.PyroSkitter] = [(StatusId.Burning, 0.33f, 4f)],
        [EnemyKind.MagmaTurtle] = [(StatusId.Burning, 0.5f, 4f)],
        [EnemyKind.LavaLeech] = [(StatusId.Burning, 0.5f, 4f)],
        [EnemyKind.EmberRat] = [(StatusId.Burning, 0.33f, 3f)],
        [EnemyKind.BasaltGolem] = [(StatusId.Burning, 0.33f, 4f)],
        [EnemyKind.EmberObelisk] = [(StatusId.Burning, 0.5f, 4f)],
        [EnemyKind.EmberSplinter] = [(StatusId.Burning, 0.5f, 3f)],
        [EnemyKind.CinderHound] = [(StatusId.Burning, 0.5f, 4f)],
        [EnemyKind.EmberRootwalker] = [(StatusId.Burning, 0.5f, 4f)],
        [EnemyKind.CinderImp] = [(StatusId.Burning, 0.5f, 4f)],
        [EnemyKind.SlagCentipede] = [(StatusId.Burning, 0.5f, 5f)],
        [EnemyKind.PyreLancer] = [(StatusId.Burning, 0.5f, 4f)],
        [EnemyKind.SlagGel] = [(StatusId.Burning, 0.33f, 3f)],
        [EnemyKind.SolarFlareEntity] = [(StatusId.Burning, 0.5f, 5f)],
        // Void and shade: darkness or cursed fire.
        [EnemyKind.Wraith] = [(StatusId.Darkness, 0.33f, 5f)],
        [EnemyKind.VoidMantis] = [(StatusId.Darkness, 0.33f, 5f)],
        [EnemyKind.EventVoidSiphon] = [(StatusId.Darkness, 0.5f, 6f)],
        [EnemyKind.ShadeCaster] = [(StatusId.Darkness, 0.33f, 6f)],
        [EnemyKind.UmbralGel] = [(StatusId.Darkness, 0.33f, 4f)],
        [EnemyKind.GloomKite] = [(StatusId.Darkness, 0.25f, 4f)],
        [EnemyKind.EventEclipseShade] = [(StatusId.Darkness, 0.33f, 5f)],
        [EnemyKind.VoidScribe] = [(StatusId.CursedFire, 0.33f, 4f)],
        [EnemyKind.VoidStalker] = [(StatusId.CursedFire, 0.33f, 5f)],
        [EnemyKind.VoidCantor] = [(StatusId.CursedFire, 0.33f, 5f)],
        // Teeth and pincers bleed; mind things confuse; tomb husks weaken; rust cracks armour; mud and dust slow;
        // runic and hex casters silence.
        [EnemyKind.BloodfinAmbusher] = [(StatusId.Bleeding, 0.5f, 8f)],
        [EnemyKind.DeepMaw] = [(StatusId.Bleeding, 0.33f, 6f)],
        [EnemyKind.PincerCrab] = [(StatusId.Bleeding, 0.25f, 5f)],
        [EnemyKind.MindLeech] = [(StatusId.Confused, 0.5f, 3f)],
        [EnemyKind.EventProbeBrainDrone] = [(StatusId.Confused, 0.33f, 3f)],
        [EnemyKind.TombHusk] = [(StatusId.Weak, 0.33f, 8f)],
        [EnemyKind.GraveHusk] = [(StatusId.Weak, 0.25f, 6f)],
        [EnemyKind.RustReaper] = [(StatusId.BrokenArmor, 0.33f, 6f)],
        [EnemyKind.RustBasilisk] = [(StatusId.BrokenArmor, 0.33f, 6f)],
        [EnemyKind.EventCorrodedMech] = [(StatusId.BrokenArmor, 0.33f, 6f)],
        [EnemyKind.DustGhoul] = [(StatusId.Slow, 0.33f, 4f)],
        [EnemyKind.MudCrawler] = [(StatusId.Slow, 0.25f, 3f)],
        [EnemyKind.RunicCaster] = [(StatusId.Silenced, 0.33f, 4f)],
        [EnemyKind.HexNecrotech] = [(StatusId.Silenced, 0.33f, 4f)],
    };

    /// <summary>Every creature kind that inflicts something, with its list (for reports and tests).</summary>
    public static IReadOnlyDictionary<EnemyKind, (StatusId Status, float Chance, float Seconds)[]> All => Table;

    /// <summary>Registration pass: gives an archetype its inflicted debuffs unless it already authors its own.</summary>
    public static EnemyArchetype Attach(EnemyArchetype archetype) =>
        archetype.Inflicts.Count == 0 && Table.TryGetValue(archetype.Kind, out var inflicts)
            ? archetype with { Inflicts = inflicts }
            : archetype;
}
