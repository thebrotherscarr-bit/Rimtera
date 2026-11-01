using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>A loose block (sand, gravel, silt) falling down its column; it re-settles as a tile when it lands.</summary>
public sealed class FallingBlock
{
    internal FallingBlock(TileId tile, int x, float y)
    {
        Tile = tile;
        X = x;
        Y = y;
    }

    public TileId Tile { get; }
    /// <summary>Column (cell x).</summary>
    public int X { get; }
    /// <summary>Top edge in world units (cells); the block is one cell tall.</summary>
    public float Y { get; internal set; }
    public float VelocityY { get; internal set; }
}

/// <summary>A thrown bomb, dynamite stick or sticky bomb counting down its fuse.</summary>
public sealed class LiveExplosive
{
    internal LiveExplosive(long id, ItemId kind, Vector2 position, Vector2 velocity, float fuse, float radius, int power, float damage, bool sticky)
    {
        Id = id;
        Kind = kind;
        Position = position;
        Velocity = velocity;
        FuseSeconds = fuse;
        Radius = radius;
        Power = power;
        Damage = damage;
        Sticky = sticky;
    }

    public long Id { get; }
    public ItemId Kind { get; }
    /// <summary>Centre in world units.</summary>
    public Vector2 Position { get; internal set; }
    public Vector2 Velocity { get; internal set; }
    public float FuseSeconds { get; internal set; }
    public float Radius { get; }
    /// <summary>Like drill power: tiles needing a stronger tool survive the blast.</summary>
    public int Power { get; }
    public float Damage { get; }
    public bool Sticky { get; }
    public bool Stuck { get; internal set; }
}

/// <summary>Batch A static content that other tables reference (kept out of GameSimulation to avoid static-init cycles).</summary>
public static class PhysicsContent
{
    /// <summary>The Precursor Dungeon's warden: appears when the dungeon is entered before the Scout Mech falls.</summary>
    public static EnemyArchetype DungeonGuardian { get; } =
        new(EnemyKind.DungeonGuardian, "Dungeon Guardian", new(2.2f, 2.2f), 99_999f, 400f, true)
        {
            MoveSpeedMultiplier = 2.6f,
            ContactCooldown = 0.4f,
            AttackRange = 0f
        };

    /// <summary>The boss whose defeat opens the Precursor Dungeon: the Scout Mech, mid-way through the easy tier (G12).</summary>
    public const EnemyKind DungeonGateBoss = EnemyKind.ScoutMech;
}

/// <summary>
/// Batch A world physics: falling blocks and plant support, liquid merges, block shaping, platforms, ropes,
/// trees, herbs, torches, wiring (switches, levers, plates, timers, gates, actuators, doors, lamps, traps,
/// teleporters), explosives and the dungeon guardian rule.
/// </summary>
public sealed partial class GameSimulation
{
    private readonly WireMap _wires = new();
    private readonly List<FallingBlock> _falling = new();
    private readonly List<LiveExplosive> _explosives = new();
    private readonly HashSet<GridPoint> _supportQueue = new();
    private readonly List<GridPoint> _physicsDirty = new();
    private readonly Dictionary<GridPoint, float> _activeTimers = new();
    private HashSet<GridPoint> _pressedPlates = new();
    private readonly Dictionary<GridPoint, int> _shakenTrees = new();
    private Random? _physicsRandomField;
    private float _guardianTimer;
    private float _guardianAbsence;
    private bool _physicsAttached;
    private int _activeWireLayer = 1;

    private Random PhysicsRandom => _physicsRandomField ??= new Random(Seed ^ 0x5A9D1E77);

    /// <summary>Wire layers for the renderer's wire view and tools.</summary>
    public WireMap Wires => _wires;
    /// <summary>Loose blocks currently falling (drawn between cells).</summary>
    public IReadOnlyList<FallingBlock> FallingBlocks => _falling;
    /// <summary>Thrown explosives in flight or stuck, with their fuses.</summary>
    public IReadOnlyList<LiveExplosive> Explosives => _explosives;
    /// <summary>Wire layer (1–3) the wire tool lays and cuts.</summary>
    public int ActiveWireLayer
    {
        get => _activeWireLayer;
        set => _activeWireLayer = Math.Clamp(value, 1, WireMap.LayerCount);
    }

    /// <summary>Cycles the wire tool's layer 1 → 2 → 3 → 1 and returns the new layer.</summary>
    public int CycleWireLayer() => ActiveWireLayer = ActiveWireLayer % WireMap.LayerCount + 1;

    /// <summary>Light a tile type emits (renderer helper; see <see cref="TilePhysics.GetTileLight"/>).</summary>
    public static TileLight? GetTileLight(TileId tile) => TilePhysics.GetTileLight(tile);

    /// <summary>Light a placed cell emits, honouring lamps switched off by wiring and unripe herbs.</summary>
    public TileLight? GetCellLight(int x, int y) => World.Contains(x, y) ? TilePhysics.GetCellLight(World.GetCell(x, y)) : null;

    /// <summary>Current conditions for herb blooming.</summary>
    public HerbConditions HerbConditions => new(!IsNight, Weather.Rain, Weather.Snow, DayProgress, MoonPhase);

    /// <summary>Current conditions for the flora rules (random-update growth, tree shaking).</summary>
    public FloraConditions FloraConditions => new(!IsNight, DayProgress, Weather.Rain > 0f, MoonPhase);

    /// <summary>How far through the daylight hours the clock is (0 at 06:00, 1 at 18:00; 0 at night).</summary>
    private float DayProgress => IsNight ? 0f : Math.Clamp((ClockHour - 6f) / 12f, 0f, 1f);

    /// <summary>True when the herb at <paramref name="cell"/> is mature and in bloom right now.</summary>
    public bool IsHerbBlooming(GridPoint cell) => World.Contains(cell) &&
        TilePhysics.IsBlooming(World.GetCell(cell), HerbConditions, cell.Y > FloraParity.SurfaceRow(World.Height));

    /// <summary>True when the player stands in front of Precursor Dungeon walls.</summary>
    public bool PlayerInDungeon => World.Contains(PlayerCell()) && World.GetCell(PlayerCell()).WallId == WallIds.Dungeon;

    // ------------------------------------------------------------------ lifecycle hooks

