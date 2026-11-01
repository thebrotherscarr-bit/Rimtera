using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Station bills (0.2.3, docs/DESIGN_COLONY_ESCAPE.md §3.3 and §4 row 3): each crafting station keeps a queue of orders
/// ("make 10 healing potions", "keep 20 torches"). Operate colonists work them top to bottom: they fetch the ingredients
/// from chests near the station, craft at the station (faster with Crafting skill, with a chance of an extra item at high
/// skill), and carry the product to storage. The player still decides what gets made; the crew saves the trips.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>A colonist works bills at stations this close to home (tiles).</summary>
    public const float OperateRadius = 24f;
    /// <summary>Chests this close to a station feed its bills and count toward "keep N".</summary>
    public const float BillStorageRadius = 24f;
    public const int MaxBillsPerStation = 8;
    /// <summary>Seconds one craft takes at a crafting speed of 1 (Crafting skill 10).</summary>
    public const float BillBaseSeconds = 8f;
    /// <summary>Above this Crafting level each craft has a growing chance of an extra batch (2% a level, 20% at 20).</summary>
    public const int ExtraItemSkill = 10;

    private readonly Dictionary<GridPoint, List<StationBill>> _bills = new();

    /// <summary>
    /// Who works a station's bills (0.2.5): chem labs brew (Brew crew, on Medical), every other station is Operate crew's
    /// (on Crafting). Residents work any station.
    /// </summary>
    public WorkType BillWork(GridPoint station) => StationAt(station) is CraftingStationId.ChemLab or CraftingStationId.BioLab
        ? WorkType.Brew
        : WorkType.Operate;

    /// <summary>Whether a tile is a crafting station (bills live on station tiles).</summary>
    public bool IsStation(GridPoint cell) =>
        World.Contains(cell) && Tiles.Contains(World.GetCell(cell).TileId) && Tiles[World.GetCell(cell).TileId].Station != CraftingStationId.None;

    public CraftingStationId StationAt(GridPoint cell) =>
        IsStation(cell) ? Tiles[World.GetCell(cell).TileId].Station : CraftingStationId.None;

    /// <summary>Stations that have bills, and their queues (top first).</summary>
    public IReadOnlyDictionary<GridPoint, List<StationBill>> Bills => _bills;

    public IReadOnlyList<StationBill> BillsAt(GridPoint station) =>
        _bills.TryGetValue(station, out var bills) ? bills : [];

    /// <summary>Station tiles within the player's crafting reach, nearest first (the Bills tab's list).</summary>
    public IReadOnlyList<GridPoint> StationsNearPlayer()
    {
        var center = PlayerCenter;
        var radius = 6f + CraftingStationReachBonus;
        var found = new List<GridPoint>();
        for (var y = (int)MathF.Floor(center.Y - radius); y <= (int)MathF.Ceiling(center.Y + radius); y++)
        for (var x = (int)MathF.Floor(center.X - radius); x <= (int)MathF.Ceiling(center.X + radius); x++)
        {
            var cell = new GridPoint(x, y);
            if (IsStation(cell) && Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) <= radius)
                found.Add(cell);
        }
        return found.OrderBy(cell => Vector2.DistanceSquared(new Vector2(cell.X + 0.5f, cell.Y + 0.5f), center)).ToArray();
    }

    /// <summary>The station area a bill is crafted in: the stations, liquids and biome around the station tile.</summary>
    private CraftingEnvironment StationEnvironment(GridPoint station) =>
        CreateCraftingEnvironment(new Vector2(station.X + 0.5f, station.Y + 0.5f), 6f);

    /// <summary>
    /// Recipes a bill can be placed for at a station: ones that need this station (or one it stands in for) and whose
    /// other needs the station's surroundings meet, those the nearby chests can already cover first. Recipes made by hand
    /// need no station and get no bills.
    /// </summary>
    public IReadOnlyList<RecipeDefinition> BillRecipesAt(GridPoint station)
    {
        var kind = StationAt(station);
        if (kind == CraftingStationId.None)
            return [];
        var provides = new HashSet<CraftingStationId>(kind.ImpliedClosure()) { kind };
        var environment = StationEnvironment(station);
        var stock = StockNear(station);
        return provides.SelectMany(Crafting.FindByStation).Distinct()
            .Where(recipe => Crafting.CheckConditions(recipe, environment).Success)
            .OrderBy(recipe => CraftingSystem.PlanIngredients(recipe, stock.GetValueOrDefault) is null ? 1 : 0)
            .ThenBy(recipe => Items[recipe.Output].Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Whether the chests near a station hold everything one craft of <paramref name="recipe"/> needs.</summary>
    public bool StockCovers(GridPoint station, RecipeDefinition recipe) =>
        CraftingSystem.PlanIngredients(recipe, StockNear(station).GetValueOrDefault) is not null;

    /// <summary>Adds a bill to the bottom of a station's queue; null when the tile is no station, the recipe can't be made there, or the queue is full.</summary>
    public StationBill? AddBill(GridPoint station, string recipeId, BillMode mode = BillMode.DoCount, int target = 5)
    {
        if (!IsStation(station) || Crafting.FindById(recipeId) is not { } recipe || !BillRecipesAt(station).Contains(recipe))
            return null;
        if (!_bills.TryGetValue(station, out var bills))
            _bills[station] = bills = new List<StationBill>();
        if (bills.Count >= MaxBillsPerStation)
            return null;
        var bill = new StationBill(recipe.Id, mode, target);
        bills.Add(bill);
        return bill;
    }

    public bool RemoveBill(GridPoint station, StationBill bill)
    {
        if (!_bills.TryGetValue(station, out var bills) || !bills.Remove(bill))
            return false;
        if (bills.Count == 0)
            _bills.Remove(station);
        return true;
    }

    /// <summary>Moves a bill one place up (-1) or down (+1) its station's queue; the top bill is worked first.</summary>
    public bool MoveBill(GridPoint station, StationBill bill, int direction)
    {
        if (!_bills.TryGetValue(station, out var bills) || bills.IndexOf(bill) is var index && index < 0)
            return false;
        var to = index + Math.Sign(direction);
        if (to < 0 || to >= bills.Count || to == index)
            return false;
        (bills[index], bills[to]) = (bills[to], bills[index]);
        return true;
    }

    /// <summary>How many of an item the chests near a station hold (a "keep N" bill counts these).</summary>
    public int StoredNear(GridPoint station, ItemId item) =>
        BillStorage(station).Sum(cell => GetContainer(cell)?.Count(item) ?? 0);

    private IReadOnlyList<GridPoint> BillStorage(GridPoint station) =>
        StorageCellsNear(new Vector2(station.X + 0.5f, station.Y + 0.5f), BillStorageRadius);

    /// <summary>Seconds one craft takes this colonist: the base time over their crafting speed (Crafting skill).</summary>
    public static float OperateSeconds(Rimtera.Characters.ColonistStats stats) => OperateSeconds(stats.CraftingSpeed);

    public static float OperateSeconds(float craftingSpeed) => BillBaseSeconds / Math.Max(0.2f, craftingSpeed);

    /// <summary>The chance a craft yields an extra batch: 2% per Crafting level above 10.</summary>
    public static float ExtraBatchChance(Rimtera.Characters.ColonistStats stats) =>
        ExtraBatchChance(stats.Level(Rimtera.Characters.ColonistSkill.Crafting));

    public static float ExtraBatchChance(int craftingLevel) => Math.Max(0, craftingLevel - ExtraItemSkill) * 0.02f;

    /// <summary>Everyone who can work bills: the crew and the town's residents.</summary>
    private IEnumerable<IStationWorker> StationWorkers() => _colonists.Cast<IStationWorker>().Concat(_townNpcs);

    private sealed record BillJob(GridPoint Station, StationBill Bill, RecipeDefinition Recipe, Dictionary<ItemId, int> Plan);

    /// <summary>The Operate job giver: store a finished product, else take the first wanted bill that can be made.</summary>
    private bool TryOperate(IStationWorker agent, Vector2 center, float deltaSeconds, Action<Vector2> move, WorkType? work = WorkType.Operate)
    {
        if (!agent.Carried.IsEmpty)
        {
            if (DeliverCarried(agent, center, move, agent.OperateStation))
                return true;
            // Nowhere to store it: set it down by the station area so a hauler (or the player) can take it.
            SpawnGroundItem(agent.Carried.ItemId, agent.Carried.Count, center, agent.Carried.Calibration);
            agent.Carried = default;
        }

        if (FindBillJob(agent, work) is not { } job)
        {
            agent.OperateStation = null;
            agent.OperateProgress = 0f;
            DropHeld(agent, center);
            return false;
        }
        if (agent.OperateStation != job.Station)
            agent.OperateProgress = 0f;
        agent.OperateStation = job.Station;
        var product = Items[job.Recipe.Output].Name;

        // Fetch whatever ingredients are still missing from the nearest chest that has some.
        var missing = job.Plan
            .Select(pair => (Item: pair.Key, Count: pair.Value - agent.Held.GetValueOrDefault(pair.Key)))
            .Where(need => need.Count > 0).ToArray();
        if (missing.Length > 0)
        {
            GridPoint? source = null;
            var nearest = float.MaxValue;
            foreach (var cell in BillStorage(job.Station))
            {
                if (GetContainer(cell) is not { } chest || !missing.Any(need => chest.Count(need.Item) > 0))
                    continue;
                var distance = Vector2.DistanceSquared(new Vector2(cell.X + 0.5f, cell.Y + 0.5f), center);
                if (distance < nearest)
                {
                    nearest = distance;
                    source = cell;
                }
            }
            if (source is not { } from)
                return false; // FindBillJob saw the stock; it moved this tick
            agent.Activity = $"Fetching ingredients for {product}";
            var at = new Vector2(from.X + 0.5f, from.Y + 0.5f);
            if (Reaches(center, from))
            {
                var chest = GetContainer(from)!;
                foreach (var (item, count) in missing)
                {
                    var take = Math.Min(count, chest.Count(item));
                    if (take > 0 && chest.TryConsume(item, take))
                        agent.Held[item] = agent.Held.GetValueOrDefault(item) + take;
                }
            }
            else
                move(at);
            return true;
        }

        // Everything is in hand: work at the station.
        var stationAt = new Vector2(job.Station.X + 0.5f, job.Station.Y + 0.5f);
        if (!Reaches(center, job.Station))
        {
            agent.Activity = $"Going to the {StationAt(job.Station).DisplayName()} to make {product}";
            move(stationAt);
            return true;
        }
        agent.Activity = $"Making {product} at the {StationAt(job.Station).DisplayName()}";
        move(center);
        agent.OperateProgress += deltaSeconds;
        if (agent.OperateProgress < OperateSeconds(agent.SpeedFor(work ?? BillWork(job.Station))))
            return true;

        agent.OperateProgress = 0f;
        foreach (var (item, count) in job.Plan)
        {
            var left = agent.Held.GetValueOrDefault(item) - count;
            if (left > 0)
                agent.Held[item] = left;
            else
                agent.Held.Remove(item);
        }
        var output = Items[job.Recipe.Output];
        var made = job.Recipe.OutputCount;
        if (output.MaxStack > 1 && CraftRandom.NextDouble() < ExtraBatchChance(agent.LevelFor(work ?? BillWork(job.Station))))
            made += job.Recipe.OutputCount;
        var calibration = output.Calibratable || CalibrationCatalog.IsAccessory(output) ? RollCalibration(output) : Calibration.None;
        agent.Carried = new ItemStack(job.Recipe.Output, made, calibration);
        job.Bill.Done++;
        Flags.Increment("colony.crafted", made);
        SoundEvents.Emit(SoundEventKind.Craft, SoundEventQueue.CraftVariant(job.Recipe), stationAt, (int)job.Recipe.Output);
        return true;
    }

    /// <summary>Within arm's reach of a tile (measured to its edge, so a colonist pressed against a solid station reaches it).</summary>
    private static bool Reaches(Vector2 center, GridPoint cell) => DistanceToRect(center, new Vector2(cell.X, cell.Y), Vector2.One) < 1.5f;

    /// <summary>The first bill this colonist can work: their own station first, then stations near home nobody else is at.</summary>
    private BillJob? FindBillJob(IStationWorker agent, WorkType? work)
    {
        var stations = _bills.Keys
            .Where(station => (work is null || BillWork(station) == work) && Vector2.Distance(new Vector2(station.X + 0.5f, station.Y + 0.5f), agent.HomePosition) <= OperateRadius &&
                              !StationWorkers().Any(other => !ReferenceEquals(other, agent) && other.IsAlive && other.OperateStation == station))
            .OrderBy(station => station == agent.OperateStation ? 0 : 1)
            .ThenBy(station => Vector2.DistanceSquared(new Vector2(station.X + 0.5f, station.Y + 0.5f), agent.Motion.Position))
            .ToArray();
        foreach (var station in stations)
        {
            if (!IsStation(station))
                continue;
            var environment = StationEnvironment(station);
            Dictionary<ItemId, int>? stock = null;
            foreach (var bill in _bills[station])
            {
                if (Crafting.FindById(bill.RecipeId) is not { } recipe || !bill.Wants(StoredNear(station, recipe.Output)) ||
                    !Crafting.CheckConditions(recipe, environment).Success)
                    continue;
                stock ??= StockNear(station);
                var plan = CraftingSystem.PlanIngredients(recipe,
                    item => stock.GetValueOrDefault(item) + agent.Held.GetValueOrDefault(item));
                if (plan is not null)
                    return new BillJob(station, bill, recipe, plan);
            }
        }
        return null;
    }

    /// <summary>Everything the chests near a station hold, by item.</summary>
    private Dictionary<ItemId, int> StockNear(GridPoint station)
    {
        var stock = new Dictionary<ItemId, int>();
        foreach (var cell in BillStorage(station))
            if (GetContainer(cell) is { } chest)
                foreach (var stack in chest.Slots)
                    if (!stack.IsEmpty)
                        stock[stack.ItemId] = stock.GetValueOrDefault(stack.ItemId) + stack.Count;
        return stock;
    }

    /// <summary>Sets down ingredients a colonist no longer has a bill for, where haulers (or the player) can pick them up.</summary>
    private void DropHeld(IStationWorker agent, Vector2 at)
    {
        foreach (var (item, count) in agent.Held)
            SpawnGroundItem(item, count, at);
        agent.Held.Clear();
    }

    /// <summary>A dead worker's load falls where they died.</summary>
    private void DropColonistLoad(IStationWorker agent)
    {
        var at = agent.Motion.Position + agent.Motion.Size / 2f;
        if (!agent.Carried.IsEmpty)
            SpawnGroundItem(agent.Carried.ItemId, agent.Carried.Count, at, agent.Carried.Calibration);
        agent.Carried = default;
        DropHeld(agent, at);
        agent.OperateStation = null;
    }

    private StationBillSnapshot[] CaptureBills() =>
        _bills.Where(pair => IsStation(pair.Key))
            .OrderBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X)
            .SelectMany(pair => pair.Value.Select(bill => bill.Capture(pair.Key)))
            .ToArray();

    private void RestoreBills(StationBillSnapshot[]? saved)
    {
        _bills.Clear();
        foreach (var entry in saved ?? [])
        {
            if (entry is null || string.IsNullOrEmpty(entry.RecipeId) || Crafting.FindById(entry.RecipeId) is null)
                continue;
            var station = new GridPoint(entry.X, entry.Y);
            if (!IsStation(station))
                continue;
            if (!_bills.TryGetValue(station, out var bills))
                _bills[station] = bills = new List<StationBill>();
            if (bills.Count < MaxBillsPerStation)
                bills.Add(StationBill.Restore(entry));
        }
    }
}
