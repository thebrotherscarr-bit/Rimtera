using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// Creative mode (characters made with <see cref="CharacterDifficulty.Creative"/>): research items by submitting a number of them,
/// then duplicate researched items freely; powers freeze or set the time, scale spawns, toggle godmode and change the world's
/// difficulty. Research and powers are kept in the world flags so they survive saves. Isolated: nothing here runs for other
/// characters.
/// </summary>
public sealed partial class GameSimulation
{
    private const string ResearchPrefix = "creative.research.";

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

    public int ResearchProgress(ItemId item) => (int)Flags.Get(ResearchPrefix + (int)item);
    public bool IsResearched(ItemId item) => IsCreative && ResearchProgress(item) >= ResearchNeeded(item);

    /// <summary>Every fully researched item.</summary>
    public IEnumerable<ItemId> ResearchedItems => Flags.All
        .Where(pair => pair.Key.StartsWith(ResearchPrefix, StringComparison.Ordinal))
        .Select(pair => int.TryParse(pair.Key[ResearchPrefix.Length..], out var id) ? (ItemId)id : (ItemId?)null)
        .OfType<ItemId>().Where(IsResearched).OrderBy(id => id);

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
        Flags.Increment(ResearchPrefix + (int)item, submitted);
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
        if (!IsCreative)
            return;
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
