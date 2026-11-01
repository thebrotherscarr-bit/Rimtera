using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>
/// Mouse-held-stack interaction logic shared by player and chest inventory grids. Given the player's inventory, the
/// held stack lives on it (<see cref="Inventory.Cursor"/>, Terraria's mouse item), so the world can use it and the
/// host can return it to the bag when the inventory closes; without one (tests) it is kept here.
/// </summary>
public sealed class InventoryUiController
{
    private readonly Inventory? _cursorHome;
    private ItemStack _localCursor;

    public InventoryUiController(Inventory? cursorHome = null) => _cursorHome = cursorHome;

    public ItemStack CursorStack
    {
        get => _cursorHome?.Cursor ?? _localCursor;
        private set
        {
            if (_cursorHome is not null)
                _cursorHome.SetCursor(value);
            else
                _localCursor = value.IsEmpty ? default : value;
        }
    }

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

    /// <summary>
    /// Terraria's right click on a slot (<c>ItemSlot.RightClick</c>): takes <paramref name="count"/> items (one per
    /// step of the hold) onto the cursor. With the same item already on the cursor it pulls more onto it, up to a full
    /// stack; with a different item it does nothing. Returns how many moved.
    /// </summary>
    public int RightClick(Inventory inventory, int slot, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        Validate(inventory, slot);
        var available = inventory.Slots[slot];
        if (available.IsEmpty || count <= 0)
            return 0;
        var held = CursorStack;
        if (held.IsEmpty)
        {
            var taken = inventory.TakeFromSlot(slot, count);
            CursorStack = taken;
            return taken.Count;
        }
        if (held.ItemId != available.ItemId || held.Calibration != available.Calibration)
            return 0;
        var room = inventory.Catalog[held.ItemId].MaxStack - held.Count;
        if (room <= 0)
            return 0;
        var more = inventory.TakeFromSlot(slot, Math.Min(count, room));
        CursorStack = held with { Count = held.Count + more.Count };
        return more.Count;
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

    /// <summary>Replaces the cursor stack (returning the trash's remainder, for one).</summary>
    public void SetCursor(ItemStack stack) => CursorStack = stack;

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
    /// <summary>The coin and ammo columns beside the grid: smaller slots, one per bag row under the hotbar, as in Terraria.</summary>
    public const float SmallSlot = 36f;
    public const float CoinColumn = Left + Columns * Stride + 4f;
    public const float AmmoColumn = CoinColumn + SmallSlot + 6f;
    public const float SideColumn = AmmoColumn + SmallSlot + 12f;
    public const float SideWidth = 118f;

    public static float GridHeight(int slots) => (slots + Columns - 1) / Columns * Stride;

    /// <summary>The top of the <paramref name="index"/>th coin or ammo slot: centred in bag row <paramref name="index"/> + 1.</summary>
    public static float SmallSlotTop(int index) => Top + (index + 1) * Stride + (Slot - SmallSlot) / 2f;
    public static float HintTop(int playerSlots) => Top + GridHeight(playerSlots);
    public static float ChestTop(int playerSlots) => HintTop(playerSlots) + 30f;
    public static float CraftingTop(int playerSlots, int chestSlots) =>
        (chestSlots > 0 ? ChestTop(playerSlots) + 24f + GridHeight(chestSlots) : HintTop(playerSlots) + 24f) + 34f;

    /// <summary>The recipe guide's item slot sits where a chest's first row would, under its heading.</summary>
    public static float GuideSlotTop(int playerSlots) => ChestTop(playerSlots) + 24f;

    /// <summary>With the recipe guide open, its recipe list starts under the guide slot (one chest row's worth of space).</summary>
    public static float GuideListTop(int playerSlots) => CraftingTop(playerSlots, Columns);
}

/// <summary>
/// The inventory screen, the hub Escape opens as in Terraria: inventory and hotbar, sort / quick stack / trash,
/// an open chest with Loot all, Deposit all, Quick stack, Restock and Sort, crafting (drawn by the canvas), worn
/// gear with loadouts, tabs for housing, crew, bestiary and feats, and the Settings button. Clicks follow
/// Terraria (ItemSlot): left picks up, places or swaps on the press; right takes one onto the cursor and keeps taking
/// while held (or equips); Shift moves to or from the open chest and otherwise does nothing; Ctrl trashes; Alt marks a
/// favorite. The cursor stack lives on the player's inventory (Terraria's mouse item), usable in the world.
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
    private UiTextElement? _guideCaption;

