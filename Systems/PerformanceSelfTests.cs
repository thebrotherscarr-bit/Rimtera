using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The performance pass (0.8.2): the table index the free-house search reads stays true to the world (tables placed and
/// broken are tracked, a bulk edit the cell events missed forces a rescan), the shared enum arrays match the enums, and the
/// late-game bench's budgets stay tighter than the engine's own.
/// </summary>
public static class PerformanceSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var world = new TileGrid(64, 32);
        world.SetTile(new GridPoint(3, 3), TileId.PlankTable);
        var index = new TableIndex(world);
        var first = index.Cells.ToArray();
        world.SetTile(new GridPoint(10, 5), TileId.DiningTable);
        world.SetTile(new GridPoint(3, 3), TileId.Air);
        var tracked = index.Cells.ToHashSet();
        var rescansBefore = index.Rescans;
        world.MutableCells[20 * 64 + 20] = world.GetCell(new GridPoint(10, 5)) with { };
        world.MarkBulkEdit();
        var afterBulk = index.Cells.ToHashSet();
        check(first.SequenceEqual([3 * 64 + 3]) && tracked.SetEquals([5 * 64 + 10]) && index.Rescans == rescansBefore + 1 &&
              afterBulk.SetEquals([5 * 64 + 10, 20 * 64 + 20]),
            "the table index tracks tables placed and broken from the cell events, and rescans after a bulk edit it did not see");

        check(EnumValues<EquipmentSlot>.All.SequenceEqual(Enum.GetValues<EquipmentSlot>()) &&
              EnumValues<WorkType>.All.SequenceEqual(Enum.GetValues<WorkType>()),
            "the shared enum arrays hold every value, in order");

        check(LateGameBench.TickBudgetMs < 5 && LateGameBench.WorstTickBudgetMs < 50 && LateGameBench.FrameQueriesBudgetMs <= 0.25 &&
              LateGameBench.Rows(new LateGameBench.Result(1, 10, 0.05, 29, 5, 0)).Count() == 3,
            "the late-game bench reports its tick, worst tick and HUD rows against budgets tighter than the engine's");
    }
}
