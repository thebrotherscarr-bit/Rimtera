using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// A grow zone (0.2.5, docs/DESIGN_COLONY_ESCAPE.md §4 row 4): a strip of cells the player marks for one herb. Farm crew
/// plant its seeds there (on the herb's own soil or in planters), harvest it when it blooms, keep seeds to replant and
/// carry the herbs to storage. Herbs grow and bloom by their usual rules (day, night, rain, dusk, ...).
/// </summary>
public sealed class GrowZone
{
    public const int MinimumWidth = 1, MaximumWidth = 16;

    public GrowZone(int x, int y, int width, TileId herb)
    {
        X = x;
        Y = y;
        Width = Math.Clamp(width, MinimumWidth, MaximumWidth);
        Herb = TilePhysics.IsHerb(herb) ? herb : TileId.HerbSunpetal;
    }

    /// <summary>The leftmost cell; herbs sit in this row, on the ground below it.</summary>
    public int X { get; private set; }
    public int Y { get; }
    public int Width { get; private set; }
    /// <summary>The herb tile planted here.</summary>
    public TileId Herb { get; set; }

    public IEnumerable<GridPoint> Cells => Enumerable.Range(X, Width).Select(x => new GridPoint(x, Y));

    public bool Contains(GridPoint cell) => cell.Y == Y && cell.X >= X && cell.X < X + Width;

    /// <summary>Widens or narrows the zone on its right edge.</summary>
    public void Resize(int width) => Width = Math.Clamp(width, MinimumWidth, MaximumWidth);

    /// <summary>The Farm tab's herb button: the next of the six herbs.</summary>
    public void CycleHerb()
    {
        var herbs = TilePhysics.AllHerbs;
        var index = herbs.Select((herb, i) => (herb, i)).First(entry => entry.herb.Tile == Herb).i;
        Herb = herbs[(index + 1) % herbs.Count].Tile;
    }

    public GrowZoneSnapshot Capture() => new(X, Y, Width, Herb);
}

/// <summary>A saved grow zone.</summary>
public sealed record GrowZoneSnapshot(int X, int Y, int Width, TileId Herb);

public sealed partial class GameSimulation
{
    public const int MaxGrowZones = 12;
    /// <summary>Seconds to plant or harvest one cell at plant work speed 1 (Plants 10).</summary>
    public const float FarmBaseSeconds = 3f;
    /// <summary>Seeds a farmer keeps for replanting; more go to storage with the herbs.</summary>
    public const int SeedsKept = 10;
    /// <summary>Herbs a farmer gathers before taking them to storage (sooner when nothing is left to harvest).</summary>
    public const int HarvestLoad = 10;

    private readonly List<GrowZone> _growZones = new();
    private Random? _farmRandomStream;
    private Random FarmRandom => _farmRandomStream ??= new Random(Seed ^ 0x0FA4A5);

    public IReadOnlyList<GrowZone> GrowZones => _growZones;

    /// <summary>The zone a cell belongs to, if any.</summary>
    public GrowZone? GrowZoneAt(GridPoint cell) => _growZones.FirstOrDefault(zone => zone.Contains(cell));

    /// <summary>The herb whose soil is under <paramref name="cell"/> (Sunpetal on planters or other ground).</summary>
    public TileId HerbForGround(GridPoint cell)
    {
        var below = World.TileAt(cell.X, cell.Y + 1);
        return TilePhysics.AllHerbs.FirstOrDefault(herb => Array.IndexOf(herb.Soils, below) >= 0)?.Tile ?? TileId.HerbSunpetal;
    }

    /// <summary>
    /// Marks a zone of <paramref name="width"/> cells centred on the player's feet (the Farm tab's button), for the herb
    /// the ground there suits. Null when zones are capped or it would overlap another zone.
    /// </summary>
    public GrowZone? AddGrowZoneAtPlayer(int width = 8)
    {
        var center = PlayerCenter;
        var feet = (int)MathF.Floor(Player.Motion.Position.Y + Player.Motion.Size.Y - 0.05f);
        width = Math.Clamp(width, GrowZone.MinimumWidth, GrowZone.MaximumWidth);
        var left = Math.Clamp((int)MathF.Floor(center.X) - width / 2, 0, World.Width - width);
        return AddGrowZone(new GridPoint(left, feet), width, HerbForGround(new GridPoint((int)MathF.Floor(center.X), feet)));
    }

