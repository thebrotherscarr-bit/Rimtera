namespace Rimtera.World;

public readonly record struct GenerationProgress(string PassName, double PassFraction, double TotalFraction);

public interface IWorldGenerationPass
{
    string Name { get; }
    double Weight { get; }
    void Apply(TileGrid grid, TileCatalog catalog, Random random, CancellationToken cancellationToken);
}

/// <summary>Runs named generation stages in order with independent deterministic random streams.</summary>
public sealed class WorldGenerationPipeline
{
    private readonly List<IWorldGenerationPass> _passes = new();

    public IReadOnlyList<IWorldGenerationPass> Passes => _passes;

    public void AddPass(IWorldGenerationPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (pass.Weight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pass), "Pass weight must be positive.");
        _passes.Add(pass);
    }

    public void Generate(
        TileGrid grid,
        TileCatalog catalog,
        int seed,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(catalog);

        var totalWeight = _passes.Sum(pass => pass.Weight);
        var completedWeight = 0d;
        foreach (var pass in _passes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new GenerationProgress(pass.Name, 0, completedWeight / totalWeight));
            pass.Apply(grid, catalog, new Random(DeriveSeed(seed, pass.Name)), cancellationToken);
            completedWeight += pass.Weight;
            progress?.Report(new GenerationProgress(pass.Name, 1, completedWeight / totalWeight));
        }
    }

    private static int DeriveSeed(int seed, string name)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in name)
            {
                hash ^= character;
                hash *= 16777619;
            }
            return (int)(hash ^ (uint)seed);
        }
    }
}

public sealed class TerrainPass : IWorldGenerationPass
{
    public string Name => "Terrain profile";
    public double Weight => 1.5;

    public void Apply(TileGrid grid, TileCatalog catalog, Random random, CancellationToken cancellationToken)
    {
        var segmentWidth = Math.Max(4, grid.Width / 12);
        var controlCount = (grid.Width + segmentWidth - 1) / segmentWidth + 1;
        var low = Math.Clamp(grid.Height / 3, 1, grid.Height - 1);
        var high = Math.Clamp(grid.Height * 2 / 3, low, grid.Height - 1);
        var controls = new int[controlCount];
        for (var i = 0; i < controls.Length; i++)
            controls[i] = random.Next(low, high + 1);

        for (var x = 0; x < grid.Width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segment = Math.Min(x / segmentWidth, controls.Length - 2);
            var fraction = (x - segment * segmentWidth) / (double)segmentWidth;
            var eased = fraction * fraction * (3 - 2 * fraction);
            var surface = Math.Clamp(
                (int)Math.Round(controls[segment] + (controls[segment + 1] - controls[segment]) * eased),
                0,
                grid.Height - 1);

            for (var y = surface; y < grid.Height; y++)
                grid.SetTile(new GridPoint(x, y), y - surface < 4 ? TileId.Soil : TileId.Rock);
        }
    }
}

public sealed class CavePass : IWorldGenerationPass
{
    public string Name => "Cave pockets";
    public double Weight => 1.2;

    public void Apply(TileGrid grid, TileCatalog catalog, Random random, CancellationToken cancellationToken)
    {
        var pocketCount = Math.Max(1, grid.Count / 1100);
        for (var pocket = 0; pocket < pocketCount; pocket++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var centerX = random.Next(grid.Width);
            var centerY = random.Next(Math.Max(1, grid.Height / 2), grid.Height);
            var radiusX = random.Next(2, 7);
            var radiusY = random.Next(2, 5);

            for (var y = centerY - radiusY; y <= centerY + radiusY; y++)
            for (var x = centerX - radiusX; x <= centerX + radiusX; x++)
            {
                var dx = (x - centerX) / (double)radiusX;
                var dy = (y - centerY) / (double)radiusY;
                var point = new GridPoint(x, y);
                if (dx * dx + dy * dy <= 1 && grid.Contains(point))
                    grid.SetTile(point, TileId.Air);
            }
        }
    }
}

public sealed class OreVeinPass : IWorldGenerationPass
{
    public string Name => "Ore clusters";
    public double Weight => 1.0;

