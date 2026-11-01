using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>One outpost (0.4.2): residents whose homes lie far from the base and near one another.</summary>
public sealed record Outpost(int Number, Vector2 Center, IReadOnlyList<TownNpc> People)
{
    public int Guards => People.Count(npc => npc.IsArmed);
    public string Label => $"Outpost {Number}";
}

/// <summary>
/// Outposts (0.4.2, docs/VISION.md §3.4): a resident can live anywhere there is valid housing (the Housing tab's Move here),
/// and one whose home lies more than <see cref="OutpostDistance"/> tiles from the base lives at an outpost. Homes within
/// <see cref="OutpostSpread"/> tiles of one another make one outpost. People at an outpost work, store and guard there as
/// they would at the base. New arrivals settle at the base first: you staff an outpost by moving people out to it.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>A home farther than this from the base (tiles) is at an outpost; outpost homes within the spread group together.</summary>
    public const float OutpostDistance = 80f, OutpostSpread = 40f;

    /// <summary>The base: the station AI's home, or the landing site until it moves in.</summary>
    public Vector2 BaseCenter => _townNpcs.FirstOrDefault(npc => npc.Kind == TownNpcKind.StationAi && !npc.IsBound && npc.HomeCell is not null) is { } ai
        ? ai.HomePosition
        : LandingSpot;

    /// <summary>Whether a resident's home is at an outpost (housed, and far from the base).</summary>
    public bool AtOutpost(TownNpc npc) => npc.HomeCell is not null && !npc.IsBound && Vector2.Distance(npc.HomePosition, BaseCenter) > OutpostDistance;

    /// <summary>The outposts, numbered from the one nearest the base.</summary>
    public IReadOnlyList<Outpost> Outposts
    {
        get
        {
            var groups = new List<List<TownNpc>>();
            foreach (var npc in _townNpcs.Where(AtOutpost).OrderBy(npc => Vector2.Distance(npc.HomePosition, BaseCenter)))
            {
                var group = groups.FirstOrDefault(members => members.Any(other => Vector2.Distance(other.HomePosition, npc.HomePosition) <= OutpostSpread));
                if (group is null)
                    groups.Add([npc]);
                else
                    group.Add(npc);
            }
            return groups.Select((members, index) => new Outpost(index + 1,
                    new Vector2(members.Average(npc => npc.HomePosition.X), members.Average(npc => npc.HomePosition.Y)), members))
                .ToArray();
        }
    }

    /// <summary>The outpost a resident lives at, if any.</summary>
    public Outpost? OutpostOf(TownNpc npc) => Outposts.FirstOrDefault(outpost => outpost.People.Contains(npc));
}
