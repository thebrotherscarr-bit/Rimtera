using System.Drawing;
using Rimtera.Characters;

namespace Rimtera.UI;

/// <summary>
/// Turns a colonist's appearance into recoloured Neo-Terra sprites: the orange suit takes the
/// outfit colour, and the face sheet takes the skin tone and hair colour. Recolours are
/// registered in the atlas as "name#key" variants so they scale and cache like any sprite.
/// </summary>
internal static class CharacterLook
{
    public const int FaceCount = 16;

    public static readonly Color[] SkinTones =
    [
        Color.FromArgb(244, 219, 196), Color.FromArgb(232, 198, 166), Color.FromArgb(210, 170, 126),
        Color.FromArgb(186, 139, 92), Color.FromArgb(144, 96, 61), Color.FromArgb(96, 62, 44)
    ];

    public static readonly Color[] HairColors =
    [
        Color.FromArgb(35, 30, 30), Color.FromArgb(72, 46, 33), Color.FromArgb(112, 65, 37),
        Color.FromArgb(145, 78, 42), Color.FromArgb(214, 181, 105), Color.FromArgb(226, 226, 220),
        Color.FromArgb(75, 111, 165), Color.FromArgb(66, 131, 86)
    ];

    /// <summary>Suit colours; index 0 keeps the sheet's original orange.</summary>
    public static readonly Color[] OutfitColors =
    [
        Color.FromArgb(214, 112, 44), Color.FromArgb(73, 112, 67), Color.FromArgb(66, 78, 138),
        Color.FromArgb(174, 145, 91), Color.FromArgb(112, 68, 116), Color.FromArgb(52, 125, 125)
    ];

    // Average colours of the sheet art the recolours are measured against.
    private static readonly Color SheetSkin = Color.FromArgb(205, 150, 110);
    private static readonly Color SheetHair = Color.FromArgb(96, 62, 38);

    public static int FaceIndex(ColonistAppearance appearance) =>
        Math.Abs(appearance.HairStyle * 5 + appearance.EyeColor * 3) % FaceCount;

    public static string Face(SpriteAtlas atlas, ColonistAppearance appearance)
    {
        var name = $"face_{FaceIndex(appearance):00}";
        var skin = SkinTones[Math.Clamp(appearance.SkinTone, 0, SkinTones.Length - 1)];
        var hair = HairColors[Math.Clamp(appearance.HairColor, 0, HairColors.Length - 1)];
        return atlas.Variant(name, $"s{appearance.SkinTone}h{appearance.HairColor}", color => RecolorHead(color, skin, hair));
    }

    /// <summary>Colonist body frame in the character's outfit colour (and skin tone on the visible face).</summary>
    public static string Body(SpriteAtlas atlas, string frame, ColonistAppearance appearance)
    {
        var outfitIndex = Math.Clamp(appearance.OutfitColor, 0, OutfitColors.Length - 1);
        var outfit = OutfitColors[outfitIndex];
        var skin = SkinTones[Math.Clamp(appearance.SkinTone, 0, SkinTones.Length - 1)];
        // Original sheet frames wear an orange suit; the generated animation frames (anim_*) a blue one.
        var blueSuit = frame.StartsWith("anim_", StringComparison.Ordinal);
        return atlas.Variant(frame, $"o{outfitIndex}s{appearance.SkinTone}", color =>
        {
            Rgb2Hsv(color, out var hue, out var saturation, out var value);
            var suit = blueSuit
                ? hue is >= 185f and <= 240f && saturation > 0.16f && value > 0.22f
                : hue is >= 8f and <= 42f && saturation > 0.55f && value > 0.3f;
            if (outfitIndex != 0 && suit)
                return Shade(outfit, value / (blueSuit ? 0.7f : 0.84f), saturation);
            return IsSkin(hue, saturation, value) ? Scale(color, skin, SheetSkin) : color;
        });
    }

    private static Color RecolorHead(Color color, Color skin, Color hair)
    {
        Rgb2Hsv(color, out var hue, out var saturation, out var value);
        if (IsSkin(hue, saturation, value))
            return Scale(color, skin, SheetSkin);
        // Hair on the face sheet is a dark, warm brown.
        if (hue is >= 10f and <= 45f && saturation > 0.3f && value is > 0.12f and <= 0.5f)
            return Scale(color, hair, SheetHair);
        return color;
    }

    private static bool IsSkin(float hue, float saturation, float value) =>
        hue is >= 12f and <= 40f && saturation is > 0.2f and <= 0.58f && value > 0.5f;

    /// <summary>Keeps the pixel's shading by scaling the target colour by pixel/reference brightness.</summary>
    private static Color Scale(Color pixel, Color target, Color reference)
    {
        var ratio = (pixel.R + pixel.G + pixel.B) / (float)Math.Max(1, reference.R + reference.G + reference.B);
        return Color.FromArgb(pixel.A,
            Math.Clamp((int)(target.R * ratio), 0, 255),
            Math.Clamp((int)(target.G * ratio), 0, 255),
            Math.Clamp((int)(target.B * ratio), 0, 255));
    }

    private static Color Shade(Color target, float brightness, float saturation)
    {
        Rgb2Hsv(target, out var hue, out var targetSaturation, out var targetValue);
        return Hsv2Rgb(hue, Math.Clamp(targetSaturation * (0.6f + saturation * 0.4f), 0f, 1f),
            Math.Clamp(targetValue * brightness, 0f, 1f));
    }

    private static void Rgb2Hsv(Color color, out float hue, out float saturation, out float value)
    {
        var r = color.R / 255f;
        var g = color.G / 255f;
        var b = color.B / 255f;
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var delta = max - min;
        value = max;
        saturation = max <= 0f ? 0f : delta / max;
        if (delta <= 0f)
            hue = 0f;
        else if (max == r)
            hue = 60f * (((g - b) / delta + 6f) % 6f);
        else if (max == g)
            hue = 60f * ((b - r) / delta + 2f);
        else
            hue = 60f * ((r - g) / delta + 4f);
    }

    private static Color Hsv2Rgb(float hue, float saturation, float value)
    {
        var c = value * saturation;
        var x = c * (1f - MathF.Abs(hue / 60f % 2f - 1f));
        var m = value - c;
        var (r, g, b) = hue switch
        {
            < 60f => (c, x, 0f),
            < 120f => (x, c, 0f),
            < 180f => (0f, c, x),
            < 240f => (0f, x, c),
            < 300f => (x, 0f, c),
            _ => (c, 0f, x)
        };
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }
}
