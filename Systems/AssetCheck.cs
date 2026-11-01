using System.Diagnostics;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The art gate (roadmap 0.2.0 PE-20, <c>--asset-check</c>): every sprite the game asks for exists and is tracked by git
/// (items, creatures, town NPCs, crew, mounts, tiles and flora), and no sprite is over the frame budget (512 px on its long
/// side, PE-2). The prover runs it on every push, so missing or oversized art fails CI instead of drawing a flat colour.
/// </summary>
public static class AssetCheck
{
    public const int FrameBudget = 512;

    /// <summary>Every problem found under <paramref name="root"/> (the repository or the build output); empty means clean.</summary>
    public static IReadOnlyList<string> Problems(string root, IReadOnlySet<string>? tracked = null)
    {
        var directory = ItemAudit.SpriteDirectory(root);
        var problems = new List<string>();
        if (!Directory.Exists(directory))
            return [$"no sprite folder at {directory}"];
        var files = Directory.EnumerateFiles(directory, "*.png")
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase);
        tracked ??= TrackedSprites(root);

        void Want(string category, string? sprite)
        {
            if (string.IsNullOrEmpty(sprite))
                return;
            var resolved = SpriteAtlas.ResolveName(sprite, files.ContainsKey);
            if (!files.ContainsKey(resolved))
                problems.Add($"{category}: missing sprite '{sprite}'");
            else if (tracked is not null && !tracked.Contains(resolved))
                problems.Add($"{category}: sprite '{resolved}' is not tracked by git");
        }

        foreach (var entry in ItemAudit.Run(directory, probe: false))
            if (!entry.SpriteFile)
                problems.Add($"item: {entry.Item.Name} ({entry.Item.Id}) has no sprite");
            else
                Want("item", entry.Sprite);
        foreach (var archetype in EnemyArchetypes.All)
            Want("creature", SpriteCatalog.ForEnemy(archetype.Kind));
        foreach (var npc in TownNpcCatalog.All)
            Want("town NPC", npc.Sprite);
        foreach (var crew in SpriteCatalog.CrewSpriteNames)
            Want("crew", crew);
        foreach (var mount in TravelContent.Mounts.Values)
            Want("mount", mount.Sprite);
        var tiles = new TileCatalog();
        foreach (var tile in Enum.GetValues<TileId>())
        {
            // Stations and furniture draw through their own sprites (as the --tile-sprites export lists them).
            if (tile != TileId.Air && tiles.Contains(tile) && tiles[tile].IsSolid && tiles[tile].Station == CraftingStationId.None &&
                !(ContentTiles.TryGet(tile, out var definition) && definition.Furniture) && !DrawnInCode(tile))
                Want("tile", SpriteCatalog.ForStation(tile) ?? SpriteCatalog.ForTile(tile));
            foreach (var decoration in SpriteCatalog.DecorationsFor(tile) ?? [])
                Want("flora", decoration);
        }

        foreach (var (name, path) in files)
            if (PngSize(path) is var (width, height) && Math.Max(width, height) > FrameBudget)
                problems.Add($"budget: '{name}' is {width}×{height} (over {FrameBudget} px on its long side)");
        return problems;
    }

    /// <summary>Tiles the renderer paints itself rather than from a sprite: moving walkways, canopies and the giant leaf.</summary>
    private static bool DrawnInCode(TileId tile) => TravelContent.IsGlideWalk(tile) || TilePhysics.IsCanopy(tile) || tile == TileId.GiantLeaf;

    /// <summary>Sprite names git tracks under Assets/Sprites, or null when git is not available (the check then trusts the disk).</summary>
    public static IReadOnlySet<string>? TrackedSprites(string root)
    {
        try
        {
            using var git = Process.Start(new ProcessStartInfo("git", "ls-files -- Assets/Sprites")
            {
                WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            });
            if (git is null)
                return null;
            var output = git.StandardOutput.ReadToEnd();
            git.WaitForExit();
            if (git.ExitCode != 0)
                return null;
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A PNG's width and height from its header, without decoding it.</summary>
    public static (int Width, int Height)? PngSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[24];
            if (stream.Read(header, 0, 24) < 24 || header[12] != (byte)'I' || header[13] != (byte)'H')
                return null;
            return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)),
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)));
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary><c>--asset-check</c>: prints every problem and fails (exit 1) if there is any.</summary>
    public static int Report(string[] args)
    {
        var root = Directory.Exists(Path.Combine(Environment.CurrentDirectory, "Assets", "Sprites"))
            ? Environment.CurrentDirectory
            : AppContext.BaseDirectory;
        var problems = Problems(root);
        foreach (var problem in problems)
            Console.WriteLine(problem);
        Console.WriteLine(problems.Count == 0 ? "Asset check: clean." : $"Asset check: {problems.Count} problem(s).");
        return problems.Count == 0 ? 0 : 1;
    }
}
