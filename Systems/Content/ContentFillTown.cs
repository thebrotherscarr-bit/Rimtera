using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Content-fill town residents (docs/TERRARIA_GAPS.md plan item 4): four harmless town gels that are found in their biome and then
/// move into free housing, and a travelling merchant who arrives on some mornings with a rotating stock and leaves at dusk.
/// Names, lines, stock and odds are original Rimtera data.
/// </summary>
public static class ContentFillTown
{
    /// <summary>Hours of the morning window in which the merchant may arrive, and the hour he packs up.</summary>
    public const float MerchantArriveFrom = 6f, MerchantArriveBy = 10f, MerchantLeaveHour = 18f;
    /// <summary>Items the merchant offers on any one visit.</summary>
    public const int MerchantStockSize = 6;
    /// <summary>Percent of days on which the merchant comes.</summary>
    public const int MerchantVisitPercent = 45;
    /// <summary>Residents the colony needs before the merchant bothers to visit.</summary>
    public const int MerchantMinimumResidents = 2;
    /// <summary>Residents the colony needs before town gels are found.</summary>
    public const int GelMinimumResidents = 3;

    /// <summary>The merchant's whole catalogue; each visit offers a day-seeded subset of it.</summary>
    public static readonly (ItemId Item, string? Flag)[] MerchantPool =
    [
        (ItemId.LuckyCharm, null), (ItemId.MagnetRing, null), (ItemId.NightVisionGoggles, null), (ItemId.CompassChip, null),
        (ItemId.DepthMeter, null), (ItemId.OreScanner, null), (ItemId.GrappleHook, null), (ItemId.DoubleJumpCanister, null),
        (ItemId.DiamondShard, null), (ItemId.ChargeCrystal, null), (ItemId.CargoPod, null), (ItemId.PetCrabShell, null),
        (ItemId.LightPetWisp, null), (ItemId.RecallCapsule, null), (ItemId.HerbPlanter, null), (ItemId.DyePetals, null),
        (ItemId.GoldenCritter, null), (ItemId.GildedVole, null), (ItemId.PhaseRod, "blight.fracture"), (ItemId.RocketPack, "ore.voidsteel")
    ];

    /// <summary>Town gels and the biome each one is found in.</summary>
    public static readonly (TownNpcKind Kind, BiomeZone[] Zones, string Colour)[] Gels =
    [
        (TownNpcKind.GelAzure, [BiomeZone.Grassland], "azure"),
        (TownNpcKind.GelAmber, [BiomeZone.JunkWasteland, BiomeZone.AcidCoast], "amber"),
        (TownNpcKind.GelRose, [BiomeZone.CrystalSpires, BiomeZone.VerdantCanopy], "rose"),
        (TownNpcKind.GelVerdant, [BiomeZone.FungalHollows, BiomeZone.Caverns], "verdant")
    ];

    public static IEnumerable<TownNpcDefinition> Definitions()
    {
        yield return new(TownNpcKind.TravellingMerchant, "Travelling Merchant", "npc_travelling_merchant",
            ["Quill Marrow", "Ondine Faraday", "Barnaby Keel", "Tess Voorhees"],
            [(BiomeZone.Grassland, Affection.Like), (BiomeZone.Caverns, Affection.Dislike)],
            [(TownNpcKind.TradeConsul, Affection.Dislike), (TownNpcKind.Quartermaster, Affection.Like)],
            MerchantPool,
            ["Fresh off the transit line. What I carry today is gone by dusk.", "Never the same crate twice. That is the charm.",
             "Credits only. Barter makes my ledger itch."]);
        foreach (var (kind, zones, colour) in Gels)
            yield return new(kind, "Town gel", $"npc_gel_{colour}", GelNames(colour),
                [(zones[0], Affection.Love)], [(TownNpcKind.AstroBotanist, Affection.Like)],
                [], [$"*a happy {colour} wobble*", "*bloop*", "*it hums a little tune and jiggles*"]);
    }

    private static string[] GelNames(string colour) => colour switch
    {
        "azure" => ["Bloop", "Skye", "Wobbler"],
        "amber" => ["Nugget", "Rusty", "Glim"],
        "rose" => ["Petal", "Fizz", "Prisma"],
        _ => ["Spore", "Moss", "Glowby"]
    };

    public static bool IsGel(TownNpcKind kind) => kind is >= TownNpcKind.GelAzure and <= TownNpcKind.GelVerdant;