    public void Apply(TileGrid grid, TileCatalog catalog, Random random, CancellationToken cancellationToken)
    {
        var veinCount = Math.Max(1, grid.Count / 850);
        for (var vein = 0; vein < veinCount; vein++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var x = random.Next(grid.Width);
            var y = random.Next(Math.Max(1, grid.Height / 2), grid.Height);
            var steps = random.Next(6, 16);

            for (var step = 0; step < steps; step++)
            {
                var radius = random.Next(1, 3);
                for (var oy = -radius; oy <= radius; oy++)
                for (var ox = -radius; ox <= radius; ox++)
                {
                    if (ox * ox + oy * oy > radius * radius + 1)
                        continue;

                    var point = new GridPoint(x + ox, y + oy);
                    if (grid.Contains(point) && grid.GetCell(point).TileId == TileId.Rock)
                        grid.SetTile(point, TileId.CopperOre);
                }

                x = Math.Clamp(x + random.Next(-1, 2), 0, grid.Width - 1);
                y = Math.Clamp(y + random.Next(-1, 2), 0, grid.Height - 1);
            }
        }
    }
}

/// <summary>
/// Deeper ore bands: cobalt below the ferrite layer and viridium in the deepest rock.
/// Veins only replace plain rock so ferrite and cave space are preserved.
/// </summary>
public sealed class DeepOrePass : IWorldGenerationPass
{
    public string Name => "Deep ore bands";
    public double Weight => 0.8;

    public void Apply(TileGrid grid, TileCatalog catalog, Random random, CancellationToken cancellationToken)
    {
        CarveVeins(grid, random, TileId.CobaltOre, Math.Max(1, grid.Count / 1500),
            minY: grid.Height * 72 / 100, steps: (5, 12), cancellationToken);
        CarveVeins(grid, random, TileId.ViridiumOre, Math.Max(1, grid.Count / 2600),
            minY: grid.Height * 86 / 100, steps: (4, 9), cancellationToken);
    }

    private static void CarveVeins(TileGrid grid, Random random, TileId ore, int veinCount, int minY,
        (int Min, int Max) steps, CancellationToken cancellationToken)
    {
        minY = Math.Clamp(minY, 1, grid.Height - 1);
        for (var vein = 0; vein < veinCount; vein++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var x = random.Next(grid.Width);
            var y = random.Next(minY, grid.Height);
            var length = random.Next(steps.Min, steps.Max + 1);
            for (var step = 0; step < length; step++)
            {
                var radius = random.Next(0, 2);
                for (var oy = -radius; oy <= radius; oy++)
                for (var ox = -radius; ox <= radius; ox++)
                {
                    var point = new GridPoint(x + ox, y + oy);
                    if (point.Y >= minY && grid.Contains(point) && grid.GetCell(point).TileId == TileId.Rock)
                        grid.SetTile(point, ore);
                }

                x = Math.Clamp(x + random.Next(-1, 2), 0, grid.Width - 1);
                y = Math.Clamp(y + random.Next(-1, 2), minY, grid.Height - 1);
            }
        }
    }
}

/// <summary>Rare energy-crystal clusters that grow into the rock walls of underground caves.</summary>
public sealed class CrystalClusterPass : IWorldGenerationPass
{
    public string Name => "Energy crystal clusters";
    public double Weight => 0.4;

    public void Apply(TileGrid grid, TileCatalog catalog, Random random, CancellationToken cancellationToken)
    {
        var clusters = Math.Max(1, grid.Count / 1900);
        var attempts = clusters * 40;
        for (var attempt = 0; attempt < attempts && clusters > 0; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var x = random.Next(1, Math.Max(2, grid.Width - 1));
            var y = random.Next(Math.Max(1, grid.Height / 2), Math.Max(2, grid.Height - 1));
            if (grid.GetCell(new GridPoint(x, y)).TileId != TileId.Air)
                continue;

            // Seed on the first rock face touching this open cave cell, then spread a few crystals.
            var grown = 0;
            foreach (var (dx, dy) in new[] { (0, 1), (0, -1), (-1, 0), (1, 0) })
            {
                var face = new GridPoint(x + dx, y + dy);
                if (!grid.Contains(face) || grid.GetCell(face).TileId != TileId.Rock)
                    continue;
                grid.SetTile(face, TileId.EnergyCrystal);
                grown++;
                var size = random.Next(1, 4);
                for (var extra = 0; extra < size; extra++)
                {
                    var next = new GridPoint(face.X + random.Next(-1, 2), face.Y + random.Next(-1, 2));
                    if (grid.Contains(next) && grid.GetCell(next).TileId == TileId.Rock)
                    {
                        grid.SetTile(next, TileId.EnergyCrystal);
                        grown++;
                    }
                }
                break;
            }
            if (grown > 0)
                clusters--;
        }
    }
}

