using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>A resident's gear slots (0.4.0): a weapon, Terraria's three armour pieces and two accessories (append-only).</summary>
public enum ResidentGearSlot : byte { Weapon, Head, Body, Legs, Accessory, Accessory2 }

/// <summary>
/// Equipping people (0.4.0, docs/VISION.md §2: Terraria's gear, RimWorld's act of equipping). You hand a resident gear from
/// your bag: a weapon, armour and accessories, the same items you wear. Armour and accessories add defense (Terraria's rule:
/// a hit loses half the defense, at least 1 damage gets through) and life; a weapon makes them a guard of their area, and its
/// damage, reach and speed are how they fight, scaled by their Melee (close quarters) or Shooting (everything else). What
/// they wear is saved with them and drops where they fall.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>Unarmed reach and the reach of a resident's ranged weapon (tiles).</summary>
    public const float UnarmedReach = 1.2f, RangedGuardReach = 10f;

    /// <summary>The slot an item goes in on a resident, or null when it is not gear (tools, blocks, ammo, vanity).</summary>
    public ResidentGearSlot? ResidentSlotFor(ItemId item)
    {
        if (!Items.Contains(item))
            return null;
        var definition = Items[item];
        if (definition.IsVanity)
            return null;
        return definition.EquipmentSlot switch
        {
            EquipmentSlot.Head => ResidentGearSlot.Head,
            EquipmentSlot.Body => ResidentGearSlot.Body,
            EquipmentSlot.Legs => ResidentGearSlot.Legs,
            EquipmentSlot.Accessory or EquipmentSlot.Accessory2 or EquipmentSlot.Accessory3 or EquipmentSlot.Accessory4 or
                EquipmentSlot.Accessory5 or EquipmentSlot.Accessory6 => ResidentGearSlot.Accessory,
            _ => definition.Damage > 0 && definition.Style is not (WeaponStyle.None or WeaponStyle.SummonStaff or WeaponStyle.SentryStaff)
                ? ResidentGearSlot.Weapon : null
        };
    }

    /// <summary>
    /// Hands the item in your hand to a resident: it goes in its slot (an accessory in the first free one), and anything it
    /// replaces comes back to your bag. Returns what happened, for the housing list.
    /// </summary>
    public string EquipResident(TownNpc npc, ItemId? item = null)
    {
        ArgumentNullException.ThrowIfNull(npc);
        var held = item ?? (Player.Inventory.SelectedStack.IsEmpty ? null : Player.Inventory.SelectedStack.ItemId);
        if (npc.IsBound || !npc.IsAlive || !_townNpcs.Contains(npc))
            return $"{npc.Name} cannot take gear right now.";
        if (npc.Kind == TownNpcKind.StationAi || TownTrades.Idle(npc.Kind) && npc.Kind != TownNpcKind.TradeConsul)
            return $"{npc.Name} cannot wear or wield gear.";
        if (held is not { } gear)
            return "Hold the weapon, armour or accessory you want to hand over.";
        if (ResidentSlotFor(gear) is not { } kind)
            return $"{Items[gear].Name} is not something a resident can wear or wield.";
        if (Player.Inventory.Count(gear) <= 0 && Player.Inventory.SelectedStack.ItemId != gear)
            return $"You are not carrying {Items[gear].Name}.";
        var slot = kind == ResidentGearSlot.Accessory && npc.Gear.ContainsKey(ResidentGearSlot.Accessory) &&
                   !npc.Gear.ContainsKey(ResidentGearSlot.Accessory2) ? ResidentGearSlot.Accessory2 : kind;
        if (slot is ResidentGearSlot.Accessory or ResidentGearSlot.Accessory2 &&
            npc.Gear.Where(pair => pair.Key is ResidentGearSlot.Accessory or ResidentGearSlot.Accessory2 && pair.Key != slot).Any(pair => pair.Value == gear))
            return $"{npc.Name} already wears a {Items[gear].Name}.";
        if (!(item is null ? Player.Inventory.TryConsumeSelected() : Player.Inventory.TryConsume(gear)))
            return $"You are not carrying {Items[gear].Name}.";
        var replaced = npc.Gear.GetValueOrDefault(slot);
        npc.Gear[slot] = gear;
        if (replaced != default && npc.Gear.Count > 0)
            GiveBack(replaced, npc);
        RefreshResidentGear(npc);
        return replaced != default
            ? $"{npc.Name} takes the {Items[gear].Name} and hands back the {Items[replaced].Name}."
            : $"{npc.Name} takes the {Items[gear].Name}.";
    }

    /// <summary>Takes a resident's gear back into your bag (one slot, or all of it).</summary>
    public int UnequipResident(TownNpc npc, ResidentGearSlot? slot = null)
    {
        ArgumentNullException.ThrowIfNull(npc);
        var taken = 0;
        foreach (var key in npc.Gear.Keys.Where(key => slot is null || key == slot).ToArray())
        {
            GiveBack(npc.Gear[key], npc);
            npc.Gear.Remove(key);
            taken++;
        }
        RefreshResidentGear(npc);
        return taken;
    }

    private void GiveBack(ItemId item, TownNpc npc)
    {
        if (Player.Inventory.Add(item, 1) < 1)
            SpawnGroundItem(item, 1, npc.Motion.Position + npc.Motion.Size / 2f);
    }

    /// <summary>A resident's defense: the armour and accessories they wear (plus a set bonus when all three pieces match).</summary>
    public int ResidentDefense(TownNpc npc)
    {
        var defense = npc.Gear.Where(pair => pair.Key != ResidentGearSlot.Weapon).Sum(pair => Items[pair.Value].Defense);
        var armour = new[] { ResidentGearSlot.Head, ResidentGearSlot.Body, ResidentGearSlot.Legs }
            .Select(slot => npc.Gear.TryGetValue(slot, out var piece) ? Items[piece] : null).ToArray();
        if (armour.All(piece => piece?.ArmorSetId is { } set && set == armour[0]!.ArmorSetId))
            defense += armour.Max(piece => piece!.SetBonusDefense);
        return defense;
    }

    /// <summary>Damage a hit does to a resident after their defense: Terraria's half-defense rule, at least 1.</summary>
    public float ResidentDamageTaken(TownNpc npc, float amount) => Math.Max(1f, amount - ResidentDefense(npc) / 2f);

    /// <summary>Recomputes what gear gives: life from armour and accessories (hit points keep their share of the maximum).</summary>
    private void RefreshResidentGear(TownNpc npc)
    {
        var bonus = npc.Gear.Where(pair => pair.Key != ResidentGearSlot.Weapon).Sum(pair => Items[pair.Value].MaximumLifeBonus);
        var share = npc.MaximumHitPoints <= 0f ? 1f : npc.HitPoints / npc.MaximumHitPoints;
        npc.MaximumHitPoints = TownNpc.BaseHitPoints + bonus;
        if (!npc.IsArmed)
            npc.Post = null; // 0.4.1: no weapon, no post
        if (npc.IsAlive)
            npc.HitPoints = Math.Clamp(share * npc.MaximumHitPoints, 1f, npc.MaximumHitPoints);
        npc.RefreshWork();
    }

    /// <summary>How a resident fights: damage a blow or shot, reach (tiles) and seconds between attacks.</summary>
    public (float Damage, float Reach, float Seconds) ResidentAttack(TownNpc npc)
    {
        var stats = npc.Stats;
        if (!npc.Gear.TryGetValue(ResidentGearSlot.Weapon, out var weapon))
            return (GuardStrikeDamage(stats), UnarmedReach, 1f);
        var definition = Items[weapon];
        var melee = definition.Style is WeaponStyle.Broadsword or WeaponStyle.Shortsword or WeaponStyle.Spear or WeaponStyle.Flail or WeaponStyle.Whip;
        var skill = melee ? ColonistSkill.Melee : ColonistSkill.Shooting;
        var damage = definition.Damage * (0.75f + stats.Level(skill) * 0.025f) *
                     (melee ? stats.MeleeDamageMultiplier : stats.ShootingDamageMultiplier);
        var reach = melee ? Math.Max(UnarmedReach + 0.4f, definition.Reach > 0f ? definition.Reach : 1.6f) : RangedGuardReach;
        var seconds = Math.Max(0.25f, (definition.UseCooldownTicks > 0 ? definition.UseCooldownTicks : 30) / 60f);
        return (damage, reach, seconds);
    }

    /// <summary>A fallen resident's gear drops where they fell.</summary>
    private void DropResidentGear(TownNpc npc)
    {
        foreach (var item in npc.Gear.Values)
            SpawnGroundItem(item, 1, npc.Motion.Position + npc.Motion.Size / 2f);
        npc.Gear.Clear();
    }

    private ItemId?[]? CaptureResidentGear(TownNpc npc) => npc.Gear.Count == 0
        ? null
        : Enum.GetValues<ResidentGearSlot>().Select(slot => npc.Gear.TryGetValue(slot, out var item) ? item : (ItemId?)null).ToArray();

    private void RestoreResidentGear(TownNpc npc, ItemId?[]? saved)
    {
        npc.Gear.Clear();
        var slots = Enum.GetValues<ResidentGearSlot>();
        for (var index = 0; index < Math.Min(slots.Length, saved?.Length ?? 0); index++)
            if (saved![index] is { } item && ResidentSlotFor(item) is { } kind &&
                (kind == slots[index] || kind == ResidentGearSlot.Accessory && slots[index] == ResidentGearSlot.Accessory2))
                npc.Gear[slots[index]] = item;
        var hp = npc.HitPoints;
        RefreshResidentGear(npc);
        npc.HitPoints = Math.Clamp(hp, 1f, npc.MaximumHitPoints);
    }
}
