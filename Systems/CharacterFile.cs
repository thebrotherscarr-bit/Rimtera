using System.Text.Json;
using System.Text.Json.Serialization;
using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// The character's carried state, saved separately from any world (characters/&lt;id&gt;.rchar in the profile
/// data directory) so inventory, gear, loadouts, max-HP and Charge upgrades, the Cargo Pod bank, pets and
/// achievements travel between worlds. World snapshots also embed a copy as a fallback.
/// </summary>
public sealed record CharacterFileData(
    int FormatVersion,
    Guid CharacterId,
    string Name,
    DateTimeOffset SavedAt,
    ItemStack[] Inventory,
    int SelectedSlot,
    EquipmentSnapshot? Equipment = null,
    EquipmentSnapshot?[]? Loadouts = null,
    int ActiveLoadout = 0,
    int LifeCoresUsed = 0,
    int ChargeCrystalsUsed = 0,
    ItemStack[]? Bank = null,
    ItemStack? Trash = null,
    ItemId? Pet = null,
    ItemId? LightPet = null,
    MountKind Mount = MountKind.None,
    string[]? Achievements = null,
    int LifebloomPodsUsed = 0,
    bool AuxSocketUsed = false,
    PermanentUpgrade PermanentUpgrades = PermanentUpgrade.None,
    bool CreativeKitGranted = false,
    int StarterKitVersion = 0)
{
    public const int CurrentFormatVersion = 1;
}

