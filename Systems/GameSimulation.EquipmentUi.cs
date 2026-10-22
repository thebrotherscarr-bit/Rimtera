namespace Rimtera.Systems;

/// <summary>Equipment actions used by the inventory screen: equip straight from a grid slot.</summary>
public sealed partial class GameSimulation
{
    /// <summary>Equips the item in <paramref name="slot"/> (armor, accessory or vanity) without changing the selected hotbar slot.</summary>
    public bool TryEquipFromInventory(int slot)
    {
        if ((uint)slot >= (uint)Player.Inventory.SlotCount || Player.Inventory.Slots[slot].IsEmpty)
            return false;
        var item = Items[Player.Inventory.Slots[slot].ItemId];
        if (item.EquipmentSlot is null || item.UseKind is not (ItemUseKind.Equipment or ItemUseKind.Vanity))
            return false;
        var selected = Player.Inventory.SelectedSlot;
        var before = Player.Inventory.Slots[slot];
        Player.Inventory.Select(slot);
        EquipSelected(item.IsVanity);
        Player.Inventory.Select(selected);
        return Player.Inventory.Slots[slot] != before;
    }
}
