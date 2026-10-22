# Phase 13 — Main Settings, Preferences, Display, and Input Specification

## 1. Evidence identity and extraction boundary

- Target: `C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe`; Terraria 1.4.5.8.
- SHA-256: `960a03bff6050cf7be16dfc1a7b19e10fc2c4f8f835a6a3b135a50dd9e6ba2f3`; MVID `2c29f6c3-4bd9-4add-9c58-da159804e083`.
- REA static member evidence: `ev_ddbbd9b97bdbb94d189d0aa61db3ecc08c82a469b78420c5fb5f735e87ee874d`.
- `docs\Decompiled_Terraria` was not present. Data below comes from selected PE/CLI member metadata and CIL anchors; target code was never loaded or executed. Anchors are not a full C# decompilation.

## 2. Configuration ownership and files

- `Main.Configuration` is a `Terraria.IO.Preferences` instance. `Main..cctor` constructs it using a save-directory path ending in `config.json`.
- `Main.InputProfiles` is a second `Preferences` instance whose path ends in `input profiles.json`.
- `Main.LoadSettings` contains a legacy `config.dat` existence/migration path (`OpenLegacySettings`, `SaveSettings`, delete old file), then calls `Preferences.Load` on the current configuration.
- `Main.SaveSettings` clears and repopulates `Main.Configuration`, calls `Preferences.Save`, and calls `PlayerInput.Save`; `Main.LoadSettings` reads settings and calls `PlayerInput.Load`.
- `Terraria.IO.GameConfiguration` also exists (`_root`, `Get<T>(string)`), but `Main.Configuration` is the separate `Terraria.IO.Preferences` object. No `Terraria.GameContent.Configuration` type was found in the scanned type metadata.
- `Preferences` owns `_data: Dictionary<string,object>`, `_path`, `_serializerSettings`, `UseBson`, `_lock`, `AutoSave`, `OnSave`, `OnLoad`, and `OnProcessText`.
- Encoding is conditional on `UseBson`: false-path `Load` uses `File.ReadAllText` and JSON deserialization; false-path `Save` uses JSON serialization and `File.WriteAllText`; true-path uses `BsonReader` / `BsonWriter`. The `config.json` path literal is observed, but selected anchors do not establish the constructor's boolean argument values; runtime file encoding is therefore conditional, not asserted solely from its extension.

## 3. Preferences load/save state arrays

### 3.1 `Preferences.Load()` (216 IL bytes; 85 instructions)

```text
Monitor.Enter(_lock)
if File.Exists(_path) is false:
    continue with empty/default data
else if UseBson is false:
    text = File.ReadAllText(_path)
    _data = JSON-deserialize(text, _serializerSettings)
else:
    stream = File.OpenRead(_path)
    reader = BsonReader(stream)
    serializer = JsonSerializer.Create(_serializerSettings)
    _data = serializer.Deserialize(reader)
if _data is null: _data = new Dictionary<string, object>()
if OnLoad != null: invoke OnLoad(this)
Monitor.Exit(_lock) in the guarded exit path
```

### 3.2 `Preferences.Save(bool)` (325 IL bytes; 112 instructions)

```text
Monitor.Enter(_lock)
if OnSave != null: invoke OnSave(this)
if the boolean option selects the no-create path and the destination does not exist: return
ensure parent directory exists
if destination exists: set FileAttributes = 128 (Normal)
if UseBson is false:
    text = JsonConvert.SerializeObject(_data, _serializerSettings)
    if OnProcessText != null: text = OnProcessText(text)
    File.WriteAllText(_path, text)
    restore FileAttributes = 128
else:
    stream = File.Create(_path)
    writer = BsonWriter(stream)
    serializer = JsonSerializer.Create(_serializerSettings)
    serializer.Serialize(writer, _data)
finally: dispose guarded resources; Monitor.Exit(_lock)
```

`Preferences.Put(key,value)` locks `_data`, assigns dictionary entry, and calls `Save` when `AutoSave` is true. `Get<T>` uses dictionary lookup and converts the value to requested type; enum conversion and `Convert.ChangeType` calls are present. The selected method signature only exposes `Save(bool)`; the boolean's semantic name is unresolved.

## 4. Main settings/property matrix

