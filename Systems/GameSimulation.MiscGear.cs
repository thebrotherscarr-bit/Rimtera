namespace Rimtera.Systems;

/// <summary>Terraria's miscellaneous equipment slots: what each holds is in use as long as it is equipped.</summary>
public enum MiscSlot : byte { Pet, LightPet, Mount, Hook }

/// <summary>
/// The misc slots beside the armor (docs/UI_UX_REVIEW.md §2.3): a pet, a light pet, a mount and a grappling hook. An item
/// in its slot works as if carried: the pet follows, the mount key rides it, the grapple key throws it. Items still
/// work from the bag too.
/// </summary>
public sealed partial class GameSimulation
{
    private readonly ItemId?[] _misc = new ItemId?[4];

    public ItemId? MiscEquipped(MiscSlot slot) => _misc[(int)slot];

    /// <summary>True when <paramref name="item"/> belongs in <paramref name="slot"/>.</summary>
    public static bool FitsMisc(MiscSlot slot, ItemId item) => slot switch
    {
        MiscSlot.Pet => PetInfo(item) is { Light: false },
        MiscSlot.LightPet => PetInfo(item) is { Light: true },
        MiscSlot.Mount => TravelContent.MountForItem(item) is not null,
        _ => GrowthItems.HookFor(item) is not null
    };

    /// <summary>The misc slot an item belongs in, if any (for Shift-click equipping).</summary>
    public static MiscSlot? MiscSlotFor(ItemId item) =>
        Enum.GetValues<MiscSlot>().Cast<MiscSlot?>().FirstOrDefault(slot => FitsMisc(slot!.Value, item));

    /// <summary>
    /// Puts <paramref name="item"/> (or nothing) in a misc slot and returns what was there. Refuses an item that does
    /// not belong (returns false and changes nothing).
    /// </summary>
    public bool SetMisc(MiscSlot slot, ItemId? item, out ItemId? previous)
    {
        previous = _misc[(int)slot];
        if (item is { } id && !FitsMisc(slot, id))
            return false;
        _misc[(int)slot] = item;
        if (slot == MiscSlot.Mount && item is null && ActiveMountSpec is { } riding && !Carries(riding.Item))
            Dismount();
        return true;
    }

    /// <summary>
    /// Cancelling an equipped pet's buff takes it out of its slot and back into the bag (if there is room), so it stays
    /// away instead of being summoned again at once.
    /// </summary>
    private void UnequipMiscTo(MiscSlot slot, ItemId? item)
    {
        if (item is not { } id || _misc[(int)slot] != id || Player.Inventory.Add(id, 1) == 0)
            return;
        _misc[(int)slot] = null;
    }

    /// <summary>In the bag or in a misc slot.</summary>
    private bool Carries(ItemId item) => Player.Inventory.Count(item) > 0 || _misc.Contains(item);

    /// <summary>Keeps the equipped pet and light pet out (summoning them quietly when the slot is filled).</summary>
    private void UpdateMiscGear()
    {
        if (_misc[(int)MiscSlot.Pet] is { } pet && _pet?.SourceItem != pet)
            _pet = CreatePet(pet);
        if (_misc[(int)MiscSlot.LightPet] is { } light && _lightPet?.SourceItem != light)
            _lightPet = CreatePet(light);
    }

    private ItemId?[] CaptureMisc() => _misc.ToArray();

    private void RestoreMisc(ItemId?[]? saved)
    {
        Array.Clear(_misc);
        if (saved is null)
            return;
        for (var index = 0; index < Math.Min(saved.Length, _misc.Length); index++)
            if (saved[index] is { } id && Enum.IsDefined(id) && Items.Contains(id) && FitsMisc((MiscSlot)index, id))
                _misc[index] = id;
    }
}
