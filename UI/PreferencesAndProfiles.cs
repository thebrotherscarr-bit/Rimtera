using System.Text.Json;
using System.Text.Json.Serialization;
using Rimtera.Characters;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>Terraria's Frame Skip: On catches the world up after a slow frame, Subtle only after small hitches, Off slows the world instead.</summary>
public enum FrameSkipMode : byte { Off, On, Subtle }
public enum LightingQuality : byte { Low, Standard, High, Off }
public enum HealthBarPlacement : byte { Above, Below, Off }
public enum ParticleAmount : byte { Off, Low, High }
public enum SmartCursorMode : byte { Toggle, Hold }
public enum OptionsCategory : byte { General, Interface, Video, Volume, Cursor, Controls }
public enum WindowMode : byte { Windowed, Borderless, Fullscreen }
/// <summary>The in-world map style (Tab cycles it, as Terraria's map style key): the corner minimap, the full-screen overlay, or none.</summary>
public enum MapOverlayStyle : byte { Minimap, Overlay, Off }

public static class DisplayOptions
{
    public static readonly (int Width, int Height)[] StandardSizes =
    [
        (1024, 768),
        (1280, 720),
        (1366, 768),
        (1600, 900),
        (1920, 1080)
    ];

    public static string ModeLabel(WindowMode mode) => mode switch
    {
        WindowMode.Borderless => "Borderless fullscreen",
        WindowMode.Fullscreen => "Fullscreen",
        _ => "Windowed"
    };

    public static WindowMode CycleMode(WindowMode mode, int direction)
    {
        var values = Enum.GetValues<WindowMode>();
        var index = Array.IndexOf(values, mode);
        return values[(index + Math.Sign(direction) + values.Length) % values.Length];
    }

    public static int NearestSizeIndex(int width, int height)
    {
        var best = 0;
        var bestDistance = long.MaxValue;
        for (var i = 0; i < StandardSizes.Length; i++)
        {
            var size = StandardSizes[i];
            var distance = Math.Abs((long)size.Width - width) + Math.Abs((long)size.Height - height);
            if (distance >= bestDistance)
                continue;
            best = i;
            bestDistance = distance;
        }
        return best;
    }
}

/// <summary>Original Rimtera client settings; all user-facing scalars are clamped on load/save.</summary>
public sealed record GamePreferences
{
    public float SoundVolume { get; set; } = 1f;
    public float AmbientVolume { get; set; } = 1f;
    public float MusicVolume { get; set; } = 0.8f;
    public float GraphicsQuality { get; set; } = 1f;
    public int DisplayWidth { get; set; } = 1280;
    public int DisplayHeight { get; set; } = 720;
    public WindowMode ScreenMode { get; set; } = WindowMode.Windowed;
    public bool Fullscreen { get; set; }
    public bool WindowMaximized { get; set; }
    public bool WindowBorderless { get; set; }
    public FrameSkipMode FrameSkip { get; set; } = FrameSkipMode.On;
    public LightingQuality Lighting { get; set; } = LightingQuality.Standard;
    public void SetScreenMode(WindowMode mode)
    {
        ScreenMode = mode;
        Fullscreen = mode == WindowMode.Fullscreen;
        WindowBorderless = mode == WindowMode.Borderless;
    }
    public bool BackgroundEnabled { get; set; } = true;
    public float BackgroundParallax { get; set; } = 1f;
    public float Zoom { get; set; } = 1f;
    public float UiScale { get; set; } = 1f;
    public float MapScale { get; set; } = 1f;
    public bool AutoSave { get; set; } = true;
    /// <summary>Minutes between autosaves (2, 5, 10 or 15); the save itself runs off the game thread.</summary>
    public int AutosaveMinutes { get; set; } = 10;
    /// <summary>How much the camera eases after the player: 0 snaps to them every frame (Terraria's way), 1 is the softest.</summary>
    public float CameraSmoothing { get; set; }
    public static readonly int[] AutosaveChoices = [2, 5, 10, 15];
    public bool AutoPause { get; set; }
    /// <summary>A faint ghost of the block or wall where it would go, as Terraria's Placement Preview.</summary>
    public bool PlacementPreview { get; set; } = true;
    public bool ShowItemText { get; set; } = true;

    // General / Interface
    public bool DamageNumbers { get; set; } = true;
    public bool MinimapEnabled { get; set; } = true;
    /// <summary>PU-15: the map style Tab last chose, kept across restarts.</summary>
    public MapOverlayStyle MapView { get; set; } = MapOverlayStyle.Minimap;
    /// <summary>PU-15: screen pixels per map tile on the minimap and overlay (1-6; = and - on the map keys change it).</summary>
    public int MinimapScale { get; set; } = 2;
    /// <summary>PU-15: minimap and overlay opacity (PageUp / PageDown), kept across restarts.</summary>
    public float MinimapOpacity { get; set; } = 1f;
    public float OverlayOpacity { get; set; } = 0.55f;
    public bool ShowFrameRate { get; set; }
    /// <summary>The station AI's one-line "next step" hint at the top of the screen.</summary>
    public bool GuideHints { get; set; } = true;
    /// <summary>The crafting panel also lists recipes the player is close to (greyed, missing parts in red).</summary>
    public bool CraftingShowsAlmost { get; set; }
    public HealthBarPlacement EnemyHealthBars { get; set; } = HealthBarPlacement.Above;

    // Video
    public bool WeatherEffects { get; set; } = true;
    public bool FlashyEffects { get; set; } = true;
    public ParticleAmount Particles { get; set; } = ParticleAmount.High;
    /// <summary>PE-16: the camera shakes briefly for boss roars, explosions and heavy landings.</summary>
    public bool ScreenShake { get; set; } = true;

