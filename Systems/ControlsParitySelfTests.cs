using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rimtera.AI;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The controls pass (docs/CONTROLS_PARITY.md): the one mouse-button state that feeds both the UI and the world, press-time
/// slot clicks, Terraria's right-click stack split, auto-reuse, the cursor (mouse) item, throwing, buffered hotbar changes,
/// Shift auto-select, Terraria's default keys and profile migration, chest names, and a player that never acts or recovers
/// on its own.
/// </summary>
public static class ControlsParitySelfTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private static GameSimulation NewGame(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 80, height: 48) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e7f);
        for (var y = 4; y <= 21; y++)
        for (var x = 4; x <= 76; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y >= 20 ? TileId.Rock : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            data.WallId = WallIds.None;
            game.World.SetCell(cell, data);
        }
        game.Player.Motion.Position = new Vector2(30f, 20f - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        foreach (var agent in game.Colonists)
            agent.Motion.Position = new Vector2(10f, 20f - agent.Motion.Size.Y - 0.01f);
        Tick(game, 3);
        return game;
    }

    private static void Tick(GameSimulation game, int ticks = 1, PlayerAction action = PlayerAction.None, GridPoint? target = null)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f), action, target));
    }

    private static void ClearInventory(GameSimulation game)
    {
        var inventory = game.Player.Inventory;
        for (var slot = 0; slot < inventory.SlotCount; slot++)
            inventory.TakeFromSlot(slot);
        inventory.SetCursor(default);
        inventory.Select(0);
    }

    public static void Run(Action<bool, string> check)
    {
        MouseState(check);
        HubPointer(check);
        UseAndSplit(check);
        AutoSwingGear(check);
        CursorItem(check);
        ThrowAndHotbar(check);
        SmartSelect(check);
        KeysAndProfiles(check);
        ChestNames(check);
        NoAutonomousPlayer(check);
    }

    // ------------------------------------------------------------------ the mouse-button state machine

    private static void MouseState(Action<bool, string> check)
    {
        var mouse = new MouseButtonState();
        mouse.Press(UiPointerButton.Left, PointerOwner.World);
        var worldUse = mouse.WorldUseHeld(overInterface: false);
        var pausedOverUi = !mouse.WorldUseHeld(overInterface: true);
        var notUiDown = !mouse.InterfaceLeftDown;
        mouse.Release(UiPointerButton.Left);
        check(worldUse && pausedOverUi && notUiDown && !mouse.WorldUseHeld(false) && !mouse.AnyDown,
            "a press on the world uses the item while held, pauses over an interface, never reaches the UI, and stops on release");

        mouse.Press(UiPointerButton.Left, PointerOwner.World);
        mouse.Press(UiPointerButton.Right, PointerOwner.Interface);
        mouse.Clear();
        check(!mouse.AnyDown && !mouse.WorldUseHeld(false) && !mouse.InterfaceRightDown,
            "losing focus or mouse capture releases every button, so a lost release can't leave the player using an item forever");

        mouse.Press(UiPointerButton.Left, PointerOwner.Interface);
        check(mouse.InterfaceLeftDown && !mouse.WorldUseHeld(false),
            "a press that began on an interface never uses the held item in the world (Terraria's mouseInterface)");
    }

    /// <summary>Drives the real hub through the UI with frames built from the event-tracked state, as the window does.</summary>
    private static void HubPointer(Action<bool, string> check)
    {
        var game = NewGame(7301);
        var hub = new InventoryUiState(game.Player.Inventory, game: game);
        var ui = new UserInterface();
        ui.SetViewport(1280, 720);
        ui.SetState(hub);
        var settings = hub.DescendantsAndSelf().First(element => element.Id == "inventory-settings");
        var at = new Vector2(settings.Bounds.X + settings.Bounds.Width / 2f, settings.Bounds.Y + settings.Bounds.Height / 2f);
        var opened = 0;
        hub.SettingsRequested += () => opened++;
        var mouse = new MouseButtonState();
        UiInputFrame Frame() => new(at, LeftDown: mouse.InterfaceLeftDown, RightDown: mouse.InterfaceRightDown);

        // A posted click: the down and up events arrive, the hardware never reports the button. The events alone press it.
        mouse.Press(UiPointerButton.Left, PointerOwner.Interface);
        ui.Update(Frame());
        mouse.Release(UiPointerButton.Left);
        ui.Update(Frame());
        var posted = opened;
        // A press that began on the world and is released over the button is not a click.
        mouse.Press(UiPointerButton.Left, PointerOwner.World);
        ui.Update(Frame());
        mouse.Release(UiPointerButton.Left);
        ui.Update(Frame());
        // A press cancelled by a focus loss does not click when the button comes up later.
        mouse.Press(UiPointerButton.Left, PointerOwner.Interface);
        ui.Update(Frame());
        mouse.Clear();
        ui.CancelPointer();
        ui.Update(Frame());
        check(posted == 1 && opened == 1,
            "hub buttons press from the window's own mouse events (posted clicks work), never from a world press, never after a cancelled press");

        // Item slots act on the press, as Terraria's ItemSlot.LeftClick does.
        game.Player.Inventory.PlaceIntoSlot(12, new ItemStack(ItemId.RockBlock, 7));
        var slot = hub.Slots.First(element => ReferenceEquals(element.Inventory, game.Player.Inventory) && element.SlotIndex == 12);
        at = new Vector2(slot.Bounds.X + slot.Bounds.Width / 2f, slot.Bounds.Y + slot.Bounds.Height / 2f);
        mouse.Press(UiPointerButton.Left, PointerOwner.Interface);
        ui.Update(Frame());
        var onPress = game.Player.Inventory.Cursor.Count == 7 && game.Player.Inventory.Slots[12].IsEmpty;
        mouse.Release(UiPointerButton.Left);
        ui.Update(Frame());
        check(onPress && game.Player.Inventory.Cursor.Count == 7,
            "inventory slots pick up on the press, and the release does nothing more");
        game.ReturnCursorItem();
    }

    // ------------------------------------------------------------------ auto-swing gear (IC-15)

    /// <summary>
    /// Auto-swing accessories (the power glove, Terraria's feral claws) make every melee weapon and whip repeat while the
    /// button is held. Drives the simulation the way the window does: the use gate fed with <see cref="GameSimulation.AutoReuseSelected"/>.
    /// </summary>
    private static void AutoSwingGear(Action<bool, string> check)
    {
        var items = new ItemCatalog();
        var shortsword = items.All.First(item => item.Style == WeaponStyle.Shortsword);
        var whip = items.All.First(item => item.Style == WeaponStyle.Whip);
        var potion = items.All.First(item => item.UseKind == ItemUseKind.Consumable && item.HealAmount > 0f);
        check(ItemUseRules.AutoReuse(shortsword, autoSwingActive: true) && ItemUseRules.AutoReuse(whip, autoSwingActive: true) &&
              !ItemUseRules.AutoReuse(shortsword, autoSwingActive: false) && !ItemUseRules.AutoReuse(potion, autoSwingActive: true),
            "auto-swing gear makes melee weapons and whips repeat; potions still need a fresh click");

        int Swings(bool glove)
        {
            var game = NewGame(7311);
            ClearInventory(game);
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
                game.Player.Equipment.Set(slot, null);
            if (glove)
                game.Player.Equipment.Set(EquipmentSlot.Accessory, ItemId.PowerGlove);
            game.Player.Inventory.Add(shortsword.Id, 1);
            game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(shortsword.Id));
            var target = new GridPoint((int)(game.Player.Motion.Position.X + 3f), (int)(game.Player.Motion.Position.Y + 1f));
            var gate = new UseButtonGate();
            var swings = 0;
            float? previous = null;
            for (var tick = 0; tick < 90; tick++)
            {
                var use = gate.Update(held: true, game.AutoReuseSelected);
                Tick(game, 1, use ? PlayerAction.UseSelected : PlayerAction.None, target);
                var progress = game.PlayerSwing?.Progress;
                if (progress is { } now && (previous is null || now < previous.Value))
                    swings++;
                previous = progress;
            }
            return swings;
        }
        var (without, with) = (Swings(glove: false), Swings(glove: true));
        check(without == 1 && with >= 3,
            $"holding use with a shortsword: one stab bare-handed, repeated stabs with the power glove ({without} vs {with} in 1.5 s)");
    }

    // ------------------------------------------------------------------ auto-reuse and the right-click split

    private static void UseAndSplit(Action<bool, string> check)
    {
        var items = new ItemCatalog();
        var potion = items.All.First(item => item.UseKind == ItemUseKind.Consumable && item.HealAmount > 0f);
        var broadsword = items.All.First(item => item.Style == WeaponStyle.Broadsword);
        var shortsword = items.All.First(item => item.Style == WeaponStyle.Shortsword);
        check(ItemUseRules.AutoReuse(items[ItemId.SoilBlock]) && ItemUseRules.AutoReuse(items[ItemId.CopperPickaxe]) &&
              ItemUseRules.AutoReuse(broadsword) && !ItemUseRules.AutoReuse(potion) && !ItemUseRules.AutoReuse(shortsword),
            "auto-reuse follows Terraria's kinds: blocks, picks and broadswords repeat while held; potions and shortswords need a fresh click");

        int Uses(bool autoReuse, params bool[] held)
        {
            var gate = new UseButtonGate();
            return held.Count(tick => gate.Update(tick, autoReuse));
        }
        var hold = Enumerable.Repeat(true, 20).ToArray();
        check(Uses(false, hold) == 1 && Uses(true, hold) == 20 && Uses(false, true, true, false, true, true) == 2,
            "holding the button uses an item without auto-reuse once; letting go and pressing again uses it again");

        var split = new StackSplitRepeater();
        var takes = Enumerable.Range(0, 200).Select(_ => split.Tick(true)).ToArray();
        var firstRepeat = Array.FindIndex(takes, 1, take => take > 0);
        var secondRepeat = Array.FindIndex(takes, firstRepeat + 1, take => take > 0);
        check(takes[0] == 1 && firstRepeat == StackSplitRepeater.FirstRepeatTicks && secondRepeat - firstRepeat is 6 or 7 &&
              takes.Skip(150).Max() > 1 && split.Tick(false) == 0 && split.Tick(true) == 1,
            "right-click hold takes one, the next after 30 ticks, then every 7 and faster until several a tick; letting go starts over");

        var game = NewGame(7302);
        ClearInventory(game);
        game.Player.Inventory.PlaceIntoSlot(15, new ItemStack(ItemId.RockBlock, 40));
        var hub = new InventoryUiState(game.Player.Inventory, game: game);
        var slot = hub.Slots.First(element => ReferenceEquals(element.Inventory, game.Player.Inventory) && element.SlotIndex == 15);
        for (var i = 0; i < 31; i++)
            hub.TickRightHold(slot, rightHeld: true);
        var afterHold = game.Player.Inventory.Cursor.Count;
        hub.TickRightHold(slot, rightHeld: false);
        game.ReturnCursorItem();
        var returned = game.Player.Inventory.Count(ItemId.RockBlock) == 40;
        game.Player.Inventory.PlaceIntoSlot(20, new ItemStack(ItemId.WoodBlock, 4));
        hub.ClickPlayerSlot(20, shift: true);
        var shiftNoChest = game.Player.Inventory.Slots[20].Count == 4 && game.TrashSlot.IsEmpty && game.Player.Inventory.Cursor.IsEmpty;
        check(afterHold == 2 && returned && shiftNoChest,
            "the hub's right-click hold takes one, then another after 30 ticks; Shift-click with no chest open does nothing (Terraria leaves Shift-trash off)");

        game.Player.Inventory.PlaceIntoSlot(21, new ItemStack(ItemId.Berry, 3));
        hub.ClickPlayerSlot(21, ctrl: true);
        hub.Modifiers = () => (true, false, false);
        var trash = hub.DescendantsAndSelf().First(element => element.Id == "inventory-trash");
        trash.RaiseClick(new UiPointerEvent(trash, Vector2.Zero, UiPointerButton.Left, false));
        check(game.TrashSlot.IsEmpty && game.Player.Inventory.Count(ItemId.Berry) == 3 && trash.ClickOnPress,
            "Shift-clicking the trash slot sends its item straight back to the inventory");
    }

    // ------------------------------------------------------------------ the cursor (mouse) item

    private static void CursorItem(Action<bool, string> check)
    {
        var items = new ItemCatalog();
        var bag = new Inventory(items, slotCount: 3);
        bag.Add(ItemId.RockBlock, 10);
        bag.SetCursor(new ItemStack(ItemId.RockBlock, 5));
        var held = bag.SelectedItem?.Id == ItemId.RockBlock && bag.SelectedStack.Count == 5;
        bag.TryConsume(ItemId.RockBlock, 2);
        var spentCursor = bag.Cursor.Count == 3 && bag.Count(ItemId.RockBlock) == 10;
        var saved = bag.SlotsWithCursorReturned();
        check(held && spentCursor && saved.Where(stack => !stack.IsEmpty).Sum(stack => stack.Count) == 13 && bag.Cursor.Count == 3,
            "the cursor stack is the held item, a use spends it first, and a save folds it back into the bag");

        var full = new Inventory(items, slotCount: 1);
        full.Add(ItemId.WoodBlock, 5);
        full.SetCursor(new ItemStack(ItemId.RockBlock, 4));
        var rest = full.ReturnCursor();
        check(rest.Count == 4 && full.Cursor.IsEmpty, "returning the cursor to a full bag hands back what did not fit");

        var game = NewGame(7303);
        ClearInventory(game);
        game.Player.Inventory.Add(ItemId.WoodBlock, 50 * game.Items[ItemId.WoodBlock].MaxStack);
        game.Player.Inventory.SetCursor(new ItemStack(ItemId.RockBlock, 6));
        var groundBefore = game.GroundItems.Count;
        var dropped = game.ReturnCursorItem();
        check(dropped is { ItemId: ItemId.RockBlock, Count: 6 } && game.GroundItems.Count == groundBefore + 1 && game.Player.Inventory.Cursor.IsEmpty,
            "closing the inventory with a full bag drops the cursor stack at the player's feet; nothing is lost");

        ClearInventory(game);
        game.Player.Inventory.SetCursor(new ItemStack(ItemId.SoilBlock, 4));
        var target = new GridPoint(33, 19);
        for (var i = 0; i < 40 && game.World.TileAt(target.X, target.Y) == TileId.Air; i++)
            Tick(game, 1, PlayerAction.UseSelected, target);
        check(game.World.TileAt(target.X, target.Y) == TileId.Soil && game.Player.Inventory.Cursor.Count == 3 &&
              game.Player.Inventory.Count(ItemId.SoilBlock) == 0,
            "with the inventory open the item on the cursor is used in the world (Terraria's slot 58): a block placed from it");

        var thrown = game.ThrowCursorItem();
        check(thrown is { ItemId: ItemId.SoilBlock, Count: 3 } && game.Player.Inventory.Cursor.IsEmpty,
            "right-clicking the world throws the cursor stack into the world");
    }

    // ------------------------------------------------------------------ throwing and the hotbar

    private static void ThrowAndHotbar(Action<bool, string> check)
    {
        var game = NewGame(7304);
        ClearInventory(game);
        game.Player.Inventory.PlaceIntoSlot(0, new ItemStack(ItemId.RockBlock, 5));
        game.Player.Inventory.PlaceIntoSlot(1, new ItemStack(ItemId.WoodBlock, 5));
        game.TrySelectInventorySlot(0);
        game.ToggleFavorite(0);
        var favorite = game.TryThrowHeld();
        game.ToggleFavorite(0);
        game.Player.ActionCooldownTicks = 5;
        var busy = game.TryThrowHeld();
        var buffered = game.RequestHotbarSlot(1) && game.Player.Inventory.SelectedSlot == 0 && game.PendingHotbarSlot == 1;
        Tick(game, 10);
        var landed = game.Player.Inventory.SelectedSlot == 1;
        game.TrySelectInventorySlot(0);
        var thrown = game.TryThrowHeld();
        check(favorite == ThrowOutcome.Favorite && busy == ThrowOutcome.Busy && thrown == ThrowOutcome.Thrown &&
              game.Player.Inventory.Slots[0].IsEmpty,
            "throw refuses favorites and waits for the current use to end, then throws the held stack");
        check(buffered && landed, "a hotbar change made mid-use waits until the use ends, then lands");
    }

    // ------------------------------------------------------------------ Shift auto-select

    private static void SmartSelect(Action<bool, string> check)
    {
        var game = NewGame(7305);
        ClearInventory(game);
        var cutter = game.Items.All.First(item => item.UseKind == ItemUseKind.Cutter).Id;
        game.Player.Inventory.PlaceIntoSlot(2, new ItemStack(ItemId.SoilBlock, 10));
        game.Player.Inventory.PlaceIntoSlot(30, new ItemStack(ItemId.CopperPickaxe, 1));
        game.Player.Inventory.PlaceIntoSlot(31, new ItemStack(cutter, 1));
        game.Player.Inventory.PlaceIntoSlot(44, new ItemStack(ItemId.WallTorch, 5));
        var floor = new GridPoint(31, 20);
        var pick = game.SmartSelectSlot(floor);
        var air = game.SmartSelectSlot(new GridPoint(31, 12));
        var far = game.SmartSelectSlot(new GridPoint(70, 20));
        game.World.SetTile(new GridPoint(32, 19), TileId.Cactus);
        var cactus = game.SmartSelectSlot(new GridPoint(32, 19));
        check(pick == 30 && air == 44 && far == 44 && cactus == 31,
            "Shift auto-select searches all 50 slots: a drill for a block in reach, a cutter for a cactus or tree, and a torch over air or out of reach");
    }

    // ------------------------------------------------------------------ keys and saved profiles

    private static void KeysAndProfiles(Action<bool, string> check)
    {
        var defaults = InputProfile.CreateDefault();
        bool Keys(string action, params string[] keys) => defaults.Bindings.TryGetValue(action, out var bound) && bound.SequenceEqual(keys);
        check(Keys("Loadout1", "F1") && Keys("Loadout2", "F2") && Keys("Loadout3", "F3") && Keys("ZoomIn", "OemPlus") &&
              Keys("ZoomOut", "OemMinus") && Keys("MinimapZoomIn", "Add") && Keys("MinimapZoomOut", "Subtract") &&
              Keys("MapAlphaUp", "PageUp") && Keys("MapAlphaDown", "PageDown") && Keys("HideUi", "F11") && Keys("Creative", "C") &&
              Keys("ThrowItem", "T") && ControlCatalog.Find("Guide") is null,
            "default keys are Terraria 1.4.5.8's (PlayerInput.Reset): loadouts F1-F3, zoom on = and -, minimap zoom on the numpad, map opacity on PageUp/PageDown, F11 hides the UI, C creative; T throws; the recipe guide has no key");

        var old = new InputProfile
        {
            MenuBindingVersion = 4,
            Bindings = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Guide"] = ["V"], ["MinimapZoomIn"] = ["Add", "OemPlus"], ["MinimapZoomOut"] = ["Subtract", "OemMinus"],
                ["Loadout1"] = [], ["Loadout2"] = [], ["Loadout3"] = [], ["Sleep"] = ["F3"], ["Jump"] = ["W"]
            }
        };
        old.Normalize();
        check(!old.Bindings.ContainsKey("Guide") && old.Bindings["MinimapZoomIn"].SequenceEqual(["Add"]) &&
              old.Bindings["MinimapZoomOut"].SequenceEqual(["Subtract"]) && old.Bindings["Loadout1"].SequenceEqual(["F1"]) &&
              old.Bindings["Loadout3"].Count == 0 && old.Bindings["Sleep"].SequenceEqual(["F3"]) && old.Bindings["Jump"].SequenceEqual(["W"]) &&
              old.Bindings["ZoomIn"].SequenceEqual(["OemPlus"]) && old.MenuBindingVersion == InputProfile.CurrentMenuBindingVersion,
            "saved profiles migrate: the recipe-guide binding goes, = and - leave minimap zoom for camera zoom, unbound loadouts take free F-keys, the player's own keys stay");
    }

    // ------------------------------------------------------------------ chest names

    private static void ChestNames(Action<bool, string> check)
    {
        var game = NewGame(7306);
        var chestTile = ContentTiles.Definitions.First(definition => (definition.Traits & TileTraits.Container) != 0 &&
                                                                     (definition.Traits & TileTraits.Locked) == 0 &&
                                                                     definition.Id is not (TileId.CoastalHabPod or TileId.FungalHabRuin)).Id;
        var cell = new GridPoint(36, 19);
        game.World.SetTile(cell, chestTile);
        var named = game.SetChestName(cell, "  Ore and more, sorted by tier  ");
        var restored = GameSimulation.Restore(JsonSerializer.Deserialize<GameSnapshot>(JsonSerializer.Serialize(game.CaptureSnapshot(), Json), Json)!,
            game.PlayerProfile);
        var notChest = game.SetChestName(new GridPoint(37, 12), "air");
        check(named && !notChest && restored.GetChestName(cell) == "Ore and more, sorted" &&
              game.GetChestName(cell)!.Length == GameSimulation.ChestNameMaxLength,
            "chests can be renamed (trimmed to Terraria's 20 characters), only chests, and the name survives a save");
        var hub = new InventoryUiState(game.Player.Inventory, chest: game.GetContainer(cell), game: game, chestCell: cell);
        check(hub.DescendantsAndSelf().Any(element => element.Id == "chest-rename"), "an open chest shows Terraria's Rename button");
    }

    // ------------------------------------------------------------------ the player never acts on its own

    private static void NoAutonomousPlayer(Action<bool, string> check)
    {
        var game = NewGame(7307);
        ClearInventory(game);
        game.Player.Inventory.Add(ItemId.Berry, 5);
        game.Player.RestorePersistentState(game.Player.MaximumHitPoints * 0.5f, foodReserve: 0.15f, restReserve: 0.15f);
        var foodBefore = game.Player.Needs.FoodReserve;
        var hpBefore = game.Player.HitPoints;
        var allowed = 0f;
        for (var i = 0; i < 1200; i++)
        {
            allowed += game.NaturalLifeRegenPerSecond / 60f;
            Tick(game);
        }
        check(game.Player.Brain is null && game.Player.Inventory.Count(ItemId.Berry) == 5 && game.Player.Needs.FoodReserve <= foodBefore &&
              game.AutonomousActivity.Activity == ActivityKind.Idle && !game.Player.Health.IsResting,
            "a hungry player with food in the bag does not eat, rest or work on their own (Terraria's player never acts by itself)");
        check(game.Player.Stats.LifeRegenerationPerHour == 0f && game.Player.HitPoints - hpBefore <= allowed + 0.05f && game.Player.HitPoints > hpBefore,
            "the player's health comes back only by Terraria's natural regeneration formula, nothing more");

        // Crew medics tend each other, never the player (no free healing for Terraria's player).
        var colony = NewGame(7308);
        var addWanderer = typeof(GameSimulation).GetMethod("AddWanderer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        ColonistAgent? medic = null;
        for (var index = 0; index < 24 && medic is null; index++)
        {
            var agent = (ColonistAgent)addWanderer.Invoke(colony, [index, colony.Player.Motion.Position])!;
            if (agent.ColonistStats.Level(Rimtera.Characters.ColonistSkill.Medical) >= 1)
                medic = agent;
        }
        colony.Player.TakeDamage(18f, DamageKind.Cut);
        var woundedNeedsTending = colony.Player.Health.NeedsTending;
        var hpAfterHit = colony.Player.HitPoints;
        var regenAllowed = 0f;
        for (var i = 0; i < 2400; i++)
        {
            foreach (var agent in colony.Colonists)
            {
                agent.Motion.Position = colony.Player.Motion.Position + new Vector2(1.5f, 0f);
                agent.Motion.Velocity = Vector2.Zero;
            }
            regenAllowed += colony.NaturalLifeRegenPerSecond / 60f;
            Tick(colony);
        }
        check(medic is not null && woundedNeedsTending && colony.Player.Health.NeedsTending &&
              colony.Player.HitPoints - hpAfterHit <= regenAllowed + 0.05f,
            "a crew medic standing beside the wounded player never tends them, and their health still only follows Terraria's natural regen");
    }
}
