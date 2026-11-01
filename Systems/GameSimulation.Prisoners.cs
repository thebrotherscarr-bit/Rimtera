using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Prisoners and conversion (0.5.6, docs/VISION.md §3.3). Human raiders (raider grunts and their kin, dust raiders,
/// scavengers, pirate deckhands and gunners) surrender when brought down to <see cref="SurrenderShare"/> of their life, if
/// the colony has a free house to hold them in; with no cell free they get away. A prisoner stays in their cell, eats from
/// storage and does no work. Every hour the colony's best talker works on them, wearing their resistance down by their
/// Social; when it breaks they join as a settler. Settlers and prisoners can be sent away (the Housing tab's Send away);
/// gear they wore comes back to you.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>A human raider surrenders at or below this share of their life.</summary>
    public const float SurrenderShare = 0.15f;

    /// <summary>The creatures that are people: raiders who can surrender and be recruited.</summary>
    public static bool IsHumanRaider(EnemyKind kind) => kind is EnemyKind.EventGrunt or EnemyKind.EventScout or EnemyKind.EventMarksman or
        EnemyKind.EventBrute or EnemyKind.EventCommander or EnemyKind.EventDustRaider or EnemyKind.EventScavengerGrunt or
        EnemyKind.EventScavengerBrute or EnemyKind.EventScavengerSniper or EnemyKind.EventScavengerRigger or
        EnemyKind.EventPirateDeckhand or EnemyKind.EventPirateGunner;

    /// <summary>The prisoners held in the colony.</summary>
    public IReadOnlyList<TownNpc> Prisoners => _townNpcs.Where(npc => npc.IsPrisoner && npc.IsAlive).ToArray();

    private Random? _prisonStream;
    private Random PrisonRandom => _prisonStream ??= new Random(Seed ^ 0x9215C);

    /// <summary>Each step: a human raider brought low surrenders into a free cell, or gets away when there is none.</summary>
    private void UpdateSurrenders()
    {
        List<EnemyAgent>? beaten = null;
        foreach (var enemy in _enemies)
            if (enemy.IsAlive && !enemy.IsBoss && IsHumanRaider(enemy.Kind) && enemy.HitPoints <= enemy.MaximumHitPoints * SurrenderShare)
                (beaten ??= new List<EnemyAgent>()).Add(enemy);
        if (beaten is null)
            return;
        foreach (var enemy in beaten)
        {
            RecordDeath(enemy, DeathReason.Despawned); // no loot: they are taken, not killed
            _enemies.Remove(enemy);
            _raidEnemyIds.Remove(enemy.Id);
            if (FindFreeHouse() is not { } cell)
            {
                PostMessage($"The {enemy.DisplayName} surrenders, but there is no free room to hold them. They get away.");
                continue;
            }
            var prisoner = AddSettler(2000 + _townNpcs.Count, StandingAt(cell));
            prisoner.Motion.Position = StandingAt(cell);
            prisoner.HomeCell = cell;
            prisoner.IsPrisoner = true;
            prisoner.Resistance = 4f + PrisonRandom.Next(5);
            if (Backgrounds.Find("wasteland-raider") is { } raider && prisoner.Childhood is { } childhood)
                prisoner.SetStory(childhood, raider);
            PostMessage($"The {enemy.DisplayName} surrenders: {prisoner.Name} is held prisoner. Your best talker will work on recruiting them.");
            Record($"{prisoner.Name}, a {enemy.DisplayName.ToLowerInvariant()}, was taken prisoner.");
        }
    }

    /// <summary>The colony's best talker: the free, housed person with the highest Social.</summary>
    public TownNpc? Recruiter => _townNpcs
        .Where(npc => npc.IsAlive && !npc.IsBound && !npc.IsPrisoner && npc.HomeCell is not null && npc.Kind != TownNpcKind.StationAi)
        .MaxBy(npc => npc.Stats.Level(ColonistSkill.Social));

    /// <summary>Resistance a recruiter wears down an hour.</summary>
    public static float RecruitPerHour(int social) => 0.5f + social * 0.15f;

    /// <summary>Hourly: the best talker works on every prisoner; a prisoner whose resistance breaks joins the colony.</summary>
    private void UpdateRecruiting()
    {
        if (Recruiter is not { } recruiter)
            return;
        foreach (var prisoner in Prisoners)
        {
            prisoner.Resistance = Math.Max(0f, prisoner.Resistance - RecruitPerHour(recruiter.Stats.Level(ColonistSkill.Social)));
            if (prisoner.Resistance > 0f)
                continue;
            prisoner.IsPrisoner = false;
            prisoner.RefreshWork();
            PostMessage($"{recruiter.Name} talked {prisoner.Name} round: they join the colony as a settler.");
            Record($"{prisoner.Name} was recruited by {recruiter.Name}.");
        }
    }

    /// <summary>Sends a settler or a prisoner away for good; their gear comes back to you. Returns what happened.</summary>
    public string SendAway(TownNpc npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (!_townNpcs.Contains(npc) || !npc.IsAlive)
            return $"{npc.Name} is not here.";
        if (npc.Kind != TownNpcKind.Settler)
            return $"{npc.Name} the {npc.Definition.Role} keeps their trade here; only settlers and prisoners can be sent away.";
        UnequipResident(npc);
        DropColonistLoad(npc);
        DropPouch(npc, npc.Motion.Position + npc.Motion.Size / 2f);
        _townNpcs.Remove(npc);
        var was = npc.IsPrisoner ? "released" : "sent away";
        Record($"{npc.Name} was {was}.");
        return $"{npc.Name} was {was} and leaves the colony.";
    }

    /// <summary>A prisoner's step: they keep to their cell.</summary>
    private void UpdatePrisoner(TownNpc npc, Vector2 center, float deltaSeconds)
    {
        npc.CurrentWork = null;
        if (TryEatFromStorage(npc, center, deltaSeconds))
            return;
        npc.Activity = $"Held prisoner (resistance {npc.Resistance:0.#})";
        StepTownNpcToward(npc, center, npc.HomePosition, deltaSeconds);
    }
}
