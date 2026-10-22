# Phase 11 — UI, Menu, Inventory, and Interface-State Specification

## 1. Evidence identity and extraction boundary

- Target: `C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe`
- Assembly identity: Terraria 1.4.5.8; MVID `2c29f6c3-4bd9-4add-9c58-da159804e083`.
- SHA-256: `960a03bff6050cf7be16dfc1a7b19e10fc2c4f8f835a6a3b135a50dd9e6ba2f3`.
- REA static member evidence: `ev_522e2e61fab2d7268a65f0260ed99182bb9df8a3264c710aaf0e255abe11b662`.
- `docs\Decompiled_Terraria` was not present when checked. This sheet is from PE/CLI metadata and file-backed CIL anchors, not from that directory.
- Target code was not loaded or executed. The REA result provides member metadata and selected CIL anchors, not a complete C# reconstruction or every CIL instruction. Values below are marked observed or explicitly unresolved.

## 2. UI type and property matrix

| Component | Observed structure / properties |
|---|---|
| `Terraria.UI.UIElement` | `Parent`; `Elements`; `UniqueId`; `Top`, `Left`, `Width`, `Height`; `MinWidth`, `MaxWidth`, `MinHeight`, `MaxHeight`; `PaddingTop/Left/Right/Bottom`; `MarginTop/Left/Right/Bottom`; `HAlign`, `VAlign`; `_outerDimensions`, `_dimensions`, `_innerDimensions`; `IsMouseHovering`; `IgnoresMouseInteraction`, `PassThroughMouseInteraction`, `OverflowHidden`; event delegates for left/right mouse down/up/click/double-click, mouse-over/out, scroll, update and draw. |
| `Terraria.UI.UIState` | Inherits `UIElement`; field `NoGamepadSupport`. |
| `Terraria.UI.UserInterface` | `_currentState`, `_history`, `IsVisible`, `MousePosition`, `LeftMouse`, `RightMouse`, `_lastElementHover`, `_clickDisabledTimeRemaining`, `_isStateDirty`; state APIs include `SetState`, `GoBack`, `Update`, `Draw`, and click dispatch. |
| `UIWorldSelect` | `NewlyGeneratedWorld`, `_worldList`, `_backPanel`, `_newPanel`, `_containerPanel`, `_scrollbar`, `_isScrollbarAttached`, `favoritesCache`, `skipDraw`. |
| `UICharacterSelect` | `_playerList`, `_backPanel`, `_newPanel`, `_containerPanel`, `_scrollbar`, `_isScrollbarAttached`, favorites cache, `skipDraw`. |
| `UICharacterCreation` | `_player`, `_validClothStyles`, `_defaultHairstylesForClothStyle`, `_validVoiceStyles`, `_colorPickers`, `_selectedPicker`, `_currentColorHSL`, `_topContainer`, `_middleContainer`, `_hslContainer`, `_hairstylesContainer`, `_clothStylesContainer`, `_infoContainer`, `_charName`, `_pitchSlider`, plus preview/color/voice fields. Builder methods: `BuildPage`, `MakeCharPreview`, `MakeHairstylesMenu`, `MakeClothStylesMenu`, `MakeCategoriesBar`, `MakeInfoMenu`, `MakeHSLMenu`, `MakeBackAndCreatebuttons`. |
| `UIItemSlot` | `_itemArray`, `_itemIndex`, `_itemSlotContext`; `HandleItemSlotLogic`, `DrawSelf`; constructor assigns array/index/context and sets `Width=48px`, `Height=48px`. |
| `ItemSlot` | Slot-context threshold fields include `canFavoriteAt`, `retainFavoriteOnMoveAt`, `canLoadoutShareAt`, `canShareAt`, `canQuickDropAt`, `canItemSwapAt`; `singleSlotArray`; `OnItemTransferred`; glow hue/time storage. Operations include `Handle`, `LeftClick`, `RightClick`, `PickupItemIntoMouse`, `MouseItemSwap`, `TryItemSwap`, `AfterItemSwap`, `Draw`, `DrawItemIcon`, and `MouseHover`. |
| `ChestUI` | `buttonScaleMinimum`, `buttonScaleMaximum`, `ButtonScale`, `ButtonHovered`, `StartingRowForDrawing`, `LastHighestChestRow`, `LastChestDisplayRectangle`; operations include `Draw`, `DrawSlots`, `Scroll`, `LootAll`, `DepositAll`, `QuickStack`, `Restock`, `TryPlacingInChest`. |

