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

    /// <summary>Puts a stack on the empty cursor (taking an item back out of the trash).</summary>
    public void PickUp(ItemStack stack)
    {
        if (CursorStack.IsEmpty)
            CursorStack = stack;
    }

    private static void Validate(Inventory inventory, int slot)
    {
        if ((uint)slot >= (uint)inventory.SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot));
    }
}

/// <summary>
/// Screen positions of the inventory screen, shared with the canvas, which draws the crafting list in the space
/// they leave. Terraria 1.4's arrangement: the grid at the top left with the hotbar as its first row, an open
/// chest under it, crafting under that, equipment down the right side.
/// </summary>
public static class InventoryLayout
{
    public const float Left = 20f;
    public const float Top = 22f;
    public const float Slot = 46f;
    public const float Stride = 50f;
    public const int Columns = 10;
    public const float SideColumn = Left + Columns * Stride + 10f;
    public const float SideWidth = 118f;

    public static float GridHeight(int slots) => (slots + Columns - 1) / Columns * Stride;
    public static float HintTop(int playerSlots) => Top + GridHeight(playerSlots);
    public static float ChestTop(int playerSlots) => HintTop(playerSlots) + 30f;
    public static float CraftingTop(int playerSlots, int chestSlots) =>
        (chestSlots > 0 ? ChestTop(playerSlots) + 24f + GridHeight(chestSlots) : HintTop(playerSlots) + 24f) + 34f;
}

/// <summary>
/// The inventory screen, the hub Escape opens as in Terraria: inventory and hotbar, sort / quick stack / trash,
/// an open chest with Loot all, Deposit all, Quick stack, Restock and Sort, crafting (drawn by the canvas), worn
/// gear with loadouts, tabs for housing, crew, bestiary and feats, and the Settings button. Clicks follow
/// Terraria: left picks up and places, right takes half or equips, Shift moves to the open chest (or equips, or
/// trashes), Ctrl trashes, Alt marks a favorite.
/// </summary>
public sealed class InventoryUiState : UiState
{
    private readonly InventoryUiController _controller;
    private readonly List<UiItemSlotElement> _slots = new();
    private UiTextElement? _cursorLabel;
    private UiTextElement? _statusLabel;
    private readonly GameSimulation? _game;
    private readonly GridPoint? _chestCell;
    private readonly string _chestTitle;
    private Tab _tab;
    private string? _housingReport;

    private enum Tab { Gear, Housing }

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

    /// <summary>Raised by the Crew, Bestiary and Feats tabs, which open their own screens.</summary>
    public event Action? CrewRequested;
    public event Action? BestiaryRequested;
    public event Action? FeatsRequested;

    /// <summary>Which modifier keys are down, read at each click (the host supplies it; none without one).</summary>
    public Func<(bool Shift, bool Ctrl, bool Alt)> Modifiers { get; set; } = () => default;

    public Inventory PlayerInventory { get; }
    public Inventory? ChestInventory { get; }
    public InventoryUiController Controller => _controller;
    public IReadOnlyList<UiItemSlotElement> Slots => _slots;
    public string StatusText => _statusLabel?.Text ?? string.Empty;

    /// <summary>The click hints shown under the grid until an action reports something.</summary>
    public const string ClickHints = "Shift-click  move     Ctrl-click  trash     Alt-click  favorite     Right-click  equip";

    public void SelectHotbarSlot(int index) => PlayerInventory.Select(index);

    public int QuickMovePlayerSlotToChest(int slot) => ChestInventory is null ? 0 : _controller.QuickTransfer(PlayerInventory, slot, ChestInventory);
    public int QuickMoveChestSlotToPlayer(int slot) => ChestInventory is null ? 0 : _controller.QuickTransfer(ChestInventory, slot, PlayerInventory);

    /// <summary>A click on a player-grid slot with the given modifiers, as the screen handles it (for hosts and tests).</summary>
    public void ClickPlayerSlot(int slot, UiPointerButton button = UiPointerButton.Left, bool shift = false, bool ctrl = false, bool alt = false) =>
        HandleSlotClick(PlayerInventory, slot, button, shift, ctrl, alt, doubleClick: false);