    /// <summary>Deterministic hash roll in [0, 100) for (seed, day, salt).</summary>
    public static int Roll(int seed, long day, int salt)
    {
        unchecked
        {
            var hash = (uint)seed * 2654435761u ^ (uint)day * 2246822519u ^ (uint)salt * 3266489917u;
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            return (int)(hash % 100u);
        }
    }

    /// <summary>Whether the merchant comes on <paramref name="day"/>.</summary>
    public static bool MerchantComes(int seed, long day) => Roll(seed, day, 0x7EA) < MerchantVisitPercent;

    /// <summary>The day-seeded subset of the pool on offer during a visit.</summary>
    public static HashSet<ItemId> TodaysStock(int seed, long day) => MerchantPool
        .Select((entry, index) => (entry.Item, Rank: Roll(seed, day * 31 + index, 0x5C0) * 1000 + index))
        .OrderBy(entry => entry.Rank).Take(MerchantStockSize).Select(entry => entry.Item).ToHashSet();

    /// <summary>Shop filter: the merchant only sells today's rotation; everyone else is unaffected.</summary>
    public static bool InStock(TownNpcKind kind, ItemId item, int seed, long day) =>
        kind != TownNpcKind.TravellingMerchant || TodaysStock(seed, day).Contains(item);
}

public sealed partial class GameSimulation
{
    /// <summary>Hourly content-fill town update: the travelling merchant comes and goes, and town gels are found.</summary>
    private void UpdateContentFillTown()
    {
        var merchant = _townNpcs.FirstOrDefault(npc => npc.Kind == TownNpcKind.TravellingMerchant);
        if (merchant is not null)
        {
            if (ClockHour >= ContentFillTown.MerchantLeaveHour || ClockHour < ContentFillTown.MerchantArriveFrom)
            {
                _townNpcs.Remove(merchant);
                PostMessage($"{merchant.Name} the {merchant.Definition.Role} has packed up and moved on.");
            }
        }
        else if (ClockHour >= ContentFillTown.MerchantArriveFrom && ClockHour < ContentFillTown.MerchantArriveBy &&
                 Flags.Get("merchant.visit_day") != Day + 1 && ContentFillTown.MerchantComes(Seed, Day) &&
                 _townNpcs.Count(npc => !npc.IsBound && npc.Kind != TownNpcKind.TravellingMerchant) >= ContentFillTown.MerchantMinimumResidents)
        {
            Flags.Clear("merchant.visit_day");
            Flags.Increment("merchant.visit_day", Day + 1);
            if (ContentFillArrivalSpot(4) is { } spot)
            {
                var npc = AddTownNpc(TownNpcKind.TravellingMerchant, CellCenter(spot), bound: false);
                npc.HomeCell = spot;
                PostMessage($"{npc.Name} the {npc.Definition.Role} has arrived with today's wares!");
            }
        }

        // Town gels only turn up once the colony has a few residents (they never jump the queue for a first house).
        if (IsNight || _townNpcs.Count(npc => !npc.IsBound && !ContentFillTown.IsGel(npc.Kind) && npc.Kind != TownNpcKind.TravellingMerchant) <
            ContentFillTown.GelMinimumResidents)
            return;
        foreach (var (kind, zones, colour) in ContentFillTown.Gels)
        {
            var key = $"gel.{colour}.found";
            if (Flags.IsSet(key) || _townNpcs.Any(npc => npc.Kind == kind) || Array.IndexOf(zones, CurrentBiome.Zone) < 0 ||
                ContentFillTown.Roll(Seed, (long)MathF.Floor(ElapsedGameHours), (int)kind) >= 34)
                continue;
            if (ContentFillArrivalSpot(6) is not { } spot)
                continue;
            Flags.Set(key);
            Flags.Set($"npc.{WorldFlags.Key(kind)}.unlocked");
            var gel = AddTownNpc(kind, CellCenter(spot), bound: false);
            PostMessage($"A friendly {colour} gel named {gel.Name} bounces up to you. It will move into a free house.");
            return;
        }
    }

    /// <summary>A standing cell on the ground a few tiles beside the player, or null.</summary>
    private GridPoint? ContentFillArrivalSpot(int offset)
    {
        foreach (var side in new[] { 1, -1 })
        {
            var x = Math.Clamp((int)MathF.Floor(PlayerCenter.X) + side * offset, 2, World.Width - 3);
            if (TryFindSpawnGround(x, CurrentBiome.Underground, out var groundY))
                return new GridPoint(x, groundY - 1);
        }
        return null;
    }
}