/// <summary>
/// The seeded biome geography of one world: jittered surface borders (Frozen Wasteland on the
/// left, Grassland in the centre, Junk Wasteland on the right), the top of the Molten Chasm
/// per column, and the Radiated Deeps pockets. Built by <see cref="BiomePass"/>.
/// </summary>
public sealed class BiomeLayout
{
    private readonly int[] _frozenEdge;
    private readonly int[] _junkStart;
    private readonly int[] _surface;
    private readonly int[] _moltenTop;
    private readonly List<RadiatedPocket> _pockets = new();

    internal BiomeLayout(int width, int height, int[] frozenEdge, int[] junkStart, int[] surface, int[] moltenTop)
    {
        Width = width;
        Height = height;
        _frozenEdge = frozenEdge;
        _junkStart = junkStart;
        _surface = surface;
        _moltenTop = moltenTop;
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<RadiatedPocket> RadiatedPockets => _pockets;

    /// <summary>First column that is no longer Frozen Wasteland on row <paramref name="y"/>.</summary>
    public int FrozenEdgeAt(int y) => _frozenEdge[Math.Clamp(y, 0, Height - 1)];
    /// <summary>First column of the Junk Wasteland on row <paramref name="y"/>.</summary>
    public int JunkStartAt(int y) => _junkStart[Math.Clamp(y, 0, Height - 1)];
    /// <summary>Generated surface row of column <paramref name="x"/> (before any player edits).</summary>
    public int SurfaceAt(int x) => _surface[Math.Clamp(x, 0, Width - 1)];
    public int MoltenTopAt(int x) => _moltenTop[Math.Clamp(x, 0, Width - 1)];

    public bool IsFrozen(int x, int y) => x < FrozenEdgeAt(y);
    public bool IsJunk(int x, int y) => x >= JunkStartAt(y);
    public bool IsMolten(int x, int y) => y >= MoltenTopAt(x);

    internal void AddPocket(RadiatedPocket pocket) => _pockets.Add(pocket);

    // Raw per-column / per-row arrays for saving the layout with the world (no regeneration on load).
    internal int[] SurfaceRows => _surface;
    internal int[] MoltenRows => _moltenTop;
    internal int[] FrozenEdges => _frozenEdge;
    internal int[] JunkStarts => _junkStart;

    /// <summary>Rebuilds a saved layout; null when the arrays do not match the world size.</summary>
    internal static BiomeLayout? FromSaved(int width, int height, int[]? surface, int[]? moltenTop, int[]? frozenEdge,
        int[]? junkStart, int[]? pockets)
    {
        if (surface is null || moltenTop is null || frozenEdge is null || junkStart is null ||
            surface.Length != width || moltenTop.Length != width || frozenEdge.Length != height || junkStart.Length != height ||
            (pockets is not null && pockets.Length % 4 != 0))
            return null;
        var layout = new BiomeLayout(width, height, (int[])frozenEdge.Clone(), (int[])junkStart.Clone(),
            (int[])surface.Clone(), (int[])moltenTop.Clone());
        if (pockets is not null)
            for (var i = 0; i < pockets.Length; i += 4)
                layout.AddPocket(new RadiatedPocket(new GridPoint(pockets[i], pockets[i + 1]), pockets[i + 2], pockets[i + 3]));
        return layout;
    }
}

public readonly record struct RadiatedPocket(GridPoint Center, int RadiusX, int RadiusY);

/// <summary>
/// Lays out Neo-Terra's biomes over the carved terrain. Only plain soil/rock is converted,
/// so ferrite/cobalt/viridium bands and crystal clusters survive. Biome ores grow as
/// random-walk veins that may only replace their own biome's tiles.
/// </summary>
public sealed class BiomePass : IWorldGenerationPass
{
    public string Name => "Biome layout";
    public double Weight => 1.1;

