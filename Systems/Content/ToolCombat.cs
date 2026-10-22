namespace Rimtera.Systems;

/// <summary>
/// Tools are melee weapons too (Terraria: every pickaxe, axe and hammer has damage and knockback and swings a melee
/// hitbox). After all items are registered, each drill, cutter and demolisher gets at least its tier's tool damage and
/// knockback from <see cref="GearCurve"/>, and the melee class so melee gear boosts it.
/// </summary>
public static class ToolCombat
{
    public static bool IsTool(ItemDefinition item) => item.UseKind is ItemUseKind.Pickaxe or ItemUseKind.Cutter or ItemUseKind.Demolisher;

    public static void Apply(Dictionary<ItemId, ItemDefinition> items)
    {
        foreach (var (id, item) in items.ToArray())
        {
            if (!IsTool(item))
                continue;
            var tier = Math.Max(1, item.Tier);
            items[id] = item with
            {
                Damage = Math.Max(item.Damage, GearCurve.ToolDamage(item.UseKind, tier)),
                Knockback = item.Knockback > 0f ? item.Knockback : GearCurve.ToolKnockback(item.UseKind, tier, ToolParity.IsChannelTool(id)),
                Class = item.Class == DamageClass.None ? DamageClass.Melee : item.Class
            };
        }
    }
}
