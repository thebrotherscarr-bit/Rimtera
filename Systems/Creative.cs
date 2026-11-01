using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// Creative mode (characters made with <see cref="CharacterDifficulty.Creative"/>): research items by submitting a number of them,
/// then duplicate researched items freely; powers freeze or set the time, scale spawns, toggle godmode, change the world's
/// difficulty, hold the weather (rain and wind) and freeze the spread of blight and crystal. Research lives on the character
/// (<see cref="CharacterFileData.Research"/>, PC-13): it follows the character into every Creative world. Older worlds kept it
/// in "creative.research.*" flags; those move into the character on load. Powers are per world (world flags). Isolated:
/// nothing here runs for other characters.
/// </summary>
public sealed partial class GameSimulation
{
    private const string ResearchPrefix = "creative.research.";
    /// <summary>Copies submitted per item: the character's research (saved in the character file, never the world).</summary>
    private readonly Dictionary<ItemId, int> _research = new();

    /// <summary>True for creative characters.</summary>
    public bool IsCreative => PlayerProfile.Difficulty == CharacterDifficulty.Creative;

    /// <summary>Copies of an item that must be submitted before it can be duplicated.</summary>
    public int ResearchNeeded(ItemId item)
    {
        if (!Items.Contains(item))
            return int.MaxValue;
        var stack = Items[item].MaxStack;
        return stack <= 1 ? 1 : stack <= 30 ? 3 : stack <= 99 ? 10 : 25;
    }

    public int ResearchProgress(ItemId item) => _research.GetValueOrDefault(item);
    public bool IsResearched(ItemId item) => IsCreative && ResearchProgress(item) >= ResearchNeeded(item);

    /// <summary>Every fully researched item.</summary>
    public IEnumerable<ItemId> ResearchedItems => _research.Keys.Where(IsResearched).OrderBy(id => id);

    /// <summary>The character's research counts, keyed by item id number (what the character file stores).</summary>
    private Dictionary<int, int>? CaptureResearch() => _research.Count == 0
        ? null
        : _research.Where(pair => pair.Value > 0).ToDictionary(pair => (int)pair.Key, pair => pair.Value);

    /// <summary>Replaces the research with a character file's counts (unknown items and bad counts are skipped).</summary>
    private void RestoreResearch(Dictionary<int, int>? saved)
    {
        _research.Clear();
        foreach (var (id, count) in saved ?? new Dictionary<int, int>())
            if (count > 0 && Items.Contains((ItemId)id))
                _research[(ItemId)id] = count;
    }

    /// <summary>
    /// X-CR-10: research used to live in the world's "creative.research.*" flags. A Creative character loading such a world
    /// takes those counts into its own research (keeping the higher count) and the world forgets them.
    /// </summary>
    private void MigrateWorldResearch()
    {
        if (!IsCreative)
            return;
        foreach (var (key, value) in Flags.All.Where(pair => pair.Key.StartsWith(ResearchPrefix, StringComparison.Ordinal)).ToArray())
        {
            if (int.TryParse(key[ResearchPrefix.Length..], out var id) && value > 0 && Items.Contains((ItemId)id))
                _research[(ItemId)id] = Math.Max(_research.GetValueOrDefault((ItemId)id), (int)Math.Min(value, int.MaxValue));
            Flags.Clear(key);
        }
    }

    /// <summary>Submits copies from the selected stack toward research. Returns a status line.</summary>
    public string ResearchSelected()
    {
        if (!IsCreative)
            return "Research is only available to creative characters.";
        var slot = Player.Inventory.SelectedSlot;
        var stack = Player.Inventory.Slots[slot];
        if (stack.IsEmpty)
            return "Select an item to research.";
        var item = stack.ItemId;
        var needed = ResearchNeeded(item) - ResearchProgress(item);
        if (needed <= 0)
            return $"{Items[item].Name} is already researched.";
        var submitted = 0;
        while (submitted < needed && Player.Inventory.TryConsume(item))
            submitted++;
        _research[item] = ResearchProgress(item) + submitted;
        var message = IsResearched(item)
            ? $"Research complete: {Items[item].Name} can now be duplicated."
            : $"Researched {ResearchProgress(item)}/{ResearchNeeded(item)} {Items[item].Name}.";
        PostMessage(message);
        return message;
    }

    /// <summary>Adds a full stack of a researched item. False when it is not researched.</summary>
    public bool DuplicateItem(ItemId item)
    {
        if (!IsResearched(item))
            return false;
        Player.Inventory.Add(item, Items[item].MaxStack);
        return true;
    }

    // ------------------------------------------------------------------ powers

    public bool CreativeTimeFrozen
    {
        get => Flags.IsSet("creative.time_frozen");
        set { if (IsCreative) SetFlagValue("creative.time_frozen", value ? 1 + (long)(_elapsedGameHours * 1000f) : 0); }
    }

    public bool CreativeGodmode
    {
        get => IsCreative && Flags.IsSet("creative.godmode");
        set { if (IsCreative) SetFlagValue("creative.godmode", value ? 1 : 0); }
    }

