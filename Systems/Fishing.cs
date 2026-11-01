using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

public enum FishingRarity : byte
{
    Junk,
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

public enum FishingDepth : byte
{
    Surface,
    Underground,
    Caverns,
    Chasm
}

/// <summary>Everything a catch roll depends on.</summary>
public readonly record struct FishingContext(
    LiquidKind Liquid,
    BiomeZone Zone,
    FishingDepth Depth,
    int PoolSize,
    float Power,
    bool Night,
    float Rain,
    SeasonWindow Season = SeasonWindow.None,
    bool Fractured = false);

/// <summary>The cast bobber (renderer: position, bite flag and the reel window).</summary>
public sealed class FishingBobber
{
    internal FishingBobber(Vector2 position, Vector2 velocity, ItemId rod)
    {
        Position = position;
        Velocity = velocity;
        Rod = rod;
    }

    public Vector2 Position { get; internal set; }
    public Vector2 Velocity { get; internal set; }
    public ItemId Rod { get; }
    public bool Landed { get; internal set; }
    public LiquidKind Liquid { get; internal set; }
    public int PoolSize { get; internal set; }
    /// <summary>Seconds until the next bite (while landed and not biting).</summary>
    public float BiteTimer { get; internal set; }
    /// <summary>True while a fish is on the line; reel now.</summary>
    public bool Biting { get; internal set; }
    /// <summary>Seconds left in the reel window.</summary>
    public float ReelWindow { get; internal set; }
    internal float AgeSeconds { get; set; }
}

public sealed record FishingSaveState(int LastQuestTurnInDay = 0);

/// <summary>Result of a reel.</summary>
public sealed record CatchResult(FishingRarity Rarity, ItemId? Item, Rimtera.Entities.EnemyKind? Creature, string Message);

/// <summary>
/// Original Rimtera catch tables keyed by liquid × biome zone × depth × rarity
/// (FISHING_EVENTS_WEATHER.md §3.1), crate loot, junk and the Tide-Warden's quest fish.
/// </summary>
public static class FishingTables
{
    public const int FullPoolSize = 75;

    public static FishingDepth DepthAt(BiomeLayout layout, int x, int y, int height)
    {
        if (y >= layout.MoltenTopAt(x))
            return FishingDepth.Chasm;
        if (y <= layout.SurfaceAt(x) + 8)
            return FishingDepth.Surface;
        return y < height * 0.45f ? FishingDepth.Underground : FishingDepth.Caverns;
    }

    /// <summary>Rolls the rarity tier: more power shifts weight from junk toward legendary.</summary>
    public static FishingRarity RollRarity(float power, Random random)
    {
        var p = Math.Clamp(power, 0f, 200f);
        var legendary = p / 2400f;
        var epic = p / 700f;
        var rare = p / 240f;
        var uncommon = 0.12f + p / 260f;
        var junk = Math.Max(0.04f, 0.32f - p / 220f);
        var roll = (float)random.NextDouble();
        if ((roll -= legendary) < 0f) return FishingRarity.Legendary;
        if ((roll -= epic) < 0f) return FishingRarity.Epic;
        if ((roll -= rare) < 0f) return FishingRarity.Rare;
        if ((roll -= uncommon) < 0f) return FishingRarity.Uncommon;
        if ((roll -= junk) < 0f) return FishingRarity.Junk;
        return FishingRarity.Common;
    }

