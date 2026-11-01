using Rimtera.Characters;

namespace Rimtera.Systems;

/// <summary>A fitted bionic (0.5.4; append-only, saved as bytes).</summary>
public enum BionicPart : byte { Arm, Leg, Eye, NeuralCoprocessor }

/// <summary>
/// Bionics (0.5.4, docs/VISION.md §3.3: the sci-fi twist on lasting injuries). A bionic arm, leg or eye, crafted at the
/// Nano-Fabricator, replaces a part a resident lost: the work it ruled out comes back, and the part works a little better than
/// the one it replaces (a bionic leg walks at 105%, a bionic eye aims 10% truer). The neural coprocessor is an implant anyone
/// can take once: +3 Intellectual. You hand one over like gear (the Housing tab's Give held); someone at home with Medical 6
/// or more fits it. Bionics are saved.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>The Medical a fitter needs.</summary>
    public const int FittingSkill = 6;

    public static BionicPart? BionicFor(ItemId item) => item switch
    {
        ItemId.BionicArm => BionicPart.Arm,
        ItemId.BionicLeg => BionicPart.Leg,
        ItemId.BionicEye => BionicPart.Eye,
        ItemId.NeuralCoprocessor => BionicPart.NeuralCoprocessor,
        _ => null
    };

    /// <summary>Who can fit a bionic to <paramref name="patient"/>: the most skilled housed resident with Medical 6+, not the patient.</summary>
    public TownNpc? FitterFor(TownNpc patient) => _townNpcs
        .Where(npc => !ReferenceEquals(npc, patient) && npc.IsAlive && !npc.IsBound && npc.HomeCell is not null &&
                      npc.Kind != TownNpcKind.StationAi && npc.Stats.Level(ColonistSkill.Medical) >= FittingSkill)
        .MaxBy(npc => npc.Stats.Level(ColonistSkill.Medical));

    /// <summary>Using a bionic from the hotbar: it goes to the nearest resident within six tiles who can take it.</summary>
    private string UseBionic(ItemId item)
    {
        var near = People.Where(npc => System.Numerics.Vector2.Distance(npc.Motion.Position, Player.Motion.Position) <= 6f)
            .OrderBy(npc => System.Numerics.Vector2.Distance(npc.Motion.Position, Player.Motion.Position)).ToArray();
        var patient = near.FirstOrDefault(npc => BionicFor(item) switch
        {
            BionicPart.Arm => npc.Injuries.Contains(LastingInjury.LostArm),
            BionicPart.Leg => npc.Injuries.Contains(LastingInjury.LostLeg),
            BionicPart.Eye => npc.Injuries.Contains(LastingInjury.LostEye),
            _ => npc.Kind != TownNpcKind.StationAi && !npc.Bionics.Contains(BionicPart.NeuralCoprocessor)
        });
        return patient is null
            ? $"Stand beside someone who needs the {Items[item].Name.ToLowerInvariant()}, or hand it over in the Housing tab."
            : FitBionic(patient, item);
    }

    /// <summary>Fits a bionic from your bag to a resident; returns what happened.</summary>
    public string FitBionic(TownNpc npc, ItemId item)
    {
        if (BionicFor(item) is not { } part)
            return $"{Items[item].Name} is not a bionic.";
        if (npc.Kind == TownNpcKind.StationAi)
            return $"{npc.Name} is a station AI: there is nothing to fit a {Items[item].Name.ToLowerInvariant()} to.";
        var replaces = part switch
        {
            BionicPart.Arm => LastingInjury.LostArm,
            BionicPart.Leg => LastingInjury.LostLeg,
            BionicPart.Eye => LastingInjury.LostEye,
            _ => (LastingInjury?)null
        };
        if (replaces is { } lost && !npc.Injuries.Contains(lost))
            return $"{npc.Name} has no lost {part.ToString().ToLowerInvariant()} to replace.";
        if (part == BionicPart.NeuralCoprocessor && npc.Bionics.Contains(BionicPart.NeuralCoprocessor))
            return $"{npc.Name} already has a neural coprocessor.";
        if (FitterFor(npc) is not { } fitter)
            return $"Nobody here can fit it: it takes a resident with Medical {FittingSkill} or more (a medic).";
        if (Player.Inventory.Count(item) <= 0 || !Player.Inventory.TryConsume(item))
            return $"You are not carrying a {Items[item].Name.ToLowerInvariant()}.";
        npc.FitBionic(part, replaces);
        if (part == BionicPart.NeuralCoprocessor)
        {
            var levels = npc.Profile.SkillLevels!.ToArray();
            levels[(int)ColonistSkill.Intellectual] = Math.Min(ColonistCatalog.MaximumSkillLevel, levels[(int)ColonistSkill.Intellectual] + 3);
            npc.GrowSkills(npc.Profile with { SkillLevels = levels });
        }
        Record($"{fitter.Name} fitted {npc.Name} with a {Items[item].Name.ToLowerInvariant()}.");
        return $"{fitter.Name} fits {npc.Name} with the {Items[item].Name.ToLowerInvariant()}.";
    }
}
