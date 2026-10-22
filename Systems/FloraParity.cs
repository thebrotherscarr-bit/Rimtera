using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>The family a Rimtera tree belongs to for the flora rules (drops, shaking, acorn chances).</summary>
public enum TreeFamily : byte
{
    None,
    /// <summary>Grassland round tree.</summary>
    Grove,
    /// <summary>Frost pine.</summary>
    Frost,
    /// <summary>Rust palm and fuel palm (sand and beach).</summary>
    Palm,
    /// <summary>Canopy vinewood (the jungle family: no acorns from felling).</summary>
    Canopy,
    /// <summary>Shroom stalk (the mushroom family: no acorns, sheds glow spores).</summary>
    Shroom,
    /// <summary>Prismwood (the crystal family).</summary>
    Crystal,
    /// <summary>Blightwood.</summary>
    Blight,
    /// <summary>Geode (gem) tree.</summary>
    Geode
}

/// <summary>Why a plant is being cut: a melee swing, a projectile or the player's tool.</summary>
public enum FloraCut : byte
{
    Swing,
    Projectile
}

/// <summary>World conditions the flora rules read (herb blooms, rain-boosted updates).</summary>
public readonly record struct FloraConditions(bool Daytime, float DayProgress, bool Raining, int MoonPhase)
{
    /// <summary>Daytime at noon with clear skies (tests, legacy callers).</summary>
    public static FloraConditions Default => new(true, 0.5f, false, 0);
}

/// <summary>
/// Rimtera's flora rules, matched to the reference game's numbers (docs/FLORA_PARITY.md has one row per rule with its IL
/// citation). Everything here is Rimtera's own code and naming; the reference is used only as behavioural evidence.
/// Pure functions and constants so the ticker, the simulation and world generation share them.
/// </summary>
public static class FloraParity
{
    // ------------------------------------------------------------------ random tile updates

    /// <summary>Above-ground random updates per tick, as a fraction of every cell in the world (ref. UpdateWorld @256).</summary>
    public const double OvergroundUpdatesPerCell = 3e-5;
    /// <summary>Below-ground random updates per tick, as a fraction of every cell in the world (ref. UpdateWorld @277).</summary>
    public const double UndergroundUpdatesPerCell = 1.5e-5;
    /// <summary>Rain multiplies the above-ground updates (ref. UpdateWorld @421–@440).</summary>
    public const double RainUpdateMultiplier = 1.5;
    /// <summary>
    /// Rimtera's world-surface line as a fraction of the world height (the same depth line the biome scanner uses). The
    /// reference splits its updates at its own surface row; this is Rimtera's equivalent row.
    /// </summary>
    public const double SurfaceFraction = 0.45;

    public static int SurfaceRow(int height) => (int)(height * SurfaceFraction);

    /// <summary>
    /// One-in chance per above-ground random update that a wild herb is planted somewhere (ref. UpdateWorld @442–@547:
    /// <c>Next(Lerp(151, 151 × 2.8, clamp(width / 4200 − 1)) × 100) == 0</c>).
    /// </summary>
    public static int HerbSpawnOneIn(int width)
    {
        var t = Math.Clamp(width / 4200.0 - 1.0, 0.0, 1.0);
        return (int)(151 + (151 * 2.8 - 151) * t) * 100;
    }

    // ------------------------------------------------------------------ trees

    /// <summary>A sapling grows on one random update in 20 (ref. UpdateWorld_OvergroundTile @4374–@4396).</summary>
    public const int SaplingGrowOneIn = 20;
    /// <summary>Trunk height is Next(5, 17) (ref. GrowTree @500–@513).</summary>
    public const int TreeMinHeight = 5;
    public const int TreeMaxHeight = 16;
    /// <summary>Clear rows needed above the trunk for the crown (ref. GrowTree @602–@606: height + 4).</summary>
    public const int TreeTopPadding = 4;
    /// <summary>A canopy (jungle) plant on jungle grass becomes a tree on one update in 500 (ref. GrassGrowth @4035–@4133).</summary>
    public const int JungleTreeOneIn = 500;
    /// <summary>Glowshroom grass grows a shroom tree on one update in 250 above ground, 300 below (ref. GrassGrowth @1714, @3634).</summary>
    public const int ShroomTreeOneInSurface = 250;
    public const int ShroomTreeOneInUnderground = 300;

