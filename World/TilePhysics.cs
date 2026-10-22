using Rimtera.Systems;

namespace Rimtera.World;

/// <summary>Light colour a tile emits (0–1 per channel) and its reach in tiles.</summary>
public readonly record struct TileLight(float R, float G, float B, float Radius = 6f);

/// <summary>
/// When a mature herb is in bloom (blooming herbs drop seeds). Each rule mirrors one of the reference game's herb
/// rules (ref. WorldGen.IsAlchemyPlantHarvestable and GrowAlch; see docs/FLORA_PARITY.md).
/// </summary>
public enum HerbBloomRule : byte
{
    /// <summary>Blooms while the sun is up.</summary>
    Daytime,
    /// <summary>Blooms at night.</summary>
    Night,
    /// <summary>Blooms while it rains.</summary>
    Rain,
    /// <summary>Blooms in the last quarter of the day, unless it rains on the surface.</summary>
    Dusk,
    /// <summary>Flickers in and out of bloom at random once mature.</summary>
    Flicker,
    /// <summary>Settles into bloom for good some time after maturing.</summary>
    Settled,
    /// <summary>Blooms on full-moon nights.</summary>
    FullMoon
}

/// <summary>Static data for one herb: the item it yields, its seeds, its soil and bloom rule.</summary>
public sealed record HerbInfo(TileId Tile, ItemId Herb, ItemId Seeds, HerbBloomRule Bloom, TileId[] Soils);

/// <summary>Conditions the herb bloom rules read. <paramref name="DayProgress"/> runs 0–1 across the daylight hours.</summary>
public readonly record struct HerbConditions(bool Daytime, float Rain, float Snow, float DayProgress = 0.5f, int MoonPhase = 4);

/// <summary>
/// Rimtera's tile physics rules (Batch A): block shapes and their collision geometry, one-way platforms,
/// climbables, falling blocks, light emitters, trees, herbs and stateful props. Pure functions over tiles
/// and cells so movement, generation, the ticker and the renderer can all share them.
/// </summary>
public static class TilePhysics
{
    private const float Epsilon = 0.001f;

    // ------------------------------------------------------------------ flags and state

    /// <summary>Props whose low flag bits hold state rather than a block shape.</summary>
    public static bool UsesStateBits(TileId tile) =>
        IsHerb(tile) || IsWiringDevice(tile) || ContentTiles.Has(tile, TileTraits.Light);

    /// <summary>The flag bits a cell keeps when its tile changes from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static byte CarryFlags(TileId from, TileId to, byte flags)
    {
        if (to == TileId.Air)
            return 0;
        var keep = flags;
        // Shapes survive block-to-block conversions (blight, purifier, paint); state bits never carry.
        if (from == TileId.Air || UsesStateBits(from) || UsesStateBits(to) || IsPlatform(from) != IsPlatform(to))
            keep = (byte)(keep & ~0x07);
        // Inactive only means something for the block that was actuated.
        if (from == TileId.Air)
            keep = (byte)(keep & ~0x10);
        return keep;
    }

    // ------------------------------------------------------------------ shapes and collision

    public static bool IsPlatform(TileId tile) => ContentTiles.Has(tile, TileTraits.Platform);

    public static bool IsClimbable(TileId tile) => ContentTiles.Has(tile, TileTraits.Climbable);

    /// <summary>
    /// Floor grip, as a fraction of normal ground friction: ice-type ground keeps you sliding.
    /// </summary>
    public static float SurfaceGrip(TileId floor) => floor switch
    {
        TileId.Snow or TileId.RadiatedIce or TileId.OvergrowthIce or TileId.ThinIce => 0.12f,
        _ => 1f
    };

    /// <summary>Top running speed factor on a floor: mud and nectar blocks drag at your feet.</summary>
    public static float SurfaceSpeedFactor(TileId floor) => floor switch
    {
        TileId.Mud => 0.75f,
        TileId.Nectar => 0.45f,
        _ => 1f
    };

    /// <summary>Sticky webs (mite silk) smother movement for anything passing through them.</summary>
    public static bool IsWeb(TileId tile) => tile == TileId.MiteSilk;

    /// <summary>Top running speed factor while inside webs.</summary>
    public const float WebSpeedFactor = 0.3f;

