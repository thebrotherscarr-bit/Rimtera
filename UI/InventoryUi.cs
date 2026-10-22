using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>Mouse-held-stack interaction logic shared by player and chest inventory grids.</summary>
public sealed class InventoryUiController
{
    public ItemStack CursorStack { get; private set; }

    public void LeftClick(Inventory inventory, int slot)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        Validate(inventory, slot);
        if (CursorStack.IsEmpty)
        {
            CursorStack = inventory.TakeFromSlot(slot);
            return;
        }

        var remainder = inventory.PlaceIntoSlot(slot, CursorStack);
        if (remainder.Count != CursorStack.Count)
        {
            CursorStack = remainder;
            return;
        }

        var held = CursorStack;
        inventory.SwapWithCursor(slot, ref held);
        CursorStack = held;
    }

    public void RightClick(Inventory inventory, int slot)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        Validate(inventory, slot);
        if (CursorStack.IsEmpty)
        {
            var available = inventory.Slots[slot];
            if (!available.IsEmpty)
                CursorStack = inventory.TakeFromSlot(slot, (available.Count + 1) / 2);
            return;
        }

        var one = new ItemStack(CursorStack.ItemId, 1);
        var remainder = inventory.PlaceIntoSlot(slot, one);
        if (remainder.IsEmpty)
            CursorStack = CursorStack with { Count = CursorStack.Count - 1 };
    }

    public int QuickTransfer(Inventory from, int fromSlot, Inventory to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if ((uint)fromSlot >= (uint)from.SlotCount)
            return 0;
        var source = from.Slots[fromSlot];
        if (source.IsEmpty)
            return 0;

        var moved = 0;
        for (var i = 0; i < to.SlotCount && !from.Slots[fromSlot].IsEmpty; i++)
        {
            var target = to.Slots[i];
            if (target.IsEmpty || target.ItemId == source.ItemId)
                moved += from.TransferSlotTo(to, fromSlot, i, allowSwap: false);
        }
        return moved;
    }

    public void DropCursor() => CursorStack = default;

    private static void Validate(Inventory inventory, int slot)
    {
        if ((uint)slot >= (uint)inventory.SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot));
    }
}

/// <summary>Inventory/hotbar/chest UI tree bound to the live inventory objects.</summary>
public sealed class InventoryUiState : UiState
{
    private readonly InventoryUiController _controller;
    private readonly List<UiItemSlotElement> _slots = new();
    private UiTextElement? _cursorLabel;
    private UiTextElement? _statusLabel;
    private readonly GameSimulation? _game;
    private readonly GridPoint? _chestCell;
    private readonly string _chestTitle;
    private SlotMode _mode;

    private enum SlotMode { Normal, Favorite, Trash }

    public InventoryUiState(Inventory playerInventory, InventoryUiController? controller = null, Inventory? chest = null,
        GameSimulation? game = null, GridPoint? chestCell = null, string chestTitle = "Chest")
        : base("inventory", style: new UiStyle { Width = UiLength.Pct(1f), Height = UiLength.Pct(1f) })
    {
        PlayerInventory = playerInventory ?? throw new ArgumentNullException(nameof(playerInventory));
        ChestInventory = chest;
        _controller = controller ?? new InventoryUiController();
        _game = game;
        _chestCell = chestCell;
        _chestTitle = chestTitle;
        Build();
    }

    /// <summary>Raised by the "Cargo pod" button; the host reopens the screen with the bank as the container.</summary>
    public event Action? BankRequested;

    /// <summary>Raised by the "Settings" button (Terraria keeps settings in the inventory, which Escape opens).</summary>
    public event Action? SettingsRequested;

    public Inventory PlayerInventory { get; }
    public Inventory? ChestInventory { get; }
    public InventoryUiController Controller => _controller;
    public IReadOnlyList<UiItemSlotElement> Slots => _slots;

    public void SelectHotbarSlot(int index) => PlayerInventory.Select(index);

    public int QuickMovePlayerSlotToChest(int slot) => ChestInventory is null ? 0 : _controller.QuickTransfer(PlayerInventory, slot, ChestInventory);
    public int QuickMoveChestSlotToPlayer(int slot) => ChestInventory is null ? 0 : _controller.QuickTransfer(ChestInventory, slot, PlayerInventory);