    /// <summary>The family of a trunk (or crown) tile.</summary>
    public static TreeFamily FamilyOf(TileId tile) => tile switch
    {
        TileId.TreeTrunk or TileId.TreeCanopy => TreeFamily.Grove,
        TileId.PineTrunk or TileId.PineCanopy => TreeFamily.Frost,
        TileId.PalmTrunk or TileId.PalmCanopy or TileId.PalmwoodTrunk or TileId.PalmwoodFronds => TreeFamily.Palm,
        TileId.VinewoodTrunk or TileId.VinewoodCanopy => TreeFamily.Canopy,
        TileId.ShroomStalk or TileId.ShroomCap => TreeFamily.Shroom,
        TileId.PrismwoodTrunk or TileId.PrismwoodCanopy => TreeFamily.Crystal,
        TileId.BlightwoodTrunk or TileId.BlightwoodCanopy => TreeFamily.Blight,
        TileId.GemTreeTrunk => TreeFamily.Geode,
        _ when ContentFillFlora.GemCrowns.Any(gem => gem.Canopy == tile) => TreeFamily.Geode,
        _ => TreeFamily.None
    };

    /// <summary>Felled tops and branches drop acorns (saplings) except for the canopy and shroom families (ref. TreeTypeDropsAcorns).</summary>
    public static bool FellDropsSaplings(TreeFamily family) => family is not (TreeFamily.None or TreeFamily.Canopy or TreeFamily.Shroom or TreeFamily.Geode);

    /// <summary>Shaking drops acorns only from grove, frost and crystal trees (ref. ShakeTree @962–@1045: tree types 1, 6, 7, 12).</summary>
    public static bool ShakeDropsSaplings(TreeFamily family) => family is TreeFamily.Grove or TreeFamily.Frost or TreeFamily.Crystal;

    /// <summary>A leafy top or branch drops an acorn on one roll in 2 (ref. KillTile_GetTreeDrops @31–@67).</summary>
    public const int TopSaplingOneIn = 2;

    /// <summary>The reference's <c>axe</c> field is the tooltip power ÷ 5; Rimtera's cutter power reads like that tooltip.</summary>
    public static int AxeField(int cutterPower) => Math.Max(0, cutterPower / 5);

    /// <summary>
    /// Each felled segment's wood stack gains one when <c>Next(35) ≤ axe</c>, otherwise on one roll in 3
    /// (ref. KillTile_GetTreeDrops @255–@344).
    /// </summary>
    public static int SegmentWood(int axe, Random random) => 1 + (random.Next(35) <= axe || random.Next(3) == 0 ? 1 : 0);

    /// <summary>
    /// Ground a sapling may grow on (ref. IsTileTypeFitForTree: grass, blight grass, jungle grass, mushroom grass, hallowed
    /// grass and snow; palms on sand via GrowPalmTree). Bare soil and mud are not tree ground.
    /// </summary>
    public static bool SaplingGroundFits(TileId ground) => ground is
        TileId.Grass or TileId.HallowGrass or TileId.PrismaticGrass or TileId.JungleGrass or TileId.CanopyGrass or
        TileId.GlowshroomGrass or TileId.Snow or TileId.OvergrowthSoil or TileId.CrimsonSoil or TileId.OvergrowthIce or
        TileId.Sand or TileId.DesertSand or TileId.ShellBeach or TileId.ShellSand or TileId.PearlSand or TileId.RadiatedSand or
        TileId.OvergrowthSand or TileId.RadiatedIce;

    // ------------------------------------------------------------------ herbs

    /// <summary>Young herbs mature on one update in 50 (ref. GrowAlch @172–@248); the cold herb needs a further one in 2.</summary>
    public const int HerbMatureOneIn = 50;
    /// <summary>The cold herb, once mature, settles into bloom on one update in 30 (ref. GrowAlch @1029–@1099).</summary>
    public const int SettledBloomOneIn = 30;

    // ------------------------------------------------------------------ grass and plants