| Config key | Main field/property observed | Type / notes |
|---|---|---|
| `VolumeSound` | `soundVolume` | `Single`; static initializer sets `1.0` |
| `VolumeAmbient` | `ambientVolume` | `Single`; static initializer sets `1.0` |
| `VolumeMusic` | `musicVolume` | `Single`; static initializer sets `1.0` |
| `GraphicsQuality` | `qaStyle` | setting scalar; `gfxQuality` is a separate runtime `Single`, static initializer `1.0` |
| `Fullscreen` | `graphics.IsFullScreen` / `startFullscreen` | Boolean display state |
| `WindowMaximized` | `screenMaximized` | Boolean |
| `WindowBorderless` | `screenBorderless` | Boolean |
| `DisplayWidth`, `DisplayHeight` | `LastLoadedResolution`, graphics back-buffer dimensions | integer resolution pair |
| `FrameSkipMode` | `FrameSkipMode` | enum-valued setting; `improvedSubtleFrameSkip` is a separate Boolean |
| `BackgroundEnabled` | `BackgroundEnabled` | Boolean |
| `LightingMode` | `Lighting.Mode` | enum-valued setting |
| `BackgroundParallax` | `bgScroll` | `Single` |
| `Zoom` | `GameZoomTarget` | `Single` |
| `UIScale` | `_uiScaleWanted` / default-scale fields | `Single` |
| `MapScale` | `MapScale` | `Single` |
| `AutoSave`, `AutoPause` | `autoSave`, `autoPause` | Boolean |
| `MouseShowBuildingGrid` | `MouseShowBuildingGrid` | Boolean |
| `KeyFavoriteModifier` | `cFavoriteKey` / `FavoriteKey` | key string / key code |
| `KeyUp`, `KeyDown`, `KeyLeft`, `KeyRight`, `KeyJump`, `KeyThrowItem`, `KeyInventory`, `KeyQuickHeal`, `KeyQuickMana`, `KeyQuickBuff`, `KeyUseHook`, `KeyAutoSelect`, `KeySmartCursor`, `KeyMount`, `KeyMapStyle`, `KeyFullscreenMap`, `KeyMapZoomIn`, `KeyMapZoomOut`, `KeyMapAlphaUp`, `KeyMapAlphaDown` | matching `Main.c*` binding fields | legacy key entries; player-profile input state is stored separately |

Additional observed config keys include `ResetDefaultUIScale`, `SmartCursorToggle`, `MapEnabled`, `InvasionBarMode`, `Language`, `PlacementPreview`, `GoreVisualsAllowed`, `WorldRollbacksToKeep`, `TeamNameplateDistance`, `MultiplayerNPCSmoothingRange`, `UnlockMusicSwap`, `UseExperimentalFeatures`, `ControllerGlyphs`, `ShowItemText`, `ClientUUID`, `UseSmartCursorForCommonBlocks`, `UseSmartAxeAfterSmartPickaxe`, `SmartCursorHoldCanReleaseMidUse`, `DisableLeftShiftTrashCan`, `DisableQuickTrash`, `HighlightNewItems`, `HidePasswords`, `ReverseUpDownForArmorSetBonuses`, `AllowUnfocusedInputOnGamepad`, `LockOnPriority`, `TitleMusic`, `InvisibleCursorForGamepad`, `GamepadDisableInstructionsDisplay`, `SettingsUnlock_WorldEvil`, `SettingsEnabled_MinersWobble`, `SettingBlockGamepadsEntirely`, `SettingsEnabled_OpaqueBoxBehindTooltips`, `SettingDontScaleMainMenuUp`, `SettingsEnabled_TilesSwayInWind`, `SettingsEnabled_AutoReuseAllItems`, `UseStormEffect`, `UseHeatDistortion`, `ForegroundSunlightEffects`, `WaveQuality`, `Support4K`, `MouseColor`, `MouseBorderColor`, `QuickLaunch`, `AssetFileWatcherEnabled`, `FlashIconForEvents`, `Display`, `DisplayScreen`, `ThrottleWhenInactive`, `UnpinFromCore0`, `DoorAutoOpeningMode`, `HoverControlMode`, `DashControlMode`, `CraftingControlMode`, `StackToChestsPreferredMode`, `CraftFromNearbyChests`, `WaterfallDrawLimit`, `FlashyEffectsWorld`, `FlashyEffectsInterface`, `PlayWhenUnfocused`, `UseScreenShake`, `ShowLoadoutShareHint`, `PortraitPreference`, and `SecretSeeds`.

Static initialization values observed in `Main..cctor`: `gfxQuality=1.0`, music/ambient/sound volumes `1.0`, `screenWidth=1152`, `screenHeight=864`, `PendingResolutionWidth=800`, `PendingResolutionHeight=600`. These are binary initializer values, not guaranteed user-config values after load.

## 5. Input profile and trigger structures

### 5.1 Profile/configuration matrix