    private void Build()
    {
        _slots.Clear();
        ClearChildren();
        var playerSlots = PlayerInventory.SlotCount;
        Add(new UiTextElement("Inventory", "inventory-title", new UiStyle
        {
            Left = UiLength.Px(InventoryLayout.Left), Top = UiLength.Px(0f), Width = UiLength.Px(200f), Height = UiLength.Px(InventoryLayout.Top)
        }) { Scale = 0.8f });
        AddGrid(PlayerInventory, InventoryLayout.Top, "player");
        _statusLabel = new UiTextElement(ClickHints, "inventory-status", new UiStyle
        {
            Left = UiLength.Px(InventoryLayout.Left - 10f), Top = UiLength.Px(InventoryLayout.HintTop(playerSlots)),
            Width = UiLength.Px(InventoryLayout.Columns * InventoryLayout.Stride + 10f), Height = UiLength.Px(18f)
        }) { Scale = 0.7f, Color = new UiColor(170, 178, 192) };
        Add(_statusLabel);
        if (ChestInventory is not null)
        {
            var chestTop = InventoryLayout.ChestTop(playerSlots);
            Add(new UiTextElement(_chestTitle, "chest-title", new UiStyle
            {
                Left = UiLength.Px(InventoryLayout.Left - 10f), Top = UiLength.Px(chestTop), Width = UiLength.Px(300f), Height = UiLength.Px(22f)
            }) { Scale = 0.8f, Color = new UiColor(242, 176, 64) });
            AddGrid(ChestInventory, chestTop + 24f, "chest");
        }
        _cursorLabel = new UiTextElement(string.Empty, "inventory-cursor", new UiStyle
        {
            Left = UiLength.Px(InventoryLayout.SideColumn + InventoryLayout.SideWidth + 12f), Top = UiLength.Px(InventoryLayout.Top),
            Width = UiLength.Px(260f), Height = UiLength.Px(24f)
        }) { Scale = 0.75f };
        Add(_cursorLabel);
        if (_game is null)
            return;
        AddSideColumn(_game);
        AddChestActions(_game);
        AddCraftingHeader(_game);
        AddRightSide(_game);
        Add(new UiButtonElement("Settings", () => SettingsRequested?.Invoke(), "inventory-settings", new UiStyle
        {
            Left = UiLength.Mixed(-132f, 1f), Top = UiLength.Mixed(-46f, 1f), Width = UiLength.Px(112f), Height = UiLength.Px(32f)
        }));
        RefreshCursorLabel();
    }

    private UiButtonElement Button(string id, string text, float left, float top, float width, Action command, float height = 28f)
    {
        var button = new UiButtonElement(text, () => { command(); RefreshCursorLabel(); }, id, new UiStyle
        {
            Left = UiLength.Px(left), Top = UiLength.Px(top), Width = UiLength.Px(width), Height = UiLength.Px(height)
        }) { TextScale = 0.75f };
        Add(button);
        return button;
    }

    /// <summary>Beside the grid: sort, quick stack to nearby chests, the cargo pod, the wire-layer builder toggle and the trash slot.</summary>
    private void AddSideColumn(GameSimulation game)
    {
        var x = InventoryLayout.SideColumn;
        var top = InventoryLayout.Top;
        Button("inventory-sort", "Sort", x, top, InventoryLayout.SideWidth, () => { game.SortInventory(); Status("Sorted (hotbar and favorites stay put)."); });
        Button("inventory-quick-stack", "Quick stack", x, top + 32f, InventoryLayout.SideWidth,
            () => Status($"Quick-stacked {game.QuickStackToNearbyChests()} items into nearby chests."));
        if (ChestInventory is null && game.CanAccessBank)
            Button("inventory-bank", "Cargo pod", x, top + 64f, InventoryLayout.SideWidth, () => BankRequested?.Invoke());
        UiButtonElement? wires = null;
        wires = Button("inventory-wires", $"Wires: layer {game.ActiveWireLayer}", x, top + 96f, InventoryLayout.SideWidth, () =>
        {
            game.CycleWireLayer();
            wires!.Text = $"Wires: layer {game.ActiveWireLayer}";
            Status($"Showing and placing wire layer {game.ActiveWireLayer}.");
        });

        var trash = new UiStackSlotElement("inventory-trash", "Trash", () => game.TrashSlot, new UiStyle
        {
            Left = UiLength.Px(x), Top = UiLength.Px(InventoryLayout.HintTop(PlayerInventory.SlotCount) - InventoryLayout.Stride),
            Width = UiLength.Px(InventoryLayout.Slot), Height = UiLength.Px(InventoryLayout.Slot)
        });
        // Terraria's trash: drop a held stack in (destroying what was there); click it empty-handed to take it back.
        trash.OnClick(_ =>
        {
            if (!_controller.CursorStack.IsEmpty)
            {
                game.PutInTrash(_controller.CursorStack);
                _controller.DropCursor();
                Status("Trashed.");
            }
            else if (!game.TrashSlot.IsEmpty)
                _controller.PickUp(game.TakeTrash());
            RefreshCursorLabel();
        });
        Add(trash);
    }