    /// <summary>A short grass plant grows tall on one update in 20 (ref. UpdateWorld_OvergroundTile @4519–@4594).</summary>
    public const int TallGrassOneIn = 20;

    /// <summary>
    /// One-in chance per random update that a grass tile sprouts a plant on its open top (ref. GrassGrowth @2146–@2339:
    /// 12 on grassland grass, 10 on hallowed and blighted grass; @3904 7 on jungle grass; @3514 10 on mushroom grass).
    /// Null for ground that grows no plants.
    /// </summary>
    public static int? PlantOneIn(TileId ground) => ground switch
    {
        TileId.Grass => 12,
        TileId.HallowGrass or TileId.PrismaticGrass or TileId.OvergrowthSoil or TileId.CrimsonSoil => 10,
        TileId.JungleGrass or TileId.CanopyGrass => 7,
        TileId.GlowshroomGrass => 10,
        _ => null
    };

    /// <summary>Ground that carries grass plants (the plant pops off any other ground).</summary>
    public static bool IsPlantGround(TileId ground) => PlantOneIn(ground) is not null;

    /// <summary>
    /// The soil a grass spreads into (ref. GrassGrowth @2700–@3063 and SpreadGrass: grass and hallowed grass into dirt,
    /// jungle and mushroom grass into mud). Null for grass that does not spread through this rule (blight spreads through
    /// <see cref="BlightRules"/>).
    /// </summary>
    public static TileId[]? SpreadSoils(TileId grass) => grass switch
    {
        TileId.Grass or TileId.HallowGrass or TileId.PrismaticGrass => [TileId.Soil],
        TileId.JungleGrass or TileId.CanopyGrass => [TileId.Mud],
        TileId.GlowshroomGrass => [TileId.Mud, TileId.FungalSoil],
        _ => null
    };

    /// <summary>Grassland and hallowed grass only spread above the surface line (ref. SpreadGrass @239–@258).</summary>
    public static bool SpreadsOnlyAboveSurface(TileId grass) => grass is TileId.Grass or TileId.HallowGrass or TileId.PrismaticGrass;

    /// <summary>Hallowed (crystal) grass spreads like an infection: only after the Fracture (ref. AllowedToSpreadInfections).</summary>
    public static bool SpreadsOnlyAfterFracture(TileId grass) => grass is TileId.HallowGrass or TileId.PrismaticGrass;

    /// <summary>Grass tiles whose undersides grow vines (ref. UpdateWorld_OvergroundTile @4946–@5135).</summary>
    public static bool GrowsVines(TileId ground) => ground is TileId.Grass or TileId.JungleGrass or TileId.CanopyGrass or TileId.HallowGrass;

    /// <summary>
    /// Vine density limit (ref. GrowMoreVines): vines within 4 columns and from 6 rows above to 10 rows below weigh
    /// 1 + 2 × (rows below the growing cell); a new vine grows only while the total stays at or under 60.
    /// </summary>
    public const int VineDensityLimit = 60;

    /// <summary>A cactus grows on one update in 15 (ref. UpdateWorld_OvergroundTile @2104–@2144).</summary>
    public const int CactusGrowOneIn = 15;
    /// <summary>Open desert sand sprouts a cactus on one update in 300 (ref. UpdateWorld_OvergroundTile @2764–@2786).</summary>
    public const int CactusSproutOneIn = 300;

    /// <summary>
    /// A cactus column stops when its cells reach Next(11, 13) (ref. GrowCactus @1041–@1057) or its stem reaches
    /// Next(2, 8) (ref. GrowCactus @1698–@1712). Rimtera cacti are single columns, so both read as height caps.
    /// </summary>
    public static bool CactusMayGrow(int height, Random random) => height < random.Next(11, 13) && height < random.Next(2, 8);

    /// <summary>Sand that sprouts cacti.</summary>
    public static bool IsCactusSand(TileId ground) => ground is TileId.DesertSand or TileId.Sand;

    // ------------------------------------------------------------------ cutting

    /// <summary>Grass plants (short and tall).</summary>
    public static bool IsGrassPlant(TileId tile) => tile is TileId.GrassPlant or TileId.TallGrassPlant;