## 3. Menu/state tree and observed layout values

### 3.1 Main menu ownership

- `Terraria.Main` contains `MenuUI`, `_characterSelectMenu`, and `_worldSelectMenu` state references.
- Main-menu drawing is routed through `Terraria.Main.DrawMenu(GameTime)` (method body: 33,019 IL bytes; 12,148 decoded instructions in the static record). The selected state inventory contains no `UIMainMenu : UIState`; do not model the legacy main menu as the same tree as world/character selection without additional evidence.
- Interface layer registration uses `Main.SetupDrawInterfaceLayers`; observed ordered entries include `Vanilla: Ingame Options`, `Vanilla: Fancy UI`, `Vanilla: Inventory`, `Vanilla: Hotbar`, `Vanilla: Mouse Text`, and the other named layers in that method.

### 3.2 World and character selection

`UIWorldSelect.OnInitialize` and `UICharacterSelect.OnInitialize` share the following observed root/panel pattern:

1. Root `UIElement`: width `(0px, 0.8 parent-width)`, `MaxWidth=650px`, `Top=220px`, height `(-220px, 1.0 parent-height)`, `HAlign=0.5`.
2. `UIPanel` child: width `100%`, height `(-110px, 100%)`, background base RGB `(33,43,79)` multiplied by `0.8`.
3. `UIList`: fills the panel, `ListPadding=5`; attached to a `UIScrollbar` via `UIList.SetScrollbar`.
4. Scrollbar: `SetView(100,1000)`, height `100%`, `HAlign=1`.
5. Header: localized `UI.SelectWorld` or `UI.SelectPlayer`; `UITextPanel<T>` scale `0.8`, `HAlign=0.5`, top `-40px`, padding `15px`, background RGB `(73,94,171)`.
6. Back panel: localized `UI.Back`, text scale `0.7`, width `(-10px,50%)`, height `50px`, `VAlign=1`, `HAlign=0`, top `-45px`. Mouse-over/out and left-click delegates are attached.
7. New panel: localized `UI.New`, text scale `0.7`, copies the back-button style, then sets `HAlign=1`; it receives mouse-over/out and left-click callbacks (`NewWorldClick` / `NewCharacterClick`). Character select also registers gamepad snap labels `Back` and `New`.

The values above are `StyleDimension.Set` arguments `(pixels, percent)` in the CIL anchors. They are not a claim that every display resolution yields these exact final rectangles.

### 3.3 Character creator

`UICharacterCreation.BuildPage` first calls `RemoveAllChildren`, then builds:

- Root `UIElement`: width `500px`, height `380px`, top `220px`, `HAlign=0.5`, `VAlign=0`, padding `0`.
- Main `UIPanel`: width `100%`, height `150px`, top `50px`, background `(33,43,79) * 0.8`, padding `0`.
- A width-100% child with 50px height and top padding `4px`; a second width-100% child uses a 70px height with a 6px top offset and 3px top padding.
- Build order observed in calls: back/create buttons; character preview; info menu; HSL menu; hairstyles; clothing styles; categories; character-info click setup.

The character body/name/color/style state is held by the `UICharacterCreation` fields above; detailed preview rendering is delegated to the `Make*` methods.

## 4. Geometry, hover, click, and draw dispatch

### 4.1 Layout/recalculation

- `UIElement.GetDimensionsBasedOnParentDimensions` reads `Left`, `Top`, `Width`, `Height`, min/max dimensions, margins, and `HAlign`/`VAlign`; it calls `StyleDimension.GetValue` and `MathHelper.Clamp` for width/height.
- `UIElement.Recalculate` chooses the parent element's inner dimensions or the active interface dimensions, derives outer/dimensions/inner rectangles from margins and padding, then calls `RecalculateChildren`.
- Each `UIElement` stores three calculated rectangles: `_outerDimensions`, `_dimensions`, `_innerDimensions`. Child layout is a recursive tree walk through `Elements`.
- Exact `StyleDimension.GetValue` arithmetic is not expanded by the selected anchors; the stored scale/pixels inputs and clamp calls are observed, while a fully expanded renderer equation remains unverified here.

