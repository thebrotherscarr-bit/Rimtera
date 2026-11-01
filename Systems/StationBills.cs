using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>How long a bill runs (RimWorld's bill repeat modes).</summary>
public enum BillMode : byte
{
    /// <summary>Make the target number, then stop.</summary>
    DoCount,
    /// <summary>Keep making until storage near the station holds the target number.</summary>
    UntilHave,
    /// <summary>Keep making while ingredients last.</summary>
    Forever
}

/// <summary>
/// One order at a station (docs/DESIGN_COLONY_ESCAPE.md §3.3, "make 10 healing potions", "keep 20 torches"). Operate
/// colonists work the station's bills top to bottom: they fetch the ingredients from chests near the station, craft at it,
/// and carry the product back to storage.
/// </summary>
public sealed class StationBill
{
    public const int MinimumTarget = 1, MaximumTarget = 999;

    public StationBill(string recipeId, BillMode mode = BillMode.DoCount, int target = 5)
    {
        RecipeId = recipeId;
        Mode = mode;
        Target = Math.Clamp(target, MinimumTarget, MaximumTarget);
    }

    public string RecipeId { get; }
    public BillMode Mode { get; set; }
    public int Target { get; private set; }
    /// <summary>Crafts finished under this bill (DoCount stops at <see cref="Target"/>).</summary>
    public int Done { get; set; }
    /// <summary>Paused by the player: nobody works it until it is resumed.</summary>
    public bool Suspended { get; set; }

    public void SetTarget(int target) => Target = Math.Clamp(target, MinimumTarget, MaximumTarget);

    /// <summary>The Bills tab's mode button: make N → keep N → forever → make N.</summary>
    public void CycleMode() => Mode = Mode switch
    {
        BillMode.DoCount => BillMode.UntilHave,
        BillMode.UntilHave => BillMode.Forever,
        _ => BillMode.DoCount
    };

    /// <summary>Whether the bill still wants work, given how many of the product storage near the station holds.</summary>
    public bool Wants(int inStorage) => !Suspended && Mode switch
    {
        BillMode.DoCount => Done < Target,
        BillMode.UntilHave => inStorage < Target,
        _ => true
    };

    /// <summary>The bill's line in the Bills tab: "Make 5 (2 done)", "Keep 20", "Forever".</summary>
    public string Describe() => (Mode switch
    {
        BillMode.DoCount => Done >= Target ? $"Made {Target}" : $"Make {Target} ({Done} done)",
        BillMode.UntilHave => $"Keep {Target}",
        _ => "Forever"
    }) + (Suspended ? ", paused" : "");

    public StationBillSnapshot Capture(GridPoint station) =>
        new(station.X, station.Y, RecipeId, Mode, Target, Done, Suspended);

    public static StationBill Restore(StationBillSnapshot saved) =>
        new(saved.RecipeId, saved.Mode, saved.Target) { Done = Math.Max(0, saved.Done), Suspended = saved.Suspended };
}

/// <summary>A saved bill: the station tile it belongs to and its order.</summary>
public sealed record StationBillSnapshot(int X, int Y, string RecipeId, BillMode Mode, int Target, int Done, bool Suspended);

/// <summary>
/// Someone who can work station bills and haul (0.2.4): crew members, and town residents beside their shops. The job
/// givers work through this, so a resident fetches, crafts and stores exactly as a colonist does.
/// </summary>
internal interface IStationWorker
{
    long Id { get; }
    bool IsAlive { get; }
    Rimtera.Entities.PawnMotionState Motion { get; }
    /// <summary>Where the worker lives: bills and storage are reached from here.</summary>
    System.Numerics.Vector2 HomePosition { get; }
    /// <summary>Ingredients fetched for a bill, held until the craft uses them.</summary>
    Dictionary<ItemId, int> Held { get; }
    /// <summary>A product (or a hauled stack) on its way to storage.</summary>
    ItemStack Carried { get; set; }
    /// <summary>The station whose bill this worker has claimed.</summary>
    GridPoint? OperateStation { get; set; }
    float OperateProgress { get; set; }
    string Activity { get; set; }
    float CraftingSpeed { get; }
    int CraftingLevel { get; }
}
