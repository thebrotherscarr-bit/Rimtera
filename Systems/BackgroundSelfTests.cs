using System.Numerics;
using Rimtera.Characters;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Backgrounds (0.5.1): a childhood and an adulthood set each person's skill spread and what they will not do; a trade's
/// adulthood fits it, and its own work is never refused; stories are saved.
/// </summary>
public static class BackgroundSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        // ---- the catalog ----
        var all = Backgrounds.Childhoods.Concat(Backgrounds.Adulthoods).ToArray();
        var trades = TownNpcCatalog.All.Select(definition => definition.Kind)
            .Where(kind => kind != TownNpcKind.Settler && TownPeople.SkillsOf(kind).Count > 0).ToArray();
        var refusingOwnWork = trades.Where(kind => Enumerable.Range(0, 40).Any(seed =>
        {
            var (childhood, adulthood) = Backgrounds.For(kind, seed);
            return Backgrounds.Refused(childhood, adulthood).Any(work => WorkCatalog.Get(work).Skill == TownPeople.SkillsOf(kind)[0]);
        })).ToArray();
        check(Backgrounds.Childhoods.Count >= 10 && Backgrounds.Adulthoods.Count >= 12 && all.Select(story => story.Id).Distinct().Count() == all.Length &&
              Backgrounds.Childhoods.All(story => story.Childhood) && Backgrounds.Adulthoods.All(story => !story.Childhood) &&
              all.All(story => story.Skills.Length > 0) && refusingOwnWork.Length == 0,
            $"{Backgrounds.Childhoods.Count} childhoods and {Backgrounds.Adulthoods.Count} adulthoods, each adding skills; no trade's story refuses its own work " +
            $"({string.Join(", ", refusingOwnWork)})");

        // ---- a resident's story fits their trade and adds to their skills ----
        var game = new GameSimulation(seed: 9891, width: 120, height: 56) { FamilySpawnsEnabled = false };
        var medic = game.AddTownNpc(TownNpcKind.Medic, new Vector2(30f, 20f), bound: false);
        var plain = TownPeople.ProfileFor(TownNpcKind.Medic, medic.Name, unchecked(game.Seed * 131 + (int)TownNpcKind.Medic));
        var medicalGain = medic.Stats.Level(ColonistSkill.Medical) - plain.Stats.Level(ColonistSkill.Medical);
        var cook = game.AddTownNpc(TownNpcKind.Cook, new Vector2(40f, 20f), bound: false);
        check(medic.Adulthood?.Id == "field-medic" && medic.Childhood is { Childhood: true } && medicalGain >= 5 - 0 &&
              cook.Adulthood?.Trades?.Contains(TownNpcKind.Cook) == true,
            $"a resident's adulthood fits their trade (the medic was a {medic.Adulthood?.Title}, the cook a {cook.Adulthood?.Title}), " +
            $"and their story adds to their skills (Medical +{medicalGain})");

        // ---- what they will not do: no such work, and a pacifist takes no weapon ----
        var monk = game.AddTownNpc(TownNpcKind.Settler, new Vector2(50f, 20f), bound: false);
        monk.HomeCell = new GridPoint(50, 21);
        monk.SetStory(Backgrounds.Childhoods.First(story => story.Id == "noble-heir"), Backgrounds.Adulthoods.First(story => story.Id == "pacifist-monk"));
        game.Player.Inventory.Add(ItemId.ScrapPistol, 1);
        var refused = game.EquipResident(monk, ItemId.ScrapPistol);
        check(monk.Work.Priority(WorkType.Guard) == WorkCatalog.Off && monk.Work.Priority(WorkType.Farm) == WorkCatalog.Off &&
              monk.Work.Priority(WorkType.Fish) == WorkCatalog.Off && !monk.IsArmed && refused.Contains("will not fight") &&
              game.Player.Inventory.Count(ItemId.ScrapPistol) == 1 &&
              game.Inspect(new InspectTarget(InspectKind.Resident, monk.Id))!.Lines.Any(line => line.Contains("won't") && line.Contains("guard")),
            $"a noble pacifist won't farm, fish or guard, takes no weapon, and their card says so ('{refused}')");

        // ---- settlers' stories vary; stories are saved; an older save's resident gets one ----
        var stories = Enumerable.Range(0, 8).Select(_ => game.AddTownNpc(TownNpcKind.Settler, new Vector2(60f, 20f), bound: false))
            .Select(npc => $"{npc.Childhood!.Id}/{npc.Adulthood!.Id}").Distinct().Count();
        var reloaded = GameSimulation.Restore(game.CaptureSnapshot());
        var monkAgain = reloaded.TownNpcs.Single(npc => npc.Name == monk.Name);
        var old = game.CaptureSnapshot();
        var legacy = old with { Content = old.Content! with { TownNpcs = old.Content.TownNpcs!.Select(npc => npc with { Childhood = null, Adulthood = null }).ToArray() } };
        var legacyMedic = GameSimulation.Restore(legacy).TownNpcs.Single(npc => npc.Kind == TownNpcKind.Medic);
        check(stories >= 4 && monkAgain.Adulthood?.Id == "pacifist-monk" && monkAgain.Childhood?.Id == "noble-heir" &&
              legacyMedic.Adulthood?.Id == "field-medic" && legacyMedic.Stats.SkillLevels.SequenceEqual(medic.Stats.SkillLevels),
            $"settlers' stories vary ({stories} different among 8); a story is saved; a resident from an older save gets the one this world gives them, keeping their skills");
    }
}