    /// <summary>The catch for one tier in a context (null = nothing bites at this tier here).</summary>
    public static ItemId Pick(FishingContext context, FishingRarity rarity, Random random)
    {
        ItemId Of(params ItemId[] options) => options[random.Next(options.Length)];
        if (rarity == FishingRarity.Junk)
            return Of(ItemId.RustedCan, ItemId.TangledWire, ItemId.SoggyDatapad);

        switch (context.Liquid)
        {
            // Inland acid pools and marsh pockets: a small, caustic pool.
            case LiquidKind.Acid:
                return rarity switch
                {
                    FishingRarity.Common => ItemId.AcidEel,
                    FishingRarity.Uncommon => ItemId.CausticShard,
                    FishingRarity.Rare => ItemId.BlightCrate,
                    FishingRarity.Epic => ItemId.HarpoonGun,
                    _ => ItemId.AcidWader
                };
            // Liquid uranium (stored as Lava) in the Irradiated Core.
            case LiquidKind.Lava:
                return rarity switch
                {
                    FishingRarity.Common => ItemId.EmberKoi,
                    FishingRarity.Uncommon => ItemId.ObsidianShard,
                    FishingRarity.Rare => ItemId.MoltenCrate,
                    FishingRarity.Epic => ItemId.LavaWaderBoots,
                    _ => ItemId.MagmaFishingRod
                };
            case LiquidKind.Nectar:
                return rarity switch
                {
                    FishingRarity.Common => ItemId.NectarMinnow,
                    FishingRarity.Uncommon => ItemId.NectarBlock,
                    FishingRarity.Rare => ItemId.CanopyCrate,
                    FishingRarity.Epic => ItemId.HiveFamiliar,
                    _ => ItemId.HiveStingerRapier
                };
        }

        // The fuel sea (zone id AcidCoast) and any fuel pool: the sea's own catch pool.
        if (context.Zone == BiomeZone.AcidCoast || context.Liquid == LiquidKind.Fuel)
            return rarity switch
            {
                FishingRarity.Common => Of(ItemId.ReefSnapper, ItemId.TideFish),
                FishingRarity.Uncommon => Of(ItemId.AcidEel, ItemId.CoralShard),
                FishingRarity.Rare => ItemId.CoastCrate,
                FishingRarity.Epic => Of(ItemId.CoralWand, ItemId.HarpoonGun),
                _ => Of(ItemId.CoralTrident, ItemId.CoralPendant)
            };

        // Water: zone first, then depth.
        var common = context.Zone switch
        {
            BiomeZone.FrozenWasteland => ItemId.FrostCod,
            BiomeZone.VerdantCanopy or BiomeZone.BloomHives => ItemId.MudSkipper,
            BiomeZone.FungalHollows => ItemId.SporeGuppy,
            BiomeZone.CrystalSpires => ItemId.PrismTrout,
            BiomeZone.RadiatedDeeps or BiomeZone.Overgrowth => ItemId.BlightCarp,
            BiomeZone.AcidCoast => ItemId.ReefSnapper,
            _ => context.Depth switch
            {
                FishingDepth.Surface => context.Night ? ItemId.FishGlow : Of(ItemId.TideFish, ItemId.FishSilver),
                FishingDepth.Underground => ItemId.FishSilver,
                _ => ItemId.CaveBlindfish
            }
        };
        return rarity switch
        {
            FishingRarity.Common => common,
            FishingRarity.Uncommon => context.Depth >= FishingDepth.Caverns ? ItemId.FishGlow : Of(ItemId.ReefSnapper, ItemId.TideFish),
            FishingRarity.Rare => CrateFor(context),
            FishingRarity.Epic => context.Depth >= FishingDepth.Caverns ? ItemId.FishingLure : ItemId.FishingLine,
            // The Astral crate (tier 6) waits for the Fracture; before it a legendary catch is an alloy crate.
            _ => context.Zone == BiomeZone.CrystalSpires ? ItemId.CrystalCrate : context.Fractured ? ItemId.AstralCrate : ItemId.AlloyCrate
        };
    }

    public static ItemId CrateFor(FishingContext context) => context.Zone switch
    {
        BiomeZone.AcidCoast => ItemId.CoastCrate,
        BiomeZone.FrozenWasteland => ItemId.FrozenCrate,
        BiomeZone.JunkWasteland => ItemId.JunkCrate,
        BiomeZone.FungalHollows => ItemId.FungalCrate,
        BiomeZone.VerdantCanopy or BiomeZone.BloomHives => ItemId.CanopyCrate,
        BiomeZone.CrystalSpires => ItemId.CrystalCrate,
        BiomeZone.RadiatedDeeps or BiomeZone.Overgrowth => ItemId.BlightCrate,
        BiomeZone.MoltenChasm => ItemId.MoltenCrate,
        _ => context.Power >= 60f ? ItemId.AlloyCrate : ItemId.ScrapCrate
    };