    private enum Tab { Gear, Housing }

    public InventoryUiState(Inventory playerInventory, InventoryUiController? controller = null, Inventory? chest = null,
        GameSimulation? game = null, GridPoint? chestCell = null, string chestTitle = "Chest", GuideLookupState? guide = null,
        bool showAlmostCraftable = false)
        : base("inventory", style: new UiStyle { Width = UiLength.Pct(1f), Height = UiLength.Pct(1f) })
    {
        PlayerInventory = playerInventory ?? throw new ArgumentNullException(nameof(playerInventory));
        ChestInventory = chest;
        _controller = controller ?? new InventoryUiController(playerInventory);
        _game = game;
        _chestCell = chestCell;
        _chestTitle = chestTitle;
        // The recipe guide takes the chest's place, so it opens only without a container.
        Guide = chest is null ? guide : null;
        ShowAlmostCraftable = showAlmostCraftable;
        Build();
    }

    /// <summary>
    /// The station AI's recipe guide (Terraria's Guide slot) when it is open in place of crafting: the looked-up item,
    /// the Used in / Made from view and the lookup history. Null while the crafting list shows.
    /// </summary>
    public GuideLookupState? Guide { get; private set; }

    /// <summary>The crafting list also shows recipes the player is close to (greyed, with what's missing in red).</summary>
    public bool ShowAlmostCraftable { get; private set; }

    /// <summary>Raised when the player flips the "close to" toggle, so the host can remember it.</summary>
    public event Action<bool>? AlmostCraftableChanged;

    /// <summary>Raised by "Recipe guide" while a chest is open: the host reopens the inventory with the guide in its place.</summary>
    public event Action<ItemId?>? GuideRequested;

    /// <summary>Raised by the "Station AI tips" button: the host opens the station AI's advice.</summary>
    public event Action? TipsRequested;

    /// <summary>Where the canvas draws the crafting list or, with the guide open, the guide's recipe list.</summary>
    public float CraftingTop => Guide is not null
        ? InventoryLayout.GuideListTop(PlayerInventory.BagSlots)
        : InventoryLayout.CraftingTop(PlayerInventory.BagSlots, ChestInventory?.BagSlots ?? 0);

    /// <summary>Opens the recipe guide in place of crafting (with <paramref name="item"/> in its slot, if given).</summary>
    public void OpenGuide(ItemId? item = null, GuideView? view = null)
    {
        if (ChestInventory is not null)
            return;
        Guide ??= new GuideLookupState();
        if (item is { } lookup)
            Guide.LookUp(lookup, view);
        else if (view is { } chosen)
            Guide.View = chosen;
        Rebuild();
    }

    /// <summary>Closes the recipe guide and shows crafting again.</summary>
    public void CloseGuide()
    {
        Guide = null;
        Rebuild();
    }

    /// <summary>Looks an item up in the open guide (an ingredient or result clicked in its list, or the slot).</summary>
    public void GuideLookUp(ItemId item)
    {
        if (Guide is null)
            return;
        Guide.LookUp(item);
        RefreshGuideCaption();
    }

    /// <summary>Shows or hides the recipes the player is close to in the crafting list.</summary>
    public void SetShowAlmostCraftable(bool show)
    {
        if (ShowAlmostCraftable == show)
            return;
        ShowAlmostCraftable = show;
        AlmostCraftableChanged?.Invoke(show);
        Rebuild();
    }

    /// <summary>Raised by the chest's "Rename" button (Terraria's <c>ChestUI.RenameChest</c>); the host prompts for the name.</summary>
    public event Action<GridPoint>? RenameRequested;

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
    public const string ClickHints = "Shift-click  to chest     Ctrl-click  trash     Alt-click  favorite     Right-click  take one / equip";

