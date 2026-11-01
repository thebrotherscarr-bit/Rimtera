using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Whether holding the use button starts the next use by itself (Terraria's <c>Item.autoReuse</c>). Items without it
/// need the button let go and pressed again. Rimtera items carry no per-item flag, so the rule follows what Terraria
/// gives each kind of item (counted over docs/terraria-evidence/full/tables/items.csv): placeables 3136 of 3231 auto,
/// broadswords 161 of 205, magic staves 48 of 58, launchers all; potions 0 of 145, shortswords, spears, boomerangs,
/// whips and summon staves mostly not; bows and guns only from the hardmode tiers. Channel items (drills, yo-yos,
/// flails, beams) keep going while held.
/// </summary>
public static class ItemUseRules
{
    /// <summary>
    /// Auto-reuse with auto-swing gear (Terraria's feral claws, <c>Player.autoReuseGlove</c>): every melee weapon and whip
    /// repeats while held when <paramref name="autoSwingActive"/>; otherwise the item's own rule.
    /// </summary>
    public static bool AutoReuse(ItemDefinition? item, bool autoSwingActive) =>
        AutoReuse(item) || autoSwingActive && item is not null &&
        (item.Style == WeaponStyle.Whip || item.EffectiveClass == DamageClass.Melee && item.UseKind == ItemUseKind.Melee);

    public static bool AutoReuse(ItemDefinition? item)
    {
        if (item is null)
            return false;
        if (ToolParity.IsTool(item))
        {
            var profile = ToolParity.Profile(item);
            return profile.AutoReuse || profile.Channel;
        }
        switch (item.Style)
        {
            case WeaponStyle.Broadsword or WeaponStyle.Staff or WeaponStyle.Tome or WeaponStyle.Launcher:
            case WeaponStyle.Yoyo or WeaponStyle.Flail or WeaponStyle.Beam:
                return true;
            case WeaponStyle.Shortsword or WeaponStyle.Spear or WeaponStyle.Boomerang or WeaponStyle.Whip or WeaponStyle.Thrown:
            case WeaponStyle.SummonStaff or WeaponStyle.SentryStaff:
                return false;
            case WeaponStyle.Bow or WeaponStyle.Gun:
                return item.Tier >= 4; // Viridium and up: Terraria's hardmode repeaters and automatic guns
            case WeaponStyle.Shotgun:
                return item.Tier >= 5;
        }
        return item.UseKind switch
        {
            ItemUseKind.PlaceTile or ItemUseKind.PlaceWall => true,
            ItemUseKind.Melee => true,
            ItemUseKind.Ranged => item.Tier >= 4,
            ItemUseKind.Tech => true,
            ItemUseKind.Food or ItemUseKind.Consumable or ItemUseKind.Equipment or ItemUseKind.Vanity or ItemUseKind.Command => false,
            // Wrench, wire cutter, buckets, paint tools, rope and the bug net all auto-reuse in Terraria.
            _ => ToolParity.Profile(item).AutoReuse
        };
    }
}

/// <summary>What a throw (Terraria's Throw trigger) did.</summary>
public enum ThrowOutcome : byte { Thrown, NothingHeld, Favorite, Busy }

/// <summary>
/// Controls-side game rules that follow Terraria's player input: the cursor (mouse) item, throwing, hotbar changes
/// that wait for the swing, and Shift auto-select.
/// </summary>
public sealed partial class GameSimulation
{
    private int? _bufferedHotbarSlot;

    /// <summary>
    /// True while an item is being used: a swing, a weapon animation or the use cooldown is running (Terraria's
    /// <c>itemAnimation</c> / <c>itemTime</c>). The held item cannot change and nothing can be thrown until it ends.
    /// </summary>
    public bool IsUsingItem => _toolSwingTicks > 0 || _swing is not null || Player.ActionCooldownTicks > 0;

    /// <summary>The slot the hotbar is on, or will move to once the current use ends.</summary>
    public int PendingHotbarSlot => _bufferedHotbarSlot ?? Player.Inventory.SelectedSlot;

    /// <summary>
    /// A hotbar change from the player (number keys, wheel, clicking the hotbar): applied now, or buffered until the
    /// current use ends, as Terraria's <c>SelectedItemState</c> does (<c>CanChangeSelectedItemImmediately</c>).
    /// </summary>
    public bool RequestHotbarSlot(int slot)
    {
        if ((uint)slot >= (uint)Player.Inventory.SlotCount)
            return false;
        if (IsUsingItem)
        {
            _bufferedHotbarSlot = slot;
            return true;
        }
        _bufferedHotbarSlot = null;
        return TrySelectInventorySlot(slot);
    }

    private void ApplyBufferedHotbarSlot()
    {
        if (_bufferedHotbarSlot is { } slot && !IsUsingItem)
        {
            _bufferedHotbarSlot = null;
            TrySelectInventorySlot(slot);
        }
    }

    /// <summary>
    /// Returns the cursor stack to the bag when the inventory closes (Terraria's <c>dropItemCheck</c>: no inventory and a
    /// mouse item → <c>GetOrDropItem</c>); what does not fit drops at the player's feet. Nothing is ever lost.
    /// </summary>
    public GroundItem? ReturnCursorItem()
    {
        var rest = Player.Inventory.ReturnCursor();
        if (rest.IsEmpty)
            return null;
        return SpawnGroundItem(rest.ItemId, rest.Count, PlayerCenter, rest.Calibration, pickupDelay: 1.5f);
    }

    /// <summary>Throws the cursor stack into the world, as right-clicking the world with a mouse item does in Terraria.</summary>
    public GroundItem? ThrowCursorItem()
    {
        var held = Player.Inventory.Cursor;
        if (held.IsEmpty)
            return null;
        Player.Inventory.SetCursor(default);
        var item = SpawnGroundItem(held.ItemId, held.Count, PlayerCenter + new Vector2(Player.Motion.Facing * 0.8f, -0.3f),
            held.Calibration, pickupDelay: 1.5f);
        if (item is not null)
            item.Velocity = new Vector2(Player.Motion.Facing * 4f, -2.5f);
        return item;
    }

    /// <summary>
    /// The Throw key: the cursor stack if there is one, otherwise the selected slot. As in Terraria's <c>dropItemCheck</c>
    /// it waits for the current use to end and never throws a favourite from the hotbar.
    /// </summary>
    public ThrowOutcome TryThrowHeld()
    {
        if (IsUsingItem)
            return ThrowOutcome.Busy;
        if (Player.Inventory.HoldingCursor)
            return ThrowCursorItem() is null ? ThrowOutcome.NothingHeld : ThrowOutcome.Thrown;
        var slot = Player.Inventory.SelectedSlot;
        var stack = Player.Inventory.Slots[slot];
        if (stack.IsEmpty)
            return ThrowOutcome.NothingHeld;
        if (stack.Favorite)
            return ThrowOutcome.Favorite;
        return DropInventorySlot(slot) is null ? ThrowOutcome.NothingHeld : ThrowOutcome.Thrown;
    }

    /// <summary>
    /// Shift auto-select (Terraria's <c>SmartSelectLookup</c> → <c>SmartSelect_GetToolStrategy</c> →
    /// <c>SmartSelect_PickToolForStrategy</c>): by the tile under the cursor, a demolisher for hearts and altars (the
    /// hammer tiles), a cutter for trees, cacti and logs (axe tiles), otherwise a drill, each only if the cell is in that
    /// tool's reach. Over empty air, out of reach or with no fitting tool it picks a light: a torch, or glow sticks
    /// while the player is in liquid. The first fitting item in all 50 slots wins. Null when nothing fits.
    /// </summary>
    public int? SmartSelectSlot(GridPoint cursor)
    {
        var inventory = Player.Inventory;
        if (World.Contains(cursor))
        {
            var tile = World.TileAt(cursor.X, cursor.Y);
            if (tile is not (TileId.Air or TileId.Bedrock) && Tiles.Contains(tile))
            {
                var kind = ToolParity.IsDemolisherTile(tile) ? ItemUseKind.Demolisher
                    : ToolParity.IsCutterTile(tile) ? ItemUseKind.Cutter
                    : ItemUseKind.Pickaxe;
                for (var slot = 0; slot < inventory.SlotCount; slot++)
                    if (ItemIn(slot) is { } tool && tool.UseKind == kind && ToolParity.InBox(ToolReachBox(tool), cursor.X, cursor.Y))
                        return slot;
            }
        }
        var wet = _bodyLiquid != LiquidKind.None;
        for (var slot = 0; slot < inventory.SlotCount; slot++)
            if (ItemIn(slot) is { } light && (wet ? light.Id == ItemId.GlowSticks : IsTorch(light)))
                return slot;
        return null;

        ItemDefinition? ItemIn(int slot) => inventory.Slots[slot] is { IsEmpty: false } stack && Items.Contains(stack.ItemId) ? Items[stack.ItemId] : null;
    }

    private static bool IsTorch(ItemDefinition item) =>
        item.UseKind == ItemUseKind.PlaceTile && item.PlacedTile is TileId.WallTorch or TileId.StandingTorch;
}