    /// <summary>
    /// Tiles a melee swing or a projectile cuts (the reference's cut set: grass plants, tall grass, flowers, vines, herbs,
    /// mushrooms, dye plants, cattails, lily pads, seaweed, grown pumpkins and pots).
    /// </summary>
    public static bool IsCuttable(TileId tile) =>
        IsGrassPlant(tile) || TilePhysics.IsHerb(tile) || TilePhysics.IsPot(tile) || ContentFillFlora.IsWildPlant(tile) ||
        ContentTiles.IsDyeBlossom(tile) ||
        tile is TileId.Vine or TileId.LongMoss or TileId.WildMushroom or TileId.WildGourd or TileId.Reed or TileId.Lilypad or TileId.Seaweed;

    /// <summary>Planters protect what grows in them from swings and shots (ref. CanCutTile @14–@119: clay pot, planter box).</summary>
    public static bool IsPlanter(TileId tile) => tile is TileId.PlantPot or TileId.HerbPlanter;

    /// <summary>Plants a placed block simply replaces (ref. PlaceThing_Tiles_PlaceIt_KillGrassForSolids).</summary>
    public static bool IsReplaceablePlant(TileId tile) => IsGrassPlant(tile);

    // ------------------------------------------------------------------ shaking

    /// <summary>
    /// One tree shake (ref. ShakeTree, first matching roll wins): sapling 1 in 7 (grove, frost, crystal) ×1–2; spores 1 in 8
    /// (shroom); wood 1 in 12 ×1–3; credits 1 in 20; a critter 1 in 15 (grove, crystal); a night glider 1 in 50 (grove, night);
    /// a moth 1 in 20 (grove or crystal, clear day); fruit 1 in 12 (every leafy family). Returns what fell.
    /// </summary>
    public static ShakeResult RollShake(TreeFamily family, FloraConditions conditions, Random random)
    {
        if (ShakeDropsSaplings(family) && random.Next(7) == 0)
            return new(ShakeKind.Sapling, random.Next(1, 3));
        if (family == TreeFamily.Shroom && random.Next(8) == 0)
            return new(ShakeKind.Spores, 1);
        if (random.Next(12) == 0)
            return new(ShakeKind.Wood, random.Next(1, 4));
        if (random.Next(20) == 0)
            return new(ShakeKind.Credits, ShakeCredits(random));
        if (family is TreeFamily.Grove or TreeFamily.Crystal && random.Next(15) == 0)
            return new(ShakeKind.Critter, 1);
        if (family == TreeFamily.Grove && !conditions.Daytime && random.Next(50) == 0)
            return new(ShakeKind.NightCritter, 1);
        if (family is TreeFamily.Grove or TreeFamily.Crystal && conditions.Daytime && !conditions.Raining && random.Next(20) == 0)
            return new(ShakeKind.Moth, 1);
        if (family is not (TreeFamily.Shroom or TreeFamily.Geode or TreeFamily.None) && random.Next(12) == 0)
            return new(ShakeKind.Fruit, 1);
        return new(ShakeKind.Nothing, 0);
    }

    /// <summary>
    /// Shake coins (ref. ShakeTree @1331–@1525): 50–99 copper; one in 30 gold ×1–3; else one in 10 silver ×1–20.
    /// Rimtera pays 1 credit per 10 copper (silver = 10 credits); gold is capped at 100 credits a coin.
    /// </summary>
    public static int ShakeCredits(Random random)
    {
        if (random.Next(30) == 0)
        {
            var gold = 1 + (random.Next(5) == 0 ? 1 : 0) + (random.Next(10) == 0 ? 1 : 0);
            return gold * 100;
        }
        if (random.Next(10) == 0)
            return random.Next(1, 21) * 10;
        return random.Next(50, 100) / 10;
    }
}

/// <summary>What a shaken tree dropped.</summary>
public enum ShakeKind : byte
{
    Nothing,
    Sapling,
    Spores,
    Wood,
    Credits,
    Critter,
    NightCritter,
    Moth,
    Fruit
}

/// <summary>The outcome of one tree shake.</summary>
public readonly record struct ShakeResult(ShakeKind Kind, int Count);