    /// <summary>Called once from the constructor after the world exists.</summary>
    private void InitPhysics()
    {
        if (_physicsAttached)
            return;
        World.CellChanged += RememberHerbBeforeBreak;
        World.CellChanged += OnPhysicsCellChanged;
        _physicsAttached = true;
        _ticker.Conditions ??= () => FloraConditions; // flora parity: day, rain and moon for growth and blooms
        // Trap wiring laid by a fresh generation (loaded worlds restore wires from their flags).
        if (Features is { } features)
            foreach (var (cell, layer) in features.TrapWires)
                PlaceWire(cell, layer);
    }

    private void OnPhysicsCellChanged(int x, int y, in WorldCell before, in WorldCell after)
    {
        // Liquid reaching a torch knocks it out.
        if (after.LiquidUnits > 0 && after.TileId is TileId.WallTorch or TileId.StandingTorch)
            _supportQueue.Add(new GridPoint(x, y));
        // A removed background wall drops whatever was hung on it.
        if (before.WallId != WallIds.None && after.WallId == WallIds.None && after.TileId != TileId.Air)
            _supportQueue.Add(new GridPoint(x, y));
        if (before.TileId == after.TileId)
            return;
        // Only lost support wakes neighbours: a holding block removed (or a rope, vine, cactus or trunk cut).
        var lostHold = CellHolds(before) && !CellHolds(after);
        var cutHanger = before.TileId is TileId.Rope or TileId.Vine or TileId.Cactus || TilePhysics.IsTrunk(before.TileId);
        if (!lostHold && !cutHanger)
            return;
        _supportQueue.Add(new GridPoint(x, y - 1));
        _supportQueue.Add(new GridPoint(x, y + 1));
        if (lostHold)
        {
            // Side-mounted torches, lights and paintings lean on their neighbours too.
            _supportQueue.Add(new GridPoint(x - 1, y));
            _supportQueue.Add(new GridPoint(x + 1, y));
        }
    }

    /// <summary>Per-tick Batch A update (one hook in <see cref="Tick"/>).</summary>
    private void UpdatePhysics(float deltaSeconds)
    {
        InitPhysics();
        if (_liquids.MergedCells.Count > 0)
        {
            _physicsDirty.AddRange(_liquids.MergedCells);
            _liquids.MergedCells.Clear();
        }
        ProcessSupport();
        UpdateTileDamageHealing(deltaSeconds);
        UpdateFallingBlocks(deltaSeconds);
        UpdateExplosives(deltaSeconds);
        UpdatePressurePlates();
        UpdateTimers(deltaSeconds);
        UpdateDungeonGuardian(deltaSeconds);
        FlushPhysicsReframe();
    }