    // Cursor
    public int CursorColor { get; set; }
    public bool CursorBorder { get; set; } = true;
    public float CursorScale { get; set; } = 1f;
    public SmartCursorMode SmartCursor { get; set; } = SmartCursorMode.Toggle;

    public void Normalize()
    {
        SoundVolume = ClampFinite(SoundVolume, 1f, 0f, 1f);
        AmbientVolume = ClampFinite(AmbientVolume, 1f, 0f, 1f);
        MusicVolume = ClampFinite(MusicVolume, 0.8f, 0f, 1f);
        GraphicsQuality = ClampFinite(GraphicsQuality, 1f, 0.25f, 2f);
        DisplayWidth = Math.Clamp(DisplayWidth, 800, 7680);
        DisplayHeight = Math.Clamp(DisplayHeight, 600, 4320);
        if (!Enum.IsDefined(ScreenMode))
        {
            ScreenMode = Fullscreen
                ? WindowMode.Fullscreen
                : WindowBorderless ? WindowMode.Borderless : WindowMode.Windowed;
        }
        else if (ScreenMode == WindowMode.Windowed && (Fullscreen || WindowBorderless))
        {
            // Older profiles only stored the two booleans.
            ScreenMode = Fullscreen ? WindowMode.Fullscreen : WindowMode.Borderless;
        }
        Fullscreen = ScreenMode == WindowMode.Fullscreen;
        WindowBorderless = ScreenMode == WindowMode.Borderless;
        BackgroundParallax = ClampFinite(BackgroundParallax, 1f, 0f, 2f);
        CameraSmoothing = ClampFinite(CameraSmoothing, 0f, 0f, 1f);
        if (!AutosaveChoices.Contains(AutosaveMinutes))
            AutosaveMinutes = 10;
        Zoom = ClampFinite(Zoom, 1f, 0.5f, 2.5f);
        UiScale = ClampFinite(UiScale, 1f, 0.75f, 1.5f);
        MapScale = ClampFinite(MapScale, 1f, 0.5f, 2f);
        if (!Enum.IsDefined(FrameSkip)) FrameSkip = FrameSkipMode.On;
        if (!Enum.IsDefined(Lighting)) Lighting = LightingQuality.Standard;
        if (!Enum.IsDefined(EnemyHealthBars)) EnemyHealthBars = HealthBarPlacement.Above;
        // Background and parallax have no menu rows (0.1.3 took them out), so a value saved by an older build can't strand
        // anyone with them off. Weather, flashing effects and particles have rows again (0.2.2, PU-26) and keep their value.
        BackgroundEnabled = true;
        BackgroundParallax = 1f;
        if (!Enum.IsDefined(Particles)) Particles = ParticleAmount.High;
        if (!Enum.IsDefined(MapView)) MapView = MapOverlayStyle.Minimap;
        MinimapScale = Math.Clamp(MinimapScale, 1, 6);
        MinimapOpacity = ClampFinite(MinimapOpacity, 1f, 0.05f, 1f);
        OverlayOpacity = ClampFinite(OverlayOpacity, 0.55f, 0.05f, 1f);
        if (!Enum.IsDefined(SmartCursor)) SmartCursor = SmartCursorMode.Toggle;
        CursorColor = Math.Clamp(CursorColor, 0, CursorColors.Length - 1);
        CursorScale = ClampFinite(CursorScale, 1f, 0.75f, 1.5f);
        Zoom = ClampFinite(Zoom, 1f, 0.6f, 2f);
    }

    /// <summary>Cursor colour presets (name, RGB).</summary>
    public static readonly (string Name, byte R, byte G, byte B)[] CursorColors =
    [
        ("Amber", 242, 176, 64), ("Cyan", 90, 230, 255), ("Lime", 150, 240, 90), ("Rose", 255, 110, 150), ("White", 240, 240, 245)
    ];

