using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// One world's shape in numbers (0.3.0): the surface line's roughness and relief, how open the ground is at each depth,
/// how much of that open space joins up into large caves, how many natural openings lead down from the surface, and how
/// much of the underground is water. Measured the same way for Rimtera's generator and for the Terraria-style reference
/// model (<see cref="WorldShapeReference"/>), so the two can be compared number by number.
/// </summary>
public sealed record WorldShapeReport(
    double MeanStep,
    double SteepShare,
    int InlandRange,
    double HillsPer1000,
    int SpawnRange,
    double[] OpenByDepth,
    double BigCaveShare,
    double PocketShare,
    double OpeningsPer1000,
    double WideOpeningsPer1000,
    double UndergroundWaterShare)
{
    /// <summary>Depth bands below the surface line that <see cref="OpenByDepth"/> reports, in tiles.</summary>
    public static readonly int[] DepthBands = [0, 25, 60, 120, 200, 300, 450, 600, 750];

    public IEnumerable<string> Lines()
    {
        yield return $"surface: mean step {MeanStep:0.00} tiles, 2+ tile steps {SteepShare:P0}, inland range {InlandRange}, hills {HillsPer1000:0.0}/1000 cols, spawn range {SpawnRange}";
        for (var i = 0; i < OpenByDepth.Length; i++)
            yield return $"open {DepthBands[i],3}-{DepthBands[i + 1],3} below: {OpenByDepth[i]:P1}";
        yield return $"caves: {BigCaveShare:P0} of open space in caves of 2000+ cells, {PocketShare:P0} in pockets under 200";
        yield return $"surface openings reaching 20+ tiles down: {OpeningsPer1000:0.0} per 1000 columns, {WideOpeningsPer1000:0.0} of them 3+ tiles wide";
        yield return $"underground water: {UndergroundWaterShare:P1} of open cells";
    }
}

public static class WorldShape
{
    /// <summary>Caves bigger than this count as "large"; open regions smaller than <see cref="Pocket"/> as pockets.</summary>
    public const int LargeCave = 2000, Pocket = 200;
    /// <summary>An opening counts when sky-connected air reaches this far below the surface line.</summary>
    public const int OpeningDepth = 20;

    /// <summary>Measures a generated Rimtera world against its own surface line.</summary>
    public static WorldShapeReport Measure(GameSimulation game)
    {
        var world = game.World;
        var tiles = game.Tiles;
        IReadOnlyList<BlightZone> blight = game.Features?.BlightZones ?? [];
        return Measure(world.Width, world.Height,
            (x, y) => world.GetCell(new GridPoint(x, y)) is var cell && cell.LiquidDepth <= 0.2f && cell.TileId != TileId.Air &&
                      tiles.Contains(cell.TileId) && tiles[cell.TileId].IsSolid,
            (x, y) => world.GetCell(new GridPoint(x, y)) is { LiquidDepth: > 0.2f, LiquidKind: LiquidKind.Water },
            // Blight chasms are deliberate cliffs with nothing like them in the reference: their columns are left out of
            // the surface figures (they still count underground).
            x => blight.Any(zone => x >= zone.Left - 4 && x <= zone.Right + 4));
    }

    /// <summary>
    /// The ground line as a player sees it: in each column the first cell (from 10% of the height down, under any sky
    /// islands) with ground — solid or liquid, so a lake's surface counts — at least twelve deep, then a running median over
    /// 21 columns so holes and shafts narrower than that don't pull the line down into them.
    /// </summary>
    public static int[] Ground(int width, int height, Func<int, int, bool> ground)
    {
        var raw = new int[width];
        for (var x = 0; x < width; x++)
        {
            var y = (int)(height * 0.1);
            for (; y < height - 13; y++)
            {
                if (!ground(x, y))
                    continue;
                var deep = true;
                for (var k = 1; k <= 12 && deep; k++)
                    deep = ground(x, y + k);
                if (deep)
                    break;
            }
            raw[x] = y;
        }
        var line = new int[width];
        var window = new List<int>(21);
        for (var x = 0; x < width; x++)
        {
            window.Clear();
            for (var k = -10; k <= 10; k++)
                window.Add(raw[Math.Clamp(x + k, 0, width - 1)]);
            window.Sort();
            line[x] = window[10];
        }
        return line;
    }

