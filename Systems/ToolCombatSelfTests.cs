using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Tools are melee weapons too (Terraria): every pick, axe and hammer has damage, and its swing hurts creatures.</summary>
public static class ToolCombatSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var catalog = new GameSimulation(seed: 9801, width: 80, height: 48);
        var tools = catalog.Items.All.Where(ToolCombat.IsTool).ToArray();
        var armed = tools.All(item => item.Damage > 0 && item.Knockback > 0f && item.EffectiveClass == DamageClass.Melee);
        var starters = new[] { ItemId.CopperPickaxe, ItemId.FerriteHatchet, ItemId.FerriteDemolisher }.Select(id => catalog.Items[id].Damage).ToArray();
        check(armed && starters.All(damage => damage is >= 4 and <= 10),
            $"every one of the {tools.Length} tools has melee damage and knockback; the copper-tier pick, hatchet and hammer hit for {string.Join("/", starters)}");

        var results = new List<string>();
        var allHurt = true;
        var oncePerSwing = true;
        foreach (var (tool, seed) in new[] { (ItemId.CopperPickaxe, 9802), (ItemId.FerriteHatchet, 9803), (ItemId.FerriteDemolisher, 9804) })
        {
            var game = new GameSimulation(seed: seed, width: 80, height: 48) { FamilySpawnsEnabled = false };
            foreach (var other in game.Enemies.ToArray())
                other.TakeDamage(1e6f);
            game.Player.Inventory.Add(tool, 1);
            game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(tool));
            var dummy = game.SpawnCreatureAt(EnemyKind.AstroBeast, game.Player.Motion.Position + new Vector2(1.1f, 0.2f));
            var before = dummy.HitPoints;
            var target = new GridPoint((int)(dummy.Motion.Position.X + 0.5f), (int)(dummy.Motion.Position.Y + 0.5f));
            var first = game.StrikeWithToolSwingForTests(target);
            var second = game.StrikeWithToolSwingForTests(target);
            allHurt &= first == 1 && dummy.HitPoints < before;
            oncePerSwing &= second == 0;
            results.Add($"{game.Items[tool].Name} {before:0}->{dummy.HitPoints:0}");
        }
        check(allHurt && oncePerSwing, $"a pick, hatchet and hammer swing each hurts a creature in the arc, once per swing ({string.Join(", ", results)})");

        // Through the normal use path: holding a hatchet at a creature standing on open ground still hurts it.
        var used = new GameSimulation(seed: 9805, width: 80, height: 48) { FamilySpawnsEnabled = false };
        foreach (var other in used.Enemies.ToArray())
            other.TakeDamage(1e6f);
        used.TrySelectInventorySlot(used.Player.Inventory.FindSlot(ItemId.FerriteHatchet));
        var victim = used.SpawnCreatureAt(EnemyKind.AstroBeast, used.Player.Motion.Position + new Vector2(1.2f, 0.2f));
        var full = victim.HitPoints;
        var aim = new GridPoint((int)(victim.Motion.Position.X + 0.5f), (int)(victim.Motion.Position.Y + 0.5f));
        for (var tick = 0; tick < 30; tick++)
            used.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, aim));
        check(victim.HitPoints < full || !victim.IsAlive, $"using the starting hatchet on a creature hurts it ({full:0} -> {victim.HitPoints:0})");
    }
}