| Type | Fields / members |
|---|---|
| `PlayerInputProfile` | `InputModes`, `Name`, `AllowEditing`, `HotbarRadialHoldTimeRequired`, `TriggersDeadzone`, `InterfaceDeadzoneX`, `LeftThumbstickDeadzoneX/Y`, `RightThumbstickDeadzoneX/Y`, thumbstick invert flags, `InventoryMoveCD`, `PanControl_Keyboard`, `PanControl_Gamepad`; `Load`, `Save`, per-mode conditional-add and copy methods. |
| `KeyConfiguration` | `KeyStatus: Dictionary<string,List<string>>`; `SetupKeys`, `Processkey`, `ReadPreferences`, `WritePreferences`, `CopyKeyState`; `DoGrappleAndInteractShareTheSameKey`. |
| `TriggersPack` | `Current`, `Old`, `JustPressed`, `JustReleased`; `Initialize`, `Reset`, `Update`, `CompareDiffs`. |
| `TriggersSet` | `KeyStatus: Dictionary<string,bool>`, `LatestInputMode`, `UsedMovementKey`, `HotbarScrollCD`, `HotbarHoldTime`; action properties include MouseLeft/Right, Up/Down/Left/Right, Jump, Throw, Inventory, Grapple, SmartSelect, SmartCursor, QuickMount/Heal/Mana/Buff, Loadout1/2/3, Dash, ArmorSetAbility, next/previous loadout, map controls, Hotbar1–10, HotbarMinus/Plus, radial hotbar/quickbar, D-pad radial/snap, menu navigation, LockOn, view zoom, creative powers, and camera mode. |
| `PlayerInput` | profiles/original profiles/current profile/input mode; trigger pack; buttons/gamepad stick vectors; `MouseInfo`, `MouseInfoOld`, `MouseX/Y`, scroll current/old/delta; mouse-key list; active/blocked bindings and rebinding state; UI/building cursor state; snap cooldowns; original screen/mouse caches; callbacks `OnBindingChange`, `OnActionableInput`. |

### 5.2 Per-frame input logic

`PlayerInput.UpdateInput` observed call sequence:

1. `SettingsForUI.UpdateCounters`; reset `TriggersPack` and zero cached vectors.
2. Resolve active gamepad/profile/shared grapple-interact setting and building mode.
3. Call `MouseInput`, then `KeyboardInput`, then `GamePadInput`.
4. `TriggersPack.Update`; call `PostInput`; call `UpdateMainMouse`.
5. Read mouse-left/right trigger states, cache zoomable values, then invoke actionable-input callback when present.

Device-specific evidence:

- `MouseInput`: polls `Mouse.GetState`; reads X/Y, scroll-wheel value, left/right/middle/X1/X2 buttons; updates deltas and cursor mode.
- `KeyboardInput`: starts from `GetPressedKeys`, checks rebinding, traverses binding lists/profile trigger mappings, and updates the trigger dictionary.
- `GamePadInput`: polls `GamePad.GetState`, checks `IsConnected` and focus policy, reads buttons and thumbsticks, then applies active-profile deadzones/inversion and routes rebinding/key mappings.
- `TriggersPack.CompareDiffs(Current,Old,JustPressed)` computes transition state; `JustReleased` is stored separately and populated by trigger comparison/reset logic. Exact boolean expression for every trigger is not expanded by this anchor-only record.

### 5.3 Profile persistence flow

```text
Main.SaveSettings
  -> Preferences.Clear / repeated Preferences.Put for settings
  -> Preferences.Save
  -> PlayerInput.Save
       -> InputProfiles.Clear / put profile-name selection and entries
       -> enumerate Profiles dictionary
       -> PlayerInputProfile.Save per profile
       -> Preferences.Save

Main.LoadSettings
  -> Preferences.Load
  -> repeated Preferences.Get for display/options/key entries
  -> PlayerInput.Load
       -> Preferences.Load / Get / GetAllKeys
       -> create and Initialize PlayerInputProfile objects
       -> PlayerInputProfile.Load per entry
       -> select saved profile or first available profile
```

`PlayerInput.Initialize` constructs and initializes multiple input profiles; `PresetProfiles` observed values are `None`, `Redigit`, `Yoraiz0r`, `ConsolePS`, and `ConsoleXBox`.

## 6. Runtime display/frame state fields

| Field | Type | Role evidenced by members/callers |
|---|---|---|
| `screenWidth`, `screenHeight` | `Int32` | active screen dimensions; initialized to 1152×864 |
| `PendingResolutionWidth`, `PendingResolutionHeight` | `Int32` | pending resolution pair; initialized to 800×600 |
| `screenMaximized`, `screenBorderless`, `startFullscreen`, `toggleFullscreen` | `Boolean` | fullscreen/window state input/output |
| `FrameSkipMode` | enum | saved/loaded via `FrameSkipMode` setting key; `CycleFrameSkipMode` mutates it |
| `improvedSubtleFrameSkip` | `Boolean` | separate frame pacing option |
| `frameRate`, `frameRelease`, `successiveSkippedDraws`, `TARGET_FRAME_TIME` | `Int32`, `Boolean`, `Int32`, `Double` | runtime update/draw timing state; not all are user settings |
| `soundVolume`, `ambientVolume`, `musicVolume` | `Single` | audio scalar settings, each initialized to 1.0 |
| `qaStyle`, `gfxQuality` | setting/display quality | `GraphicsQuality` reads `qaStyle`; `gfxQuality` is initialized to 1.0 |

## 7. Unresolved exacts

- The actual `Preferences` constructor Boolean arguments from `Main..cctor` are not established by selected anchors; file path names are observed, serializer behavior is conditional on `UseBson`.
- Exact controller-specific numeric matrices and all per-action profile defaults require full method disassembly; profile fields and device polling calls are observed.
- The `GameContent.Configuration` namespace named in the request was not found in the scanned metadata; `Terraria.IO.Preferences` is the verified owner of `Main.Configuration` in this build.
- Method/field tokens are build-local and require the recorded artifact hash/MVID.
