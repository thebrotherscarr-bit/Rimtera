using Rimtera.World;
using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>What an inspect card shows: a crew member, a town resident or a creature.</summary>
public enum InspectKind : byte { Colonist, Resident, Creature }

/// <summary>Who a card follows (it is rebuilt every frame, so it stays live; it closes when they are gone).</summary>
public readonly record struct InspectTarget(InspectKind Kind, long Id);

/// <summary>
/// A click-to-inspect card (0.2.8, RimWorld's inspect pane): a title, a subtitle, health and (for people) mood as
/// fractions for the card's bars, and plain lines.
/// </summary>
public sealed record InspectCard(InspectTarget Target, string Title, string Subtitle, float Health, float? Mood, IReadOnlyList<string> Lines);

public sealed partial class GameSimulation
{
    /// <summary>The character nearest a world point within <paramref name="radius"/> tiles of their body: residents first, then creatures.</summary>
    public InspectTarget? PickInspectTarget(Vector2 point, float radius = 1f)
    {
        float Distance(PawnMotionState motion) => DistanceToRect(point, motion.Position, motion.Size);
        var resident = _townNpcs.Where(npc => npc.IsAlive && Distance(npc.Motion) <= radius).MinBy(npc => Distance(npc.Motion));
        if (resident is not null)
            return new InspectTarget(InspectKind.Resident, resident.Id);
        var creature = _enemies.Where(enemy => enemy.IsAlive && Distance(enemy.Motion) <= radius).MinBy(enemy => Distance(enemy.Motion));
        return creature is null ? null : new InspectTarget(InspectKind.Creature, creature.Id);
    }

    /// <summary>The card for a target as things stand now; null once they are dead or gone.</summary>
    public InspectCard? Inspect(InspectTarget target) => target.Kind switch
    {
        InspectKind.Colonist or InspectKind.Resident => _townNpcs.FirstOrDefault(npc => npc.Id == target.Id && npc.IsAlive) is { } npc ? ResidentCard(npc) : null,
        _ => _enemies.FirstOrDefault(enemy => enemy.Id == target.Id && enemy.IsAlive) is { } enemy ? CreatureCard(enemy) : null
    };

    private static string Percent(float value) => $"{(int)MathF.Round(Math.Clamp(value, 0f, 1f) * 100f)}%";

    /// <summary>A resident's card (0.3.10: the crew's card and the resident's in one): health, home, work, skills, shop.</summary>
    private InspectCard ResidentCard(TownNpc npc)
    {
        var stats = npc.Stats;
        var lines = new List<string>
        {
            $"Health {npc.HitPoints:0}/{npc.MaximumHitPoints:0}",
            npc.HomeCell is { } home ? $"Lives at {home.X},{home.Y} · happiness {GetHappiness(npc):+0.0;-0.0;0}" : "Homeless",
            $"Doing: {npc.Activity}",
            $"Best at: {string.Join(", ", Enum.GetValues<ColonistSkill>().OrderByDescending(stats.Level).Take(3).Select(skill => $"{skill} {stats.Level(skill)}{(stats.Passion(skill) == PassionLevel.None ? "" : "+")}"))}",
            !npc.WorksStations || TownTrades.Idle(npc.Kind) ? "Works no stations"
                : npc.Kind == TownNpcKind.Settler ? "Works any station near home by day, by skill"
                : npc.Trade.Count == 0 ? "Works the basic benches near home by day"
                : $"Works {string.Join(", ", npc.Trade.Select(station => station.DisplayName()))} and the basic benches by day"
        };
        if (npc.Work.Order().FirstOrDefault() is var first && npc.Work.Order().Any())
            lines.Add($"Drawn to: {string.Join(", ", npc.Work.Order().Where(type => npc.Work.Priority(type) == WorkCatalog.Highest).Select(type => WorkCatalog.Get(type).Label))}");
        if (npc.Gear.Count > 0)
        {
            var (damage, reach, _) = ResidentAttack(npc);
            lines.Add($"Gear: {string.Join(", ", npc.Gear.OrderBy(pair => pair.Key).Select(pair => Items[pair.Value].Name))} · DEF {ResidentDefense(npc)}" +
                      (npc.IsArmed ? $" · hits {damage:0} at {reach:0.#} tiles" : ""));
        }
        var carrying = new List<string>();
        if (!npc.Carried.IsEmpty)
            carrying.Add($"{npc.Carried.Count} {Items[npc.Carried.ItemId].Name}");
        carrying.AddRange(npc.Held.Concat(npc.Pouch).Select(pair => $"{pair.Value} {Items[pair.Key].Name}"));
        if (carrying.Count > 0)
            lines.Add($"Carrying: {string.Join(", ", carrying.Take(3))}{(carrying.Count > 3 ? ", …" : "")}");
        if (!npc.IsBound && GetShop(npc).Count > 0)
            lines.Add($"Sells {GetShop(npc).Count} things (right-click to trade)");
        var subtitle = npc.IsBound ? $"{npc.Definition.Role} · bound" : $"{npc.Definition.Role} · {npc.Profile.Age} · {npc.Profile.BackstoryName}";
        return new InspectCard(new InspectTarget(InspectKind.Resident, npc.Id), npc.Name,
            subtitle, npc.HitPoints / Math.Max(1f, npc.MaximumHitPoints), null, lines);
    }

    private InspectCard CreatureCard(EnemyAgent enemy)
    {
        var lines = new List<string>
        {
            $"Health {enemy.HitPoints:0}/{enemy.MaximumHitPoints:0}",
            enemy.IsPassive ? "Passive: leaves you alone" : $"Hits for about {enemy.AttackDamage:0}",
            $"Defense {CreatureDefense(enemy)}"
        };
        if (enemy.IsBoss)
            lines.Add("Boss");
        return new InspectCard(new InspectTarget(InspectKind.Creature, enemy.Id), enemy.DisplayName,
            enemy.IsBoss ? "Boss" : enemy.IsPassive ? "Critter" : "Creature", enemy.HitPoints / Math.Max(1f, enemy.MaximumHitPoints), null, lines);
    }
}
