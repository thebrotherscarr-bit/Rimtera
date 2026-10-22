namespace Rimtera.Systems;

/// <summary>
/// What a new character starts with, mirroring Terraria's character creation (evidence:
/// docs/terraria-evidence/full/tables/starting_items.md). Every non-Creative difficulty gets the same three lowest-tier
/// tools, each with a random calibration (one in four gets none). Creative, the Journey analog, gets the next tier's
/// tools plus a hammer, a summon, light, rope, recall, a hook and wings. Rimtera adds a little food because colonists
/// get hungry.
/// </summary>
public static class StarterKits
{
    /// <summary>Kit revision saved in character files; older characters get the current kit's tools on load.</summary>
    public const int Version = 1;

    /// <summary>The kit for Classic, Explorer and Ironman characters: a stiletto, a drill and a hatchet of ferrite.</summary>
    public static readonly (ItemId Item, int Count, bool Calibrated)[] Standard =
    [
        (ItemId.FerriteStiletto, 1, true), (ItemId.CopperPickaxe, 1, true), (ItemId.FerriteHatchet, 1, true), (ItemId.Berry, 12, false)
    ];

    /// <summary>The Creative supply drop: cobalt tools, a drone uplink, light, rope, recall, a hook and glider wings (worn).</summary>
    public static readonly (ItemId Item, int Count, bool Calibrated)[] Creative =
    [
        (ItemId.CobaltStiletto, 1, true), (ItemId.CobaltDrill, 1, true), (ItemId.CobaltHatchet, 1, true), (ItemId.CobaltDemolisher, 1, true),
        (ItemId.FerriteDroneUplink, 1, true), (ItemId.GlowSticks, 100, false), (ItemId.ClimbingRope, 100, false),
        (ItemId.RecallCapsule, 3, false), (ItemId.GrappleHook, 1, false), (ItemId.GliderWings, 1, true), (ItemId.Berry, 12, false)
    ];

    /// <summary>The item Creative characters start wearing in their first accessory slot.</summary>
    public const ItemId CreativeWornWings = ItemId.GliderWings;

    /// <summary>A starting calibration: one in four items gets none, the rest roll as if freshly crafted.</summary>
    public static Calibration Roll(Random random, ItemDefinition item) =>
        random.Next(4) == 0 ? Calibration.None : CalibrationCatalog.Roll(random, item);
}
