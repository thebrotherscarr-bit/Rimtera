using Rimtera.Characters;

namespace Rimtera.Systems;

/// <summary>A lasting injury (0.5.3; append-only, saved as bytes).</summary>
public enum LastingInjury : byte { Scar, LostEye, LostArm, LostLeg }

/// <summary>
/// Health and lasting injuries (0.5.3, docs/VISION.md §3.3): a hard hit can leave a resident with a wound that does not heal.
/// A scar is only a scar; a lost eye blunts their aim; a lost arm limits them to one-handed work (they can still cook, tend,
/// study and trade, but no longer craft at stations, farm, fish or fight); a lost leg slows them and keeps them from guarding.
/// Bionics (0.5.4) will replace lost parts. Injuries are saved.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>A hit that takes at least this share of a resident's life (after defense) may leave a lasting injury.</summary>
    public const float GraveHitShare = 0.2f;
    /// <summary>The chance a grave hit leaves one.</summary>
    public const float LastingInjuryChance = 0.2f;

    private Random? _injuryStream;
    private Random InjuryRandom => _injuryStream ??= new Random(Seed ^ 0x1A1A7);

    /// <summary>The work a set of injuries rules out.</summary>
    public static IEnumerable<WorkType> RuledOutBy(IReadOnlyCollection<LastingInjury> injuries)
    {
        if (injuries.Contains(LastingInjury.LostArm))
        {
            yield return WorkType.Operate;
            yield return WorkType.Farm;
            yield return WorkType.Fish;
            yield return WorkType.Guard;
        }
        if (injuries.Contains(LastingInjury.LostLeg))
            yield return WorkType.Guard;
    }

    /// <summary>Walking pace from legs: a lost leg slows them to 60%, both to 30%.</summary>
    public static float LegFactor(IReadOnlyCollection<LastingInjury> injuries) =>
        injuries.Count(injury => injury == LastingInjury.LostLeg) switch { 0 => 1f, 1 => 0.6f, _ => 0.3f };

    /// <summary>Gives a resident a lasting injury (raids, tests, the dev console). False when they cannot take another of that kind.</summary>
    public bool Injure(TownNpc npc, LastingInjury injury)
    {
        var limit = injury switch { LastingInjury.Scar => 3, _ => 2 };
        if (npc.Injuries.Count(existing => existing == injury) >= limit)
            return false;
        npc.AddInjury(injury);
        if (npc.IsArmed && !npc.WillDo(WorkType.Guard))
        {
            GiveBack(npc.Gear[ResidentGearSlot.Weapon], npc); // they can no longer fight: the weapon comes back to you
            npc.Gear.Remove(ResidentGearSlot.Weapon);
            RefreshResidentGear(npc);
        }
        var what = injury switch
        {
            LastingInjury.Scar => "was badly scarred",
            LastingInjury.LostEye => "lost an eye",
            LastingInjury.LostArm => "lost an arm",
            _ => "lost a leg"
        };
        PostMessage($"{npc.Name} {what}. It will not heal.");
        Record($"{npc.Name} the {npc.Definition.Role} {what}.");
        return true;
    }

    /// <summary>After a hit that leaves them alive: a grave one may leave a lasting injury.</summary>
    private void MaybeInjure(TownNpc npc, float damage)
    {
        if (npc.Kind == TownNpcKind.StationAi || damage < npc.MaximumHitPoints * GraveHitShare || InjuryRandom.NextDouble() >= LastingInjuryChance)
            return;
        var roll = InjuryRandom.NextDouble();
        var injury = roll < 0.4 ? LastingInjury.Scar : roll < 0.55 ? LastingInjury.LostEye : roll < 0.8 ? LastingInjury.LostArm : LastingInjury.LostLeg;
        if (!Injure(npc, injury))
            Injure(npc, LastingInjury.Scar);
    }
}
