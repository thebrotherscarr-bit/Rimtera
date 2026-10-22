using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>How a tool is used: ticks between hits, swing length, extra reach, and whether holding the button keeps it going.</summary>
/// <param name="UseTime">Ticks between hits (the tool's cooldown).</param>
/// <param name="UseAnimation">Ticks one swing lasts on screen.</param>
/// <param name="TileBoost">Extra cells of tile reach (negative for the weakest tools).</param>
/// <param name="Channel">Drills and saws: the swing stays up while the button is held.</param>
/// <param name="AutoReuse">Holding the button starts the next swing by itself.</param>
public readonly record struct ToolUseProfile(int UseTime, int UseAnimation, int TileBoost, bool Channel, bool AutoReuse);

/// <summary>
/// Tool rules that follow Terraria 1.4.5.8 (behavioural evidence only, docs/TOOLS_PARITY.md): use and swing times,
/// per-tool reach, which tiles each tool kind may hit, the hammer's shape cycle, wall exposure, and the hit ledger
/// that lets partly broken tiles and walls mend as further hits are made. Names, tables and words are Rimtera's own.
/// </summary>
public static class ToolParity
{
    // ------------------------------------------------------------------ use profiles

    /// <summary>Swing length per tier (index = tier) for picks, cutters and demolishers (the reference's useAnimation ladder).</summary>
    private static readonly int[] PickSwing = [0, 23, 20, 20, 25, 25, 25, 12];
    private static readonly int[] CutterSwing = [0, 30, 27, 26, 35, 35, 35, 25];
    private static readonly int[] DemolisherSwing = [0, 33, 30, 28, 27, 27, 35, 30];
    /// <summary>Drills and saws all swing for 25 ticks and keep the swing up while held.</summary>
    public const int ChannelSwing = 25;

    /// <summary>Drills and saws: tools that keep running while the button is held (the reference's channel tools).</summary>
    private static readonly HashSet<ItemId> ChannelTools =
    [
        ItemId.FerriteDrill, ItemId.CobaltDrill, ItemId.GlacialDrill, ItemId.ViridiumDrill, ItemId.LumeniteDrill,
        ItemId.VoidsteelDrill, ItemId.AstralDrill, ItemId.DronecoreDrill, ItemId.ClaimjumperDrill, ItemId.PrismBore,
        ItemId.VoidAuger, ItemId.PlasmaSaw, ItemId.ChainCutter
    ];

    /// <summary>The copper-tier tools: one cell less reach (the reference's copper pickaxe, axe and hammer have tileBoost -1).</summary>
    private static readonly HashSet<ItemId> ShortReachTools =
    [
        ItemId.WoodenPickaxe, ItemId.CopperPickaxe, ItemId.FerritePick, ItemId.FerriteDrill, ItemId.FerriteHatchet,
        ItemId.FerriteCutter, ItemId.FerriteDemolisher
    ];

    public static bool IsTool(ItemDefinition? item) =>
        item is { UseKind: ItemUseKind.Pickaxe or ItemUseKind.Cutter or ItemUseKind.Demolisher };

    public static bool IsChannelTool(ItemId id) => ChannelTools.Contains(id);

