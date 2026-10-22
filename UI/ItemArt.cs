using System.Drawing;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>
/// Art for items that have no sprite of their own: a base sprite chosen by weapon style, tool, armor slot
/// (by class), accessory kind or ammo family, recoloured toward a tier colour with a small per-item shift so
/// a tier's items still differ. Sheet or kitbashed art always wins when it exists.
/// </summary>
internal sealed class ItemArt
{
    private readonly SpriteAtlas _atlas;
    private readonly ItemCatalog _items = new();
    private readonly Dictionary<ItemId, string> _resolved = new();

    public ItemArt(SpriteAtlas atlas) => _atlas = atlas;

    /// <summary>Tier colours: scrap, alloy, ferrite, cobalt, glacial, pyrocite, viridium, lumenite, voidsteel, astral, meteorite, beyond.</summary>
    private static readonly Color[] TierColors =
    [
        Color.FromArgb(150, 140, 130), Color.FromArgb(205, 130, 80), Color.FromArgb(185, 190, 200), Color.FromArgb(70, 120, 230),
        Color.FromArgb(120, 220, 245), Color.FromArgb(240, 90, 50), Color.FromArgb(80, 210, 110), Color.FromArgb(250, 230, 120),
        Color.FromArgb(150, 80, 220), Color.FromArgb(240, 120, 210), Color.FromArgb(255, 160, 60), Color.FromArgb(120, 255, 230)
    ];

    public string Fallback(ItemId id, string named)
    {
        if (_resolved.TryGetValue(id, out var cached))
            return cached;
        var result = named;
        if (_atlas.Find(named) is null && _items.Contains(id) && BaseFor(_items[id]) is { } baseSprite && _atlas.Find(baseSprite) is { } found)
        {
            var item = _items[id];
            var tint = TierColors[Math.Clamp(item.Tier, 0, TierColors.Length - 1)];
            // A small hue nudge per item keeps same-tier items distinguishable.
            var nudge = ((int)id * 37 % 7 - 3) * 6;
            tint = Color.FromArgb(Math.Clamp(tint.R + nudge, 0, 255), Math.Clamp(tint.G - nudge, 0, 255), Math.Clamp(tint.B + nudge / 2, 0, 255));
            result = _atlas.Variant(found, $"tier{item.Tier}-{id}", color => Tint(color, tint));
        }
        _resolved[id] = result;
        return result;
    }

    private static Color Tint(Color source, Color tint)
    {
        if (source.A == 0)
            return source;
        var luminance = (source.R * 0.3f + source.G * 0.59f + source.B * 0.11f) / 255f;
        // Keep shading, pull the hue toward the tier colour; near-greys take more of it than saturated accents.
        var saturation = (Math.Max(source.R, Math.Max(source.G, source.B)) - Math.Min(source.R, Math.Min(source.G, source.B))) / 255f;
        var amount = 0.65f - saturation * 0.35f;
        int Mix(int from, int to) => Math.Clamp((int)(from * (1 - amount) + to * luminance * 1.5f * amount), 0, 255);
        return Color.FromArgb(source.A, Mix(source.R, tint.R), Mix(source.G, tint.G), Mix(source.B, tint.B));
    }

    private static string? BaseFor(ItemDefinition item)
    {
        var name = item.Id.ToString();
        switch (item.Style)
        {
            case WeaponStyle.Broadsword: return "weapon_lumenite_greatsword";
            case WeaponStyle.Shortsword: return "weapon_astral_blade";
            case WeaponStyle.Spear: return "weapon_shock_spear";
            case WeaponStyle.Yoyo: return "weapon_yoyo_drone";
            case WeaponStyle.Boomerang: return "weapon_return_boomerang";
            case WeaponStyle.Flail: return "weapon_chain_flail";
            case WeaponStyle.Whip: return "weapon_tether_whip";
            case WeaponStyle.Bow: return name.Contains("Cross", StringComparison.Ordinal) || name.Contains("Repeater", StringComparison.Ordinal)
                ? "weapon_glacial_crossbow" : "weapon_lumenite_longbow";
            case WeaponStyle.Gun: return item.Damage >= 30 || name.Contains("Rifle", StringComparison.Ordinal) ? "laser_rifle" : "plasma_pistol";
            case WeaponStyle.Shotgun: return "sniper_rifle";
            case WeaponStyle.Launcher: return name.Contains("Grenade", StringComparison.Ordinal) ? "grenade_launcher" : "rocket_launcher";
            case WeaponStyle.Thrown: return "weapon_throwing_disc";
            case WeaponStyle.Staff: return "tech_prism_lens_staff";
            case WeaponStyle.Tome: return "tech_datapad_flare";
            case WeaponStyle.Beam: return "tech_rad_beam_rod";
            case WeaponStyle.SummonStaff: return "cmd_hornet_drone_remote";
            case WeaponStyle.SentryStaff: return "cmd_turret_autogun";
        }
        return item.UseKind switch
        {
            ItemUseKind.Pickaxe => "tool_ferrite_drill",
            ItemUseKind.Cutter => "weapon_chainsaw_sword",
            ItemUseKind.Demolisher => "item_power_pickaxe",
            ItemUseKind.Melee => "weapon_lumenite_greatsword",
            ItemUseKind.Ranged => "plasma_pistol",
            ItemUseKind.Tech => "tech_prism_lens_staff",
            ItemUseKind.Command => "cmd_hornet_drone_remote",
            ItemUseKind.Ammunition => item.AmmoKind switch
            {
                AmmoClass.Bullet => "ammo_shotgun_shells",
                AmmoClass.Rocket => "ammo_mini_rocket",
                AmmoClass.Fuel => "ammo_fuel_canister",
                _ => "ammo_toxic_darts"
            },
            ItemUseKind.Equipment => item.EquipmentSlot switch
            {
                EquipmentSlot.Head => $"armor_astral_{ClassLine(item)}_helmet",
                EquipmentSlot.Body => $"armor_astral_{ClassLine(item)}_chest",
                EquipmentSlot.Legs => $"armor_astral_{ClassLine(item)}_legs",
                _ when name.Contains("Wing", StringComparison.Ordinal) => "item_glider_wings",
                _ when name.Contains("Boot", StringComparison.Ordinal) || name.Contains("Treads", StringComparison.Ordinal) => "scout_boots",
                _ when name.Contains("Ring", StringComparison.Ordinal) || name.Contains("Band", StringComparison.Ordinal) => "item_magnet_ring",
                _ when name.Contains("Shield", StringComparison.Ordinal) || name.Contains("Emitter", StringComparison.Ordinal) => "item_energy_shield_emitter",
                _ => "item_lucky_charm"
            },
            ItemUseKind.Consumable when item.GrantsStatus is not null => "item_med_stim",
            _ => null
        };
    }

    /// <summary>Armor art line by class: warden (close quarters), marksman (ballistics), sage (tech), handler (command).</summary>
    private static string ClassLine(ItemDefinition item) =>
        item.MeleeDamageBonus > 0f ? "warden"
        : item.RangedDamageBonus > 0f ? "marksman"
        : item.TechDamageBonus > 0f || item.ChargeRegenBonus > 0f ? "sage"
        : item.CommandDamageBonus > 0f || item.DroneSlotBonus > 0 || item.SentrySlotBonus > 0 ? "handler"
        : ((int)item.Id % 4) switch { 0 => "warden", 1 => "marksman", 2 => "sage", _ => "handler" };
}