    private void FlushPhysicsReframe()
    {
        if (_physicsDirty.Count == 0)
            return;
        _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, _physicsDirty));
        _physicsDirty.Clear();
    }

    // ------------------------------------------------------------------ support, falling blocks

    /// <summary>Checks queued cells: loose blocks over open space start falling; unsupported plants, vines, ropes and trees collapse.</summary>
    private void ProcessSupport()
    {
        for (var round = 0; round < 4 && _supportQueue.Count > 0; round++)
        {
            var batch = _supportQueue.ToArray();
            _supportQueue.Clear();
            foreach (var point in batch)
                CheckSupport(point);
        }
    }

    private bool CellHolds(in WorldCell cell) =>
        cell.TileId != TileId.Air && ((Tiles[cell.TileId].IsSolid && !cell.IsInactive) || TilePhysics.IsPlatform(cell.TileId));

    private bool HoldsUp(int x, int y)
    {
        if (!World.Contains(x, y))
            return true;
        var cell = World.GetCell(x, y);
        return (Tiles[cell.TileId].IsSolid && !cell.IsInactive) || TilePhysics.IsPlatform(cell.TileId);
    }

    private void CheckSupport(GridPoint point)
    {
        if (!World.Contains(point))
            return;
        var cell = World.GetCell(point);
        var tile = cell.TileId;
        if (tile == TileId.Air)
            return;
        var (x, y) = (point.X, point.Y);
        if (TilePhysics.Falls(tile) && !cell.IsInactive)
        {
            if (World.Contains(x, y + 1) && !HoldsUp(x, y + 1))
                StartFalling(point, tile);
            return;
        }
        var below = World.TileAt(x, y + 1);
        var above = World.TileAt(x, y - 1);
        var unsupported = tile switch
        {
            TileId.Sapling => !HoldsUp(x, y + 1),
            >= TileId.HerbSunpetal and <= TileId.HerbSporecap => !HoldsUp(x, y + 1) && !FloraParity.IsPlanter(below),
            TileId.GrassPlant or TileId.TallGrassPlant => !HoldsUp(x, y + 1),
            TileId.Cactus => below != TileId.Cactus && !HoldsUp(x, y + 1),
            TileId.Vine => above != TileId.Vine && !(Tiles[above].IsSolid && World.Contains(x, y - 1)),
            TileId.Rope => above != TileId.Rope && !HoldsUp(x, y - 1),
            TileId.TreeTrunk or TileId.PineTrunk or TileId.PalmTrunk => !TilePhysics.IsTrunk(below) && !HoldsUp(x, y + 1),
            _ when ContentFillFlora.IsTrunk(tile) => !TilePhysics.IsTrunk(below) && !HoldsUp(x, y + 1),
            TileId.PressurePlate or TileId.TeleporterPad => !HoldsUp(x, y + 1),
            TileId.StandingTorch => !HoldsUp(x, y + 1) || cell.LiquidUnits > 0,
            TileId.WallTorch => cell.LiquidUnits > 0 || !(cell.WallId != WallIds.None || HoldsUp(x - 1, y) || HoldsUp(x + 1, y) || HoldsUp(x, y + 1)),
            _ => !FurnitureHeld(tile, x, y, cell)
        };
        if (!unsupported)
            return;
        if (tile is TileId.WallTorch or TileId.StandingTorch || ContentTiles.TryGet(tile, out var furniture) && furniture.Furniture)
        {
            // Furniture and torches pop off as items when their support goes.
            World.SetTile(point, TileId.Air);
            _physicsDirty.Add(point);
            HandleBrokenTile(point, tile);
            return;
        }
        if (TilePhysics.IsTrunk(tile))
        {
            foreach (var part in TilePhysics.CollectTreeAbove(World, point))
                if (TilePhysics.IsTreePart(World.TileAt(part.X, part.Y)))
                    ClearPhysicsCell(part);
            return;
        }
        ClearPhysicsCell(point);
        if (TilePhysics.IsHerb(tile))
            HandleBrokenTile(point, tile); // a herb that loses its ground drops like a harvested one
    }

    /// <summary>
    /// True when a furniture cell still has what it needs: wall-hung pieces a wall or a neighbouring block,
    /// floor pieces something solid (or a platform) under them; stacked door cells hold each other. Non-furniture is always held.
    /// </summary>
    private bool FurnitureHeld(TileId tile, int x, int y, in WorldCell cell)
    {
        if (!ContentTiles.TryGet(tile, out var definition) || !definition.Furniture || definition.Solid)
            return true;
        if (ContentTiles.ToggledDoor(tile) is not null)
            return true; // doors keep their own frame rules
        if (tile is TileId.CeilingLight or TileId.PaintingPlanet or TileId.ColonyBanner)
            return cell.WallId != WallIds.None || HoldsUp(x - 1, y) || HoldsUp(x + 1, y) || HoldsUp(x, y - 1) || HoldsUp(x, y + 1);
        var below = World.Contains(x, y + 1) ? World.TileAt(x, y + 1) : TileId.Bedrock;
        return HoldsUp(x, y + 1) || below == tile || ContentTiles.TryGet(below, out var under) && under.Furniture;
    }

    private void ClearPhysicsCell(GridPoint point)
    {
        World.SetTile(point, TileId.Air);
        _physicsDirty.Add(point);
    }

    private void StartFalling(GridPoint point, TileId tile)
    {
        World.SetTile(point, TileId.Air);
        _physicsDirty.Add(point);
        _falling.Add(new FallingBlock(tile, point.X, point.Y));
    }

    private void UpdateFallingBlocks(float deltaSeconds)
    {
        for (var i = _falling.Count - 1; i >= 0; i--)
        {
            var block = _falling[i];
            block.VelocityY = Math.Min(block.VelocityY + 30f * deltaSeconds, 20f);
            var next = block.Y + block.VelocityY * deltaSeconds;
            var row = (int)MathF.Floor(next);
            // Land when the cell under the block's new bottom holds it up (or another block settled there).
            if (row + 1 >= World.Height || HoldsUp(block.X, row + 1))
            {
                Settle(block, Math.Clamp(row, 0, World.Height - 2));
                _falling.RemoveAt(i);
                continue;
            }
            block.Y = next;
            // A falling block hurts whoever it lands on.
            var box = new Vector2(block.X, block.Y);
            if (Overlaps(Player.Motion, box, Vector2.One) && block.VelocityY > 6f)
                Player.TakeDamage(4f, DamageKind.Crush);
            if (block.VelocityY > 6f)
                foreach (var enemy in _enemies)
                    if (enemy.IsAlive && enemy.CanBeHitBy(FallingBlockAttacker) && Overlaps(enemy.Motion, box, Vector2.One))
                    {
                        enemy.TakeDamage(FallingBlockDamage);
                        enemy.MarkHitBy(FallingBlockAttacker, 0.5f);
                    }
        }
    }

    /// <summary>Hit-immunity key and damage for loose blocks landing on creatures.</summary>
    private const long FallingBlockAttacker = -71;
    private const float FallingBlockDamage = 10f;

    private void Settle(FallingBlock block, int row)
    {
        // Climb back up past anything solid that filled the column while it fell.
        while (row > 0 && HoldsUp(block.X, row))
            row--;
        var point = new GridPoint(block.X, row);
        if (!World.Contains(point))
            return;
        var cell = World.GetCell(point);
        if (cell.TileId != TileId.Air && Tiles[cell.TileId].IsSolid)
            return;
        // Non-solid props in the way are crushed; liquid is displaced.
        cell.TileId = block.Tile;
        cell.LiquidUnits = 0;
        cell.LiquidKind = LiquidKind.None;
        cell.Damage = 0;
        cell.FrameMask = 0;
        cell.Flags = 0;
        World.SetCell(point, cell);
        _physicsDirty.Add(point);
    }

    private static bool Overlaps(PawnMotionState motion, Vector2 origin, Vector2 size) =>
        motion.Position.X < origin.X + size.X && motion.Position.X + motion.Size.X > origin.X &&
        motion.Position.Y < origin.Y + size.Y && motion.Position.Y + motion.Size.Y > origin.Y;

    // ------------------------------------------------------------------ placement

    /// <summary>Places Batch A tiles (platforms, ropes, ladders, plants, torches, wiring devices). False for other items.</summary>
    private bool TryPlacePhysicsTile(ItemDefinition item, GridPoint point)
    {
        ClearPlantForPlacement(item, point);
        if (TrySwapBlock(item, point) || BlockPlacementRefused(item, point))
            return true;
        var tile = item.PlacedTile;
        if (TilePhysics.Falls(tile) && World.Contains(point) && World.Contains(point.X, point.Y + 1) && !HoldsUp(point.X, point.Y + 1) &&
            World.TileAt(point.X, point.Y) == TileId.Air && Player.ActionCooldownTicks == 0 && IsInReach(point))
        {
            // Loose blocks placed over open space drop straight away.
            if (Player.Inventory.TryConsume(item.Id))
            {
                _falling.Add(new FallingBlock(tile, point.X, point.Y));
                SetPlayerCooldown(6);
            }
            return true;
        }
        if (!IsPhysicsPlaceable(tile))
            return false;
        if (Player.ActionCooldownTicks > 0)
            return true;
        if (tile == TileId.Rope && World.Contains(point) && World.GetCell(point).TileId == TileId.Rope)
        {
            // Using a rope on a rope extends it downward from its lowest cell.
            var bottom = point;
            while (World.Contains(bottom.X, bottom.Y + 1) && World.TileAt(bottom.X, bottom.Y + 1) == TileId.Rope)
                bottom = new GridPoint(bottom.X, bottom.Y + 1);
            point = new GridPoint(bottom.X, bottom.Y + 1);
            if (!World.Contains(point) || World.TileAt(point.X, point.Y) != TileId.Air)
            {
                PostMessage("The rope has nowhere to hang.");
                return true;
            }
        }
        else if (!IsInReach(point))
        {
            PostMessage("That placement is out of reach.");
            return true;
        }
        if (World.GetCell(point).TileId != TileId.Air)
        {
            PostMessage("Cannot place: occupied.");
            return true;
        }
        var cell = World.GetCell(point);
        if (Tiles[tile].IsSolid && (Overlaps(Player.Motion, new Vector2(point.X, point.Y), Vector2.One) ||
                                    _colonists.Any(agent => Overlaps(agent.Motion, new Vector2(point.X, point.Y), Vector2.One))))
        {
            PostMessage("Cannot place: something is in the way.");
            return true;
        }
        if (!PhysicsAnchorOk(tile, point, cell))
        {
            PostMessage($"Cannot place the {Tiles[tile].Name.ToLowerInvariant()} there.");
            return true;
        }
        if (!Player.Inventory.TryConsume(item.Id))
            return true;
        World.SetTile(point, tile);
        var placed = World.GetCell(point);
        placed.State = tile switch
        {
            TileId.GateNot => 4,
            TileId.DartTrap => (byte)(Player.Motion.Facing < 0 ? 1 : 0),
            _ => 0
        };
        World.SetCell(point, placed);
        _physicsDirty.Add(point);
        FlushPhysicsReframe();
        SetPlayerCooldown(6);
        PostMessage($"Placed {item.Name}.");
        return true;
    }

    private static bool IsPhysicsPlaceable(TileId tile) =>
        tile is TileId.WallTorch or TileId.StandingTorch or TileId.Sapling ||
        (tile >= TileId.PlankPlatform && tile <= TileId.TeleporterPad && !TilePhysics.IsTreePart(tile) && tile != TileId.Vine && tile != TileId.Cactus);

    private bool PhysicsAnchorOk(TileId tile, GridPoint point, in WorldCell cell)
    {
        var (x, y) = (point.X, point.Y);
        bool Solid(int cx, int cy) => World.Contains(cx, cy) && Tiles[World.TileAt(cx, cy)].IsSolid;
        var wall = cell.WallId != WallIds.None;
        var adjacentSolid = Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1);
        if (TilePhysics.HerbFor(tile) is { } herb)
            return (Array.IndexOf(herb.Soils, World.TileAt(x, y + 1)) >= 0 || FloraParity.IsPlanter(World.TileAt(x, y + 1))) &&
                   cell.LiquidUnits == 0;
        return tile switch
        {
            // Saplings take only tree ground (grass, snow, sand for palms), not bare soil or mud (ref. IsTileTypeFitForTree).
            TileId.Sapling => FloraParity.SaplingGroundFits(World.TileAt(x, y + 1)) && TilePhysics.TreeFor(World.TileAt(x, y + 1)) is not null &&
                              Solid(x, y + 1) && cell.LiquidUnits == 0,
            TileId.Rope => World.TileAt(x, y - 1) == TileId.Rope || Solid(x, y - 1),
            TileId.Ladder => Solid(x, y + 1) || Solid(x, y - 1) || World.TileAt(x, y + 1) == TileId.Ladder ||
                             World.TileAt(x, y - 1) == TileId.Ladder || wall,
            TileId.PlankPlatform or TileId.MetalPlatform => adjacentSolid || wall || TilePhysics.IsPlatform(World.TileAt(x - 1, y)) ||
                                                            TilePhysics.IsPlatform(World.TileAt(x + 1, y)),
            TileId.WallTorch => wall || Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y + 1),
            TileId.StandingTorch or TileId.PressurePlate or TileId.TeleporterPad => Solid(x, y + 1),
            TileId.DartTrap or TileId.ExplosiveTrap => true,
            _ => adjacentSolid || wall
        };
    }

    // ------------------------------------------------------------------ item uses

    /// <summary>Batch A utility uses (hammer, wire tools, actuators, explosives). False for other items.</summary>
    private bool UsePhysicsItem(ItemDefinition item, GridPoint point)
    {
        switch (item.Id)
        {
            case ItemId.ShapingHammer:
                if (World.Contains(point) && IsInReach(point) && World.GetCell(point).TileId == TileId.Air && World.GetCell(point).WallId != WallIds.None)
                    HammerWall(point, item.ToolPower);
                else
                    HammerShape(point);
                SetPlayerCooldown(Math.Max(8, item.UseCooldownTicks));
                return true;
            case ItemId.WireTool:
            {
                if (!World.Contains(point) || !IsInReach(point))
                    return false;
                var layer = ActiveWireLayer;
                if (_wires.Has(point, layer))
                {
                    _wires.Remove(point, layer);
                    Player.Inventory.Add(ItemId.WireSpool, 1);
                    PostMessage($"Cut the layer-{layer} wire.");
                }
                else if (Player.Inventory.Count(ItemId.WireSpool) > 0)
                {
                    Player.Inventory.TryConsume(ItemId.WireSpool);
                    _wires.Place(point, layer);
                    TrackTimer(point);
                    PostMessage($"Laid layer-{layer} wire.");
                }
                else
                    return false; // No spool: the tool keeps its remote door trigger.
                SetPlayerCooldown(3);
                return true;
            }
            case ItemId.WireCutter:
            {
                if (!IsInReach(point))
                {
                    PostMessage("That is out of reach.");
                    return true;
                }
                var removed = _wires.RemoveAll(point);
                if (removed > 0)
                    Player.Inventory.Add(ItemId.WireSpool, removed);
                var cell = World.GetCell(point);
                if (cell.HasActuator)
                {
                    cell.HasActuator = false;
                    cell.IsInactive = false;
                    World.SetCell(point, cell);
                    Player.Inventory.Add(ItemId.Actuator, 1);
                    _physicsDirty.Add(point);
                    removed++;
                }
                PostMessage(removed > 0 ? "Snipped the wiring." : "No wiring there.");
                SetPlayerCooldown(4);
                return true;
            }
            case ItemId.Actuator:
            {
                if (!IsInReach(point))
                {
                    PostMessage("That is out of reach.");
                    return true;
                }
                var cell = World.GetCell(point);
                if (cell.TileId == TileId.Air || cell.HasActuator || !Tiles[cell.TileId].IsSolid && !TilePhysics.IsPlatform(cell.TileId))
                {
                    PostMessage("Actuators fit into solid blocks without one.");
                    return true;
                }
                Player.Inventory.TryConsume(ItemId.Actuator);
                cell.HasActuator = true;
                World.SetCell(point, cell);
                PostMessage("Fitted an actuator.");
                SetPlayerCooldown(6);
                return true;
            }
            case ItemId.Bomb or ItemId.Dynamite or ItemId.StickyBomb:
            {
                var direction = new Vector2(point.X + 0.5f, point.Y + 0.5f) - PlayerCenter;
                direction = direction.LengthSquared() < 0.01f ? new Vector2(Player.Motion.Facing, 0f) : Vector2.Normalize(direction);
                if (!Player.Inventory.TryConsume(item.Id))
                    return true;
                ThrowExplosive(item.Id, PlayerCenter, direction * 9f + new Vector2(0f, -2.5f));
                PostMessage($"Threw a {item.Name.ToLowerInvariant()}!");
                SetPlayerCooldown(20);
                return true;
            }
        }
        return false;
    }

    private void HammerShape(GridPoint point)
    {
        if (!IsInReach(point))
        {
            PostMessage("That is out of reach.");
            return;
        }
        var cell = World.GetCell(point);
        if (!TilePhysics.IsShapeable(cell.TileId, Tiles))
        {
            PostMessage("The hammer only shapes plain blocks and platforms.");
            return;
        }
        var next = TilePhysics.NextShape(cell.Shape, TilePhysics.IsPlatform(cell.TileId));
        SetShape(point, next);
        PostMessage(next == TileShape.Full ? "Hammered the block flat." : $"Hammered the block into a {DescribeShape(next)}.");
    }

    /// <summary>Sets a cell's block shape (tools, tests, generation helpers).</summary>
    public bool SetShape(GridPoint point, TileShape shape)
    {
        if (!World.Contains(point) || !TilePhysics.IsShapeable(World.GetCell(point).TileId, Tiles))
            return false;
        var cell = World.GetCell(point);
        cell.Shape = shape;
        World.SetCell(point, cell);
        _physicsDirty.Add(point);
        FlushPhysicsReframe();
        return true;
    }

    private static string DescribeShape(TileShape shape) => shape switch
    {
        TileShape.Half => "half block",
        TileShape.SlopeUpRight or TileShape.SlopeUpLeft => "slope",
        TileShape.CeilingRight or TileShape.CeilingLeft => "ceiling slope",
        _ => "block"
    };

    // ------------------------------------------------------------------ breaking

    /// <summary>Batch A break rules: whole trees, cactus columns, rope runs, herbs and pots. True when it handled the drops.</summary>
    private bool HandleBrokenPhysicsTile(GridPoint point, TileId broken)
    {
        _activeTimers.Remove(point);
        if (HandleBrokenWorldGenTile(point, broken))
            return true;
        if (TilePhysics.IsTrunk(broken))
        {
            FellTree(point, broken);
            return true;
        }
        if (broken is TileId.Cactus or TileId.Rope)
        {
            var count = 1;
            var step = broken == TileId.Cactus ? -1 : 1;
            for (var y = point.Y + step; World.Contains(point.X, y) && World.TileAt(point.X, y) == broken; y += step)
            {
                ClearPhysicsCell(new GridPoint(point.X, y));
                count++;
            }
            BreakDrop(point, broken == TileId.Cactus ? ItemId.CactusChunk : ItemId.ClimbingRope, count);
            FlushPhysicsReframe();
            return true;
        }
        if (TilePhysics.HerbFor(broken) is { } herb)
        {
            // The cell is already air; bloom state was read from the event before the break, so use the remembered state.
            var mature = _lastBrokenHerbState.Point == point && (_lastBrokenHerbState.State & 1) != 0;
            var blooming = mature && _lastBrokenHerbState.Blooming;
            if (blooming)
            {
                // ref. KillTile_GetItemDrops @8523–@8661: the herb, plus 1–3 seeds when it is in bloom.
                BreakDrop(point, herb.Herb, 1);
                BreakDrop(point, herb.Seeds, PhysicsRandom.Next(1, 4));
                PostMessage($"Harvested blooming {Tiles[broken].Name.ToLowerInvariant()}.");
            }
            else if (mature)
            {
                BreakDrop(point, herb.Herb, 1);
                PostMessage($"Harvested {Tiles[broken].Name.ToLowerInvariant()}.");
            }
            else
                PostMessage($"The {Tiles[broken].Name.ToLowerInvariant()} was not ready.");
            return true;
        }
        if (TilePhysics.IsPot(broken))
        {
            BreakPot(broken, point);
            return true;
        }
        return false;
    }

    /// <summary>A broken tile's drop: pops out of the cell as a ground item (see <see cref="HandleBrokenTile"/>).</summary>
    private void BreakDrop(GridPoint point, ItemId item, int count) => CollectOrDrop(item, count, new Vector2(point.X + 0.5f, point.Y + 0.5f));

    private (GridPoint Point, byte State, bool Blooming) _lastBrokenHerbState = (new GridPoint(-1, -1), 0, false);

    /// <summary>Remembers herb state as it breaks (the break clears the cell before the drop handler runs).</summary>
    private void RememberHerbBeforeBreak(int x, int y, in WorldCell before, in WorldCell after)
    {
        if (TilePhysics.IsHerb(before.TileId) && after.TileId == TileId.Air)
            _lastBrokenHerbState = (new GridPoint(x, y), before.State,
                TilePhysics.IsBlooming(before, HerbConditions, y > FloraParity.SurfaceRow(World.Height)));
    }

    private void BreakPot(TileId pot, GridPoint point)
    {
        var random = PhysicsRandom;
        var credits = pot == TileId.PotDungeon ? 10 + random.Next(30) : 2 + random.Next(12);
        BreakDrop(point, ItemId.Credits, credits);
        var roll = random.Next(10);
        var (item, count) = roll switch
        {
            0 => (ItemId.RestorativeTonic, 1),
            1 or 2 => (ItemId.WallTorch, 3 + random.Next(6)),
            3 or 4 => (ItemId.Arrow, 10 + random.Next(16)),
            5 => (ItemId.ClimbingRope, 5 + random.Next(8)),
            6 => (pot == TileId.PotDungeon ? ItemId.Dynamite : ItemId.Bomb, 1 + random.Next(2)),
            7 => (pot switch
            {
                TileId.PotFrozen => ItemId.FrostbellSeeds,
                TileId.PotJunk => ItemId.RainreedSeeds,
                TileId.PotMolten => ItemId.EmberrootSeeds,
                TileId.PotCanopy => ItemId.MoonglowSeeds,
                _ => ItemId.SunpetalSeeds
            }, 1 + random.Next(2)),
            _ => (ItemId.Credits, 0)
        };
        if (count > 0)
            BreakDrop(point, item, count);
        PostMessage(count > 0 ? $"The {Tiles[pot].Name.ToLowerInvariant()} shatters: {credits} credits and {count} {Items[item].Name.ToLowerInvariant()}."
            : $"The {Tiles[pot].Name.ToLowerInvariant()} shatters: {credits} credits.");
    }

    /// <summary>Loot for Batch A containers (cabins, dungeon strongboxes); empty for others.</summary>
    private static (ItemId Item, int Min, int Max)[] PhysicsContainerLoot(TileId tile) => tile switch
    {
        TileId.CabinChest =>
        [
            (ItemId.Credits, 15, 70), (ItemId.ClimbingRope, 10, 30), (ItemId.WallTorch, 6, 16), (ItemId.Bomb, 0, 4),
            (ItemId.RestorativeTonic, 0, 2), (ItemId.ShapingHammer, 0, 1), (ItemId.ClimbingClaws, 0, 1), (ItemId.GrappleHook, 0, 1),
            (ItemId.SunpetalSeeds, 0, 3)
        ],
        TileId.DungeonChest =>
        [
            (ItemId.Credits, 120, 420), (ItemId.Dynamite, 2, 6), (ItemId.PrecursorRelic, 0, 1), (ItemId.AncientPowerCell, 0, 1),
            // Tier-3 bars, not lumenite: the dungeon opens before the Fracture (Terraria's dungeon chests hold no hardmode bars).
            (ItemId.PyrociteBar, 0, 6), (ItemId.VoidEssence, 0, 3), (ItemId.TeleporterPad, 0, 2), (ItemId.Actuator, 0, 10)
        ],
        _ => WorldGenChestLoot(tile)
    };

    // ------------------------------------------------------------------ interaction

    /// <summary>Batch A interactions: switches, levers, timers, tree shaking and herb picking. False when nothing applies.</summary>
    private bool TryInteractPhysics(GridPoint target, out InteractionResult result)
    {
        result = null!;
        if (!World.Contains(target))
            return false;
        var cell = World.GetCell(target);
        switch (cell.TileId)
        {
            case TileId.Switch:
            {
                var triggered = PulseWires(target);
                result = new InteractionResult(WorldInteractionKind.None, $"Click. ({triggered} device{(triggered == 1 ? "" : "s")} responded.)", Cell: target);
                return true;
            }
            case TileId.Lever:
            {
                cell.State = (byte)(cell.State ^ 1);
                World.SetCell(target, cell);
                _physicsDirty.Add(target);
                var triggered = PulseWires(target);
                FlushPhysicsReframe();
                result = new InteractionResult(WorldInteractionKind.None, $"The lever clunks {((cell.State & 1) != 0 ? "down" : "up")}. ({triggered} responded.)",
                    Cell: target);
                return true;
            }
            case TileId.Timer1s or TileId.Timer3s or TileId.Timer5s:
            {
                var on = ToggleTimer(target);
                result = new InteractionResult(WorldInteractionKind.None, on ? "The timer starts ticking." : "The timer stops.", Cell: target);
                return true;
            }
        }
        if (TilePhysics.IsHerb(cell.TileId))
        {
            if ((cell.State & 1) == 0)
            {
                result = new InteractionResult(WorldInteractionKind.None, "The herb is still growing.", Cell: target);
                return true;
            }
            World.SetTile(target, TileId.Air);
            HandleBrokenTile(target, cell.TileId);
            result = new InteractionResult(WorldInteractionKind.None, LastMessage, Cell: target);
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ wiring

    private bool ToggleTimer(GridPoint point)
    {
        var cell = World.GetCell(point);
        cell.State = (byte)(cell.State ^ 1);
        World.SetCell(point, cell);
        _physicsDirty.Add(point);
        FlushPhysicsReframe();
        TrackTimer(point);
        return (cell.State & 1) != 0;
    }

    private void TrackTimer(GridPoint point)
    {
        if (!World.Contains(point))
            return;
        var cell = World.GetCell(point);
        if (TilePhysics.TimerPeriod(cell.TileId) > 0f && (cell.State & 1) != 0)
            _activeTimers.TryAdd(point, 0f);
        else
            _activeTimers.Remove(point);
    }

    private void UpdateTimers(float deltaSeconds)
    {
        if (_activeTimers.Count == 0)
            return;
        foreach (var point in _activeTimers.Keys.ToArray())
        {
            var cell = World.GetCell(point);
            var period = TilePhysics.TimerPeriod(cell.TileId);
            if (period <= 0f || (cell.State & 1) == 0)
            {
                _activeTimers.Remove(point);
                continue;
            }
            var clock = _activeTimers[point] + deltaSeconds;
            if (clock >= period)
            {
                clock -= period;
                _activeTimers[point] = clock;
                PulseWires(point);
            }
            else
                _activeTimers[point] = clock;
        }
    }

    /// <summary>Total timer pulses fired (diagnostics and tests).</summary>
    public long WirePulses { get; private set; }

    private void UpdatePressurePlates()
    {
        HashSet<GridPoint>? pressed = null;
        void Probe(PawnMotionState motion)
        {
            var left = (int)MathF.Floor(motion.Position.X);
            var right = (int)MathF.Floor(motion.Position.X + motion.Size.X - 0.001f);
            var row = (int)MathF.Floor(motion.Position.Y + motion.Size.Y - 0.001f);
            for (var x = left; x <= right; x++)
                if (World.TileAt(x, row, TileId.Air) == TileId.PressurePlate)
                    (pressed ??= new HashSet<GridPoint>()).Add(new GridPoint(x, row));
        }
        Probe(Player.Motion);
        foreach (var agent in _colonists)
            Probe(agent.Motion);
        foreach (var enemy in _enemies)
            if (enemy.IsAlive)
                Probe(enemy.Motion);
        foreach (var npc in _townNpcs)
            Probe(npc.Motion);
        if (pressed is null)
        {
            _pressedPlates.Clear();
            return;
        }
        foreach (var plate in pressed)
            if (!_pressedPlates.Contains(plate))
                PulseWires(plate);
        _pressedPlates = pressed;
    }

    /// <summary>
    /// Sends a pulse from <paramref name="source"/> along every wire layer in <paramref name="layerMask"/> present there.
    /// Each device on a network reacts once: doors and lamps toggle, actuated blocks flip solid/inactive, timers start
    /// or stop, gates update (inputs on layers 1 and 2, output on layer 3), dart traps fire, explosive traps blow and
    /// teleporter pads swap whoever stands on them. Returns the number of devices that reacted.
    /// </summary>
    public int PulseWires(GridPoint source, byte layerMask = 0b111)
    {
        var queue = new Queue<(GridPoint Source, int Layer)>();
        for (var layer = 1; layer <= WireMap.LayerCount; layer++)
            if ((layerMask & WireMap.Bit(layer)) != 0 && _wires.Has(source, layer))
                queue.Enqueue((source, layer));
        var reacted = 0;
        var budget = 128;
        while (queue.Count > 0 && budget-- > 0)
        {
            var (from, layer) = queue.Dequeue();
            WirePulses++;
            var pads = new List<GridPoint>();
            var handledDoors = new HashSet<GridPoint>();
            foreach (var cell in _wires.Network(from, layer))
                if (cell != from)
                    reacted += TriggerCell(cell, layer, queue, pads, handledDoors);
            if (pads.Count >= 2)
                Teleport(pads);
        }
        FlushPhysicsReframe();
        return reacted;
    }

    private int TriggerCell(GridPoint point, int layer, Queue<(GridPoint, int)> queue, List<GridPoint> pads, HashSet<GridPoint> handledDoors)
    {
        if (!World.Contains(point))
            return 0;
        var cell = World.GetCell(point);
        var tile = cell.TileId;
        var reacted = 0;
        if (cell.HasActuator)
        {
            cell.IsInactive = !cell.IsInactive;
            World.SetCell(point, cell);
            _physicsDirty.Add(point);
            _supportQueue.Add(new GridPoint(point.X, point.Y - 1));
            reacted++;
        }
        if (ContentTiles.ToggledDoor(tile) is not null)
        {
            if (handledDoors.Add(point) && ToggleDoor(point))
            {
                for (var dy = -3; dy <= 3; dy++)
                    handledDoors.Add(new GridPoint(point.X, point.Y + dy));
                reacted++;
            }
            return reacted;
        }
        if (TilePhysics.IsGate(tile))
        {
            if (layer == 3)
                return reacted;
            var state = (byte)(cell.State ^ (layer == 1 ? 1 : 2));
            var a = (state & 1) != 0;
            var b = (state & 2) != 0;
            var output = tile switch
            {
                TileId.GateAnd => a && b,
                TileId.GateOr => a || b,
                TileId.GateXor => a ^ b,
                _ => !a
            };
            var wasOn = (state & 4) != 0;
            state = (byte)(output ? state | 4 : state & ~4);
            cell.State = state;
            World.SetCell(point, cell);
            _physicsDirty.Add(point);
            if (output != wasOn && _wires.Has(point, 3))
                queue.Enqueue((point, 3));
            return reacted + 1;
        }
        if (TilePhysics.TimerPeriod(tile) > 0f)
        {
            ToggleTimer(point);
            return reacted + 1;
        }
        if (ContentTiles.IsStatue(tile))
            return reacted + TriggerStatue(point, tile);
        switch (tile)
        {
            case TileId.DartTrap:
                FireDart(point, (cell.State & 1) != 0 ? -1 : 1);
                return reacted + 1;
            case TileId.ExplosiveTrap:
                World.SetTile(point, TileId.Air);
                Explode(new Vector2(point.X + 0.5f, point.Y + 0.5f), 3.5f, 60, 50f);
                return reacted + 1;
            case TileId.TeleporterPad:
                pads.Add(point);
                return reacted + 1;
        }
        if (!TilePhysics.IsHerb(tile) && TilePhysics.GetTileLight(tile) is not null)
        {
            cell.State = (byte)(cell.State ^ 1);
            World.SetCell(point, cell);
            _physicsDirty.Add(point);
            return reacted + 1;
        }
        return reacted;
    }

    private void FireDart(GridPoint trap, int direction)
    {
        var origin = new Vector2(trap.X + 0.5f + direction * 0.7f, trap.Y + 0.5f);
        _projectiles.Add(new ProjectileState(_nextEntityId++, origin, new Vector2(direction * 16f, 0f), 18f, 1.5f, 1.6f,
            ProjectileTeam.Enemy, "dart trap", DamageKind.Shot) { SpriteName = "proj_dart" });
    }

    private void Teleport(List<GridPoint> pads)
    {
        pads.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        void Move(PawnMotionState motion)
        {
            for (var i = 0; i < pads.Count; i++)
            {
                if (!Overlaps(motion, new Vector2(pads[i].X, pads[i].Y), Vector2.One))
                    continue;
                var target = pads[(i + 1) % pads.Count];
                motion.Position = new Vector2(target.X + 0.5f - motion.Size.X / 2f, target.Y + 1f - motion.Size.Y);
                motion.Velocity = Vector2.Zero;
                return;
            }
        }
        Move(Player.Motion);
        foreach (var agent in _colonists)
            Move(agent.Motion);
        foreach (var npc in _townNpcs)
            Move(npc.Motion);
        foreach (var enemy in _enemies)
            if (enemy.IsAlive)
                Move(enemy.Motion);
    }

    /// <summary>Lays a wire without tools (generation, tests). Returns false if it was already there.</summary>
    public bool PlaceWire(GridPoint cell, int layer = 1)
    {
        if (!World.Contains(cell) || !_wires.Place(cell, layer))
            return false;
        TrackTimer(cell);
        return true;
    }

    /// <summary>Fits an actuator without tools (generation, tests).</summary>
    public bool PlaceActuator(GridPoint point)
    {
        if (!World.Contains(point) || World.GetCell(point).TileId == TileId.Air)
            return false;
        var cell = World.GetCell(point);
        cell.HasActuator = true;
        World.SetCell(point, cell);
        return true;
    }

    private const string WireFlagPrefix = "wire.";

    /// <summary>Copies the wire layers into the persisted world flags (called when a snapshot is captured).</summary>
    private void SyncWiringToFlags()
    {
        var all = Flags.Capture();
        foreach (var key in all.Keys.Where(key => key.StartsWith(WireFlagPrefix, StringComparison.Ordinal)).ToArray())
            all.Remove(key);
        foreach (var (cell, bits) in _wires.Cells)
            all[$"{WireFlagPrefix}{cell.X}.{cell.Y}"] = bits;
        Flags.Restore(all);
    }

    /// <summary>Rebuilds wire layers (and running timers) from the world flags after a load.</summary>
    private void LoadWiringFromFlags()
    {
        _wires.Clear();
        _activeTimers.Clear();
        foreach (var (key, value) in Flags.All)
        {
            if (!key.StartsWith(WireFlagPrefix, StringComparison.Ordinal))
                continue;
            var parts = key[WireFlagPrefix.Length..].Split('.');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var x) || !int.TryParse(parts[1], out var y) || !World.Contains(x, y))
                continue;
            for (var layer = 1; layer <= WireMap.LayerCount; layer++)
                if ((value & WireMap.Bit(layer)) != 0)
                    _wires.Place(new GridPoint(x, y), layer);
            TrackTimer(new GridPoint(x, y));
        }
    }

    // ------------------------------------------------------------------ explosives

    /// <summary>Throws an explosive (bomb, dynamite or sticky bomb) from <paramref name="from"/>.</summary>
    public LiveExplosive ThrowExplosive(ItemId kind, Vector2 from, Vector2 velocity)
    {
        var (fuse, radius, power, damage) = kind switch
        {
            ItemId.Dynamite => (3.5f, 6f, 70, 120f),
            _ => (2.5f, 3.5f, 55, 60f)
        };
        var explosive = new LiveExplosive(_nextEntityId++, kind, from, velocity, fuse, radius, power, damage, kind == ItemId.StickyBomb);
        _explosives.Add(explosive);
        return explosive;
    }

    private void UpdateExplosives(float deltaSeconds)
    {
        for (var i = _explosives.Count - 1; i >= 0; i--)
        {
            var bomb = _explosives[i];
            bomb.FuseSeconds -= deltaSeconds;
            if (!bomb.Stuck)
            {
                var velocity = bomb.Velocity + new Vector2(0f, 20f * deltaSeconds);
                var next = bomb.Position + velocity * deltaSeconds;
                if (TilePhysics.IsSolidAt(World, Tiles, next.X, next.Y))
                {
                    if (bomb.Sticky)
                    {
                        bomb.Stuck = true;
                        velocity = Vector2.Zero;
                    }
                    else
                    {
                        var hitSide = TilePhysics.IsSolidAt(World, Tiles, next.X, bomb.Position.Y);
                        velocity = hitSide ? new Vector2(-velocity.X * 0.4f, velocity.Y) : new Vector2(velocity.X * 0.6f, -velocity.Y * 0.3f);
                        next = bomb.Position;
                    }
                }
                bomb.Velocity = velocity;
                bomb.Position = next;
            }
            if (bomb.FuseSeconds <= 0f)
            {
                _explosives.RemoveAt(i);
                Explode(bomb.Position, bomb.Radius, bomb.Power, bomb.Damage);
            }
        }
    }

    /// <summary>Tiles no blast can break: bedrock, containers, locked and boss-linked structures, the dungeon.</summary>
    public static bool IsExplosionProof(TileId tile) =>
        tile is TileId.Bedrock or TileId.PrecursorVaultDoor or TileId.DungeonBrickPrecursor or TileId.PrecursorBrick or
            TileId.PrecursorGlyph or TileId.BloomHive or TileId.CrystalAltar or TileId.WeaponShrine or TileId.LifeCore ||
        BlightRules.IsHeart(tile) || BlightRules.IsAltar(tile) ||
        ContentTiles.Has(tile, TileTraits.Container) || ContentTiles.Has(tile, TileTraits.Locked);

    /// <summary>
    /// Blows a circular hole of <paramref name="radius"/> cells: breaks every tile whose required tool power is at most
    /// <paramref name="power"/> (never explosion-proof tiles), drops their items, and hurts actors in range. Returns tiles broken.
    /// </summary>
    public int Explode(Vector2 center, float radius, int power, float damage)
    {
        SoundEvents.Emit(SoundEventKind.Explosion, string.Empty, center, (int)MathF.Round(radius));
        var broken = 0;
        var r = (int)MathF.Ceiling(radius);
        var cx = (int)MathF.Floor(center.X);
        var cy = (int)MathF.Floor(center.Y);
        for (var y = cy - r; y <= cy + r; y++)
        for (var x = cx - r; x <= cx + r; x++)
        {
            if (!World.Contains(x, y))
                continue;
            var dx = x + 0.5f - center.X;
            var dy = y + 0.5f - center.Y;
            if (dx * dx + dy * dy > radius * radius)
                continue;
            var tile = World.TileAt(x, y);
            if (tile == TileId.Air || IsExplosionProof(tile) || Tiles[tile].MinimumToolPower > power)
                continue;
            var point = new GridPoint(x, y);
            World.SetTile(point, TileId.Air);
            _physicsDirty.Add(point);
            if (TilePhysics.IsTreePart(tile) || TilePhysics.IsPot(tile) || TilePhysics.IsHerb(tile))
                HandleBrokenPhysicsTile(point, tile);
            else if (FloraParity.IsGrassPlant(tile))
                continue; // grass plants leave nothing (and do not count as blasted blocks)
            else if (TileDropTable.GetDrop(tile) is { } drop)
                Player.Inventory.Add(drop, 1);
            broken++;
        }
        void Hurt(PawnMotionState motion, Action<float> apply)
        {
            var distance = Vector2.Distance(motion.Position + motion.Size / 2f, center);
            if (distance <= radius + 1f)
                apply(damage * Math.Clamp(1f - distance / (radius + 1f), 0.25f, 1f));
        }
        Hurt(Player.Motion, amount => Player.TakeDamage(amount, DamageKind.Burn));
        foreach (var agent in _colonists)
            Hurt(agent.Motion, amount => agent.TakeDamage(amount));
        foreach (var enemy in _enemies)
            if (enemy.IsAlive)
                Hurt(enemy.Motion, amount => enemy.TakeDamage(amount));
        FlushPhysicsReframe();
        PostMessage(broken > 0 ? $"Boom! The blast tore out {broken} tiles." : "Boom!");
        return broken;
    }

    // ------------------------------------------------------------------ the dungeon guardian

    private void UpdateDungeonGuardian(float deltaSeconds)
    {
        _guardianTimer += deltaSeconds;
        if (_guardianTimer < 0.5f)
            return;
        var elapsed = _guardianTimer;
        _guardianTimer = 0f;
        var guardian = _enemies.FirstOrDefault(enemy => enemy.Kind == EnemyKind.DungeonGuardian && enemy.IsAlive);
        var guarded = !_defeatedBosses.Contains(PhysicsContent.DungeonGateBoss);
        if (guarded && PlayerInDungeon)
        {
            _guardianAbsence = 0f;
            if (guardian is null)
            {
                SpawnCreature(EnemyKind.DungeonGuardian, PlayerCenter + new Vector2(Player.Motion.Facing * -8f, -6f));
                Flags.Increment("dungeon.guardian.spawns");
                PostMessage("A towering Dungeon Guardian wakes. The dungeon is not yet yours to enter!");
            }
            return;
        }
        if (guardian is null)
            return;
        _guardianAbsence += elapsed;
        if (_guardianAbsence >= 3f || !guarded)
        {
            RecordDeath(guardian, DeathReason.Despawned);
            _enemies.Remove(guardian);
            _guardianAbsence = 0f;
            PostMessage("The Dungeon Guardian withdraws into the dark.");
        }
    }
}