    private static float ClampFinite(float value, float fallback, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

/// <summary>How the controls screen and the help overlay group actions.</summary>
public enum ControlGroup : byte { Movement, Actions, QuickUse, Panels, World, Hotbar, Menus }

/// <summary>
/// One rebindable action: its saved id, the name players see, its group, its default keys (binding names as
/// <see cref="InputBindingResolver.TryParseKeyCode"/> reads them) and a short hint. Menu actions live in their
/// own context, so a key may serve one menu action and one gameplay action at once.
/// </summary>
public sealed record ControlAction(string Id, string Label, ControlGroup Group, string[] Defaults, string Hint = "")
{
    /// <summary>Menu navigation keys work only while a menu is open, so they never clash with gameplay keys.</summary>
    public bool IsMenu => Id.StartsWith("Menu", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The single list of player controls. Gameplay input, the controls screen and the in-game help overlay all
/// read it, so a key shown anywhere is the key that works. Defaults follow Terraria's PC layout (WASD, Space
/// jump, Escape inventory, E grapple, T throw, Shift smart select, Ctrl smart cursor, R mount, H/J/B quick
/// heal/charge/buff, M map, Tab map style). As in Terraria, panels live in the inventory (crafting, crew,
/// housing, bestiary, feats, wires) and the world answers right clicks (beds, the station AI), so those actions
/// start unbound but can be given keys. Mouse buttons stay fixed, as in Terraria: left uses the held item, right
/// interacts (or throws the item on the cursor), the wheel scrolls the hotbar (with the inventory open it scrolls the
/// crafting list; on the full map it zooms). Camera zoom is on = and -, as Terraria's ViewZoomIn / ViewZoomOut.
/// Keyboard defaults are Terraria 1.4.5.8's (PlayerInput.Reset): see docs/CONTROLS_PARITY.md.
/// </summary>
public static class ControlCatalog
{
    public static readonly ControlAction[] All =
    [
        new("MoveLeft", "Move left", ControlGroup.Movement, ["A"]),
        new("MoveRight", "Move right", ControlGroup.Movement, ["D"]),
        new("MoveUp", "Up / climb", ControlGroup.Movement, ["W"]),
        new("MoveDown", "Down / drop through", ControlGroup.Movement, ["S"]),
        new("Jump", "Jump", ControlGroup.Movement, ["Spacebar"]),
        new("Dash", "Dash", ControlGroup.Movement, [], "Or double-tap left or right."),
        new("Grapple", "Grapple", ControlGroup.Movement, ["E"], "Fires a hook at the cursor. Jump to let go."),
        new("Mount", "Quick mount", ControlGroup.Movement, ["R"]),

        new("SmartSelect", "Smart select (hold)", ControlGroup.Actions, ["Shift"], "Switches to the right tool for what's under the cursor while held."),
        new("SmartCursor", "Smart cursor", ControlGroup.Actions, ["Ctrl"], "Toggle or hold, as set in Settings > Cursor."),
        new("ThrowItem", "Throw", ControlGroup.Actions, ["T"], "Throws the item in your hand (not favorites). Right-click the world to throw the item on the cursor."),
        new("Interact", "Interact", ControlGroup.Actions, [], "Right click. Bind a key to interact from the keyboard too."),
        new("UseSelected", "Use item", ControlGroup.Actions, [], "Left click. Bind a key to use from the keyboard too."),
        new("ReplaceTile", "Replace block", ControlGroup.Actions, [], "Placing a block over another swaps it on its own."),
        new("Emote", "Emotes (hold, then 1-8)", ControlGroup.Actions, ["Oem3"]),

        new("QuickHeal", "Quick heal", ControlGroup.QuickUse, ["H"]),
        new("QuickCharge", "Quick charge", ControlGroup.QuickUse, ["J"]),
        new("QuickBuff", "Quick buff", ControlGroup.QuickUse, ["B"]),
        new("Eat", "Eat", ControlGroup.QuickUse, [], "Using food from the hotbar eats it."),
        new("Loadout1", "Loadout 1", ControlGroup.QuickUse, ["F1"], "Or the loadout buttons in the inventory."),
        new("Loadout2", "Loadout 2", ControlGroup.QuickUse, ["F2"]),
        new("Loadout3", "Loadout 3", ControlGroup.QuickUse, ["F3"]),

        new("Inventory", "Inventory", ControlGroup.Panels, ["Escape", "I"], "Settings are in the inventory."),
        new("Map", "Full map", ControlGroup.Panels, ["M"]),
        new("MapStyle", "Map style", ControlGroup.Panels, ["Tab"], "Minimap, overlay or off."),
        new("MinimapZoomIn", "Map zoom in", ControlGroup.Panels, ["Add"], "Numpad +: the minimap, the overlay or the full map."),
        new("MinimapZoomOut", "Map zoom out", ControlGroup.Panels, ["Subtract"]),
        new("MapAlphaUp", "Map opacity up", ControlGroup.Panels, ["PageUp"], "The minimap or the overlay map."),
        new("MapAlphaDown", "Map opacity down", ControlGroup.Panels, ["PageDown"]),
        new("ZoomIn", "Zoom in", ControlGroup.Panels, ["OemPlus"], "The camera; the same setting as Settings > Zoom."),
        new("ZoomOut", "Zoom out", ControlGroup.Panels, ["OemMinus"]),
        new("Craft", "Craft selected recipe", ControlGroup.Panels, [], "Crafting lives in the inventory."),
        new("CraftPrevious", "Previous recipe", ControlGroup.Panels, []),
        new("CraftNext", "Next recipe", ControlGroup.Panels, []),
        new("Colonist", "Crew", ControlGroup.Panels, [], "Or the Crew tab in the inventory."),
        new("Bestiary", "Bestiary", ControlGroup.Panels, [], "Or the Bestiary tab in the inventory."),
        new("Achievements", "Feats", ControlGroup.Panels, [], "Or the Feats tab in the inventory."),
        new("ControlsHelp", "Show / hide key help", ControlGroup.Panels, ["Oem2"]),
        new("FrameSkipMode", "Cycle frame skip", ControlGroup.Panels, ["F8"], "On, Subtle or Off (Settings > Video)."),
        new("LightingMode", "Cycle lighting", ControlGroup.Panels, ["F9"], "Low, Standard, High or Off (Settings > Video), as Terraria's F9."),
        new("FrameRate", "Show / hide frame rate", ControlGroup.Panels, ["F10"], "Frames per second and drawing time, in the corner."),
        new("HideUi", "Hide the interface", ControlGroup.Panels, ["F11"], "Hides the whole HUD. Alt+Enter toggles fullscreen."),
        new("Creative", "Creative menu", ControlGroup.Panels, ["C"], "Creative worlds only."),

        new("Sleep", "Sleep (near a bed, at night)", ControlGroup.World, [], "Or right-click a bed."),
        new("CheckHousing", "Check housing at cursor", ControlGroup.World, [], "Or the Housing tab in the inventory."),
        new("WireLayer", "Next wire layer", ControlGroup.World, [], "Or the Wires toggle in the inventory."),
        new("CycleInventory", "Next hotbar item", ControlGroup.World, [], "The mouse wheel does this."),
        new("SummonBoss", "Summon next boss (debug)", ControlGroup.World, []),

        new("Hotbar1", "Hotbar 1", ControlGroup.Hotbar, ["D1"]), new("Hotbar2", "Hotbar 2", ControlGroup.Hotbar, ["D2"]),
        new("Hotbar3", "Hotbar 3", ControlGroup.Hotbar, ["D3"]), new("Hotbar4", "Hotbar 4", ControlGroup.Hotbar, ["D4"]),
        new("Hotbar5", "Hotbar 5", ControlGroup.Hotbar, ["D5"]), new("Hotbar6", "Hotbar 6", ControlGroup.Hotbar, ["D6"]),
        new("Hotbar7", "Hotbar 7", ControlGroup.Hotbar, ["D7"]), new("Hotbar8", "Hotbar 8", ControlGroup.Hotbar, ["D8"]),
        new("Hotbar9", "Hotbar 9", ControlGroup.Hotbar, ["D9"]), new("Hotbar10", "Hotbar 10", ControlGroup.Hotbar, ["D0"]),

        new("Pause", "Settings", ControlGroup.Menus, [], "Opens settings straight away. The inventory's Settings button does the same."),
        new("MenuUp", "Menu up", ControlGroup.Menus, ["UpArrow"]),
        new("MenuDown", "Menu down", ControlGroup.Menus, ["DownArrow"]),
        new("MenuLeft", "Menu left / adjust", ControlGroup.Menus, ["LeftArrow"]),
        new("MenuRight", "Menu right / adjust", ControlGroup.Menus, ["RightArrow"]),
        new("MenuConfirm", "Menu select", ControlGroup.Menus, ["Enter", "Spacebar"]),
        new("MenuBack", "Menu back", ControlGroup.Menus, ["Escape", "Backspace"]),
    ];

    private static readonly Dictionary<string, ControlAction> ById = All.ToDictionary(action => action.Id, StringComparer.OrdinalIgnoreCase);

    public static ControlAction? Find(string id) => ById.GetValueOrDefault(id);

    public static string GroupName(ControlGroup group) => group switch
    {
        ControlGroup.QuickUse => "Quick use",
        ControlGroup.Panels => "Panels and map",
        _ => group.ToString()
    };

    /// <summary>The two help-overlay lines, built from the player's current keys.</summary>
    public static string[] HintLines(InputProfile profile)
    {
        string K(string id) => profile.KeyLabel(id, max: 1) is { Length: > 0 } label ? label : "—";
        string[] moves = [K("MoveUp"), K("MoveLeft"), K("MoveDown"), K("MoveRight")];
        var move = moves.All(key => key.Length == 1) ? string.Concat(moves) : string.Join("/", moves);
        return
        [
            $"{move} move   {K("Jump")} jump   Left click use   Right click interact   {K("Grapple")} grapple   {K("Mount")} mount   {K("ThrowItem")} throw   {K("SmartSelect")} smart select   {K("SmartCursor")} smart cursor",
            $"{K("QuickHeal")} heal   {K("QuickCharge")} charge   {K("QuickBuff")} buff   {K("Inventory")} inventory, crafting, crew, settings   {K("Map")} map   {K("MapStyle")} map style   {K("ControlsHelp")} hide"
        ];
    }
}

public sealed class InputProfile
{
    /// <summary>
    /// Saved-profile format: 2 took WASD off menu navigation; 3 moved gameplay onto <see cref="ControlCatalog"/>
    /// (bindings that still match the old defaults take the new ones; anything the player changed is kept); 4 moved
    /// the panel keys into the inventory hub, unbinding them unless the player had changed them; 5 matched Terraria
    /// 1.4.5.8's keyboard defaults (loadouts on F1-F3, camera zoom on = and -, minimap zoom on the numpad only) and
    /// removed the recipe-guide action (the guide is reached from the hub and the station AI).
    /// </summary>
    public const int CurrentMenuBindingVersion = 5;

    public string Name { get; set; } = "Keyboard + Mouse";
    public int MenuBindingVersion { get; set; } = CurrentMenuBindingVersion;
    public Dictionary<string, List<string>> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public float TriggerDeadzone { get; set; } = 0.18f;
    public float InterfaceDeadzone { get; set; } = 0.25f;
    public bool InvertLookX { get; set; }
    public bool InvertLookY { get; set; }
    public float InventoryRepeatSeconds { get; set; } = 0.12f;

    public static InputProfile CreateDefault()
    {
        var profile = new InputProfile();
        profile.Bindings = ControlCatalog.All.ToDictionary(action => action.Id, action => action.Defaults.ToList(), StringComparer.OrdinalIgnoreCase);
        return profile;
    }

    /// <summary>The keys bound to an action, for display: "Space / W / ↑". Empty when unbound.</summary>
    public string KeyLabel(string action, int max = 3) =>
        Bindings.TryGetValue(action, out var keys) ? string.Join(" / ", keys.Take(max).Select(InputBindingResolver.Describe)) : string.Empty;

    /// <summary>
    /// Makes <paramref name="key"/> the first key of <paramref name="action"/>, keeping its alternates. If another
    /// action in the same context (gameplay or menu) had that key, it loses it, and its id is returned so the
    /// screen can say so.
    /// </summary>
    public string? Rebind(string action, string key)
    {
        string? displaced = null;
        var menu = ControlCatalog.Find(action)?.IsMenu ?? action.StartsWith("Menu", StringComparison.OrdinalIgnoreCase);
        foreach (var (other, keys) in Bindings)
        {
            if (string.Equals(other, action, StringComparison.OrdinalIgnoreCase) ||
                (ControlCatalog.Find(other)?.IsMenu ?? other.StartsWith("Menu", StringComparison.OrdinalIgnoreCase)) != menu)
                continue;
            if (keys.RemoveAll(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)) > 0)
                displaced = other;
        }
        var list = Bindings.TryGetValue(action, out var current) ? current : Bindings[action] = new List<string>();
        list.RemoveAll(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase));
        if (list.Count > 0)
            list[0] = key;
        else
            list.Add(key);
        Revision++;
        return displaced;
    }

    /// <summary>Bumped whenever bindings change, so input code can rebuild its key lookup.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Revision { get; private set; }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Name)) Name = "Keyboard + Mouse";
        Bindings ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (action, keys) in Bindings)
        {
            if (string.IsNullOrWhiteSpace(action) || keys is null)
                continue;
            normalized[action.Trim()] = keys.Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
        }
        Bindings = normalized;
        var menuActionVersion = MenuBindingVersion;
        if (menuActionVersion < 2)
            StripGameplayKeysFromMenuNavigation();
        if (menuActionVersion < 3)
            MigrateToCatalog();
        if (menuActionVersion < 4)
            UnbindPanelKeys();
        if (menuActionVersion < 5)
            MigrateToTerrariaDefaults();
        // Actions that no longer exist (the recipe guide, for one) go, so an old binding can't linger or steal a key.
        foreach (var stale in Bindings.Keys.Where(action => ControlCatalog.Find(action) is null).ToArray())
            Bindings.Remove(stale);
        MenuBindingVersion = CurrentMenuBindingVersion;
        // New actions arrive with their defaults; a player's empty list (deliberately unbound) stays empty.
        foreach (var action in ControlCatalog.All)
            if (!Bindings.ContainsKey(action.Id))
                Bindings[action.Id] = action.Defaults.ToList();
        TriggerDeadzone = ClampFinite(TriggerDeadzone, 0.18f, 0f, 0.95f);
        InterfaceDeadzone = ClampFinite(InterfaceDeadzone, 0.25f, 0f, 0.95f);
        InventoryRepeatSeconds = ClampFinite(InventoryRepeatSeconds, 0.12f, 0.03f, 1f);
        Revision++;
    }