    /// <summary>Terraria's chest links: Loot all, Deposit all, Quick stack, Restock, Sort.</summary>
    private void AddChestActions(GameSimulation game)
    {
        if (ChestInventory is null)
            return;
        var x = InventoryLayout.SideColumn;
        var top = InventoryLayout.ChestTop(PlayerInventory.SlotCount) + 24f;
        Button("chest-loot-all", "Loot all", x, top, InventoryLayout.SideWidth, () => Status($"Took {game.LootAll(_chestCell)} items."));
        Button("chest-deposit-all", "Deposit all", x, top + 30f, InventoryLayout.SideWidth, () => Status($"Stored {game.DepositAll(_chestCell)} items."));
        Button("chest-quick-stack", "Quick stack", x, top + 60f, InventoryLayout.SideWidth, () => Status($"Stacked {game.QuickStackInto(_chestCell)} items here."));
        Button("chest-restock", "Restock", x, top + 90f, InventoryLayout.SideWidth, () => Status($"Restocked {game.Restock(_chestCell)} items."));
        Button("chest-sort", "Sort", x, top + 120f, InventoryLayout.SideWidth, () => Status(game.SortContainer(_chestCell) ? "Sorted the container." : "Nothing to sort."));
    }

    /// <summary>The crafting heading and the station AI, which (like Terraria's Guide) tells you what your materials make.</summary>
    private void AddCraftingHeader(GameSimulation game)
    {
        var top = InventoryLayout.CraftingTop(PlayerInventory.SlotCount, ChestInventory?.SlotCount ?? 0) - 30f;
        Add(new UiTextElement("Crafting", "crafting-title", new UiStyle
        {
            Left = UiLength.Px(InventoryLayout.Left - 10f), Top = UiLength.Px(top), Width = UiLength.Px(140f), Height = UiLength.Px(26f)
        }) { Scale = 0.8f, Color = new UiColor(242, 176, 64) });
        Button("crafting-guide", "Ask station AI", InventoryLayout.Left + 130f, top, 140f, () =>
        {
            var advice = game.ConsultGuide();
            Status(advice.Count > 0 ? advice[0] : "The station AI has nothing new to suggest.");
        }, height: 26f);
    }

    /// <summary>Right side: loadouts, worn gear (or the housing panel) and the tabs.</summary>
    private void AddRightSide(GameSimulation game)
    {
        const float gearTop = 206f;
        UiLength FromRight(float pixels) => UiLength.Mixed(-pixels, 1f);
        void Tab(string id, string text, int row, Action command, bool active = false) =>
            Add(new UiButtonElement(text, () => { command(); RefreshCursorLabel(); }, id, new UiStyle
            {
                Left = FromRight(258f), Top = UiLength.Px(gearTop + row * 32f), Width = UiLength.Px(116f), Height = UiLength.Px(28f)
            }) { TextScale = 0.75f, Color = active ? UiColor.ButtonActive : UiColor.ButtonDefault });

        Tab("tab-gear", "Gear", 0, () => SwitchTab(InventoryUiState.Tab.Gear), _tab == InventoryUiState.Tab.Gear);
        Tab("tab-housing", "Housing", 1, () => SwitchTab(InventoryUiState.Tab.Housing), _tab == InventoryUiState.Tab.Housing);
        Tab("tab-crew", "Crew", 2, () => CrewRequested?.Invoke());
        Tab("tab-bestiary", "Bestiary", 3, () => BestiaryRequested?.Invoke());
        Tab("tab-feats", "Feats", 4, () => FeatsRequested?.Invoke());
        for (var i = 0; i < GameSimulation.LoadoutCount; i++)
        {
            var index = i;
            Add(new UiButtonElement($"Loadout {i + 1}", () =>
            {
                Status(game.SwitchLoadout(index) ? $"Loadout {index + 1} equipped." : "Already wearing that loadout.");
                Rebuild();
            }, $"loadout-{i + 1}", new UiStyle
            {
                Left = FromRight(258f), Top = UiLength.Px(gearTop + (6 + i) * 32f), Width = UiLength.Px(116f), Height = UiLength.Px(28f)
            }) { TextScale = 0.75f, Color = game.ActiveLoadout == i ? UiColor.ButtonActive : UiColor.ButtonDefault });
        }

        if (_tab == InventoryUiState.Tab.Housing)
            AddHousing(game, gearTop);
        else
            AddEquipment(game, gearTop);
    }

