using Rimtera.Characters;

namespace Rimtera.Systems;

/// <summary>
/// Natural spawning on Terraria's model (0.3.4, EN-8): the base rate and cap, the time, depth and hardmode factors, the
/// ramp as the area fills, and the battle and calming stims.
/// </summary>
public static class SpawnRateSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        // ---- base: Terraria's 1-in-600 roll a tick (0.1 spawns a second) and a population of five ----
        var perSecond = -MathF.Log(1f - GameSimulation.SpawnAttemptChance(1f)) / 0.65f;
        check(Math.Abs(perSecond - 0.1f) < 0.002f && CombatTuning.BasePopulation == 5,
            $"the base spawn rate is Terraria's 1 in 600 ticks ({perSecond:0.000} a second) and the base population five");

        // ---- factors: night, underground, hardmode, battle and calming ----
        var plain = CombatTuning.SpawnFactors(false, false, false, 0, false);
        var night = CombatTuning.SpawnFactors(true, false, false, 0, false);
        var under = CombatTuning.SpawnFactors(false, true, false, 0, false);
        var hard = CombatTuning.SpawnFactors(false, false, false, 0, false, hardmode: true);
        var battle = CombatTuning.SpawnFactors(false, false, false, 0, false, battle: true);
        var calm = CombatTuning.SpawnFactors(false, false, false, 0, false, calm: true);
        bool Near(float value, float expected) => Math.Abs(value - expected) < 0.01f;
        check(plain == (1f, 1f) && Near(night.Rate, 1f / 0.6f) && Near(night.Cap, 1.3f) && Near(under.Rate, 1f / 0.45f) &&
              Near(under.Cap, 1.8f) && Near(hard.Rate, 1f / 0.9f) && Near(hard.Cap, 1.2f) && Near(battle.Rate, 2f) && Near(battle.Cap, 2f) &&
              Near(calm.Rate, 1f / 1.65f) && Near(calm.Cap, 0.7f),
            $"spawn factors follow Terraria: night ×{night.Rate:0.00}/×{night.Cap:0.0}, underground ×{under.Rate:0.00}/×{under.Cap:0.0}, " +
            $"hardmode ×{hard.Rate:0.00} and one more slot, battle ×2/×2, calming ×{calm.Rate:0.00}/×{calm.Cap:0.0}");

        // ---- the ramp: an emptier area fills faster ----
        check(Near(CombatTuning.SpawnRamp(0, 5), 1f / 0.6f) && Near(CombatTuning.SpawnRamp(1, 5), 1f / 0.7f) &&
              Near(CombatTuning.SpawnRamp(2, 5), 1f / 0.8f) && Near(CombatTuning.SpawnRamp(3, 5), 1f / 0.9f) && Near(CombatTuning.SpawnRamp(4, 5), 1f),
            "spawns come faster while the area is emptier (rate ×1/0.6, 0.7, 0.8, 0.9 under 20, 40, 60, 80% of the cap)");

        // ---- in play: a battle stim draws more creatures, a calming stim fewer; both are craftable stims ----
        int Count(StatusId? status)
        {
            var game = new GameSimulation(seed: 9640, width: 220, height: 70) { FamilySpawnsEnabled = false };
            foreach (var enemy in game.Enemies.ToArray())
                enemy.TakeDamage(1e6f);
            if (status is { } id)
                game.Player.Statuses.Apply(id, BuffCurve.Hours(30));
            var seen = new HashSet<long>(game.Enemies.Select(enemy => enemy.Id));
            var count = 0;
            for (var attempt = 0; attempt < 600; attempt++)
            {
                game.ProbeSpawnTick(0.65f);
                foreach (var enemy in game.Enemies.Where(enemy => seen.Add(enemy.Id)).ToArray())
                {
                    count++;
                    enemy.TakeDamage(1e6f);
                }
            }
            return count;
        }
        var baseline = Count(null);
        var battled = Count(StatusId.Battle);
        var calmed = Count(StatusId.Calm);
        var items = new ItemCatalog();
        var crafting = new CraftingSystem(items);
        check(battled > baseline * 1.4 && calmed < baseline * 0.8 && baseline > 0 &&
              items[ItemId.BattleStim].GrantsStatus == StatusId.Battle && items[ItemId.CalmingStim].GrantsStatus == StatusId.Calm &&
              crafting.FindByOutput(ItemId.BattleStim).Any() && crafting.FindByOutput(ItemId.CalmingStim).Any(),
            $"a battle stim draws more creatures and a calming stim fewer, and both are crafted at the chem lab " +
            $"(spawns in 390 s: plain {baseline}, battle {battled}, calming {calmed})");
    }
}
