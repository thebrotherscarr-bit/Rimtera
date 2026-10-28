# Controls parity: Terraria 1.4.5.8 vs Rimtera

Phase 1 was the research and diff below. **Phase 2 (2026-10-28) implemented it**; the status section here is current, and the
tables below keep the phase-1 findings ("Rimtera now" means before phase 2). Terraria is behavioural evidence; every fix is
Rimtera's own code.

## Phase 2 status (implemented)

| area | what Rimtera does now | where |
|---|---|---|
| One mouse state | `MouseButtonState` is built only from the canvas's MouseDown/MouseUp events, records who owns each press (world or interface), and is cleared on Deactivate and on lost mouse capture. The UI frames and the world use both read it; `Control.MouseButtons` is no longer read. A world press never reaches the UI and a UI press never uses the item. | `UI/PointerInput.cs`, `GraphicalSandbox.cs` (`OnCanvasMouseDown/Up`, `PointerFrame`, `ReleaseAllPointer`) |
| Player never acts on its own | The player has no brain: no auto-eat, no auto-rest, no "Mine" pick, no free joy or comfort near camp, no free 0.035/h base regen, no tending by crew medics. Health comes back only by Terraria's natural regen formula plus gear, buffs and items the player uses. | `GameSimulation.cs` (`UpdateAutonomousBehavior`, `UpdateTending`), `PlayerProgression.cs` (`PlayerStatsCalculator`) |
| Cursor (mouse) item | Lives on `Inventory.Cursor`; it is the held item while the hub is open (Terraria's slot 58) and uses spend it first; right-clicking the world throws it; closing the hub (or any state that isn't the hub) returns it to the bag, or drops it at the player if the bag is full; saves fold it back in. | `ItemSystem.cs` (`Inventory`), `GameSimulation.Controls.cs`, `GraphicalSandbox.cs` (`Advance`, `RightClickWorld`) |
| Slots | Left acts on the press (`ClickOnPress`); right takes one and repeats while held (30 ticks, then 7 and faster, `StackSplitRepeater`), with a stack in hand it pulls more; right press equips a single equippable; Shift moves to/from a chest or the open guide, otherwise nothing; Shift on the trash returns it; Ctrl trashes; Alt favourites; double-click quick-transfer removed. | `InventoryUi.cs`, `UiFramework.cs` |
| Auto-reuse | `ItemUseRules.AutoReuse` (rule counted from Terraria's items.csv): items without it need a fresh click (`UseButtonGate`). | `GameSimulation.Controls.cs`, `PointerInput.cs` |
| Hotbar changes | wait until the current use ends (`RequestHotbarSlot`, applied in `Tick`). | `GameSimulation.Controls.cs` |
| Shift auto-select | re-checked every tick while held and the mouse is over the world, across all 50 slots: demolisher for hearts/altars (Terraria's hammer tiles), cutter for trees/cacti/logs (axe tiles), drill otherwise, each only in reach; else a torch (glow sticks in liquid). Never while over UI. | `GameSimulation.SmartSelectSlot`, `GraphicalSandbox.UpdateSmartSelect` |
| Over UI | Shift/Ctrl don't fire smart actions over a panel; clicks on the HUD (hotbar, vitals, buffs, minimap, status, boss and event bars, the guide hint) never swing. | `GameCanvas.IsOverHud`, `PointerOverInterface` |
| Keys | All gameplay keys work with the hub open (Esc stays the hub's Back, `I` closes it, M closes it and opens the map). Defaults: loadouts F1-F3, camera zoom `=`/`-`, minimap zoom numpad only, map opacity PgUp/PgDn, F11 hide UI, Alt+Enter fullscreen, C creative menu. The recipe-guide action is gone (hub button and station AI only); profile version 5 migrates saved profiles and drops stale actions. | `PreferencesAndProfiles.cs` |
| Wheel | Hotbar in the world; crafting list or hovered list with the hub open; zoom on the full map. **Ctrl+wheel zoom removed** (user decision). | `GraphicalSandbox.cs` |
| Smaller | E always fires a hook (jump lets go); left+right cancel; T refuses favourites and waits for the swing; chest Rename (20 characters, saved); WASD pans and numpad +/- zooms the full map while the player stands still. | various |

**Deliberate differences kept:** T is bound to Throw (Terraria 1.4.5.8 leaves Throw unbound; T was its legacy default); `I` also opens the inventory; the backtick emote key; `/` toggles the key help; Rimtera has hunger, so the player must eat food themselves. Not done: chat (Enter), camera mode (F4), lighting/diagnostic F-keys, armor set abilities (Rimtera has none), selling with Ctrl-click (Rimtera's shop is its own screen), carrying the favourite flag through `ReturnCursor` (returned stacks are not favourites).

**Correction to phase 1:** Terraria's Shift strategy 4 in liquid picks a *waterproof light* (glow sticks, water torch, flare), not a bucket (`SmartSelect_PickToolForStrategy @158-379`). Phase 2 follows the IL.

**Tests:** `Systems/ControlsParitySelfTests.cs` (25 checks, hooked into `CoreSelfTests`). Updated old checks: the right-click half-split checks (Terraria takes one) and the panel-key list (the guide action no longer exists).

- **Terraria evidence:** `docs/terraria-evidence/full/cil/*.il.txt` (dumped for this pass with `scripts/dumpre.py`) and `scripts/lite.py` decompile-lite views. Citations read `Type::Method @IL-offset`. "Knowledge" means general Terraria knowledge that the dumps did not settle.
- **Rimtera line numbers** are from the working tree on 2026-10-28, which already has uncommitted edits to `UI/GraphicalSandbox.cs` and `UI/InventoryUi.cs` from the inventory/crafting agent. Re-grep before editing.
- **Status:** `OK` matches, `DIFF` behaves differently, `MISSING` has no equivalent, `EXTRA` is a Rimtera addition (keep unless noted), `BUG` is wrong in Rimtera's own terms.

## 0. Key settings defaults (Terraria)

| setting | Terraria default | evidence | Rimtera |
|---|---|---|---|
| Autopause | **off** | `Main::.cctor @3978 stsfld Main.autoPause = 0` | `Preferences.AutoPause` defaults false (`UI/PreferencesAndProfiles.cs:86`). OK |
| Smart cursor mode | **toggle** | `Main::.cctor @7460 cSmartCursorModeIsToggleAndNotHold = 1` | `SmartCursorMode.Toggle` (`PreferencesAndProfiles.cs:110`). OK |
| Favourite key | **Left Alt** | `Main::.cctor @7449 cFavoriteKey 'LeftAlt'`, `@7604 FavoriteKey = 164` | Alt (`InventoryUi.cs:603`). OK |
| Shift-click trash | **disabled** (`DisableLeftShiftTrashCan = true`) | `Options::.cctor @1` | Rimtera Shift-click trashes when there is no chest (`InventoryUi.cs:631`). DIFF |
| Ctrl-click quick trash | **enabled** (`DisableQuickTrash = false`) | `Options::.cctor @7` | Ctrl-click trashes (`InventoryUi.cs:609`). OK |
| Auto-reuse all items | **off** | `Main::.cctor @59 SettingsEnabled_AutoReuseAllItems = 0` | Every item repeats while held. DIFF (see 4.1) |
| Minimap style | **minimap (1)** | `Main::.cctor @3163 mapStyle = 1` | check `GameCanvas.MapView` default |
| Stack split delay | 30 ticks first, then `stackDelay = 7` and shrinking | `ItemSlot::RefreshStackSplitCooldown`, `Main::.cctor @6505` | none. MISSING |

## 1. Default keybinds

Terraria's keyboard preset comes from `PlayerInput::Reset @52..1040` (the same in all four presets). Legacy 1.2 names live in `Main::.cctor @7309..7594`, and `PlayerInput::ManageVersion_1_3` migrates them. The Rimtera catalog is `UI/PreferencesAndProfiles.cs:185-247` (`ControlCatalog`). Commit fbcdeec states the catalog follows "Terraria's PC layout" on purpose. Commit 637532d left Rimtera-only panels unbound on purpose.

| action | Terraria default (IL) | Rimtera default | status | fix |
|---|---|---|---|---|
| Use item | Mouse1 (`Reset @58`) | Left click (hard-wired, `GraphicalSandbox.cs:163-166`). `UseSelected` is bindable but unbound | OK | none |
| Alternate use / interact | Mouse2 (`@84`) | Right click (`GraphicalSandbox.cs:167`). `Interact` is unbound | OK | none |
| Up / Down / Left / Right | W / S / A / D (`@110..198`) | W S A D (`:189-192`) | OK | none |
| Jump | Space (`@214`) | Spacebar (`:193`) | OK | none |
| Inventory | Escape (`@240`) | Escape **and I** (`:214`) | EXTRA | keep I, or drop it for strict parity |
| Grapple | E (`@266`) | E (`:195`) | OK (behaviour differs, see 5) | |
| Smart select (auto-select) | LeftShift (`@292`) | Shift (`:198`) | OK (behaviour differs, see 4.6) | |
| Smart cursor | LeftControl (`@318`) | Ctrl (`:199`) | OK | |
| Quick mount | R (`@344`) | R (`:196`) | OK | |
| Quick heal / mana / buff | H / J / B (`@370..432`) | H / J (Quick **charge**, Rimtera's mana) / B (`:206-208`) | OK | |
| Map style | Tab (`@448`) | Tab (`:216`) | OK | |
| Full map | M (`@474`) | M (`:215`) | OK | |
| Minimap zoom in / out | **numpad Add / Subtract** (`@500..536`) | Add **and OemPlus** / Subtract **and OemMinus** (`:217-218`) | DIFF | Remove OemPlus/OemMinus from minimap zoom. Terraria uses them for view zoom (next row) |
| Map opacity up / down | PageUp / PageDown (`@552..588`) | none | MISSING | add MapAlphaUp/Down for the minimap and overlay (`Player::Update @3874..4017`, ±0.015 per tick) |
| Hotbar 1-10 | D1..D9, D0 (`@604..848`) | D1..D0 (`:235-239`) | OK | |
| View zoom out / in | **OemMinus / OemPlus** (`@864..900`) | none. Camera zoom is Ctrl+wheel (`GraphicalSandbox.cs:190-200`) and Settings → Zoom | DIFF | bind ViewZoomIn/Out to `=`/`-`, sharing `Preferences.Zoom`. (Phase 2: done, and Ctrl+wheel removed at the user's request) |
| Creative (Journey) menu | C (`@916`) | Creative opens from Settings only (`GraphicalSandbox.cs:926`) | MISSING | bind C to the creative menu when `game.IsCreative` |
| Loadout 1 / 2 / 3 | **F1 / F2 / F3** (`@942..994`) | unbound (`:210-212`) | DIFF | default F1-F3 (`RunAction` already handles `LoadoutN`, `GraphicalSandbox.cs:396`) |
| Camera mode | F4 (`@1020`) | none | MISSING | low priority. Could open a screenshot/capture mode later; otherwise note it as unsupported |
| Throw | **unbound** in 1.4.5.8 (absent from `Reset`). It was **T** in legacy configs (`Main::.cctor @7359 cThrowItem 'T'`, migrated by `ManageVersion_1_3 @157`) | T (`:200`) | EXTRA (deliberate, fbcdeec) | keep T. The real default throw path is right-clicking the world with an item on the cursor (section 2) |
| Dash | unbound; double-tap (`PlayerInput::.cctor` lists `Dash` with no `Reset` entry) | unbound, plus double-tap (`:194`) | OK | |
| Armor set ability | unbound; double-tap **Down** (`Player::Update @4455..4469`, `ReversedUpDownArmorSetBonuses = false`) | none | MISSING | only if Rimtera gains set abilities |
| Previous / next loadout, Lock-on | unbound on keyboard | none | OK | |
| Chat | Enter, hard-coded (`Main::DoUpdate @2241 DoUpdate_Enter_ToggleChat`) | none | MISSING | single-player chat is only commands and emotes; optional |
| Emotes | no key. Opened from the inventory's Emotes button (knowledge) | `` ` `` held + 1-8 (`:204`, `GraphicalSandbox.PlayerSystems.cs:18`) | EXTRA | keep |
| Hide UI | **F11** (`Main::DoUpdate_F11_ToggleUI`, key 122) | `/` toggles the key help only (`:226`) | DIFF | add F11 to hide the whole HUD. `/` key help stays an EXTRA |
| Frame rate | F10 (`Main::DoUpdate @2200`) | F10 (`:227`) | OK | |
| Lighting mode / diagnostics | F9 / F8 / F7 (`@2205..2215`) | none | MISSING | optional (F9 could cycle Settings → Lighting) |
| Fullscreen toggle | Alt+Enter (`@2225`) | none | MISSING | cheap to add: toggle `Preferences.ScreenMode` |
| Settings | the inventory's Settings button. Esc closes it | inventory Settings button. Esc with nothing bound opens settings (`GraphicalSandbox.cs:365-371`) | OK | |
| Menu navigation | arrows (gamepad/UI) | arrows, Enter/Space, Esc/Backspace (`:242-247`) | OK | |

## 2. Inventory-open behaviour

In Terraria, opening the inventory only sets `Main.playerInventory`. With autopause off, `Main::CanPauseGame` returns false for the inventory (`CanPauseGame @72-85`: the inventory pauses only when `autoPause`), so `Player.Update` runs fully.

| behaviour | Terraria | Rimtera now | status | fix |
|---|---|---|---|---|
| World keeps running | yes unless autopause (`CanPauseGame @72`) | runs behind the inventory and crew sheet when AutoPause is off (`GraphicalSandbox.cs:727-735`) | OK | |
| Move / jump / hotbar keys / quick keys work | yes. `TriggersSet::CopyInto` copies every trigger regardless of the inventory; hotbar keys are blocked only by chat, sign or chest-name editing (`Player::Update @4892..4912`) | only an allow-list (`IsPlayThroughAction`, `GraphicalSandbox.cs:666-669`): move, jump, dash, grapple, mount, quick keys, smart keys, throw, F10, hotbar. Map, MapStyle, minimap zoom, loadouts and Inventory itself go to the menu | DIFF | let every gameplay action through except text entry. M/Tab/zoom/loadouts must work with the inventory open (`TryOpeningFullscreenMap` closes the inventory, `Player::TryOpeningFullscreenMap @8`) |
| Left click on the world uses the held item; clicks over UI go to the UI | `controlUseItem = MouseLeft && !mouseInterface && !blockMouse` (`TriggersSet::CopyInto @463-482`); `delayUseItem` needs a release after a UI click (`@561-580`) | yes when the cursor is empty and the point is off panels (`GraphicalSandbox.cs:150-157`) | OK, except with an item on the cursor (next row) | |
| **Using the item on the cursor** | with `Main.mouseItem` set, the selected item becomes slot 58, the mouse item: `Player::PickItemSelectionOverride @10-17`. `dropItemCheck @140-158` copies it into `inventory[58]`. You swing or place straight from the cursor | a click with a non-empty `CursorStack` never reaches the world (`GraphicalSandbox.cs:150` requires `CursorStack.IsEmpty`) | MISSING | while the cursor holds a stack and the inventory is open, treat it as the held item for use, swing drawing and placement (consume from `CursorStack`) |
| **Right-click the world with an item on the cursor throws it** | `dropItemCheck @419-500`: `(mouseRight && !mouseInterface && mouseRightRelease) or !playerInventory`, and mouseItem not empty → `DropSelectedItem` (slot 58 = the mouse item) | not possible. The click goes to the UI and is ignored | MISSING | right-click off-UI with a cursor stack → spawn it as a ground item (`GroundItems.SpawnGroundItem`), throw velocity toward facing |
| Closing the inventory with an item on the cursor | `dropItemCheck @299-354`: `!playerInventory` and a mouse item → `GetOrDropItem(mouseItem, ReturnItemFromSlot)`, which returns it to the inventory or drops it if there is no room | `CloseMenu` (`GraphicalSandbox.cs:954`) and `MenuFlow.Close` (`MenuSystem.cs:376`) discard the state; each `ShowStorage` makes a fresh `InventoryUiController` (`GraphicalSandbox.Screens.cs:26`), and no code returns `CursorStack` | BUG (likely item loss; verify) | on inventory close or state exit, put `CursorStack` back (`Inventory.TryAdd`) or drop it at the player |
| Mouse wheel with the inventory open | does **not** change the hotbar. `Player::Update @5694-5711`: when `playerInventory`, `Main.DoScrollingInInventory` runs instead of `HandleHotbarControls`, scrolling only the crafting grid/list or chest under the mouse | crafting scroll or UI scroll (`GraphicalSandbox.cs:209-211`) | OK (Terraria does not do what the brief assumed) | none |
| Hotbar keys with the inventory open | work | work when PlaysThroughMenu | OK | |
| Smart select / smart cursor keys over UI | ignored while `mouseInterface`: `controlTorch`/`controlSmart` are only copied when `!mouseInterface` (`CopyInto @99-120`), and the smart cursor toggle is skipped over UI (`Player::Update @3612-3627`). That frees Shift and Ctrl for slot clicks | `SmartSelect` and `SmartCursor` run on key down even over the inventory (`GraphicalSandbox.cs:334-343`), so **Ctrl-click to trash also toggles the smart cursor** and Shift-click also smart-selects | BUG | skip both actions while the mouse is over a panel |
| Autopause on | inventory, signs and any InGameUI pause (`CanPauseGame @72-117`) | the inventory and crew sheet pause | OK | |

## 3. Mouse item and slot interactions

Terraria: `ItemSlot::LeftClick`, `RightClick`, `OverrideHover`, `OverrideLeftClick`, `GetAlternateClickAction`. Rimtera: `InventoryUiController` (`InventoryUi.cs:7-85`) and `InventoryUiState.HandleSlotClick` (`InventoryUi.cs:597-654`).

| interaction | Terraria | Rimtera now | status | fix |
|---|---|---|---|---|
| When a click fires | on **press** (`LeftClick @34-46`: `mouseLeftRelease && mouseLeft`) | on **release over the same element** (`UiFramework.cs:317-335`) | DIFF | fire item-slot clicks on press (buttons can stay on release) |
| UI reads which input | the polled mouse state, once per frame (`Main.mouseLeft`) | `PointerFrame` reads `Control.MouseButtons`, the live hardware state, rather than the event's button (`GraphicalSandbox.cs:659-660`). World use reads the events (`MouseDown`/`MouseUp`). See finding A | BUG | one source of truth (finding A) |
| Left-click pickup / place / merge / swap | pickup to the mouse; place; same-type merge up to maxStack, keeping the rest on the mouse; otherwise swap. Favourite flags swap too (`LeftClick @357-629`) | the same (`InventoryUi.cs:11-31`) | OK | carry the favourite flag with the item (Rimtera keeps favourites per slot, `Inventory.IsFavorite(slot)`) |
| No slot clicks mid-use | `LeftClick @65-73` skips while `itemAnimation`/`itemTime` | allowed | DIFF (minor) | ignore slot clicks while a swing is in progress |
| Right-click with empty cursor | takes **one** into the mouse. Held, it repeats: first repeat after 30 ticks, then `stackDelay` (7) shrinking to `superFastStack` (`RightClick @308-422`, `RefreshStackSplitCooldown`) | takes **half** (`InventoryUi.cs:37-42`), once | DIFF | take one per tick-gated repeat while right is held; accelerate |
| Right-click with cursor holding the same item | takes one **more** from the slot into the mouse (`RightClick @345-385`) | puts one **into** the slot (`InventoryUi.cs:45-48`, Minecraft style) | DIFF | invert: right-click pulls from the slot onto the cursor |
| Right-click on equippable (stack 1) | swaps into the equipment slot (`RightClick @256-296 SwapEquip`); bags open (`@126-139`); `HasItemSwap` items convert (`@159-198`) | equips (`InventoryUi.cs:644-650`) | OK | add opening bags/crates on right-click if Rimtera has them |
| Shift-click, chest open | moves to the chest (`GetAlternateClickAction @402-445`) or from the chest (`@469-507`) | QuickTransfer both ways (`InventoryUi.cs:623-628`) | OK | |
| Shift-click, no chest | **nothing** for inventory items by default (`@446-457`: trash only when `DisableLeftShiftTrashCan` is off). Equipment and trash slots move back to the inventory (`@508-589`, `Unequip` / `TransferToBackpack`). Reforge/guide menus take it | equips it, or **trashes** it (`InventoryUi.cs:629-632`) | DIFF | no chest: do nothing for grid items; Shift-click on equipped gear unequips; Shift-click on the trash slot returns the item. Keep the guide hand-off (it matches `InGuideCraftMenu @367`) |
| Ctrl-click | sell when a shop is open, otherwise trash (`@49-93 GetSellOrTrash`); favourites are exempt (`@18`) | trash; favourites refuse (`InventoryUi.cs:609-612`) | OK | add sell-to-shop when a shop is open |
| Alt-click | toggle favourite (`OverrideHover @46-90`, `OverrideLeftClick @120-138`); with chat open it links the item | toggle favourite (`:603-607`) | OK | |
| Trash slot | holds one stack; the new trash replaces the old; Shift-click takes it back | `inventory-trash` slot: drop the cursor on it, or click to take it back (`InventoryUi.cs:322-340`) | OK | |
| Double-click | no special action (knowledge) | quick-transfers when a chest is open (`InventoryUi.cs:636-641`) | EXTRA | harmless, but with press-time clicks it will collide with pickup; drop it for parity |
| Drag and drop | none; click to pick, click to place | the same | OK | |
| Sort / quick stack to nearby | inventory buttons (knowledge) | Sort, Quick stack (`InventoryUi.cs:309-311`) | OK | |
| Chest buttons | Loot all, Deposit all, Quick stack, Restock, Sort, **Rename** (`ChestUI::LootAll`, `DepositAll`, `QuickStack`, `Restock`, `RenameChest`) | Loot all, Deposit all, Quick stack, Restock, Sort (`InventoryUi.cs:350-354`) | MISSING Rename | add Rename (`TextPromptState` exists) |
| Mouse wheel over a chest | scrolls the chest rows (`DoScrollingInInventory @150-268`) | n/a (chests fit on screen) | OK | |

## 4. Using items

### 4.1 Holding the button: auto-reuse and channel
| | Terraria | Rimtera now | status | fix |
|---|---|---|---|---|
| Hold left with an `autoReuse` item | repeats (`items.csv autoReuse`) | repeats | OK | |
| Hold left with a non-autoReuse item | one use; needs a release and a new click (`releaseUseItem`) unless the AutoReuseAllItems setting is on (default off) | `_mouseUseHeld` turns into `_use` **every tick** (`GraphicalSandbox.cs:748-754`). Tools respect `ToolUseProfile.AutoReuse` (`Systems/ToolParityUse.cs:59`), but weapons (`TryUseWeaponStyle`, `GameSimulation.WeaponStyles.cs:90-121`), placing, consumables and food repeat on the cooldown alone | DIFF | gate every `UseSelected` start on `item.AutoReuse` or a fresh press (reuse `_useHeldLastTick`). Add the AutoReuseAllItems setting, default off |
| Channel items (drills, beams, yo-yos) | use continues while held (`channel`) and stops on release | yo-yo and beam keep going while held (`WeaponStyles.cs:101-104`); channel drills handled (`ToolParityUse.cs:59`) | OK | |
| Hotbar change during a swing | buffered until the use ends (`SelectedItemState::get_CanChangeSelectedItemImmediately`: not while `UsingOrReusingItem` / `itemTime`) | immediate (`GameSimulation.cs:758`) | DIFF | buffer the slot change until the swing ends |

### 4.2 Facing and direction
| | Terraria | Rimtera now | status | fix |
|---|---|---|---|---|
| The player turns to the cursor when using | yes (`ItemCheck` sets `direction` from the mouse unless the item has `useTurn`; knowledge) | yes (`GameSimulation.cs:684-687`) | OK | |
| Swing arc toward the cursor | yes | yes (0.1.2 release note) | OK | |

### 4.3 No use over UI
| | Terraria | Rimtera now | status | fix |
|---|---|---|---|---|
| Inventory closed: click over HUD (hotbar, minimap, buffs, health) | `mouseInterface` is set over every HUD element, so no use; a hotbar click selects the slot | the hotbar selects (`GraphicalSandbox.cs:163`) and the guide hint is consumed (`:159`); clicks on the **minimap, buff icons or other HUD** fall through and swing | DIFF | one `IsOverHud(point)` covering every drawn HUD rect, checked before `_mouseUseHeld = true` |

### 4.4 Right-click alternate use and quick keys
| | Terraria | Rimtera now | status | fix |
|---|---|---|---|---|
| Right click on the world | `controlUseTile` (held state, `CopyInto @612-646`): tile interaction, and item alt-use (`ItemCheck_ManageRightClickFeatures`: shields, `altFunctionUse`) | `InteractAtCursor` once on press (`GraphicalSandbox.cs:167`, `:1042`) | DIFF (minor) | expose a "right held" input for items with an alternate use |
| Quick heal H | skips at full life (`QuickHeal @45`) and with potion sickness (`@54`); picks the potion that best covers missing life (knowledge); dismounts some mounts | the same rules (`GameSimulation.Player.cs:586-610`) | OK | |
| Quick mana J | first mana potion; skips at full mana (`QuickMana @52`) | QuickCharge: smallest restore; skips at full (`:614-628`) | OK (Rimtera resource) | |
| Quick buff B | uses every non-active buff potion (`QuickBuff`) | the same (`:631-646`) | OK | |
| Fresh-press only | `releaseQuickHeal` / `releaseQuickMana` edge (`Player::Update @3469-3534`) | key repeat filtered (`GraphicalSandbox.cs:377`) | OK | |

### 4.5 Throw (T in Rimtera)
| | Terraria | Rimtera now | status | fix |
|---|---|---|---|---|
| Throw the held stack | `dropItemCheck @355-412`: not when **favourited**, not with chat open; only when no item is in use (`Player::Update @5716-5741` gates `dropItemCheck` on `itemAnimation == 0 && itemTime == 0 && reuseDelay == 0`) | throws the selected slot (`GraphicalSandbox.cs:420-423`, `GroundItems.cs:134`). No favourite check, no in-use check | DIFF | refuse favourites; refuse mid-swing; with a cursor stack, throw the cursor stack |

### 4.6 Smart select (Shift) and smart cursor (Ctrl)
| | Terraria | Rimtera now | status | fix |
|---|---|---|---|---|
| Ctrl smart cursor | toggle by default; hold mode optional (`Player::TryToToggleSmartCursor`) | the same (`GraphicalSandbox.cs:411-419`, `:222-223`) | OK | not over UI (section 2) |
| Shift auto-select, re-evaluated | **every frame while held**, as a selection override that ends on release (`Player::PickItemSelectionOverride @25`, `SmartSelectLookup`) | picked **once on key down** (`BeginSmartSelect`, `GraphicalSandbox.cs:540-555`); it does not follow the cursor | DIFF | re-run the lookup each tick while Shift is held |
| Which tool | strategy by the target tile (`SmartSelect_GetToolStrategy`): hammer-able → hammer (1), axe-able → axe (2), else pick (3), in liquid → waterproof light: glow sticks, water torch, flare (4; phase 1 wrongly said bucket), cursor far (>200 px) → torch/flare (5), cannon → ammo (6), extractinator → feedable item (7), moss → paint scraper (8); **no tile → torch or glowstick** (strategy 0, `SmartSelect_PickToolForStrategy @29-54`) | cutter, then drill, then demolisher (wall), hotbar only | DIFF | add: torch on empty air or out of range; hammer for hammer tiles (platforms, slopes); glow sticks in liquid |
| Search range | **all 50 inventory slots** (`PickToolForStrategy` loops to 50) | hotbar 0-9 only (`:547`) | DIFF | search the whole main inventory |
| Over UI | ignored (`CopyInto @99`) | runs | BUG | see section 2 |

### 4.7 Torches
| | Terraria | Rimtera now | status | fix |
|---|---|---|---|---|
| Shift with nothing to dig → torch | yes (above) | no | MISSING | part of the 4.6 fix |
| Biome torches swap on placement | optional setting (`Player::BiomeTorchPlaceStyle`) | n/a | n/a | |

## 5. Movement

| | Terraria | Rimtera now | status | fix |
|---|---|---|---|---|
| Left and right held together | **cancel** (`Player::Update @3583-3602`: both cleared) | left wins (`GraphicalSandbox.cs:747`) | DIFF | horizontal = right − left |
| S drops through platforms | `controlDown` on platforms | `MovementInput.DropThrough` (`Entities/PawnMovement.cs:19`) | OK | |
| Ropes: W/S climb, jump lets go | yes | `Vertical` climbs (`PawnMovement.cs:110-114`) | OK | |
| Double-tap dash | 15-tick window after a release (`Player::Update @4692-4726 doubleTapCardinalTimer = 15`) | 250 ms (`GraphicalSandbox.cs:522-532`) | OK | |
| Double-tap / hold Down | armor set ability (`KeyDoubleTap`, `KeyHoldDown` ≥ 60 ticks) | none | MISSING | only if set abilities are added |
| Grapple E | **fires another hook on every press** (`QuickGrapple`: old hooks over the limit are killed, `@814-832`); a press never releases | a second press **releases** (`GraphicalSandbox.cs:425-426`) | DIFF | E re-fires (`TryFireGrapple` already replaces the hook); only jumping releases |
| Jump releases hooks | yes | yes (`GameSimulation.Player.cs:271-280`) | OK | |
| Mount R | `QuickMount` on a fresh press (`@3717-3744`) | toggles (`GraphicalSandbox.cs:430-433`) | OK | |
| Confused swaps the controls | `@4317-4370` | check `Statuses` | — | |

## 6. Map and camera

| | Terraria | Rimtera now | status | fix |
|---|---|---|---|---|
| M fullscreen map | closes the inventory, NPC chat and signs; not while dead (`TryOpeningFullscreenMap`, `Player::Update @4107-4187`) | opens (`GraphicalSandbox.cs:451`); not reachable with the inventory open | DIFF | allow from the inventory and close it |
| In the full map: WASD pans, the wheel zooms, the player stops | `@3071-3349` (controls zeroed, map panned); wheel `@5563-5666` | wheel zooms, drag pans (`:183-188`, `:126-130`); other keys ignored; the player stops (`:742-746`) | DIFF (minor) | add WASD panning |
| Map click | pylon teleport on left click (knowledge) | pylon teleport (`GraphicalSandbox.Travel.cs:133`) | OK | |
| Tab map style | cycles 0→1→2 (`@4028-4096`) | the same (`:452-455`) | OK | |
| Minimap zoom +/- | numpad Add/Subtract: ×1.025 on the minimap, ×1.05 on the overlay, ×1.05 full (`@3754-3969`) | `ZoomMinimap` on Add/Subtract **and** `=`/`-` | DIFF | see section 1 |
| Map opacity PageUp/PageDown | yes | none | MISSING | |
| Camera zoom | `=`/`-` keys (ViewZoomIn/Out); no wheel zoom | Ctrl+wheel (`:190-200`) | EXTRA plus MISSING keys | add `=`/`-` (phase 2: done; Ctrl+wheel removed at the user's request) |

## 7. Pause, Esc and menus

| | Terraria | Rimtera now | status | fix |
|---|---|---|---|---|
| Esc | `Inventory` trigger → `Player::ToggleInv`: closes, in order, the full map, the settings window, fancy UI, camera mode, NPC chat (and the inventory), sign, clothes window, crafting UI; otherwise toggles the inventory (`ToggleInv @11-337`) | inventory (`:214`). In menus, MenuBack (Esc) steps back (`GraphicalSandbox.cs:344-355`) | OK, mostly | make Esc from Settings opened from the inventory return to the game, as `IngameOptions.Close` does (verify the current behaviour) |
| Settings pause | **always** pauses single-player (`CanPauseGame @9-15 ingameOptionsWindow`) | the pause menu stops the world (`runBehindMenu` only covers Inventory/Crew, `:727`) | OK | |
| Inventory pause | only with autopause (`@72-85`) | the same | OK | |
| Window loses focus | gamePaused; mouse releases cleared (`DoUpdate @2083-2117 FocusHelper`) | `Deactivate` clears keys only (`GraphicalSandbox.cs:230-234`); `_mouseUseHeld` survives | BUG | clear `_mouseUseHeld`, smart-select state and drag on Deactivate (finding B) |
| Chat (Enter) | opens chat; while it is open, movement uses `CopyIntoDuringChat` (`@4215-4249`) | none | MISSING (optional) | |

## 8. Findings requested by the lead

### A. Inventory buttons read the real mouse state, not the window's mouse events
- **Rimtera:** `PointerFrame` (`UI/GraphicalSandbox.cs:659-660`) builds every UI frame with `LeftDown: (MouseButtons & MouseButtons.Left) != 0`, where `MouseButtons` is the static `Control.MouseButtons`, the **live hardware button state**, rather than `e.Button` from the event being handled. `UiRoot.Update` (`UI/UiFramework.cs:317-343`) fires a click only on a down→up *transition* of that flag on the same element. A synthesised or posted `WM_LBUTTONDOWN`/`UP` pair (automation, the guidance agent, remote desktop, touch injection, a button released after a focus change) delivers the events, but `Control.MouseButtons` never reads "down", so no transition happens and the button is never pressed. The craft-hold repeat has the same dependency (`GraphicalSandbox.Guide.cs:469`). Meanwhile the world path uses the events (`_mouseUseHeld` set in `MouseDown`, cleared in `MouseUp`), so the two halves can disagree.
- **Terraria:** reads the device once per frame (`Main.mouseLeft` from the polled state). UI and world use share that one flag, and `mouseLeftRelease` gives edges. Item slots act on the **press** edge (`ItemSlot::LeftClick @34-46`).
- **Fix:** keep `_leftDown`/`_rightDown` fields set from `MouseDown` (`e.Button`) and cleared from `MouseUp`, `MouseCaptureChanged` and `Deactivate`. Build `PointerFrame` from those fields, not `Control.MouseButtons`, and use the same fields for `_mouseUseHeld` and the craft hold. Item slots then fire on the press edge; plain buttons may stay on release. Add a self-test that drives `UiRoot.Update` with event-derived frames.

### B. "Something moved and used the player while it stood idle"
What exists:
- **The player has a RimWorld-style brain.** `GameSimulation.cs:226-229` gives `Player.Brain` a `PrioritySelectorNode` with Food (eats when the food reserve is under 0.42 and berries are carried), Rest (reserve under 0.3 and no hostile within 8 tiles) and Work (`ActivityKind.Mine` whenever the facing tile is solid) nodes (`AI/SurvivalBehavior.cs:105-160`). `UpdateAutonomousBehavior` (`GameSimulation.cs:1912-1944`) runs **every tick, unconditionally**. It **auto-eats a berry from the inventory** (`:1924-1930`) and **marks the player as resting** (`:1931-1936`; `Player.Health.IsResting` at `:655` speeds healing). `ActivityKind.Mine` is chosen but nothing acts on it, so the brain does not dig. Terraria's player never does any of this.
- **Why the player chopped, dug and opened a chest:** no autonomous code mines, chops or opens containers for the player. The likely mechanism is a **stuck use flag**. `_mouseUseHeld` latches on `MouseDown` and clears only on `MouseUp` over the canvas (`GraphicalSandbox.cs:154,166,178`). It is not cleared on `Deactivate` (`:230-234`), on menu transitions, or when the up event is lost (finding A: posted clicks, or releases outside the window). While it stays latched, `Advance` turns it into Mine/Use **every tick** (`:748-754`). With the smart cursor on, `AimTile` → `SmartCursor.Resolve(..., useHeld: true)` (`:793-800`, `UI/SmartCursor.cs:28`) keeps choosing the next tree or tile in reach. That looks exactly like the player chopping and digging by itself. Chest opening is a right click (`InteractAtCursor`, `:1042`), which posted right clicks reach directly through `MouseDown`.
- **Recommendation:** (1) Make the player brain **off by default**: don't assign `Player.Brain` (or skip `UpdateAutonomousBehavior` for the player) unless an explicit, labelled opt-in setting is on (e.g. "Auto-eat when hungry"). Keep needs decaying; eating stays a deliberate use. Crew colonists keep their brains. (2) Fix A, and clear `_mouseUseHeld` on `Deactivate`, `MouseCaptureChanged`, menu open/close and death. Better, sample the held state from the event-tracked fields each tick, as Terraria polls. (3) Add a self-test: 600 ticks of `GameInput` with no action must leave the world, the inventory and the needs-driven consumption unchanged.

## 9. Ordered fix list for phase 2

1. Single mouse-state source (A), plus clearing the held state on deactivate and capture loss (B, part 2). This removes ghost clicks and phantom chopping.
2. Player brain off by default, opt-in only (B, part 1).
3. Cursor-stack parity: use the cursor item in the world, right-click off-UI throws it, return it on close (also fixes the likely item loss).
4. Shift/Ctrl not acting as smart keys while over UI.
5. Right-click take-one and hold-to-split with acceleration; right-click with a held stack pulls one more. Slot clicks on press.
6. Shift-click without a chest does nothing for grid items; Shift-click unequips gear and returns trash.
7. Non-autoReuse items need a fresh click; buffer hotbar changes mid-swing.
8. Shift auto-select re-evaluated every tick, whole inventory, torch, hammer and waterproof-light strategies.
9. HUD clicks never swing (minimap, buffs).
10. Inventory open: let Map, MapStyle, minimap zoom and loadouts through (M closes the inventory).
11. Keybinds: Loadouts F1-F3; `=`/`-` for camera zoom (minimap zoom on numpad only); PageUp/PageDown map opacity; F11 hide UI; C creative; Alt+Enter fullscreen.
12. Grapple E re-fires instead of releasing; left+right cancel; throw refuses favourites and mid-swing; chest Rename; WASD panning on the full map.
