using System.Numerics;
using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// 0.2.0 Stream A (PC-15, BO-23, D-6): the boss curve read from the <c>--boss-balance</c> report (<see cref="BossBalance"/>)
/// and Terraria's difficulty scaling (Brutal = Expert ×2, Annihilation = Master ×3).
/// </summary>
public static class BalanceSelfTests
{
    public static int RunStandalone()
    {
        GameSimulation.FastClock = true;
        var passed = 0;
        var failed = 0;
        Run((condition, name) =>
        {
            if (condition)
                passed++;
            else
                failed++;
            Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
        });
        Console.WriteLine($"{passed} balance checks passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    public static void Run(Action<bool, string> check)
    {
        var classic = BossBalance.Rows(WorldDifficulty.Classic);
        var brutal = BossBalance.Rows(WorldDifficulty.Brutal);
        var annihilation = BossBalance.Rows(WorldDifficulty.Annihilation);

        // ---- A1: the report covers every boss and mini-boss, each against a kit of its point in progression ----
        var bosses = EnemyArchetypes.All.Count(archetype => archetype.IsBoss);
        check(bosses == 33 && BossBalance.MiniBosses.Count == 9 && classic.Count == bosses + 9 &&
              classic.Select(row => row.Kind).Distinct().Count() == classic.Count &&
              classic.All(row => row.Kit.Weapons.Count > 0 && row.Reference.Dps > 0f && row.Kit.Life >= 100f && row.Kit.PotionHeal > 0f),
            $"A1: --boss-balance covers all {bosses} bosses and {BossBalance.MiniBosses.Count} mini-bosses, each with a reference kit " +
            "(weapons, armour, a stim) from the crafting audit");
        var first = BossBalance.RowFor(EnemyKind.StoneSentinel);
        var final = BossBalance.RowFor(EnemyKind.AstroGod);
        var trio = BossBalance.RowFor(EnemyKind.CyberSwarmLeader);
        check(first.Reference.Weapon.Tier <= 3 && trio.Reference.Weapon.Tier is >= 4 and <= 6 && final.Reference.Weapon.Tier == 7 &&
              first.Kit.Defense < trio.Kit.Defense && trio.Kit.Defense < final.Kit.Defense &&
              first.Kit.PotionHeal < final.Kit.PotionHeal && classic.All(row => row.Reference.Weapon.Tier <= BossBalance.TierCeiling(row.Kit.Stage, CraftingAudit.Run())),
            $"A1: kits rise with progression ({first.Name}: {first.Reference.Label}, def {first.Kit.Defense}, {first.Kit.PotionName}; " +
            $"{trio.Name}: {trio.Reference.Label}, def {trio.Kit.Defense}; {final.Name}: {final.Reference.Label}, def {final.Kit.Defense}) " +
            "and never carry a drop above the crafted tier");
        check(MathF.Abs(BossBalance.Spearman([1f, 2f, 3f, 4f], [10f, 20f, 30f, 40f]) - 1f) < 1e-5f &&
              MathF.Abs(BossBalance.Spearman([1f, 2f, 3f, 4f], [4f, 3f, 2f, 1f]) + 1f) < 1e-5f &&
              MathF.Abs(BossBalance.Spearman([1f, 2f, 3f], [5f, 5f, 9f]) - 0.866f) < 0.01f,
            "A1: the Spearman rank correlation is +1 for a rising series, -1 for a falling one, and averages tied ranks");

        // ---- A2 (PC-15): hits to kill rise with progression; the final boss takes at least twice the first ----
        var rho = BossBalance.ProgressionCorrelation();
        check(rho >= 0.8f && EnemyArchetypes.BossProgression.Count >= 11,
            $"PC-15: hits to kill rise with progression (Spearman {rho:0.00} >= 0.8 across the {EnemyArchetypes.BossProgression.Count} progression bosses)");
        check(final.Hits >= 2 * first.Hits && final.Seconds >= 2f * first.Seconds,
            $"PC-15: the {final.Name} takes {final.Hits} hits and {final.Seconds:0} s, at least twice the {first.Name}'s {first.Hits} hits and {first.Seconds:0} s");
        var capped = classic.Where(row => !row.MiniBoss && row.Archetype.AttackDamage * DefenseBalance.EnemyHitScale(row.Archetype) >=
            GameSimulation.BossDamageCapFor(row.Archetype, WorldDifficulty.Classic)).Select(row => row.Name).ToArray();
        check(capped.Length == 0 && GameSimulation.BossDamageCapFor(final.Archetype, WorldDifficulty.Classic) == GameSimulation.BossDamageCap &&
              GameSimulation.BossDamageCapFor(first.Archetype, WorldDifficulty.Classic) < GameSimulation.BossDamageCapFor(trio.Archetype, WorldDifficulty.Classic) &&
              GameSimulation.BossDamageCapFor(final.Archetype, WorldDifficulty.Annihilation) == 3f * GameSimulation.BossDamageCap,
            $"PC-15: the boss damage ceiling is per tier and scales with difficulty; no Classic boss hit reaches it ({string.Join(", ", capped)})");

        // ---- A2 (D-6): Brutal is Expert (x2), Annihilation is Master (x3) ----
        var lifeRatios = classic.Zip(brutal, (c, b) => b.HitPoints / c.HitPoints).ToArray();
        var masterRatios = classic.Zip(annihilation, (c, a) => a.HitPoints / c.HitPoints).ToArray();
        check(lifeRatios.All(ratio => ratio is >= 1.9f and <= 2.1f) && masterRatios.All(ratio => ratio is >= 2.8f and <= 3.2f),
            $"D-6: boss life on Brutal is {lifeRatios.Min():0.00}-{lifeRatios.Max():0.00}x Classic and on Annihilation {masterRatios.Min():0.00}-{masterRatios.Max():0.00}x");
        var hitRatios = classic.Zip(brutal, (c, b) => b.RawHit / c.RawHit).ToArray();
        var masterHits = classic.Zip(annihilation, (c, a) => a.RawHit / c.RawHit).ToArray();
        check(hitRatios.All(ratio => ratio is >= 1.9f and <= 2.1f) && masterHits.All(ratio => ratio is >= 2.8f and <= 3.2f) &&
              DefenseBalance.DefenseFactor(WorldDifficulty.Classic) == 0.5f && DefenseBalance.DefenseFactor(WorldDifficulty.Brutal) == 0.75f &&
              DefenseBalance.DefenseFactor(WorldDifficulty.Annihilation) == 1f,
            "D-6: boss hits are x2 on Brutal and x3 on Annihilation (the ceiling scales with them); defense counts 50/75/100%");

        // In a live world: the same strike takes half as much of a Brutal creature's life, a third of an Annihilation one's.
        float Share(WorldDifficulty difficulty, EnemyKind kind)
        {
            var game = new GameSimulation(seed: 9601, width: 80, height: 48) { FamilySpawnsEnabled = false, Difficulty = difficulty };
            var creature = game.SpawnCreatureAt(kind, game.Player.Motion.Position + new Vector2(20f, -6f));
            var before = creature.HitPoints;
            game.ProbeStrike(creature, 30f, false, 0);
            return before - creature.HitPoints;
        }
        foreach (var kind in new[] { EnemyKind.RockGolem, EnemyKind.StoneSentinel })
        {
            var (c, b, a) = (Share(WorldDifficulty.Classic, kind), Share(WorldDifficulty.Brutal, kind), Share(WorldDifficulty.Annihilation, kind));
            check(c / b is >= 1.9f and <= 2.1f && c / a is >= 2.8f and <= 3.2f,
                $"D-6: the {EnemyArchetypes.Get(kind).DisplayName} takes {c:0} of a 30-point strike on Classic, {b:0} on Brutal, {a:0} on Annihilation");
        }
        var fractureAuthored = EnemyArchetypes.All.Where(archetype => CreatureStatTable.IsFractureAuthored(archetype.Kind)).ToArray();
        check(Enum.GetValues<WorldDifficulty>().All(difficulty => GameSimulation.HardmodeScale(difficulty) == GameSimulation.HardmodeScale(WorldDifficulty.Classic)) &&
              fractureAuthored.Length > 40 && fractureAuthored.All(archetype => !GameSimulation.HardmodeRescales(archetype)) &&
              EnemyArchetypes.All.Where(archetype => archetype.IsBoss).All(archetype => !GameSimulation.HardmodeRescales(archetype)),
            "D-6/D-18: the Fracture's rescale is the same on every difficulty and stays off for Fracture-authored creatures and bosses");

        // ---- A3 (BO-23): every boss and mini-boss in band on Classic ----
        var outOfBand = classic.Where(row => !row.InBand).Select(row => $"{row.Name} {row.Seconds:0}s/{row.SurvivalUnhealed:0}s").ToArray();
        check(outOfBand.Length == 0,
            $"BO-23: all {classic.Count} bosses and mini-bosses kill in {BossBalance.MinimumSeconds:0}-{BossBalance.MaximumSeconds:0} s with tier gear, " +
            $"survive {BossBalance.MinimumUnhealedSurvival:0}+ s unhealed and outlast the fight with stims and half the shots dodged ({string.Join("; ", outOfBand.Take(4))})");
        var spread = classic.Where(row => row.ProgressionIndex >= 0).OrderBy(row => row.ProgressionIndex).Select(row => row.Seconds).ToArray();
        check(spread.Zip(spread.Skip(1)).Count(pair => pair.Second < pair.First) <= 1 && spread[^1] / spread[0] >= 2f,
            $"BO-23: time to kill climbs the progression ({string.Join(" → ", spread.Select(seconds => seconds.ToString("0")))} s)");
    }
}