    private void Build()
    {
        var panel = new UiPanelElement("inventory-panel", new UiStyle
        {
            Width = UiLength.Pct(0.9f), Height = UiLength.Pct(0.82f), HAlign = 0.5f, VAlign = 0.5f,
            MaxWidth = 1000f, MaxHeight = 680f, Padding = UiThickness.Uniform(18f)
        });
        Add(panel);
        panel.Add(new UiTextElement(ChestInventory is null ? "Inventory" : $"Inventory  •  {_chestTitle}", "inventory-title",
            new UiStyle { Width = UiLength.Pct(1f), Height = UiLength.Px(36f) }) { Scale = 1.1f });

        AddGrid(panel, PlayerInventory, top: 52f, columns: 8, rowsLimit: int.MaxValue, prefix: "player");
        if (ChestInventory is not null)
            AddGrid(panel, ChestInventory, top: 52f + GridHeight(PlayerInventory.SlotCount, 8) + 64f,
                columns: 8, rowsLimit: int.MaxValue, prefix: "chest");
        _cursorLabel = new UiTextElement(string.Empty, "inventory-cursor", new UiStyle
        {
            Width = UiLength.Pct(1f), Height = UiLength.Px(32f), Top = UiLength.Mixed(-36f, 1f)
        }) { Scale = 0.75f };
        panel.Add(_cursorLabel);
        if (_game is not null)
        {
            AddActions(panel, _game);
            AddEquipment(panel, _game);
        }
    }

    /// <summary>
    /// Armor and five accessory slots, with vanity beside them. Clicking a worn item takes it off;
    /// right-clicking an equippable item in the grid puts it on.
    /// </summary>
    private void AddEquipment(UiPanelElement panel, GameSimulation game)
    {
        (EquipmentSlot Slot, string Label)[] functional =
        [
            (EquipmentSlot.Head, "HEAD"), (EquipmentSlot.Body, "BODY"), (EquipmentSlot.Legs, "LEGS"),
            (EquipmentSlot.Accessory, "ACC 1"), (EquipmentSlot.Accessory2, "ACC 2"), (EquipmentSlot.Accessory3, "ACC 3"),
            (EquipmentSlot.Accessory4, "ACC 4"), (EquipmentSlot.Accessory5, "ACC 5"),
            // The sixth slot opens with the auxiliary socket or on Annihilation worlds.
            (EquipmentSlot.Accessory6, game.AccessorySlot6Unlocked ? "ACC 6" : "LOCKED")
        ];
        void Add(EquipmentSlot slot, string label, bool vanity, float left, int row)
        {
            var element = new UiEquipmentSlotElement($"equip-{(vanity ? "vanity" : "gear")}-{slot}", game.Player.Equipment, slot, vanity, label, new UiStyle
            {
                Left = UiLength.Px(left), Top = UiLength.Px(52f + row * 50f), Width = UiLength.Px(52f), Height = UiLength.Px(44f)
            });
            element.OnClick(_ =>
            {
                if (element.Item is { } worn)
                    Status(game.TryUnequipEquipment(slot, vanity) ? $"Took off {game.Items[worn].Name}." : "No room in the inventory.");
            });
            panel.Add(element);
        }
        for (var row = 0; row < functional.Length; row++)
            Add(functional[row].Slot, functional[row].Label, vanity: false, 846f, row);
        Add(EquipmentSlot.Head, "VAN H", vanity: true, 902f, 0);
        Add(EquipmentSlot.Body, "VAN B", vanity: true, 902f, 1);
        Add(EquipmentSlot.Legs, "VAN L", vanity: true, 902f, 2);
    }

