using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>A raid on its way (0.4.3): where it will hit, from which side, how many, and when.</summary>
public readonly record struct PendingRaid(Vector2 Target, string TargetName, int Side, int Count, float DueHour, BiomeZone Biome)
{
    public string SideName => Side < 0 ? "west" : "east";
}

/// <summary>
/// Raids with warning (0.4.3, docs/VISION.md §3.4): the storyteller announces a raid before it comes: how big, where it
/// will hit (the base, or an outpost) and from which side, and how long you have. It arrives at its target, not at you, so
/// your people hold their areas while you are away, and you can come home to help: a small tower defence. With nobody
/// housed yet, a raid comes for you, as before. The plan is kept in the world's flags, so it survives a save.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>Hours of warning before a raid arrives (on the clock: two in-game hours, about two and a half minutes).</summary>
    public const float RaidWarningHours = 2f;
    /// <summary>The most raiders one raid brings.</summary>
    public const int MaxRaiders = 10;

    private const string RaidDueFlag = "raid.due", RaidXFlag = "raid.x", RaidYFlag = "raid.y", RaidSideFlag = "raid.side",
        RaidCountFlag = "raid.count", RaidBiomeFlag = "raid.biome", RaidTargetFlag = "raid.target";

    /// <summary>The raid on its way, or null.</summary>
    public PendingRaid? PendingRaid
    {
        get
        {
            if (!Flags.IsSet(RaidDueFlag))
                return null;
            var target = new Vector2(Flags.Get(RaidXFlag) / 10f, Flags.Get(RaidYFlag) / 10f);
            var number = (int)Flags.Get(RaidTargetFlag);
            return new PendingRaid(target, number == 0 ? "the base" : number < 0 ? "you" : $"Outpost {number}",
                Flags.Get(RaidSideFlag) == 1 ? -1 : 1, (int)Flags.Get(RaidCountFlag), Flags.Get(RaidDueFlag) / 100f,
                (BiomeZone)Flags.Get(RaidBiomeFlag));
        }
    }

    /// <summary>Hours until the raid on its way arrives (0 when none).</summary>
    public float RaidHoursLeft => PendingRaid is { } raid ? Math.Max(0f, raid.DueHour - ElapsedGameHours) : 0f;

    /// <summary>
    /// Plans a raid sized by <paramref name="threatPoints"/>: it picks a target (the base, or an outpost, the bigger the more
    /// likely; you when nobody is housed), a side and a size, and announces it <see cref="RaidWarningHours"/> ahead.
    /// Returns how many will come (0 when a raid is already on its way).
    /// </summary>
    public int PlanRaid(float threatPoints, out string description, float warningHours = RaidWarningHours)
    {
        description = string.Empty;
        if (PendingRaid is not null)
            return 0;
        // 0.4.4 balance: the storyteller answers the colony's guards: one more raider for every two armed residents (up to 10).
        var armed = _townNpcs.Count(npc => npc.IsArmed && !npc.IsBound && npc.HomeCell is not null);
        var count = Math.Clamp((int)MathF.Round(threatPoints / 45f) + armed / 2, 2, MaxRaiders);
        var housed = _townNpcs.Any(npc => !npc.IsBound && npc.HomeCell is not null);
        Vector2 target;
        int number;
        if (!housed)
        {
            target = PlayerCenter;
            number = -1;
        }
        else
        {
            // The base and each outpost, weighted by how many live there (the station AI counts twice: it is what raids want).
            var options = new List<(Vector2 Where, int Number, float Weight)>
            {
                (BaseCenter, 0, _townNpcs.Count(npc => !npc.IsBound && npc.HomeCell is not null && !AtOutpost(npc)) +
                                (_townNpcs.Any(npc => npc.Kind == TownNpcKind.StationAi && !npc.IsBound) ? 2f : 0f))
            };
            options.AddRange(Outposts.Select(outpost => (outpost.Center, outpost.Number, (float)outpost.People.Count)));
            var roll = _storyteller.NextUnit() * options.Sum(option => Math.Max(0.5f, option.Weight));
            var chosen = options[^1];
            foreach (var option in options)
            {
                roll -= Math.Max(0.5f, option.Weight);
                if (roll <= 0f)
                {
                    chosen = option;
                    break;
                }
            }
            (target, number) = (chosen.Where, chosen.Number);
        }
        var side = _storyteller.NextUnit() < 0.5f ? -1 : 1;
        var biome = BiomeScanner.Scan(World, RegionMap.CellAt(target)).Zone;
        SetFlagValue(RaidDueFlag, Math.Max(1L, (long)MathF.Round((ElapsedGameHours + warningHours) * 100f)));
        SetFlagValue(RaidXFlag, (long)MathF.Round(target.X * 10f));
        SetFlagValue(RaidYFlag, (long)MathF.Round(target.Y * 10f));
        SetFlagValue(RaidSideFlag, side < 0 ? 1 : 2);
        SetFlagValue(RaidCountFlag, count);
        SetFlagValue(RaidBiomeFlag, (long)biome);
        SetFlagValue(RaidTargetFlag, number);
        var raid = PendingRaid!.Value;
        description = $"About {count} hostiles are coming for {raid.TargetName} from the {raid.SideName}, in about {warningHours:0.#} hours.";
        Record($"A raid of about {count} was sighted, heading for {raid.TargetName}.");
        return count;
    }

    /// <summary>Each step: a raid whose hour has come arrives at its target.</summary>
    private void UpdateRaids()
    {
        if (PendingRaid is not { } raid || ElapsedGameHours < raid.DueHour)
            return;
        foreach (var flag in new[] { RaidDueFlag, RaidXFlag, RaidYFlag, RaidSideFlag, RaidCountFlag, RaidBiomeFlag, RaidTargetFlag })
            Flags.Clear(flag);
        var arrived = SpawnRaidAt(raid);
        PostMessage(arrived == 0
            ? $"The raid heading for {raid.TargetName} never came."
            : $"Raid! {arrived} hostiles are hitting {raid.TargetName} from the {raid.SideName}!");
    }

    /// <summary>Spawns a raid just beyond view of its target, on its side; returns how many arrived.</summary>
    private int SpawnRaidAt(PendingRaid raid)
    {
        var night = ClockHour >= 18f || ClockHour < 6f;
        var side = raid.Side;
        var centerX = (int)MathF.Floor(raid.Target.X);
        var centerY = (int)MathF.Floor(raid.Target.Y);
        var arrived = 0;
        for (var index = 0; index < raid.Count; index++)
        {
            var kind = BiomeSpawnTables.Pick(raid.Biome, _storyteller.NextUnit(), night);
            var archetype = EnemyArchetypes.Get(kind);
            var x = centerX + side * ((int)ViewHalfWidth + 3 + index * 2);
            if (x < 2 || x >= World.Width - 2)
            {
                side = -side;
                x = centerX + side * ((int)ViewHalfWidth + 3 + index * 2);
                if (x < 2 || x >= World.Width - 2)
                    continue;
            }
            if (!TryFindGroundNear(x, centerY, out var groundY))
                continue;
            var lift = archetype.Flying ? 3f : 0f;
            _raidEnemyIds.Add(SpawnCreature(kind, new Vector2(x, groundY - archetype.Size.Y - lift)).Id);
            arrived++;
        }
        return arrived;
    }

    /// <summary>Standing ground in column <paramref name="x"/> within ten tiles of <paramref name="y"/>, else the surface.</summary>
    private bool TryFindGroundNear(int x, int y, out int groundY)
    {
        for (var distance = 0; distance <= 10; distance++)
        for (var sign = -1; sign <= 1; sign += 2)
        {
            var row = y + sign * distance;
            if (row < 3 || row >= World.Height)
                continue;
            if (Tiles[World.GetCell(new GridPoint(x, row)).TileId].IsSolid &&
                !Tiles[World.GetCell(new GridPoint(x, row - 1)).TileId].IsSolid &&
                !Tiles[World.GetCell(new GridPoint(x, row - 2)).TileId].IsSolid &&
                World.GetCell(new GridPoint(x, row - 1)).LiquidKind != LiquidKind.Lava)
            {
                groundY = row;
                return true;
            }
        }
        return TryFindGround(x, out groundY);
    }
}
