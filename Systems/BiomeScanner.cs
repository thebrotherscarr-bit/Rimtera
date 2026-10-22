using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>The five Neo-Terra biomes plus plain caverns.</summary>
public enum BiomeZone : byte
{
    Grassland,
    FrozenWasteland,
    JunkWasteland,
    RadiatedDeeps,
    MoltenChasm,
    Caverns,
    /// <summary>The Overgrowth blight (surface zones, chasms and deep caverns).</summary>
    Overgrowth,
    // Content expansion (append-only).
    /// <summary>The fuel oceans and shell beaches at both world edges (displayed as "Fuel Sea"; the id keeps its old name).</summary>
    AcidCoast,
    /// <summary>Underground glowshroom caverns.</summary>
    FungalHollows,
    /// <summary>Surface jungle of mud and canopy grass.</summary>
    VerdantCanopy,
    /// <summary>Underground hive comb chambers under the canopy.</summary>
    BloomHives,
    /// <summary>Post-Fracture crystal biome (competes with the blight).</summary>
    CrystalSpires,
    /// <summary>Silk-strung mite nests.</summary>
    MiteWarrens,
    /// <summary>Inside the precursor vault.</summary>
    PrecursorVault
}

/// <summary>Per-biome tile counts around a point and the zone they resolve to.</summary>
public readonly record struct BiomeReading(
    BiomeZone Zone,
    bool Underground,
    int FrozenTiles,
    int JunkTiles,
    int RadiatedTiles,
    int MoltenTiles,
    int GrassTiles,
    int OvergrowthTiles = 0,
    int CoastTiles = 0,
    int FungalTiles = 0,
    int CanopyTiles = 0,
    int HiveTiles = 0,
    int CrystalTiles = 0,
    int MiteTiles = 0,
    int VaultTiles = 0)
{
    public string DisplayName => BiomeScanner.DisplayName(Zone);
}

/// <summary>
/// Decides the player's biome by counting biome tiles in a window around them, so the
/// zone follows the terrain actually nearby rather than fixed world coordinates.
/// Pattern only (count tile families, compare against thresholds); window, tile sets,
/// thresholds, and priority are Rimtera's own and sized for its smaller worlds.
/// </summary>
public static class BiomeScanner
{
    public const int ScanHalfWidth = 30;
    public const int ScanHalfHeight = 20;
    public const int FrozenThreshold = 110;
    public const int JunkThreshold = 110;
    public const int RadiatedThreshold = 60;
    public const int MoltenThreshold = 60;
    public const int GrassThreshold = 24;
    public const int OvergrowthThreshold = 60;
    /// <summary>
    /// Percentage of the ground skin (the top <see cref="SurfaceSkinDepth"/> solid rows of each
    /// scanned column) that must be blight for a surface reading to become a blight zone.
    /// </summary>
    public const int SurfaceBlightThreshold = 25;
    public const int SurfaceSkinDepth = 8;

    private static readonly HashSet<TileId> FrozenTiles = [TileId.Snow];
    private static readonly HashSet<TileId> JunkTiles = [TileId.DesertSand, TileId.ScrapSandstone];
    private static readonly HashSet<TileId> RadiatedTiles = [TileId.CrimsonSoil, TileId.CorruptRock];
    // The Irradiated Core (the zone id keeps its old MoltenChasm name); legacy ember rock still counts for old saves.
    private static readonly HashSet<TileId> MoltenTiles = [TileId.IrradiatedRock, TileId.CoreCrust, TileId.VitrifiedGlass, TileId.EmberRock];
    private static readonly HashSet<TileId> GrassTiles = [TileId.Grass, TileId.JungleGrass];
    private static readonly HashSet<TileId> OvergrowthTiles = [];
    // Content-expansion families (counted separately; they only override calm zones).
    private static readonly HashSet<TileId> CoastTiles = [TileId.AcidSand, TileId.ShellSand, TileId.ShellBeach, TileId.SaltCrust, TileId.CoralReef];
    private static readonly HashSet<TileId> FungalTiles = [TileId.GlowshroomGrass, TileId.MyceliumStone, TileId.SporeBlock, TileId.MushroomCap];
    private static readonly HashSet<TileId> CanopyTiles = [TileId.Mud, TileId.CanopyGrass];
    private static readonly HashSet<TileId> HiveTiles = [TileId.HiveComb, TileId.Nectar, TileId.BloomHive];
    private static readonly HashSet<TileId> CrystalTiles = [TileId.PrismaticGrass, TileId.PearlStone, TileId.PearlSand, TileId.CrystalSpire, TileId.LumeniteOre, TileId.CrystalAltar];
    private static readonly HashSet<TileId> MiteTiles = [TileId.MiteSilk, TileId.MiteSilkCocoon];
    private static readonly HashSet<TileId> VaultTiles = [TileId.PrecursorBrick, TileId.PrecursorGlyph, TileId.PrecursorVaultDoor];
    public const int CoastThreshold = 70;
    /// <summary>Extra columns past the generated ocean width that still read as ocean (the beach's landward lip).</summary>
    public const int OceanMargin = 12;