    /// <summary>The layout from the most recent <see cref="Apply"/>.</summary>
    public BiomeLayout? Layout { get; private set; }

    /// <summary>
    /// Which underground hazard dominates. Null keeps the balanced default layout; Molten raises
    /// the chasm, Radiated adds more and larger caustic pockets.
    /// </summary>
    public Rimtera.Systems.WorldHazard? Hazard { get; init; }

    public void Apply(TileGrid grid, TileCatalog catalog, Random random, CancellationToken cancellationToken)
    {
        var width = grid.Width;
        var height = grid.Height;
        var amplitude = Math.Clamp(width / 40, 1, 5);
        var frozenBase = (int)Math.Round(width * (0.22 + random.NextDouble() * 0.06));
        var junkBase = (int)Math.Round(width * (0.72 + random.NextDouble() * 0.06));
        var frozenJitter = JitterProfile(random, height, 6, amplitude);
        var junkJitter = JitterProfile(random, height, 6, amplitude);
        var frozenEdge = new int[height];
        var junkStart = new int[height];
        for (var y = 0; y < height; y++)
        {
            frozenEdge[y] = Math.Clamp(frozenBase + frozenJitter[y], 1, width / 2 - 1);
            junkStart[y] = Math.Clamp(junkBase + junkJitter[y], width / 2 + 1, width - 1);
        }

        var surface = new int[width];
        for (var x = 0; x < width; x++)
            surface[x] = FindSurface(grid, catalog, x);

        var moltenJitter = JitterProfile(random, width, 8, Math.Max(1, height / 30));
        var moltenTop = new int[width];
        for (var x = 0; x < width; x++)
            moltenTop[x] = Math.Clamp(Math.Max((int)Math.Round(height * MoltenDepthFraction) + moltenJitter[x], surface[x] + 14),
                1, Math.Max(1, height - 2));

        var layout = new BiomeLayout(width, height, frozenEdge, junkStart, surface, moltenTop);
        cancellationToken.ThrowIfCancellationRequested();

        PaintSurfaceBiomes(grid, random, layout);
        cancellationToken.ThrowIfCancellationRequested();
        PaintMoltenChasm(grid, catalog, random, layout);
        cancellationToken.ThrowIfCancellationRequested();
        PaintRadiatedPockets(grid, random, layout, Hazard == Rimtera.Systems.WorldHazard.RadiatedDeeps ? 2 : 1);
        cancellationToken.ThrowIfCancellationRequested();
        GrowBiomeOres(grid, random, layout);
        Layout = layout;
    }

    private static void PaintSurfaceBiomes(TileGrid grid, Random random, BiomeLayout layout)
    {
        var snowDepth = JitterProfile(random, grid.Width, 5, 1);
        var duneDepth = JitterProfile(random, grid.Width, 5, 1);
        for (var x = 0; x < grid.Width; x++)
        {
            var top = layout.SurfaceAt(x);
            for (var y = top; y < Math.Min(grid.Height, top + 9); y++)
            {
                var point = new GridPoint(x, y);
                var tile = grid.GetCell(point).TileId;
                if (tile is not (TileId.Soil or TileId.Rock or TileId.Grass))
                    continue;
                var depth = y - top;
                if (layout.IsFrozen(x, y))
                {
                    if (depth < 7 + snowDepth[x])
                        grid.SetTile(point, TileId.Snow);
                }
                else if (layout.IsJunk(x, y))
                {
                    if (depth < 6 + duneDepth[x])
                        grid.SetTile(point, TileId.DesertSand);
                }
                else if (depth == 0 && tile == TileId.Soil)
                {
                    grid.SetTile(point, TileId.Grass);
                }
            }
        }

        // A few vine-mat meadows break up the grassland, away from the spawn clearing.
        var middle = grid.Width / 2;
        var meadows = Math.Max(1, grid.Width / 48);
        for (var meadow = 0; meadow < meadows; meadow++)
        {
            var x = random.Next(Math.Max(0, layout.FrozenEdgeAt(0)), Math.Max(1, layout.JunkStartAt(0)));
            var length = random.Next(3, 9);
            for (var step = 0; step < length; step++, x++)
            {
                if (x < 0 || x >= grid.Width || Math.Abs(x - middle) <= 4)
                    continue;
                var point = new GridPoint(x, layout.SurfaceAt(x));
                if (grid.Contains(point) && grid.GetCell(point).TileId == TileId.Grass)
                    grid.SetTile(point, TileId.JungleGrass);
            }
        }
    }

