using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Player-placed marker tiles (Batch B): tombstones left at a death site, text signs, the Cargo Pod
/// station that opens personal storage, and kill banners. All are non-solid props. Ids live in a high
/// band of <see cref="TileId"/> so they never collide with world-tile appends.
/// </summary>
public static class PlayerTiles
{
    public static IReadOnlyList<TileDefinition> Definitions { get; } =
    [
        new(TileId.Tombstone, "Memorial marker", false, false, 30, 1.2f, 90),
        new(TileId.CargoPodStation, "Cargo pod station", false, false, 36, 1.2f, 92),
        new(TileId.KillBanner, "Trophy banner", false, false, 14, 0.6f, 93)
    ];

    /// <summary>Tiles that carry a line of text (signs and tombstones).</summary>
    public static bool HoldsText(TileId tile) => tile is TileId.SignPost or TileId.Tombstone;
}