    /// <summary>True when column <paramref name="x"/> lies in the ocean band at either world edge.</summary>
    public static bool InOceanBand(TileGrid world, int x)
    {
        var band = WorldGenDensity.CoastWidth.Count(world.Width, world.Height) + OceanMargin;
        band = Math.Min(band, world.Width / 5 + OceanMargin);
        return x < band || x >= world.Width - band;
    }
    public const int FungalThreshold = 50;
    public const int CanopyThreshold = 90;
    public const int HiveThreshold = 50;
    public const int CrystalThreshold = 60;
    public const int MiteThreshold = 12;
    public const int VaultThreshold = 40;

    /// <summary>Lets new biome tiles (ores, crystals) count toward their biome.</summary>
    public static void RegisterBiomeTile(BiomeZone zone, TileId tile)
    {
        var set = zone switch
        {
            BiomeZone.FrozenWasteland => FrozenTiles,
            BiomeZone.JunkWasteland => JunkTiles,
            BiomeZone.RadiatedDeeps => RadiatedTiles,
            BiomeZone.MoltenChasm => MoltenTiles,
            BiomeZone.Grassland => GrassTiles,
            BiomeZone.Overgrowth => OvergrowthTiles,
            _ => null
        };
        set?.Add(tile);
    }

    public static BiomeReading Scan(TileGrid world, GridPoint center)
    {
        ArgumentNullException.ThrowIfNull(world);
        // Small worlds shrink the window (and the thresholds with it) so one scan does not
        // swallow most of the map and blend every biome together.
        var halfWidth = Math.Min(ScanHalfWidth, Math.Max(8, world.Width / 6));
        var halfHeight = Math.Min(ScanHalfHeight, Math.Max(6, world.Height / 4));
        var scale = (2 * halfWidth + 1) * (2 * halfHeight + 1) / (double)((2 * ScanHalfWidth + 1) * (2 * ScanHalfHeight + 1));
        int frozen = 0, junk = 0, radiated = 0, molten = 0, grass = 0, overgrowth = 0;
        int coast = 0, fungal = 0, canopy = 0, hive = 0, crystal = 0, mite = 0, vault = 0;
        // Ocean water only counts inside the edge bands, so inland lakes stay part of their own biome.
        var oceanBand = InOceanBand(world, center.X);

        var minX = Math.Max(0, center.X - halfWidth);
        var maxX = Math.Min(world.Width - 1, center.X + halfWidth);
        var minY = Math.Max(0, center.Y - halfHeight);
        var maxY = Math.Min(world.Height - 1, center.Y + halfHeight);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var cell = world.GetCell(new GridPoint(x, y));
            if (cell.LiquidKind == LiquidKind.Lava && cell.LiquidDepth > 0.2f)
                molten += 2;
            else if (oceanBand && cell.LiquidKind is LiquidKind.Fuel or LiquidKind.Water or LiquidKind.Acid && cell.LiquidDepth > 0.2f)
                coast++;
            else if (cell.LiquidKind == LiquidKind.Nectar && cell.LiquidDepth > 0.2f)
                hive++;
            var tile = cell.TileId;
            if (tile == TileId.Air)
                continue;
            if (FrozenTiles.Contains(tile))
                frozen++;
            else if (JunkTiles.Contains(tile))
                junk++;
            else if (RadiatedTiles.Contains(tile))
                radiated++;
            else if (OvergrowthTiles.Contains(tile))
                overgrowth++;
            else if (MoltenTiles.Contains(tile))
                molten++;
            else if (GrassTiles.Contains(tile))
                grass++;
            else if ((int)tile >= (int)TileId.Mud)
            {
                if (CoastTiles.Contains(tile)) { if (oceanBand) coast++; }
                else if (FungalTiles.Contains(tile)) fungal++;
                else if (CanopyTiles.Contains(tile)) canopy++;
                else if (HiveTiles.Contains(tile)) hive++;
                else if (CrystalTiles.Contains(tile)) crystal++;
                else if (MiteTiles.Contains(tile)) mite++;
                else if (VaultTiles.Contains(tile)) vault++;
            }
        }