    private static void PaintMoltenChasm(TileGrid grid, TileCatalog catalog, Random random, BiomeLayout layout)
    {
        for (var x = 0; x < grid.Width; x++)
        for (var y = layout.MoltenTopAt(x); y < grid.Height; y++)
        {
            var point = new GridPoint(x, y);
            if (grid.GetCell(point).TileId is TileId.Rock or TileId.Soil)
                grid.SetTile(point, TileId.IrradiatedRock);
        }

        // Liquid-uranium pools (LiquidKind.Lava): carve small chambers and flood their lower half. Settling happens in the liquid pass.
        var chambers = Math.Max(2, grid.Width / 28);
        for (var chamber = 0; chamber < chambers; chamber++)
        {
            var cx = random.Next(2, Math.Max(3, grid.Width - 2));
            var top = layout.MoltenTopAt(cx) + 1;
            if (top >= grid.Height - 2)
                continue;
            var cy = random.Next(top, grid.Height - 2);
            var rx = random.Next(2, 6);
            var ry = random.Next(1, 3);
            for (var y = cy - ry; y <= cy + ry; y++)
            for (var x = cx - rx; x <= cx + rx; x++)
            {
                var point = new GridPoint(x, y);
                if (!grid.Contains(point) || y >= grid.Height - 1 || !layout.IsMolten(x, y))
                    continue;
                var dx = (x - cx) / (double)rx;
                var dy = (y - cy) / (double)ry;
                if (dx * dx + dy * dy > 1.0 || grid.GetCell(point).TileId != TileId.IrradiatedRock)
                    continue;
                grid.SetTile(point, TileId.Air);
                if (y >= cy)
                    grid.TryAddLiquid(point, LiquidKind.Lava, 0.55f + (float)random.NextDouble() * 0.45f, catalog);
            }
        }
    }

    private double MoltenDepthFraction => Hazard switch
    {
        Rimtera.Systems.WorldHazard.MoltenChasm => 0.72,
        Rimtera.Systems.WorldHazard.RadiatedDeeps => 0.88,
        _ => 0.82
    };

    private static void PaintRadiatedPockets(TileGrid grid, Random random, BiomeLayout layout, int pocketMultiplier = 1)
    {
        var wanted = Math.Max(2, grid.Width / 45) * pocketMultiplier;
        for (var attempt = 0; attempt < wanted * 16 && layout.RadiatedPockets.Count < wanted; attempt++)
        {
            var cx = random.Next(3, Math.Max(4, grid.Width - 3));
            var rx = random.Next(5, 11);
            var ry = random.Next(3, 6);
            // Relax the depth margin on later attempts so shallow worlds still get pockets.
            var margin = attempt < wanted * 10 ? 12 : 7;
            var minY = layout.SurfaceAt(cx) + margin + ry;
            var maxY = layout.MoltenTopAt(cx) - 1;
            if (minY > maxY)
                continue;
            var cy = random.Next(minY, maxY + 1);
            var placed = 0;
            for (var y = cy - ry; y <= cy + ry; y++)
            {
                // Each row gets its own reach so pocket walls are ragged, not elliptical.
                var reach = rx * Math.Sqrt(Math.Max(0, 1 - Math.Pow((y - cy) / (double)(ry + 0.5), 2))) + random.Next(-1, 2);
                for (var x = cx - (int)reach; x <= cx + (int)reach; x++)
                {
                    var point = new GridPoint(x, y);
                    if (!grid.Contains(point) || layout.IsMolten(x, y))
                        continue;
                    if (grid.GetCell(point).TileId is TileId.Rock or TileId.Soil or TileId.Snow or TileId.DesertSand)
                    {
                        grid.SetTile(point, TileId.CrimsonSoil);
                        placed++;
                    }
                }
            }
            if (placed == 0)
                continue;
            var pocket = new RadiatedPocket(new GridPoint(cx, cy), rx, ry);
            layout.AddPocket(pocket);

            var veins = random.Next(1, 3);
            for (var vein = 0; vein < veins; vein++)
                WalkVein(grid, random, new GridPoint(cx + random.Next(-rx / 2, rx / 2 + 1), cy), 1.6, 0.8,
                    TileId.CorruptRock, point => grid.GetCell(point).TileId == TileId.CrimsonSoil);
        }
    }