    public void SelectHotbarSlot(int index) => PlayerInventory.Select(index);

    public int QuickMovePlayerSlotToChest(int slot) => ChestInventory is null ? 0 : _controller.QuickTransfer(PlayerInventory, slot, ChestInventory);
    public int QuickMoveChestSlotToPlayer(int slot) => ChestInventory is null ? 0 : _controller.QuickTransfer(ChestInventory, slot, PlayerInventory);

    /// <summary>A click on a player-grid slot with the given modifiers, as the screen handles it (for hosts and tests).</summary>
    public void ClickPlayerSlot(int slot, UiPointerButton button = UiPointerButton.Left, bool shift = false, bool ctrl = false, bool alt = false) =>
        HandleSlotClick(PlayerInventory, slot, button, shift, ctrl, alt);

    private void Build()
    {
        _slots.Clear();
        ClearChildren();
        var playerSlots = PlayerInventory.BagSlots;
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
        if (Guide is not null && _game is not null)
            AddGuideSlot();
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
            Left = UiLength.Px(x), Top = UiLength.Px(InventoryLayout.HintTop(PlayerInventory.BagSlots) - InventoryLayout.Stride),
            Width = UiLength.Px(InventoryLayout.Slot), Height = UiLength.Px(InventoryLayout.Slot)
        });
        trash.ClickOnPress = true;
        // Terraria's trash: drop a held stack in (destroying what was there); click it empty-handed to take it back, or
        // Shift-click it to send it straight back to the inventory (GetAlternateClickAction: TransferToBackpack).
        trash.OnClick(_ =>
        {
            if (Modifiers().Shift && _controller.CursorStack.IsEmpty && !game.TrashSlot.IsEmpty)
            {
                var back = game.TakeTrash();
                var added = PlayerInventory.Add(back.ItemId, back.Count, back.Calibration);
                if (added < back.Count)
                    game.PutInTrash(back with { Count = back.Count - added });
                Status(added > 0 ? "Back in the inventory." : "No room.");
            }
            else if (!_controller.CursorStack.IsEmpty)
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
        var top = InventoryLayout.ChestTop(PlayerInventory.BagSlots) + 24f;
        Button("chest-loot-all", "Loot all", x, top, InventoryLayout.SideWidth, () => Status($"Took {game.LootAll(_chestCell)} items."));
        Button("chest-deposit-all", "Deposit all", x, top + 30f, InventoryLayout.SideWidth, () => Status($"Stored {game.DepositAll(_chestCell)} items."));
        Button("chest-quick-stack", "Quick stack", x, top + 60f, InventoryLayout.SideWidth, () => Status($"Stacked {game.QuickStackInto(_chestCell)} items here."));
        Button("chest-restock", "Restock", x, top + 90f, InventoryLayout.SideWidth, () => Status($"Restocked {game.Restock(_chestCell)} items."));
        Button("chest-sort", "Sort", x, top + 120f, InventoryLayout.SideWidth, () => Status(game.SortContainer(_chestCell) ? "Sorted the container." : "Nothing to sort."));
        if (_chestCell is { } cell)
            Button("chest-rename", "Rename", x, top + 150f, InventoryLayout.SideWidth, () => RenameRequested?.Invoke(cell));
    }

    /// <summary>
    /// The crafting heading with its toggles: also show recipes you're close to, the station AI's recipe guide (Terraria's
    /// Guide: hand it an item to see what it makes) and its tips. With the guide open, the heading switches its views.
    /// </summary>
    private void AddCraftingHeader(GameSimulation game)
    {
        var top = CraftingTop - 30f;
        Add(new UiTextElement(Guide is null ? "Crafting" : "Recipe guide", "crafting-title", new UiStyle
        {
            Left = UiLength.Px(InventoryLayout.Left - 10f), Top = UiLength.Px(top), Width = UiLength.Px(140f), Height = UiLength.Px(26f)
        }) { Scale = 0.8f, Color = new UiColor(242, 176, 64) });
        if (Guide is null)
        {
            Button("crafting-almost", ShowAlmostCraftable ? "Close to: shown" : "Close to: hidden", InventoryLayout.Left + 110f, top, 140f,
                () => SetShowAlmostCraftable(!ShowAlmostCraftable), height: 26f);
            Button("crafting-guide", "Recipe guide", InventoryLayout.Left + 256f, top, 120f, () =>
            {
                // Seeds the guide with the item on the cursor or in hand, as handing the Guide an item would. The guide
                // takes the chest's place, so with a chest open the host reopens the inventory without it.
                ItemId? item = !_controller.CursorStack.IsEmpty ? _controller.CursorStack.ItemId
                    : PlayerInventory.SelectedStack.IsEmpty ? null : PlayerInventory.SelectedStack.ItemId;
                if (ChestInventory is null)
                    OpenGuide(item);
                else
                    GuideRequested?.Invoke(item);
            }, height: 26f);
            Button("crafting-tips", "Station AI tips", InventoryLayout.Left + 382f, top, 128f, () => TipsRequested?.Invoke(), height: 26f);
            return;
        }
        var guide = Guide;
        void ViewButton(string id, string text, GuideView view, float left) =>
            Add(new UiButtonElement(text, () => { guide.View = view; guide.Scroll = 0; Rebuild(); }, id, new UiStyle
            {
                Left = UiLength.Px(InventoryLayout.Left + left), Top = UiLength.Px(top), Width = UiLength.Px(100f), Height = UiLength.Px(26f)
            }) { TextScale = 0.75f, Color = guide.View == view ? UiColor.ButtonActive : UiColor.ButtonDefault });
        ViewButton("guide-used-in", "Used in", GuideView.UsedIn, 110f);
        ViewButton("guide-made-from", "Made from", GuideView.MadeFrom, 214f);
        Button("guide-back", "Back", InventoryLayout.Left + 318f, top, 70f, () =>
        {
            if (guide.Back())
                RefreshGuideCaption();
        }, height: 26f);
        Button("guide-close", "Crafting", InventoryLayout.Left + 392f, top, 100f, CloseGuide, height: 26f);
    }

    /// <summary>
    /// The guide's item slot, where a chest would sit: click it holding an item (or Shift-click an inventory item) to look
    /// that item up; click it empty-handed to clear it. The item itself never leaves the inventory or the cursor.
    /// </summary>
    private void AddGuideSlot()
    {
        var guide = Guide!;
        var playerSlots = PlayerInventory.BagSlots;
        Add(new UiTextElement("Station AI · recipe guide", "guide-heading", new UiStyle
        {
            Left = UiLength.Px(InventoryLayout.Left - 10f), Top = UiLength.Px(InventoryLayout.ChestTop(playerSlots)), Width = UiLength.Px(300f), Height = UiLength.Px(22f)
        }) { Scale = 0.8f, Color = new UiColor(242, 176, 64) });
        var slot = new UiStackSlotElement("guide-slot", "Item", () => guide.Item is { } item ? new ItemStack(item, 1) : default, new UiStyle
        {
            Left = UiLength.Px(InventoryLayout.Left), Top = UiLength.Px(InventoryLayout.GuideSlotTop(playerSlots)),
            Width = UiLength.Px(InventoryLayout.Slot), Height = UiLength.Px(InventoryLayout.Slot)
        });
        slot.OnClick(_ =>
        {
            if (!_controller.CursorStack.IsEmpty)
                GuideLookUp(_controller.CursorStack.ItemId);
            else
            {
                guide.Clear();
                RefreshGuideCaption();
            }
        });
        Add(slot);
        _guideCaption = new UiTextElement(string.Empty, "guide-caption", new UiStyle
        {
            Left = UiLength.Px(InventoryLayout.Left + InventoryLayout.Stride + 4f), Top = UiLength.Px(InventoryLayout.GuideSlotTop(playerSlots) + 2f),
            Width = UiLength.Px(InventoryLayout.Columns * InventoryLayout.Stride - InventoryLayout.Stride - 10f), Height = UiLength.Px(42f)
        }) { Scale = 0.72f, Color = new UiColor(200, 206, 218), Wrap = true };
        Add(_guideCaption);
        RefreshGuideCaption();
    }

    /// <summary>The line beside the guide slot: what is looked up and how to use the guide.</summary>
    public string GuideCaption => _guideCaption?.Text ?? string.Empty;

    private void RefreshGuideCaption()
    {
        if (_guideCaption is null || Guide is null || _game is null)
            return;
        _guideCaption.Text = Guide.Item is { } item
            ? $"{_game.Items[item].Name}: {(Guide.View == GuideView.UsedIn ? "what it is used in" : "what makes it")}. Click any item below to look it up; Back returns."
            : "Click this slot while holding an item, or Shift-click an item in your inventory, to see every recipe that uses it.";
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

    /// <summary>Rebuilds the screen from the game's current state (a loadout switched from the keyboard, for one).</summary>
    public void Refresh() => Rebuild();

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

        // Terraria's misc equipment: pet, light pet, mount and hook, under the vanity column. Drop a fitting item from the
        // cursor in (what was there comes back on the cursor); click empty-handed to take it out.
        (MiscSlot Slot, string Label)[] misc = [(MiscSlot.Pet, "Pet"), (MiscSlot.LightPet, "Light"), (MiscSlot.Mount, "Mount"), (MiscSlot.Hook, "Hook")];
        for (var index = 0; index < misc.Length; index++)
        {
            var (miscSlot, label) = misc[index];
            var element = new UiStackSlotElement($"equip-misc-{miscSlot}", label,
                () => game.MiscEquipped(miscSlot) is { } id ? new ItemStack(id, 1) : default, new UiStyle
                {
                    Left = UiLength.Mixed(-120f, 1f), Top = UiLength.Px(top + (4 + index) * InventoryLayout.Stride),
                    Width = UiLength.Px(InventoryLayout.Slot), Height = UiLength.Px(InventoryLayout.Slot)
                });
            element.ClickOnPress = true;
            element.OnClick(click =>
            {
                var held = _controller.CursorStack;
                if (!held.IsEmpty)
                {
                    if (held.Count != 1 || !game.SetMisc(miscSlot, held.ItemId, out var previous))
                    {
                        Status($"That doesn't go in the {label.ToLowerInvariant()} slot.");
                        return;
                    }
                    _controller.DropCursor();
                    if (previous is { } back)
                        _controller.PickUp(new ItemStack(back, 1));
                    Status($"Equipped {game.Items[held.ItemId].Name}.");
                }
                else if (game.MiscEquipped(miscSlot) is { } worn && game.SetMisc(miscSlot, null, out _))
                    _controller.PickUp(new ItemStack(worn, 1));
                RefreshCursorLabel();
            });
            Add(element);
        }
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
        // Terraria's housing page: every resident with where they live and how happy they are; "Move here" assigns them
        // the room the player stands in. Then the crew, and any role left empty by a death.
        var y = top + 64f;
        void Line(string text, string id, UiColor color)
        {
            Add(new UiTextElement(text, id, new UiStyle
            {
                Left = UiLength.Mixed(-250f, 1f), Top = UiLength.Px(y), Width = UiLength.Px(240f), Height = UiLength.Px(18f)
            }) { Scale = 0.7f, Color = color });
            y += 20f;
        }
        if (_housingReport is not null)
            Line(_housingReport, "housing-report", new UiColor(242, 176, 64));
        var residents = game.TownNpcs.Where(npc => !npc.IsBound).Take(8).ToList();
        Line(residents.Count == 0 ? "No residents yet." : "Residents", "housing-residents", new UiColor(242, 176, 64));
        foreach (var npc in residents)
        {
            var resident = npc;
            var home = npc.HomeCell is null ? "homeless" : $"happiness {game.GetHappiness(npc):+0.0;-0.0;0}";
            Add(new UiButtonElement("Move here", () =>
            {
                var center = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
                _housingReport = game.AssignHome(resident, new GridPoint((int)center.X, (int)center.Y));
                Status(_housingReport);
                Rebuild();
            }, $"housing-move-{npc.Id}", new UiStyle
            {
                Left = UiLength.Mixed(-82f, 1f), Top = UiLength.Px(y - 1f), Width = UiLength.Px(72f), Height = UiLength.Px(20f)
            }) { TextScale = 0.6f });
            Line($"{npc.Name} · {npc.Definition.Role} · {home}", $"housing-npc-{npc.Id}", new UiColor(226, 230, 238));
        }
        var crew = game.Colonists.Where(colonist => colonist.IsAlive).Take(6).ToList();
        Line(crew.Count == 0 ? "No crew yet." : "Crew", "housing-crew", new UiColor(242, 176, 64));
        foreach (var colonist in crew)
            Line($"{colonist.Profile.Name} · {colonist.Activity}", $"housing-crew-{colonist.Id}", new UiColor(226, 230, 238));
        foreach (var lost in game.LostResidentKinds().Take(4))
            Line($"Vacant: {TownNpcCatalog.Get(lost).Role} (lost)", $"housing-lost-{lost}", new UiColor(150, 160, 172));
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
        if (inventory.CoinSlots > 0)
            ColumnLabel("Coins", $"{prefix}-coins-label", InventoryLayout.CoinColumn, top);
        if (inventory.AmmoSlots > 0)
            ColumnLabel("Ammo", $"{prefix}-ammo-label", InventoryLayout.AmmoColumn, top);
        for (var slot = 0; slot < inventory.SlotCount; slot++)
        {
            var slotIndex = slot;
            var coin = inventory.IsCoinSlot(slotIndex);
            var ammo = inventory.IsAmmoSlot(slotIndex);
            var style = coin || ammo
                ? new UiStyle
                {
                    Left = UiLength.Px(coin ? InventoryLayout.CoinColumn : InventoryLayout.AmmoColumn),
                    Top = UiLength.Px(top - InventoryLayout.Top +
                        InventoryLayout.SmallSlotTop(slotIndex - (coin ? inventory.CoinSlotStart : inventory.AmmoSlotStart))),
                    Width = UiLength.Px(InventoryLayout.SmallSlot), Height = UiLength.Px(InventoryLayout.SmallSlot), Padding = UiThickness.Uniform(2f)
                }
                : new UiStyle
                {
                    Left = UiLength.Px(InventoryLayout.Left + slotIndex % InventoryLayout.Columns * InventoryLayout.Stride),
                    Top = UiLength.Px(top + slotIndex / InventoryLayout.Columns * InventoryLayout.Stride),
                    Width = UiLength.Px(InventoryLayout.Slot), Height = UiLength.Px(InventoryLayout.Slot), Padding = UiThickness.Uniform(3f)
                };
            var element = new UiItemSlotElement($"{prefix}-slot-{slotIndex}", inventory, slotIndex, style)
            {
                Badge = player && slotIndex < Inventory.HotbarSlots ? ((slotIndex + 1) % 10).ToString() : null,
                ShowSelection = player && !coin && !ammo,
                ClickOnPress = true
            };
            element.OnClick(input =>
            {
                var (shift, ctrl, alt) = Modifiers();
                HandleSlotClick(inventory, slotIndex, input.Button, shift, ctrl, alt);
            });
            element.OnActivate(_ => { _controller.LeftClick(inventory, slotIndex); RefreshCursorLabel(); });
            _slots.Add(element);
            Add(element);
        }
    }

    /// <summary>The small heading over the coin or ammo column, in the hotbar row beside it.</summary>
    private void ColumnLabel(string text, string id, float left, float gridTop) =>
        Add(new UiTextElement(text, id, new UiStyle
        {
            Left = UiLength.Px(left - 10f), Top = UiLength.Px(gridTop + 14f), Width = UiLength.Px(InventoryLayout.SmallSlot + 22f), Height = UiLength.Px(18f)
        }) { Scale = 0.6f, Color = new UiColor(200, 206, 218) });

    /// <summary>
    /// A press on a grid slot, as Terraria's <c>ItemSlot</c> handles it (<c>OverrideHover</c> / <c>GetAlternateClickAction</c> /
    /// <c>LeftClick</c>): Alt toggles a favourite; Ctrl trashes (favourites refuse); Shift moves the stack to or from the open
    /// chest, or hands it to the open recipe guide, and with neither does nothing (Terraria leaves Shift-trash off by
    /// default); a plain left press picks up, places, merges or swaps. A right press goes through <see cref="RightPress"/>,
    /// which the host repeats while the button is held (<see cref="TickRightHold"/>).
    /// </summary>
    private void HandleSlotClick(Inventory inventory, int slotIndex, UiPointerButton button, bool shift, bool ctrl, bool alt)
    {
        if (button == UiPointerButton.Right)
        {
            RightPress(inventory, slotIndex, fresh: true, count: 1);
            return;
        }
        var player = ReferenceEquals(inventory, PlayerInventory);
        var stack = inventory.Slots[slotIndex];
        if (_game is not null && player && _controller.CursorStack.IsEmpty && !stack.IsEmpty)
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
        if (shift && _controller.CursorStack.IsEmpty && !stack.IsEmpty)
        {
            if (Guide is not null && player)
                // With the recipe guide open, Shift-click hands the item to the guide (Terraria's Guide slot).
                GuideLookUp(stack.ItemId);
            else if (ChestInventory is not null)
            {
                var moved = player ? _controller.QuickTransfer(PlayerInventory, slotIndex, ChestInventory)
                    : _controller.QuickTransfer(ChestInventory, slotIndex, PlayerInventory);
                Status(moved > 0 ? $"Moved {moved}." : "No room.");
            }
            RefreshCursorLabel();
            return;
        }
        _controller.LeftClick(inventory, slotIndex);
        RefreshCursorLabel();
    }

    private readonly StackSplitRepeater _rightHold = new();
    private bool _rightHeldLastTick;

    /// <summary>
    /// Called by the host every tick with the element under the pointer and whether the right button (pressed on the
    /// interface) is held. Over a grid slot the press takes one item (or equips), and holding keeps taking at Terraria's
    /// accelerating rate: after 30 ticks, then every 7 ticks and faster (<see cref="StackSplitRepeater"/>).
    /// </summary>
    public void TickRightHold(UiElement? hovered, bool rightHeld)
    {
        var slot = hovered as UiItemSlotElement;
        var fresh = rightHeld && !_rightHeldLastTick;
        _rightHeldLastTick = rightHeld;
        var take = _rightHold.Tick(rightHeld && slot is not null);
        if (take > 0 && slot is not null)
            RightPress(slot.Inventory, slot.SlotIndex, fresh, take);
    }

    /// <summary>
    /// One step of a right click on a slot. On the press itself an equippable single item in the player's grid is put on
    /// (<c>SwapEquip</c>); otherwise <paramref name="count"/> items move onto the cursor (Terraria's stack split).
    /// </summary>
    private void RightPress(Inventory inventory, int slotIndex, bool fresh, int count)
    {
        var player = ReferenceEquals(inventory, PlayerInventory);
        var stack = inventory.Slots[slotIndex];
        if (stack.IsEmpty)
            return;
        if (fresh && _game is not null && player && _controller.CursorStack.IsEmpty && stack.Count == 1 &&
            _game.Items[stack.ItemId].EquipmentSlot is not null)
        {
            var name = _game.Items[stack.ItemId].Name;
            Status(_game.TryEquipFromInventory(slotIndex) ? $"Equipped {name}." : $"Cannot equip {name}.");
            return;
        }
        _controller.RightClick(inventory, slotIndex, count);
        RefreshCursorLabel();
    }

    /// <summary>Shows what rides on the cursor (refreshed every tick: a use in the world or a throw changes it too).</summary>
    public void RefreshCursorLabel()
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
