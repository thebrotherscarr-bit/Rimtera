namespace Rimtera.Systems;

/// <summary>
/// Gives every pre-existing weapon a <see cref="WeaponStyle"/> and every pre-existing ammunition item an
/// <see cref="AmmoClass"/>, judged from its current definition (use kind, flags, name). Items that already
/// carry a style or ammo family (new arsenal content) are left alone.
/// </summary>
internal static class WeaponStyleAssignments
{
    public static void Apply(Dictionary<ItemId, ItemDefinition> items)
    {
        // Ammo first so weapons can inherit their ammo's family.
        foreach (var (id, item) in items.ToArray())
        {
            if (item.UseKind != ItemUseKind.Ammunition || item.AmmoKind != AmmoClass.None)
                continue;
            var kind = AmmoFamily(id, item.Name);
            if (kind != AmmoClass.None)
                items[id] = item with { AmmoKind = kind };
        }

        foreach (var (id, item) in items.ToArray())
        {
            if (item.Style != WeaponStyle.None)
                continue;
            var style = StyleFor(item);
            if (style == WeaponStyle.None)
                continue;
            var updated = item with { Style = style };
            if (item.UseKind == ItemUseKind.Ranged && item.UsesAmmo == AmmoClass.None)
            {
                var ammoFamily = item.AmmoType is { } ammo && items.TryGetValue(ammo, out var ammoItem) ? ammoItem.AmmoKind : AmmoClass.None;
                // Bows draw arrows when the player has them and fall back to their old ammunition otherwise.
                updated = updated with { UsesAmmo = style == WeaponStyle.Bow ? AmmoClass.Arrow : ammoFamily };
            }
            items[id] = updated;
        }
    }

    private static AmmoClass AmmoFamily(ItemId id, string name)
    {
        if (id == ItemId.Arrow)
            return AmmoClass.Cell; // "Energy cell": the original shared ammunition.
        var n = name.ToLowerInvariant();
        if (n.Contains("rocket") || n.Contains("explosive") || n.Contains("grenade") || n.Contains("missile"))
            return AmmoClass.Rocket;
        if (n.Contains("dart"))
            return AmmoClass.Dart;
        if (n.Contains("fuel") || n.Contains("canister"))
            return AmmoClass.Fuel;
        if (n.Contains("arrow") || n.Contains("bolt"))
            return AmmoClass.Arrow;
        if (n.Contains("round") || n.Contains("shell") || n.Contains("slug") || n.Contains("bullet"))
            return AmmoClass.Bullet;
        if (n.Contains("cell"))
            return AmmoClass.Cell;
        return AmmoClass.None; // Purifier gel and other special ammo keep their dedicated weapon.
    }

    private static bool Has(string name, params string[] words) => words.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static WeaponStyle StyleFor(ItemDefinition item)
    {
        var name = item.Name;
        switch (item.UseKind)
        {
            case ItemUseKind.Melee:
                if (item.SignalWhip || Has(name, "whip", "lash"))
                    return WeaponStyle.Whip;
                if (Has(name, "yo-yo", "yoyo"))
                    return WeaponStyle.Yoyo;
                if (item.Thrown && item.MaxStack > 1)
                    return WeaponStyle.Thrown;
                if (Has(name, "boomerang", "disc", "chakram") || item.Thrown)
                    return WeaponStyle.Boomerang;
                if (Has(name, "chainsaw"))
                    return WeaponStyle.Broadsword;
                if (Has(name, "flail", "chain", "morning star"))
                    return WeaponStyle.Flail;
                if (Has(name, "spear", "glaive", "trident", "pike", "lance", "halberd"))
                    return WeaponStyle.Spear;
                if (Has(name, "shiv", "scalpel", "rapier", "dagger", "knife", "stiletto"))
                    return WeaponStyle.Shortsword;
                return WeaponStyle.Broadsword;
            case ItemUseKind.Ranged:
                if (item.Thrown)
                    return WeaponStyle.Thrown;
                if (Has(name, "bow", "repeater"))
                    return WeaponStyle.Bow;
                if (Has(name, "launcher", "mortar", "rocket", "grenade", "bazooka"))
                    return WeaponStyle.Launcher;
                if (Has(name, "scatter", "shotgun", "blunderbuss") || item.ProjectileCount > 1)
                    return WeaponStyle.Shotgun;
                return WeaponStyle.Gun;
            case ItemUseKind.Tech:
                if (Has(name, "beam", "ray"))
                    return WeaponStyle.Beam;
                return item.ProjectileCount > 1 ? WeaponStyle.Tome : WeaponStyle.Staff;
            case ItemUseKind.Command:
                if (item.Summons is not { } kind)
                    return WeaponStyle.None;
                return MinionCatalog.Get(kind).IsSentry ? WeaponStyle.SentryStaff : WeaponStyle.SummonStaff;
            default:
                return WeaponStyle.None;
        }
    }
}
