namespace Rimtera.Systems;

/// <summary>
/// A measuring model of Terraria 1.4's terrain and cave generation (0.3.0), used only to compare world shape: Rimtera's
/// generator is tuned until <see cref="WorldShape"/> reads the same on both. It is a reconstruction from the structure of
/// 1.4's TerrainPass walk and its TileRunner cave passes (surface caves, small holes, dirt-layer and rock-layer caves);
/// counts and sizes are approximate, and passes that only add biomes, structures or liquids are left out, so it
/// understates how open real Terraria worlds are. Nothing here generates Rimtera content.
/// </summary>
public static class WorldShapeReference
{
    public static WorldShapeReport Measure(int width, int height, int seed)
    {
        var (solid, water) = Build(width, height, seed);
        return WorldShape.Measure(width, height, (x, y) => solid[y * width + x], (x, y) => water[y * width + x]);
    }

    /// <summary>The reference grid itself: which cells are solid and which hold water (row-major).</summary>
    public static (bool[] Solid, bool[] Water) Build(int width, int height, int seed)
    {
        var random = new Random(seed);
        var surface = Walk(width, height, random);
        var rockLayer = surface.Select(y => y + (int)(height * 0.1)).ToArray();
        var solid = new bool[width * height];
        var water = new bool[width * height];
        for (var x = 0; x < width; x++)
        for (var y = surface[x]; y < height; y++)
            solid[y * width + x] = true;

        void Runner(int i, int j, double strength, int steps, bool wet = false, double? speedX = null, double? speedY = null, bool noYChange = false)
        {
            double size = strength, left = steps, px = i, py = j;
            var vx = speedX ?? random.Next(-10, 11) * 0.1;
            var vy = speedY ?? random.Next(-10, 11) * 0.1;
            while (size > 0 && left > 0)
            {
                size = strength * (left / steps);
                left--;
                int x0 = (int)(px - size * 0.5), x1 = (int)(px + size * 0.5), y0 = (int)(py - size * 0.5), y1 = (int)(py + size * 0.5);
                for (var x = Math.Max(1, x0); x < Math.Min(width - 1, x1); x++)
                for (var y = Math.Max(1, y0); y < Math.Min(height - 1, y1); y++)
                    if (Math.Abs(x - px) + Math.Abs(y - py) < strength * 0.5 * (1 + random.Next(-10, 11) * 0.015))
                    {
                        solid[y * width + x] = false;
                        water[y * width + x] = wet;
                    }
                px += vx;
                py += vy;
                vx = Math.Clamp(vx + random.Next(-10, 11) * 0.05, -1, 1);
                if (!noYChange)
                    vy = Math.Clamp(vy + random.Next(-10, 11) * 0.05, -1, 1);
            }
        }

        var area = width * (double)height;
        var surfaceHigh = surface.Max();
        var rockHigh = rockLayer.Max();
        // Surface caves: entrances angling down from the ground.
        for (var k = 0; k < (int)(width * 0.002); k++)
        {
            var x = random.Next(width);
            Runner(x, surface[x], random.Next(3, 6), random.Next(5, 50), speedX: random.Next(-10, 11) * 0.1, speedY: 1.0);
        }
        for (var k = 0; k < (int)(width * 0.0007); k++)
        {
            var x = random.Next(width);
            Runner(x, surface[x], random.Next(10, 15), random.Next(50, 130), speedX: 0, speedY: 2.0);
        }
        for (var k = 0; k < (int)(width * 0.0003); k++)
        {
            var x = random.Next(width);
            Runner(x, surface[x], random.Next(12, 25), random.Next(150, 500), speedX: random.Next(-10, 11) * 0.1, speedY: 4.0);
        }
        for (var k = 0; k < (int)(width * 0.0004); k++)
        {
            var x = random.Next(width);
            Runner(x, surface[x], random.Next(7, 12), random.Next(150, 250), speedX: 0, speedY: 1.0, noYChange: true);
        }
        // Small holes: a small runner in the surface band and a larger one below it, a fifth of them water-filled.
        for (var k = 0; k < (int)(area * 0.0015); k++)
        {
            var wet = random.Next(5) == 0;
            Runner(random.Next(width), random.Next(0, surfaceHigh), random.Next(2, 5), random.Next(2, 20), wet);
            Runner(random.Next(width), random.Next(surfaceHigh, height), random.Next(8, 15), random.Next(7, 30), wet);
        }
        // Dirt-layer caves.
        for (var k = 0; k < (int)(area * 3e-05); k++)
            Runner(random.Next(width), random.Next(surfaceHigh, rockHigh), random.Next(5, 15), random.Next(30, 200), random.Next(6) == 0);
        // Rock-layer caves.
        for (var k = 0; k < (int)(area * 0.00013); k++)
            Runner(random.Next(width), random.Next(rockHigh, height), random.Next(6, 20), random.Next(50, 300));

        return (solid, water);
    }

    /// <summary>The surface walk: runs of 5–40 columns of plateau, hill, dale, mountain or valley, each nudging the row.</summary>
    private static int[] Walk(int width, int height, Random random)
    {
        var surface = new int[width];
        var y = height * 0.3 * (random.Next(90, 110) * 0.01);
        int left = 0, kind = 0;
        for (var x = 0; x < width; x++)
        {
            if (left <= 0)
            {
                kind = random.Next(5);
                left = random.Next(5, 40);
                if (kind == 0)
                    left = (int)(left * random.Next(5, 30) * 0.2);
            }
            left--;
            switch (kind)
            {
                case 0:
                    while (random.Next(0, 7) == 0) y += random.Next(-1, 2);
                    break;
                case 1:
                    while (random.Next(0, 4) == 0) y--;
                    while (random.Next(0, 10) == 0) y++;
                    break;
                case 2:
                    while (random.Next(0, 4) == 0) y++;
                    while (random.Next(0, 10) == 0) y--;
                    break;
                case 3:
                    while (random.Next(0, 2) == 0) y--;
                    while (random.Next(0, 6) == 0) y++;
                    break;
                default:
                    while (random.Next(0, 2) == 0) y++;
                    while (random.Next(0, 5) == 0) y--;
                    break;
            }
            y = Math.Clamp(y, height * 0.17, height * 0.30);
            surface[x] = (int)y;
        }
        return surface;
    }
}
