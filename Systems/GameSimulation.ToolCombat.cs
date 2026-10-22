using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Tool swings hit creatures (Terraria: a pickaxe, axe or hammer swing carries a melee hitbox, so tools damage whatever
/// they pass through while they work on tiles). Each use strikes every creature in the swing arc on the side the player
/// faces the target, once per swing, with the tool's melee damage, crit and knockback; the tile action happens as usual.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>Reach of a tool's swing arc from the player's centre (tiles): about the tool's length plus the arm.</summary>
    public const float ToolSwingRadius = 1.9f;

    /// <summary>Strikes the creatures in the tool's swing arc. Returns how many were hit.</summary>
    private int StrikeWithToolSwing(ItemDefinition item, GridPoint target)
    {
        if (!ToolCombat.IsTool(item) || item.Damage <= 0)
            return 0;
        var center = PlayerCenter;
        var toward = target.X + 0.5f - center.X;
        var facing = MathF.Abs(toward) > 0.05f ? MathF.Sign(toward) : Player.Motion.Facing >= 0 ? 1 : -1;
        // The arc runs from overhead to forward-low on the facing side: centre it a little ahead of and above the body.
        var arc = center + new Vector2(facing * 0.9f, -0.3f);
        var swingSeconds = Math.Max(4, item.UseCooldownTicks) / 60f;
        var calibration = SelectedCalibration;
        var hits = 0;
        foreach (var enemy in _enemies.Where(candidate => candidate.IsAlive && !candidate.IsPassive).ToArray())
        {
            if (EnemyHitDistance(enemy, arc) > ToolSwingRadius || !enemy.CanBeHitBy(-1))
                continue;
            LastHitCritical = false;
            var (rolled, critical) = RollUse(ComputeWeaponDamage(item, calibration), CritChanceFor(item, calibration));
            var damage = StrikeEnemy(enemy, rolled, critical, item.EffectiveClass);
            enemy.MarkHitBy(-1, swingSeconds * 0.8f); // once per swing
            OnMeleeHit(item, enemy, damage);
            var knockback = item.Knockback * CalibrationCatalog.Get(calibration).KnockbackMultiplier;
            var away = enemy.Motion.Position.X >= Player.Motion.Position.X ? 1f : -1f;
            KnockEnemy(enemy, new Vector2(away * knockback, -knockback * 0.2f), LastHitCritical);
            PostMessage(enemy.IsAlive ? $"Hit {enemy.DisplayName} for {damage:0} damage." : $"Defeated {enemy.DisplayName}.");
            hits++;
        }
        return hits;
    }

    /// <summary>Test hook: one tool swing at <paramref name="target"/> with the selected item.</summary>
    internal int StrikeWithToolSwingForTests(GridPoint target) =>
        Player.Inventory.SelectedItem is { } item ? StrikeWithToolSwing(item, target) : 0;
}
