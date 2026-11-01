using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>What kinds of item a chest takes (0.2.4 storage tags, RimWorld's stockpile filters at chest size).</summary>
[Flags]
public enum StorageCategory : ushort
{
    None = 0,
    Materials = 1 << 0,
    Blocks = 1 << 1,
    Food = 1 << 2,
    Potions = 1 << 3,
    Weapons = 1 << 4,
    Tools = 1 << 5,
    Gear = 1 << 6,
    Critters = 1 << 7,
    All = Materials | Blocks | Food | Potions | Weapons | Tools | Gear | Critters
}

public static class StorageCategories
{
    /// <summary>The categories in the order the chest panel shows them, with short labels.</summary>
    public static IReadOnlyList<(StorageCategory Category, string Label)> Listed { get; } =
    [
        (StorageCategory.Materials, "Mats"), (StorageCategory.Blocks, "Blocks"), (StorageCategory.Food, "Food"),
        (StorageCategory.Potions, "Potions"), (StorageCategory.Weapons, "Arms"), (StorageCategory.Tools, "Tools"),
        (StorageCategory.Gear, "Gear"), (StorageCategory.Critters, "Critters")
    ];

    /// <summary>The one category an item falls in.</summary>
    public static StorageCategory Of(ItemDefinition item) => item.UseKind switch
    {
        ItemUseKind.PlaceTile or ItemUseKind.PlaceWall => StorageCategory.Blocks,
        ItemUseKind.Food => StorageCategory.Food,
        ItemUseKind.Consumable => StorageCategory.Potions,
        ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Ammunition or ItemUseKind.Tech or ItemUseKind.Command => StorageCategory.Weapons,
        ItemUseKind.Pickaxe or ItemUseKind.Demolisher or ItemUseKind.Cutter or ItemUseKind.BugNet or ItemUseKind.Utility => StorageCategory.Tools,
        ItemUseKind.Equipment or ItemUseKind.Vanity => StorageCategory.Gear,
        ItemUseKind.Critter => StorageCategory.Critters,
        _ => StorageCategory.Materials
    };
}

/// <summary>A saved chest filter (chests that take everything are not saved).</summary>
public sealed record ChestFilterSnapshot(int X, int Y, StorageCategory Takes);

/// <summary>
/// Storage tags (0.2.4): each chest can be told what it takes. Haulers and bill workers carry a stack to the nearest chest
/// that takes it, preferring a tagged chest over one that takes everything, so a "Food" chest by the kitchen fills with
/// food and the general chest with the rest. Ingredients are still fetched from any chest near a station.
/// </summary>
public sealed partial class GameSimulation
{
    private readonly Dictionary<GridPoint, StorageCategory> _chestFilters = new();

    /// <summary>What a chest takes; everything unless the player has tagged it.</summary>
    public StorageCategory ChestFilter(GridPoint cell) => _chestFilters.GetValueOrDefault(cell, StorageCategory.All);

    public void SetChestFilter(GridPoint cell, StorageCategory takes)
    {
        takes &= StorageCategory.All;
        if (takes == StorageCategory.All)
            _chestFilters.Remove(cell);
        else
            _chestFilters[cell] = takes;
    }

    /// <summary>The chest panel's toggle: turns one category on or off for a chest.</summary>
    public void ToggleChestCategory(GridPoint cell, StorageCategory category) => SetChestFilter(cell, ChestFilter(cell) ^ category);

    public bool ChestTakes(GridPoint cell, ItemId item) =>
        Items.Contains(item) && (ChestFilter(cell) & StorageCategories.Of(Items[item])) != 0;

    /// <summary>The chest among <paramref name="cells"/> that takes <paramref name="item"/> and has room: tagged chests first, then the nearest.</summary>
    private GridPoint? NearestWithRoom(IReadOnlyList<GridPoint> cells, ItemId item, Vector2 from)
    {
        GridPoint? best = null;
        var bestScore = (Tagged: false, Distance: float.MaxValue);
        foreach (var cell in cells)
        {
            if (!ChestTakes(cell, item) || GetContainer(cell) is not { } inventory || inventory.GetAddableCount(item) <= 0)
                continue;
            var tagged = ChestFilter(cell) != StorageCategory.All;
            var distance = Vector2.DistanceSquared(new Vector2(cell.X + 0.5f, cell.Y + 0.5f), from);
            if (best is null || (tagged && !bestScore.Tagged) || (tagged == bestScore.Tagged && distance < bestScore.Distance))
            {
                best = cell;
                bestScore = (tagged, distance);
            }
        }
        return best;
    }

    private ChestFilterSnapshot[] CaptureChestFilters() =>
        _chestFilters.Where(pair => World.Contains(pair.Key) && ContentTiles.Has(World.GetCell(pair.Key).TileId, TileTraits.Container))
            .OrderBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X)
            .Select(pair => new ChestFilterSnapshot(pair.Key.X, pair.Key.Y, pair.Value)).ToArray();

    private void RestoreChestFilters(ChestFilterSnapshot[]? saved)
    {
        _chestFilters.Clear();
        foreach (var entry in saved ?? [])
            if (entry is not null && World.Contains(entry.X, entry.Y))
                SetChestFilter(new GridPoint(entry.X, entry.Y), entry.Takes);
    }
}