    /// <summary>
    /// The shared measure. Columns 10–90% of the width count (the coasts are left out); the underground runs from ten
    /// tiles below the surface line to 85% of the height.
    /// </summary>
    public static WorldShapeReport Measure(int width, int height, Func<int, int, bool> solid, Func<int, int, bool> water,
        Func<int, bool>? skipSurface = null)
    {
        skipSurface ??= _ => false;
        var surface = Ground(width, height, (x, y) => solid(x, y) || water(x, y));
        int left = width / 10, right = width - width / 10, mid = width / 2;
        var floor = (int)(height * 0.85);

        // ---- the surface line ----
        double steps = 0;
        int steep = 0, n = 0;
        int inlandMin = int.MaxValue, inlandMax = int.MinValue, spawnMin = int.MaxValue, spawnMax = int.MinValue, hills = 0;
        for (var x = left + 1; x < right; x++)
        {
            if (skipSurface(x) || skipSurface(x - 1))
                continue;
            var d = Math.Abs(surface[x] - surface[x - 1]);
            steps += d;
            n++;
            if (d >= 2)
                steep++;
            inlandMin = Math.Min(inlandMin, surface[x]);
            inlandMax = Math.Max(inlandMax, surface[x]);
            if (Math.Abs(x - mid) <= 300)
            {
                spawnMin = Math.Min(spawnMin, surface[x]);
                spawnMax = Math.Max(spawnMax, surface[x]);
            }
        }
        for (var x = left + 30; x < right - 30; x++)
        {
            if (skipSurface(x))
                continue;
            var peak = true;
            for (var k = -30; k <= 30 && peak; k++)
                peak = surface[x + k] >= surface[x];
            if (peak && Math.Max(surface[x - 30], surface[x + 30]) - surface[x] >= 6)
            {
                hills++;
                x += 30;
            }
        }

        // ---- open space by depth ----
        var bands = WorldShapeReport.DepthBands;
        var openByDepth = new double[bands.Length - 1];
        for (var i = 0; i < openByDepth.Length; i++)
        {
            long open = 0, all = 0;
            for (var x = left; x < right; x += 2)
            for (var d = bands[i]; d < bands[i + 1]; d++)
            {
                var y = surface[x] + d;
                if (y >= floor)
                    break;
                all++;
                if (!solid(x, y))
                    open++;
            }
            openByDepth[i] = all == 0 ? 0 : open / (double)all;
        }

        // ---- caves: connected open regions underground, and underground water ----
        var seen = new bool[width * height];
        var queue = new Queue<int>();
        long total = 0, big = 0, pockets = 0, wet = 0;
        bool Underground(int x, int y) => x >= left && x < right && y < floor && y >= surface[x] + 10;
        for (var x = left; x < right; x++)
        for (var y = surface[x] + 10; y < floor; y++)
        {
            var start = y * width + x;
            if (seen[start] || solid(x, y))
                continue;
            var size = 0;
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                size++;
                int cx = cell % width, cy = cell / width;
                if (water(cx, cy))
                    wet++;
                Visit(cx + 1, cy);
                Visit(cx - 1, cy);
                Visit(cx, cy + 1);
                Visit(cx, cy - 1);
            }
            total += size;
            if (size >= LargeCave)
                big += size;
            else if (size < Pocket)
                pockets += size;
        }
        void Visit(int x, int y)
        {
            if (!Underground(x, y))
                return;
            var index = y * width + x;
            if (seen[index] || solid(x, y))
                return;
            seen[index] = true;
            queue.Enqueue(index);
        }

        // ---- openings: sky-connected air that reaches OpeningDepth below the surface line ----
        var reached = new bool[width * height];
        var walk = new Queue<int>();
        for (var x = left; x < right; x++)
        {
            var y = Math.Max(0, surface[x] - 1);
            if (!solid(x, y) && !water(x, y))
            {
                reached[y * width + x] = true;
                walk.Enqueue(y * width + x);
            }
        }
        while (walk.Count > 0)
        {
            var cell = walk.Dequeue();
            int cx = cell % width, cy = cell / width;
            foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
            {
                if (nx < left || nx >= right || ny < 0 || ny >= floor || ny < surface[nx] - 1 || ny > surface[nx] + OpeningDepth)
                    continue;
                var index = ny * width + nx;
                if (reached[index] || solid(nx, ny) || water(nx, ny))
                    continue;
                reached[index] = true;
                walk.Enqueue(index);
            }
        }
        int openings = 0, wide = 0, lastDeep = int.MinValue, run = 0;
        for (var x = left; x < right; x++)
        {
            var y = surface[x] + OpeningDepth;
            if (y >= floor || !reached[y * width + x])
            {
                run = 0;
                continue;
            }
            if (x - lastDeep > 4)
                openings++;
            run = x - lastDeep == 1 ? run + 1 : 1;
            if (run == 3)
                wide++;
            lastDeep = x;
        }

        var span = right - left;
        return new WorldShapeReport(
            steps / Math.Max(1, n), steep / (double)Math.Max(1, n), inlandMax - inlandMin, hills * 1000.0 / span, spawnMax - spawnMin,
            openByDepth, total == 0 ? 0 : big / (double)total, total == 0 ? 0 : pockets / (double)total,
            openings * 1000.0 / span, wide * 1000.0 / span, total == 0 ? 0 : wet / (double)total);
    }
}