/// <summary>Reads and writes <see cref="CharacterFileData"/> files with atomic replacement.</summary>
public sealed class CharacterFileStore
{
    public const string Extension = ".rchar";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false, Converters = { new JsonStringEnumConverter() } };

    public CharacterFileStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
    }

    /// <summary>
    /// The store the simulation saves to on <see cref="GameSimulation.CaptureSnapshot"/> and applies on world load. Set by the
    /// profile store (profile data dir + "characters"); null (nothing persisted) until a profile store exists.
    /// </summary>
    public static CharacterFileStore? Active { get; set; }

    public string Directory { get; }

    public string PathFor(Guid characterId) => Path.Combine(Directory, $"{characterId:N}{Extension}");

    public bool Exists(Guid characterId) => File.Exists(PathFor(characterId));

    public void Save(CharacterFileData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.CharacterId == Guid.Empty)
            throw new ArgumentException("A character id is required.", nameof(data));
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(data.CharacterId);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data, Options));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The saved character, or null when missing, unreadable or of an unknown version.</summary>
    public CharacterFileData? TryLoad(Guid characterId)
    {
        try
        {
            var path = PathFor(characterId);
            if (!File.Exists(path))
                return null;
            var data = JsonSerializer.Deserialize<CharacterFileData>(File.ReadAllText(path), Options);
            return data is { FormatVersion: CharacterFileData.CurrentFormatVersion, Inventory: not null } && data.CharacterId == characterId
                ? data
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Moves a character's file aside (into "deleted") instead of erasing it.</summary>
    public bool Delete(Guid characterId)
    {
        var path = PathFor(characterId);
        if (!File.Exists(path))
            return false;
        var archive = Path.Combine(Directory, "deleted");
        System.IO.Directory.CreateDirectory(archive);
        File.Move(path, Path.Combine(archive, $"{characterId:N}-{DateTime.UtcNow:yyyyMMddHHmmss}{Extension}"), overwrite: true);
        return true;
    }
}

public sealed partial class GameSimulation
{
    /// <summary>The character's carried state (inventory, gear, loadouts, upgrades, bank, pets, achievements).</summary>
    public CharacterFileData CaptureCharacter()
    {
        var loadouts = new EquipmentSnapshot?[LoadoutCount];
        for (var index = 0; index < LoadoutCount; index++)
            loadouts[index] = index == ActiveLoadout ? null : _loadouts[index];
        return new CharacterFileData(
            CharacterFileData.CurrentFormatVersion,
            PlayerProfile.Id,
            PlayerProfile.Name,
            DateTimeOffset.UtcNow,
            Player.Inventory.Slots.ToArray(),
            Player.Inventory.SelectedSlot,
            CaptureEquipmentWithCalibrations(),
            loadouts,
            ActiveLoadout,
            Player.LifeCoresUsed,
            Player.ChargeCrystalsUsed,
            Bank.Slots.ToArray(),
            TrashSlot.IsEmpty ? null : TrashSlot,
            _pet?.SourceItem,
            _lightPet?.SourceItem,
            ActiveMount,
            Achievements.UnlockedIds.OrderBy(id => id).ToArray(),
            LifebloomPodsUsed, AuxSocketUsed, PermanentUpgrades, CreativeKitGranted, StarterKits.Version);
    }

    /// <summary>Writes the character file to <paramref name="store"/> (default: <see cref="CharacterFileStore.Active"/>). False when there is no store.</summary>
    public bool SaveCharacterFile(CharacterFileStore? store = null)
    {
        store ??= CharacterFileStore.Active;
        if (store is null)
            return false;
        store.Save(CaptureCharacter());
        return true;
    }

    /// <summary>Loads and applies the character file from <paramref name="store"/> (default: the active store). False when none exists.</summary>
    public bool LoadCharacterFile(CharacterFileStore? store = null)
    {
        store ??= CharacterFileStore.Active;
        if (store?.TryLoad(PlayerProfile.Id) is not { } data)
            return false;
        ApplyCharacter(data);
        return true;
    }

    /// <summary>Snapshot copy of the character; also refreshes the character file when a store is active.</summary>
    private CharacterFileData CaptureCharacterForSnapshot()
    {
        var data = CaptureCharacter();
        try
        {
            CharacterFileStore.Active?.Save(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The world save still carries the embedded copy.
        }
        return data;
    }

    /// <summary>
    /// Characters saved before the current starting kit get its tools once (nothing is taken away): the old kit had no
    /// hatchet, so those characters had nothing to fell trees with.
    /// </summary>
    private void TopUpStarterKit(int savedVersion)
    {
        if (savedVersion >= StarterKits.Version || PlayerProfile.Difficulty == CharacterDifficulty.Creative)
            return;
        var kitRandom = new Random(Seed ^ 0x57A280);
        var added = new List<string>();
        foreach (var (item, count, calibrated) in StarterKits.Standard)
        {
            if (!calibrated || Player.Inventory.Count(item) > 0)
                continue;
            if (Player.Inventory.Add(item, count, StarterKits.Roll(kitRandom, Items[item])) > 0)
                added.Add(Items[item].Name.ToLowerInvariant());
        }
        if (added.Count > 0)
            PostMessage($"Your pack now carries the standard landing tools: {string.Join(", ", added)}.");
    }

    /// <summary>Applies a character's carried state. Invalid parts are skipped rather than failing the load.</summary>
    public void ApplyCharacter(CharacterFileData data, bool inventoryAlreadyRestored = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!inventoryAlreadyRestored && data.Inventory is { } stacks)
        {
            var before = Player.Inventory.Slots.ToArray();
            var selected = Player.Inventory.SelectedSlot;
            var fitted = stacks.Length == Player.Inventory.SlotCount
                ? stacks
                : stacks.Concat(Enumerable.Repeat(default(ItemStack), Math.Max(0, Player.Inventory.SlotCount - stacks.Length)))
                    .Take(Player.Inventory.SlotCount).ToArray();
            if (!Player.Inventory.RestoreState(fitted, Math.Clamp(data.SelectedSlot, 0, Player.Inventory.SlotCount - 1)))
                Player.Inventory.RestoreState(before, selected);
        }
        if (!inventoryAlreadyRestored && data.Equipment is { } equipment && Player.Equipment.Restore(equipment, Items))
            RestoreAccessoryCalibrations(equipment);
        if (data.Loadouts is { } loadouts)
            for (var index = 0; index < LoadoutCount && index < loadouts.Length; index++)
                _loadouts[index] = loadouts[index] is { } set && new EquipmentLoadout().Restore(set, Items) ? set : null;
        ActiveLoadout = Math.Clamp(data.ActiveLoadout, 0, LoadoutCount - 1);
        _loadouts[ActiveLoadout] = null;
        Player.LifeCoresUsed = Math.Clamp(data.LifeCoresUsed, 0, PlayerCharacter.MaximumLifeCores);
        Player.ChargeCrystalsUsed = Math.Clamp(data.ChargeCrystalsUsed, 0, PlayerItems.MaximumChargeCrystals);
        LifebloomPodsUsed = Math.Clamp(data.LifebloomPodsUsed, 0, GrowthItems.MaximumLifeblooms);
        AuxSocketUsed = data.AuxSocketUsed;
        PermanentUpgrades = data.PermanentUpgrades & (PermanentUpgrade)0x7F;
        CreativeKitGranted = data.CreativeKitGranted;
        if (data.Bank is { Length: BankSlots } bank && !Bank.RestoreState(bank, 0))
            Bank.RestoreState(new ItemStack[BankSlots], 0);
        TrashSlot = data.Trash is { IsEmpty: false } trash && Items.Contains(trash.ItemId) ? trash : default;
        _pet = null;
        _lightPet = null;
        if (data.Pet is { } pet)
            TogglePetQuiet(pet);
        if (data.LightPet is { } light)
            TogglePetQuiet(light);
        // Any mount comes back if its key is still carried.
        ActiveMount = TravelContent.Mounts.TryGetValue(data.Mount, out var savedMount)
            && Player.Inventory.Count(savedMount.Item) > 0 ? data.Mount : MountKind.None;
        Achievements.Load(data.Achievements);
        TopUpStarterKit(data.StarterKitVersion);
        Player.RestorePersistentState(Player.HitPoints, Player.Needs.FoodReserve, Player.Needs.RestReserve,
            Player.Needs.JoyReserve, Player.Needs.ComfortReserve);
    }

    private void TogglePetQuiet(ItemId item)
    {
        var last = LastMessage;
        TogglePet(item);
        LastMessage = last;
    }
}
