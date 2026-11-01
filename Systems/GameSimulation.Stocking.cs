using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The colony by default (0.3.9, docs/VISION.md §3.3): nobody sets bills any more. Every few seconds each lab and cooking
/// station near someone's home gets a standing order to keep <see cref="StockTarget"/> of the best healing item or meal it
/// can make in the storage around it, and the people whose work it is (a chemist, a cook) fill it by skill. An order only
/// takes a recipe the player's research and the station AI's power allow, and the best one changes as the colony grows.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>How many of a stocked item the colony keeps in the storage near its station.</summary>
    public const int StockTarget = 10;
    private const float StockIntervalSeconds = 5f;
    private float _stockTimer = StockIntervalSeconds;

    /// <summary>What a station keeps in stock: healing at the labs, food at the cooking station; none elsewhere.</summary>
    public static Func<ItemDefinition, float>? StockScore(CraftingStationId station) => station switch
    {
        CraftingStationId.ChemLab or CraftingStationId.BioLab => item => item.HealAmount,
        CraftingStationId.CookingStation => item => item.UseKind == ItemUseKind.Food ? item.FoodRestore + item.StatusDurationHours * 0.001f : 0f,
        _ => null
    };

    private void UpdateStocking(float deltaSeconds)
    {
        _stockTimer += deltaSeconds;
        if (_stockTimer < StockIntervalSeconds)
            return;
        _stockTimer = 0f;
        var stations = new HashSet<GridPoint>();
        var reach = (int)OperateRadius;
        foreach (var home in StationWorkers().Where(worker => worker.IsAlive && (worker is not TownNpc npc || npc is { IsBound: false, HomeCell: not null }))
                     .Select(worker => worker.HomePosition))
            for (var y = (int)home.Y - reach; y <= (int)home.Y + reach; y++)
            for (var x = (int)home.X - reach; x <= (int)home.X + reach; x++)
            {
                var cell = new GridPoint(x, y);
                if (IsStation(cell) && StockScore(StationAt(cell)) is not null &&
                    Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), home) <= OperateRadius)
                    stations.Add(cell);
            }
        foreach (var station in stations)
            StockStation(station);
    }

    /// <summary>Sets a station's standing order to the best stock recipe it can make now (a bill set any other way is left alone).</summary>
    private void StockStation(GridPoint station)
    {
        if (_bills.TryGetValue(station, out var existing) && existing.Any(bill => !bill.Auto))
            return;
        var score = StockScore(StationAt(station))!;
        var candidates = BillRecipesAt(station).Where(recipe => score(Items[recipe.Output]) > 0f).ToArray();
        if (candidates.Length == 0)
        {
            _bills.Remove(station);
            return;
        }
        var stock = StockNear(station);
        var covered = candidates.Where(recipe => CraftingSystem.PlanIngredients(recipe, stock.GetValueOrDefault) is not null).ToArray();
        var best = (covered.Length > 0 ? covered : candidates).MaxBy(recipe => score(Items[recipe.Output]))!;
        if (existing is [{ } current] && current.RecipeId == best.Id)
            return;
        _bills[station] = [new StationBill(best.Id, BillMode.UntilHave, StockTarget) { Auto = true }];
    }
}
