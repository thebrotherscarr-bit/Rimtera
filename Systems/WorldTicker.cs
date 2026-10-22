using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Tile conversion tables for the world's blight. Rimtera's own tables: each blight maps the
/// four convertible ground families (soil/grass, rock, sand, ice) to its own tiles, and the
/// purifier maps every blight tile back to its natural family.
/// </summary>
public static class BlightRules
{
    /// <summary>Grass-like surface tiles: the only tiles blight may take before the Fracture.</summary>
    public static bool IsGrassLike(TileId tile) => tile is TileId.Grass or TileId.JungleGrass or TileId.HallowGrass or
        TileId.PrismaticGrass or TileId.CanopyGrass;

    /// <summary>The blight tile that replaces <paramref name="tile"/>, or null if it does not convert.</summary>
    public static TileId? Convert(TileId tile, WorldBlight blight)
    {
        var radiated = blight == WorldBlight.Radiated;
        return tile switch
        {
            TileId.Grass or TileId.JungleGrass or TileId.HallowGrass or TileId.Soil or TileId.FungalSoil or
                TileId.PrismaticGrass or TileId.CanopyGrass or TileId.Mud =>
                radiated ? TileId.CrimsonSoil : TileId.OvergrowthSoil,
            TileId.Rock or TileId.PearlStone => radiated ? TileId.CorruptRock : TileId.OvergrowthRock,
            TileId.Sand or TileId.DesertSand or TileId.PearlSand => radiated ? TileId.RadiatedSand : TileId.OvergrowthSand,
            TileId.Snow => radiated ? TileId.RadiatedIce : TileId.OvergrowthIce,
            _ => null
        };
    }

    /// <summary>
    /// True when blight may replace <paramref name="tile"/>: grass-like tiles before the Fracture,
    /// every convertible tile after it. Hearts, altars, ores, structures and furniture never convert.
    /// </summary>
    public static bool CanBlightReplace(TileId tile, WorldBlight blight, bool fractured) =>
        Convert(tile, blight) is not null && (fractured || IsGrassLike(tile));

    /// <summary>True for any blight ground tile (either blight).</summary>
    public static bool IsBlightTile(TileId tile) => BlightOf(tile) is not null;

    /// <summary>Which blight a ground tile belongs to, or null.</summary>
    public static WorldBlight? BlightOf(TileId tile) => tile switch
    {
        TileId.CrimsonSoil or TileId.CorruptRock or TileId.RadiatedSand or TileId.RadiatedIce => WorldBlight.Radiated,
        TileId.OvergrowthSoil or TileId.OvergrowthRock or TileId.OvergrowthSand or TileId.OvergrowthIce => WorldBlight.Overgrowth,
        _ => null
    };

    /// <summary>The natural tile a purifier restores (soil becomes grass when it is open to the air).</summary>
    public static TileId? Purify(TileId tile, bool exposedToAir) => tile switch
    {
        TileId.CrimsonSoil or TileId.OvergrowthSoil => exposedToAir ? TileId.Grass : TileId.Soil,
        TileId.CorruptRock or TileId.OvergrowthRock => TileId.Rock,
        TileId.RadiatedSand or TileId.OvergrowthSand => TileId.Sand,
        TileId.RadiatedIce or TileId.OvergrowthIce => TileId.Snow,
        _ => null
    };

    public static TileId HeartTile(WorldBlight blight) => blight == WorldBlight.Radiated ? TileId.ReactorCore : TileId.SporeHeart;
    public static TileId AltarTile(WorldBlight blight) => blight == WorldBlight.Radiated ? TileId.RadAltar : TileId.RootAltar;
    public static bool IsHeart(TileId tile) => tile is TileId.ReactorCore or TileId.SporeHeart;
    public static bool IsAltar(TileId tile) => tile is TileId.RadAltar or TileId.RootAltar;
}

/// <summary>
/// Random tile updates, in two streams. The world pass (Rimtera's own rate) picks cells proportional to the world's area:
/// blight tiles try to convert a nearby tile (grass only before the Fracture; any convertible tile and more often after it;
/// never within <see cref="BeaconRadius"/> of a purifier beacon), crystal spreads after the Fracture, exposed cave rock next
/// to a crystal rarely grows a new crystal, wild plants and gem trees regrow and lifeblooms sprout. The flora pass runs at
/// the reference game's rates (<see cref="FloraParity"/>): above-ground and below-ground updates grow saplings, herbs, grass,
/// grass plants, vines and cacti, and now and then plant a wild herb. Uses its own random streams so it never shifts spawns
/// or combat.
/// </summary>
public sealed class WorldTicker
{
    /// <summary>World-pass random updates per cell per tick (1600×500 ≈ 160 per tick).</summary>
    public const double UpdatesPerCellPerTick = 1.0 / 5000.0;
    public const int BeaconRadius = 12;