    /// <summary>The use profile of <paramref name="item"/> (tools, the utility tools this file covers, and placeables).</summary>
    public static ToolUseProfile Profile(ItemDefinition item)
    {
        var tier = Math.Clamp(item.Tier, 1, 7);
        var useTime = Math.Max(1, item.UseCooldownTicks);
        var shortReach = ShortReachTools.Contains(item.Id);
        switch (item.UseKind)
        {
            case ItemUseKind.Pickaxe or ItemUseKind.Cutter or ItemUseKind.Demolisher:
            {
                var channel = ChannelTools.Contains(item.Id);
                var swing = channel ? ChannelSwing : item.UseKind switch
                {
                    ItemUseKind.Pickaxe => PickSwing[tier],
                    ItemUseKind.Cutter => CutterSwing[tier],
                    _ => DemolisherSwing[tier]
                };
                // Lunar-tier tools reach 4 more cells (their drills 3), copper-tier tools one fewer.
                var boost = shortReach ? -1 : tier >= 7 ? (channel && item.UseKind == ItemUseKind.Pickaxe ? 3 : 4) : 0;
                return new ToolUseProfile(useTime, Math.Max(swing, 1), boost, channel, !channel);
            }
        }
        return item.Id switch
        {
            // Wrench and wire cutter: 5-tick hits, 15-tick swing, 20 extra cells of reach.
            ItemId.WireTool or ItemId.WireCutter => new ToolUseProfile(5, 15, 20, false, true),
            ItemId.Actuator => new ToolUseProfile(10, 15, 0, false, true),
            ItemId.EmptyBucket or ItemId.BucketWater or ItemId.BucketLava or ItemId.BucketFuel => new ToolUseProfile(10, 15, 0, false, true),
            ItemId.Paintbrush or ItemId.PaintRoller or ItemId.PaintScraper => new ToolUseProfile(10, 15, 0, false, true),
            // The shaping hammer is a light demolisher: copper-hammer swing.
            ItemId.ShapingHammer => new ToolUseProfile(useTime, DemolisherSwing[1], 0, false, true),
            // Rope places faster than blocks and reaches 3 cells further.
            ItemId.ClimbingRope => new ToolUseProfile(8, 15, 3, false, true),
            _ when item.UseKind == ItemUseKind.BugNet => new ToolUseProfile(25, 25, 0, false, true),
            _ when item.UseKind == ItemUseKind.PlaceTile => new ToolUseProfile(BlockUseTime, 15, 0, false, true),
            _ when item.UseKind == ItemUseKind.PlaceWall => new ToolUseProfile(WallUseTime, 15, 0, false, true),
            _ => new ToolUseProfile(useTime, Math.Max(useTime, 1), 0, false, true)
        };
    }

    /// <summary>Extra reach of the held item (0 when nothing is held).</summary>
    public static int TileBoost(ItemDefinition? item) => item is null ? 0 : Profile(item).TileBoost;

    /// <summary>Ticks between placed blocks and between placed walls before placement-speed bonuses (dirt block 10, wood wall 7).</summary>
    public const int BlockUseTime = 10;
    public const int WallUseTime = 7;

    // ------------------------------------------------------------------ reach

    /// <summary>The base reach (5 sideways, 3 up/down, plus gear) is capped at this many cells before the tool's own boost.</summary>
    public const int TileReachLimit = 20;

    /// <summary>
    /// The reach box in cells: from the leftmost/topmost cell the body touches minus the range, to the rightmost/bottom cell
    /// plus the range. Range = min(base + gear bonus, 20) + the held tool's boost.
    /// </summary>
    public static (int MinX, int MinY, int MaxX, int MaxY) ReachBox(float bodyLeft, float bodyTop, float bodyWidth, float bodyHeight,
        int gearBonus, int tileBoost)
    {
        var left = (int)MathF.Floor(bodyLeft);
        var right = (int)MathF.Floor(bodyLeft + bodyWidth - 0.001f);
        var top = (int)MathF.Floor(bodyTop);
        var bottom = (int)MathF.Floor(bodyTop + bodyHeight - 0.001f);
        var rx = Math.Min(CombatTuning.TileReachX + gearBonus, TileReachLimit) + tileBoost;
        var ry = Math.Min(CombatTuning.TileReachY + gearBonus, TileReachLimit) + tileBoost;
        return (left - rx, top - ry, right + rx, bottom + ry);
    }

    public static bool InBox((int MinX, int MinY, int MaxX, int MaxY) box, int x, int y) =>
        x >= box.MinX && x <= box.MaxX && y >= box.MinY && y <= box.MaxY;

    // ------------------------------------------------------------------ which tool hits what