    /// <summary>True for blocks the shaping hammer may slope or halve: plain solid blocks and platforms.</summary>
    public static bool IsShapeable(TileId tile, TileCatalog catalog)
    {
        if (tile == TileId.Air || tile == TileId.Bedrock)
            return false;
        if (IsPlatform(tile))
            return true;
        if (!catalog.Contains(tile) || !catalog[tile].IsSolid || catalog[tile].Station != CraftingStationId.None)
            return false;
        if (ContentTiles.TryGet(tile, out var definition) &&
            (definition.Furniture || (definition.Traits & (TileTraits.Door | TileTraits.Container | TileTraits.Wired | TileTraits.Locked)) != 0))
            return false;
        return !BlightRules.IsHeart(tile) && !BlightRules.IsAltar(tile) && tile is not (TileId.SupplyCache or TileId.PrecursorVaultDoor);
    }

    /// <summary>The next shape in the hammer's cycle: full → half → four slopes → full.</summary>
    public static TileShape NextShape(TileShape shape, bool platform) => platform
        ? shape switch
        {
            TileShape.Full => TileShape.SlopeUpRight,
            TileShape.SlopeUpRight => TileShape.SlopeUpLeft,
            _ => TileShape.Full
        }
        : shape switch
        {
            TileShape.Full => TileShape.Half,
            TileShape.Half => TileShape.SlopeUpRight,
            TileShape.SlopeUpRight => TileShape.SlopeUpLeft,
            TileShape.SlopeUpLeft => TileShape.CeilingRight,
            TileShape.CeilingRight => TileShape.CeilingLeft,
            _ => TileShape.Full
        };

    /// <summary>
    /// Lowest floor surface (local y, 0 = cell top, 1 = cell bottom) under the local x-range [lx0, lx1] of a
    /// floor-type shape. Ceiling shapes report 0 (their top edge is flush with the cell top).
    /// </summary>
    public static float FloorTop(TileShape shape, float lx0, float lx1) => shape switch
    {
        TileShape.Half => 0.5f,
        TileShape.SlopeUpRight => 1f - lx1,
        TileShape.SlopeUpLeft => lx0,
        _ => 0f
    };

    /// <summary>Floor height (local y) at one local x for a floor shape, for renderers and probes.</summary>
    public static float SurfaceAt(TileShape shape, float lx) => shape switch
    {
        TileShape.Half => 0.5f,
        TileShape.SlopeUpRight => 1f - lx,
        TileShape.SlopeUpLeft => lx,
        _ => 0f
    };

    /// <summary>
    /// True when the local rectangle [lx0,lx1]×[ly0,ly1] (clipped to the cell, y down) overlaps the solid part
    /// of a block with <paramref name="shape"/>.
    /// </summary>
    public static bool ShapeOverlaps(TileShape shape, float lx0, float ly0, float lx1, float ly1)
    {
        if (lx1 - lx0 <= Epsilon || ly1 - ly0 <= Epsilon)
            return false;
        return shape switch
        {
            TileShape.Full => true,
            TileShape.Half => ly1 > 0.5f + Epsilon,
            TileShape.SlopeUpRight => ly1 > 1f - lx1 + Epsilon,
            TileShape.SlopeUpLeft => ly1 > lx0 + Epsilon,
            TileShape.CeilingRight => ly0 < lx1 - Epsilon,
            TileShape.CeilingLeft => ly0 < 1f - lx0 - Epsilon,
            _ => true
        };
    }

    /// <summary>True when a world-space box overlaps the solid part of the cell at (x, y), honouring shape and actuation.</summary>
    public static bool BoxHitsCell(in WorldCell cell, TileCatalog catalog, int x, int y, float left, float top, float right, float bottom)
    {
        if (cell.TileId == TileId.Air || cell.IsInactive || !catalog[cell.TileId].IsSolid)
            return false;
        return ShapeOverlaps(cell.Shape, Math.Max(0f, left - x), Math.Max(0f, top - y), Math.Min(1f, right - x), Math.Min(1f, bottom - y));
    }

    /// <summary>True when a world-space point is inside the solid part of a cell (projectiles, falling blocks, probes).</summary>
    public static bool IsSolidAt(TileGrid grid, TileCatalog catalog, float worldX, float worldY)
    {
        var x = (int)MathF.Floor(worldX);
        var y = (int)MathF.Floor(worldY);
        if (!grid.Contains(x, y))
            return true;
        var cell = grid.GetCell(x, y);
        if (cell.TileId == TileId.Air || cell.IsInactive || !catalog[cell.TileId].IsSolid)
            return false;
        var lx = worldX - x;
        var ly = worldY - y;
        return cell.Shape switch
        {
            TileShape.Half => ly >= 0.5f,
            TileShape.SlopeUpRight => ly >= 1f - lx,
            TileShape.SlopeUpLeft => ly >= lx,
            TileShape.CeilingRight => ly <= lx,
            TileShape.CeilingLeft => ly <= 1f - lx,
            _ => true
        };
    }