    /// <summary>Spawn-rate multiplier (0 = none, 1 = normal, up to 3).</summary>
    public float CreativeSpawnRate
    {
        get => IsCreative && Flags.IsSet("creative.spawn_rate") ? (Flags.Get("creative.spawn_rate") - 1) / 100f : 1f;
        set { if (IsCreative) SetFlagValue("creative.spawn_rate", 1 + (long)MathF.Round(Math.Clamp(value, 0f, 3f) * 100f)); }
    }

    /// <summary>Weather control: while on, the rain and wind below hold whatever the sky would roll (Journey's weather power).</summary>
    public bool CreativeWeatherLocked
    {
        get => IsCreative && Flags.IsSet("creative.weather");
        set { if (IsCreative) SetFlagValue("creative.weather", value ? 1 : 0); }
    }

    /// <summary>Held rain, 0 (dry) to 1 (downpour).</summary>
    public float CreativeRain
    {
        get => Flags.IsSet("creative.weather_rain") ? (Flags.Get("creative.weather_rain") - 1) / 100f : 0f;
        set { if (IsCreative) SetFlagValue("creative.weather_rain", 1 + (long)MathF.Round(Math.Clamp(value, 0f, 1f) * 100f)); }
    }

    public const float CreativeMaximumWind = 10f;

    /// <summary>Held wind in tiles per second (negative blows left), up to <see cref="CreativeMaximumWind"/> either way.</summary>
    public float CreativeWind
    {
        get => Flags.IsSet("creative.weather_wind") ? (Flags.Get("creative.weather_wind") - 1) / 10f - CreativeMaximumWind : 0f;
        set
        {
            if (IsCreative)
                SetFlagValue("creative.weather_wind",
                    1 + (long)MathF.Round((Math.Clamp(value, -CreativeMaximumWind, CreativeMaximumWind) + CreativeMaximumWind) * 10f));
        }
    }

    /// <summary>Enemy-spread freeze: blight and crystal stop spreading (Journey's infection-spread power).</summary>
    public bool CreativeSpreadFrozen
    {
        get => IsCreative && Flags.IsSet("creative.spread_frozen");
        set { if (IsCreative) SetFlagValue("creative.spread_frozen", value ? 1 : 0); }
    }

    /// <summary>Sets the time of day, moving forward to its next occurrence (creative power; tests use it to stage nights).</summary>
    public void SetTimeOfDay(float hour)
    {
        hour = ((hour % 24f) + 24f) % 24f;
        var absolute = 8f + _elapsedGameHours;
        var target = MathF.Floor(absolute / 24f) * 24f + hour;
        if (target < absolute)
            target += 24f;
        SetElapsedGameHours(target - 8f);
        if (Flags.IsSet("creative.time_frozen"))
            SetFlagValue("creative.time_frozen", 1 + (long)(_elapsedGameHours * 1000f));
    }

    private void SetElapsedGameHours(float elapsed)
    {
        _elapsedGameHours = Math.Max(0f, elapsed);
        var absolute = 8f + _elapsedGameHours;
        Day = 1 + (int)MathF.Floor(absolute / 24f);
        ClockHour = absolute % 24f;
    }

    /// <summary>Changes the world difficulty (creative power).</summary>
    public void SetCreativeDifficulty(WorldDifficulty difficulty)
    {
        if (IsCreative)
            Difficulty = difficulty;
    }

    private void UpdateCreative(float deltaSeconds)
    {
        var variant = SecretSeeds.Detect(Seed);
        if ((variant & SecretSeedVariant.Hardened) != 0)
        {
            if (Difficulty < WorldDifficulty.Brutal && !IsCreative)
                Difficulty = WorldDifficulty.Brutal;
            _spawnTimer += deltaSeconds * 0.5f; // half again as many spawns
        }
        _ticker.SpreadFrozen = CreativeSpreadFrozen;
        // 0.3.6 dev console overrides, in any mode (Systems/DevConsole.cs): god mode and a spawn-rate multiplier.
        if (Flags.IsSet(DevConsole.GodFlag) && !Player.IsDead)
            Player.Heal(Player.MaximumHitPoints);
        if (Flags.Get(DevConsole.SpawnRateFlag) is var devRate and > 0)
        {
            var multiplier = (devRate - 1) / 100f;
            if (multiplier <= 0f)
                _spawnTimer = 0f;
            else
                _spawnTimer += deltaSeconds * (multiplier - 1f);
        }
        if (!IsCreative)
            return;
        if (CreativeWeatherLocked)
            _weather.Force(rain: CreativeRain, snow: 0f, wind: CreativeWind, storm: false, acid: false, immediate: true);
        if (Flags.Get("creative.time_frozen") is var frozen and > 0)
            SetElapsedGameHours((frozen - 1) / 1000f);
        var rate = CreativeSpawnRate;
        if (rate <= 0f)
            _spawnTimer = 0f;
        else if (Math.Abs(rate - 1f) > 0.001f)
            _spawnTimer += deltaSeconds * (rate - 1f);
        if (CreativeGodmode && !Player.IsDead)
            Player.Heal(Player.MaximumHitPoints);
    }
}