    public GrowZone? AddGrowZone(GridPoint left, int width, TileId herb)
    {
        if (_growZones.Count >= MaxGrowZones || !World.Contains(left))
            return null;
        var zone = new GrowZone(left.X, left.Y, Math.Min(width, World.Width - left.X), herb);
        if (zone.Cells.Any(cell => GrowZoneAt(cell) is not null))
            return null;
        _growZones.Add(zone);
        return zone;
    }

    public bool RemoveGrowZone(GrowZone zone) => _growZones.Remove(zone);

    /// <summary>Resizes a zone unless the new cells would run into another zone or off the map.</summary>
    public bool ResizeGrowZone(GrowZone zone, int width)
    {
        width = Math.Clamp(width, GrowZone.MinimumWidth, Math.Min(GrowZone.MaximumWidth, World.Width - zone.X));
        if (Enumerable.Range(zone.X, width).Any(x => _growZones.Any(other => other != zone && other.Contains(new GridPoint(x, zone.Y)))))
            return false;
        zone.Resize(width);
        return true;
    }

    /// <summary>Whether a zone cell is empty and its ground takes the zone's herb (its soil, or a planter), out of liquid.</summary>
    public bool CanPlant(GrowZone zone, GridPoint cell)
    {
        if (!World.Contains(cell) || World.TileAt(cell.X, cell.Y) != TileId.Air || World.GetCell(cell).LiquidUnits > 0 ||
            TilePhysics.HerbFor(zone.Herb) is not { } herb)
            return false;
        var below = World.TileAt(cell.X, cell.Y + 1);
        return Array.IndexOf(herb.Soils, below) >= 0 || FloraParity.IsPlanter(below);
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
    /// The Farm job giver: store a full load, else harvest a blooming herb in a zone near home, else plant an empty zone
    /// cell with seeds from the pouch (fetched from a chest near home when the pouch has none).
    /// </summary>
    private bool TryFarm(ColonistAgent agent, Vector2 center, float deltaSeconds)
    {
        void Move(Vector2 to) => StepColonistToward(agent, center, to, deltaSeconds, 1f);
        if (!agent.Carried.IsEmpty)
        {
            if (DeliverCarried(agent, center, Move))
                return true;
            SpawnGroundItem(agent.Carried.ItemId, agent.Carried.Count, center, agent.Carried.Calibration);
            agent.Carried = default;
        }

        var zones = _growZones.Where(zone => Vector2.Distance(new Vector2(zone.X + zone.Width / 2f, zone.Y + 0.5f), agent.HomePosition) <= OperateRadius).ToArray();
        var claimed = _colonists.Where(other => !ReferenceEquals(other, agent) && other.IsAlive && other.FarmTarget is not null)
            .Select(other => other.FarmTarget!.Value).ToHashSet();
        var harvest = zones.SelectMany(zone => zone.Cells)
            .Where(cell => !claimed.Contains(cell) && TilePhysics.IsHerb(World.TileAt(cell.X, cell.Y)) && IsHerbBlooming(cell))
            .OrderBy(cell => Vector2.DistanceSquared(new Vector2(cell.X + 0.5f, cell.Y + 0.5f), center))
            .Cast<GridPoint?>().FirstOrDefault();

        // A full load, or herbs in hand and nothing left to harvest: take them to storage, one stack at a time.
        var produce = Produce(agent.Pouch);
        if (produce > 0 && (produce >= HarvestLoad || harvest is null))
            return UnloadPouch(agent);

        if (harvest is { } ripe)
            return WorkCell(agent, center, deltaSeconds, ripe, $"Harvesting {Tiles[World.TileAt(ripe.X, ripe.Y)].Name.ToLowerInvariant()}", Move, () => Harvest(agent, ripe));

        // Plant: the nearest empty cell whose herb the farmer has seeds for, or can fetch them for.
        foreach (var (zone, cell) in zones.SelectMany(zone => zone.Cells.Select(cell => (zone, cell)))
                     .Where(pair => !claimed.Contains(pair.cell) && CanPlant(pair.zone, pair.cell))
                     .OrderBy(pair => Vector2.DistanceSquared(new Vector2(pair.cell.X + 0.5f, pair.cell.Y + 0.5f), center)))
        {
            var seeds = TilePhysics.HerbFor(zone.Herb)!.Seeds;
            if (agent.Pouch.GetValueOrDefault(seeds) > 0)
                return WorkCell(agent, center, deltaSeconds, cell, $"Planting {Items[seeds].Name.ToLowerInvariant()}", Move, () =>
                {
                    if (!CanPlant(zone, cell) || agent.Pouch.GetValueOrDefault(seeds) <= 0)
                        return;
                    Unpouch(agent, seeds, 1);
                    World.SetTile(cell, zone.Herb);
                    Flags.Increment("colony.planted");
                });
            if (NearestHolding(agent, seeds) is { } chestCell)
            {
                agent.FarmTarget = null;
                agent.Activity = $"Fetching {Items[seeds].Name.ToLowerInvariant()}";
                if (Reaches(center, chestCell))
                {
                    var chest = GetContainer(chestCell)!;
                    var take = Math.Min(SeedsKept, chest.Count(seeds));
                    if (take > 0 && chest.TryConsume(seeds, take))
                        agent.Pouch[seeds] = agent.Pouch.GetValueOrDefault(seeds) + take;
                }
                else
                    Move(new Vector2(chestCell.X + 0.5f, chestCell.Y + 0.5f));
                return true;
            }
        }

        agent.FarmTarget = null;
        agent.FarmProgress = 0f;
        return false;
    }

    /// <summary>Walks to a zone cell and works it for <see cref="FarmSeconds"/>, then does <paramref name="finish"/>.</summary>
    private bool WorkCell(ColonistAgent agent, Vector2 center, float deltaSeconds, GridPoint cell, string activity, Action<Vector2> move, Action finish)
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
        if (agent.FarmProgress < FarmSeconds(agent.ColonistStats) / ResearchFarmSpeed)
            return true;
        agent.FarmProgress = 0f;
        agent.FarmTarget = null;
        finish();
        return true;
    }