    // ------------------------------------------------------------------ falling blocks

    /// <summary>Loose blocks that fall when nothing holds them up.</summary>
    public static bool Falls(TileId tile) => tile is TileId.Sand or TileId.DesertSand or TileId.Gravel or TileId.Silt or
        TileId.AcidSand or TileId.ShellSand or TileId.PearlSand or TileId.RadiatedSand or TileId.OvergrowthSand or TileId.ShellBeach or
        TileId.Slush or TileId.Boulder;

    // ------------------------------------------------------------------ light

    /// <summary>Light a tile emits, or null. Lamps switched off by wiring report null through <see cref="GetCellLight"/>.</summary>
    public static TileLight? GetTileLight(TileId tile) => tile switch
    {
        TileId.WallTorch or TileId.StandingTorch => new(1f, 0.74f, 0.42f, 7f),
        TileId.DeskLamp or TileId.FloorLamp => new(1f, 0.9f, 0.7f, 6f),
        TileId.CeilingLight or TileId.DisplayPanel => new(0.85f, 0.92f, 1f, 7f),
        TileId.StandingFloodlight => new(1f, 1f, 0.95f, 10f),
        TileId.GlowLampAmber => new(1f, 0.7f, 0.25f, 6f),
        TileId.GlowLampCyan => new(0.3f, 0.95f, 1f, 6f),
        TileId.GlowLampViolet => new(0.75f, 0.4f, 1f, 6f),
        TileId.MetalFurnace => new(1f, 0.5f, 0.2f, 5f),
        TileId.HerbMoonglow => new(0.45f, 0.65f, 1f, 3f),
        TileId.HerbEmberroot => new(1f, 0.45f, 0.15f, 3f),
        TileId.WeaponShrine => new(0.7f, 0.85f, 1f, 5f),
        TileId.EnergyCrystal => new(0.4f, 0.8f, 1f, 3f),
        TileId.LongMoss => new(0.45f, 1f, 0.6f, 2f),
        TileId.SolarLily => new(1f, 0.85f, 0.4f, 3f),
        TileId.CrystalShrub => new(0.75f, 0.5f, 1f, 3f),
        TileId.Campfire => new(1f, 0.6f, 0.25f, 7f),
        TileId.SlagForge => new(1f, 0.45f, 0.15f, 6f),
        TileId.VaultAltar => new(0.5f, 1f, 0.8f, 5f),
        _ => ContentTiles.Has(tile, TileTraits.Light) ? new TileLight(0.9f, 0.9f, 0.8f, 5f) : null
    };

    /// <summary>Light emitted by a placed cell: lamps that wiring switched off (state bit 0) are dark.</summary>
    public static TileLight? GetCellLight(in WorldCell cell)
    {
        if (GetTileLight(cell.TileId) is not { } light)
            return null;
        if (IsHerb(cell.TileId))
            return (cell.State & 1) != 0 ? light : null;
        return (cell.State & 1) != 0 ? null : light;
    }

    // ------------------------------------------------------------------ wiring devices

    public static bool IsWiringDevice(TileId tile) => ContentTiles.Has(tile, TileTraits.Wired);

    public static bool IsGate(TileId tile) => tile is TileId.GateAnd or TileId.GateOr or TileId.GateNot or TileId.GateXor;

    public static float TimerPeriod(TileId tile) => tile switch
    {
        TileId.Timer1s => 1f,
        TileId.Timer3s => 3f,
        TileId.Timer5s => 5f,
        _ => 0f
    };

    public static bool IsPot(TileId tile) => tile is >= TileId.PotSurface and <= TileId.PotCanopy;

    // ------------------------------------------------------------------ trees

    public static bool IsTrunk(TileId tile) => tile is TileId.TreeTrunk or TileId.PineTrunk or TileId.PalmTrunk || Rimtera.Systems.ContentFillFlora.IsTrunk(tile);

    public static bool IsCanopy(TileId tile) => tile is TileId.TreeCanopy or TileId.PineCanopy or TileId.PalmCanopy || Rimtera.Systems.ContentFillFlora.IsCanopy(tile);

    public static bool IsTreePart(TileId tile) => IsTrunk(tile) || IsCanopy(tile);