    private static void GrowBiomeOres(TileGrid grid, Random random, BiomeLayout layout)
    {
        var frozenWidth = Math.Max(1, layout.FrozenEdgeAt(grid.Height / 2));
        for (var vein = Math.Max(2, frozenWidth / 9); vein > 0; vein--)
        {
            var x = random.Next(0, frozenWidth);
            var start = new GridPoint(x, layout.SurfaceAt(x) + random.Next(3, 12));
            WalkVein(grid, random, start, 2.2, 0.82, TileId.GlacialOre, point =>
                layout.IsFrozen(point.X, point.Y) && point.Y > layout.SurfaceAt(point.X) + 1 &&
                point.Y <= layout.SurfaceAt(point.X) + 16 &&
                grid.GetCell(point).TileId is TileId.Snow or TileId.Rock);
        }

        var junkWidth = Math.Max(1, grid.Width - layout.JunkStartAt(grid.Height / 2));
        for (var vein = Math.Max(2, junkWidth / 9); vein > 0; vein--)
        {
            var x = random.Next(grid.Width - junkWidth, grid.Width);
            var start = new GridPoint(x, layout.SurfaceAt(x) + random.Next(2, 10));
            WalkVein(grid, random, start, 2.2, 0.8, TileId.ScrapIronOre, point =>
                layout.IsJunk(point.X, point.Y) && point.Y > layout.SurfaceAt(point.X) &&
                point.Y <= layout.SurfaceAt(point.X) + 14 &&
                grid.GetCell(point).TileId is TileId.DesertSand or TileId.Rock or TileId.Soil);
        }

        foreach (var pocket in layout.RadiatedPockets)
        {
            WalkVein(grid, random, pocket.Center, 1.8, 0.78, TileId.UraniumOre, point =>
                grid.GetCell(point).TileId is TileId.CrimsonSoil or TileId.CorruptRock);
        }

        for (var vein = Math.Max(2, grid.Width / 24); vein > 0; vein--)
        {
            var x = random.Next(grid.Width);
            var top = layout.MoltenTopAt(x);
            if (top >= grid.Height - 1)
                continue;
            var start = new GridPoint(x, random.Next(top, grid.Height));
            WalkVein(grid, random, start, 2f, 0.8, TileId.PyrociteOre, point =>
                grid.GetCell(point).TileId == TileId.IrradiatedRock);
        }
    }

    /// <summary>
    /// Random-walk vein: stamp a round blob, drift one cell (usually keeping the previous
    /// heading), shrink the blob, repeat until it fades out.
    /// </summary>
    private static void WalkVein(TileGrid grid, Random random, GridPoint start, double radius, double shrink,
        TileId ore, Func<GridPoint, bool> canReplace)
    {
        var x = start.X;
        var y = start.Y;
        var heading = (X: random.Next(-1, 2), Y: random.Next(-1, 2));
        for (var guard = 0; radius >= 0.5 && guard < 64; guard++)
        {
            var reach = (int)Math.Ceiling(radius);
            var limit = radius * radius + 0.5;
            for (var oy = -reach; oy <= reach; oy++)
            for (var ox = -reach; ox <= reach; ox++)
            {
                var point = new GridPoint(x + ox, y + oy);
                if (ox * ox + oy * oy <= limit && grid.Contains(point) && canReplace(point))
                    grid.SetTile(point, ore);
            }

            if (random.NextDouble() < 0.4 || heading == (0, 0))
                heading = (random.Next(-1, 2), random.Next(-1, 2));
            x = Math.Clamp(x + heading.X, 0, grid.Width - 1);
            y = Math.Clamp(y + heading.Y, 0, grid.Height - 1);
            radius *= shrink + random.NextDouble() * 0.12;
        }
    }