    /// <summary>A farmer's harvest: the herb (a second one sometimes, with Plants skill) and 1–3 seeds into the pouch.</summary>
    private void Harvest(ColonistAgent agent, GridPoint cell)
    {
        if (TilePhysics.HerbFor(World.TileAt(cell.X, cell.Y)) is not { } herb || !IsHerbBlooming(cell))
            return;
        World.SetTile(cell, TileId.Air);
        var herbs = 1 + (FarmRandom.NextDouble() < ExtraHerbChance(agent.ColonistStats) ? 1 : 0);
        agent.Pouch[herb.Herb] = agent.Pouch.GetValueOrDefault(herb.Herb) + herbs;
        agent.Pouch[herb.Seeds] = agent.Pouch.GetValueOrDefault(herb.Seeds) + FarmRandom.Next(1, 4);
        Flags.Increment("colony.harvested", herbs);
    }

    private static void Unpouch(ColonistAgent agent, ItemId item, int count)
    {
        var left = agent.Pouch.GetValueOrDefault(item) - count;
        if (left > 0)
            agent.Pouch[item] = left;
        else
            agent.Pouch.Remove(item);
    }

    /// <summary>The nearest chest near home holding <paramref name="item"/>.</summary>
    private GridPoint? NearestHolding(ColonistAgent agent, ItemId item) =>
        StorageCellsNear(agent.HomePosition)
            .Where(cell => GetContainer(cell)?.Count(item) > 0)
            .OrderBy(cell => Vector2.DistanceSquared(new Vector2(cell.X + 0.5f, cell.Y + 0.5f), agent.Motion.Position))
            .Cast<GridPoint?>().FirstOrDefault();

    /// <summary>Sets a farmer's seeds and herbs down for the haulers (taken off Farm, or dead).</summary>
    private void DropPouch(ColonistAgent agent, Vector2 at)
    {
        foreach (var (item, count) in agent.Pouch)
            SpawnGroundItem(item, count, at);
        agent.Pouch.Clear();
        agent.FarmTarget = null;
        agent.FarmProgress = 0f;
    }

    private GrowZoneSnapshot[] CaptureGrowZones() => _growZones.Select(zone => zone.Capture()).ToArray();

    private void RestoreGrowZones(GrowZoneSnapshot[]? saved)
    {
        _growZones.Clear();
        foreach (var entry in saved ?? [])
            if (entry is not null && World.Contains(entry.X, entry.Y))
                AddGrowZone(new GridPoint(entry.X, entry.Y), entry.Width, entry.Herb);
    }
}
