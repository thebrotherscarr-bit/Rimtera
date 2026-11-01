using System.Drawing;
using System.Numerics;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// 0.2.0 C1 (PE-11): coloured lighting on High. High presents the light as a multiply layer, each colour channel of the
/// scene scaled by that channel of the light; Standard keeps its colour-blind darkness layer. The colour maths is checked
/// directly, and a real light map is built over a dark cave with a blue and a red light and applied to a stone-grey frame.
/// </summary>
public static class LightingSelfTests
{
    private const int TileSize = 20; // 100% zoom: 64×36 tiles fill 1280×720
    private static readonly int Stone = unchecked((int)0xFF787878); // opaque mid-grey stone

    public static void Run(Action<bool, string> check)
    {
        CheckMaths(check);
        CheckCave(check);
    }

    private static (int R, int G, int B) Channels(int pixel) => ((pixel >> 16) & 0xFF, (pixel >> 8) & 0xFF, pixel & 0xFF);

    /// <summary>The darkness pixel as it was before C1, kept here as the reference Standard must keep matching.</summary>
    private static int OldDarkness(Vector3 light)
    {
        var max = MathF.Max(light.X, MathF.Max(light.Y, light.Z));
        var brightness = Math.Clamp(max, 0f, 1f);
        var darkAlpha = (int)(245 * (1f - brightness));
        return darkAlpha << 24 | (0x05 * darkAlpha / 255) << 16 | (0x06 * darkAlpha / 255) << 8 | 0x0A * darkAlpha / 255;
    }

    private static void CheckMaths(Action<bool, string> check)
    {
        // High: a blue-only light keeps the blue of a grey pixel and darkens red and green; red-only the reverse.
        var blue = Channels(LightMap.MultiplyPixel(Stone, LightMap.TintOf(new Vector3(0f, 0f, 1f))));
        var red = Channels(LightMap.MultiplyPixel(Stone, LightMap.TintOf(new Vector3(1f, 0f, 0f))));
        check(blue.B == 0x78 && blue.R < 0x10 && blue.G < 0x10, "High: a cell lit only by blue light comes out blue");
        check(red.R == 0x78 && red.G < 0x10 && red.B < 0x10, "High: a cell lit only by red light comes out red");

        // White light leaves every colour exactly as drawn; a dimmer white scales the channels together (no hue shift).
        var white = LightMap.TintOf(Vector3.One);
        var unchanged = true;
        var hueKept = true;
        var grey = LightMap.TintOf(new Vector3(0.5f));
        foreach (var pixel in new[] { Stone, unchecked((int)0xFF2A6B3C), unchecked((int)0xFFFF0000), unchecked((int)0xFFFFFFFF), unchecked((int)0x80402010), 0 })
        {
            unchanged &= LightMap.MultiplyPixel(pixel, white) == pixel;
            var before = Channels(pixel);
            var after = Channels(LightMap.MultiplyPixel(pixel, grey));
            hueKept &= Math.Abs(after.R * 255 - before.R * 132) <= 255 && Math.Abs(after.G * 255 - before.G * 132) <= 255
                       && Math.Abs(after.B * 255 - before.B * 132) <= 255 && (LightMap.MultiplyPixel(pixel, grey) & unchecked((int)0xFF000000)) == (pixel & unchecked((int)0xFF000000));
        }
        check(unchanged, "High: full white light leaves the scene's colours unchanged");
        check(hueKept, "High: a grey light dims all channels together and keeps alpha");

        // Darkness floor matches Standard's: unlit stone on High is as dark as Standard's 245/255 shade.
        var unlit = Channels(LightMap.MultiplyPixel(Stone, LightMap.TintOf(Vector3.Zero)));
        var standardUnlit = 0x78 * 10 / 255 + 4; // stone under Standard's darkest layer: 10/255 of it plus the bluish-black shade
        check(Math.Abs(unlit.R - standardUnlit) <= 4 && unlit.R == unlit.G && unlit.G == unlit.B && LightMap.TintOf(new Vector3(float.NaN)) == LightMap.TintOf(Vector3.Zero),
            "High: unlit cells are as dark as on Standard, and a bad light value reads as unlit");

        // Standard keeps today's output, for every shade and tint, and stays colour-blind.
        var same = true;
        var probes = new List<Vector3>();
        for (var i = 0; i <= 20; i++)
        {
            var v = i / 16f; // runs past 1 into the clamp
            probes.Add(new Vector3(v));
            probes.Add(new Vector3(v, v * 0.3f, 0.1f));
            probes.Add(new Vector3(0f, v * 0.5f, v));
        }
        foreach (var light in probes)
            same &= LightMap.DarknessPixel(light) == OldDarkness(light);
        check(same && LightMap.DarknessPixel(new Vector3(0f, 0f, 0.6f)) == LightMap.DarknessPixel(new Vector3(0.6f, 0f, 0f)),
            "Standard: the darkness layer is unchanged and colour-blind");
    }

