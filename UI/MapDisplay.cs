using System.Numerics;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>
/// PU-15: the rules behind the in-world map (minimap and overlay) and the map icons, kept out of the renderer so they
/// can be tested. Style, scale and both opacities live in <see cref="GamePreferences"/>, so they survive a restart.
/// </summary>
public static class MapDisplay
{
    public const float OpacityStep = 0.015f;

    /// <summary>The overlay draws only with the map enabled (Settings → General → Map enabled) and the overlay style.</summary>
    public static bool DrawsOverlay(GamePreferences preferences) => preferences.MinimapEnabled && preferences.MapView == MapOverlayStyle.Overlay;

    /// <summary>The corner minimap draws only with the map enabled and the minimap style.</summary>
    public static bool DrawsMinimap(GamePreferences preferences) => preferences.MinimapEnabled && preferences.MapView == MapOverlayStyle.Minimap;

    /// <summary>Tab: Minimap → Overlay → Off → Minimap.</summary>
    public static MapOverlayStyle CycleStyle(GamePreferences preferences) =>
        preferences.MapView = (MapOverlayStyle)(((int)preferences.MapView + 1) % Enum.GetValues<MapOverlayStyle>().Length);

    /// <summary>Changes the opacity of whichever map is showing; true when it changed.</summary>
    public static bool AdjustOpacity(GamePreferences preferences, float step)
    {
        switch (preferences.MapView)
        {
            case MapOverlayStyle.Overlay:
                var overlay = Math.Clamp(preferences.OverlayOpacity + step, 0.05f, 1f);
                if (overlay == preferences.OverlayOpacity)
                    return false;
                preferences.OverlayOpacity = overlay;
                return true;
            case MapOverlayStyle.Minimap:
                var minimap = Math.Clamp(preferences.MinimapOpacity + step, 0.05f, 1f);
                if (minimap == preferences.MinimapOpacity)
                    return false;
                preferences.MinimapOpacity = minimap;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Screen pixels per map tile, 1-6; true when it changed.</summary>
    public static bool ZoomMinimap(GamePreferences preferences, int delta)
    {
        var scale = Math.Clamp(preferences.MinimapScale + delta, 1, 6);
        if (scale == preferences.MinimapScale)
            return false;
        preferences.MinimapScale = scale;
        return true;
    }

    /// <summary>A town NPC's head on the map: where it is and the sprite drawn (the NPC sprite until head art exists).</summary>
    public readonly record struct NpcHead(TownNpc Npc, Vector2 Position, string Sprite, bool Housed);

    /// <summary>
    /// The town NPC icons for the maps. Every housed, living resident shows wherever it is, as Terraria shows town NPC heads;
    /// the full map also shows bound survivors and homeless arrivals once their spot has been seen (<paramref name="revealed"/>).
    /// The minimap passes no <paramref name="revealed"/> and shows housed residents only.
    /// </summary>
    public static List<NpcHead> NpcHeads(GameSimulation game, Func<int, int, bool>? revealed = null)
    {
        var heads = new List<NpcHead>();
        foreach (var npc in game.TownNpcs)
        {
            if (!npc.IsAlive)
                continue;
            var center = npc.Motion.Position + npc.Motion.Size / 2f;
            var housed = !npc.IsBound && npc.HomeCell is not null;
            if (housed || (revealed is not null && revealed((int)center.X, (int)center.Y)))
                heads.Add(new NpcHead(npc, center, npc.SpriteName, housed));
        }
        return heads;
    }
}
