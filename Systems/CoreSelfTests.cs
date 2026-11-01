using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

public static class CoreSelfTests
{
    public static int Run()
    {
        // The checks were written against the 250-second cycle; the real clock has its own checks (ModesSelfTests).
        GameSimulation.FastClock = true;
        var passed = 0;
        void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException($"Self-test failed: {name}");
            passed++;
            Console.WriteLine($"PASS {name}");
        }

        var itemCatalog = new ItemCatalog();
        var inventory = new Inventory(itemCatalog, slotCount: 4);
        // Stacks hold 9999 since 0.1.9 (IC-19).
        Check(inventory.Add(ItemId.Arrow, 10000) == 10000, "inventory accepts and splits stacks");
        Check(inventory.Count(ItemId.Arrow) == 10000 && inventory.Slots.Count(stack => stack.Count == 9999) == 1,
            "inventory stack limits");
        Check(inventory.TryConsume(ItemId.Arrow, 7) && inventory.Count(ItemId.Arrow) == 9993,
            "inventory consumes across stacks");

        var tiles = new TileCatalog();
        var grid = new TileGrid(8, 7);
        for (var x = 0; x < grid.Width; x++)
            grid.SetTile(new GridPoint(x, 5), TileId.Rock);
        var placer = new PlacementSystem();
        var placed = placer.TryPlaceTile(grid, tiles, new GridPoint(2, 4),
            new PlacementDefinition(TileId.Wood, Width: 2, AnchorRule: PlacementAnchorRule.SolidBelow));
        Check(placed.Success && placed.ChangedCells.Count == 2, "multi-cell placement validates and commits atomically");

        var frameWorld = new TileGrid(3, 3);
        for (var y = 0; y < frameWorld.Height; y++)
        for (var x = 0; x < frameWorld.Width; x++)
            frameWorld.SetTile(new GridPoint(x, y), TileId.Rock);
        frameWorld.SetTile(new GridPoint(0, 0), TileId.Air);
        var framing = new TileFramingSystem();
        framing.ReframeAll(frameWorld, tiles);
        var framedCenter = frameWorld.GetCell(new GridPoint(1, 1));
        Check(framedCenter.FrameMask == 127 && framedCenter.FrameX == 240 && framedCenter.FrameY == 112,
            "8-neighbor bitmask maps into the configured 16x16 frame atlas");

        var constrainedWorld = new TileGrid(7, 5);
        constrainedWorld.SetTile(new GridPoint(1, 1), TileId.Rock);
        var constrainedPlacement = placer.TryPlaceTile(constrainedWorld, tiles, new GridPoint(3, 1),
            new PlacementDefinition(TileId.Wood, AnchorRule: PlacementAnchorRule.None),
            new PlacementActorContext(new Vector2(0.5f, 1.5f), 6f));
        Check(constrainedPlacement.Failure == PlacementFailureReason.BlockedLineOfSight,
            "placement rejects a blocked tile ray");
        constrainedPlacement = placer.TryPlaceTile(constrainedWorld, tiles, new GridPoint(3, 1),
            new PlacementDefinition(TileId.Wood, AnchorRule: PlacementAnchorRule.None),
            new PlacementActorContext(new Vector2(0.5f, 1.5f), 2f, RequireLineOfSight: false));
        Check(constrainedPlacement.Failure == PlacementFailureReason.OutOfReach,
            "placement enforces squared Euclidean reach");

        var replaceWorld = new TileGrid(5, 5);
        replaceWorld.SetTile(new GridPoint(2, 2), TileId.Rock);
        var replacementRules = new TileReplacementRules(new HashSet<TileId> { TileId.Rock }, RequiredToolPower: 30);
        var replacementActor = new PlacementActorContext(new Vector2(1.5f, 2.5f), 4f);
        var replacement = placer.TryReplaceTile(replaceWorld, tiles, new GridPoint(2, 2), TileId.Soil,
            replacementRules, toolPower: 20, replacementActor);
        Check(replacement.Failure == PlacementFailureReason.InsufficientToolPower,
            "tile replacement gates on tool power and allow-list");
        replacement = placer.TryReplaceTile(replaceWorld, tiles, new GridPoint(2, 2), TileId.Soil,
            replacementRules, toolPower: 38, replacementActor);
        Check(replacement.Success && replacement.PreviousTile == TileId.Rock &&
              replaceWorld.GetCell(new GridPoint(2, 2)).TileId == TileId.Soil,
            "successful replacement returns its previous tile for explicit drop handling");

        grid.TryAddLiquid(new GridPoint(4, 3), LiquidKind.Water, 0.8f, tiles);
        var before = grid.TotalLiquidDepth();
        LiquidSettler.Settle(grid, tiles, maxRounds: 16);
        Check(Math.Abs(grid.TotalLiquidDepth() - before) < 0.001,
            "liquid relaxation conserves volume");

        var game = new GameSimulation(seed: 918273, width: 80, height: 48) { DebugSummonKeyEnabled = true }; // BO-18: the key is a --kit tool
        var pickSlot = game.Player.Inventory.FindSlot(ItemId.CopperPickaxe); // the starting ferrite drill
        game.TrySelectInventorySlot(pickSlot);
        var center = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
        var target = FindReachableSolid(game, center);
        var beforeDamage = game.World.GetCell(target).Damage;
        game.Tick(new GameInput(new MovementInput(0f), PlayerAction.Mine, target));
        var after = game.World.GetCell(target);
        Check(after.Damage > beforeDamage || after.TileId == TileId.Air,
            "integrated player mining damages or breaks a reachable tile");
        Check(game.TrySummonSentinel() && game.Enemies.Any(enemy => enemy.IsBoss),
            "boss actor enters the simulation entity set");
        game.Tick(new GameInput(new MovementInput(0f)));
        Check(game.TickNumber == 2 && float.IsFinite(game.Player.Motion.Position.X),
            "fixed-step game simulation advances integrated actors");