    private readonly TileGrid _world;
    private readonly TileCatalog _tiles;
    private readonly Random _random;
    private readonly Random _flora;
    private readonly List<GridPoint> _changed = new();
    private double _carry;
    private double _overCarry;
    private double _underCarry;

    public WorldTicker(TileGrid world, TileCatalog tiles, int seed)
    {
        _world = world;
        _tiles = tiles;
        _random = new Random(seed);
        _flora = new Random(seed ^ 0x0F1A2A);
    }

    /// <summary>The world's blight; null disables spread (legacy worlds).</summary>
    public WorldBlight? Blight { get; set; }
    public bool Fractured { get; set; }
    /// <summary>Day, rain and moon for the flora rules; the simulation keeps this current (defaults to a clear noon).</summary>
    public Func<FloraConditions>? Conditions { get; set; }
    /// <summary>Purifier beacon cells; maintained by the simulation.</summary>
    public HashSet<GridPoint> Beacons { get; } = new();
    public long BlightConversions { get; private set; }
    /// <summary>Soil cells grass spread into.</summary>
    public long GrassRegrowths { get; private set; }
    public long CrystalRegrowths { get; private set; }
    /// <summary>Tiles the Crystal Spires have taken (from natural ground or blight).</summary>
    public long CrystalConversions { get; private set; }
    /// <summary>Saplings that grew into trees.</summary>
    public long TreesGrown { get; private set; }
    /// <summary>Plant growth steps (herbs maturing, vines and cacti lengthening, grass plants, wild sprouts).</summary>
    public long PlantGrowths { get; private set; }
    /// <summary>Wild herbs planted by the herb roll.</summary>
    public long HerbsPlanted { get; private set; }
    /// <summary>Flora-pass random updates run so far (above and below ground).</summary>
    public long FloraUpdates { get; private set; }

    private int SurfaceRow => FloraParity.SurfaceRow(_world.Height);
    private FloraConditions Now => Conditions?.Invoke() ?? FloraConditions.Default;

    /// <summary>Runs one tick's random updates (world pass and flora pass) and returns the cells that changed.</summary>
    public IReadOnlyList<GridPoint> Tick()
    {
        _changed.Clear();
        _carry += _world.Count * UpdatesPerCellPerTick * (Fractured ? 1.5 : 1.0);
        var count = (int)_carry;
        _carry -= count;
        for (var i = 0; i < count; i++)
            WorldUpdate(_random.Next(_world.Width), _random.Next(_world.Height));
        FloraTick();
        return _changed;
    }

    /// <summary>
    /// The flora pass: the reference's above-ground and below-ground update counts for this tick (ref. UpdateWorld @256–@1433),
    /// scaled to this world's area, with the herb roll on each above-ground update.
    /// </summary>
    private void FloraTick()
    {
        var conditions = Now;
        var surface = Math.Clamp(SurfaceRow, 2, _world.Height - 1);
        _overCarry += _world.Count * FloraParity.OvergroundUpdatesPerCell * (conditions.Raining ? FloraParity.RainUpdateMultiplier : 1.0);
        _underCarry += _world.Count * FloraParity.UndergroundUpdatesPerCell;
        var over = (int)_overCarry;
        var under = (int)_underCarry;
        _overCarry -= over;
        _underCarry -= under;
        FloraUpdates += over + under;
        var herbOneIn = FloraParity.HerbSpawnOneIn(_world.Width);
        for (var i = 0; i < over; i++)
        {
            if (_flora.Next(herbOneIn) == 0)
                PlantWildHerb();
            FloraUpdate(_flora.Next(_world.Width), _flora.Next(1, surface), conditions);
        }
        for (var i = 0; i < under; i++)
            FloraUpdate(_flora.Next(_world.Width), _flora.Next(surface - 1, _world.Height), conditions);
    }

    /// <summary>Runs <paramref name="count"/> updates at random cells (tests, fast-forward).</summary>
    public IReadOnlyList<GridPoint> RandomUpdates(int count)
    {
        _changed.Clear();
        for (var i = 0; i < count; i++)
            UpdateCell(_random.Next(_world.Width), _random.Next(_world.Height));
        return _changed;
    }