    /// <summary>
    /// Version 3: the old profile listed keys the game never read (Mine on M, Quit on Q, arrow movement) while
    /// the real keys were fixed in code. Actions still on their old defaults move to the catalog's; actions the
    /// player rebound keep their keys; actions that no longer exist go.
    /// </summary>
    /// <summary>
    /// Version 5: Terraria's keyboard defaults. Minimap zoom keeps only the numpad if it still had the old pair (= and -
    /// now zoom the camera); loadouts that were never bound take F1-F3 unless another action already uses that key.
    /// The recipe guide action is removed with every other stale action.
    /// </summary>
    private void MigrateToTerrariaDefaults()
    {
        void Narrow(string action, string keep, string old)
        {
            if (Bindings.TryGetValue(action, out var saved) && saved.Count == 2 &&
                string.Equals(saved[0], keep, StringComparison.OrdinalIgnoreCase) && string.Equals(saved[1], old, StringComparison.OrdinalIgnoreCase))
                saved.RemoveAt(1);
        }
        Narrow("MinimapZoomIn", "Add", "OemPlus");
        Narrow("MinimapZoomOut", "Subtract", "OemMinus");
        for (var i = 1; i <= 3; i++)
        {
            var key = $"F{i}";
            var taken = Bindings.Any(pair => pair.Value.Any(bound => string.Equals(bound, key, StringComparison.OrdinalIgnoreCase)));
            if (!taken && Bindings.TryGetValue($"Loadout{i}", out var saved) && saved.Count == 0)
                saved.Add(key);
        }
    }