    /// <summary>Sort, quick stack, favorites, trash, loadouts, and loot/deposit for the open container.</summary>
    private void AddActions(UiPanelElement panel, GameSimulation game)
    {
        var top = 52f;
        void Button(string text, Action command)
        {
            var button = new UiButtonElement(text, () => { command(); RefreshCursorLabel(); }, $"inventory-action-{top}", new UiStyle
            {
                Left = UiLength.Px(632f), Top = UiLength.Px(top), Width = UiLength.Px(200f), Height = UiLength.Px(36f)
            });
            panel.Add(button);
            top += 42f;
        }

        Button("Sort inventory", () => { game.SortInventory(); Status("Sorted (hotbar and favorites stay put)."); });
        Button("Quick stack to nearby", () => Status($"Stacked {game.QuickStackToNearbyChests()} items into nearby containers."));
        Button("Favorite mode", () => { _mode = _mode == SlotMode.Favorite ? SlotMode.Normal : SlotMode.Favorite; Status(ModeText()); });
        Button("Trash mode", () => { _mode = _mode == SlotMode.Trash ? SlotMode.Normal : SlotMode.Trash; Status(ModeText()); });
        Button("Recover trash", () => Status(game.RecoverTrash() > 0 ? "Recovered the trashed stack." : "The trash is empty."));
        if (ChestInventory is not null)
        {
            Button("Loot all", () => Status($"Took {game.LootAll(_chestCell)} items."));
            Button("Deposit all", () => Status($"Stored {game.DepositAll(_chestCell)} items."));
        }
        else if (game.CanAccessBank)
            Button("Open cargo pod", () => BankRequested?.Invoke());
        Button("Settings", () => SettingsRequested?.Invoke());
        for (var i = 0; i < GameSimulation.LoadoutCount; i++)
        {
            var index = i;
            Button($"Loadout {i + 1}{(game.ActiveLoadout == i ? "  (worn)" : string.Empty)}",
                () => Status(game.SwitchLoadout(index) ? $"Loadout {index + 1} equipped." : "Already wearing that loadout."));
        }
        _statusLabel = new UiTextElement(ModeText(), "inventory-status", new UiStyle
        {
            Left = UiLength.Px(632f), Top = UiLength.Px(top + 4f), Width = UiLength.Px(200f), Height = UiLength.Px(48f)
        }) { Scale = 0.7f };
        panel.Add(_statusLabel);
    }

    private string ModeText() => _mode switch
    {
        SlotMode.Favorite => "Click slots to mark favorites (kept by sort and deposit).",
        SlotMode.Trash => "Click a slot to trash it.",
        _ => _game is { TrashSlot.IsEmpty: false } game ? $"Trash: {game.Items[game.TrashSlot.ItemId].Name}" : "Trash: empty"
    };

    private void Status(string text)
    {
        if (_statusLabel is not null)
            _statusLabel.Text = text;
    }

    private void AddGrid(UiPanelElement panel, Inventory inventory, float top, int columns, int rowsLimit, string prefix)
    {
        // Widen before multiplying: an unbounded row limit (int.MaxValue) used to overflow to a negative count.
        var count = (int)Math.Min(inventory.SlotCount, (long)columns * rowsLimit);
        for (var slot = 0; slot < count; slot++)
        {
            var slotIndex = slot;
            var column = slotIndex % columns;
            var row = slotIndex / columns;
            var element = new UiItemSlotElement($"{prefix}-slot-{slotIndex}", inventory, slotIndex, new UiStyle
            {
                Left = UiLength.Px(column * 76f), Top = UiLength.Px(top + row * 54f),
                Width = UiLength.Px(70f), Height = UiLength.Px(46f), Padding = UiThickness.Uniform(3f)
            });
            element.OnClick(input =>
            {
                if (_game is not null && _mode != SlotMode.Normal && ReferenceEquals(inventory, PlayerInventory))
                {
                    if (_mode == SlotMode.Favorite)
                        _game.ToggleFavorite(slotIndex);
                    else
                        Status(_game.TrashInventorySlot(slotIndex) ? ModeText() + "  ·  " + "Trashed." : "Can't trash that (empty or favorite).");
                    return;
                }
                if (input.IsDoubleClick && ChestInventory is not null)
                {
                    if (ReferenceEquals(inventory, PlayerInventory)) _controller.QuickTransfer(PlayerInventory, slotIndex, ChestInventory);
                    else _controller.QuickTransfer(ChestInventory, slotIndex, PlayerInventory);
                    RefreshCursorLabel();
                    return;
                }
                // Right-clicking armor or an accessory in the player grid wears it (Terraria-style quick equip).
                if (input.Button != UiPointerButton.Left && _game is not null && ReferenceEquals(inventory, PlayerInventory) &&
                    _controller.CursorStack.IsEmpty && !inventory.Slots[slotIndex].IsEmpty &&
                    _game.Items[inventory.Slots[slotIndex].ItemId].EquipmentSlot is not null)
                {
                    var name = _game.Items[inventory.Slots[slotIndex].ItemId].Name;
                    Status(_game.TryEquipFromInventory(slotIndex) ? $"Equipped {name}." : $"Cannot equip {name}.");
                    return;
                }
                if (input.Button == UiPointerButton.Left) _controller.LeftClick(inventory, slotIndex);
                else _controller.RightClick(inventory, slotIndex);
                RefreshCursorLabel();
            });
            element.OnActivate(_ => { _controller.LeftClick(inventory, slotIndex); RefreshCursorLabel(); });
            _slots.Add(element);
            panel.Add(element);
        }
    }

