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
    /// <summary>The character nearest a world point within <paramref name="radius"/> tiles of their body: crew first, then residents, then creatures.</summary>
    public InspectTarget? PickInspectTarget(Vector2 point, float radius = 1f)
    {
        float Distance(PawnMotionState motion) => DistanceToRect(point, motion.Position, motion.Size);
        var colonist = _colonists.Where(agent => agent.IsAlive && Distance(agent.Motion) <= radius).MinBy(agent => Distance(agent.Motion));
        if (colonist is not null)
            return new InspectTarget(InspectKind.Colonist, colonist.Id);
        var resident = _townNpcs.Where(npc => npc.IsAlive && Distance(npc.Motion) <= radius).MinBy(npc => Distance(npc.Motion));
        if (resident is not null)
            return new InspectTarget(InspectKind.Resident, resident.Id);
        var creature = _enemies.Where(enemy => enemy.IsAlive && Distance(enemy.Motion) <= radius).MinBy(enemy => Distance(enemy.Motion));
        return creature is null ? null : new InspectTarget(InspectKind.Creature, creature.Id);
    }

    /// <summary>The card for a target as things stand now; null once they are dead or gone.</summary>
    public InspectCard? Inspect(InspectTarget target) => target.Kind switch
    {
        InspectKind.Colonist => _colonists.FirstOrDefault(agent => agent.Id == target.Id && agent.IsAlive) is { } agent ? ColonistCard(agent) : null,
        InspectKind.Resident => _townNpcs.FirstOrDefault(npc => npc.Id == target.Id && npc.IsAlive) is { } npc ? ResidentCard(npc) : null,
        _ => _enemies.FirstOrDefault(enemy => enemy.Id == target.Id && enemy.IsAlive) is { } enemy ? CreatureCard(enemy) : null
    };

    private static string Percent(float value) => $"{(int)MathF.Round(Math.Clamp(value, 0f, 1f) * 100f)}%";

    private InspectCard ColonistCard(ColonistAgent agent)
    {
        var stats = agent.ColonistStats;
        var lines = new List<string>
        {
            $"Health {agent.HitPoints:0}/{agent.MaximumHitPoints:0}{(agent.IsDowned ? " · downed" : "")}",
            $"Mood {Percent(agent.Mood)} ({agent.MoodLabel}){(agent.Mind.Breaker.IsBreaking ? $" · {agent.Mind.Breaker.StateLabel}" : "")}",
            $"Food {Percent(agent.Needs.FoodReserve)} · Rest {Percent(agent.Needs.RestReserve)} · Joy {Percent(agent.Needs.JoyReserve)}",
            $"Doing: {agent.Activity}",
            $"Work: {string.Join(", ", agent.Work.Order().Take(4).Select(type => $"{WorkCatalog.Get(type).Label} {agent.Work.Priority(type)}"))}" +
                (agent.Work.Order().Count() > 4 ? ", …" : ""),
            $"Best at: {string.Join(", ", Enum.GetValues<ColonistSkill>().OrderByDescending(stats.Level).Take(3).Select(skill => $"{skill} {stats.Level(skill)}{(stats.Passion(skill) == PassionLevel.None ? "" : "+")}"))}",
            agent.FollowsPlayer ? "Follows you" : "Stays home"
        };
        var carrying = new List<string>();
        if (!agent.Carried.IsEmpty)
            carrying.Add($"{agent.Carried.Count} {Items[agent.Carried.ItemId].Name}");
        carrying.AddRange(agent.Held.Concat(agent.Pouch).Select(pair => $"{pair.Value} {Items[pair.Key].Name}"));
        if (carrying.Count > 0)
            lines.Add($"Carrying: {string.Join(", ", carrying.Take(3))}{(carrying.Count > 3 ? ", …" : "")}");
        foreach (var thought in agent.Thoughts.OrderBy(thought => thought.MoodOffset).Take(2))
            lines.Add($"Thinks: {thought.Label} ({thought.MoodOffset:+0;-0})");
        return new InspectCard(new InspectTarget(InspectKind.Colonist, agent.Id), agent.Name,
            $"Crew · {agent.Profile.Age} · {agent.Profile.Backstory}", agent.HitPoints / Math.Max(1f, agent.MaximumHitPoints), agent.Mood, lines);
    }

    private InspectCard ResidentCard(TownNpc npc)
    {
        var lines = new List<string>
        {
            $"Health {npc.HitPoints:0}/{npc.MaximumHitPoints:0}",
            npc.HomeCell is { } home ? $"Lives at {home.X},{home.Y} · happiness {GetHappiness(npc):+0.0;-0.0;0}" : "Homeless",
            $"Doing: {npc.Activity}",
            npc.WorksStations ? "Works bills near home by day" : "Doesn't work bills"
        };
        if (!npc.Carried.IsEmpty)
            lines.Add($"Carrying: {npc.Carried.Count} {Items[npc.Carried.ItemId].Name}");
        if (!npc.IsBound && GetShop(npc).Count > 0)
            lines.Add($"Sells {GetShop(npc).Count} things (right-click to trade)");
        return new InspectCard(new InspectTarget(InspectKind.Resident, npc.Id), npc.Name,
            npc.IsBound ? $"{npc.Definition.Role} · bound" : npc.Definition.Role, npc.HitPoints / Math.Max(1f, npc.MaximumHitPoints), null, lines);
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
