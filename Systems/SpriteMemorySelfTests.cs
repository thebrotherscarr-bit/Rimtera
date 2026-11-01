using Rimtera.UI;

namespace Rimtera.Systems;

/// <summary>
/// 0.1.7 A2 (PE-2), landed in 0.2.2: lazy sprite loading. <see cref="SpriteAtlas.Load"/> builds an index (name, path and
/// size from the PNG header) and decodes nothing; sprites decode on first use into an LRU bounded at 192 MB, scaled copies
/// into one bounded at 64 MB. The cache bookkeeping is checked with stand-in values sized from the real PNG headers, so it
/// runs where System.Drawing cannot (Linux); on Windows real bitmaps are decoded through the atlas as well.
/// </summary>
public static class SpriteMemorySelfTests
{
    private sealed class Fake(long bytes)
    {
        public readonly long Bytes = bytes;
        public bool Released;
    }

    public static void Run(Action<bool, string> check)
    {
        CheckIndex(check);
        CheckCacheBookkeeping(check);
        CheckFrameBudget(check);
        if (OperatingSystem.IsWindows())
            CheckRealDecoding(check);
    }

    private static string SpriteDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Sprites");

    private static void CheckIndex(Action<bool, string> check)
    {
        var started = DateTime.UtcNow;
        var atlas = SpriteAtlas.Load();
        var seconds = (DateTime.UtcNow - started).TotalSeconds;
        var files = Directory.Exists(SpriteDirectory) ? Directory.GetFiles(SpriteDirectory, "*.png").Length : 0;
        check(files > 1000 && atlas.Count == files,
            $"PE-2: the sprite index lists every PNG on disk ({atlas.Count} of {files})");
        check(atlas.DecodedCount == 0 && atlas.DecodedBytes == 0 && atlas.ScaledCount == 0,
            $"PE-2: loading the atlas decodes nothing (indexed in {seconds:0.00} s)");
        // Find and Resolve answer from the index: a prefixed sheet name resolves, a missing one does not, nothing decodes.
        var anyCreature = Path.GetFileNameWithoutExtension(Directory.GetFiles(SpriteDirectory, "creature_*.png").First());
        var bare = anyCreature["creature_".Length..];
        check(atlas.Find(anyCreature) == anyCreature && (atlas.Find(bare) is { } found && found.EndsWith(bare, StringComparison.Ordinal)) &&
              atlas.Find("no_such_sprite_anywhere") is null && atlas.Find(null) is null && atlas.Resolve("no_such_sprite_anywhere") == "no_such_sprite_anywhere",
            "PE-2: Find and Resolve work from the file list (prefixed names resolve, missing names do not)");
        var size = atlas.SizeOf(anyCreature);
        check(size is { Width: > 0, Height: > 0 } && AssetCheck.PngSize(Path.Combine(SpriteDirectory, anyCreature + ".png")) == (size.Value.Width, size.Value.Height) &&
              atlas.DecodedCount == 0,
            "PE-2: a sprite's size comes from its PNG header without decoding it");
        // A recoloured variant is registered by name only; it decodes (from its source) on first use.
        var variant = atlas.Variant(anyCreature, "test", color => color);
        check(variant == anyCreature + "#test" && atlas.Find(variant) == variant && atlas.SizeOf(variant) == size && atlas.DecodedCount == 0 &&
              atlas.Variant("no_such_sprite_anywhere", "test", color => color) == "no_such_sprite_anywhere",
            "PE-2: registering a recoloured variant decodes nothing; a missing source keeps its own name");
        // Reading the whole sprite folder decoded would take this much; the budget bounds what stays in memory.
        long all = 0;
        foreach (var path in Directory.GetFiles(SpriteDirectory, "*.png"))
            if (AssetCheck.PngSize(path) is var (w, h))
                all += 4L * w * h;
        check(all > SpriteAtlas.DecodedBudgetBytes && SpriteAtlas.DecodedBudgetBytes == 192L << 20 && SpriteAtlas.ScaledBudgetBytes == 64L << 20,
            $"PE-2: decoding everything would hold {all >> 20} MB; the decoded LRU is capped at 192 MB and the scaled one at 64 MB");
    }