        var builder = new GameSimulation(seed: 1234, width: 80, height: 48);
        builder.Player.Inventory.Add(ItemId.SoilBlock, 50);
        var soilSlot = builder.Player.Inventory.FindSlot(ItemId.SoilBlock);
        builder.TrySelectInventorySlot(soilSlot);
        var buildCenter = builder.Player.Motion.Position + builder.Player.Motion.Size / 2f;
        var buildTarget = new GridPoint((int)MathF.Floor(buildCenter.X), Math.Max(0, (int)MathF.Floor(buildCenter.Y) - 2));
        builder.World.SetWall(buildTarget, WallIds.Plank); // parity B6: blocks need a neighbouring block or a wall behind them
        var blocksBefore = builder.Player.Inventory.Count(ItemId.SoilBlock);
        builder.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, buildTarget));
        Check(builder.World.GetCell(buildTarget).TileId == TileId.Soil &&
              builder.Player.Inventory.Count(ItemId.SoilBlock) == blocksBefore - 1,
            "selected block placement updates world and consumes inventory");

        var archer = new GameSimulation(seed: 4567, width: 80, height: 48);
        archer.Player.Inventory.Add(ItemId.CopperBow, 1);
        archer.Player.Inventory.Add(ItemId.Arrow, 80);
        archer.TrySelectInventorySlot(archer.Player.Inventory.FindSlot(ItemId.CopperBow));
        var arrowsBefore = archer.Player.Inventory.Count(ItemId.Arrow);
        var aim = new GridPoint(Math.Min(archer.World.Width - 1, (int)archer.Player.Motion.Position.X + 10),
            Math.Max(0, (int)archer.Player.Motion.Position.Y));
        archer.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, aim));
        Check(archer.Player.Inventory.Count(ItemId.Arrow) == arrowsBefore - 1 && archer.Projectiles.Count == 1,
            "ranged item consumes ammunition and creates a projectile entity");

        // EV-2 (D-28): the legacy EventManager is retired; WorldEventSystem's modifiers feed the spawner (BossSelfTests).
        var eventSystem = new WorldEventSystem(seed: 7);
        Check(Math.Abs(eventSystem.Modifiers.Spawn - 1f) < 0.0001f && eventSystem.Spec(WorldEventKind.RustMoon).SpawnMultiplier > 1f,
            "event start modifiers are exposed to the spawn pipeline");

        var spawner = new GameSimulation(seed: 48271, width: 160, height: 72);
        for (var i = 0; i < 600; i++)
            spawner.Tick(new GameInput(new MovementInput(0f)));
        Check(spawner.Enemies.Count > 0, "off-screen spawn scheduler populates the world over time");

        var craftingItems = new ItemCatalog();
        var crafting = new CraftingSystem(craftingItems);
        var craftingInventory = new Inventory(craftingItems, slotCount: 8);
        craftingInventory.Add(ItemId.RockBlock, 20);
        craftingInventory.Add(ItemId.WoodBlock, 5);
        var craftingWorld = new TileGrid(10, 10);
        var craftingTiles = new TileCatalog();
        craftingWorld.SetTile(new GridPoint(5, 5), TileId.Workbench);
        var farEnvironment = new CraftingEnvironment(new Vector2(0.5f, 0.5f), craftingWorld, craftingTiles, StationRadiusTiles: 2f);
        var craftAttempt = crafting.TryCraft("furnace", craftingInventory, farEnvironment);
        Check(!craftAttempt.Success && craftingInventory.Count(ItemId.RockBlock) == 20,
            "station radius failure leaves ingredients untouched");
        var nearEnvironment = farEnvironment with { PlayerPosition = new Vector2(4.5f, 5.5f) };
        craftAttempt = crafting.TryCraft("furnace", craftingInventory, nearEnvironment);
        Check(craftAttempt.Success && craftingInventory.Count(ItemId.FurnaceBlock) == 1 &&
              craftingInventory.Count(ItemId.RockBlock) == 0,
            "recipe index checks station proximity and consumes ingredients atomically");

        var guideCrafting = new CraftingSystem(craftingItems);
        var guideInventory = new Inventory(craftingItems, slotCount: 8);
        guideInventory.Add(ItemId.CopperBar, 8);
        guideInventory.Add(ItemId.WoodBlock, 10);
        var anvilWorld = new TileGrid(8, 8);
        anvilWorld.SetTile(new GridPoint(4, 4), TileId.Anvil);
        var anvilEnvironment = new CraftingEnvironment(new Vector2(4.5f, 3.5f), anvilWorld, craftingTiles);
        Check(!guideCrafting.TryCraft("copper-pickaxe", guideInventory, anvilEnvironment).Success,
            "guide-gated recipe is locked before consultation");
        Check(guideCrafting.ConsultGuide(guideInventory).Contains("copper-pickaxe") &&
              guideCrafting.TryCraft("copper-pickaxe", guideInventory, anvilEnvironment).Success,
            "guide material unlocks the recipe and enables crafting");

        var exchangeInventory = new Inventory(craftingItems, slotCount: 1);
        exchangeInventory.Add(ItemId.WoodBlock, 10);
        Check(exchangeInventory.TryApplyTransaction(
                  new Dictionary<ItemId, int> { [ItemId.WoodBlock] = 10 }, ItemId.WorkbenchBlock, 1) &&
              exchangeInventory.Count(ItemId.WorkbenchBlock) == 1 && exchangeInventory.Count(ItemId.WoodBlock) == 0,
            "inventory transaction can consume inputs before checking output capacity");

        var integrated = new GameSimulation(seed: 1234, width: 80, height: 48);
        integrated.Player.Inventory.Add(ItemId.WorkbenchBlock, 1);
        integrated.Player.Inventory.Add(ItemId.SoilBlock, 50);
        integrated.Player.Inventory.Add(ItemId.WoodBlock, 30);
        integrated.Player.Inventory.Add(ItemId.RockBlock, 50);
        var integratedCenter = integrated.Player.Motion.Position + integrated.Player.Motion.Size / 2f;
        var stationPoint = new GridPoint((int)MathF.Floor(integratedCenter.X), (int)MathF.Floor(integratedCenter.Y) - 2);
        Check(integrated.World.GetCell(stationPoint).TileId == TileId.Air,
            "integration fixture has clear space above the player");
        var adjacentStationPoint = new GridPoint(stationPoint.X + 1, stationPoint.Y);
        integrated.World.SetTile(adjacentStationPoint, TileId.Workbench);
        integrated.TrySelectInventorySlot(integrated.Player.Inventory.FindSlot(ItemId.WorkbenchBlock));
        integrated.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, stationPoint));
        var selectedRecipe = integrated.CycleCraftableRecipe();
        var furnaceResult = integrated.TryCraftNext();
        Check(integrated.World.GetCell(stationPoint).TileId == TileId.Workbench &&
              integrated.World.GetCell(stationPoint).FrameMask == 4 &&
              integrated.World.GetCell(stationPoint).FrameX == 64 &&
              selectedRecipe?.Id == "furnace" && furnaceResult.Success &&
              integrated.Player.Inventory.Count(ItemId.FurnaceBlock) == 1,
            "placement reframes adjacent tiles and crafting consumes a nearby physical station");

        integrated.Player.Inventory.Add(ItemId.CopperBar, 1);
        Check(integrated.ConsultGuide().Contains("copper-pickaxe"),
            "nearby guide NPC unlocks recipes from carried materials in the integrated simulation");

        integrated.Player.Inventory.Add(ItemId.CopperPickaxe, 1);
        integrated.TrySelectInventorySlot(integrated.Player.Inventory.FindSlot(ItemId.SoilBlock));
        // Tool parity: a block now takes the reference's 10-tick use time to place, so wait out the whole cooldown.
        for (var i = 0; i < 6 || integrated.Player.ActionCooldownTicks > 0 && i < 60; i++)
            integrated.Tick(new GameInput(new MovementInput(0f)));
        integrated.Tick(new GameInput(new MovementInput(0f), PlayerAction.ReplaceSelected, stationPoint));
        Check(integrated.World.GetCell(stationPoint).TileId == TileId.Soil &&
              integrated.World.GetCell(adjacentStationPoint).FrameMask == 0 &&
              integrated.Player.Inventory.Count(ItemId.WorkbenchBlock) == 1,
            "integrated tool-gated replacement reframes the tile and returns its configured drop");

        var uiItems = new ItemCatalog();
        var uiInventory = new Inventory(uiItems, slotCount: 6);
        uiInventory.Add(ItemId.RockBlock, 20);
        uiInventory.Add(ItemId.WoodBlock, 6);
        var inventoryUi = new InventoryUiController();
        // Terraria's right click (ItemSlot.RightClick) takes ONE item onto the cursor, and with that item already held pulls
        // more; it never splits the stack in half or drops items into the slot. (These checks used to encode the older
        // half-split / place-one behaviour.)
        var tookOne = inventoryUi.RightClick(uiInventory, 0);
        Check(tookOne == 1 && inventoryUi.CursorStack.Count == 1 && uiInventory.Slots[0].Count == 19,
            "inventory UI right click takes one item onto the cursor (Terraria's stack split)");
        var pulled = inventoryUi.RightClick(uiInventory, 0, 4);
        var refused = inventoryUi.RightClick(uiInventory, 1);
        Check(pulled == 4 && inventoryUi.CursorStack.Count == 5 && uiInventory.Slots[0].Count == 15 && refused == 0 && uiInventory.Slots[1].Count == 6,
            "inventory UI right click with the same item held pulls more onto the cursor, and a different item is left alone");
        inventoryUi.LeftClick(uiInventory, 2);
        Check(inventoryUi.CursorStack.IsEmpty && uiInventory.Slots[2] == new ItemStack(ItemId.RockBlock, 5),
            "inventory UI places the held stack into an empty slot");
        inventoryUi.LeftClick(uiInventory, 1);
        inventoryUi.LeftClick(uiInventory, 0);
        Check(uiInventory.Slots[0].ItemId == ItemId.WoodBlock && inventoryUi.CursorStack.ItemId == ItemId.RockBlock,
            "inventory UI swaps incompatible slot and cursor stacks without loss");

        var ui = new UserInterface();
        ui.SetViewport(800, 600);
        var testState = new UiState("pointer-test", style: new UiStyle { Width = UiLength.Pct(1f), Height = UiLength.Pct(1f) });
        var clickCount = 0;
        var doubleClickCount = 0;
        var testButton = new UiButtonElement("test", "test-button", new UiStyle
        {
            Left = UiLength.Px(30), Top = UiLength.Px(40), Width = UiLength.Px(120), Height = UiLength.Px(44)
        });
        testButton.OnClick(_ => clickCount++);
        testButton.OnDoubleClick(_ => doubleClickCount++);
        testState.Add(testButton);
        ui.SetState(testState);
        var clickPoint = new System.Numerics.Vector2(50, 60);
        ui.Update(new UiInputFrame(clickPoint, LeftDown: true, Elapsed: TimeSpan.FromMilliseconds(10)));
        ui.Update(new UiInputFrame(clickPoint, LeftDown: false, Elapsed: TimeSpan.FromMilliseconds(10)));
        ui.Update(new UiInputFrame(clickPoint, LeftDown: true, Elapsed: TimeSpan.FromMilliseconds(10)));
        ui.Update(new UiInputFrame(clickPoint, LeftDown: false, Elapsed: TimeSpan.FromMilliseconds(10)));
        Check(clickCount == 2 && doubleClickCount == 1 && testButton.IsFocused,
            "retained UI performs rectangle hit-testing, click dispatch, double-click timing, and focus");
        var defaultBindings = InputProfile.CreateDefault();
        Check(InputBindingResolver.IsBound(defaultBindings, "Jump", ConsoleKey.Spacebar) &&
              InputBindingResolver.IsBound(defaultBindings, "Hotbar10", ConsoleKey.D0),
            "input profile resolves alternate keyboard bindings and hotbar key aliases");
        bool Keys(string action, params string[] keys) => defaultBindings.Bindings[action].SequenceEqual(keys);
        Check(Keys("MoveUp", "W") && Keys("MoveLeft", "A") && Keys("MoveDown", "S") && Keys("MoveRight", "D") &&
              Keys("Jump", "Spacebar") && Keys("Inventory", "Escape", "I") && Keys("Grapple", "E") && Keys("ThrowItem", "T") &&
              Keys("SmartSelect", "Shift") && Keys("SmartCursor", "Ctrl") && Keys("Mount", "R") && Keys("QuickHeal", "H") &&
              Keys("QuickCharge", "J") && Keys("QuickBuff", "B") && Keys("Map", "M") && Keys("MapStyle", "Tab") &&
              Keys("Dash") && Keys("Interact") && Keys("UseSelected"),
            "default controls follow Terraria's PC layout (WASD, Space jump, Esc inventory, E grapple, T throw, Shift/Ctrl smart select/cursor, R mount, H/J/B quick use, M/Tab map; dash on double-tap, mouse for use and interact)");
        var gameplayKeys = ControlCatalog.All.Where(action => !action.IsMenu)
            .SelectMany(action => action.Defaults.Select(key => (action.Id, Code: InputBindingResolver.TryParseKeyCode(key, out var code) ? code : -1)))
            .ToList();
        Check(gameplayKeys.All(entry => entry.Code > 0) &&
              gameplayKeys.GroupBy(entry => entry.Code).All(group => group.Count() == 1) &&
              ControlCatalog.All.All(action => defaultBindings.Bindings.ContainsKey(action.Id)),
            $"every control has parseable defaults and no two gameplay actions share a default key ({gameplayKeys.Count} keys, {ControlCatalog.All.Length} actions)");
        var conflictProfile = InputProfile.CreateDefault();
        var displaced = conflictProfile.Rebind("Grapple", "H");
        var menuSide = conflictProfile.Rebind("MenuConfirm", "E");
        Check(displaced == "QuickHeal" && conflictProfile.Bindings["QuickHeal"].Count == 0 &&
              conflictProfile.Bindings["Grapple"].SequenceEqual(["H"]) &&
              menuSide is null && conflictProfile.Bindings["MenuConfirm"][0] == "E" && conflictProfile.Bindings["Grapple"].SequenceEqual(["H"]),
            "rebinding moves a key off the gameplay action that had it (and reports it), while menu keys live in their own context");
        Check(InputBindingResolver.TryParseKeyCode("Shift", out var shiftCode) && shiftCode == 16 &&
              InputBindingResolver.TryParseKeyCode("Ctrl", out var ctrlCode) && ctrlCode == 17 &&
              InputBindingResolver.NameForKeyCode(161) == "Shift" && InputBindingResolver.NameForKeyCode(0x4A) == "J" &&
              InputBindingResolver.Describe("Oem4") == "[" && InputBindingResolver.Describe("D7") == "7" &&
              InputBindingResolver.Describe("Spacebar") == "Space" && defaultBindings.KeyLabel("Inventory") == "Esc / I",
            "bindings cover Shift/Ctrl/Alt and read on screen as Space, 7, [, Esc / I");
        var hints = ControlCatalog.HintLines(defaultBindings);
        Check(hints[0].StartsWith("WASD move") && hints[0].Contains("Space jump") && hints[1].Contains("Esc inventory") &&
              hints[1].Contains("H heal"),
            "the in-game key help is built from the bindings (" + hints[0][..24] + "…)");
        var oldProfile = new InputProfile
        {
            MenuBindingVersion = 2,
            Bindings = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Jump"] = ["W", "Spacebar", "UpArrow"], ["Mine"] = ["M"], ["Quit"] = ["Q"], ["SummonBoss"] = ["B"],
                ["Colonist"] = ["J"], ["Inventory"] = ["I"], ["MenuUp"] = ["UpArrow"]
            }
        };
        oldProfile.Normalize();
        Check(oldProfile.MenuBindingVersion == InputProfile.CurrentMenuBindingVersion &&
              oldProfile.Bindings["Jump"].SequenceEqual(["Spacebar"]) && !oldProfile.Bindings.ContainsKey("Mine") &&
              !oldProfile.Bindings.ContainsKey("Quit") && oldProfile.Bindings["SummonBoss"].Count == 0 &&
              oldProfile.Bindings["Inventory"].SequenceEqual(["Escape", "I"]) && oldProfile.Bindings["Colonist"].SequenceEqual(["J"]) &&
              oldProfile.Bindings["Grapple"].SequenceEqual(["E"]),
            "saved profiles move to the new layout: untouched old defaults take the new ones, a player's own rebinds stay, dead actions go");
        var v3Profile = new InputProfile
        {
            MenuBindingVersion = 3,
            Bindings = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Colonist"] = ["K"], ["Bestiary"] = ["N"], ["Sleep"] = ["Y"], ["Craft"] = ["C"]
            }
        };
        v3Profile.Normalize();
        // The recipe guide no longer has an action at all (reached from the hub button and the station AI), so it is not
        // in this list any more; an old profile's guide binding is dropped (ControlsParitySelfTests).
        Check(new[] { "Colonist", "Bestiary", "Achievements", "Craft", "CraftPrevious", "CraftNext", "Sleep", "CheckHousing", "WireLayer", "Eat" }
                  .All(action => defaultBindings.Bindings[action].Count == 0) && !defaultBindings.Bindings.ContainsKey("Guide") &&
              v3Profile.Bindings["Colonist"].Count == 0 && v3Profile.Bindings["Bestiary"].Count == 0 &&
              v3Profile.Bindings["Craft"].Count == 0 && v3Profile.Bindings["Sleep"].SequenceEqual(["Y"]),
            "panel keys start unbound (their panels live in the inventory, beds and the station AI answer right clicks); a saved profile drops the old ones unless the player changed them");
        Check(defaultBindings.Bindings["MenuUp"].SequenceEqual(["UpArrow"]) &&
              defaultBindings.Bindings["MenuDown"].SequenceEqual(["DownArrow"]) &&
              defaultBindings.Bindings["MenuLeft"].SequenceEqual(["LeftArrow"]) &&
              defaultBindings.Bindings["MenuRight"].SequenceEqual(["RightArrow"]) &&
              defaultBindings.Bindings["MenuUp"].Concat(defaultBindings.Bindings["MenuDown"])
                  .Concat(defaultBindings.Bindings["MenuLeft"]).Concat(defaultBindings.Bindings["MenuRight"])
                  .All(key => key is not ("W" or "A" or "S" or "D")),
            "menu navigation defaults to the arrow keys while WASD stays on gameplay");
        var legacyBindings = new InputProfile
        {
            MenuBindingVersion = 0,
            Bindings = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["MenuUp"] = ["UpArrow", "W"],
                ["MenuDown"] = ["S"],
                ["MenuLeft"] = ["A"],
                ["MenuRight"] = ["RightArrow", "D"],
                ["Jump"] = ["Spacebar"],
                ["MoveLeft"] = ["A"]
            }
        };
        legacyBindings.Normalize();
        Check(legacyBindings.Bindings["MenuUp"].SequenceEqual(["UpArrow"]) &&
              legacyBindings.Bindings["MenuDown"].SequenceEqual(["DownArrow"]) &&
              legacyBindings.Bindings["MenuLeft"].SequenceEqual(["LeftArrow"]) &&
              legacyBindings.Bindings["MenuRight"].SequenceEqual(["RightArrow"]) &&
              legacyBindings.Bindings["MoveLeft"].SequenceEqual(["A"]) &&
              legacyBindings.MenuBindingVersion == InputProfile.CurrentMenuBindingVersion,
            "legacy input profiles migrate menu navigation off WASD without touching gameplay keys");
        var triggers = new InputTriggerSet();
        triggers.Update(["Jump"]);
        var pressedOnce = triggers.Pressed("Jump") && triggers.IsDown("Jump");
        triggers.Update(Array.Empty<string>());
        Check(pressedOnce && triggers.Released("Jump") && !triggers.IsDown("Jump"),
            "input trigger state reports current, just-pressed, and just-released transitions");

        Check(DisplayOptions.StandardSizes.Length == 5 &&
              DisplayOptions.StandardSizes[0] == (1024, 768) &&
              DisplayOptions.StandardSizes[^1] == (1920, 1080),
            "display options expose five standard window sizes from 1024x768 to 1920x1080");
        Check(DisplayOptions.CycleMode(WindowMode.Windowed, 1) == WindowMode.Borderless &&
              DisplayOptions.CycleMode(WindowMode.Borderless, 1) == WindowMode.Fullscreen &&
              DisplayOptions.CycleMode(WindowMode.Fullscreen, 1) == WindowMode.Windowed &&
              DisplayOptions.CycleMode(WindowMode.Windowed, -1) == WindowMode.Fullscreen,
            "window mode cycles through windowed, borderless fullscreen, and fullscreen");
        Check(DisplayOptions.NearestSizeIndex(1280, 720) == 1 &&
              DisplayOptions.NearestSizeIndex(1918, 1078) == 4 &&
              DisplayOptions.NearestSizeIndex(1020, 770) == 0,
            "window size selection snaps to the nearest standard size");
        var defaultDisplay = new GamePreferences();
        defaultDisplay.Normalize();
        var legacyFullscreen = new GamePreferences { ScreenMode = WindowMode.Windowed, Fullscreen = true };
        legacyFullscreen.Normalize();
        var legacyBorderless = new GamePreferences { ScreenMode = WindowMode.Windowed, WindowBorderless = true };
        legacyBorderless.Normalize();
        var savedBorderless = new GamePreferences { ScreenMode = WindowMode.Borderless };
        savedBorderless.Normalize();
        Check(defaultDisplay.ScreenMode == WindowMode.Windowed &&
              defaultDisplay.DisplayWidth == 1280 && defaultDisplay.DisplayHeight == 720 &&
              legacyFullscreen.ScreenMode == WindowMode.Fullscreen &&
              legacyBorderless.ScreenMode == WindowMode.Borderless &&
              savedBorderless.ScreenMode == WindowMode.Borderless &&
              !savedBorderless.Fullscreen && savedBorderless.WindowBorderless,
            "display preferences start windowed and migrate legacy fullscreen flags");

        var oddAutosave = new GamePreferences { AutosaveMinutes = 7 };
        oddAutosave.Normalize();
        var fiveMinutes = new GamePreferences { AutosaveMinutes = 5 };
        fiveMinutes.Normalize();
        Check(new GamePreferences().AutosaveMinutes == 10 && oddAutosave.AutosaveMinutes == 10 && fiveMinutes.AutosaveMinutes == 5,
            "autosave runs every 10 minutes by default; 2, 5, 10 and 15 are the choices");

        var shopScreen = new MenuScreen("npc-shop", "Shop", Array.Empty<MenuEntry>(), string.Empty);
        var pauseScreen = new MenuScreen("pause-menu", "Settings", Array.Empty<MenuEntry>(), string.Empty);
        Check(MenuPauseRules.WorldRuns(shopScreen, autoPause: false) && !MenuPauseRules.WorldRuns(pauseScreen, autoPause: false) &&
              !MenuPauseRules.WorldRuns(shopScreen, autoPause: true),
            "Terraria's pause rules: with Autopause off a shop keeps the world running and the settings pause it; with it on, both pause");

        // The art gate: clean against the sprites on disk; a sprite the game needs that git does not track is reported.
        var assetRoot = AppContext.BaseDirectory;
        var onDisk = Directory.Exists(ItemAudit.SpriteDirectory(assetRoot))
            ? Directory.EnumerateFiles(ItemAudit.SpriteDirectory(assetRoot), "*.png").Select(Path.GetFileNameWithoutExtension).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleanAssets = AssetCheck.Problems(assetRoot, onDisk);
        var dropped = SpriteCatalog.ForEnemy(EnemyKind.MossSlime);
        var droppedResolved = SpriteAtlas.ResolveName(dropped, onDisk.Contains);
        var withoutOne = AssetCheck.Problems(assetRoot, onDisk.Where(name => !string.Equals(name, droppedResolved, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase));
        Check(cleanAssets.Count == 0 && withoutOne.Any(problem => problem.Contains(droppedResolved, StringComparison.OrdinalIgnoreCase)),
            $"the asset check is clean, and a needed sprite missing from git is reported ({string.Join("; ", cleanAssets.Take(3))})");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"rimtera-ui-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var profilesPath = Path.Combine(tempRoot, "profiles.json");
        var profileStore = new UserProfileStore(profilesPath);
        var savedCharacter = profileStore.CreateCharacter("UI Tester");
        var savedWorld = profileStore.CreateWorld("UI World", 123456, 240, 108);
        var reloadedProfiles = new UserProfileStore(profilesPath);
        Check(reloadedProfiles.Characters.Single().Id == savedCharacter.Id &&
              reloadedProfiles.Characters.Single().Backstory == savedCharacter.Backstory &&
              reloadedProfiles.Characters.Single().SkillLevels?.Length == 12 &&
              reloadedProfiles.Characters.Single().Passions?.Length == 12 &&
              reloadedProfiles.Worlds.Single().Seed == 123456 &&
              reloadedProfiles.Worlds.Single().Width == 240 &&
              reloadedProfiles.Worlds.Single().Height == 108,
            "character and world selection profiles round-trip through versioned JSON metadata");

        var draftA = CharacterDraft.CreateRandom(12345, "Stat Tester");
        var draftB = CharacterDraft.CreateRandom(12345, "Stat Tester");
        Check(draftA.Backstory == draftB.Backstory &&
              draftA.SkillLevels.SequenceEqual(draftB.SkillLevels) &&
              draftA.Passions.SequenceEqual(draftB.Passions) &&
              draftA.Traits.SequenceEqual(draftB.Traits),
            "seeded colonist drafts reproduce backstory, traits, skills, and passions");
        draftA.ClearTraits();
        draftA.ToggleTrait(ColonistTraitId.Optimist);
        Check(!draftA.ToggleTrait(ColonistTraitId.Pessimist),
            "opposed colonist traits are rejected");
        draftA.ClearTraits();
        draftA.ToggleTrait(ColonistTraitId.Tough);
        draftA.ToggleTrait(ColonistTraitId.Industrious);
        draftA.ToggleTrait(ColonistTraitId.GreenThumb);
        Check(!draftA.ToggleTrait(ColonistTraitId.Kind) && draftA.Traits.Count == 3,
            "colonist trait selection is capped at three");
        draftA.SetFocus(ColonistSkill.Mining);
        Check(draftA.Passions[(int)ColonistSkill.Mining] == PassionLevel.Burning &&
              draftA.SkillLevels[(int)ColonistSkill.Mining] >= 2,
            "skill focus grants a burning passion and a level edge");
        var draftProfile = draftA.ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow);
        Check(draftProfile.SkillLevels?.Length == 12 && draftProfile.Passions?.Length == 12 &&
              draftProfile.Stats.MaximumHealthBonus == 15f &&
              draftProfile.EffectiveMaximumHealth == 115f,
            "colonist profile derives skills, passions, and health from traits");
        var moodStats = draftProfile.Stats;
        var fullMood = ColonistMoodCalculator.Calculate(new Rimtera.AI.SurvivalNeeds(1f, 1f, 1f, 1f), moodStats);
        var emptyMood = ColonistMoodCalculator.Calculate(new Rimtera.AI.SurvivalNeeds(0f, 0f, 0f, 0f), moodStats);
        Check(float.IsFinite(fullMood) && fullMood > emptyMood &&
              ColonistMoodCalculator.Label(fullMood).Length > 0,
            "colonist mood responds to needs and reports a readable label");

        var menuFlow = new MenuFlow(profileStore, tempRoot);
        WorldProfile? launchRequest = null;
        menuFlow.WorldLaunchRequested += (world, _) => launchRequest = world;
        menuFlow.Open();
        Check(menuFlow.CurrentScreen.Id == "main-menu" && menuFlow.Ui.BuildDrawCommands().Any(command => command.Kind == UiDrawKind.Button),
            "main menu builds retained widgets and renderer-neutral draw commands");
        menuFlow.Close();
        var menuClosed = !menuFlow.IsOpen;
        menuFlow.Open();
        Check(menuClosed && menuFlow.IsOpen && menuFlow.CurrentScreen.Id == "main-menu",
            "start menu closes and reopens without discarding its flow");
        menuFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        menuFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        menuFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        Check(launchRequest?.Id == savedWorld.Id,
            "menu routes through character and world selection to a launch request");
        var resumedFromPause = false;
        menuFlow.ShowPauseMenu(() => resumedFromPause = true, () => { }, () => { });
        menuFlow.Handle(new UiInputFrame(default, BackPressed: true));
        Check(resumedFromPause, "pause UI resumes through Escape/back dispatch");
        menuFlow.ShowPauseMenu(
            resume: menuFlow.Close,
            openInventory: null,
            saveAndExit: () => { },
            save: () => { },
            quitToTitle: () => { },
            exitGame: () => { },
            openFeats: () => { });
        var pauseLabels = menuFlow.CurrentScreen.Entries.Select(entry => entry.Label).ToArray();
        Check(pauseLabels.SequenceEqual(new[]
              {
                  "Close Menu", "— Settings —", "General", "Interface", "Video", "Volume", "Cursor", "Controls",
                  "— Game —", "Save Game", "Save & Exit to Title", "Save & Quit Game"
              }) && menuFlow.CurrentScreen.Title == "Settings",
            "the in-game Settings menu follows Terraria: close, the settings categories, then save and the exits");
        var videoIndex = Array.IndexOf(pauseLabels, "Video");
        menuFlow.CurrentScreen.SelectIndex(videoIndex);
        menuFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        var videoPage = menuFlow.CurrentScreen.Title == "Video" &&
                        menuFlow.CurrentScreen.Entries.Any(entry => entry.Label.StartsWith("Window mode:", StringComparison.Ordinal));
        menuFlow.Handle(new UiInputFrame(default, BackPressed: true));
        Check(videoPage && menuFlow.CurrentScreen.Id == "pause-menu",
            "a settings category opens its own page and Back returns to the in-game Settings");
        menuFlow.Handle(new UiInputFrame(default, BackPressed: true));
        menuFlow.ShowPauseMenu(
            resume: menuFlow.Close,
            openInventory: () => { },
            saveAndExit: () => { },
            save: () => { },
            quitToTitle: () => { },
            exitGame: () => { },
            openColonist: () => { });
        var pauseWithColonist = menuFlow.CurrentScreen.Entries.Select(entry => entry.Label).ToArray();
        Check(pauseWithColonist[1] == "Inventory" && pauseWithColonist[2] == "Crew" &&
              pauseWithColonist[^1] == "Save & Quit Game",
            "hosts without the inventory hub (the terminal) still get Inventory and Crew in Settings");
        menuFlow.Handle(new UiInputFrame(default, BackPressed: true));
        var inventoryClosed = false;
        menuFlow.ShowInventory(new InventoryUiState(uiInventory), () => inventoryClosed = true);
        menuFlow.Handle(new UiInputFrame(default, BackPressed: true));
        Check(inventoryClosed, "inventory overlay can close back into the active simulation");

        var hubGame = new GameSimulation(seed: 5, width: 80, height: 48);
        var hubUi = new UserInterface();
        hubUi.SetViewport(1280, 720);
        hubUi.SetState(new InventoryUiState(hubGame.Player.Inventory, game: hubGame));
        Check(hubUi.IsOverInterface(new System.Numerics.Vector2(40f, 80f)) && !hubUi.IsOverInterface(new System.Numerics.Vector2(700f, 500f)),
            "the inventory covers only its panels: a click on the world beside them reaches the world, as in Terraria");

        var longMenu = new MenuScreen("long", "Long", Enumerable.Range(0, 12).Select(i => new MenuEntry($"Row {i}", () => { })));
        longMenu.SelectIndex(0);
        longMenu.MoveSelection(-1);
        var stayedAtTop = longMenu.SelectedIndex == 0;
        for (var i = 0; i < 20; i++)
            longMenu.MoveSelection(1);
        Check(stayedAtTop && longMenu.SelectedIndex == 11 && longMenu.Entries.Count == 12,
            "menus stop at their first and last rows instead of wrapping round");

        var saveGame = new GameSimulation(seed: 789, width: 80, height: 48);
        saveGame.Player.Motion.Position = new System.Numerics.Vector2(20.5f, 15f);
        saveGame.Player.Motion.Velocity = new System.Numerics.Vector2(2f, -1f);
        saveGame.Player.RestorePersistentState(73f, 0.6f, 0.4f);
        saveGame.Player.Inventory.Add(ItemId.CopperOre, 17);
        for (var i = 0; i < 9; i++) saveGame.Tick(new GameInput(new MovementInput(0f)));
        var saveRepository = new WorldSaveRepository(Path.Combine(tempRoot, "saves"));
        var savedState = saveGame.CaptureSnapshot();
        saveRepository.Save(savedWorld.Id, savedState, savedCharacter.Id, savedCharacter.Name);
        Check(saveRepository.TryLoad(savedWorld.Id, out var loadedState) && loadedState is not null,
            "world save repository writes and reads a separate versioned snapshot");
        Check(saveRepository.TryReadFile(savedWorld.Id, out var saveFile) &&
              saveFile?.CharacterId == savedCharacter.Id && saveFile.CharacterName == savedCharacter.Name,
            "world save records the character used for Continue and Load");
        var legacyState = savedState with { Inventory = savedState.Inventory.Take(24).ToArray() };
        var legacyGame = GameSimulation.Restore(legacyState);
        Check(legacyGame.Player.Inventory.SlotCount == GameSimulation.PlayerTotalSlots &&
              legacyGame.Player.Inventory.BagSlots == GameSimulation.PlayerInventorySlots &&
              legacyGame.Player.Inventory.Slots.Take(24).SequenceEqual(savedState.Inventory.Take(24)) &&
              legacyGame.Player.Inventory.Slots.Skip(24).All(stack => stack.IsEmpty),
            "saves from the old 24-slot inventory load into the 50-slot one, items in place and the new slots empty");
        var resumedGame = GameSimulation.Restore(loadedState!);
        Check(resumedGame.World.Width == saveGame.World.Width && resumedGame.TickNumber == saveGame.TickNumber &&
              resumedGame.Player.HitPoints == saveGame.Player.HitPoints &&
              resumedGame.Player.Inventory.Count(ItemId.CopperOre) == saveGame.Player.Inventory.Count(ItemId.CopperOre) &&
              resumedGame.Player.Motion.Velocity == saveGame.Player.Motion.Velocity,
            "world save restores grid, clock, player vitals, motion, and inventory state");

        // Disk saves keep the cells out of the JSON: a raw deflate sidecar next to a small envelope.
        var sidecarWorld = Guid.NewGuid();
        saveGame.World.SetCell(new GridPoint(12, 30), new WorldCell { TileId = TileId.Rock, Shape = TileShape.SlopeUpLeft });
        var sidecarSnapshot = saveGame.CaptureSnapshot(binaryCells: true);
        saveRepository.Save(sidecarWorld, sidecarSnapshot);
        // PU-11 (0.2.2): sidecars are stamped per save (<id>.<stamp>.cells), so the envelope always names a complete one.
        var sidecarPath = Directory.EnumerateFiles(Path.Combine(tempRoot, "saves"), $"{sidecarWorld:N}.*.cells").SingleOrDefault() ?? "missing";
        var envelopeText = File.ReadAllText(Path.Combine(tempRoot, "saves", $"{sidecarWorld:N}.rimtera.json"));
        var sidecarLoaded = saveRepository.TryLoad(sidecarWorld, out var sidecarState) && sidecarState?.CellData is not null;
        var sidecarGame = sidecarLoaded ? GameSimulation.Restore(sidecarState!) : null;
        Check(sidecarSnapshot.CompressedCells is null && File.Exists(sidecarPath) && !envelopeText.Contains("CellData") &&
              sidecarGame is not null &&
              // Frame masks are derived and recomputed on load; every saved field must match exactly.
              sidecarGame.World.Cells.ToArray().Select(cell => cell with { FrameMask = 0 }).SequenceEqual(saveGame.World.Cells.ToArray().Select(cell => cell with { FrameMask = 0 })) &&
              sidecarGame.World.GetCell(new GridPoint(12, 30)).Shape == TileShape.SlopeUpLeft,
            "binary cell sidecar saves and loads the exact grid without putting cells in the JSON");
        File.Delete(sidecarPath);
        Check(!saveRepository.TryLoad(sidecarWorld, out _), "a save whose cell sidecar is missing is refused instead of loading empty");

        var colonyGame = new GameSimulation(seed: 2211, width: 80, height: 48, colonist: draftProfile);
        colonyGame.AddStartingCrewForChecks();
        Check(colonyGame.Player.Colonist.Id == draftProfile.Id &&
              colonyGame.Colonists.Count == 2 &&
              colonyGame.Player.MaximumHitPoints == draftProfile.EffectiveMaximumHealth &&
              float.IsFinite(colonyGame.Player.Mood) &&
              colonyGame.Colonists.All(agent => float.IsFinite(agent.Mood)),
            "colonist stats seed the player and a generated, inspectable NPC crew");
        var colonyAgent = colonyGame.Colonists[0];
        colonyAgent.Motion.Position = new Vector2(22.5f, 16f);
        colonyAgent.Needs.SetReserves(0.44f, 0.55f, 0.33f, 0.66f);
        var colonySnapshot = colonyGame.CaptureSnapshot();
        Check(colonySnapshot.Colonists?.Length == 2 && colonySnapshot.JoyReserve is not null &&
              colonySnapshot.ComfortReserve is not null,
            "world snapshots include player joy/comfort and the colonist roster");
        var restoredColony = GameSimulation.Restore(colonySnapshot, draftProfile);
        Check(restoredColony.PlayerProfile.Id == draftProfile.Id &&
              restoredColony.Colonists.Count == 2 &&
              restoredColony.Colonists[0].Profile.Name == colonyGame.Colonists[0].Profile.Name &&
              Vector2.Distance(restoredColony.Colonists[0].Motion.Position, colonyAgent.Motion.Position) < 0.001f &&
              Math.Abs(restoredColony.Colonists[0].Needs.FoodReserve - 0.44f) < 0.001f &&
              Math.Abs(restoredColony.Colonists[0].Needs.JoyReserve - 0.33f) < 0.001f,
            "restoring a snapshot preserves the character sheet and colonist NPC state");
        for (var i = 0; i < 20; i++)
            restoredColony.Tick(new GameInput(new MovementInput(0f)));
        Check(float.IsFinite(restoredColony.Colonists[0].Motion.Position.X) &&
              restoredColony.Colonists.All(agent => agent.Needs.FoodReserve is >= 0f and <= 1f),
            "colonist NPCs update needs and movement inside the fixed-step simulation");

        var secondCharacter = profileStore.CreateCharacter("Second Tester");
        menuFlow.Open();
        menuFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        var secondCharacterIndex = menuFlow.CurrentScreen.Entries
            .Select((entry, index) => (entry, index))
            .Single(item => item.entry.Label.StartsWith(secondCharacter.Name, StringComparison.Ordinal)).index;
        menuFlow.CurrentScreen.SelectIndex(secondCharacterIndex);
        menuFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        var selectedSecond = menuFlow.SelectedCharacter?.Id == secondCharacter.Id;
        var savedWorldListed = menuFlow.CurrentScreen.Id == "world-select" &&
                               menuFlow.CurrentScreen.Entries.Any(entry => entry.Label.StartsWith(savedWorld.Name, StringComparison.Ordinal) &&
                                                                           entry.Label.Contains("Saved", StringComparison.Ordinal));
        CharacterProfile? continueCharacter = null;
        menuFlow.WorldLaunchRequested += (_, character) => continueCharacter = character;
        menuFlow.CurrentScreen.SelectIndex(menuFlow.CurrentScreen.Entries
            .Select((entry, index) => (entry, index))
            .First(item => item.entry.Label.StartsWith(savedWorld.Name, StringComparison.Ordinal)).index);
        menuFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        // D-43 (PU-9, 0.2.2): any character may play any world; the menu launches the chosen character (Continue alone uses the saved one).
        Check(selectedSecond && savedWorldListed && continueCharacter?.Id == secondCharacter.Id &&
              menuFlow.SelectedCharacter?.Id == secondCharacter.Id,
            "Single Player's world list marks saved worlds, and a saved world launches with the character chosen in the menu (D-43)");

        // Creative characters and worlds stay apart (Terraria's Journey split).
        var journeyHero = savedCharacter with { Id = Guid.NewGuid(), Difficulty = CharacterDifficulty.Creative };
        var journeyWorld = profileStore.CreateWorld("Journey World", 77, createdByCreative: true);
        Check(journeyWorld.CreatedByCreative && new UserProfileStore(profilesPath).Worlds.Single(world => world.Id == journeyWorld.Id).CreatedByCreative &&
              journeyWorld.Admits(journeyHero) && !journeyWorld.Admits(savedCharacter) &&
              !savedWorld.Admits(journeyHero) && savedWorld.Admits(savedCharacter) && savedWorld.Admits(journeyHero, savedBy: journeyHero.Id),
            "Creative characters play only Creative worlds and the reverse (a character may always return to a world it saved)");

        menuFlow.Open();
        Check(menuFlow.CurrentScreen.Entries.Select(entry => entry.Label).SequenceEqual(["Single Player", "Achievements", "Settings", "Credits", "Exit Game"]),
            "the title menu follows Terraria: Single Player, Achievements, Settings, Credits, Exit (PU-24)");
        menuFlow.Close();
        Check(!menuFlow.IsOpen, "start menu closes without losing its host");
        menuFlow.Open();

        var settingsIndex = menuFlow.CurrentScreen.Entries
            .Select((entry, index) => (entry, index))
            .Single(item => item.entry.Label == "Settings").index;
        menuFlow.CurrentScreen.SelectIndex(settingsIndex);
        menuFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        var settingsCategories = menuFlow.CurrentScreen.Entries.Select(entry => entry.Label).ToArray();
        menuFlow.CurrentScreen.SelectIndex(Array.IndexOf(settingsCategories, "Video"));
        menuFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        var optionLabels = menuFlow.CurrentScreen.Entries.Select(entry => entry.Label).ToArray();
        var windowModeIndex = menuFlow.CurrentScreen.Entries
            .Select((entry, index) => (entry, index))
            .Single(item => item.entry.Label.StartsWith("Window mode:", StringComparison.Ordinal)).index;
        menuFlow.CurrentScreen.SelectIndex(windowModeIndex);
        menuFlow.Handle(new UiInputFrame(default, NavigationX: 1));
        var borderlessSelected = menuFlow.Preferences.ScreenMode == WindowMode.Borderless &&
                                 menuFlow.Preferences.WindowBorderless &&
                                 !menuFlow.Preferences.Fullscreen;
        var windowSizeIndex = menuFlow.CurrentScreen.Entries
            .Select((entry, index) => (entry, index))
            .Single(item => item.entry.Label.StartsWith("Resolution:", StringComparison.Ordinal)).index;
        menuFlow.CurrentScreen.SelectIndex(windowSizeIndex);
        menuFlow.Handle(new UiInputFrame(default, NavigationX: 1));
        var sizedWindowSelected = menuFlow.Preferences.DisplayWidth == 1366 &&
                                  menuFlow.Preferences.DisplayHeight == 768;
        menuFlow.Handle(new UiInputFrame(default, BackPressed: true));
        Check(settingsCategories.SequenceEqual(["General", "Interface", "Video", "Volume", "Cursor", "Controls", "Back"]) &&
              optionLabels.Any(label => label == "Window mode: Windowed") &&
              optionLabels.Any(label => label == "Resolution: 1280×720") &&
              optionLabels.Any(label => label == "Frame skip: On") && !optionLabels.Any(label => label.StartsWith("Frame pacing", StringComparison.Ordinal)) &&
              borderlessSelected && sizedWindowSelected &&
              menuFlow.CurrentScreen.Id == "settings",
            "Settings lists Terraria's categories; Video exposes live window mode, standard resolutions and frame skip");

        var controlsIndex = menuFlow.CurrentScreen.Entries
            .Select((entry, index) => (entry, index))
            .Single(item => item.entry.Label == "Controls").index;
        menuFlow.CurrentScreen.SelectIndex(controlsIndex);
        menuFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        var colonistBindingIndex = menuFlow.CurrentScreen.Entries
            .Select((entry, index) => (entry, index))
            .Single(item => item.entry.Label.StartsWith("Crew:", StringComparison.Ordinal)).index;
        menuFlow.CurrentScreen.SelectIndex(colonistBindingIndex);
        menuFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        var bindingCapture = menuFlow.Ui.CurrentState is BindingCaptureState;
        menuFlow.CaptureBinding(ConsoleKey.J);
        Check(bindingCapture && !menuFlow.IsBindingCaptureActive &&
              menuFlow.CurrentScreen.Id == "controls" &&
              menuFlow.InputProfile.Bindings["Colonist"].SequenceEqual(["J"]),
            "controls bind keys through in-game key capture instead of a native prompt");
        var menuUpIndex = menuFlow.CurrentScreen.Entries
            .Select((entry, index) => (entry, index))
            .Single(item => item.entry.Label.StartsWith("Menu up:", StringComparison.Ordinal)).index;
        menuFlow.CurrentScreen.SelectIndex(menuUpIndex);
        menuFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        var menuCapture = menuFlow.Ui.CurrentState is BindingCaptureState;
        menuFlow.CaptureBinding(ConsoleKey.W);
        Check(menuCapture && menuFlow.CurrentScreen.Id == "controls" &&
              menuFlow.InputProfile.Bindings["MenuUp"].SequenceEqual(["UpArrow"]) &&
              menuFlow.CurrentScreen.StatusMessage.Contains("arrow keys", StringComparison.OrdinalIgnoreCase),
            "menu navigation refuses WASD bindings and keeps the arrow-key scheme");

        var creationRoot = Path.Combine(tempRoot, "creation");
        var creationFlow = new MenuFlow(dataDirectory: creationRoot);
        WorldProfile? createdWorld = null;
        CharacterProfile? createdCharacter = null;
        creationFlow.WorldLaunchRequested += (world, character) =>
        {
            createdWorld = world;
            createdCharacter = character;
        };
        creationFlow.Open();
        creationFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        creationFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        Check(creationFlow.CurrentScreen.Id == "character-create" &&
              creationFlow.CurrentScreen.Entries.Any(entry => entry.Label.StartsWith("Backstory:", StringComparison.Ordinal)) &&
              creationFlow.CurrentScreen.Entries.Any(entry => entry.Label.StartsWith("Traits (", StringComparison.Ordinal)),
            "character creation exposes backstory, traits, skills, appearance, and focus");
        creationFlow.SetCharacterName("Prompt Tester");
        var nameEntryIndex = creationFlow.CurrentScreen.Entries
            .Select((entry, index) => (entry, index))
            .Single(item => item.entry.Label.StartsWith("Name:", StringComparison.Ordinal)).index;
        creationFlow.CurrentScreen.SelectIndex(nameEntryIndex);
        creationFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        var namePrompt = creationFlow.Ui.CurrentState as TextPromptState;
        var inputCommand = creationFlow.Ui.BuildDrawCommands()
            .FirstOrDefault(command => command.Kind == UiDrawKind.Input);
        creationFlow.InsertText("!");
        creationFlow.EditText(TextEditCommand.Backspace);
        creationFlow.SubmitTextPrompt();
        Check(namePrompt is not null && inputCommand.Kind == UiDrawKind.Input && inputCommand.Focused &&
              !creationFlow.IsTextPromptActive && creationFlow.CurrentScreen.Id == "character-create" &&
              creationFlow.CharacterDraft.Name == "Prompt Tester",
            "character names use the focused in-game text field and return to the menu on submit");
        creationFlow.SetCharacterName("Menu Hero");
        var createCharacterIndex = creationFlow.CurrentScreen.Entries
            .Select((entry, index) => (entry, index))
            .Single(item => item.entry.Label == "Create").index;
        creationFlow.CurrentScreen.SelectIndex(createCharacterIndex);
        creationFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        creationFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        creationFlow.SetWorldName("Menu World");
        var sizeIndex = creationFlow.CurrentScreen.Entries
            .Select((entry, index) => (entry, index))
            .Single(item => item.entry.Label.StartsWith("Size:", StringComparison.Ordinal)).index;
        creationFlow.CurrentScreen.SelectIndex(sizeIndex);
        creationFlow.Handle(new UiInputFrame(default, NavigationX: 1));
        var mediumSizeShown = creationFlow.CurrentScreen.Entries
            .Any(entry => entry.Label.Contains("Medium", StringComparison.Ordinal));
        var hazardIndex = creationFlow.CurrentScreen.Entries
            .Select((entry, index) => (entry, index))
            .Single(item => item.entry.Label.StartsWith("Hazard:", StringComparison.Ordinal)).index;
        creationFlow.CurrentScreen.SelectIndex(hazardIndex);
        creationFlow.Handle(new UiInputFrame(default, NavigationX: 1));
        var hazardPicked = creationFlow.WorldHazardDraft;
        var createWorldIndex = creationFlow.CurrentScreen.Entries
            .Select((entry, index) => (entry, index))
            .Single(item => item.entry.Label == "Create & Play").index;
        creationFlow.CurrentScreen.SelectIndex(createWorldIndex);
        creationFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        Check(mediumSizeShown && createdCharacter?.Name == "Menu Hero" &&
              (createdCharacter?.BackstoryName.Length ?? 0) > 0 &&
              (createdCharacter?.SkillLevels?.Length ?? 0) == 12 &&
              createdWorld?.Name == "Menu World" && createdWorld.Width == 6400 && createdWorld.Height == 1800 &&
              hazardPicked != WorldHazard.Random && createdWorld.Hazard == hazardPicked,
            "menu creates a detailed named character and a sized world (with the chosen hazard) before launching it");

        creationFlow.ShowColonistSheet(createdCharacter!);
        Check(creationFlow.Ui.CurrentState is ColonistSheetState &&
              creationFlow.Ui.BuildDrawCommands().Any(command =>
                  command.Kind == UiDrawKind.Text && command.Text == "TOP SKILLS"),
            "character creation can preview the full colonist sheet");
        creationFlow.Handle(new UiInputFrame(default, BackPressed: true));
        Check(creationFlow.CurrentScreen.Id == "world-create",
            "closing the colonist preview returns to the prior creation screen");

        var liveSheetClosed = false;
        menuFlow.ShowColonistSheet(draftProfile, colonyGame, () => liveSheetClosed = true);
        Check(menuFlow.Ui.CurrentState is ColonistSheetState &&
              menuFlow.Ui.BuildDrawCommands().Count > 10,
            "in-game colonist sheet builds the live dossier and crew pages");
        menuFlow.Handle(new UiInputFrame(default, NavigationX: 1));
        menuFlow.Handle(new UiInputFrame(default, ConfirmPressed: true));
        Check(menuFlow.Ui.BuildDrawCommands().Any(command =>
                  command.Kind == UiDrawKind.Text && command.Text == "WORK SPEED"),
            "colonist sheet page tabs respond to keyboard focus and confirm");
        menuFlow.Handle(new UiInputFrame(default, BackPressed: true));
        Check(liveSheetClosed, "in-game colonist sheet closes back into the active simulation");

        Directory.Delete(tempRoot, recursive: true);

        // Neo-Terra creatures: archetype table, boss flags, and sizes.
        var enemyKinds = Enum.GetValues<EnemyKind>();
        var archetypes = EnemyArchetypes.All.ToArray();
        Check(archetypes.Length == enemyKinds.Length &&
              enemyKinds.All(kind => EnemyArchetypes.Contains(kind) &&
                  !string.IsNullOrWhiteSpace(EnemyArchetypes.Get(kind).DisplayName) &&
                  EnemyArchetypes.Get(kind).HitPoints > 0f &&
                  new EnemyAgent(1, kind, Vector2.Zero).DisplayName == EnemyArchetypes.Get(kind).DisplayName),
            "enemy archetype table covers every creature kind with a display name");
        var legacySentinel = new EnemyAgent(1, EnemyKind.StoneSentinel, Vector2.Zero);
        Check(legacySentinel.IsBoss && legacySentinel.MaximumHitPoints == 9400f && legacySentinel.AttackDamage == 26f &&
              !new EnemyAgent(2, EnemyKind.MossSlime, Vector2.Zero).IsBoss &&
              archetypes.Count(archetype => archetype.IsBoss && archetype.Kind <= EnemyKind.SkyHawk) == 15 &&
              archetypes.Where(archetype => archetype.IsBoss && archetype.Kind != EnemyKind.StoneSentinel)
                  .All(archetype => archetype.Size.X * archetype.Size.Y >= 4f && archetype.VolleyCount >= 1 &&
                                    archetype.ProjectileSpeed > 0f) &&
              EnemyArchetypes.Get(EnemyKind.AstroGod).Size == new Vector2(5f, 5f) &&
              EnemyArchetypes.Get(EnemyKind.WarshipCore) is { Flying: true } warship && warship.Size.X > warship.Size.Y,
            "boss flags, tiers, and large boss sizes come from the archetype table");

        // Boss progression: gate, easy tier, hard tier, then the Astro-God.
        var progression = new GameSimulation(seed: 31337, width: 80, height: 48) { DebugSummonKeyEnabled = true };
        // BO-18 (0.2.2): the key goes through the item refusals, so the run stands in the Irradiated Core (the igniter's
        // place) and the obelisks are down (the sigil's gate); the bulb boss waits for the whole hard trio.
        progression.Player.Motion.Position = progression.Player.Motion.Position with { Y = progression.World.Height * 0.85f };
        progression.Flags.Set("obelisks.down");
        Check(progression.TrySummonSentinel() && !progression.TrySummonNextBoss() &&
              progression.Enemies.Single(enemy => enemy.IsBoss).Kind == EnemyKind.StoneSentinel,
            "the challenge key summons the Overseer mech first and only one boss at a time");
        var summoned = new List<EnemyKind>();
        for (var round = 0; round < EnemyArchetypes.BossProgression.Count; round++)
        {
            var boss = progression.Enemies.SingleOrDefault(enemy => enemy.IsBoss && enemy.IsAlive);
            if (boss is null)
            {
                if (!progression.TrySummonNextBoss())
                    break;
                boss = progression.Enemies.Single(enemy => enemy.IsBoss && enemy.IsAlive);
            }
            summoned.Add(boss.Kind);
            boss.TakeDamage(boss.MaximumHitPoints);
            progression.Tick(new GameInput(new MovementInput(0f)));
        }
        // BO-18 changed expectation: the bulb boss refuses until the whole trio is down, so the key skips it until then.
        var refusalOrder = EnemyArchetypes.BossProgression.Where(kind => kind != GameSimulation.BulbBoss && kind != EnemyKind.AstroGod)
            .Append(GameSimulation.BulbBoss).Append(EnemyKind.AstroGod);
        Check(summoned.SequenceEqual(refusalOrder) &&
              summoned.Skip(1).Take(4).All(kind => EnemyArchetypes.Get(kind).Tier == BossTier.Easy) &&
              summoned[5] == GameSimulation.CoreGateBoss && EnemyArchetypes.Get(summoned[5]).Tier == BossTier.Gate &&
              summoned.Skip(6).Take(5).All(kind => EnemyArchetypes.Get(kind).Tier == BossTier.Hard) &&
              summoned.IndexOf(GameSimulation.BulbBoss) > GameSimulation.HardTrio.Max(kind => summoned.IndexOf(kind)) &&
              summoned[^1] == EnemyKind.AstroGod &&
              progression.DefeatedBosses.Count == EnemyArchetypes.BossProgression.Count &&
              progression.NextBoss == EnemyKind.AstroGod,
            "boss progression runs gate, easy, the core gate boss, hard, then the Astro-God and records each defeat");
        // Boss rewards go to the player; with the core gate boss and its essences in the run the pack fills up, and
        // what does not fit lands at the player's feet (GameSimulation.DropLoot).
        int Delivered(ItemId id) => progression.Player.Inventory.Count(id) +
                                    progression.GroundItems.Where(ground => ground.ItemId == id).Sum(ground => ground.Count);
        Check(Delivered(ItemId.RocketLauncher) == 1 && Delivered(ItemId.PlasmaPistol) == 1 &&
              progression.Player.Inventory.Count(ItemId.EnergyCrystal) > 0,
            "bosses drop crystals and weapons to the player (into the inventory, overflow at their feet)");

        var bossSnapshot = progression.CaptureSnapshot();
        var snapshotJson = new System.Text.Json.JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        var parsedSnapshot = System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(bossSnapshot, snapshotJson), snapshotJson)!;
        var restoredBosses = GameSimulation.Restore(parsedSnapshot);
        var legacyBosses = GameSimulation.Restore(bossSnapshot with { DefeatedBosses = null });
        Check(restoredBosses.DefeatedBosses.ToHashSet().SetEquals(progression.DefeatedBosses) &&
              restoredBosses.NextBoss == EnemyKind.AstroGod &&
              legacyBosses.DefeatedBosses.Count == 0 && legacyBosses.NextBoss == EnemyKind.StoneSentinel,
            "snapshots round-trip defeated bosses and older saves without them still load");

        // Energy weapons share Energy cell ammunition.
        var gunner = new GameSimulation(seed: 4567, width: 80, height: 48);
        gunner.Player.Inventory.Add(ItemId.PlasmaPistol, 1);
        gunner.Player.Inventory.Add(ItemId.Arrow, 80);
        gunner.TrySelectInventorySlot(gunner.Player.Inventory.FindSlot(ItemId.PlasmaPistol));
        var cellsBefore = gunner.Player.Inventory.Count(ItemId.Arrow);
        var gunAim = new GridPoint(Math.Min(gunner.World.Width - 1, (int)gunner.Player.Motion.Position.X + 10),
            Math.Max(0, (int)gunner.Player.Motion.Position.Y));
        gunner.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, gunAim));
        var guns = new[] { ItemId.PlasmaPistol, ItemId.SniperRifle, ItemId.GrenadeLauncher, ItemId.RocketLauncher }
            .Select(id => gunner.Items[id]).ToArray();
        Check(cellsBefore > 0 && gunner.Player.Inventory.Count(ItemId.Arrow) == cellsBefore - 1 &&
              gunner.Projectiles.Count == 1 &&
              Math.Abs(gunner.Projectiles[0].Velocity.Length() - gunner.Items[ItemId.PlasmaPistol].ProjectileSpeed) < 0.01f &&
              guns.All(gun => gun.UseKind == ItemUseKind.Ranged && gun.AmmoType == ItemId.Arrow) &&
              guns[0].UseCooldownTicks < guns[1].UseCooldownTicks && guns[0].Damage < guns[1].Damage &&
              guns[1].ProjectileSpeed > guns.Where(gun => gun.Id != ItemId.SniperRifle).Max(gun => gun.ProjectileSpeed) &&
              guns[2].Knockback > guns[1].Knockback && guns[3].Damage > guns[2].Damage,
            "firing a gun consumes one Energy cell and guns have distinct ballistic profiles");

        // Deep ores: drill gating and drops.
        var miner = new GameSimulation(seed: 918273, width: 80, height: 48);
        var minerCenter = miner.Player.Motion.Position + miner.Player.Motion.Size / 2f;
        var oreTarget = FindReachableSolid(miner, minerCenter);
        miner.World.SetTile(oreTarget, TileId.CobaltOre);
        miner.Player.Inventory.Add(ItemId.WoodenPickaxe, 1); // the old survey drill, too weak for deep ore
        miner.TrySelectInventorySlot(miner.Player.Inventory.FindSlot(ItemId.WoodenPickaxe));
        miner.Tick(new GameInput(new MovementInput(0f), PlayerAction.Mine, oreTarget));
        var surveyBlocked = miner.World.GetCell(oreTarget) is { TileId: TileId.CobaltOre, Damage: 0 };
        miner.Player.Inventory.Add(ItemId.CopperPickaxe, 1);
        miner.TrySelectInventorySlot(miner.Player.Inventory.FindSlot(ItemId.CopperPickaxe));
        var cobaltBefore = miner.Player.Inventory.Count(ItemId.CobaltOre);
        for (var i = 0; i < 1200 && miner.World.GetCell(oreTarget).TileId != TileId.Air; i++)
            miner.Tick(new GameInput(new MovementInput(0f), PlayerAction.Mine, oreTarget));
        Check(surveyBlocked && miner.World.GetCell(oreTarget).TileId == TileId.Air &&
              // Parity B4: the ore pops out as a ground item (or is already magnet-collected).
              miner.Player.Inventory.Count(ItemId.CobaltOre) + miner.GroundItems.Where(drop => drop.ItemId == ItemId.CobaltOre).Sum(drop => drop.Count) == cobaltBefore + 1 &&
              TileDropTable.GetDrop(TileId.ViridiumOre) == ItemId.ViridiumOre &&
              TileDropTable.GetDrop(TileId.EnergyCrystal) == ItemId.EnergyCrystal &&
              TileDropTable.GetDrop(TileId.SolarPanel) == ItemId.SolarPanel,
            "deep ore needs a Ferrite drill and mining it drops the matching ore item");

        // World generation: ore depth bands and crystal clusters are deterministic.
        static (double Ferrite, double Cobalt, double Viridium, int Crystals) OreProfile(GameSimulation world)
        {
            double Depth(TileId tile)
            {
                var rows = new List<int>();
                for (var y = 0; y < world.World.Height; y++)
                for (var x = 0; x < world.World.Width; x++)
                    if (world.World.GetCell(new GridPoint(x, y)).TileId == tile)
                        rows.Add(y);
                return rows.Count == 0 ? -1 : rows.Average();
            }
            return (Depth(TileId.CopperOre), Depth(TileId.CobaltOre), Depth(TileId.ViridiumOre),
                world.World.CountTiles(TileId.EnergyCrystal));
        }
        var oreWorld = OreProfile(new GameSimulation(seed: 48271, width: 160, height: 72));
        Check(oreWorld.Ferrite > 0 && oreWorld.Cobalt > oreWorld.Ferrite && oreWorld.Viridium > oreWorld.Cobalt &&
              oreWorld.Crystals > 0 && oreWorld == OreProfile(new GameSimulation(seed: 48271, width: 160, height: 72)),
            "world generation layers ferrite, cobalt, then viridium and seeds crystal clusters per seed");

        // Neo-Terra recipes and guide unlocks.
        var neoItems = new ItemCatalog();
        var neoCrafting = new CraftingSystem(neoItems);
        var neoInventory = new Inventory(neoItems, slotCount: 8);
        neoInventory.Add(ItemId.EnergyCrystal, 1);
        neoInventory.Add(ItemId.ViridiumOre, 1);
        var fabricatorWorld = new TileGrid(8, 8);
        fabricatorWorld.SetTile(new GridPoint(4, 4), TileId.Workbench);
        var fabricatorEnvironment = new CraftingEnvironment(new Vector2(4.5f, 3.5f), fabricatorWorld, craftingTiles);
        var cellCraft = neoCrafting.TryCraft("energy-cell", neoInventory, fabricatorEnvironment);
        var forgeMissing = neoCrafting.TryCraft("plasma-pistol", neoInventory, fabricatorEnvironment);
        Check(cellCraft.Success && neoInventory.Count(ItemId.Arrow) == 25 && neoInventory.Count(ItemId.EnergyCrystal) == 0 &&
              forgeMissing.Message.Contains("Power Forge", StringComparison.Ordinal) &&
              neoCrafting.FindByOutput(ItemId.RocketLauncher).Single().Ingredients
                  .Any(group => group.Alternatives.Contains(ItemId.ViridiumOre)) &&
              neoCrafting.ConsultGuide(neoInventory).Contains("rocket-launcher") &&
              new[] { ItemId.MiniTurret, ItemId.HydroponicsTray, ItemId.SolarPanel, ItemId.OxygenUnit }
                  .All(id => neoCrafting.FindByOutput(id).Single().RequiredStations!.Contains(CraftingStationId.Workbench) &&
                             !craftingTiles[neoItems[id].PlacedTile].IsSolid),
            "Nano-Fabricator makes energy cells and furniture; guns need the Power Forge and the guide reveals them");

        // Biome layout: frozen left, grassland centre, junk right, radiated pockets, molten floor.
        var biomeWorld = new GameSimulation(seed: 48271, width: 160, height: 72);
        var layout = biomeWorld.BiomeLayout;
        int CountWhere(GameSimulation sim, Func<int, int, TileId, bool> predicate)
        {
            var total = 0;
            for (var y = 0; y < sim.World.Height; y++)
            for (var x = 0; x < sim.World.Width; x++)
                if (predicate(x, y, sim.World.GetCell(new GridPoint(x, y)).TileId))
                    total++;
            return total;
        }
        var spawnColumn = (int)MathF.Floor(biomeWorld.Player.Motion.Position.X + biomeWorld.Player.Motion.Size.X / 2f);
        var spawnGround = biomeWorld.World.GetCell(new GridPoint(spawnColumn, layout.SurfaceAt(spawnColumn))).TileId;
        var frozenEdges = Enumerable.Range(0, biomeWorld.World.Height).Select(layout.FrozenEdgeAt).Distinct().Count();
        Check(CountWhere(biomeWorld, (x, y, t) => t == TileId.Snow && layout.IsFrozen(x, y)) > 150 &&
              CountWhere(biomeWorld, (x, y, t) => t == TileId.Snow && !layout.IsFrozen(x, y)) == 0 &&
              CountWhere(biomeWorld, (x, y, t) => t == TileId.DesertSand && layout.IsJunk(x, y)) > 150 &&
              CountWhere(biomeWorld, (x, y, t) => t == TileId.DesertSand && !layout.IsJunk(x, y)) == 0 &&
              spawnGround is TileId.Grass or TileId.JungleGrass &&
              layout.RadiatedPockets.Count > 0 &&
              CountWhere(biomeWorld, (_, _, t) => t == TileId.CrimsonSoil) > 0 &&
              CountWhere(biomeWorld, (_, _, t) => t == TileId.CorruptRock) > 0 &&
              CountWhere(biomeWorld, (x, y, t) => t == TileId.IrradiatedRock && layout.IsMolten(x, y)) > 100 &&
              CountWhere(biomeWorld, (x, y, _) => biomeWorld.World.GetCell(new GridPoint(x, y)).LiquidKind == LiquidKind.Lava) > 0 &&
              frozenEdges > 1,
            "seeded biome layout places frozen, grassland, junk, radiated, and molten biomes with jittered borders");
        Check(CountWhere(biomeWorld, (x, y, t) => t == TileId.GlacialOre) > 0 &&
              CountWhere(biomeWorld, (x, y, t) => t == TileId.GlacialOre && !layout.IsFrozen(x, y)) == 0 &&
              CountWhere(biomeWorld, (x, y, t) => t == TileId.ScrapIronOre) > 0 &&
              CountWhere(biomeWorld, (x, y, t) => t == TileId.ScrapIronOre && !layout.IsJunk(x, y)) == 0 &&
              CountWhere(biomeWorld, (x, y, t) => t == TileId.UraniumOre) > 0 &&
              CountWhere(biomeWorld, (x, y, t) => t == TileId.PyrociteOre) > 0 &&
              CountWhere(biomeWorld, (x, y, t) => t == TileId.PyrociteOre && !layout.IsMolten(x, y)) == 0 &&
              biomeWorld.Tiles[TileId.UraniumOre].MinimumToolPower > biomeWorld.Items[ItemId.WoodenPickaxe].ToolPower &&
              biomeWorld.Tiles[TileId.UraniumOre].MinimumToolPower <= biomeWorld.Items[ItemId.CopperPickaxe].ToolPower &&
              TileDropTable.GetDrop(TileId.GlacialOre) == ItemId.GlacialOre &&
              TileDropTable.GetDrop(TileId.ScrapIronOre) == ItemId.ScrapIronOre &&
              TileDropTable.GetDrop(TileId.Snow) == ItemId.SnowBlock &&
              TileDropTable.GetDrop(TileId.EmberRock) == ItemId.EmberStoneBlock &&
              TileDropTable.GetDrop(TileId.CrimsonSoil) == ItemId.RiftSoilBlock &&
              biomeWorld.Crafting.FindByOutput(ItemId.InsulatedSuit).Any() &&
              biomeWorld.Items[ItemId.InsulatedSuit].ImmunityTags.Contains("cold") &&
              CountWhere(new GameSimulation(seed: 48271, width: 160, height: 72), (_, _, t) => t == TileId.UraniumOre) ==
                  CountWhere(biomeWorld, (_, _, t) => t == TileId.UraniumOre),
            "biome ores grow only in their biome, are drill-gated, drop matching items, and feed new recipes");

        var frozenX = Math.Max(2, layout.FrozenEdgeAt(layout.SurfaceAt(4)) / 2);
        var junkX = (layout.JunkStartAt(layout.SurfaceAt(150)) + biomeWorld.World.Width) / 2;
        var deepX = biomeWorld.World.Width / 2;
        Check(BiomeScanner.Scan(biomeWorld.World, new GridPoint(frozenX, layout.SurfaceAt(frozenX) - 1)).Zone == BiomeZone.FrozenWasteland &&
              BiomeScanner.Scan(biomeWorld.World, new GridPoint(junkX, layout.SurfaceAt(junkX) - 1)).Zone == BiomeZone.JunkWasteland &&
              BiomeScanner.Scan(biomeWorld.World, new GridPoint(deepX, biomeWorld.World.Height - 3)).Zone == BiomeZone.MoltenChasm &&
              biomeWorld.CurrentBiome.Zone == BiomeZone.Grassland,
            "biome scanner reads Frozen at the left surface, Junk on the right, Molten deep, and Grassland at spawn");

        // Rooms: a sealed hull-panel box is a room; a hole in its roof opens it to the sky.
        var roomGame = new GameSimulation(seed: 1234, width: 80, height: 48);
        void BuildBox(int left, int top)
        {
            for (var x = left; x <= left + 5; x++)
            for (var y = top; y <= top + 4; y++)
                roomGame.World.SetTile(new GridPoint(x, y), x == left || x == left + 5 || y == top || y == top + 4 ? TileId.Wood : TileId.Air);
        }
        BuildBox(34, 3);
        var boxInside = new Vector2(36.5f, 5.5f);
        var sealedRoom = roomGame.Regions.GetRoomAt(boxInside);
        roomGame.World.SetTile(new GridPoint(36, 3), TileId.Air);
        var openRoom = roomGame.Regions.GetRoomAt(boxInside);
        Check(sealedRoom is { IsRoom: true, CellCount: 12, TouchesMapEdge: false, OpenRoofCells: 0 } &&
              openRoom is null && roomGame.Regions.GetRegion(RegionMap.CellAt(boxInside)) is { TouchesMapEdge: true } &&
              RegionMap.CellAt(new Vector2(36.9f, 5.2f)) == new GridPoint(36, 5),
            "a sealed hull-panel box becomes a room and an opened one rejoins the outdoors");

        // Temperature: an Oxygen unit pulls a frozen room toward its target; lava heats a room.
        var frozenTop = 3;
        BuildBox(4, frozenTop);
        BuildBox(12, frozenTop);
        BuildBox(20, frozenTop);
        roomGame.World.SetTile(new GridPoint(6, frozenTop + 3), TileId.OxygenUnit);
        roomGame.World.TryAddLiquid(new GridPoint(22, frozenTop + 3), LiquidKind.Lava, 1f, roomGame.Tiles);
        var model = new TemperatureModel();
        const float coldOutdoor = -20f;
        float Settle(Vector2 inside)
        {
            var room = roomGame.Regions.GetRoomAt(inside)!;
            var sources = TemperatureModel.CountSources(room, roomGame.World, roomGame.Tiles);
            var temperature = float.NaN;
            for (var hour = 0; hour < 48; hour++)
                temperature = TemperatureModel.StepRoom(temperature, coldOutdoor, room, sources, deepUnderground: false, 0.5f);
            return temperature;
        }
        var oxygenRoom = Settle(new Vector2(6.5f, frozenTop + 2.5f));
        var plainRoom = Settle(new Vector2(14.5f, frozenTop + 2.5f));
        var lavaRoom = Settle(new Vector2(21.5f, frozenTop + 2.5f));
        for (var i = 0; i < GameSimulation.EnvironmentIntervalTicks; i++)
            roomGame.Tick(new GameInput(new MovementInput(0f)));
        Check(Math.Abs(oxygenRoom - TemperatureModel.OxygenUnitTarget) < Math.Abs(oxygenRoom - coldOutdoor) &&
              oxygenRoom > plainRoom + 15f && Math.Abs(plainRoom - coldOutdoor) < 1f && lavaRoom > coldOutdoor + 15f &&
              roomGame.Regions.GetRoomAt(new Vector2(6.5f, frozenTop + 2.5f)) is { } trackedRoom && !float.IsNaN(trackedRoom.Temperature) &&
              float.IsFinite(roomGame.PlayerAmbientTemperature) && roomGame.PlayerComfort.Comfortable &&
              TemperatureModel.Evaluate(-20f, null).Dangerous &&
              !TemperatureModel.Evaluate(-20f, roomGame.Items[ItemId.HearthCharm].ImmunityTags).Dangerous &&
              model.OutdoorAt(BiomeZone.FrozenWasteland, false, 14f) < model.OutdoorAt(BiomeZone.MoltenChasm, true, 14f) &&
              model.OutdoorAt(BiomeZone.JunkWasteland, false, 14f) > model.OutdoorAt(BiomeZone.JunkWasteland, false, 3f) + 25f,
            "room temperature trends toward an Oxygen unit's target, lava heats rooms, and cold gear widens the safe band");

        // New creatures: archetypes, display names, biome rosters, drops, and passive wildlife.
        var newKinds = Enum.GetValues<EnemyKind>().Where(kind => kind >= EnemyKind.SnowWolf && kind <= EnemyKind.SkyHawk).ToArray();
        Check(newKinds.Length == 21 &&
              newKinds.All(kind => EnemyArchetypes.Contains(kind) && !EnemyArchetypes.Get(kind).IsBoss &&
                                   NpcDropRegistry.GetDrops(kind).Count > 0) &&
              EnemyArchetypes.Get(EnemyKind.SnowWolf).DisplayName == "Snow-wolf" &&
              EnemyArchetypes.Get(EnemyKind.RadLeech).DisplayName == "Rad-leech" &&
              EnemyArchetypes.Get(EnemyKind.NeoTerraDeer) is { Passive: true, DisplayName: "Neo-Terra deer" } &&
              EnemyArchetypes.Get(EnemyKind.SkyHawk) is { Passive: true, Flying: true } &&
              EnemyArchetypes.Get(EnemyKind.GeigerBug).Flying && EnemyArchetypes.Get(EnemyKind.SalvageDrone).Flying &&
              EnemyArchetypes.All.Count(archetype => archetype.Passive && archetype.Kind <= EnemyKind.SkyHawk) == 2 &&
              BiomeSpawnTables.KindsFor(BiomeZone.FrozenWasteland).Contains(EnemyKind.GlacialGolem) &&
              BiomeSpawnTables.KindsFor(BiomeZone.MoltenChasm).Contains(EnemyKind.CoreHusk) &&
              Enumerable.Range(0, 50).Select(i => BiomeSpawnTables.Pick(BiomeZone.JunkWasteland, i / 50d, false))
                  .All(kind => BiomeSpawnTables.KindsFor(BiomeZone.JunkWasteland).Contains(kind)) &&
              NpcDropRegistry.GetDrops(EnemyKind.NeoTerraDeer).Any(drop => drop.Item == ItemId.Berry),
            "each new biome creature has an archetype, drops, and a biome roster; wildlife is passive");

        var deerGame = new GameSimulation(seed: 4567, width: 80, height: 48);
        var deer = deerGame.SpawnCreature(EnemyKind.NeoTerraDeer, deerGame.Player.Motion.Position + new Vector2(0.2f, 0.6f));
        var hpBeforeDeer = deerGame.Player.HitPoints;
        for (var i = 0; i < 180; i++)
            deerGame.Tick(new GameInput(new MovementInput(0f)));
        var deerGap = MathF.Abs(deer.Motion.Position.X - deerGame.Player.Motion.Position.X);
        deer.TakeDamage(1f);
        for (var i = 0; i < 90; i++)
            deerGame.Tick(new GameInput(new MovementInput(0f)));
        Check(deerGame.Player.HitPoints == hpBeforeDeer && deer.IsAlive &&
              MathF.Abs(deer.Motion.Position.X - deerGame.Player.Motion.Position.X) > deerGap + 1f,
            "passive deer never damages the player and flees when hurt");

        // Storyteller: deterministic incidents for a fixed seed, and they survive a save round-trip.
        static GameSimulation RunStory(int seed, int maxHours)
        {
            var sim = new GameSimulation(seed: seed, width: 80, height: 48);
            var ticks = (int)(maxHours / (0.25f * GameSimulation.ClockHoursPerRealSecond));
            for (var i = 0; i < ticks && sim.IncidentLog.Count < 2 && !sim.IsOver; i++)
            {
                // Keep the idle test pawn alive so wildlife cannot end the run early.
                sim.Player.Heal(sim.Player.MaximumHitPoints);
                sim.Tick(new GameInput(new MovementInput(0f)), 0.25f);
            }
            return sim;
        }
        var storyA = RunStory(2024, 24 * 7); // past the three quiet opening days
        var storyB = RunStory(2024, 24 * 7);
        Check(storyA.IncidentLog.Count > 0 &&
              storyA.IncidentLog.Select(record => (record.IncidentId, record.Day, record.Summary))
                  .SequenceEqual(storyB.IncidentLog.Select(record => (record.IncidentId, record.Day, record.Summary))) &&
              storyA.Storyteller.Capture().RngState == storyB.Storyteller.Capture().RngState &&
              Storyteller.ComputeThreatPoints(1, 0f, 2, 0) < Storyteller.ComputeThreatPoints(8, 5000f, 4, 3) &&
              Storyteller.ComputeThreatPoints(400, 1e9f, 50, 15) == Storyteller.MaximumThreatPoints,
            "storyteller fires the same incidents for a fixed seed and threat points scale with the colony");

        // A cold snap is refused while another temperature shift is running.
        if (storyA.OutdoorTemperatureOffset == 0f)
            storyA.ForceIncident("cold-snap");
        storyA.ForceIncident("wanderer-joins");
        var storySnapshot = storyA.CaptureSnapshot();
        var storyJson = System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(storySnapshot, snapshotJson), snapshotJson)!;
        var restoredStory = GameSimulation.Restore(storyJson);
        var legacyStory = GameSimulation.Restore(storySnapshot with { Storyteller = null, Colonists = null });
        var savedTeller = storyA.Storyteller.Capture();
        var loadedTeller = restoredStory.Storyteller.Capture();
        Check(loadedTeller.RngState == savedTeller.RngState && loadedTeller.CooldownHours == savedTeller.CooldownHours &&
              loadedTeller.TemperatureOffset == savedTeller.TemperatureOffset && savedTeller.TemperatureOffset != 0f &&
              loadedTeller.TemperatureOffsetHoursRemaining == savedTeller.TemperatureOffsetHoursRemaining &&
              loadedTeller.HoursElapsed == savedTeller.HoursElapsed && loadedTeller.WanderersJoined == savedTeller.WanderersJoined &&
              restoredStory.Colonists.Count == storyA.Colonists.Count &&
              restoredStory.IncidentLog.Select(record => record.IncidentId).SequenceEqual(storyA.IncidentLog.Select(record => record.IncidentId)) &&
              restoredStory.OutdoorTemperatureOffset == storyA.OutdoorTemperatureOffset &&
              legacyStory.IncidentLog.Count == 0 && legacyStory.Storyteller.CooldownHours > 0f,
            "snapshots round-trip storyteller state, temperature offsets, and joined wanderers; older saves still load");

        // Colonist health: coverage-weighted hits, part destruction, bleeding and tending.
        var hitBody = new HealthComponent(ColonistAnatomy.CreateTorso(), seed: 1);
        var hitWeights = hitBody.GetHitWeights();
        var hitCounts = new Dictionary<BodyPart, int>();
        var hitRandom = new Random(42);
        const int hitSamples = 40000;
        for (var i = 0; i < hitSamples; i++)
        {
            var part = hitBody.ChooseHitPart(hitRandom);
            hitCounts[part] = hitCounts.GetValueOrDefault(part) + 1;
        }
        BodyPart HitPart(string name) => hitWeights.Keys.Single(part => part.Name == name);
        Check(MathF.Abs(hitWeights.Values.Sum() - 1f) < 0.001f &&
              hitWeights.Keys.All(part => part.Depth == BodyPartDepth.Outside) &&
              !hitWeights.Keys.Any(part => part.Name is "brain" or "heart") &&
              hitWeights.All(pair => MathF.Abs(hitCounts.GetValueOrDefault(pair.Key) / (float)hitSamples - pair.Value) < 0.012f) &&
              hitWeights[HitPart("torso")] > hitWeights[HitPart("left leg")] &&
              hitWeights[HitPart("left leg")] > hitWeights[HitPart("left hand")] &&
              hitWeights[HitPart("left hand")] > hitWeights[HitPart("left eye")],
            "hit-part selection follows outside coverage; organs are only reached by inward damage");

        var armBody = new HealthComponent(ColonistAnatomy.CreateTorso(), seed: 2);
        var leftArm = armBody.FindPart("left arm")!;
        var leftHand = armBody.FindPart("left hand")!;
        var armResult = armBody.ApplyDamageToPart(leftArm, 100f, DamageKind.Cut);
        Check(armResult.DestroyedParts.Contains(leftArm) && leftArm.IsMissing && leftHand.IsMissing &&
              armBody.GetPartHealth(leftHand) == 0f && armBody.Manipulation < 0.6f && armBody.IsBleeding &&
              armBody.Injuries.Any(injury => injury.Kind == ConditionKind.MissingPart && injury.Part == leftArm && injury.IsFresh) &&
              !armBody.Injuries.Any(injury => injury.Part == leftHand) &&
              !armBody.GetHitWeights().ContainsKey(leftHand) && !armBody.IsDead,
            "destroying an arm removes the hand with it and leaves a fresh, bleeding stump");

        var bleedBody = new HealthComponent(ColonistAnatomy.CreateTorso(), seed: 3);
        bleedBody.ApplyDamageToPart(bleedBody.FindPart("torso")!, 15f, DamageKind.Cut);
        var untendedBleed = bleedBody.BleedRate;
        bleedBody.Tick(2f);
        var lossUntended = bleedBody.BloodLoss;
        var tendedCount = bleedBody.Tend(0.8f);
        var tendedBleed = bleedBody.BleedRate;
        bleedBody.Tick(2f);
        var fatalBody = new HealthComponent(ColonistAnatomy.CreateTorso(), seed: 4);
        fatalBody.AddWholeBody(ConditionKind.BloodLoss, 1f);
        Check(untendedBleed > 0f && lossUntended > 0.05f && tendedCount > 0 && tendedBleed == 0f &&
              bleedBody.BloodLoss < lossUntended && !bleedBody.NeedsTending &&
              fatalBody.IsDead && fatalBody.DeathCause == "blood loss",
            "untended bleeding raises blood loss, tending stops it, and total blood loss is fatal");

        var legGame = new GameSimulation(seed: 3131, width: 80, height: 48);
        var steadyGame = new GameSimulation(seed: 3131, width: 80, height: 48);
        var legHealth = legGame.Player.Health;
        legHealth.ApplyDamageToPart(legHealth.FindPart("left leg")!, 20f, DamageKind.Crush);
        legHealth.ApplyDamageToPart(legHealth.FindPart("right leg")!, 20f, DamageKind.Crush);
        var legTopSpeed = 0f;
        var steadyTopSpeed = 0f;
        for (var i = 0; i < 40; i++)
        {
            legGame.Tick(new GameInput(new MovementInput(1f)));
            steadyGame.Tick(new GameInput(new MovementInput(1f)));
            legTopSpeed = MathF.Max(legTopSpeed, MathF.Abs(legGame.Player.Motion.Velocity.X));
            steadyTopSpeed = MathF.Max(steadyTopSpeed, MathF.Abs(steadyGame.Player.Motion.Velocity.X));
        }
        // D-36 (IC-4, 0.2.2): the player is HP-only, so ruined legs no longer slow them; the body still records the damage.
        Check(legHealth.Moving < 0.5f && steadyGame.Player.Health.Moving > 0.95f &&
              legGame.Player.MoveCapacityFactor == 1f && !legGame.Player.IsDowned &&
              steadyTopSpeed > 0f && MathF.Abs(legTopSpeed - steadyTopSpeed) < 0.01f,
            "leg damage lowers the body's moving capacity but never the player's run speed (D-36)");

        var brainGame = new GameSimulation(seed: 3232, width: 80, height: 48);
        var brainHealth = brainGame.Player.Health;
        brainHealth.ApplyDamageToPart(brainHealth.FindPart("brain")!, 50f, DamageKind.Shot);
        brainGame.Tick(new GameInput(new MovementInput(0f)));
        // D-36 (IC-4, 0.2.2): a colonist's body still dies of a destroyed brain; the player dies only at 0 hit points.
        var colonistBrain = new HealthComponent(ColonistAnatomy.CreateTorso(), seed: 5);
        colonistBrain.ApplyDamageToPart(colonistBrain.FindPart("brain")!, 50f, DamageKind.Shot);
        Check(colonistBrain.IsDead && colonistBrain.DeathCause?.Contains("brain") == true &&
              !brainHealth.IsDead && !brainGame.Player.IsDead && !brainGame.IsOver && brainGame.Player.HitPoints > 0f,
            "destroying the brain kills a colonist even with hit points left, never the HP-only player");

        // Thoughts and mood.
        var painGame = new GameSimulation(seed: 3333, width: 80, height: 48);
        for (var i = 0; i < GameSimulation.EnvironmentIntervalTicks; i++)
            painGame.Tick(new GameInput(new MovementInput(0f)));
        var calmTarget = painGame.Player.Mind.Mood.Target;
        var calmMood = painGame.Player.Mood;
        painGame.Player.Health.ApplyDamageToPart(painGame.Player.Health.FindPart("torso")!, 30f, DamageKind.Blunt);
        for (var i = 0; i < GameSimulation.EnvironmentIntervalTicks * 3; i++)
            painGame.Tick(new GameInput(new MovementInput(0f)));
        Check(painGame.Player.Health.PainLevel >= 0.3f &&
              painGame.Player.Thoughts.Any(thought => thought.Id == ThoughtId.SeriousPain && thought.MoodOffset < 0f) &&
              painGame.Player.Mind.Mood.Target < calmTarget - 0.05f && painGame.Player.Mood < calmMood,
            "pain creates a pain thought and pulls mood down");

        var memoryTracker = new ThoughtTracker();
        memoryTracker.AddMemory(ThoughtId.KilledCreature);
        var singleKill = memoryTracker.TotalMoodOffset;
        memoryTracker.AddMemory(ThoughtId.KilledCreature);
        memoryTracker.AddMemory(ThoughtId.KilledCreature);
        var threeKills = memoryTracker.TotalMoodOffset;
        for (var i = 0; i < 10; i++)
            memoryTracker.AddMemory(ThoughtId.KilledCreature);
        memoryTracker.AddMemory(ThoughtId.AteNanoBanana);
        memoryTracker.AddMemory(ThoughtId.AteNanoBanana);
        var stackedKills = memoryTracker.Memories.Count(memory => memory.Id == ThoughtId.KilledCreature);
        var bananas = memoryTracker.Memories.Count(memory => memory.Id == ThoughtId.AteNanoBanana);
        memoryTracker.Advance(100f);
        Check(singleKill > 0f && threeKills > singleKill && threeKills < singleKill * 3f - 0.0001f &&
              stackedKills == ThoughtCatalog.Get(ThoughtId.KilledCreature).StackLimit && bananas == 1 &&
              memoryTracker.Memories.Count == 0 && memoryTracker.TotalMoodOffset == 0f,
            "memories stack to a limit with diminishing returns, merge when fresh, and expire");

        static (MentalBreakKind Kind, long StartTick, bool Catharsis) RunBreak()
        {
            var sim = new GameSimulation(seed: 3434, width: 80, height: 48);
            sim.AddStartingCrewForChecks();
            var crew = sim.Colonists[0];
            var started = MentalBreakKind.None;
            var startTick = -1L;
            var catharsis = false;
            for (var i = 0; i < 4000 && !catharsis && crew.IsAlive; i++)
            {
                crew.Needs.SetReserves(0f, 0f, 0f, 0f);
                if (started == MentalBreakKind.None)
                    crew.Mind.Mood.Force(0.02f);
                sim.Player.Heal(sim.Player.MaximumHitPoints);
                sim.Tick(new GameInput(new MovementInput(0f)), 0.25f);
                if (started == MentalBreakKind.None && crew.Mind.Breaker.IsBreaking)
                {
                    started = crew.MentalBreak;
                    startTick = sim.TickNumber;
                }
                catharsis = started != MentalBreakKind.None && crew.Mind.Thoughts.HasMemory(ThoughtId.Catharsis);
            }
            return (started, startTick, catharsis);
        }
        var breakA = RunBreak();
        var breakB = RunBreak();
        Check(breakA.Kind != MentalBreakKind.None && breakA.Catharsis && breakA == breakB,
            "a forced low-mood colonist breaks deterministically and recovers with a catharsis memory");

        // Relations and interactions.
        var socialGame = new GameSimulation(seed: 3535, width: 80, height: 48);
        socialGame.AddStartingCrewForChecks();
        var chatter = socialGame.Colonists[0];
        var listener = socialGame.Colonists[1];
        ColonySocialSystem.SetBoth(chatter, listener, RelationKind.Crewmate, 0f);
        var opinionBefore = SocialRules.Opinion(chatter, listener);
        for (var i = 0; i < 12; i++)
            socialGame.Social.Interact(chatter, listener, InteractionKind.Chitchat, socialGame.ElapsedGameHours);
        for (var i = 0; i < 5; i++)
            socialGame.Social.Interact(chatter, listener, InteractionKind.DeepTalk, socialGame.ElapsedGameHours);
        var chatterView = socialGame.GetOpinions(chatter).Single(view => view.OtherKey == listener.SocialKey);
        Check(SocialRules.Opinion(chatter, listener) > opinionBefore + 20f &&
              SocialRules.DerivedRelation(chatter, listener) == RelationKind.Friend &&
              chatterView.RelationLabel == "Friend" && chatterView.Opinion >= SocialRules.FriendOpinion &&
              SocialRules.Compatibility(chatter.Profile, listener.Profile) == SocialRules.Compatibility(listener.Profile, chatter.Profile),
            "repeated chats raise opinion until the pair count as friends");

        var fightGame = new GameSimulation(seed: 3636, width: 80, height: 48);
        fightGame.AddStartingCrewForChecks();
        var bully = fightGame.Colonists[0];
        var victim = fightGame.Colonists[1];
        ColonySocialSystem.SetBoth(bully, victim, RelationKind.Crewmate, 0f);
        var fought = false;
        for (var i = 0; i < 30 && !fought; i++)
            fought = fightGame.Social.Interact(bully, victim, InteractionKind.Insult, fightGame.ElapsedGameHours).SocialFight;
        Check(fought && SocialRules.Opinion(victim, bully) <= SocialRules.RivalOpinion &&
              victim.Health.Injuries.Any(injury => injury.Kind == ConditionKind.Bruise) &&
              bully.Health.Injuries.Any(injury => injury.Kind == ConditionKind.Bruise) &&
              victim.HitPoints < victim.MaximumHitPoints &&
              victim.Mind.Thoughts.HasMemory(ThoughtId.SocialFight, bully.SocialKey),
            "insults at low opinion can start a social fight that bruises both sides");

        var griefGame = new GameSimulation(seed: 3737, width: 80, height: 48);
        griefGame.AddStartingCrewForChecks();
        var mourner = griefGame.Colonists[0];
        var lost = griefGame.Colonists[1];
        ColonySocialSystem.SetBoth(mourner, lost, RelationKind.Crewmate, 0f);
        for (var i = 0; i < 12; i++)
            griefGame.Social.Interact(mourner, lost, InteractionKind.Chitchat, griefGame.ElapsedGameHours);
        for (var i = 0; i < 5; i++)
            griefGame.Social.Interact(mourner, lost, InteractionKind.DeepTalk, griefGame.ElapsedGameHours);
        var moodBeforeGrief = mourner.Mind.Thoughts.TotalMoodOffset;
        lost.Health.DestroyPart(lost.Health.FindPart("heart")!);
        griefGame.Tick(new GameInput(new MovementInput(0f)));
        Check(!lost.IsAlive && lost.HitPoints == 0f &&
              mourner.Mind.Thoughts.HasMemory(ThoughtId.FriendDied, lost.SocialKey) &&
              mourner.Mind.Thoughts.TotalMoodOffset < moodBeforeGrief &&
              griefGame.Player.Mind.Thoughts.Memories.Any(memory => memory.OtherKey == lost.SocialKey),
            "a friend's death leaves a relation-scaled grief memory");

        // Body, mind and social state survive a save round-trip; older saves still load.
        var mindSave = new GameSimulation(seed: 3838, width: 80, height: 48);
        mindSave.AddStartingCrewForChecks();
        var savedPlayer = mindSave.Player;
        var savedCrew = mindSave.Colonists[0];
        savedPlayer.Health.ApplyDamageToPart(savedPlayer.Health.FindPart("left hand")!, 100f, DamageKind.Cut);
        savedPlayer.Health.Tend(0.6f);
        savedPlayer.Health.ApplyDamageToPart(savedPlayer.Health.FindPart("right leg")!, 9f, DamageKind.Bite);
        savedPlayer.Mind.Thoughts.AddMemory(ThoughtId.KilledCreature);
        savedCrew.Health.ApplyDamageToPart(savedCrew.Health.FindPart("torso")!, 8f, DamageKind.Burn);
        mindSave.Social.Interact(savedCrew, savedPlayer, InteractionKind.Chitchat, mindSave.ElapsedGameHours);
        ColonySocialSystem.SetBoth(savedCrew, mindSave.Colonists[1], RelationKind.Lover, 0f);
        savedCrew.Mind.Breaker.Start(MentalBreakKind.Daze, MentalBreakSeverity.Minor, 2f);
        var mindSnapshot = mindSave.CaptureSnapshot();
        var mindJson = System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(mindSnapshot, snapshotJson), snapshotJson)!;
        var mindLoaded = GameSimulation.Restore(mindJson, savedPlayer.Colonist);
        var loadedPlayer = mindLoaded.Player;
        var loadedCrew = mindLoaded.Colonists[0];
        static string Describe(HealthComponent health) => string.Join("|", health.Injuries
            .Select(injury => $"{injury.Kind}:{injury.Part.Name}:{injury.Severity:0.000}:{injury.IsTended}"));
        var legacyMind = GameSimulation.Restore(mindSnapshot with { PlayerState = null, ColonistStates = null });
        Check(mindJson.FormatVersion == GameSnapshot.CurrentFormatVersion && loadedPlayer.Health.FindPart("left hand")!.IsMissing &&
              Describe(loadedPlayer.Health) == Describe(savedPlayer.Health) &&
              Describe(loadedCrew.Health) == Describe(savedCrew.Health) &&
              loadedPlayer.Health.GetPartHealth(loadedPlayer.Health.FindPart("right leg")!) ==
              savedPlayer.Health.GetPartHealth(savedPlayer.Health.FindPart("right leg")!) &&
              loadedPlayer.Mind.Thoughts.HasMemory(ThoughtId.KilledCreature) &&
              loadedPlayer.Mind.Thoughts.HasMemory(ThoughtId.Chitchat, savedCrew.SocialKey) &&
              loadedPlayer.Mind.Thoughts.Memories.Count == savedPlayer.Mind.Thoughts.Memories.Count &&
              loadedCrew.Mind.Relations.Get(mindLoaded.Colonists[1].SocialKey) == RelationKind.Lover &&
              loadedCrew.Mind.Breaker.ActiveKind == MentalBreakKind.Daze &&
              MathF.Abs(SocialRules.Opinion(loadedPlayer, loadedCrew) - SocialRules.Opinion(savedPlayer, savedCrew)) < 0.001f &&
              MathF.Abs(loadedCrew.Mood - savedCrew.Mood) < 0.0001f &&
              legacyMind.Player.Health.Injuries.Count == 0 && legacyMind.Colonists.Count == 2,
            "snapshots round-trip body conditions, memories, relations and break state; older saves still load");

        // ---- Engine and world generation (ENGINE_WORLDGEN.md section 3) ----

        // Detailed generator: a Large radiated world, generated twice from the same seed.
        var bigA = new GameSimulation(seed: 4242, width: 1600, height: 500, blight: WorldBlight.Radiated);
        var bigB = new GameSimulation(seed: 4242, width: 1600, height: 500, blight: WorldBlight.Radiated);
        var bigFeatures = bigA.Features!;
        int CountTiles(GameSimulation sim, params TileId[] ids)
        {
            var set = ids.ToHashSet();
            var total = 0;
            foreach (var cell in sim.World.Cells)
                if (set.Contains(cell.TileId))
                    total++;
            return total;
        }
        var naturalWalls = new HashSet<ushort>();
        var walledAir = 0;
        foreach (var cell in bigA.World.Cells)
        {
            if (WallIds.IsNatural(cell.WallId))
                naturalWalls.Add(cell.WallId);
            if (cell.TileId == TileId.Air && cell.WallId != 0)
                walledAir++;
        }
        var zoneCenter = (bigFeatures.BlightZones[0].Left + bigFeatures.BlightZones[0].Right) / 2;
        var zoneSurface = bigA.BiomeLayout.SurfaceAt(zoneCenter);
        var bigSpawnGround = bigA.World.GetCell(bigA.SpawnColumn, bigA.BiomeLayout.SurfaceAt(bigA.SpawnColumn)).TileId;
        Check(bigA.World.Cells.SequenceEqual(bigB.World.Cells) &&
              bigA.Blight == WorldBlight.Radiated && bigFeatures.BlightZones.Count >= 2 &&
              bigFeatures.Hearts.Count >= 3 && CountTiles(bigA, TileId.ReactorCore) == bigFeatures.Hearts.Count * 4 &&
              bigFeatures.Altars.Count >= 2 && CountTiles(bigA, TileId.RadAltar) == bigFeatures.Altars.Count * 4 &&
              bigFeatures.BlightZones.All(zone => Math.Abs((zone.Left + zone.Right) / 2 - bigA.World.Width / 2) > bigA.World.Width / 10) &&
              CountTiles(bigA, TileId.Snow) > 1000 && CountTiles(bigA, TileId.DesertSand) > 1000 &&
              CountTiles(bigA, TileId.IrradiatedRock) > 1000 && CountTiles(bigA, TileId.CoreCrust) > 100 &&
              CountTiles(bigA, TileId.FungalSoil) > 50 && CountTiles(bigA, TileId.Grass) > 200 &&
              CountTiles(bigA, TileId.CrimsonSoil, TileId.CorruptRock) > 2000 &&
              CountTiles(bigA, TileId.CopperOre) > 0 && CountTiles(bigA, TileId.CobaltOre) > 0 && CountTiles(bigA, TileId.ViridiumOre) > 0 &&
              CountTiles(bigA, TileId.GlacialOre) > 0 && CountTiles(bigA, TileId.UraniumOre) > 0 && CountTiles(bigA, TileId.PyrociteOre) > 0 &&
              CountTiles(bigA, TileId.EnergyCrystal) > 0 &&
              CountTiles(bigA, TileId.LifeCore) == bigFeatures.LifeCores.Count && bigFeatures.LifeCores.Count >= 10 &&
              CountTiles(bigA, TileId.SupplyCache) > 10 && CountTiles(bigA, TileId.SalvagePod) > 10 &&
              bigFeatures.Islands.Count >= 1 && bigFeatures.Derelict is not null && CountTiles(bigA, TileId.ShipDoor) > 0 &&
              naturalWalls.Count >= 6 && walledAir > 10000 &&
              bigA.World.Cells.ToArray().Any(cell => cell.LiquidKind == LiquidKind.Lava) &&
              bigA.World.Cells.ToArray().Any(cell => cell.LiquidKind == LiquidKind.Water) &&
              bigSpawnGround is TileId.Grass or TileId.JungleGrass && !bigFeatures.BlightZones.Any(zone =>
                  bigA.SpawnColumn >= zone.Left && bigA.SpawnColumn <= zone.Right) &&
              BiomeScanner.Scan(bigA.World, new GridPoint(zoneCenter, zoneSurface - 1)).Zone == BiomeZone.RadiatedDeeps &&
              bigFeatures.PassMilliseconds.Count >= 25,
            "large world generates every biome, blight zones with hearts and altars, loot, structures and natural walls per seed");

        var overgrown = new GameSimulation(seed: 515, width: 480, height: 200, blight: WorldBlight.Overgrowth);
        var overZone = overgrown.Features!.BlightZones[0];
        var overCenter = (overZone.Left + overZone.Right) / 2;
        Check(overgrown.Blight == WorldBlight.Overgrowth &&
              CountTiles(overgrown, TileId.OvergrowthSoil, TileId.OvergrowthRock) > 500 &&
              CountTiles(overgrown, TileId.SporeHeart) >= 8 && CountTiles(overgrown, TileId.CrimsonSoil) == 0 &&
              CountTiles(overgrown, TileId.RootAltar) > 0 &&
              BiomeScanner.Scan(overgrown.World, new GridPoint(overCenter, overgrown.BiomeLayout.SurfaceAt(overCenter) - 1)).Zone == BiomeZone.Overgrowth &&
              BiomeScanner.DisplayName(BiomeZone.Overgrowth) == "Overgrowth" &&
              BiomeSpawnTables.UsesTable(BiomeZone.Overgrowth) &&
              Enumerable.Range(0, 20).All(i => BiomeSpawnTables.KindsFor(BiomeZone.Overgrowth)
                  .Contains(BiomeSpawnTables.Pick(BiomeZone.Overgrowth, i / 20d, i % 2 == 0))) &&
              BiomeSpawnTables.KindsFor(BiomeZone.RadiatedDeeps).Contains(EnemyKind.GlowStalker),
            "an Overgrowth world grows overgrown chasms, spore hearts and root altars that read as the Overgrowth zone");

        // Blight spread: grass only before the Fracture, never near a beacon, everything convertible after.
        var blighted = new GameSimulation(seed: 777, width: 160, height: 72, blight: WorldBlight.Radiated);
        TileId[] columnTiles = [TileId.Grass, TileId.Soil, TileId.Rock, TileId.Sand, TileId.Snow, TileId.Grass, TileId.Rock];
        void BuildPatch(int left)
        {
            for (var y = 1; y <= 11; y++)
            for (var x = left - 3; x <= left + 9; x++)
                blighted.World.SetTile(new GridPoint(x, y), TileId.Air);
            for (var y = 3; y <= 7; y++)
            for (var i = 0; i < columnTiles.Length; i++)
                blighted.World.SetTile(new GridPoint(left + i, y), columnTiles[i]);
            blighted.World.SetTile(new GridPoint(left + 3, 5), TileId.CrimsonSoil);
            // A bedrock shelf under the patch so its loose sand cannot fall away while the ticker runs.
            for (var i = 0; i < columnTiles.Length; i++)
                blighted.World.SetTile(new GridPoint(left + i, 8), TileId.Bedrock);
        }
        BuildPatch(10);
        BuildPatch(40);
        BuildPatch(70);
        blighted.World.SetTile(new GridPoint(43, 9), TileId.PurifierBeacon);
        for (var i = 0; i < 400; i++)
        {
            blighted.Ticker.UpdateCell(13, 5);
            blighted.Ticker.UpdateCell(43, 5);
        }
        bool ColumnIs(int x, TileId tile) => Enumerable.Range(3, 5).All(y => y == 5 && x is 13 or 43 or 73
            ? true : blighted.World.GetCell(x, y).TileId == tile);
        Check(ColumnIs(15, TileId.CrimsonSoil) && ColumnIs(11, TileId.Soil) && ColumnIs(12, TileId.Rock) &&
              ColumnIs(13, TileId.Sand) && ColumnIs(14, TileId.Snow) && ColumnIs(10, TileId.Grass) &&
              blighted.BeaconPositions.Contains(new GridPoint(43, 9)) && ColumnIs(45, TileId.Grass) &&
              !blighted.Ticker.TrySpreadInto(45, 4) &&
              BlightRules.CanBlightReplace(TileId.Grass, WorldBlight.Radiated, fractured: false) &&
              !BlightRules.CanBlightReplace(TileId.Rock, WorldBlight.Radiated, fractured: false) &&
              BlightRules.CanBlightReplace(TileId.Rock, WorldBlight.Radiated, fractured: true) &&
              !BlightRules.CanBlightReplace(TileId.ReactorCore, WorldBlight.Overgrowth, fractured: true),
            "blight spreads only into grass before the Fracture and never within a purifier beacon's reach");

        // The Fracture: the core gate boss (summonable once the last easy boss is down) → flag, scars, faster spread
        // into every convertible tile.
        var stripedBefore = CountTiles(blighted, TileId.CrimsonSoil, TileId.CorruptRock, TileId.RadiatedSand, TileId.RadiatedIce);
        var fractureBoss = blighted.SpawnCreature(GameSimulation.CoreGateBoss, blighted.Player.Motion.Position + new Vector2(30f, -6f));
        fractureBoss.TakeDamage(fractureBoss.MaximumHitPoints);
        blighted.Tick(new GameInput(new MovementInput(0f)));
        for (var i = 0; i < 600; i++)
        {
            blighted.Ticker.UpdateCell(73, 5);
            blighted.Ticker.UpdateCell(43, 5);
        }
        Check(GameSimulation.LastEasyBoss == EnemyKind.ScoutMech && blighted.FractureActive && blighted.Ticker.Fractured &&
              // 0.3.0: worlds have far more caves now, so the scar meets more open air on this 160×72 test world
              // (it converted 51 cells before, 39 after); 25 still proves the scar was carved.
              blighted.FractureCellsConverted > 25 &&
              CountTiles(blighted, TileId.CrimsonSoil, TileId.CorruptRock, TileId.RadiatedSand, TileId.RadiatedIce) > stripedBefore + 50 &&
              ColumnIs(71, TileId.CrimsonSoil) && ColumnIs(72, TileId.CorruptRock) && ColumnIs(73, TileId.RadiatedSand) &&
              ColumnIs(74, TileId.RadiatedIce) && ColumnIs(75, TileId.CrimsonSoil) && ColumnIs(45, TileId.Grass) && ColumnIs(42, TileId.Rock) &&
              !blighted.TriggerFracture(),
            "defeating the core gate boss fractures the world: flag set, scars carved, spread reaches soil, rock, sand and ice");

        // Purification: area purge and an actual sprayer shot.
        var purified = blighted.PurifyArea(new GridPoint(73, 5), 3f);
        var shooter = new GameSimulation(seed: 778, width: 160, height: 72, blight: WorldBlight.Overgrowth);
        var shooterCenter = shooter.Player.Motion.Position + shooter.Player.Motion.Size / 2f;
        var wallX = (int)MathF.Floor(shooterCenter.X) + 4;
        var aimY = (int)MathF.Floor(shooterCenter.Y);
        for (var y = aimY - 3; y <= aimY + 3; y++)
            shooter.World.SetTile(new GridPoint(wallX, y), TileId.OvergrowthRock);
        shooter.Player.Inventory.Add(ItemId.PurifierSprayer, 1);
        shooter.Player.Inventory.Add(ItemId.PurifierGel, 5);
        shooter.TrySelectInventorySlot(shooter.Player.Inventory.FindSlot(ItemId.PurifierSprayer));
        shooter.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, new GridPoint(wallX, aimY)));
        for (var i = 0; i < 40; i++)
            shooter.Tick(new GameInput(new MovementInput(0f)));
        var sprayerRecipes = new[] { ItemId.PurifierSprayer, ItemId.PurifierGel, ItemId.PurifierBeacon }
            .All(id => shooter.Crafting.FindByOutput(id).Any());
        Check(purified > 0 && Enumerable.Range(70, 7).All(x => Enumerable.Range(3, 5).All(y =>
                  !BlightRules.IsBlightTile(blighted.World.GetCell(x, y).TileId) || (x - 73) * (x - 73) + (y - 5) * (y - 5) > 9)) &&
              blighted.World.GetCell(73, 5).TileId == TileId.Soil && blighted.World.GetCell(72, 5).TileId == TileId.Rock &&
              shooter.Player.Inventory.Count(ItemId.PurifierGel) == 4 &&
              shooter.World.GetCell(wallX, aimY).TileId == TileId.Rock && sprayerRecipes &&
              !shooter.Tiles[TileId.PurifierBeacon].IsSolid,
            "purifier gel and the area purge turn blight back into natural ground; purifier gear is craftable");

        // Hearts: every third break summons the blight's boss.
        var heartSim = new GameSimulation(seed: 779, width: 160, height: 72, blight: WorldBlight.Radiated);
        var heartBosses = new List<int>();
        for (var h = 0; h < 6; h++)
        {
            var heartTile = h < 3 ? TileId.ReactorCore : TileId.SporeHeart;
            var hx = 20 + h * 5;
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
                heartSim.World.SetTile(new GridPoint(hx + dx, 3 + dy), heartTile);
            heartSim.ForceBreakTile(new GridPoint(hx + 1, 4));
            heartBosses.Add(heartSim.Enemies.Count(enemy => enemy.IsBoss));
        }
        Check(heartSim.HeartsBroken == 6 && heartBosses.SequenceEqual(new[] { 0, 0, 1, 1, 1, 2 }) &&
              heartSim.Enemies.Any(enemy => enemy.Kind == EnemyKind.FalloutHivemind) &&
              heartSim.Enemies.Any(enemy => enemy.Kind == EnemyKind.PlantOvergrowth) &&
              CountTiles(heartSim, TileId.ReactorCore, TileId.SporeHeart) == CountTiles(new GameSimulation(seed: 779, width: 160, height: 72,
                  blight: WorldBlight.Radiated), TileId.ReactorCore, TileId.SporeHeart) &&
              heartSim.Player.Inventory.Count(ItemId.UraniumOre) > 0 && heartSim.Player.Inventory.Count(ItemId.CobaltOre) > 0 &&
              heartSim.Player.Inventory.Count(ItemId.ViridiumOre) == 0,
            "breaking blight hearts drops loot and the third heart summons the Fallout Hive-mind (radiated, before the Fracture) or Plant Overgrowth");

        // Altars: warded until the Fracture, then each smash seeds high-tier ore.
        for (var dy = 0; dy < 2; dy++)
        for (var dx = 0; dx < 2; dx++)
            heartSim.World.SetTile(new GridPoint(60 + dx, 3 + dy), TileId.RadAltar);
        var lockedAltar = !heartSim.ForceBreakTile(new GridPoint(60, 3)) && heartSim.World.GetCell(60, 3).TileId == TileId.RadAltar;
        heartSim.Player.Inventory.Add(ItemId.CopperPickaxe, 1);
        heartSim.TriggerFracture();
        var altarOreBefore = CountTiles(heartSim, GameSimulation.AltarOreCycle[0]);
        var smashed = heartSim.ForceBreakTile(new GridPoint(61, 4));
        Check(lockedAltar && smashed && heartSim.AltarsSmashed == 1 && heartSim.LastAltarOreSeeds.Count >= 3 &&
              CountTiles(heartSim, GameSimulation.AltarOreCycle[0]) > altarOreBefore &&
              Enumerable.Range(0, 4).All(i => heartSim.World.GetCell(60 + i % 2, 3 + i / 2).TileId == TileId.Air) &&
              heartSim.RecentMessages.Any(message => message.Contains("viridium", StringComparison.OrdinalIgnoreCase)),
            "blight altars refuse every drill before the Fracture and seed the first hardmode ore (viridium) when smashed after it");

        // Active liquids: a poured column settles into a level pool; the set drains and stays bounded.
        var basin = new TileGrid(60, 30);
        var basinTiles = new TileCatalog();
        for (var x = 0; x < 60; x++)
            basin.SetTile(new GridPoint(x, 25), TileId.Rock);
        for (var y = 0; y < 25; y++)
        {
            basin.SetTile(new GridPoint(0, y), TileId.Rock);
            basin.SetTile(new GridPoint(59, y), TileId.Rock);
        }
        var basinLiquid = new ActiveLiquidSimulator(basin, basinTiles);
        for (var y = 5; y < 15; y++)
            basin.TryAddLiquid(new GridPoint(30, y), LiquidKind.Water, 1f, basinTiles);
        var poured = basin.TotalLiquidDepth();
        var basinSteps = basinLiquid.RunUntilSettled(3000);
        var poolDepths = Enumerable.Range(1, 58).Select(x => basin.GetCell(x, 24).LiquidDepth).ToArray();
        var panicBasin = new ActiveLiquidSimulator(basin, basinTiles) { PanicThreshold = 4 };
        for (var x = 20; x < 30; x++)
            basin.TryAddLiquid(new GridPoint(x, 2), LiquidKind.Water, 0.5f, basinTiles);
        panicBasin.RunUntilSettled(3000);
        basinLiquid.RunUntilSettled(3000);
        Check(basinSteps < 3000 && basinLiquid.ActiveCount == 0 && Math.Abs(basin.TotalLiquidDepth() - poured - 5) < 0.01 &&
              Math.Abs(poolDepths.Sum() - poured) < 0.01 && poolDepths.Zip(poolDepths.Skip(1)).All(pair => MathF.Abs(pair.First - pair.Second) <= 0.025f) &&
              basinLiquid.PeakActiveCount <= 400 && panicBasin.FallbackSettles >= 1 && panicBasin.ActiveCount == 0,
            "the active liquid set levels a poured pool, conserves volume, stays bounded and falls back to a world settle when flooded");

        // Saves: RLE + Deflate cells round-trip a Large world exactly and dwarf the JSON cell array.
        var bigSnapshot = bigA.CaptureSnapshot();
        var jsonCellBytes = System.Text.Json.JsonSerializer.Serialize(bigA.World.Cells.ToArray(), snapshotJson).Length;
        var bigJson = System.Text.Json.JsonSerializer.Serialize(bigSnapshot, snapshotJson);
        var bigRestored = GameSimulation.Restore(System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>(bigJson, snapshotJson)!);
        Check(bigSnapshot.Cells is null && bigSnapshot.CompressedCells is { Length: > 0 } packedCells &&
              CellCodec.Decode(packedCells, bigA.World.Count).AsSpan().SequenceEqual(bigA.World.Cells) &&
              bigRestored.World.Cells.SequenceEqual(bigA.World.Cells) && bigRestored.Blight == WorldBlight.Radiated &&
              bigJson.Length * 40L < jsonCellBytes && bigSnapshot.FormatVersion == GameSnapshot.CurrentFormatVersion,
            "compressed cell saves round-trip a Large world exactly at a small fraction of the JSON cell size");

        var oldStyle = new GameSimulation(seed: 789, width: 80, height: 48);
        oldStyle.World.SetTile(new GridPoint(5, 5), TileId.Wood);
        var oldSnapshot = oldStyle.CaptureSnapshot() with
        {
            Cells = oldStyle.World.Cells.ToArray(), CompressedCells = null, Blight = null, Hazard = null,
            FractureActive = null, HeartsBroken = null, AltarsSmashed = null, LifeCoresUsed = null
        };
        var oldLoaded = GameSimulation.Restore(System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(oldSnapshot, snapshotJson), snapshotJson)!);
        Check(oldLoaded.World.Cells.SequenceEqual(oldStyle.World.Cells) && oldLoaded.Blight is null &&
              !oldLoaded.FractureActive && oldLoaded.Player.LifeCoresUsed == 0,
            "older snapshots with uncompressed JSON cells still load");

        // Rooms: a local rebuild after an edit matches a full rebuild.
        static bool SameRooms(GameSimulation sim, RegionMap local)
        {
            var full = new RegionMap(sim.World, sim.Tiles);
            full.EnsureCurrent();
            var pairing = new Dictionary<int, int>();
            for (var y = 0; y < sim.World.Height; y++)
            for (var x = 0; x < sim.World.Width; x++)
            {
                var a = local.GetRegion(new GridPoint(x, y));
                var b = full.GetRegion(new GridPoint(x, y));
                if ((a is null) != (b is null))
                    return false;
                if (a is null || b is null)
                    continue;
                if (a.IsRoom != b.IsRoom)
                    return false;
                if (!a.IsRoom)
                    continue;
                if (a.CellCount != b.CellCount || a.ShelteredCells != b.ShelteredCells || a.OpenRoofCells != b.OpenRoofCells)
                    return false;
                if (pairing.TryGetValue(a.Id, out var paired) ? paired != b.Id : pairing.ContainsValue(b.Id))
                    return false;
                pairing[a.Id] = b.Id;
            }
            return true;
        }
        var dirtyGame = new GameSimulation(seed: 1234, width: 80, height: 48);
        dirtyGame.Regions.EnsureCurrent();
        var fullBuilds = dirtyGame.Regions.FullBuildCount;
        for (var x = 30; x <= 36; x++)
        for (var y = 3; y <= 8; y++)
            dirtyGame.World.SetTile(new GridPoint(x, y), x is 30 or 36 || y is 3 or 8 ? TileId.Wood : TileId.Air);
        var localRoom = dirtyGame.Regions.GetRoomAt(new Vector2(33.5f, 5.5f));
        var smallMatches = SameRooms(dirtyGame, dirtyGame.Regions);
        dirtyGame.World.SetTile(new GridPoint(33, 3), TileId.Air);
        var reopenedMatches = SameRooms(dirtyGame, dirtyGame.Regions);
        bigA.Regions.EnsureCurrent();
        var bigFullBuilds = bigA.Regions.FullBuildCount;
        var digX = bigA.SpawnColumn + 40;
        var digY = bigA.BiomeLayout.SurfaceAt(digX) + 30;
        for (var x = digX; x <= digX + 6; x++)
        for (var y = digY; y <= digY + 5; y++)
            bigA.World.SetTile(new GridPoint(x, y), x == digX || x == digX + 6 || y == digY || y == digY + 5 ? TileId.Wood : TileId.Air);
        var bigLocalWatch = System.Diagnostics.Stopwatch.StartNew();
        bigA.Regions.EnsureCurrent();
        bigLocalWatch.Stop();
        Check(localRoom is { IsRoom: true, CellCount: 20 } && smallMatches && reopenedMatches &&
              dirtyGame.Regions.FullBuildCount == fullBuilds && dirtyGame.Regions.BuildCount > fullBuilds &&
              bigA.Regions.FullBuildCount == bigFullBuilds && bigA.Regions.LastIncrementalWork < 20_000 &&
              bigA.Regions.GetRoomAt(new Vector2(digX + 3.5f, digY + 2.5f)) is { CellCount: 20 } &&
              SameRooms(bigA, bigA.Regions),
            $"dirty-region room rebuilds match a full rebuild ({bigLocalWatch.Elapsed.TotalMilliseconds:0.0} ms on a Large world)");

        // Life cores: +10 maximum hit points each, saved with the world.
        var coreGame = new GameSimulation(seed: 4567, width: 80, height: 48);
        var baseMax = coreGame.Player.MaximumHitPoints;
        var coreCell = new GridPoint(10, 2);
        coreGame.World.SetTile(coreCell, TileId.LifeCore);
        coreGame.ForceBreakTile(coreCell);
        coreGame.Player.Inventory.Add(ItemId.LifeCore, 1);
        coreGame.TrySelectInventorySlot(coreGame.Player.Inventory.FindSlot(ItemId.LifeCore));
        coreGame.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected));
        var usedViaItem = coreGame.Player.LifeCoresUsed == 1;
        coreGame.UseLifeCore();
        var coreLoaded = GameSimulation.Restore(System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(coreGame.CaptureSnapshot(), snapshotJson), snapshotJson)!);
        Check(usedViaItem && coreGame.Player.LifeCoresUsed == 2 && coreGame.Player.MaximumHitPoints == baseMax + 20f &&
              coreLoaded.Player.LifeCoresUsed == 2 && coreLoaded.Player.MaximumHitPoints == baseMax + 20f &&
              TileDropTable.GetDrop(TileId.LifeCore) == ItemId.LifeCore,
            "life cores mined from the world raise maximum hit points by 10 each and persist in saves");

        // Blight progress persists.
        var progressLoaded = GameSimulation.Restore(System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(heartSim.CaptureSnapshot(), snapshotJson), snapshotJson)!);
        Check(progressLoaded.HeartsBroken == 6 && progressLoaded.AltarsSmashed == 1 && progressLoaded.FractureActive &&
              progressLoaded.Ticker.Fractured && progressLoaded.Blight == WorldBlight.Radiated,
            "hearts broken, altars smashed and the Fracture survive a save round-trip");

        ContentSelfTests.Run(Check);
        BatchBSelfTests.Run(Check);
        BatchASelfTests.Run(Check);
        ArsenalSelfTests.Run(Check);
        DefenseSelfTests.Run(Check);
        CreatureDefenseSelfTests.Run(Check); // creature defense tiers, armor penetration, no double hardmode rescale, immunities, lava, banners (0.1.9 A)
        WeaponStyleSelfTests.Run(Check);
        WorldGenSelfTests.Run(Check);
        ThemeSelfTests.Run(Check);
        ParityCombatTests.Run(Check);
        ParityBuildTests.Run(Check);
        ParityMovementTests.Run(Check);
        ParityWorldTests.Run(Check);
        HardmodeSelfTests.Run(Check);
        ProgressionChainSelfTests.Run(Check);
        WorldLeisureSelfTests.Run(Check);
        PlayerSystemsSelfTests.Run(Check);
        TravelSelfTests.Run(Check);
        ContentFillSelfTests.Run(Check);
        ModesSelfTests.Run(Check); // town tracks, modes and progression achievements (G13-G16)
        ControlsParitySelfTests.Run(Check); // Terraria's controls: mouse state, slots, cursor item, auto-reuse, keys (docs/CONTROLS_PARITY.md)
        GrowthSelfTests.Run(Check); // character growth: lifebloom, sixth slot, upgrades, glide rigs, hooks, creative kit (G3, G7-G11)
        StarterKitSelfTests.Run(Check); // the Terraria-style starting kit (Classic ferrite trio, Creative cobalt drop)
        AudioSceneSelfTests.Run(Check); // music and ambience players: Terraria-style choice, fallbacks, looping streams
        SoundCatalogSelfTests.Run(Check); // everything that needs a sound has a cue, and every cue falls back to a sound (docs/AUDIO_FLOW.md)
        SpawnSafetySelfTests.Run(Check); // no sky island over the landing site, no spawning into a cave under a plant
        ToolCombatSelfTests.Run(Check); // tools are melee weapons too: picks, hatchets and hammers hurt creatures in their swing
        GearCurveSelfTests.Run(Check); // Terraria-shaped gear tiers: drills open the next ore, armour and weapon curves, copper-tier starting kit
        StatParitySelfTests.Run(Check); // stat formulas and tool mechanics (docs/GEAR_DIFF_MECHANICS.md)
        ToolParitySelfTests.Run(Check); // tool rules matched to the reference (docs/TOOLS_PARITY.md)
        FloraParitySelfTests.Run(Check); // flora rules matched to the reference (docs/FLORA_PARITY.md)
        ItemAuditSelfTests.Run(Check); // every item has a function and a sprite of its own (docs/ITEM_AUDIT.md)
        CreatureCoverageSelfTests.Run(Check); // every biome, event and boss has its creatures, leaders and summons (docs/CREATURE_COVERAGE.md)
        CraftingGuideSelfTests.Run(Check); // crafting guidance: recipe guide lookups, "close to" recipes, tooltips, the station AI's next steps
        PlayerDebuffSelfTests.Run(Check); // 0.1.9 combat core: creature debuffs, afflictions, contact boxes, hurt variance (stream B)
        CraftingAuditSelfTests.Run(Check); // crafting progression: a felled tree builds the first station; every station and item is reachable (docs/CRAFTING_PROGRESSION.md)
        ProgressionGateSelfTests.Run(Check); // 0.2.2 stream B: the Leviathan waits for the Fracture, transitive stations, the sealed temple, no tier 6+ before the Fracture
        SoundEventSelfTests.Run(Check); // 0.1.9 feedback: simulation sound events, despawns are not kills, dust under the cap, crit records (PE-4/5/7/14)
        TooltipSelfTests.Run(Check); // 0.1.9 IC-17: the truthful item tooltip (class damage, real ammo, calibration, sets, sell value)
        HealingLadderSelfTests.Run(Check); // 0.1.9 C1: healing and charge ladders, their gates, potion sickness, quick heal, cooked food
        DropRuleSelfTests.Run(Check); // 0.1.9 C2-C5: drop chances and conditions, event drops, hearts and orbs, boss potions, 9999 stacks
        LightingSelfTests.Run(Check); // 0.2.0 C1 (PE-11): coloured lighting on High is a per-channel multiply; Standard unchanged
        ColonyWorkSelfTests.Run(Check); // 0.2.1: work priorities, the Guard, Tend and Haul job givers, staying home, the Work tab
        StationBillsSelfTests.Run(Check); // 0.2.3: station bills, the Operate job giver, the Bills tab
        StorageSelfTests.Run(Check); // 0.2.4: storage tags, delivery by tag, residents working bills
        FarmingSelfTests.Run(Check); // 0.2.5: grow zones, the Farm job giver, Brew bills at chem labs, the Farm tab
        KitchenSelfTests.Run(Check); // 0.2.6: Cook bills, eating from storage, crew fishing
        ResearchSelfTests.Run(Check); // 0.2.7: research projects (Build removed in 0.2.9)
        InspectSelfTests.Run(Check); // 0.2.8: colonist bar, inspect cards
        TradeSelfTests.Run(Check); // 0.2.9: Trade
        ColonyRulingSelfTests.Run(Check); // 0.2.2 E: no auto-tend, no player needs, harmless breaks, unhappy residents stay, HUD, resident HP (NC-1/2/7/9/16/20)
        CreativeResearchSelfTests.Run(Check); // 0.2.2 E: research on the character, old world flags migrated, weather and spread powers, the Creative kit (PC-13, X-CR-10, PC-14)
        BalanceSelfTests.Run(Check); // 0.2.0 A: the boss curve (--boss-balance), the 60-180 s band and D-6 difficulty scaling (PC-15, BO-23)
        SpriteMemorySelfTests.Run(Check); // 0.2.2 (0.1.7 A2, PE-2): lazy sprite loading: an index at load, decoded and scaled LRUs with byte budgets
        MenuParitySelfTests.Run(Check); // 0.2.2 (0.1.8 D3-D7, E1): map preferences and NPC heads, title feats and credits, video rows, screen shake, audio latency
        SaveSafetySelfTests.Run(Check); // 0.2.2 D: save backups and migrations, the 0.1.6 fixture, threaded autosave, any character on any world, the HP-only player, minions, statue spawns (PU-11/12, PU-9, IC-4, IC-7, EN-6)
        BossSelfTests.Run(Check); // 0.2.2 C: boss and event lifecycle (BO-24 suite; PC-1/2/4, BO-6/9/17/18, EV-1/2, EN-7)
        Console.WriteLine($"{passed} checks passed.");
        return 0;
    }

    private static GridPoint FindReachableSolid(GameSimulation game, Vector2 center)
    {
        var bestDistance = float.MaxValue;
        var best = new GridPoint(-1, -1);
        var minX = Math.Max(0, (int)center.X - 4);
        var maxX = Math.Min(game.World.Width - 1, (int)center.X + 4);
        var minY = Math.Max(0, (int)center.Y - 4);
        var maxY = Math.Min(game.World.Height - 1, (int)center.Y + 4);
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var point = new GridPoint(x, y);
            var tileId = game.World.GetCell(point).TileId;
            if (tileId is TileId.Air or TileId.Bedrock)
                continue;
            var distance = Vector2.Distance(center, new Vector2(x + 0.5f, y + 0.5f));
            if (distance < bestDistance && distance <= 4.8f)
            {
                bestDistance = distance;
                best = point;
            }
        }
        if (best.X < 0)
            throw new InvalidOperationException("Test world had no reachable mineable tile.");
        return best;
    }

}