    /// <summary>Version 4: the panel keys of version 3, where still untouched, go (their panels moved into the inventory).</summary>
    private void UnbindPanelKeys()
    {
        var version3 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Eat"] = "F", ["Craft"] = "C", ["CraftPrevious"] = "Oem4", ["CraftNext"] = "Oem6", ["Colonist"] = "K", ["Guide"] = "V",
            ["Bestiary"] = "N", ["Achievements"] = "O", ["Sleep"] = "Z", ["CheckHousing"] = "P", ["WireLayer"] = "L"
        };
        foreach (var (action, key) in version3)
            if (Bindings.TryGetValue(action, out var saved) && saved.Count == 1 && string.Equals(saved[0], key, StringComparison.OrdinalIgnoreCase))
                saved.Clear();
    }

    private void MigrateToCatalog()
    {
        var oldDefaults = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["MoveLeft"] = ["A", "LeftArrow"], ["MoveRight"] = ["D", "RightArrow"], ["Jump"] = ["W", "Spacebar", "UpArrow"],
            ["Dash"] = ["X"], ["Inventory"] = ["I"], ["CycleInventory"] = ["Tab"], ["Interact"] = ["E", "Enter"],
            ["UseSelected"] = ["E", "Enter"], ["ReplaceTile"] = ["R"], ["Eat"] = ["F"], ["Craft"] = ["C"],
            ["CraftPrevious"] = ["Oem4"], ["CraftNext"] = ["Oem6"], ["Guide"] = ["V"], ["Colonist"] = ["K"],
            ["SummonBoss"] = ["B"], ["Pause"] = ["Escape"]
        };
        foreach (var obsolete in new[] { "Mine", "Place", "Quit" })
            Bindings.Remove(obsolete);
        foreach (var (action, keys) in oldDefaults)
            if (Bindings.TryGetValue(action, out var saved) && saved.SequenceEqual(keys, StringComparer.OrdinalIgnoreCase))
                Bindings.Remove(action);
    }

    private void StripGameplayKeysFromMenuNavigation()
    {
        (string Action, string Arrow)[] menuActions =
        [
            ("MenuUp", "UpArrow"),
            ("MenuDown", "DownArrow"),
            ("MenuLeft", "LeftArrow"),
            ("MenuRight", "RightArrow")
        ];
        foreach (var (action, arrow) in menuActions)
        {
            if (!Bindings.TryGetValue(action, out var keys))
                continue;
            keys.RemoveAll(key =>
                string.Equals(key, "W", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "A", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "S", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "D", StringComparison.OrdinalIgnoreCase));
            if (keys.Count == 0)
                keys.Add(arrow);
        }
    }

    private static float ClampFinite(float value, float fallback, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

public static class InputBindingResolver
{
    public static bool IsBound(InputProfile profile, string action, ConsoleKey key)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Bindings.TryGetValue(action, out var bindings) && bindings.Any(binding =>
            TryParseConsoleKey(binding, out var candidate) && candidate == key);
    }

    public static bool TryParseConsoleKey(string binding, out ConsoleKey key)
    {
        var normalized = binding.Trim();
        if (normalized.Length == 1 && char.IsDigit(normalized[0]))
        {
            key = normalized[0] == '0' ? ConsoleKey.D0 : (ConsoleKey)((int)ConsoleKey.D0 + normalized[0] - '0');
            return true;
        }
        if (string.Equals(normalized, "Space", StringComparison.OrdinalIgnoreCase)) normalized = nameof(ConsoleKey.Spacebar);
        if (string.Equals(normalized, "Esc", StringComparison.OrdinalIgnoreCase)) normalized = nameof(ConsoleKey.Escape);
        if (string.Equals(normalized, "Return", StringComparison.OrdinalIgnoreCase)) normalized = nameof(ConsoleKey.Enter);
        return Enum.TryParse(normalized, ignoreCase: true, out key) && Enum.IsDefined(key);
    }

    /// <summary>
    /// The Windows virtual-key code for a binding: any <see cref="ConsoleKey"/> name (their values are virtual-key
    /// codes) plus the modifiers Shift, Ctrl and Alt, which ConsoleKey has no names for.
    /// </summary>
    public static bool TryParseKeyCode(string binding, out int code)
    {
        code = binding.Trim().ToLowerInvariant() switch { "shift" => 16, "ctrl" or "control" => 17, "alt" => 18, _ => 0 };
        if (code != 0)
            return true;
        if (!TryParseConsoleKey(binding, out var key))
            return false;
        code = (int)key;
        return true;
    }

    /// <summary>The binding name for a virtual-key code, or null for keys that cannot be bound.</summary>
    public static string? NameForKeyCode(int code) => code switch
    {
        16 or 160 or 161 => "Shift",
        17 or 162 or 163 => "Ctrl",
        18 or 164 or 165 => "Alt",
        _ when Enum.IsDefined((ConsoleKey)code) => ((ConsoleKey)code).ToString(),
        _ => null
    };

    /// <summary>How a binding reads on screen: "Space", "1", "[", "↑".</summary>
    public static string Describe(string binding) => binding.Trim() switch
    {
        var b when b.Length == 2 && b[0] is 'D' or 'd' && char.IsDigit(b[1]) => b[1..],
        var b when b.Equals("Spacebar", StringComparison.OrdinalIgnoreCase) => "Space",
        var b when b.Equals("Escape", StringComparison.OrdinalIgnoreCase) => "Esc",
        var b when b.Equals("LeftArrow", StringComparison.OrdinalIgnoreCase) => "←",
        var b when b.Equals("RightArrow", StringComparison.OrdinalIgnoreCase) => "→",
        var b when b.Equals("UpArrow", StringComparison.OrdinalIgnoreCase) => "↑",
        var b when b.Equals("DownArrow", StringComparison.OrdinalIgnoreCase) => "↓",
        var b when b.Equals("Oem1", StringComparison.OrdinalIgnoreCase) => ";",
        var b when b.Equals("Oem2", StringComparison.OrdinalIgnoreCase) => "/",
        var b when b.Equals("Oem3", StringComparison.OrdinalIgnoreCase) => "`",
        var b when b.Equals("Oem4", StringComparison.OrdinalIgnoreCase) => "[",
        var b when b.Equals("Oem5", StringComparison.OrdinalIgnoreCase) => "\\",
        var b when b.Equals("Oem6", StringComparison.OrdinalIgnoreCase) => "]",
        var b when b.Equals("Oem7", StringComparison.OrdinalIgnoreCase) => "'",
        var b when b.Equals("OemPlus", StringComparison.OrdinalIgnoreCase) => "=",
        var b when b.Equals("OemMinus", StringComparison.OrdinalIgnoreCase) => "-",
        var b when b.Equals("OemComma", StringComparison.OrdinalIgnoreCase) => ",",
        var b when b.Equals("OemPeriod", StringComparison.OrdinalIgnoreCase) => ".",
        var b when b.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) => "Num " + b[6..],
        var b when b.Equals("Add", StringComparison.OrdinalIgnoreCase) => "Num +",
        var b when b.Equals("Subtract", StringComparison.OrdinalIgnoreCase) => "Num -",
        var b when b.Equals("Multiply", StringComparison.OrdinalIgnoreCase) => "Num *",
        var b when b.Equals("Divide", StringComparison.OrdinalIgnoreCase) => "Num /",
        var b when b.Equals("Decimal", StringComparison.OrdinalIgnoreCase) => "Num .",
        var b when b.Equals("Menu", StringComparison.OrdinalIgnoreCase) || b.Equals("LMenu", StringComparison.OrdinalIgnoreCase) => "Alt",
        var b when b.Equals("Back", StringComparison.OrdinalIgnoreCase) => "Backspace",
        var b when b.Equals("Return", StringComparison.OrdinalIgnoreCase) => "Enter",
        var b when b.Equals("Capital", StringComparison.OrdinalIgnoreCase) => "Caps Lock",
        var b when b.Equals("Prior", StringComparison.OrdinalIgnoreCase) || b.Equals("PageUp", StringComparison.OrdinalIgnoreCase) => "Page Up",
        var b when b.Equals("Next", StringComparison.OrdinalIgnoreCase) || b.Equals("PageDown", StringComparison.OrdinalIgnoreCase) => "Page Down",
        var b when b.Equals("ControlKey", StringComparison.OrdinalIgnoreCase) => "Ctrl",
        var b when b.Equals("ShiftKey", StringComparison.OrdinalIgnoreCase) => "Shift",
        var b => b
    };
}