    /// <summary>Loot rolled when a crate is opened: (item, min, max) entries, each rolled once.</summary>
    public static (ItemId Item, int Min, int Max)[] CrateLoot(ItemId crate) => crate switch
    {
        ItemId.ScrapCrate => [(ItemId.FerriteBar, 2, 6), (ItemId.Credits, 10, 40), (ItemId.SlugRounds, 10, 40), (ItemId.Worm, 0, 3)],
        ItemId.AlloyCrate => [(ItemId.CobaltBar, 3, 8), (ItemId.PyrociteBar, 0, 4), (ItemId.Credits, 40, 120), (ItemId.FishingLure, 0, 1)],
        ItemId.AstralCrate => [(ItemId.LumeniteBar, 3, 8), (ItemId.VoidsteelBar, 0, 4), (ItemId.AstralOre, 0, 3), (ItemId.Credits, 200, 500)],
        ItemId.CoastCrate => [(ItemId.CoralShard, 3, 8), (ItemId.TideFish, 1, 3), (ItemId.ExplosiveCharge, 0, 10), (ItemId.Credits, 20, 60)],
        ItemId.FrozenCrate => [(ItemId.GlacialOre, 4, 10), (ItemId.FrostCrystal, 2, 5), (ItemId.FrostGripGloves, 0, 1), (ItemId.Credits, 20, 60)],
        ItemId.JunkCrate => [(ItemId.ScrapIronOre, 6, 14), (ItemId.CircuitBoard, 1, 3), (ItemId.PowerCell, 0, 2), (ItemId.Credits, 20, 60)],
        ItemId.FungalCrate => [(ItemId.GlowSpore, 4, 10), (ItemId.MushroomCapBlock, 3, 8), (ItemId.SporeCenser, 0, 1), (ItemId.Credits, 20, 60)],
        ItemId.CanopyCrate => [(ItemId.NectarBlock, 3, 8), (ItemId.ViridiumOre, 2, 6), (ItemId.RoyalNectar, 0, 1), (ItemId.Credits, 20, 60)],
        ItemId.CrystalCrate => [(ItemId.PrismShard, 3, 8), (ItemId.LumeniteOre, 4, 10), (ItemId.DiamondShard, 0, 2), (ItemId.Credits, 60, 160)],
        ItemId.BlightCrate => [(ItemId.CausticShard, 3, 6), (ItemId.UraniumOre, 3, 8), (ItemId.PurifierGel, 10, 30), (ItemId.Credits, 30, 80)],
        ItemId.MoltenCrate => [(ItemId.PyrociteOre, 5, 12), (ItemId.ObsidianShard, 4, 10), (ItemId.UraniumOre, 2, 6), (ItemId.LavaLure, 0, 1), (ItemId.Credits, 40, 100)],
        _ => []
    };

    /// <summary>
    /// Crate finds that wait for the Fracture: before it, a crate holds none of the hardmode ores and bars (Terraria's
    /// crates only give hardmode ores in hardmode), so a lucky catch cannot skip the drill-power and station gates.
    /// </summary>
    public static readonly IReadOnlySet<ItemId> FractureCrateLoot = new HashSet<ItemId>
    {
        ItemId.LumeniteOre, ItemId.LumeniteBar, ItemId.VoidsteelBar, ItemId.AstralOre
    };

    /// <summary>Astral ore waits for the obelisks to fall (the Astral Assembler, Terraria's Ancient Manipulator, comes after the cultists).</summary>
    public const string AstralCrateFlag = "obelisks.down";

    /// <summary>A crate's loot in a world that has (or has not yet) fractured, and whose obelisks are (or are not yet) down.</summary>
    public static IEnumerable<(ItemId Item, int Min, int Max)> CrateLoot(ItemId crate, bool fractured, bool obelisksDown = true) =>
        CrateLoot(crate).Where(entry => (fractured || !FractureCrateLoot.Contains(entry.Item)) && (obelisksDown || entry.Item != ItemId.AstralOre));

    /// <summary>The Tide-Warden's quest fish: where each bites and the flag that unlocks it (null = always).</summary>
    public static readonly (ItemId Fish, Func<FishingContext, bool> Where, string? Unlock)[] QuestFish =
    [
        (ItemId.SpinyRustback, context => context.Liquid == LiquidKind.Water && context.Depth == FishingDepth.Surface, null),
        (ItemId.TwinTailSnapper, context => context.Liquid == LiquidKind.Water && context.Zone == BiomeZone.Grassland, null),
        (ItemId.LanternEel, context => context.Liquid == LiquidKind.Water && context.Depth >= FishingDepth.Underground, null),
        (ItemId.MottledGrouper, context => context.Liquid == LiquidKind.Water && context.Zone is BiomeZone.VerdantCanopy or BiomeZone.BloomHives, null),
        (ItemId.BubbleRay, context => context.Liquid == LiquidKind.Acid || context.Zone == BiomeZone.AcidCoast, null),
        (ItemId.CrownedTetra, context => context.Liquid == LiquidKind.Nectar, null),
        (ItemId.Hexscale, context => context.Zone is BiomeZone.RadiatedDeeps or BiomeZone.Overgrowth, "hearts.broken"),
        (ItemId.GlassPike, context => context.Zone == BiomeZone.CrystalSpires, "blight.fracture"),
        (ItemId.VoidAngler, context => context.Liquid == LiquidKind.Lava, "ore.voidsteel"),
        (ItemId.StarfallMinnow, context => context.Liquid == LiquidKind.Water && context.Night, "season.starfall")
    ];
}