### 4.2 Bounding box and pointer state

- `UIElement.ContainsPoint(Vector2)` delegates to `Rectangle.Contains` on `_dimensions`; hover is therefore tested against the recalculated element rectangle, not only the sprite's opaque pixels.
- `UserInterface.HandleClick` tracks `InputPointerCache.WasDown`, `LastDown`, `LastClicked`, `LastTimeDown`, `MouseDownEvent`, `ClickEvent`, `DoubleClickEvent`, and `MouseUpEvent`.
- Observed double-click branch: same prior clicked element and elapsed `GameTime.TotalGameTime.TotalMilliseconds < 500`; `>=500` exits that branch.
- Pointer dispatch order in the anchored path: mouse-down event; optional double-click; on release, if the recorded down element still contains `MousePosition`, invoke click; then invoke mouse-up; clear/update pointer-down state.
- Text labels are constructed through `UIText`/`UITextPanel<T>` and localized via `Language.GetText`; direct legacy interface text uses `DynamicSpriteFontExtensionMethods.DrawString`.

## 5. Inventory/chest/hotbar interaction structures

### 5.1 Slot item transfer state

- `UIItemSlot` binds an array, array index, and item-slot context; drawing/logic forwards to the `Terraria.UI.ItemSlot` routines.
- `ItemSlot.MouseItemSwap` calls `Utils.Swap` between `Main.mouseItem` and the addressed slot's item. When `retainFavoriteOnMoveAt` is false, the two items' `favorited` flags are cleared in the observed branch.
- `ItemSlot.LeftClick` checks pending inventory actions and mouse release state, dispatches overrides, obtains a slot movement action, then handles pickup/swap/stacking/equip notification branches. Observed data dependencies include `Item.type`, `stack`, `maxStack`, `favorited`, `CanStack`, `Main.mouseItem`, and transfer announcements. The method is 2,658 IL bytes; this summary does not replace its full branch table.
- `ItemSlot.RightClick` handles openable items, optional `TryItemSwap`, vanity/equipment cases, and partial stack pickup; the exact flow is context-dependent and remains in the method's full static evidence.

### 5.2 Chest grid

`ChestUI.DrawSlots` (571 IL bytes; 229 instructions) has these observed inputs and values:

- Chest source: `Main.player[Main.myPlayer].chest`; chest IDs `-2`, `-3`, `-4`, `-5` select the player's `bank`, `bank2`, `bank3`, `bank4` arrays respectively.
- `Main.inventoryScale=0.755` while building `LastChestDisplayRectangle`.
- Display rectangle arguments include `x=73`, `y=Main.invBottom`, `width=56*inventoryScale`, `height=56*inventoryScale`.
- Uses `StartingRowForDrawing`, clamps/updates it, records `LastHighestChestRow`, calls `ItemSlot.PrepareForChest`, then iterates rows/slots and calls `ItemSlot.Handle` and `ItemSlot.Draw`.
- `10` appears as an IL constant at method entry; its semantic role is not assigned from the selected anchors.

### 5.3 Player inventory and hotbar

- `Main.DrawInventory` is a legacy drawing/interaction routine (11,041 IL bytes; 3,863 instructions). It reads `mouseX`, `mouseY`, `inventoryScale`, player inventory/equipment/bank arrays, updates hover flags, and calls `ItemSlot.Handle`/`Draw`; chest rendering delegates to `ChestUI.Draw`.
- `Main.DrawInterface_27_Inventory` checks `playerInventory`, player state and creative-menu state, then calls `Main.DrawInventory`.
- `Main.DrawInterface_30_Hotbar` delegates to `Main.GUIHotbarDrawInner`.
- `Main.GUIHotbarDrawInner` reads `playerInventory`, `hotbarScale`, mouse coordinates, and player item slots; it uses `ItemSlot.Draw`. Observed constants in that method include `20`, `22`, `75`, `150`, `13`, `36`, and scale step `0.05`; their exact geometric role is not assigned without full IL/source reconstruction.
- Static slot partition members include `InventoryItemSlotsStart/Count`, `InventoryCoinSlotsStart/Count`, `InventoryAmmoSlotsStart/Count`, `InventorySlotsTotal`; the selected data does not expose their runtime values.

