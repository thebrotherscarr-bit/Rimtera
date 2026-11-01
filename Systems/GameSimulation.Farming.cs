using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>A grow zone from a save made before 0.3.11 (zones are gone; farmers tend the planters near home). Read, then dropped.</summary>
public sealed record GrowZoneSnapshot(int X, int Y, int Width, TileId Herb);

public sealed partial class GameSimulation
{
    /// <summary>Seconds to plant or harvest one cell at plant work speed 1 (Plants 10).</summary>
    public const float FarmBaseSeconds = 3f;
    /// <summary>Seeds a farmer keeps for replanting; more go to storage with the herbs.</summary>
    public const int SeedsKept = 10;
    /// <summary>Herbs a farmer gathers before taking them to storage (sooner when nothing is left to harvest).</summary>
    public const int HarvestLoad = 10;

    private Random? _farmRandomStream;
    private Random FarmRandom => _farmRandomStream ??= new Random(Seed ^ 0x0FA4A5);

    /// <summary>The herb whose soil is under <paramref name="cell"/> (Sunpetal on planters or other ground).</summary>
    public TileId HerbForGround(GridPoint cell)
    {
        var below = World.TileAt(cell.X, cell.Y + 1);
        return TilePhysics.AllHerbs.FirstOrDefault(herb => Array.IndexOf(herb.Soils, below) >= 0)?.Tile ?? TileId.HerbSunpetal;
    }

    private readonly Dictionary<GridPoint, (long Tick, GridPoint[] Cells)> _farmCellsNearHome = new();