    /// <summary>One random update at a specific cell: both the world rules and the flora rules.</summary>
    public void UpdateCell(int x, int y)
    {
        FloraUpdate(x, y, Now);
        WorldUpdate(x, y);
    }

    /// <summary>The world-pass rules for one cell (blight, crystal, wild plants, gem trees, lifeblooms).</summary>
    private void WorldUpdate(int x, int y)
    {
        var tile = _world.TileAt(x, y);
        if (Blight is { } blight && BlightRules.BlightOf(tile) == blight)
        {
            var attempts = Fractured ? 3 : 1;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var tx = x + _random.Next(-2, 3);
                var ty = y + _random.Next(-2, 3);
                TrySpreadInto(tx, ty);
            }
            return;
        }

        if (Fractured && CrystalRules.IsCrystal(tile) && tile != TileId.CrystalAltar)
        {
            // Crystal spreads after the Fracture and purifies blight it touches; beacons do not stop it.
            var tx = x + _random.Next(-2, 3);
            var ty = y + _random.Next(-2, 3);
            TrySpreadCrystal(tx, ty);
            return;
        }

        if (tile == TileId.Rock && y > _world.Height * 0.45 && _random.Next(40) == 0 &&
            TouchesAir(x, y) && CrystalNear(x, y, 3))
        {
            Set(x, y, TileId.EnergyCrystal);
            CrystalRegrowths++;
            return;
        }