    /// <summary>Cutter-only tiles: trunks and tree crowns, cactus and fallen logs (the reference's axe tiles).</summary>
    public static bool IsCutterTile(TileId tile) => TilePhysics.IsTreePart(tile) || tile is TileId.Cactus or TileId.FallenLog;

    /// <summary>Demolisher-only tiles: blight hearts and altars (the reference's orbs and altars).</summary>
    public static bool IsDemolisherTile(TileId tile) => BlightRules.IsHeart(tile) || BlightRules.IsAltar(tile);

    /// <summary>
    /// Tiles any drill breaks in one hit (the reference's no-fail and rope tiles): plants a swing can cut, herbs, pots,
    /// rope, torches and fragile tiles.
    /// </summary>
    public static bool IsOneHitTile(TileId tile) =>
        FloraParity.IsCuttable(tile) || TilePhysics.IsHerb(tile) || TilePhysics.IsPot(tile) || ContentTiles.Has(tile, TileTraits.Fragile) ||
        tile is TileId.Rope or TileId.Vine or TileId.StandingTorch or TileId.WallTorch;

    /// <summary>A plain solid block: active, solid, not a platform, full shape and not actuated off.</summary>
    public static bool IsSolidBlock(TileGrid world, TileCatalog tiles, int x, int y)
    {
        if (!world.Contains(x, y))
            return false;
        var cell = world.GetCell(x, y);
        return cell.TileId != TileId.Air && tiles.Contains(cell.TileId) && tiles[cell.TileId].IsSolid && !TilePhysics.IsPlatform(cell.TileId) &&
               cell.Shape == TileShape.Full && !cell.IsInactive;
    }

    /// <summary>Solid (or platform) and not a demolisher-only tile: a demolisher swing on it is spent on shaping.</summary>
    public static bool IsPoundable(TileId tile, TileCatalog tiles) =>
        tile != TileId.Air && !IsDemolisherTile(tile) && tiles.Contains(tile) && (tiles[tile].IsSolid || TilePhysics.IsPlatform(tile));

    /// <summary>
    /// False when the tile at <paramref name="point"/> props something up that must go first: a trunk, cactus or fallen
    /// log of another kind, a container, an altar or a heart standing on it.
    /// </summary>
    public static bool CanBreakUnder(TileGrid world, GridPoint point)
    {
        if (!world.Contains(point) || point.Y < 1)
            return true;
        var tile = world.TileAt(point.X, point.Y);
        var above = world.TileAt(point.X, point.Y - 1);
        if (above == TileId.Air || above == tile)
            return true;
        if (TilePhysics.IsTrunk(above) || above is TileId.Cactus or TileId.FallenLog)
            return false;
        return !(ContentTiles.Has(above, TileTraits.Container) || BlightRules.IsAltar(above) || BlightRules.IsHeart(above));
    }

    // ------------------------------------------------------------------ the demolisher's shape cycle