    /// <summary>
    /// The cells a farmer works near a home (0.3.11, no grow zones): every cell on a planter (planter box or plant pot), and
    /// every herb growing on its own soil, within reach of home. Rescanned every two seconds.
    /// </summary>
    public IReadOnlyList<GridPoint> FarmCellsNear(Vector2 home)
    {
        var key = new GridPoint((int)MathF.Floor(home.X), (int)MathF.Floor(home.Y));
        if (_farmCellsNearHome.TryGetValue(key, out var cached) && TickNumber - cached.Tick < 120)
            return cached.Cells;
        var cells = new List<GridPoint>();
        var reach = (int)OperateRadius;
        for (var y = key.Y - reach; y <= key.Y + reach; y++)
        for (var x = key.X - reach; x <= key.X + reach; x++)
        {
            if (!World.Contains(x, y) || !World.Contains(x, y + 1) ||
                Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), home) > OperateRadius)
                continue;
            if (FloraParity.IsPlanter(World.TileAt(x, y + 1)) || TilePhysics.IsHerb(World.TileAt(x, y)))
                cells.Add(new GridPoint(x, y));
        }
        _farmCellsNearHome[key] = (TickNumber, cells.ToArray());
        return cells;
    }

    /// <summary>Whether a farmer can plant <paramref name="herb"/> at a cell: empty, dry, on a planter or the herb's own soil.</summary>
    public bool CanPlant(TileId herb, GridPoint cell)
    {
        if (!World.Contains(cell) || World.TileAt(cell.X, cell.Y) != TileId.Air || World.GetCell(cell).LiquidUnits > 0 ||
            TilePhysics.HerbFor(herb) is not { } definition)
            return false;
        var below = World.TileAt(cell.X, cell.Y + 1);
        return FloraParity.IsPlanter(below) || Array.IndexOf(definition.Soils, below) >= 0;
    }

    /// <summary>Seconds one planting or harvest takes a farmer: the base time over their plant work speed (Plants skill).</summary>
    public static float FarmSeconds(Rimtera.Characters.ColonistStats stats) => FarmBaseSeconds / Math.Max(0.2f, stats.PlantWorkSpeed);

    /// <summary>The chance a harvest yields a second herb: 3% per Plants level above 10.</summary>
    public static float ExtraHerbChance(Rimtera.Characters.ColonistStats stats) =>
        Math.Max(0, stats.Level(Rimtera.Characters.ColonistSkill.Plants) - 10) * 0.03f;

    private static bool IsSeed(ItemId item) => TilePhysics.AllHerbs.Any(herb => herb.Seeds == item);

    /// <summary>What the farmer should take to storage: every herb, and seeds beyond what they keep for replanting.</summary>
    private static int Produce(Dictionary<ItemId, int> pouch) =>
        pouch.Sum(pair => IsSeed(pair.Key) ? Math.Max(0, pair.Value - SeedsKept) : pair.Value);

    /// <summary>
    /// The Farm job giver (0.3.11: no grow zones or Farm tab): store a full load, else harvest a blooming herb near home,
    /// else plant an empty planter near home with seeds from the pouch (fetched from a chest near home when it has none).
    /// </summary>
    private bool TryFarm(TownNpc agent, Vector2 center, float deltaSeconds)
    {
        void Move(Vector2 to) => StepColonistToward(agent, center, to, deltaSeconds, 1f);
        if (!agent.Carried.IsEmpty)
        {
            if (DeliverCarried(agent, center, Move))
                return true;
            SpawnGroundItem(agent.Carried.ItemId, agent.Carried.Count, center, agent.Carried.Calibration);
            agent.Carried = default;
        }

        var cells = FarmCellsNear(agent.HomePosition);
        var claimed = _townNpcs.Where(other => !ReferenceEquals(other, agent) && other.IsAlive && other.FarmTarget is not null)
            .Select(other => other.FarmTarget!.Value).ToHashSet();
        var harvest = cells
            .Where(cell => !claimed.Contains(cell) && TilePhysics.IsHerb(World.TileAt(cell.X, cell.Y)) && IsHerbBlooming(cell))
            .OrderBy(cell => Vector2.DistanceSquared(new Vector2(cell.X + 0.5f, cell.Y + 0.5f), center))
            .Cast<GridPoint?>().FirstOrDefault();

        // A full load, or herbs in hand and nothing left to harvest: take them to storage, one stack at a time.
        var produce = Produce(agent.Pouch);
        if (produce > 0 && (produce >= HarvestLoad || harvest is null))
            return UnloadPouch(agent);

        if (harvest is { } ripe)
            return WorkCell(agent, center, deltaSeconds, ripe, $"Harvesting {Tiles[World.TileAt(ripe.X, ripe.Y)].Name.ToLowerInvariant()}", Move, () => Harvest(agent, ripe));

        // Plant: the nearest empty planter (or bare herb soil where a herb grew) with seeds from the pouch, or fetched from a
        // chest near home. The herb is whichever the farmer has seeds for, its own soil's herb first.
        foreach (var cell in cells.Where(cell => !claimed.Contains(cell) && World.TileAt(cell.X, cell.Y) == TileId.Air &&
                                                 FloraParity.IsPlanter(World.TileAt(cell.X, cell.Y + 1)))
                     .OrderBy(cell => Vector2.DistanceSquared(new Vector2(cell.X + 0.5f, cell.Y + 0.5f), center)))
        {
            var herbs = TilePhysics.AllHerbs.OrderBy(herb => herb.Tile == HerbForGround(cell) ? 0 : 1).ToArray();
            if (herbs.FirstOrDefault(herb => agent.Pouch.GetValueOrDefault(herb.Seeds) > 0 && CanPlant(herb.Tile, cell)) is { } held)
                return WorkCell(agent, center, deltaSeconds, cell, $"Planting {Items[held.Seeds].Name.ToLowerInvariant()}", Move, () =>
                {
                    if (!CanPlant(held.Tile, cell) || agent.Pouch.GetValueOrDefault(held.Seeds) <= 0)
                        return;
                    Unpouch(agent, held.Seeds, 1);
                    World.SetTile(cell, held.Tile);
                    Flags.Increment("colony.planted");
                });
            foreach (var herb in herbs)
            {
                if (NearestHolding(agent, herb.Seeds) is not { } chestCell)
                    continue;
                agent.FarmTarget = null;
                agent.Activity = $"Fetching {Items[herb.Seeds].Name.ToLowerInvariant()}";
                if (Reaches(center, chestCell))
                {
                    var chest = GetContainer(chestCell)!;
                    var take = Math.Min(SeedsKept, chest.Count(herb.Seeds));
                    if (take > 0 && chest.TryConsume(herb.Seeds, take))
                        agent.Pouch[herb.Seeds] = agent.Pouch.GetValueOrDefault(herb.Seeds) + take;
                }
                else
                    Move(new Vector2(chestCell.X + 0.5f, chestCell.Y + 0.5f));
                return true;
            }
            break; // no seeds anywhere near home: nothing to plant
        }

        agent.FarmTarget = null;
        agent.FarmProgress = 0f;
        return false;
    }

    /// <summary>Walks to a zone cell and works it for <see cref="FarmSeconds"/>, then does <paramref name="finish"/>.</summary>
    private bool WorkCell(TownNpc agent, Vector2 center, float deltaSeconds, GridPoint cell, string activity, Action<Vector2> move, Action finish)
    {
        if (agent.FarmTarget != cell)
            agent.FarmProgress = 0f;
        agent.FarmTarget = cell;
        agent.Activity = activity;
        if (!Reaches(center, cell))
        {
            move(new Vector2(cell.X + 0.5f, cell.Y + 0.5f));
            return true;
        }
        move(center);
        agent.FarmProgress += deltaSeconds;
        if (agent.FarmProgress < FarmSeconds(agent.Stats))
            return true;
        agent.FarmProgress = 0f;
        agent.FarmTarget = null;
        finish();
        return true;
    }

    /// <summary>A farmer's harvest: the herb (a second one sometimes, with Plants skill) and 1–3 seeds into the pouch.</summary>
    private void Harvest(TownNpc agent, GridPoint cell)
    {
        if (TilePhysics.HerbFor(World.TileAt(cell.X, cell.Y)) is not { } herb || !IsHerbBlooming(cell))
            return;
        World.SetTile(cell, TileId.Air);
        var herbs = 1 + (FarmRandom.NextDouble() < ExtraHerbChance(agent.Stats) ? 1 : 0);
        agent.Pouch[herb.Herb] = agent.Pouch.GetValueOrDefault(herb.Herb) + herbs;
        agent.Pouch[herb.Seeds] = agent.Pouch.GetValueOrDefault(herb.Seeds) + FarmRandom.Next(1, 4);
        Flags.Increment("colony.harvested", herbs);
    }

    private static void Unpouch(TownNpc agent, ItemId item, int count)
    {
        var left = agent.Pouch.GetValueOrDefault(item) - count;
        if (left > 0)
            agent.Pouch[item] = left;
        else
            agent.Pouch.Remove(item);
    }

    /// <summary>The nearest chest near home holding <paramref name="item"/>.</summary>
    private GridPoint? NearestHolding(TownNpc agent, ItemId item) =>
        StorageCellsNear(agent.HomePosition)
            .Where(cell => GetContainer(cell)?.Count(item) > 0)
            .OrderBy(cell => Vector2.DistanceSquared(new Vector2(cell.X + 0.5f, cell.Y + 0.5f), agent.Motion.Position))
            .Cast<GridPoint?>().FirstOrDefault();

    /// <summary>Sets a farmer's seeds and herbs down for the haulers (taken off Farm, or dead).</summary>
    private void DropPouch(TownNpc agent, Vector2 at)
    {
        foreach (var (item, count) in agent.Pouch)
            SpawnGroundItem(item, count, at);
        agent.Pouch.Clear();
        agent.FarmTarget = null;
        agent.FarmProgress = 0f;
    }
}