    private void SwitchTab(Tab tab)
    {
        _tab = tab;
        Rebuild();
    }

    private void Rebuild()
    {
        var status = _statusLabel?.Text;
        Build();
        if (status is not null && _statusLabel is not null)
            _statusLabel.Text = status;
    }

    /// <summary>
    /// Armor and the accessory slots in a column at the right edge, vanity beside them. Clicking a worn item takes it
    /// off; right-clicking (or Shift-clicking) an equippable item in the grid puts it on.
    /// </summary>
    private void AddEquipment(GameSimulation game, float top)
    {
        (EquipmentSlot Slot, string Label)[] functional =
        [
            (EquipmentSlot.Head, "Head"), (EquipmentSlot.Body, "Body"), (EquipmentSlot.Legs, "Legs"),
            (EquipmentSlot.Accessory, "Acc 1"), (EquipmentSlot.Accessory2, "Acc 2"), (EquipmentSlot.Accessory3, "Acc 3"),
            (EquipmentSlot.Accessory4, "Acc 4"), (EquipmentSlot.Accessory5, "Acc 5"),
            // The sixth slot opens with the auxiliary socket or on Annihilation worlds.
            (EquipmentSlot.Accessory6, game.AccessorySlot6Unlocked ? "Acc 6" : "Locked")
        ];
        void Slot(EquipmentSlot slot, string label, bool vanity, float fromRight, int row)
        {
            var element = new UiEquipmentSlotElement($"equip-{(vanity ? "vanity" : "gear")}-{slot}", game.Player.Equipment, slot, vanity, label, new UiStyle
            {
                Left = UiLength.Mixed(-fromRight, 1f), Top = UiLength.Px(top + row * InventoryLayout.Stride),
                Width = UiLength.Px(InventoryLayout.Slot), Height = UiLength.Px(InventoryLayout.Slot)
            });
            element.OnClick(_ =>
            {
                if (element.Item is { } worn)
                    Status(game.TryUnequipEquipment(slot, vanity) ? $"Took off {game.Items[worn].Name}." : "No room in the inventory.");
            });
            Add(element);
        }
        for (var row = 0; row < functional.Length; row++)
            Slot(functional[row].Slot, functional[row].Label, vanity: false, 66f, row);
        Slot(EquipmentSlot.Head, "Vanity", vanity: true, 120f, 0);
        Slot(EquipmentSlot.Body, "Vanity", vanity: true, 120f, 1);
        Slot(EquipmentSlot.Legs, "Vanity", vanity: true, 120f, 2);
    }

    /// <summary>Terraria's housing panel: check the room you stand in, and who lives with you.</summary>
    private void AddHousing(GameSimulation game, float top)
    {
        var left = UiLength.Mixed(-130f, 1f);
        Add(new UiTextElement("Housing", "housing-title", new UiStyle { Left = left, Top = UiLength.Px(top), Width = UiLength.Px(120f), Height = UiLength.Px(24f) })
            { Scale = 0.8f, Color = new UiColor(242, 176, 64) });
        Add(new UiButtonElement("Check this room", () =>
        {
            var center = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
            var housing = game.CheckHousing(new GridPoint((int)center.X, (int)center.Y));
            _housingReport = housing.Valid ? "Suitable housing." : $"Not valid: {housing.Reason}";
            Status(_housingReport);
            Rebuild();
        }, "housing-check", new UiStyle { Left = left, Top = UiLength.Px(top + 28f), Width = UiLength.Px(120f), Height = UiLength.Px(28f) }) { TextScale = 0.75f });
        var lines = new List<string>();
        if (_housingReport is not null)
            lines.Add(_housingReport);
        lines.Add(game.Colonists.Count == 0 ? "No crew yet." : "Crew:");
        lines.AddRange(game.Colonists.Where(colonist => colonist.IsAlive).Take(10).Select(colonist => $"{colonist.Profile.Name} · {colonist.Activity}"));
        for (var i = 0; i < lines.Count; i++)
            Add(new UiTextElement(lines[i], $"housing-line-{i}", new UiStyle
            {
                Left = UiLength.Mixed(-250f, 1f), Top = UiLength.Px(top + 62f + i * 20f), Width = UiLength.Px(240f), Height = UiLength.Px(20f)
            }) { Scale = 0.7f });
    }

    /// <summary>Shows a message on the status line under the grid (crafting results, for one).</summary>
    public void ShowStatus(string text) => Status(text);

    private void Status(string text)
    {
        if (_statusLabel is not null)
            _statusLabel.Text = text;
    }

