using Rimtera.Systems;

namespace Rimtera.Systems
{
    /// <summary>Special world variants unlocked by secret seed phrases (original phrases and effects).</summary>
    [Flags]
    public enum SecretSeedVariant : byte
    {
        None = 0,
        /// <summary>"twin blights": both blights take root, alternating zone by zone.</summary>
        TwinBlight = 1,
        /// <summary>"evergreen": the verdant canopy spreads across every temperate surface.</summary>
        Evergreen = 2,
        /// <summary>"hard landing": brutal creatures and half again as many spawns.</summary>
        Hardened = 4,
        /// <summary>"core rise": the Irradiated Core bulges up just under the surface.</summary>
        CoreRise = 8
    }

    /// <summary>
    /// Secret seeds. A text seed is hashed by the world-creation menu (FNV-1a), so the variant is a pure function of the
    /// numeric seed: <see cref="Detect"/> recognises the hashes of the phrases (any case, with or without spaces).
    /// </summary>
    public static class SecretSeeds
    {
        public static readonly (string Phrase, SecretSeedVariant Variant)[] Phrases =
        [
            ("twin blights", SecretSeedVariant.TwinBlight),
            ("evergreen", SecretSeedVariant.Evergreen),
            ("hard landing", SecretSeedVariant.Hardened),
            ("core rise", SecretSeedVariant.CoreRise),
            ("all of it", SecretSeedVariant.TwinBlight | SecretSeedVariant.Evergreen | SecretSeedVariant.Hardened | SecretSeedVariant.CoreRise)
        ];

        private static readonly Dictionary<int, SecretSeedVariant> Table = Build();

        private static Dictionary<int, SecretSeedVariant> Build()
        {
            var table = new Dictionary<int, SecretSeedVariant>();
            foreach (var (phrase, variant) in Phrases)
            {
                var title = string.Join(' ', phrase.Split(' ').Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
                foreach (var spelling in new[] { phrase, phrase.ToUpperInvariant(), title, phrase.Replace(" ", ""), title.Replace(" ", ""),
                             phrase.Replace(' ', '_'), phrase.Replace(' ', '-') })
                    table[SeedFor(spelling)] = variant;
            }
            return table;
        }

        /// <summary>The numeric seed the world-creation menu makes from a text seed (FNV-1a, as in UI/MenuSystem).</summary>
        public static int SeedFor(string text)
        {
            var hash = 2166136261u;
            foreach (var character in text)
                hash = unchecked((hash ^ character) * 16777619u);
            return unchecked((int)hash);
        }

        public static SecretSeedVariant Detect(int seed) => Table.GetValueOrDefault(seed);

        public static string Describe(SecretSeedVariant variant) => variant == SecretSeedVariant.None
            ? "An ordinary world."
            : string.Join(", ", Enum.GetValues<SecretSeedVariant>().Where(flag => flag != 0 && variant.HasFlag(flag)).Select(flag => flag switch
            {
                SecretSeedVariant.TwinBlight => "both blights",
                SecretSeedVariant.Evergreen => "a world-wide canopy",
                SecretSeedVariant.Hardened => "a hard landing",
                _ => "a risen core"
            }));
    }
}

namespace Rimtera.World
{
    public sealed partial class LargeWorldGenerator
    {
        /// <summary>Secret seed variant applied by the late passes below.</summary>
        public SecretSeedVariant Variant { get; init; }

        private void AddSeedVariantPasses(Action<string, double, Action<TileGrid, TileCatalog, Random, CancellationToken>> add)
        {
            if ((Variant & SecretSeedVariant.TwinBlight) != 0)
                add("Twinning the blight", 0.3, TwinBlight);
            if ((Variant & SecretSeedVariant.Evergreen) != 0)
                add("Spreading the endless canopy", 0.3, EvergreenSurface);
            if ((Variant & SecretSeedVariant.CoreRise) != 0)
                add("Raising the core", 0.3, RaiseCore);
        }

        /// <summary>Every other blight zone (or the east half of a lone zone) turns to the opposite blight.</summary>
        private void TwinBlight(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
        {
            var other = _blight == WorldBlight.Radiated ? WorldBlight.Overgrowth : WorldBlight.Radiated;
            var zones = Features.BlightZones;
            for (var index = 0; index < zones.Count; index++)
            {
                var zone = zones[index];
                var left = zones.Count == 1 ? (zone.Left + zone.Right) / 2 : zone.Left - 4;
                if (zones.Count > 1 && index % 2 == 0)
                    continue;
                for (var x = Math.Max(0, left); x <= Math.Min(_width - 1, zone.Right + 4); x++)
                for (var y = 0; y < _height - 1; y++)
                {
                    var tile = grid.TileAt(x, y);
                    TileId? next = null;
                    if (BlightRules.BlightOf(tile) == _blight && BlightRules.Purify(tile, false) is { } natural)
                        next = BlightRules.Convert(natural, other);
                    else if (tile == BlightRules.HeartTile(_blight))
                        next = BlightRules.HeartTile(other);
                    else if (tile == BlightRules.AltarTile(_blight))
                        next = BlightRules.AltarTile(other);
                    if (next is { } converted)
                        grid.SetTile(new GridPoint(x, y), converted);
                }
            }
        }

        /// <summary>Temperate surface grass and soil everywhere become canopy grass over mud.</summary>
        private void EvergreenSurface(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
        {
            for (var x = 0; x < _width; x++)
            {
                var top = TopSolid(grid, catalog, x);
                if (top <= 0 || top >= _height - 2)
                    continue;
                for (var y = top; y < Math.Min(_height - 1, top + 8); y++)
                {
                    var tile = grid.TileAt(x, y);
                    if (y == top && tile is TileId.Grass or TileId.JungleGrass or TileId.Soil)
                        grid.SetTile(new GridPoint(x, y), TileId.CanopyGrass);
                    else if (y > top && tile is TileId.Soil or TileId.Grass)
                        grid.SetTile(new GridPoint(x, y), TileId.Mud);
                }
            }
            Features.AddCount("evergreen-columns", _width);
        }

        /// <summary>Irradiated rock bulges up under the surface with small pools of liquid uranium.</summary>
        private void RaiseCore(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
        {
            for (var x = 6; x < _width - 6; x += 18 + random.Next(10))
            {
                if (NearSpawn(x, 0.06))
                    continue;
                var y = Math.Min(_height - 4, _surface[x] + 12 + random.Next(6));
                Blob(grid, random, x, y, 3 + random.NextDouble() * 2, random.Next(6, 12), random.NextDouble() - 0.5, random.NextDouble() * 0.3,
                    tile => tile is TileId.Rock or TileId.Soil or TileId.Clay or TileId.Sand ? TileId.IrradiatedRock : null);
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = 0; dy <= 1; dy++)
                    grid.SetTile(new GridPoint(Math.Clamp(x + dx, 0, _width - 1), y + dy), TileId.Air);
                for (var dx = -1; dx <= 1; dx++)
                    grid.TryAddLiquid(new GridPoint(Math.Clamp(x + dx, 0, _width - 1), y + 1), LiquidKind.Lava, 1f, catalog);
                Features.AddCount("risen-core-pockets");
            }
        }
    }
}