    /// <summary>The trunk and canopy a sapling grows into on <paramref name="ground"/>, or null if trees cannot grow there.</summary>
    public static (TileId Trunk, TileId Canopy)? TreeFor(TileId ground) => Rimtera.Systems.ContentFillFlora.TreeFor(ground) ?? ground switch
    {
        TileId.Grass or TileId.Soil or TileId.JungleGrass or TileId.CanopyGrass or TileId.Mud or TileId.HallowGrass or
            TileId.PrismaticGrass or TileId.OvergrowthSoil => (TileId.TreeTrunk, TileId.TreeCanopy),
        TileId.Snow or TileId.OvergrowthIce => (TileId.PineTrunk, TileId.PineCanopy),
        TileId.Sand or TileId.DesertSand or TileId.ShellBeach or TileId.ShellSand or TileId.ScrapIronOre => (TileId.PalmTrunk, TileId.PalmCanopy),
        _ => null
    };

    /// <summary>
    /// Grows a full tree whose base is the cell (x, y) (usually a sapling): a trunk of 5–10 cells and a
    /// species-shaped canopy. Fails, changing nothing, when the ground is wrong or the space is blocked.
    /// </summary>
    public static bool TryGrowTree(TileGrid grid, TileCatalog catalog, int x, int y, Random random, List<GridPoint>? changed = null,
        int minHeight = 5, int maxHeight = 10)
    {
        if (!grid.Contains(x, y) || !grid.Contains(x, y + 1))
            return false;
        if (TreeFor(grid.TileAt(x, y + 1)) is not { } species || !catalog[grid.TileAt(x, y + 1)].IsSolid)
            return false;
        var height = random.Next(minHeight, maxHeight + 1);
        // Shrink to the space available.
        var free = 0;
        while (free < height + 3 && IsOpen(grid, x, y - free, allowSapling: free == 0))
            free++;
        height = Math.Min(height, free - 3);
        if (height < minHeight - 1)
            return false;
        var top = y - height + 1;
        var canopyCells = CanopyCells(species.Canopy, x, top);
        foreach (var (cx, cy) in canopyCells)
            if (!grid.Contains(cx, cy) || (grid.TileAt(cx, cy) != TileId.Air && !IsCanopy(grid.TileAt(cx, cy)) &&
                                           !FloraParity.IsReplaceablePlant(grid.TileAt(cx, cy))))
                return false;
        for (var ty = y; ty >= top; ty--)
            Set(grid, x, ty, species.Trunk, changed);
        foreach (var (cx, cy) in canopyCells)
            if (grid.TileAt(cx, cy) == TileId.Air || FloraParity.IsReplaceablePlant(grid.TileAt(cx, cy)))
                Set(grid, cx, cy, species.Canopy, changed);
        return true;
    }

    private static bool IsOpen(TileGrid grid, int x, int y, bool allowSapling)
    {
        if (!grid.Contains(x, y))
            return false;
        var cell = grid.GetCell(x, y);
        return (cell.TileId == TileId.Air || FloraParity.IsReplaceablePlant(cell.TileId) || (allowSapling && cell.TileId == TileId.Sapling)) &&
               cell.LiquidUnits == 0;
    }

    private static List<(int X, int Y)> CanopyCells(TileId canopy, int x, int top)
    {
        var cells = new List<(int, int)>();
        switch (Rimtera.Systems.ContentFillFlora.ShapeOf(canopy))
        {
            case TileId.PineCanopy:
                // A narrow cone down the top of the trunk.
                cells.Add((x, top - 1));
                cells.Add((x, top - 2));
                for (var row = 0; row < 3; row++)
                {
                    var half = 1 + row / 2;
                    for (var dx = -half; dx <= half; dx++)
                        if (dx != 0)
                            cells.Add((x + dx, top + row));
                }
                break;
            case TileId.PalmCanopy:
                // Fronds spread wide and flat.
                cells.Add((x, top - 1));
                for (var dx = -3; dx <= 3; dx++)
                    if (dx != 0)
                        cells.Add((x + dx, top + (Math.Abs(dx) == 3 ? 1 : 0)));
                break;
            default:
                // A round 5×3 crown sitting on the trunk.
                for (var dy = -3; dy <= -1; dy++)
                for (var dx = -2; dx <= 2; dx++)
                    if (!(Math.Abs(dx) == 2 && dy != -2))
                        cells.Add((x + dx, top + dy));
                cells.Add((x - 1, top));
                cells.Add((x + 1, top));
                break;
        }
        return cells;
    }

    private static void Set(TileGrid grid, int x, int y, TileId tile, List<GridPoint>? changed)
    {
        var point = new GridPoint(x, y);
        grid.SetTile(point, tile);
        changed?.Add(point);
    }