    private void AddGrid(Inventory inventory, float top, string prefix)
    {
        var player = ReferenceEquals(inventory, PlayerInventory);
        for (var slot = 0; slot < inventory.SlotCount; slot++)
        {
            var slotIndex = slot;
            var element = new UiItemSlotElement($"{prefix}-slot-{slotIndex}", inventory, slotIndex, new UiStyle
            {
                Left = UiLength.Px(InventoryLayout.Left + slotIndex % InventoryLayout.Columns * InventoryLayout.Stride),
                Top = UiLength.Px(top + slotIndex / InventoryLayout.Columns * InventoryLayout.Stride),
                Width = UiLength.Px(InventoryLayout.Slot), Height = UiLength.Px(InventoryLayout.Slot), Padding = UiThickness.Uniform(3f)
            })
            {
                Badge = player && slotIndex < Inventory.HotbarSlots ? ((slotIndex + 1) % 10).ToString() : null,
                ShowSelection = player
            };
            element.OnClick(input =>
            {
                var (shift, ctrl, alt) = Modifiers();
                HandleSlotClick(inventory, slotIndex, input.Button, shift, ctrl, alt, input.IsDoubleClick);
            });
            element.OnActivate(_ => { _controller.LeftClick(inventory, slotIndex); RefreshCursorLabel(); });
            _slots.Add(element);
            Add(element);
        }
    }

    private void HandleSlotClick(Inventory inventory, int slotIndex, UiPointerButton button, bool shift, bool ctrl, bool alt, bool doubleClick)
    {
        var player = ReferenceEquals(inventory, PlayerInventory);
        var stack = inventory.Slots[slotIndex];
        if (_game is not null && player && button == UiPointerButton.Left && _controller.CursorStack.IsEmpty && !stack.IsEmpty)
        {
            if (alt)
            {
                _game.ToggleFavorite(slotIndex);
                Status(inventory.IsFavorite(slotIndex) ? "Favorited: kept by sort, deposit and quick stack." : "No longer a favorite.");
                return;
            }
            if (ctrl)
            {
                Status(_game.TrashInventorySlot(slotIndex) ? "Trashed." : "Favorites can't be trashed.");
                return;
            }
        }
        if (shift && button == UiPointerButton.Left && _controller.CursorStack.IsEmpty && !stack.IsEmpty)
        {
            if (ChestInventory is not null)
            {
                var moved = player ? _controller.QuickTransfer(PlayerInventory, slotIndex, ChestInventory)
                    : _controller.QuickTransfer(ChestInventory, slotIndex, PlayerInventory);
                Status(moved > 0 ? $"Moved {moved}." : "No room.");
            }
            else if (_game is not null && player && _game.Items[stack.ItemId].EquipmentSlot is not null)
                Status(_game.TryEquipFromInventory(slotIndex) ? $"Equipped {_game.Items[stack.ItemId].Name}." : $"Cannot equip {_game.Items[stack.ItemId].Name}.");
            else if (_game is not null && player)
                Status(_game.TrashInventorySlot(slotIndex) ? "Trashed." : "Favorites can't be trashed.");
            RefreshCursorLabel();
            return;
        }
        if (doubleClick && ChestInventory is not null)
        {
            if (player) _controller.QuickTransfer(PlayerInventory, slotIndex, ChestInventory);
            else _controller.QuickTransfer(ChestInventory, slotIndex, PlayerInventory);
            RefreshCursorLabel();
            return;
        }
        // Right-clicking armor or an accessory in the player grid wears it (Terraria-style quick equip).
        if (button != UiPointerButton.Left && _game is not null && player && _controller.CursorStack.IsEmpty && !stack.IsEmpty &&
            _game.Items[stack.ItemId].EquipmentSlot is not null)
        {
            var name = _game.Items[stack.ItemId].Name;
            Status(_game.TryEquipFromInventory(slotIndex) ? $"Equipped {name}." : $"Cannot equip {name}.");
            return;
        }
        if (button == UiPointerButton.Left) _controller.LeftClick(inventory, slotIndex);
        else _controller.RightClick(inventory, slotIndex);
        RefreshCursorLabel();
    }

    private void RefreshCursorLabel()
    {
        if (_cursorLabel is not null)
            _cursorLabel.Text = _controller.CursorStack.IsEmpty
                ? string.Empty
                : $"Holding {(_game is null ? _controller.CursorStack.ItemId.ToString() : _game.Items[_controller.CursorStack.ItemId].Name)} × {_controller.CursorStack.Count}";
    }
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
