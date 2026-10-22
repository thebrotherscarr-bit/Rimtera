using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>
/// `--tile-sprites`: every solid block drawn as a texture, for tools/comfy/gen_textures.py. One tab-separated line per
/// texture: tile id name, the texture's sprite name (as the renderer resolves it), and the tile's display name.
/// Props (furniture, stations, plants) and liquids are left out; textures shared by several tiles are listed once.
/// </summary>
public static class TileTextureExport
{
    public static int Print()
    {
        var atlas = SpriteAtlas.Load();
        var tiles = new TileCatalog();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tile in Enum.GetValues<TileId>())
        {
            if (tile == TileId.Air || !tiles.Contains(tile) || !tiles[tile].IsSolid)
                continue;
            if (tiles[tile].Station != CraftingStationId.None || ContentTiles.TryGet(tile, out var definition) && definition.Furniture)
                continue;
            if (SpriteCatalog.ForStation(tile) is not null)
                continue;
            var sprite = atlas.Find(SpriteCatalog.ForTile(tile));
            if (sprite is null || !seen.Add(sprite))
                continue;
            Console.WriteLine($"{tile}\t{sprite}\t{tiles[tile].Name}");
        }
        return 0;
    }

    /// <summary>
    /// `--flora-sprites`: every surface decoration (plants, debris) with the ground it grows on, for
    /// tools/comfy/gen_env.py. Tab-separated: decoration sprite, ground tile name. Listed once each.
    /// </summary>
    public static int PrintFlora()
    {
        var tiles = new TileCatalog();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tile in Enum.GetValues<TileId>())
        {
            if (SpriteCatalog.DecorationsFor(tile) is not { } options)
                continue;
            foreach (var sprite in options)
                if (seen.Add(sprite))
                    Console.WriteLine($"{sprite}\t{(tiles.Contains(tile) ? tiles[tile].Name : tile.ToString())}");
        }
        return 0;
    }
}