## 6. Structural logic arrays

```text
Layout pass: parent inner dimensions (or interface viewport)
  -> resolve child Left/Top/Width/Height from StyleDimension inputs
  -> clamp computed Width/Height to Min/Max
  -> compute outer rectangle using margins
  -> compute inner rectangle using padding
  -> recurse through Elements

Pointer pass: pointer position + down/up state
  -> hit-test UIElement._dimensions via Rectangle.Contains
  -> dispatch down/over/out callbacks
  -> on release, click only if LastDown still contains pointer
  -> double-click when same LastClicked target and elapsed ms < 500
  -> update InputPointerCache state

Slot interaction: ItemSlot.Handle(context)
  -> inspect slot/item + Main.mouseItem + input/context thresholds
  -> route left/right click or swap handler
  -> update type/stack/favorite state and transfer notifications
  -> draw through ItemSlot.Draw
```

## 7. Unresolved exacts

- The absent `Decompiled_Terraria` directory was not parsed.
- Complete main-menu button tree, exact inventory/hotbar rectangle formulas, all slot-context branches, and exact text/font draw arguments require full IL decompilation or the original source tree. They are not fabricated here.
- REA method tokens are build-local: use only with this artifact SHA-256 and MVID.

## 8. Creation, list-item, and generation-progress structure (REA 6.7.0, 2026-10-20)

Source: `docs/terraria-evidence/menu-flow.json` (`rea inspect-managed-members` on `Terraria.exe`, SHA-256 `960a03bf…a2f3`, coverage `complete` for the selected types). Member names and type shape only; no bodies, textures, strings, or tuning were copied.

- `UICharacterListItem` / `UIWorldListItem`: per-row `PlayGame`, `FavoriteButtonClick`, `RenameButtonClick`, `DeleteButtonClick` (with a separate delete label/confirm state), `CompareTo` (favourites sort first), and on worlds `_isNewlyGenerated` / `_hasBeenPlayedByActivePlayer` markers plus a `_worldIcon`.
- `UICharacterCreation`: `MakeCategoriesBar`, `MakeCharPreview`, `MakeInfoMenu`, `MakeHSLMenu`, `MakeHairstylesMenu`, `MakeClothStylesMenu`, `Click_RandomizePlayer`, `Click_NamingAndCreating`, `FinishCreatingCharacter`, and a `_difficultyDescriptionText` field — categories around a live preview, randomise, then name-and-create.
- `UIWorldCreation`: `AddWorldSizeOptions`, `AddWorldDifficultyOptions`, `AddWorldEvilOptions`, `AddDescriptionPanel` with `ShowOptionDescription`/`ClearOptionDescription`, `ClickRandomizeName`, `RandomizeSeed`, `UpdatePreviewPlate`, `FinishCreatingWorld`.
- `UIWorldLoad` holds `_progressBar` and `_progressMessage`; `UIGenProgressBar.SetProgress` keeps separate target and visual values for a current-pass bar and an overall bar; `WorldBuilding.GenerationProgress` exposes `Message`, `Value`, `TotalProgress`, `CurrentPassWeight`, `TotalWeight`.

Rimtera mapping: `UI/MenuSystem.cs` (card lists with favourite/rename/delete and a cancel-first delete confirmation; creation screens with option descriptions; world difficulty and dominant-hazard choices in place of the evil option), `UI/GraphicalSandbox.cs` (live character/world previews; generation/loading/saving screens), `UI/ScreenTransition.cs` (eased pass/overall progress, fade-in, respawn countdown), `UI/CharacterLook.cs` (appearance recolours of the Neo-Terra suit and face sprites).
