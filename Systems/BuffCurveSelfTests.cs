using Rimtera.Characters;

namespace Rimtera.Systems;

/// <summary>Buff durations on Terraria's scale (0.3.2): every buff item lasts as long as its Terraria counterpart, and the status lasts that long in play.</summary>
public static class BuffCurveSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var items = new ItemCatalog();
        float Minutes(ItemId id) => items[id].StatusDurationHours / GameSimulation.VitalHoursPerRealSecond / 60f;

        // ---- every listed item takes its counterpart's minutes; no consumable buff is left under two minutes ----
        var wrong = BuffCurve.Potions.Where(entry => Math.Abs(Minutes(entry.Item) - entry.Minutes) > 0.01f).Select(entry => entry.Item).ToArray();
        var shortBuffs = items.All.Where(item => item.UseKind == ItemUseKind.Consumable && item.GrantsStatus is { } status &&
                                                 !StatusEffectSystem.IsDebuff(status) && Minutes(item.Id) < 1.99f).Select(item => item.Id).ToArray();
        check(wrong.Length == 0 && shortBuffs.Length == 0 && Math.Abs(Minutes(ItemId.SpeedStim) - 8f) < 0.01f &&
              Math.Abs(Minutes(ItemId.BuildersBrew) - 15f) < 0.01f,
            $"buff items last as long as their Terraria counterparts: swiftness 8 min, builder 15 min, thorns 2 min " +
            $"(wrong: {string.Join(", ", wrong)}; under 2 min: {string.Join(", ", shortBuffs)})");

        // ---- food: a common meal gives Terraria's ten minutes of Well Fed, better meals longer ----
        check(Math.Abs(Minutes(ItemId.ProteinRation) - 10f) < 0.01f && Minutes(ItemId.CoralSushi) > Minutes(ItemId.ProteinRation) &&
              Minutes(ItemId.ColonyFeast) > Minutes(ItemId.CoralSushi) && Minutes(ItemId.ColonyFeast) <= 20f,
            $"a common meal gives ten minutes of Well Fed, better meals longer, a feast at most twenty " +
            $"(ration {Minutes(ItemId.ProteinRation):0.#}, sushi {Minutes(ItemId.CoralSushi):0.#}, feast {Minutes(ItemId.ColonyFeast):0.#} min)");

        // ---- in play: the status is not cut short by its cap, and runs out on time ----
        var statuses = new StatusEffectController();
        var hours = items[ItemId.SpeedStim].StatusDurationHours;
        statuses.Apply(StatusId.Haste, hours);
        var full = Math.Abs(statuses.RemainingHours(StatusId.Haste) - hours) < 1e-3f;
        statuses.Advance(BuffCurve.Hours(7.9f));
        var stillOn = statuses.IsActive(StatusId.Haste);
        statuses.Advance(BuffCurve.Hours(0.2f));
        var restored = new StatusEffectController();
        var probe = new StatusEffectController();
        probe.Apply(StatusId.Builder, BuffCurve.Hours(15));
        check(full && stillOn && !statuses.IsActive(StatusId.Haste) && restored.Restore(probe.Active) &&
              restored.RemainingHours(StatusId.Builder) > BuffCurve.Hours(14.9f),
            "a speed stim's haste lasts its full eight minutes and then ends; a fifteen-minute builder buff survives a save");
    }
}