        // Underground means below the old depth line and also a few cells under this column's
        // ground, so a tall hill does not read as a cave.
        var underground = center.Y > world.Height * 0.45f && center.Y > ColumnGround(world, center.X) + 3;
        // Scale counts back to the reference window so the published thresholds still apply.
        int Norm(int count) => scale >= 0.999 ? count : (int)Math.Round(count / scale);
        // Surface blight: share of the ground skin (first solid rows of each column) that is blight.
        int surfaceRadiated = 0, surfaceOvergrowth = 0;
        if (!underground && (radiated > 0 || overgrowth > 0))
        {
            int skinCells = 0, skinRadiated = 0, skinOvergrowth = 0;
            for (var x = minX; x <= maxX; x++)
            {
                var ground = ColumnGround(world, x);
                for (var y = ground; y < Math.Min(world.Height, ground + SurfaceSkinDepth); y++)
                {
                    var tile = world.GetCell(new GridPoint(x, y)).TileId;
                    skinCells++;
                    if (RadiatedTiles.Contains(tile))
                        skinRadiated++;
                    else if (OvergrowthTiles.Contains(tile))
                        skinOvergrowth++;
                }
            }
            if (skinCells > 0)
            {
                surfaceRadiated = skinRadiated * 100 / skinCells;
                surfaceOvergrowth = skinOvergrowth * 100 / skinCells;
            }
        }
        var zone = Classify(underground, Norm(frozen), Norm(junk), Norm(radiated), Norm(molten), Norm(grass),
            Norm(overgrowth), surfaceRadiated, surfaceOvergrowth);
        zone = ClassifyContent(zone, underground, Norm(coast), Norm(fungal), Norm(canopy), Norm(hive), Norm(crystal), Norm(mite), Norm(vault));
        return new BiomeReading(zone, underground, frozen, junk, radiated, molten, grass, overgrowth,
            coast, fungal, canopy, hive, crystal, mite, vault);
    }

    private static int ColumnGround(TileGrid world, int x)
    {
        x = Math.Clamp(x, 0, world.Width - 1);
        for (var y = 0; y < world.Height; y++)
        {
            var tile = world.GetCell(new GridPoint(x, y)).TileId;
            if (tile != TileId.Air && tile is not (TileId.MiniTurret or TileId.HydroponicsTray or TileId.SolarPanel or TileId.OxygenUnit))
                return y;
        }
        return world.Height - 1;
    }

    /// <summary>
    /// Underground, the hazard biomes win over surface biomes; otherwise the larger of the
    /// frozen/junk counts wins, falling back to Grassland (surface) or Caverns (underground).
    /// </summary>
    public static BiomeZone Classify(bool underground, int frozen, int junk, int radiated, int molten, int grass,
        int overgrowth = 0, int surfaceRadiatedPercent = 0, int surfaceOvergrowthPercent = 0)
    {
        // The Radiated Deeps and Molten Chasm lie below the crust; on the surface only the
        // surface biomes compete, unless a blight zone covers the ground right around the player.
        if (underground && molten >= MoltenThreshold)
            return BiomeZone.MoltenChasm;
        if (underground && (radiated >= RadiatedThreshold || overgrowth >= OvergrowthThreshold))
            return radiated >= overgrowth ? BiomeZone.RadiatedDeeps : BiomeZone.Overgrowth;
        if (!underground && (surfaceRadiatedPercent >= SurfaceBlightThreshold || surfaceOvergrowthPercent >= SurfaceBlightThreshold))
            return surfaceRadiatedPercent >= surfaceOvergrowthPercent ? BiomeZone.RadiatedDeeps : BiomeZone.Overgrowth;
        var frozenZone = frozen >= FrozenThreshold;
        var junkZone = junk >= JunkThreshold;
        if (frozenZone && (!junkZone || frozen >= junk))
            return BiomeZone.FrozenWasteland;
        if (junkZone)
            return BiomeZone.JunkWasteland;
        // Open ground with no dominant hazard biome reads as grassland on the surface.
        return underground && grass < GrassThreshold ? BiomeZone.Caverns : BiomeZone.Grassland;
    }

    /// <summary>
    /// Content-expansion zones override only the calm readings (grassland, caverns, frozen, junk);
    /// molten and blight readings keep priority, so blight and crystal compete tile by tile.
    /// </summary>
    public static BiomeZone ClassifyContent(BiomeZone zone, bool underground, int coast, int fungal, int canopy, int hive,
        int crystal, int mite, int vault)
    {
        if (zone is BiomeZone.MoltenChasm or BiomeZone.RadiatedDeeps or BiomeZone.Overgrowth)
            return zone;
        if (vault >= VaultThreshold)
            return BiomeZone.PrecursorVault;
        if (crystal >= CrystalThreshold)
            return BiomeZone.CrystalSpires;
        if (underground)
        {
            if (hive >= HiveThreshold) return BiomeZone.BloomHives;
            if (fungal >= FungalThreshold) return BiomeZone.FungalHollows;
            if (mite >= MiteThreshold) return BiomeZone.MiteWarrens;
        }
        else
        {
            if (coast >= CoastThreshold) return BiomeZone.AcidCoast;
            if (canopy >= CanopyThreshold) return BiomeZone.VerdantCanopy;
        }
        return zone;
    }

    public static string DisplayName(BiomeZone zone) => zone switch
    {
        BiomeZone.AcidCoast => "Fuel Sea",
        BiomeZone.FungalHollows => "Fungal Hollows",
        BiomeZone.VerdantCanopy => "Verdant Canopy",
        BiomeZone.BloomHives => "Bloom Hives",
        BiomeZone.CrystalSpires => "Crystal Spires",
        BiomeZone.MiteWarrens => "Mite Warrens",
        BiomeZone.PrecursorVault => "Precursor Vault",
        BiomeZone.FrozenWasteland => "Frozen Wasteland",
        BiomeZone.JunkWasteland => "Junk Wasteland",
        BiomeZone.RadiatedDeeps => "Radiated Deeps",
        BiomeZone.MoltenChasm => "Irradiated Core",
        BiomeZone.Caverns => "Caverns",
        BiomeZone.Overgrowth => "Overgrowth",
        _ => "Grassland"
    };
}