    /// <summary>Smooth integer offsets in [-amplitude, amplitude], eased between random control points.</summary>
    private static int[] JitterProfile(Random random, int length, int spacing, int amplitude)
    {
        var controls = new double[length / spacing + 2];
        for (var i = 0; i < controls.Length; i++)
            controls[i] = random.NextDouble() * 2 - 1;
        var result = new int[length];
        for (var i = 0; i < length; i++)
        {
            var segment = i / spacing;
            var t = (i - segment * spacing) / (double)spacing;
            var eased = t * t * (3 - 2 * t);
            result[i] = (int)Math.Round((controls[segment] + (controls[segment + 1] - controls[segment]) * eased) * amplitude);
        }
        return result;
    }

    private static int FindSurface(TileGrid grid, TileCatalog catalog, int x)
    {
        for (var y = 0; y < grid.Height; y++)
            if (catalog[grid.GetCell(new GridPoint(x, y)).TileId].IsSolid)
                return y;
        return grid.Height - 1;
    }
}

public sealed class LiquidSettlingPass : IWorldGenerationPass
{
    public string Name => "Cave water";
    public double Weight => 0.8;

    public void Apply(TileGrid grid, TileCatalog catalog, Random random, CancellationToken cancellationToken)
    {
        var sources = Math.Max(1, grid.Width / 28);
        for (var source = 0; source < sources; source++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var x = random.Next(grid.Width);
            for (var y = grid.Height - 2; y >= grid.Height / 3; y--)
            {
                var point = new GridPoint(x, y);
                var below = new GridPoint(x, y + 1);
                if (grid.GetCell(point).TileId == TileId.Air &&
                    catalog[grid.GetCell(below).TileId].IsSolid)
                {
                    var depth = random.Next(96, 220) / 255f;
                    grid.TryAddLiquid(point, LiquidKind.Water, depth, catalog);
                    break;
                }
            }
        }

        LiquidSettler.Settle(grid, catalog, grid.Height * 2, cancellationToken);
    }
}

public static class LiquidSettler
{
    public static float Settle(
        TileGrid grid,
        TileCatalog catalog,
        int maxRounds,
        CancellationToken cancellationToken = default)
    {
        const float horizontalTransferLimit = 0.2f;
        var totalMoved = 0f;
        for (var round = 0; round < maxRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var movedThisRound = 0f;
            for (var y = grid.Height - 2; y >= 0; y--)
            {
                var reverse = ((round + y) & 1) != 0;
                for (var offset = 0; offset < grid.Width; offset++)
                {
                    var x = reverse ? grid.Width - 1 - offset : offset;
                    var source = new GridPoint(x, y);
                    var cell = grid.GetCell(source);
                    if (cell.LiquidDepth <= 0)
                        continue;

                    movedThisRound += grid.TransferLiquid(
                        source,
                        new GridPoint(x, y + 1),
                        cell.LiquidDepth,
                        catalog);

                    cell = grid.GetCell(source);
                    if (cell.LiquidDepth <= 0.02f)
                        continue;

                    var firstDirection = ((x + round) & 1) == 0 ? -1 : 1;
                    for (var side = 0; side < 2; side++)
                    {
                        var direction = side == 0 ? firstDirection : -firstDirection;
                        var destination = new GridPoint(x + direction, y);
                        if (!grid.Contains(destination))
                            continue;

                        var neighbor = grid.GetCell(destination);
                        var difference = cell.LiquidDepth - neighbor.LiquidDepth;
                        if (difference <= 0.02f)
                            continue;

                        var requested = Math.Min(horizontalTransferLimit, difference / 2f);
                        var moved = grid.TransferLiquid(source, destination, requested, catalog);
                        movedThisRound += moved;
                        cell = grid.GetCell(source);
                    }
                }
            }

            totalMoved += movedThisRound;
            if (movedThisRound <= 0.0001f)
                break;
        }

        return totalMoved;
    }
}
