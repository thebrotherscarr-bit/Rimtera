using Rimtera.AI;
using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Cooking and fishing (0.2.6, docs/DESIGN_COLONY_ESCAPE.md §4 rows 5 and 6). Cook crew work the bills at cooking stations
/// (GameSimulation.Bills.cs); hungry crew eat from the chests near home, the most filling food first, before they forage;
/// Fish crew fish open water near home and carry the catch to storage.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>A colonist eats from storage below this food level.</summary>
    public const float EatBelow = 0.6f;
    /// <summary>A fishing spot: open water this close to home (tiles), in a pool at least this big.</summary>
    public const float FishRadius = 24f;
    public const int FishPoolMinimum = 25;
    /// <summary>Seconds per catch at fishing speed 1 (Animals 10).</summary>
    public const float FishBaseSeconds = 12f;
    /// <summary>Fish a fisher gathers before carrying them to storage.</summary>
    public const int CatchLoad = 5;

    private Random? _crewFishingStream;
    private Random CrewFishingRandom => _crewFishingStream ??= new Random(Seed ^ 0x0F15E5);

    /// <summary>How much of a colonist's food need an item fills (its food value; 0 for anything inedible).</summary>
    public float FoodValue(ItemId item) =>
        Items.Contains(item) && Items[item].UseKind == ItemUseKind.Food ? Math.Max(0f, Items[item].FoodRestore) : 0f;

    /// <summary>
    /// A hungry colonist walks to the nearest chest near home with food in it and eats the most filling item there.
    /// False when not hungry or there is no food in storage (the colonist then forages as before).
    /// </summary>
    private bool TryEatFromStorage(ColonistAgent agent, Vector2 center, float deltaSeconds)
    {
        if (agent.Needs.FoodReserve >= EatBelow)
            return false;
        GridPoint? pantry = null;
        var nearest = float.MaxValue;
        foreach (var cell in StorageCellsNear(agent.HomePosition))
        {
            if (GetContainer(cell) is not { } chest || !chest.Slots.Any(stack => !stack.IsEmpty && FoodValue(stack.ItemId) > 0f))
                continue;
            var distance = Vector2.DistanceSquared(new Vector2(cell.X + 0.5f, cell.Y + 0.5f), center);
            if (distance < nearest)
            {
                nearest = distance;
                pantry = cell;
            }
        }
        if (pantry is not { } at)
            return false;
        if (!Reaches(center, at))
        {
            agent.Activity = "Going to eat";
            StepColonistToward(agent, center, new Vector2(at.X + 0.5f, at.Y + 0.5f), deltaSeconds, 1f);
            return true;
        }
        var chestInventory = GetContainer(at)!;
        var meal = chestInventory.Slots.Where(stack => !stack.IsEmpty && FoodValue(stack.ItemId) > 0f)
            .OrderByDescending(stack => FoodValue(stack.ItemId)).ThenBy(stack => stack.ItemId).First().ItemId;
        if (!chestInventory.TryConsume(meal, 1))
            return false;
        agent.Needs.Restore(NeedKind.Food, FoodValue(meal));
        agent.Activity = $"Eating {Items[meal].Name}";
        Flags.Increment("colony.meals");
        StepColonistToward(agent, center, center, deltaSeconds, 1f);
        return true;
    }

    /// <summary>A colonist's fishing speed: Animals skill (0.65 + 0.035 a level, 1 at 10) on their work factor.</summary>
    public static float FishSpeed(ColonistStats stats) => WorkCatalog.SkillSpeed(stats, WorkType.Fish);

    /// <summary>Seconds per catch for a colonist.</summary>
    public static float FishSeconds(ColonistStats stats) => FishBaseSeconds / Math.Max(0.2f, FishSpeed(stats));

    /// <summary>A crew fisher's power: 10, plus 2 an Animals level (no rod or bait needed; the player's rods out-fish them).</summary>
    public static float CrewFishingPower(ColonistStats stats) => 10f + stats.Level(ColonistSkill.Animals) * 2f;

    /// <summary>
    /// The nearest fishing spot near <paramref name="home"/>: a water cell open to the air above, in a pool of at least
    /// <see cref="FishPoolMinimum"/> cells.
    /// </summary>
    public GridPoint? FishingSpotNear(Vector2 home)
    {
        var reach = (int)FishRadius;
        GridPoint? best = null;
        var nearest = float.MaxValue;
        for (var y = (int)home.Y - reach; y <= (int)home.Y + reach; y++)
        for (var x = (int)home.X - reach; x <= (int)home.X + reach; x++)
        {
            if (!World.Contains(x, y) || !World.Contains(x, y - 1))
                continue;
            var cell = World.GetCell(new GridPoint(x, y));
            var above = World.GetCell(new GridPoint(x, y - 1));
            if (cell.LiquidKind != LiquidKind.Water || cell.LiquidDepth <= 0.2f || above.LiquidDepth > 0.2f ||
                Tiles[above.TileId].IsSolid)
                continue;
            var distance = Vector2.DistanceSquared(new Vector2(x + 0.5f, y + 0.5f), home);
            if (distance <= FishRadius * FishRadius && distance < nearest)
            {
                nearest = distance;
                best = new GridPoint(x, y);
            }
        }
        return best is { } spot && MeasurePool(spot, LiquidKind.Water, FishPoolMinimum + 1) >= FishPoolMinimum ? spot : null;
    }

    private readonly Dictionary<GridPoint, (long Tick, GridPoint? Spot)> _fishingSpots = new();

    /// <summary>The fishing spot near a home, rescanned every five seconds.</summary>
    private GridPoint? CachedFishingSpot(Vector2 home)
    {
        var key = new GridPoint((int)home.X, (int)home.Y);
        if (_fishingSpots.TryGetValue(key, out var cached) && TickNumber - cached.Tick < 300)
            return cached.Spot;
        var spot = FishingSpotNear(home);
        _fishingSpots[key] = (TickNumber, spot);
        return spot;
    }

    /// <summary>The Fish job giver: carry a full catch to storage, else fish at the spot near home.</summary>
    private bool TryFish(ColonistAgent agent, Vector2 center, float deltaSeconds)
    {
        void Move(Vector2 to) => StepColonistToward(agent, center, to, deltaSeconds, 1f);
        if (!agent.Carried.IsEmpty)
        {
            if (DeliverCarried(agent, center, Move))
                return true;
            SpawnGroundItem(agent.Carried.ItemId, agent.Carried.Count, center, agent.Carried.Calibration);
            agent.Carried = default;
        }
        var spot = CachedFishingSpot(agent.HomePosition);
        var catchCount = Produce(agent.Pouch);
        if (catchCount > 0 && (catchCount >= CatchLoad || spot is null))
            return UnloadPouch(agent);
        if (spot is not { } water)
        {
            agent.FarmTarget = null;
            return false;
        }

        // Stand at the water's edge (within a few tiles of the spot, on the shore) and fish.
        var at = new Vector2(water.X + 0.5f, water.Y + 0.5f);
        if (agent.FarmTarget != water)
            agent.FarmProgress = 0f;
        agent.FarmTarget = water;
        if (Vector2.Distance(at, center) > 3.5f)
        {
            agent.Activity = "Going fishing";
            Move(at);
            return true;
        }
        agent.Activity = "Fishing";
        Move(center);
        agent.FarmProgress += deltaSeconds;
        if (agent.FarmProgress < FishSeconds(agent.ColonistStats))
            return true;
        agent.FarmProgress = 0f;
        var context = GetFishingContext(water, LiquidKind.Water, MeasurePool(water, LiquidKind.Water), CrewFishingPower(agent.ColonistStats));
        var rarity = FishingTables.RollRarity(context.Power, CrewFishingRandom);
        var caught = FishingTables.Pick(context, rarity, CrewFishingRandom);
        if (Items.Contains(caught))
        {
            agent.Pouch[caught] = agent.Pouch.GetValueOrDefault(caught) + 1;
            Flags.Increment("colony.fish");
        }
        return true;
    }

    /// <summary>Moves one stack of produce (anything but the seeds kept for replanting) from the pouch into the worker's hands.</summary>
    private bool UnloadPouch(ColonistAgent agent)
    {
        var (item, count) = agent.Pouch.First(pair => IsSeed(pair.Key) ? pair.Value > SeedsKept : pair.Value > 0);
        var take = Math.Min(IsSeed(item) ? count - SeedsKept : count, Items[item].MaxStack);
        Unpouch(agent, item, take);
        agent.Carried = new ItemStack(item, take);
        agent.FarmTarget = null;
        agent.Activity = $"Taking {Items[item].Name} to storage";
        return true;
    }
}