    private void RefreshCursorLabel()
    {
        if (_cursorLabel is not null)
            _cursorLabel.Text = _controller.CursorStack.IsEmpty
                ? string.Empty
                : $"Holding {(_game is null ? _controller.CursorStack.ItemId.ToString() : _game.Items[_controller.CursorStack.ItemId].Name)} × {_controller.CursorStack.Count}";
    }

    private static float GridHeight(int count, int columns) => ((count + columns - 1) / columns) * 54f;
}

/// <summary>
/// Retained text field. The host forwards typed characters and edit commands;
/// the field owns the value, caret, and maximum length so every frontend uses
/// the same in-game widget instead of a native dialog.
/// </summary>
public sealed class UiTextInputElement : UiElement
{
    public string Label { get; set; }
    public string Value { get; private set; }
    public string Placeholder { get; set; } = string.Empty;
    public int MaxLength { get; set; } = 32;
    public int CaretIndex { get; private set; }
    public UiColor Color { get; set; } = new(24, 28, 42);

    public UiTextInputElement(string label, string value = "", string? id = null, UiStyle? style = null) : base(id, style)
    {
        SemanticRole = "input";
        Label = label;
        Value = value ?? string.Empty;
        CaretIndex = Value.Length;
    }

    public bool Insert(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var remaining = Math.Max(0, MaxLength - Value.Length);
        if (remaining == 0)
            return false;

        var accepted = new List<char>(Math.Min(remaining, text.Length));
        foreach (var character in text)
        {
            if (char.IsControl(character))
                continue;
            accepted.Add(character);
            if (accepted.Count == remaining)
                break;
        }
        if (accepted.Count == 0)
            return false;

        var inserted = new string(accepted.ToArray());
        Value = Value.Insert(Math.Clamp(CaretIndex, 0, Value.Length), inserted);
        CaretIndex = Math.Clamp(CaretIndex + inserted.Length, 0, Value.Length);
        return true;
    }

    public bool Backspace()
    {
        if (CaretIndex <= 0)
            return false;
        var index = Math.Clamp(CaretIndex, 0, Value.Length) - 1;
        Value = Value.Remove(index, 1);
        CaretIndex = index;
        return true;
    }

    public bool Delete()
    {
        if (CaretIndex < 0 || CaretIndex >= Value.Length)
            return false;
        Value = Value.Remove(CaretIndex, 1);
        return true;
    }

    public void MoveCaret(int delta)
    {
        CaretIndex = Math.Clamp(CaretIndex + delta, 0, Value.Length);
    }

    public void MoveCaretToStart() => CaretIndex = 0;
    public void MoveCaretToEnd() => CaretIndex = Value.Length;

    public void SetValue(string? value)
    {
        Value = value ?? string.Empty;
        CaretIndex = Value.Length;
    }

    public string GetDisplayText()
    {
        if (!IsFocused)
            return string.IsNullOrEmpty(Value) ? Placeholder : Value;
        var caret = Math.Clamp(CaretIndex, 0, Value.Length);
        return Value.Insert(caret, "|");
    }
}