    /// <summary>
    /// The shape one demolisher pound gives the block at <paramref name="point"/>, or null when it can't be shaped.
    /// Blocks: full → half → slope → mirrored slope → ceiling slope → mirrored ceiling slope → full. The first slope leans
    /// its high side against a wall on the right when the left is open; with a solid block above and open space below the
    /// ceiling slopes come first. Platforms: full → slope → mirrored slope → full, starting toward a neighbouring stair.
    /// </summary>
    public static TileShape? NextPoundShape(TileGrid world, TileCatalog tiles, GridPoint point)
    {
        if (!CanPound(world, tiles, point))
            return null;
        var x = point.X;
        var y = point.Y;
        var cell = world.GetCell(point);
        bool Solid(int cx, int cy) => IsSolidBlock(world, tiles, cx, cy);
        TileShape ShapeAt(int cx, int cy) => world.Contains(cx, cy) ? world.GetCell(cx, cy).Shape : TileShape.Full;

        if (TilePhysics.IsPlatform(cell.TileId))
        {
            if (cell.Shape == TileShape.Half)
                return TileShape.Full;
            var first = TileShape.SlopeUpLeft;
            var second = TileShape.SlopeUpRight;
            if (world.Contains(x + 1, y - 1) && TilePhysics.IsPlatform(world.TileAt(x + 1, y - 1)) ||
                world.Contains(x - 1, y + 1) && TilePhysics.IsPlatform(world.TileAt(x - 1, y + 1)) ||
                Solid(x + 1, y) && !Solid(x - 1, y))
                (first, second) = (second, first);
            if (cell.Shape == TileShape.Full)
                return first;
            return cell.Shape == first ? second : TileShape.Full;
        }

        if (cell.Shape == TileShape.Full)
            return TileShape.Half;

        // Floor slopes a/b and their ceiling counterparts.
        var floorA = TileShape.SlopeUpLeft;
        var floorB = TileShape.SlopeUpRight;
        var rightShape = ShapeAt(x + 1, y);
        var wallRight = Solid(x + 1, y) || rightShape is TileShape.SlopeUpLeft or TileShape.CeilingLeft && world.TileAt(x + 1, y) != TileId.Air;
        if (wallRight && !Solid(x - 1, y))
            (floorA, floorB) = (floorB, floorA);
        var ceilingA = Ceiling(floorA);
        var ceilingB = Ceiling(floorB);
        var ceilingFirst = Solid(x, y - 1) && !Solid(x, y + 1);
        var cycle = ceilingFirst
            ? new[] { TileShape.Half, ceilingA, ceilingB, floorA, floorB }
            : new[] { TileShape.Half, floorA, floorB, ceilingA, ceilingB };
        var index = Array.IndexOf(cycle, cell.Shape);
        return index < 0 || index == cycle.Length - 1 ? TileShape.Full : cycle[index + 1];

        static TileShape Ceiling(TileShape floor) => floor == TileShape.SlopeUpLeft ? TileShape.CeilingLeft : TileShape.CeilingRight;
    }

    /// <summary>
    /// True when a pound may reshape the block: a plain block or platform (not doors, containers, stations or furniture),
    /// nothing standing on it that forbids sloping (containers, altars, hearts), and the block itself could be broken.
    /// </summary>
    public static bool CanPound(TileGrid world, TileCatalog tiles, GridPoint point)
    {
        if (!world.Contains(point))
            return false;
        var tile = world.TileAt(point.X, point.Y);
        if (!TilePhysics.IsShapeable(tile, tiles))
            return false;
        if (point.Y > 0)
        {
            var above = world.TileAt(point.X, point.Y - 1);
            if (above != tile && (ContentTiles.Has(above, TileTraits.Container) || BlightRules.IsAltar(above) || BlightRules.IsHeart(above)))
                return false;
        }
        return CanBreakUnder(world, point);
    }

    // ------------------------------------------------------------------ walls

    /// <summary>True for walls the player built (they can be knocked out from anywhere, natural walls only from an edge).</summary>
    public static bool IsBuiltWall(ushort wall) => wall != WallIds.None && wall < WallIds.FirstNatural;

    /// <summary>
    /// A demolisher can hit the wall at <paramref name="point"/>: a built wall always, a natural wall when one of the
    /// eight cells around it has no wall or a built wall.
    /// </summary>
    public static bool CanSmashWall(TileGrid world, GridPoint point)
    {
        if (!world.Contains(point))
            return false;
        var wall = world.GetCell(point).WallId;
        if (wall == WallIds.None)
            return false;
        if (IsBuiltWall(wall))
            return true;
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (!world.Contains(point.X + dx, point.Y + dy))
                continue;
            var next = world.GetCell(point.X + dx, point.Y + dy).WallId;
            if (next == WallIds.None || IsBuiltWall(next))
                return true;
        }
        return false;
    }

    /// <summary>
    /// A wall the smart cursor may pick for a demolisher: one it can smash, unless a block sits in front of it with all
    /// eight neighbours solid too.
    /// </summary>
    public static bool IsWallTarget(TileGrid world, TileCatalog tiles, GridPoint point)
    {
        if (!CanSmashWall(world, point))
            return false;
        var cell = world.GetCell(point);
        if (cell.TileId == TileId.Air || cell.IsInactive)
            return true;
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0)
                continue;
            var cx = point.X + dx;
            var cy = point.Y + dy;
            if (!world.Contains(cx, cy))
                continue;
            var near = world.GetCell(cx, cy);
            if (near.TileId == TileId.Air || near.IsInactive || !tiles[near.TileId].IsSolid || TilePhysics.IsPlatform(near.TileId))
                return true;
        }
        return false;
    }
}

