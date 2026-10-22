using System.Text.Json;
using System.Text.Json.Serialization;
using Rimtera.Characters;
using Rimtera.Systems;

namespace Rimtera.UI;

public enum FramePacingMode : byte { Smooth, Balanced, SavePower }
public enum LightingQuality : byte { Low, Standard, High, Off }
public enum HealthBarPlacement : byte { Above, Below, Off }
public enum ParticleAmount : byte { Off, Low, High }
public enum SmartCursorMode : byte { Toggle, Hold }
public enum OptionsCategory : byte { General, Interface, Video, Volume, Cursor, Controls }
public enum WindowMode : byte { Windowed, Borderless, Fullscreen }

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
    public FramePacingMode FramePacing { get; set; } = FramePacingMode.Balanced;
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
    public bool AutoPause { get; set; }
    public bool ShowBuildingGrid { get; set; } = true;
    public bool ShowItemText { get; set; } = true;

    // General / Interface
    public bool DamageNumbers { get; set; } = true;
    public bool MinimapEnabled { get; set; } = true;
    public bool StatusPanel { get; set; } = true;
    public bool NameTags { get; set; } = true;
    public bool ShowFrameRate { get; set; }
    public HealthBarPlacement EnemyHealthBars { get; set; } = HealthBarPlacement.Above;

    // Video
    public bool WeatherEffects { get; set; } = true;
    public bool FlashyEffects { get; set; } = true;
    public ParticleAmount Particles { get; set; } = ParticleAmount.High;

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
        Zoom = ClampFinite(Zoom, 1f, 0.5f, 2.5f);
        UiScale = ClampFinite(UiScale, 1f, 0.75f, 1.5f);
        MapScale = ClampFinite(MapScale, 1f, 0.5f, 2f);
        if (!Enum.IsDefined(FramePacing)) FramePacing = FramePacingMode.Balanced;
        if (!Enum.IsDefined(Lighting)) Lighting = LightingQuality.Standard;
        if (!Enum.IsDefined(EnemyHealthBars)) EnemyHealthBars = HealthBarPlacement.Above;
        if (!Enum.IsDefined(Particles)) Particles = ParticleAmount.High;
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

public sealed class InputProfile
{
    public const int CurrentMenuBindingVersion = 2;

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
        profile.Bindings = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["MoveLeft"] = ["A", "LeftArrow"],
            ["MoveRight"] = ["D", "RightArrow"],
            ["Jump"] = ["W", "Spacebar", "UpArrow"],
            ["Dash"] = ["X"],
            ["Inventory"] = ["I"],
            ["CycleInventory"] = ["Tab"],
            ["Interact"] = ["E", "Enter"],
            ["UseSelected"] = ["E", "Enter"],
            ["Mine"] = ["M"],
            ["Place"] = ["E", "Enter"],
            ["ReplaceTile"] = ["R"],
            ["Eat"] = ["F"],
            ["Craft"] = ["C"],
            ["CraftPrevious"] = ["Oem4"],
            ["CraftNext"] = ["Oem6"],
            ["Guide"] = ["V"],
            ["Colonist"] = ["K"],
            ["SummonBoss"] = ["B"],
            ["Pause"] = ["Escape"],
            ["Quit"] = ["Q"],
            ["MenuUp"] = ["UpArrow"],
            ["MenuDown"] = ["DownArrow"],
            ["MenuLeft"] = ["LeftArrow"],
            ["MenuRight"] = ["RightArrow"],
            ["Hotbar1"] = ["D1"], ["Hotbar2"] = ["D2"], ["Hotbar3"] = ["D3"],
            ["Hotbar4"] = ["D4"], ["Hotbar5"] = ["D5"], ["Hotbar6"] = ["D6"],
            ["Hotbar7"] = ["D7"], ["Hotbar8"] = ["D8"], ["Hotbar9"] = ["D9"], ["Hotbar10"] = ["D0"],
            ["MenuConfirm"] = ["Enter", "Spacebar"], ["MenuBack"] = ["Escape", "Backspace"]
        };
        return profile;
    }

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
        if (menuActionVersion < CurrentMenuBindingVersion)
        {
            StripGameplayKeysFromMenuNavigation();
            MenuBindingVersion = CurrentMenuBindingVersion;
        }
        TriggerDeadzone = ClampFinite(TriggerDeadzone, 0.18f, 0f, 0.95f);
        InterfaceDeadzone = ClampFinite(InterfaceDeadzone, 0.25f, 0f, 0.95f);
        InventoryRepeatSeconds = ClampFinite(InventoryRepeatSeconds, 0.12f, 0.03f, 1f);
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
    WorldBlight Blight = WorldBlight.Random);
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
        WorldBlight blight = WorldBlight.Random)
    {
        name = NormalizeName(name, "New World");
        width = Math.Clamp(width, 80, 8400);
        height = Math.Clamp(height, 48, 2400);
        lock (_gate)
        {
            if (_index.Worlds.Any(world => string.Equals(world.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A world named '{name}' already exists.");
            var now = DateTimeOffset.UtcNow;
            var world = new WorldProfile(Guid.NewGuid(), name, seed, now, now, width, height, difficulty, hazard, Blight: blight);
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
            if (!File.Exists(_path))
            {
                _index = ProfileIndex.Empty;
                return;
            }
            try
            {
                _index = JsonSerializer.Deserialize<ProfileIndex>(File.ReadAllText(_path), JsonOptions) ?? ProfileIndex.Empty;
                if (_index.FormatVersion != 1)
                    _index = ProfileIndex.Empty;
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

    private void SaveUnsafe()
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_index, JsonOptions));
        File.Move(temporary, _path, overwrite: true);
    }

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
