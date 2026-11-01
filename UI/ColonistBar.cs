using System.Drawing;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>One portrait on the colonist bar: who, how hurt, how happy, and whether they are downed or breaking.</summary>
public sealed record ColonistBarEntry(long Id, string Name, string Initials, float Health, float Mood, bool Downed, bool Breaking, string Activity);

/// <summary>
/// The colonist bar (0.2.8, RimWorld's): a row of crew portraits along the top of the screen, between the hotbar and the
/// life panel. Each shows the crew member's initials with a health bar and a mood bar, red when downed and purple while a
/// mental break runs; clicking one opens their inspect card. The renderer draws it; this class decides what and where.
/// </summary>
public static class ColonistBar
{
    public const float Slot = 40f, Gap = 6f, MinimumSlot = 24f, Top = 8f;

    public static IReadOnlyList<ColonistBarEntry> Entries(GameSimulation game) =>
        game.Colonists.Where(agent => agent.IsAlive).Select(agent => new ColonistBarEntry(
            agent.Id,
            agent.Name,
            Initials(agent.Name),
            Math.Clamp(agent.HitPoints / Math.Max(1f, agent.MaximumHitPoints), 0f, 1f),
            Math.Clamp(agent.Mood, 0f, 1f),
            agent.IsDowned,
            agent.Mind.Breaker.IsBreaking,
            agent.Activity)).ToArray();

    /// <summary>Up to two capital letters from a name ("Sable Lark" → "SL").</summary>
    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }

    /// <summary>
    /// Portrait boxes for <paramref name="count"/> crew, centred in the gap from <paramref name="left"/> to
    /// <paramref name="right"/>; slots shrink (to <see cref="MinimumSlot"/>) before wrapping onto a second row.
    /// </summary>
    public static IReadOnlyList<RectangleF> Layout(int count, float left, float right, float top = Top)
    {
        if (count <= 0 || right <= left)
            return [];
        var width = right - left;
        var slot = Slot;
        while (slot > MinimumSlot && count * (slot + Gap) - Gap > width)
            slot -= 2f;
        var perRow = Math.Max(1, (int)((width + Gap) / (slot + Gap)));
        var rects = new List<RectangleF>(count);
        for (var i = 0; i < count; i++)
        {
            var row = i / perRow;
            var inRow = Math.Min(perRow, count - row * perRow);
            var rowWidth = inRow * (slot + Gap) - Gap;
            var x = left + (width - rowWidth) / 2f + i % perRow * (slot + Gap);
            rects.Add(new RectangleF(x, top + row * (slot + 18f), slot, slot));
        }
        return rects;
    }
}