    private static void CheckCave(Action<bool, string> check)
    {
        // A cave sealed in rock from the top of the world down, so no sky reaches it even at noon.
        var game = new GameSimulation(seed: 11011, width: 96, height: 60) { FamilySpawnsEnabled = false };
        for (var y = 0; y < game.World.Height; y++)
        for (var x = 0; x < game.World.Width; x++)
            game.World.SetCell(new GridPoint(x, y), new WorldCell { TileId = TileId.Rock }); // no wall, no liquid
        const int floor = 44;
        for (var y = 36; y < floor; y++)
        for (var x = 8; x < 88; x++)
            game.World.SetCell(new GridPoint(x, y), new WorldCell { TileId = TileId.Air });
        const int startX = 16, startY = 22, viewW = 64, viewH = 36;

        var light = new LightMap { Subdivision = 4, ColouredGlow = true };
        // Blue and red torches 18 tiles apart: the player's own lamp style, in pure colours.
        var blueAt = new GridPoint(30, 41);
        var redAt = new GridPoint(48, 41);
        var lamps = new[]
        {
            new LightMap.LampLight(new Vector2(blueAt.X + 0.5f, blueAt.Y + 0.5f), Color.FromArgb(40, 70, 255), 1f),
            new LightMap.LampLight(new Vector2(redAt.X + 0.5f, redAt.Y + 0.5f), Color.FromArgb(255, 50, 30), 1f)
        };
        light.Compute(game.World, game.Tiles, startX, startY, viewW, viewH, daylight: 1f, lamps);

        // The frame: every pixel stone grey, then the High multiply over 1280×720 as the renderer applies it.
        var width = viewW * TileSize;
        var height = viewH * TileSize;
        var frame = new int[width * height];
        Array.Fill(frame, Stone);
        light.MultiplyInto(frame, width, new Rectangle(0, 0, width, height), -LightMap.Padding * TileSize, -LightMap.Padding * TileSize, TileSize);
        (int R, int G, int B) At(int tileX, int tileY) =>
            Channels(frame[((tileY - startY) * TileSize + TileSize / 2) * width + (tileX - startX) * TileSize + TileSize / 2]);

        // A pool round each torch (the cave just above the floor) and on the stone floor tile itself.
        var bluePool = At(blueAt.X + 2, floor - 1);
        var redPool = At(redAt.X - 2, floor - 1);
        var blueFloor = At(blueAt.X + 1, floor);
        var redFloor = At(redAt.X - 1, floor);
        check(bluePool.B > 0x40 && bluePool.B > bluePool.R * 2 && bluePool.B > bluePool.G * 2
              && blueFloor.B > 0x28 && blueFloor.B > blueFloor.R * 2 && blueFloor.B > blueFloor.G * 2,
            $"High cave: the blue torch makes a blue pool on the stone ({bluePool}, floor {blueFloor})");
        check(redPool.R > 0x40 && redPool.R > redPool.B * 2 && redPool.R > redPool.G * 2
              && redFloor.R > 0x28 && redFloor.R > redFloor.B * 2 && redFloor.R > redFloor.G * 2,
            $"High cave: the red torch makes a red pool on the stone ({redPool}, floor {redFloor})");
        var dark = At(startX + 2, startY + 2); // deep in the rock above the cave
        check(dark.R < 0x10 && dark.G < 0x10 && dark.B < 0x10, $"High cave: rock far from both torches stays dark at noon ({dark})");
        var between = At((blueAt.X + redAt.X) / 2, blueAt.Y);
        check(between.R > 0x08 && between.B > 0x08 && between.R < redPool.R && between.B < bluePool.B,
            $"High cave: between the torches the two lights mix, fainter than either pool ({between})");
        check(LightMap.MultiplyPixel(Stone, light.TintAt(blueAt.X + 2, floor - 1)) == frame[((floor - 1 - startY) * TileSize + TileSize / 2) * width + (blueAt.X + 2 - startX) * TileSize + TileSize / 2],
            "High cave: the frame multiply uses the light map's own tint for each tile");

        // The darkness debuff still dims the light on High.
        var dimmed = new LightMap { Subdivision = 4, ColouredGlow = true, Dimming = 0.6f };
        dimmed.Compute(game.World, game.Tiles, startX, startY, viewW, viewH, daylight: 1f, lamps);
        var full = Channels(LightMap.MultiplyPixel(Stone, light.TintAt(blueAt.X + 1, blueAt.Y)));
        var dim = Channels(LightMap.MultiplyPixel(Stone, dimmed.TintAt(blueAt.X + 1, blueAt.Y)));
        check(dim.B < full.B * 0.6f && dim.B > dim.R, $"High cave: the darkness debuff dims the coloured light and keeps its hue ({full} → {dim})");

        // Placed coloured lamps (cyan and amber glow lamps) make blue- and red-leaning pools on their own.
        game.World.SetTile(blueAt, TileId.GlowLampCyan);
        game.World.SetTile(redAt, TileId.GlowLampAmber);
        var placed = new LightMap { Subdivision = 4, ColouredGlow = true };
        placed.Compute(game.World, game.Tiles, startX, startY, viewW, viewH, daylight: 1f, []);
        var cyan = Channels(LightMap.MultiplyPixel(Stone, placed.TintAt(blueAt.X + 2, floor - 1)));
        var amber = Channels(LightMap.MultiplyPixel(Stone, placed.TintAt(redAt.X - 2, floor - 1)));
        check(cyan.B > cyan.R + 0x20 && amber.R > amber.B + 0x20,
            $"High cave: a cyan glow lamp lights the stone blue and an amber one red ({cyan}, {amber})");
    }
}