/// <summary>
/// Hit damage on partly broken tiles and walls, out of 100. Every hit refreshes its entry's life to 60 hits; every hit that
/// does damage ages all the other entries by one, and entries mend faster as they age (−2, −5, −7, then −10 per hit once
/// fewer than 48, 36, 24 and 12 hits of life remain) until they clear. Damage does not mend with time alone.
/// </summary>
public sealed class HitLedger
{
    public const int Life = 60;
    public const int Capacity = 500;

    public enum Layer : byte { Tile, Wall }

    private readonly Dictionary<(GridPoint Point, Layer Layer), Entry> _entries = new();

    private sealed class Entry
    {
        public int Damage;
        public int Life;
        public ushort Kind;
        public long Order;
    }

    private long _order;

    public int Count => _entries.Count;

    /// <summary>Damage so far on the tile or wall at <paramref name="point"/>, when the entry is for the same tile or wall kind.</summary>
    public int Damage(GridPoint point, Layer layer, ushort kind) =>
        _entries.TryGetValue((point, layer), out var entry) && entry.Kind == kind ? entry.Damage : 0;

    public bool Contains(GridPoint point, Layer layer) => _entries.ContainsKey((point, layer));

    /// <summary>Adds <paramref name="damage"/> and returns the new total (a different kind in the same cell starts over).</summary>
    public int Add(GridPoint point, Layer layer, ushort kind, int damage)
    {
        if (!_entries.TryGetValue((point, layer), out var entry) || entry.Kind != kind)
        {
            if (_entries.Count >= Capacity)
            {
                var oldest = _entries.MinBy(pair => pair.Value.Order).Key;
                _entries.Remove(oldest);
            }
            entry = new Entry { Kind = kind };
            _entries[(point, layer)] = entry;
        }
        entry.Damage += damage;
        entry.Life = Life;
        entry.Order = ++_order;
        return entry.Damage;
    }

    public void Clear(GridPoint point, Layer layer) => _entries.Remove((point, layer));

    public void ClearAll() => _entries.Clear();

    /// <summary>
    /// Ages every entry by one hit (called after each hit that did damage). <paramref name="stillThere"/> says whether the
    /// tile or wall an entry belongs to is still in place. Returns the cells whose damage changed or cleared.
    /// </summary>
    public List<(GridPoint Point, Layer Layer, int Damage)> Prune(Func<GridPoint, Layer, ushort, bool> stillThere)
    {
        var changed = new List<(GridPoint, Layer, int)>();
        foreach (var (key, entry) in _entries.ToArray())
        {
            if (entry.Life <= 1 || !stillThere(key.Point, key.Layer, entry.Kind))
            {
                _entries.Remove(key);
                changed.Add((key.Point, key.Layer, 0));
                continue;
            }
            entry.Life--;
            var before = entry.Damage;
            entry.Damage -= entry.Life switch
            {
                < 12 => 10,
                < 24 => 7,
                < 36 => 5,
                < 48 => 2,
                _ => 0
            };
            if (entry.Damage < 0)
            {
                _entries.Remove(key);
                changed.Add((key.Point, key.Layer, 0));
            }
            else if (entry.Damage != before)
                changed.Add((key.Point, key.Layer, entry.Damage));
        }
        return changed;
    }
}