    /// <summary>
    /// Every cell of the tree that loses its support when the trunk cell at <paramref name="start"/> is cut: the
    /// trunk at and above it plus every canopy cell connected to that part.
    /// </summary>
    public static List<GridPoint> CollectTreeAbove(TileGrid grid, GridPoint start)
    {
        var result = new List<GridPoint>();
        var seen = new HashSet<GridPoint>();
        var frontier = new Stack<GridPoint>();
        frontier.Push(start);
        while (frontier.Count > 0 && result.Count < 200)
        {
            var point = frontier.Pop();
            if (!grid.Contains(point) || point.Y > start.Y || !seen.Add(point))
                continue;
            var tile = grid.GetCell(point).TileId;
            if (!IsTreePart(tile) && !(point == start))
                continue;
            result.Add(point);
            frontier.Push(new GridPoint(point.X, point.Y - 1));
            frontier.Push(new GridPoint(point.X - 1, point.Y));
            frontier.Push(new GridPoint(point.X + 1, point.Y));
            if (point.Y < start.Y)
                frontier.Push(new GridPoint(point.X, point.Y + 1));
        }
        return result;
    }

    // ------------------------------------------------------------------ herbs

    private static readonly HerbInfo[] Herbs =
    [
        new(TileId.HerbSunpetal, ItemId.SunpetalHerb, ItemId.SunpetalSeeds, HerbBloomRule.Daytime, [TileId.Grass, TileId.HallowGrass, TileId.PrismaticGrass]),
        new(TileId.HerbMoonglow, ItemId.MoonglowHerb, ItemId.MoonglowSeeds, HerbBloomRule.Night, [TileId.JungleGrass, TileId.CanopyGrass, TileId.Mud]),
        new(TileId.HerbRainreed, ItemId.RainreedHerb, ItemId.RainreedSeeds, HerbBloomRule.Rain, [TileId.Sand, TileId.DesertSand, TileId.ShellBeach]),
        new(TileId.HerbFrostbell, ItemId.FrostbellHerb, ItemId.FrostbellSeeds, HerbBloomRule.Settled, [TileId.Snow]),
        new(TileId.HerbEmberroot, ItemId.EmberrootHerb, ItemId.EmberrootSeeds, HerbBloomRule.Dusk, [TileId.Ash, TileId.EmberRock, TileId.CoreCrust, TileId.IrradiatedRock]),
        new(TileId.HerbSporecap, ItemId.SporecapHerb, ItemId.SporecapSeeds, HerbBloomRule.Flicker, [TileId.FungalSoil, TileId.GlowshroomGrass, TileId.MyceliumStone])
    ];

    public static IReadOnlyList<HerbInfo> AllHerbs => Herbs;

    public static bool IsHerb(TileId tile) => tile is >= TileId.HerbSunpetal and <= TileId.HerbSporecap;

    public static HerbInfo? HerbFor(TileId tile) => IsHerb(tile) ? Herbs[tile - TileId.HerbSunpetal] : null;

    public static HerbInfo? HerbForSeeds(ItemId seeds) => Herbs.FirstOrDefault(herb => herb.Seeds == seeds);

    /// <summary>Herb state bit 0: mature. Bit 1: in bloom (flickering and settled herbs keep their bloom in the cell).</summary>
    public const byte HerbMatureBit = 1, HerbBloomBit = 2;

    /// <summary>
    /// True when a mature herb cell is in bloom under <paramref name="conditions"/> (ref. IsAlchemyPlantHarvestable):
    /// day, night, rain (or clouds), dusk = the last quarter of the day and not raining unless below the surface, full moon;
    /// flickering and settled herbs read their bloom bit.
    /// </summary>
    public static bool IsBlooming(in WorldCell cell, HerbConditions conditions, bool belowSurface = false)
    {
        if (HerbFor(cell.TileId) is not { } herb || (cell.State & HerbMatureBit) == 0)
            return false;
        return herb.Bloom switch
        {
            HerbBloomRule.Daytime => conditions.Daytime,
            HerbBloomRule.Night => !conditions.Daytime,
            HerbBloomRule.Rain => conditions.Rain > 0f,
            HerbBloomRule.Dusk => conditions.Daytime && conditions.DayProgress > 0.75f && (conditions.Rain <= 0f || belowSurface),
            HerbBloomRule.FullMoon => !conditions.Daytime && conditions.MoonPhase == 0,
            _ => (cell.State & HerbBloomBit) != 0
        };
    }

    /// <summary>Herbs that live through water (night and rain herbs) or lava (the dusk herb) (ref. GrowAlch @22–@143).</summary>
    public static bool HerbSurvivesLiquid(HerbBloomRule rule, LiquidKind liquid) => liquid switch
    {
        LiquidKind.Water => rule is HerbBloomRule.Night or HerbBloomRule.Rain,
        LiquidKind.Lava => rule == HerbBloomRule.Dusk,
        _ => false
    };
}
