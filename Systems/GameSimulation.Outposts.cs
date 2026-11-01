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

    private const string OutpostCountFlag = "outposts.count", OutpostsFoundedFlag = "history.outposts", OutpostSyncFlag = "outposts.synced";

    /// <summary>Outposts founded over the run (0.7.1).</summary>
    public int OutpostsFounded => (int)Flags.Get(OutpostsFoundedFlag);

    /// <summary>Where an outpost lies from the base, in words ("120 tiles east").</summary>
    public string OutpostBearing(Outpost outpost)
    {
        var dx = outpost.Center.X - BaseCenter.X;
        return $"{MathF.Abs(dx):0} tiles {(dx < 0 ? "west" : "east")} of the base";
    }

    /// <summary>
    /// Hourly (0.7.1): a new outpost is founded when people first make a home far from the base, and one is abandoned when
    /// its last resident leaves or dies. Each writes a letter and goes in the chronicle.
    /// </summary>
    private void UpdateOutposts()
    {
        var outposts = Outposts;
        var known = (int)Flags.Get(OutpostCountFlag);
        // A world from before 0.7.1 starts counting from what stands, without letters for outposts it already had.
        if (Flags.Set(OutpostSyncFlag))
        {
            SetFlagValue(OutpostCountFlag, outposts.Count);
            if (outposts.Count > 0)
                SetFlagValue(OutpostsFoundedFlag, Math.Max(OutpostsFounded, outposts.Count));
            return;
        }
        if (outposts.Count == known)
            return;
        SetFlagValue(OutpostCountFlag, outposts.Count);
        if (outposts.Count > known)
        {
            var newest = outposts.OrderByDescending(outpost => outpost.People.Max(npc => npc.Id)).First();
            Flags.Increment(OutpostsFoundedFlag);
            var guard = newest.Guards > 0 ? "" : " Nobody there is armed yet: give one of them a weapon and a post.";
            SendLetter(LetterKind.Good, "Outpost founded",
                $"{string.Join(" and ", newest.People.Select(npc => npc.Name))} made a home {OutpostBearing(newest)}: a new outpost.{guard}");
            Record($"An outpost was founded {OutpostBearing(newest)}.");
        }
        else
        {
            SendLetter(LetterKind.BadNews, "Outpost abandoned", "An outpost stands empty: its last resident has gone.");
            Record("An outpost was abandoned.");
        }
    }
}