public sealed class InputTriggerSet
{
    private readonly HashSet<string> _current = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _previous = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Current => _current;
    public IReadOnlySet<string> JustPressed { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> JustReleased { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public void Update(IEnumerable<string> activeBindings)
    {
        ArgumentNullException.ThrowIfNull(activeBindings);
        _previous.Clear();
        _previous.UnionWith(_current);
        _current.Clear();
        _current.UnionWith(activeBindings.Where(binding => !string.IsNullOrWhiteSpace(binding)));
        JustPressed = new HashSet<string>(_current.Except(_previous), StringComparer.OrdinalIgnoreCase);
        JustReleased = new HashSet<string>(_previous.Except(_current), StringComparer.OrdinalIgnoreCase);
    }

    public bool IsDown(string action) => _current.Contains(action);
    public bool Pressed(string action) => JustPressed.Contains(action);
    public bool Released(string action) => JustReleased.Contains(action);
}

public sealed record WorldProfile(
    Guid Id,
    string Name,
    int Seed,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastPlayed,
    int Width = 160,
    int Height = 72,
    WorldDifficulty Difficulty = WorldDifficulty.Classic,
    WorldHazard Hazard = WorldHazard.Random,
    bool Favorite = false,
    WorldBlight Blight = WorldBlight.Random,
    bool CreatedByCreative = false)
{
    /// <summary>
    /// Creative (Journey) characters and worlds stay apart, as in Terraria: a Creative character plays only worlds a
    /// Creative character created, and the reverse. The character that saved a world may always return to it (worlds
    /// from before the split carry no mark).
    /// </summary>
    public bool Admits(CharacterProfile character, Guid? savedBy = null) =>
        CreatedByCreative == (character.Difficulty == CharacterDifficulty.Creative) || savedBy == character.Id;
}
public sealed record ProfileIndex(int FormatVersion, IReadOnlyList<WorldProfile> Worlds, IReadOnlyList<CharacterProfile> Characters)
{
    public static ProfileIndex Empty => new(1, Array.Empty<WorldProfile>(), Array.Empty<CharacterProfile>());
}

/// <summary>Small profile catalog for menu flows. World simulation payloads are intentionally a separate format.</summary>
public sealed class UserProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _path;
    private readonly object _gate = new();
    private ProfileIndex _index = ProfileIndex.Empty;

    public UserProfileStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rimtera", "profiles.json");
        Load();
        // Character files (inventory, gear and upgrades that travel between worlds) live beside the profiles.
        CharacterFiles = new CharacterFileStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_path))!, "characters"));
        CharacterFileStore.Active = CharacterFiles;
    }

    public IReadOnlyList<WorldProfile> Worlds { get { lock (_gate) return _index.Worlds.ToArray(); } }
    public IReadOnlyList<CharacterProfile> Characters { get { lock (_gate) return _index.Characters.ToArray(); } }
    public string PathName => _path;

    public WorldProfile CreateWorld(string name, int seed, int width = 160, int height = 72,
        WorldDifficulty difficulty = WorldDifficulty.Classic, WorldHazard hazard = WorldHazard.Random,
        WorldBlight blight = WorldBlight.Random, bool createdByCreative = false)
    {
        name = NormalizeName(name, "New World");
        width = Math.Clamp(width, 80, 8400);
        height = Math.Clamp(height, 48, 2400);
        lock (_gate)
        {
            if (_index.Worlds.Any(world => string.Equals(world.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A world named '{name}' already exists.");
            var now = DateTimeOffset.UtcNow;
            var world = new WorldProfile(Guid.NewGuid(), name, seed, now, now, width, height, difficulty, hazard, Blight: blight,
                CreatedByCreative: createdByCreative);
            _index = _index with { Worlds = _index.Worlds.Append(world).ToArray() };
            SaveUnsafe();
            return world;
        }
    }

    public CharacterProfile CreateCharacter(string name)
    {
        name = NormalizeName(name, "New Adventurer");
        return CreateCharacter(CharacterDraft.CreateRandom(Random.Shared.Next(), name));
    }

    public CharacterProfile CreateCharacter(CharacterDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var name = NormalizeName(draft.Name, "New Adventurer");
        draft.Name = name;
        lock (_gate)
        {
            if (_index.Characters.Any(character => string.Equals(character.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A character named '{name}' already exists.");
            var character = draft.ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow).Normalized();
            _index = _index with { Characters = _index.Characters.Append(character).ToArray() };
            SaveUnsafe();
            return character;
        }
    }

    public bool DeleteWorld(Guid id) => UpdateIndex(index =>
        index.Worlds.Any(world => world.Id == id)
            ? index with { Worlds = index.Worlds.Where(world => world.Id != id).ToArray() }
            : null);

    /// <summary>Per-character carried state files (characters/&lt;id&gt;.rchar beside profiles.json).</summary>
    public CharacterFileStore CharacterFiles { get; }

    public bool DeleteCharacter(Guid id)
    {
        var removed = UpdateIndex(index =>
            index.Characters.Any(character => character.Id == id)
                ? index with { Characters = index.Characters.Where(character => character.Id != id).ToArray() }
                : null);
        if (removed)
        {
            try
            {
                CharacterFiles.Delete(id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return removed;
    }

    public bool ToggleWorldFavorite(Guid id) => UpdateIndex(index => index with
    {
        Worlds = index.Worlds.Select(world => world.Id == id ? world with { Favorite = !world.Favorite } : world).ToArray()
    });

    public bool ToggleCharacterFavorite(Guid id) => UpdateIndex(index => index with
    {
        Characters = index.Characters.Select(character => character.Id == id ? character with { Favorite = !character.Favorite } : character).ToArray()
    });

    public bool RenameWorld(Guid id, string name)
    {
        name = NormalizeName(name, "New World");
        return UpdateIndex(index =>
            index.Worlds.Any(world => world.Id != id && string.Equals(world.Name, name, StringComparison.OrdinalIgnoreCase))
                ? throw new InvalidOperationException($"A world named '{name}' already exists.")
                : index with { Worlds = index.Worlds.Select(world => world.Id == id ? world with { Name = name } : world).ToArray() });
    }

    public bool RenameCharacter(Guid id, string name)
    {
        name = NormalizeName(name, "New Adventurer");
        return UpdateIndex(index =>
            index.Characters.Any(character => character.Id != id && string.Equals(character.Name, name, StringComparison.OrdinalIgnoreCase))
                ? throw new InvalidOperationException($"A character named '{name}' already exists.")
                : index with { Characters = index.Characters.Select(character => character.Id == id ? character with { Name = name } : character).ToArray() });
    }

    /// <summary>Applies a change to the index under the lock and saves; a null result means "no change".</summary>
    private bool UpdateIndex(Func<ProfileIndex, ProfileIndex?> change)
    {
        lock (_gate)
        {
            var next = change(_index);
            if (next is null)
                return false;
            _index = next;
            SaveUnsafe();
            return true;
        }
    }

    public bool MarkWorldPlayed(Guid id)
    {
        lock (_gate)
        {
            var world = _index.Worlds.FirstOrDefault(candidate => candidate.Id == id);
            if (world is null)
                return false;
            _index = _index with
            {
                Worlds = _index.Worlds.Select(candidate => candidate.Id == id
                    ? candidate with { LastPlayed = DateTimeOffset.UtcNow }
                    : candidate).ToArray()
            };
            SaveUnsafe();
            return true;
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path) && !File.Exists(SafeFile.BackupPath(_path)))
            {
                _index = ProfileIndex.Empty;
                return;
            }
            try
            {
                // PU-11: a damaged profiles.json falls back to profiles.json.bak instead of starting empty.
                _index = SafeFile.ReadWithBackup(_path, path =>
                    JsonSerializer.Deserialize<ProfileIndex>(File.ReadAllText(path), JsonOptions) is { FormatVersion: 1 } index ? index : null,
                    out var fromBackup) ?? ProfileIndex.Empty;
                LoadedFromBackup = fromBackup;
                _index = _index with
                {
                    Worlds = _index.Worlds?
                        .Where(world => world.Id != Guid.Empty && !string.IsNullOrWhiteSpace(world.Name))
                        .Select(world => world with
                        {
                            Width = world.Width <= 0 ? 160 : Math.Clamp(world.Width, 80, 8400),
                            Height = world.Height <= 0 ? 72 : Math.Clamp(world.Height, 48, 2400)
                        })
                        .ToArray() ?? [],
                    Characters = _index.Characters?
                        .Where(character => character.Id != Guid.Empty && !string.IsNullOrWhiteSpace(character.Name))
                        .Select(character => character.Normalized())
                        .ToArray() ?? []
                };
            }
            catch (IOException) { _index = ProfileIndex.Empty; }
            catch (JsonException) { _index = ProfileIndex.Empty; }
        }
    }

    /// <summary>True when the last <see cref="Load"/> read profiles.json.bak because the main file was missing or damaged.</summary>
    public bool LoadedFromBackup { get; private set; }

    private void SaveUnsafe() => SafeFile.WriteAllText(_path, JsonSerializer.Serialize(_index, JsonOptions)); // keeps profiles.json.bak

    private static string NormalizeName(string value, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (name.Length > 32 || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Names must be at most 32 characters and cannot contain path characters.", nameof(value));
        return name;
    }

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>Thread-safe JSON preferences with safe defaults and atomic replacement writes.</summary>
public sealed class PreferencesStore<T> where T : class, new()
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public PreferencesStore(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));
    public string PathName => _path;

    public T Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
                return new T();
            try { return JsonSerializer.Deserialize<T>(File.ReadAllText(_path), _options) ?? new T(); }
            catch (IOException) { return new T(); }
            catch (JsonException) { return new T(); }
        }
    }

    public void Save(T preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, _options));
            File.Move(temporary, _path, overwrite: true);
        }
    }
}
