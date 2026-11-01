using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>
/// The quiet status line in the HUD's bottom-right corner (NC-16): day, clock, defence and credits always; "Crew n" only
/// with living crew; drones and sentries only while one is out or a summon item is held; the time rate while sped up.
/// No mood: the player has no mood upkeep (D-48).
/// </summary>
public static class HudStatusLine
{
    public const string Separator = " · ";

    public static string Build(GameSimulation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var parts = new List<string>
        {
            $"Day {game.Day}",
            FormatClock(game.ClockHour),
            $"DEF {game.Player.Stats.Defense}",
            $"{game.Player.Inventory.Count(ItemId.Credits)} cr"
        };
        var crew = game.Colonists.Count(agent => agent.IsAlive);
        if (crew > 0)
            parts.Add($"Crew {crew}");
        var held = game.Player.Inventory.SelectedItem?.Summons is { } kind ? MinionCatalog.Get(kind) : null;
        if (game.ActiveDrones > 0 || held is { IsSentry: false })
            parts.Add($"Drones {game.ActiveDrones}/{game.DroneSlots}");
        if (game.ActiveSentries > 0 || held is { IsSentry: true })
            parts.Add($"Sentries {game.ActiveSentries}/{game.SentrySlots}");
        if (game.TimeRate > 1.01f)
            parts.Add($"time ×{game.TimeRate:0}");
        return string.Join(Separator, parts);
    }

    /// <summary>A 12-hour clock ("8:00 AM").</summary>
    public static string FormatClock(float hour)
    {
        var h = (int)hour % 24;
        var m = (int)((hour - MathF.Floor(hour)) * 60f);
        return $"{(h % 12 == 0 ? 12 : h % 12)}:{m:00} {(h < 12 ? "AM" : "PM")}";
    }
}