        // Rimtera's own regrowth (tuned to this pass's rate): post-Fracture lifeblooms, wild plants and gem trees.
        if (Fractured && GrowthItems.TryGrowLifebloom(_world, x, y, tile, _changed)) { PlantGrowths++; return; }
        if (ContentFillFlora.TickGrowth(_world, _tiles, x, y, tile, _changed)) PlantGrowths++;
    }

    /// <summary>Converts one cell to blight if the rules allow it. Returns true when it converted.</summary>
    public bool TrySpreadInto(int x, int y)
    {
        if (Blight is not { } blight || !_world.Contains(x, y))
            return false;
        var target = _world.TileAt(x, y);
        if (!BlightRules.CanBlightReplace(target, blight, Fractured) || IsNearBeacon(x, y))
            return false;
        Set(x, y, BlightRules.Convert(target, blight)!.Value);
        BlightConversions++;
        return true;
    }

    /// <summary>Converts one cell to Crystal Spires ground if it is natural ground or blight.</summary>
    public bool TrySpreadCrystal(int x, int y)
    {
        if (!_world.Contains(x, y))
            return false;
        var target = _world.TileAt(x, y);
        var exposed = !_tiles[_world.TileAt(x, y - 1, TileId.Air)].IsSolid;
        if (CrystalRules.Convert(target, exposed) is not { } crystal || crystal == target)
            return false;
        Set(x, y, crystal);
        CrystalConversions++;
        return true;
    }

    public bool IsNearBeacon(int x, int y)
    {
        foreach (var beacon in Beacons)
        {
            var dx = beacon.X - x;
            var dy = beacon.Y - y;
            if (dx * dx + dy * dy <= BeaconRadius * BeaconRadius)
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ flora rules (FloraParity)

    /// <summary>
    /// The flora rules for one random update at (x, y): saplings, herbs, grass plants, vines, cacti, and grass ground
    /// (plants on top, spread into soil, vines below, the odd jungle or shroom tree).
    /// </summary>
    private void FloraUpdate(int x, int y, FloraConditions conditions)
    {
        if (!_world.Contains(x, y))
            return;
        var cell = _world.GetCell(x, y);
        var tile = cell.TileId;
        switch (tile)
        {
            case TileId.Air:
                return;
            case TileId.Sapling:
                // ref. UpdateWorld_OvergroundTile @4374: one in 20, then the tree may still fail for room or ground.
                if (_flora.Next(FloraParity.SaplingGrowOneIn) == 0 && TryGrowSapling(_world, _tiles, x, y, _flora, _changed))
                    TreesGrown++;
                return;
            case TileId.GrassPlant:
                if (_flora.Next(FloraParity.TallGrassOneIn) == 0)
                    SetKeep(x, y, TileId.TallGrassPlant);
                return;
            case TileId.Vine:
                GrowVineBelow(x, y);
                return;
            case TileId.Cactus:
                if (_flora.Next(FloraParity.CactusGrowOneIn) == 0)
                    GrowCactus(x, y);
                return;
        }
        if (TilePhysics.HerbFor(tile) is { } herb)
        {
            GrowHerb(x, y, cell, herb, conditions);
            return;
        }
        if (!_tiles[tile].IsSolid || cell.IsInactive)
            return;

        var above = y > 0 ? _world.GetCell(x, y - 1) : default;
        var openAbove = y > 0 && above.TileId == TileId.Air && above.LiquidUnits == 0;
        // Grass plants on the open top (ref. GrassGrowth @2146–@2339, @3514, @3904).
        if (openAbove && FloraParity.PlantOneIn(tile) is { } plantOneIn && _flora.Next(plantOneIn) == 0)
            Grow(x, y - 1, TileId.GrassPlant);
        // Spread into the neighbouring soil (ref. GrassGrowth @2700–@3063, SpreadGrass).
        if (FloraParity.SpreadSoils(tile) is { } soils &&
            (!FloraParity.SpreadsOnlyAboveSurface(tile) || y < SurfaceRow) && (!FloraParity.SpreadsOnlyAfterFracture(tile) || Fractured))
            SpreadGrass(x, y, tile, soils);
        // Vines from grass undersides (ref. UpdateWorld_OvergroundTile @4946–@5135).
        if (FloraParity.GrowsVines(tile))
            GrowVineBelow(x, y);
        // A canopy plant on jungle grass becomes a tree; glowshroom grass grows shroom trees (ref. GrassGrowth @4035, @1714, @3634).
        if (tile is TileId.JungleGrass or TileId.CanopyGrass && y > 0 && FloraParity.IsGrassPlant(above.TileId) &&
            _flora.Next(FloraParity.JungleTreeOneIn) == 0 && TilePhysics.TryGrowTree(_world, _tiles, x, y - 1, _flora, _changed,
                FloraParity.TreeMinHeight, FloraParity.TreeMaxHeight))
            TreesGrown++;
        if (tile == TileId.GlowshroomGrass && openAbove &&
            _flora.Next(y < SurfaceRow ? FloraParity.ShroomTreeOneInSurface : FloraParity.ShroomTreeOneInUnderground) == 0 &&
            ContentFillFlora.TryGrowSpecies(_world, x, y - 1, TileId.ShroomStalk, TileId.ShroomCap, TileId.PalmCanopy, _flora, _changed, 3, 6))
            TreesGrown++;
        // Open desert sand sprouts cacti above ground (ref. UpdateWorld_OvergroundTile @2764).
        if (FloraParity.IsCactusSand(tile) && openAbove && y < SurfaceRow && _flora.Next(FloraParity.CactusSproutOneIn) == 0 &&
            _world.TileAt(x - 1, y - 1, TileId.Bedrock) == TileId.Air && _world.TileAt(x + 1, y - 1, TileId.Bedrock) == TileId.Air)
            Grow(x, y - 1, TileId.Cactus);
    }

    /// <summary>
    /// A sapling's growth attempt (ref. GrowTree): the ground under it must be tree ground with tree ground beside it, the
    /// sapling's row and the one above must be clear three wide, and the trunk (5–16 tall, shrunk to the room there is) needs
    /// four clear rows above it for the crown. Returns true when a tree grew.
    /// </summary>
    public static bool TryGrowSapling(TileGrid world, TileCatalog tiles, int x, int y, Random random, List<GridPoint>? changed = null)
    {
        if (!world.Contains(x, y + 1))
            return false;
        var ground = world.TileAt(x, y + 1);
        if (!FloraParity.SaplingGroundFits(ground) || !tiles[ground].IsSolid ||
            !(FloraParity.SaplingGroundFits(world.TileAt(x - 1, y + 1, TileId.Air)) || FloraParity.SaplingGroundFits(world.TileAt(x + 1, y + 1, TileId.Air))))
            return false;
        bool Clear(int cx, int cy)
        {
            if (!world.Contains(cx, cy) || world.GetCell(cx, cy).LiquidUnits > 0)
                return false;
            var t = world.TileAt(cx, cy);
            return t == TileId.Air || t == TileId.Sapling || FloraParity.IsReplaceablePlant(t);
        }
        for (var dx = -1; dx <= 1; dx++)
            if (!Clear(x + dx, y) || !Clear(x + dx, y - 1))
                return false;
        var height = random.Next(FloraParity.TreeMinHeight, FloraParity.TreeMaxHeight + 1);
        // Rows above the sapling that are clear five wide (the crown's room).
        var free = 2;
        while (free < height + FloraParity.TreeTopPadding && Enumerable.Range(-2, 5).All(dx => Clear(x + dx, y - free)))
            free++;
        height = Math.Min(height, free - FloraParity.TreeTopPadding + 1);
        if (height < FloraParity.TreeMinHeight)
            return false;
        return TilePhysics.TryGrowTree(world, tiles, x, y, random, changed, height, height);
    }

    /// <summary>
    /// Herb growth (ref. GrowAlch): the wrong liquid kills the herb; a young herb matures on one update in 50 (the settled
    /// cold herb needs a further one in 2), with an extra one-in-50 chance in its own conditions (sun, night, rain, below the
    /// surface, the molten depths); a mature flickering herb blinks in and out of bloom, and the cold herb settles into bloom
    /// on one update in 30.
    /// </summary>
    private void GrowHerb(int x, int y, WorldCell cell, HerbInfo herb, FloraConditions conditions)
    {
        if (cell.LiquidUnits > 0 && !TilePhysics.HerbSurvivesLiquid(herb.Bloom, cell.LiquidKind))
        {
            Set(x, y, TileId.Air);
            return;
        }
        var state = cell.State;
        if ((state & TilePhysics.HerbMatureBit) == 0)
        {
            bool matures;
            if (_flora.Next(FloraParity.HerbMatureOneIn) == 0)
                matures = herb.Bloom != HerbBloomRule.Settled || _flora.Next(2) == 0;
            else
            {
                var boosted = herb.Bloom switch
                {
                    HerbBloomRule.Daytime => conditions.Daytime,
                    HerbBloomRule.Night => !conditions.Daytime,
                    HerbBloomRule.Rain => conditions.Raining,
                    HerbBloomRule.Flicker => y > SurfaceRow,
                    HerbBloomRule.Dusk => y > _world.Height - _world.Height / 6,
                    _ => false
                };
                matures = boosted && _flora.Next(FloraParity.HerbMatureOneIn) == 0;
            }
            if (matures)
                SetState(x, y, cell, (byte)(state | TilePhysics.HerbMatureBit));
            return;
        }
        if (herb.Bloom == HerbBloomRule.Flicker && _flora.Next(3) != 0)
        {
            if ((state & TilePhysics.HerbBloomBit) == 0)
            {
                if (_flora.Next(2) == 0)
                    SetState(x, y, cell, (byte)(state | TilePhysics.HerbBloomBit));
            }
            else
                SetState(x, y, cell, (byte)(state & ~TilePhysics.HerbBloomBit));
        }
        else if (herb.Bloom == HerbBloomRule.Settled && (state & TilePhysics.HerbBloomBit) == 0 && _flora.Next(FloraParity.SettledBloomOneIn) == 0)
            SetState(x, y, cell, (byte)(state | TilePhysics.HerbBloomBit));
    }

    private void SetState(int x, int y, WorldCell cell, byte state)
    {
        cell.State = state;
        _world.SetCell(new GridPoint(x, y), cell);
        _changed.Add(new GridPoint(x, y));
        PlantGrowths++;
    }

    /// <summary>
    /// The wild-herb roll (ref. PlantAlch): a random column and depth (one in 40 deep, else one in 10 anywhere, else below
    /// the surface), the first ground found going down, open and dry above, fewer than 5 herbs within 15 cells per 4200 of
    /// width; then whichever herb that ground suits.
    /// </summary>
    private void PlantWildHerb()
    {
        var width = _world.Width;
        var height = _world.Height;
        var margin = Math.Min(20, width / 4);
        var x = _flora.Next(margin, Math.Max(margin + 1, width - margin));
        var bottom = Math.Max(3, height - 20);
        var surface = Math.Clamp(SurfaceRow, 1, bottom - 1);
        var deep = Math.Clamp(((surface + height) / 2 + height) / 2, 1, bottom - 1);
        int y;
        if (_flora.Next(40) == 0)
            y = _flora.Next(deep, bottom);
        else if (_flora.Next(10) == 0)
            y = _flora.Next(Math.Min(20, bottom - 1), bottom);
        else
            y = _flora.Next(surface, bottom);
        while (y < bottom && _world.TileAt(x, y) == TileId.Air)
            y++;
        if (y >= bottom || y < 1)
            return;
        var ground = _world.GetCell(x, y);
        if (!_tiles[ground.TileId].IsSolid || ground.IsInactive || _world.TileAt(x, y - 1) != TileId.Air || _world.GetCell(x, y - 1).LiquidUnits > 0)
            return;
        var radius = Math.Max(1, 15 * width / 4200);
        var herbs = 0;
        for (var cx = x - radius; cx <= x + radius; cx++)
        for (var cy = y - radius; cy <= y + radius; cy++)
            if (TilePhysics.IsHerb(_world.TileAt(cx, cy, TileId.Air)))
                herbs++;
        if (herbs >= 5)
            return;
        foreach (var herb in TilePhysics.AllHerbs)
            if (Array.IndexOf(herb.Soils, ground.TileId) >= 0)
            {
                Grow(x, y - 1, herb.Tile);
                HerbsPlanted++;
                return;
            }
    }

    /// <summary>Spreads grass into every neighbouring soil cell that is open to the air and not touching lava (ref. SpreadGrass @263–@455).</summary>
    private void SpreadGrass(int x, int y, TileId grass, TileId[] soils)
    {
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0)
                continue;
            var nx = x + dx;
            var ny = y + dy;
            if (!_world.Contains(nx, ny) || Array.IndexOf(soils, _world.TileAt(nx, ny)) < 0 || !ExposedWithoutLava(nx, ny))
                continue;
            SetKeep(nx, ny, grass);
            GrassRegrowths++;
        }
    }

    /// <summary>True when some cell of the 3×3 around (x, y) is open or not solid, and none holds lava.</summary>
    private bool ExposedWithoutLava(int x, int y)
    {
        var exposed = false;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (!_world.Contains(x + dx, y + dy))
                continue;
            var cell = _world.GetCell(x + dx, y + dy);
            if (cell.LiquidKind == LiquidKind.Lava && cell.LiquidUnits > 0)
                return false;
            if (cell.TileId == TileId.Air || !_tiles[cell.TileId].IsSolid)
                exposed = true;
        }
        return exposed;
    }

    /// <summary>Grows a vine into the open cell below (x, y) unless vines nearby are already too dense (ref. GrowMoreVines).</summary>
    private void GrowVineBelow(int x, int y)
    {
        if (!_world.Contains(x, y + 1))
            return;
        var below = _world.GetCell(x, y + 1);
        if (below.TileId != TileId.Air || below.LiquidKind == LiquidKind.Lava && below.LiquidUnits > 0)
            return;
        var weight = 0;
        for (var cx = x - 4; cx <= x + 4; cx++)
        for (var cy = y - 6; cy <= y + 10; cy++)
        {
            if (_world.TileAt(cx, cy, TileId.Air) != TileId.Vine)
                continue;
            weight++;
            if (cy > y)
                weight += (cy - y) * 2;
            if (weight > FloraParity.VineDensityLimit)
                return;
        }
        Grow(x, y + 1, TileId.Vine);
    }

    /// <summary>Lengthens a cactus column from its top while it is under its height cap (ref. GrowCactus).</summary>
    private void GrowCactus(int x, int y)
    {
        var top = y;
        while (top > 0 && _world.TileAt(x, top - 1) == TileId.Cactus)
            top--;
        var bottom = y;
        while (bottom < _world.Height - 1 && _world.TileAt(x, bottom + 1) == TileId.Cactus)
            bottom++;
        if (!FloraParity.IsCactusSand(_world.TileAt(x, bottom + 1, TileId.Air)) || top < 1 || _world.GetCell(x, top - 1).LiquidUnits > 0)
            return;
        if (FloraParity.CactusMayGrow(bottom - top + 1, _flora))
            Grow(x, top - 1, TileId.Cactus);
    }

    private void Grow(int x, int y, TileId tile)
    {
        if (!_world.Contains(x, y) || _world.TileAt(x, y) != TileId.Air)
            return;
        Set(x, y, tile);
        PlantGrowths++;
    }

    /// <summary>Changes a tile in place (grass spread, plants growing tall) and records it.</summary>
    private void SetKeep(int x, int y, TileId tile)
    {
        Set(x, y, tile);
        PlantGrowths++;
    }

    private void Set(int x, int y, TileId tile)
    {
        var point = new GridPoint(x, y);
        _world.SetTile(point, tile);
        _changed.Add(point);
    }

    private bool TouchesAir(int x, int y) =>
        !_tiles[_world.TileAt(x - 1, y)].IsSolid || !_tiles[_world.TileAt(x + 1, y)].IsSolid ||
        !_tiles[_world.TileAt(x, y - 1)].IsSolid || !_tiles[_world.TileAt(x, y + 1)].IsSolid;

    private bool CrystalNear(int x, int y, int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
            if (_world.TileAt(x + dx, y + dy, TileId.Air) == TileId.EnergyCrystal)
                return true;
        return false;
    }
}
