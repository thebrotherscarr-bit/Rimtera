using Rimtera.Systems;

namespace Rimtera.World;

/// <summary>
/// Neo-Terra theme pass: the Irradiated Core palette that replaces the old magma/ash/obsidian band at the bottom of
/// the world. Liquid uranium (stored as <see cref="LiquidKind.Lava"/>) hardens into vitrified core glass where it
/// meets water. Names and numbers are original.
/// </summary>
public static partial class ContentTiles
{
    /// <summary>Natural blocks of the Irradiated Core band.</summary>
    public static bool IsCoreTile(TileId tile) =>
        tile is TileId.IrradiatedRock or TileId.CoreCrust or TileId.VitrifiedGlass;

    private static void AddThemeTiles(List<ContentTileDefinition> list)
    {
        // Irradiated core rock: the bulk of the bottom band (sprite tile_irradiated_rock).
        list.Add(new(TileId.IrradiatedRock, "Irradiated core rock", true, 54, 1.8f, 13, null, TileTraits.None,
            Value: 1, Drop: ItemId.EmberStoneBlock));
        // Cracked core crust: the brittle ceiling over the core (sprite tile_core_crust).
        list.Add(new(TileId.CoreCrust, "Cracked core crust", true, 20, 0.9f, 30, null, TileTraits.None,
            Value: 1, Drop: ItemId.AshBlock));
        // Vitrified core glass: liquid uranium quenched by water, and glassy seams in the core (sprite tile_vitrified_glass).
        list.Add(new(TileId.VitrifiedGlass, "Vitrified core glass", true, 70, 2.4f, 31, null, TileTraits.None,
            MinimumToolPower: 35, Value: 2, Drop: ItemId.ObsidianShard));
        // Char slag: the burnt crust left where liquid uranium touches the fuel sea (sprite tile_char_slag).
        list.Add(new(TileId.CharSlag, "Char slag", true, 40, 1.5f, 30, null, TileTraits.None,
            Value: 1, Drop: ItemId.AshBlock));
    }
}