    /// <summary>The roadmap test: 5,000 random gets over 50 sprites keep the decoded bytes within the budget.</summary>
    private static void CheckCacheBookkeeping(Action<bool, string> check)
    {
        // The 50 largest sprites, sized from their headers (about 40 MB decoded); a 16 MB budget forces evictions.
        var largest = Directory.GetFiles(SpriteDirectory, "*.png")
            .Select(path => (Name: Path.GetFileNameWithoutExtension(path), Size: AssetCheck.PngSize(path)))
            .Where(entry => entry.Size is not null)
            .Select(entry => (entry.Name, Bytes: 4L * entry.Size!.Value.Width * entry.Size.Value.Height))
            .OrderByDescending(entry => entry.Bytes).Take(50).ToArray();
        foreach (var budget in new[] { SpriteAtlas.DecodedBudgetBytes, 16L << 20 })
        {
            var released = 0;
            var cache = new SpriteCache<string, Fake>(budget, fake => { fake.Released = true; released++; });
            var random = new Random(7);
            var worst = 0L;
            var stale = false;
            for (var i = 0; i < 5000; i++)
            {
                if (i % 12 == 0)
                    cache.BeginFrame(); // a frame draws about a dozen sprites
                var (name, bytes) = largest[random.Next(largest.Length)];
                if (!cache.TryGet(name, out var value))
                    cache.Add(name, value = new Fake(bytes), bytes);
                stale |= value.Released;
                worst = Math.Max(worst, cache.Bytes);
            }
            check(worst <= budget && !stale,
                $"PE-2: 5,000 random gets over 50 sprites stay within a {budget >> 20} MB budget (peak {worst / 1048576.0:0.0} MB, {cache.Evictions} evictions, none handed out after release)");
            if (budget < SpriteAtlas.DecodedBudgetBytes)
                check(cache.Evictions > 0 && cache.Count < largest.Length && released + cache.RetiredCount == cache.Evictions,
                    "PE-2: over budget the least recently used sprites are evicted, retired, then released");
        }

        // Pinning: everything used this frame survives even over budget; the next frame trims back.
        var clock = 0L;
        var pinned = new SpriteCache<string, Fake>(100, fake => fake.Released = true, clock: () => clock);
        var first = new Fake(60);
        pinned.Add("a", first, 60);
        pinned.Add("b", new Fake(60), 60);
        check(pinned.Count == 2 && pinned.Bytes == 120 && !first.Released,
            "PE-2: sprites drawn this frame are pinned, never evicted mid-draw, even over budget");
        pinned.BeginFrame();
        pinned.TryGet("b", out _);
        pinned.BeginFrame();
        check(pinned.Count == 1 && pinned.ContainsKey("b") && pinned.Bytes == 60 && pinned.RetiredCount == 1 && !first.Released,
            "PE-2: the next frame trims the least recently used sprite; it is retired, not disposed at once");
        clock += SpriteCache<string, Fake>.RetireMilliseconds - 1;
        pinned.BeginFrame();
        var early = first.Released;
        clock += 1;
        pinned.BeginFrame();
        check(!early && first.Released && pinned.RetiredCount == 0,
            "PE-2: a retired sprite is released 3 s later, once nothing (a gore chunk) can still be drawing it");

        // A zoom change retires every scaled copy; ReleaseAll (window close) frees everything.
        var scaled = new SpriteCache<(string, int), Fake>(1000);
        var copy = new Fake(10);
        scaled.Add(("a", 20), copy, 10);
        scaled.Clear();
        check(scaled.Count == 0 && scaled.Bytes == 0 && scaled.RetiredCount == 1, "PE-2: clearing the scaled cache (zoom change) retires every copy");
        var all = new SpriteCache<string, Fake>(1000, fake => fake.Released = true);
        var kept = new Fake(10);
        var gone = new Fake(10);
        all.Add("k", kept, 10);
        all.Add("g", gone, 10);
        all.Clear();
        all.Add("k", kept = new Fake(10), 10);
        all.ReleaseAll();
        check(kept.Released && gone.Released && all.Count == 0, "PE-2: disposing the atlas releases held and retired sprites alike");
    }

    /// <summary>The roadmap's frame rule (0.2.0 --asset-check enforces it): no generated animation frame is over 512 px.</summary>
    private static void CheckFrameBudget(Action<bool, string> check)
    {
        var over = Directory.GetFiles(SpriteDirectory, "*.png")
            .Where(path => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), @"_(idle|move|attack)_\d+\.png$"))
            .Count(path => AssetCheck.PngSize(path) is var (w, h) && Math.Max(w, h) > AssetCheck.FrameBudget);
        check(over == 0, $"PE-2: no animation frame is over {AssetCheck.FrameBudget} px on its long side ({over} over)");
    }

    /// <summary>Windows only (System.Drawing needs GDI+): the real atlas decodes on demand and stays within its budget.</summary>
    private static void CheckRealDecoding(Action<bool, string> check)
    {
        using var atlas = SpriteAtlas.Load();
        var names = Directory.GetFiles(SpriteDirectory, "*.png").Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .OrderByDescending(name => atlas.SizeOf(name) is { } size ? size.Width * size.Height : 0).Take(400).ToArray();
        var first = atlas.Get(names[0]);
        check(first is not null && atlas.DecodedCount == 1 && atlas.DecodedBytes == 4L * first.Width * first.Height,
            "PE-2 (Windows): Get decodes one sprite on first use");
        var random = new Random(11);
        var worst = 0L;
        for (var i = 0; i < 2000; i++)
        {
            if (i % 12 == 0)
                atlas.BeginFrame();
            atlas.Get(names[random.Next(names.Length)]);
            worst = Math.Max(worst, atlas.DecodedBytes);
        }
        check(worst <= SpriteAtlas.DecodedBudgetBytes, $"PE-2 (Windows): 2,000 gets over the 400 largest sprites peak at {worst >> 20} MB decoded (budget 192)");
        var scaled = atlas.GetScaled(names[0], 32, 32);
        check(scaled is { Width: 32, Height: 32 } && ReferenceEquals(scaled, atlas.GetScaled(names[0], 32, 32)) && atlas.ScaledBytes == 4096,
            "PE-2 (Windows): a scaled copy is cached and counted");
    }
}
