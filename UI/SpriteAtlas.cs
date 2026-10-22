using System.Drawing;
using System.Numerics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Rimtera.Entities;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>
/// Loads the Neo-Terra sprite PNGs cut from the concept sheets (see tools/sprites)
/// and caches scaled/flipped copies so the per-frame renderer only blits.
/// Missing files are tolerated: callers fall back to flat colours.
/// </summary>
internal sealed class SpriteAtlas : IDisposable
{
    private readonly Dictionary<string, Bitmap> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Name, int Width, int Height, bool Flip), Bitmap> _scaled = new();
    private readonly HashSet<string> _opaque = new(StringComparer.OrdinalIgnoreCase);

    public static SpriteAtlas Load(string? directory = null)
    {
        var atlas = new SpriteAtlas();
        directory ??= Path.Combine(AppContext.BaseDirectory, "Assets", "Sprites");
        if (!Directory.Exists(directory))
            return atlas;
        foreach (var file in Directory.EnumerateFiles(directory, "*.png"))
        {
            try
            {
                using var source = new Bitmap(file);
                var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(copy))
                    g.DrawImage(source, 0, 0, source.Width, source.Height);
                var name = Path.GetFileNameWithoutExtension(file);
                atlas._sprites[name] = copy;
                if (IsFullyOpaque(source))
                    atlas._opaque.Add(name);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or OutOfMemoryException)
            {
                // A damaged sprite falls back to flat-colour drawing.
            }
        }
        return atlas;
    }

    public int Count => _sprites.Count;

    /// <summary>Prefixes used by the generated sheets (tools/sprites/grid_sheets.py), tried in order.</summary>
    internal static readonly string[] SheetPrefixes =
        ["", "weapon_", "tech_", "cmd_", "armor_", "ammo_", "item_", "tool_", "station_", "furn_", "struct_", "npc_", "critter_", "event_", "creature_", "boss_", "minion_", "sentry_", "deco_", "flora_"];

    /// <summary>The first existing sprite among name and prefixed variants, or the name itself.</summary>
    public string Resolve(string? name) => string.IsNullOrEmpty(name) ? string.Empty : ResolveName(name, _sprites.ContainsKey);

    /// <summary><see cref="Resolve"/> against any sprite set (the item audit checks the files on disk).</summary>
    internal static string ResolveName(string name, Func<string, bool> exists)
    {
        foreach (var prefix in SheetPrefixes)
            if (exists(prefix + name))
                return prefix + name;
        return name;
    }

    /// <summary>Resolves a convention name, or null when no prefix variant exists.</summary>
    public string? Find(string? name)
    {
        var resolved = Resolve(name);
        return _sprites.ContainsKey(resolved) ? resolved : null;
    }

    public Bitmap? Get(string? name) => name is not null && (_sprites.TryGetValue(name, out var bitmap) || _sprites.TryGetValue(Resolve(name), out bitmap)) ? bitmap : null;

    /// <summary>
    /// Registers (once) a per-pixel recolour of a sprite as "name#key" and returns that name.
    /// Only fully opaque pixels are recoloured so anti-aliased edges keep their original tint.
    /// Returns the original name when the source sprite is missing.
    /// </summary>
    public string Variant(string name, string key, Func<Color, Color> recolor)
    {
        var variantName = $"{name}#{key}";
        if (_sprites.ContainsKey(variantName))
            return variantName;
        if (Get(name) is not { } source)
            return name;

        using var editable = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(editable))
            g.DrawImage(source, 0, 0, source.Width, source.Height);
        var bounds = new Rectangle(0, 0, editable.Width, editable.Height);
        var data = editable.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[data.Stride / 4 * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            for (var i = 0; i < pixels.Length; i++)
            {
                var color = Color.FromArgb(pixels[i]);
                if (color.A == 255)
                    pixels[i] = recolor(color).ToArgb() | unchecked((int)0xFF000000);
            }
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            editable.UnlockBits(data);
        }

        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(result))
            g.DrawImage(editable, 0, 0, source.Width, source.Height);
        _sprites[variantName] = result;
        return variantName;
    }

    /// <summary>Returns the sprite scaled to exactly width x height, cached.</summary>
    public Bitmap? GetScaled(string? name, int width, int height, bool flip = false)
    {
        if (Get(name) is not { } source || width <= 0 || height <= 0)
            return null;
        name = _sprites.ContainsKey(name!) ? name : Resolve(name);
        var key = (name!, width, height, flip);
        if (_scaled.TryGetValue(key, out var cached))
            return cached;

        var scaled = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            // Upscaling keeps hard pixel edges; downscaling large sheet art averages to avoid shimmer.
            var upscale = width >= source.Width;
            g.InterpolationMode = upscale ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingMode = CompositingMode.SourceCopy;
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            var destination = flip
                ? new[] { new Point(width, 0), new Point(0, 0), new Point(width, height) }
                : new[] { new Point(0, 0), new Point(width, 0), new Point(0, height) };
            g.DrawImage(source, destination, new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel, attributes);
        }
        // Resampling leaves faintly transparent edge texels; tiles that were solid must stay solid,
        // otherwise the sky shows through as specks where four tiles meet.
        if (_opaque.Contains(name!))
            ForceOpaque(scaled);
        _scaled[key] = scaled;
        return scaled;
    }

    private static bool IsFullyOpaque(Bitmap bitmap)
    {
        if (!Image.IsAlphaPixelFormat(bitmap.PixelFormat))
            return true;
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[data.Stride / 4 * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return pixels.All(pixel => (pixel >>> 24) == 0xFF);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void ForceOpaque(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[data.Stride / 4 * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            // Locked as straight (non-premultiplied) ARGB, so raising alpha keeps each texel's colour.
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] |= unchecked((int)0xFF000000);
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>Scales a sprite to fit inside a box while keeping its aspect ratio.</summary>
    public Bitmap? GetFitted(string? name, int maxWidth, int maxHeight, bool flip = false)
    {
        if (Get(name) is not { } source || maxWidth <= 0 || maxHeight <= 0)
            return null;
        var scale = Math.Min(maxWidth / (float)source.Width, maxHeight / (float)source.Height);
        return GetScaled(name, Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)), flip);
    }

    public void Dispose()
    {
        foreach (var bitmap in _scaled.Values)
            bitmap.Dispose();
        foreach (var bitmap in _sprites.Values)
            bitmap.Dispose();
        _scaled.Clear();
        _sprites.Clear();
    }
}

/// <summary>Maps simulation content to Neo-Terra sprite names.</summary>
internal static class SpriteCatalog
{
    public static string? ForTile(TileId tile) => tile switch
    {
        TileId.Soil => "terrain_regolith",
        TileId.Grass => "tile_grassland",
        TileId.Rock => "terrain_rock",
        TileId.CopperOre => "terrain_ore_red",
        TileId.Sand => "terrain_sand",
        TileId.Wood => "wall_panel",
        TileId.Bedrock => "terrain_bedrock",
        TileId.Snow => "tile_icy_soil",
        TileId.DesertSand => "tile_scrap_heap",
        TileId.JungleGrass => "terrain_vine",
        TileId.FungalSoil => "terrain_fungal",
        TileId.EmberRock => "tile_obsidian",
        TileId.CorruptRock => "tile_caustic_crystal",
        TileId.CrimsonSoil => "tile_radiated_soil",
        TileId.HallowGrass => "terrain_prism",
        TileId.DungeonBrick => "wall_cracked",
        TileId.CobaltOre => "terrain_ore_blue",
        TileId.ViridiumOre => "terrain_ore_green",
        TileId.EnergyCrystal => "terrain_crystal",
        // Biome ores added later follow the naming convention terrain_<snake_case>.
        _ when tile.ToString().EndsWith("Ore", StringComparison.Ordinal) => "terrain_" + SnakeCase(tile.ToString()),
        _ => NamedTiles.GetValueOrDefault(tile.ToString()) ?? "tile_" + SnakeCase(tile.ToString())
    };

    /// <summary>Tiles matched by name so newer tile ids get art without a code dependency.</summary>
    private static readonly Dictionary<string, string> NamedTiles = new(StringComparer.Ordinal)
    {
        ["RadiatedSand"] = "tile_radiated_sand",
        ["RadiatedIce"] = "tile_radiated_ice",
        ["OvergrowthSoil"] = "tile_overgrowth_soil",
        ["OvergrowthRock"] = "tile_overgrowth_rock",
        ["OvergrowthSand"] = "tile_overgrowth_sand",
        ["OvergrowthIce"] = "tile_overgrowth_ice",
        ["Ash"] = "tile_ash",
        ["Obsidian"] = "tile_obsidian",
    };

    /// <summary>Background wall texture for a wall id (see World.WallIds); natural walls use their terrain.</summary>
    public static string ForWall(ushort wallId) => wallId switch
    {
        WallIds.PlayerHull => "wall_plating_grey",
        WallIds.Regolith => "wall_regolith",
        WallIds.Rock => "wall_rock",
        WallIds.Ice => "wall_ice",
        WallIds.Scrap => "wall_scrap",
        WallIds.Spore => "wall_fungal",
        WallIds.Radiated => "wall_radiated",
        WallIds.Overgrowth => "wall_overgrowth",
        WallIds.Ash => "wall_irradiated_core",
        108 => "wall_crystal",
        109 => "wall_derelict_hull",
        110 => "wall_mud",
        _ => WallIds.SpriteName(wallId) ?? "wall_plating_grey"
    };

    /// <summary>Block items use their tile's art, wall items their wall's; everything else snake_case.</summary>
    private static string ConventionName(string name) =>
        name.EndsWith("Block", StringComparison.Ordinal) && name.Length > 5 ? "tile_" + SnakeCase(name[..^5])
        : name.EndsWith("Wall", StringComparison.Ordinal) && name.Length > 4 ? "wall_" + SnakeCase(name[..^4])
        : SnakeCase(name);

    /// <summary>Items matched by name (newer ids); otherwise the snake_case sprite convention applies.</summary>
    private static readonly Dictionary<string, string> NamedItems = new(StringComparer.Ordinal)
    {
        ["LifeCore"] = "crystalline_flora",
        ["GliderWings"] = "item_glider_pack",
        ["AshBlock"] = "tile_ash",
        ["PurifierBeacon"] = "hab_frozen",
        ["SupplyCache"] = "mining_pack",
        ["InsulatedSuit"] = "armor_insulated_suit",
        ["HeatWard"] = "item_heat_ward",
        ["RadFilterMask"] = "armor_rad_filter_mask",
        ["MossBlock"] = "terrain_moss",
        ["PrecursorDungeonBrick"] = "tile_dungeon_brick_precursor",
        ["DartTrap"] = "tile_dart_trap",
        ["ExplosiveTrap"] = "tile_explosive_trap",
        ["Timer1s"] = "item_timer_1s",
        ["Timer3s"] = "item_timer_3s",
        ["Timer5s"] = "item_timer_5s",
        ["VentDoor"] = "tile_vent_door",
        ["FerriteBrickWall"] = "wall_brick_ferrite",
        ["AlabasterBrickWall"] = "wall_brick_alabaster",
    };

    /// <summary>Structures drawn as props standing on their tile, matched by name.</summary>
    private static readonly Dictionary<string, string> NamedProps = new(StringComparer.Ordinal)
    {
        ["ReactorCore"] = "hab_corrupted_a",
        // Irradiated core props (the bottom band's reactor ruins and speleothems).
        ["SlagForge"] = "station_reactor_forge",
        ["SlagChest"] = "furn_reactor_chest",
        ["PotMolten"] = "deco_pot_isotope",
        ["StalactiteAsh"] = "deco_stalactite_core",
        ["StalagmiteAsh"] = "deco_stalagmite_core",
        ["SporeHeart"] = "radiant_fungus",
        ["RadAltar"] = "caustic_rock",
        ["RootAltar"] = "mutant_vinelash",
        ["LifeCore"] = "crystalline_flora",
        ["SupplyCache"] = "mining_pack",
        ["SalvagePod"] = "salvaged_electronics",
        ["PurifierBeacon"] = "hab_frozen",
        ["ShipDoor"] = "wallart_bulkhead_door",
        ["TrapdoorOpen"] = "furn_trapdoor",
        ["VentDoorOpen"] = "tile_vent_door",
    };

    /// <summary>"GlacialOre" → "glacial_ore": the sprite naming convention for sheet assets.</summary>
    public static string SnakeCase(string pascal) =>
        System.Text.RegularExpressions.Regex.Replace(pascal, "(?<=[a-z0-9])([A-Z])", "_$1").ToLowerInvariant();

    /// <summary>Crafting stations are drawn as props standing on the tile rather than as blocks.</summary>
    public static string? ForStation(TileId tile) => tile switch
    {
        TileId.Workbench => "nano_fabricator",
        TileId.Furnace => "hab_lava",
        TileId.Anvil => "magma_brazier",
        TileId.MiniTurret => "mini_turret",
        TileId.HydroponicsTray => "hydroponics_tray",
        TileId.SolarPanel => "solar_panel",
        TileId.OxygenUnit => "oxygen_unit",
        _ => NamedProps.GetValueOrDefault(tile.ToString())
    };

    /// <summary>
    /// Renderer-supplied art for items with no sprite of their own (given the item and its conventional name).
    /// The graphical client installs <see cref="ItemArt.Fallback"/>; headless code leaves it null.
    /// </summary>
    public static Func<ItemId, string, string>? ItemArtFallback { get; set; }

    public static string? ForItem(ItemId item) => ForItem(item, SpriteExists);

    /// <summary>
    /// The item's sprite: its named or conventional sprite when that file exists (kitbashed or generated art always
    /// wins), otherwise the renderer's fallback art. <paramref name="exists"/> answers whether a sprite is loaded.
    /// </summary>
    internal static string? ForItem(ItemId item, Func<string, bool>? exists)
    {
        var named = ForItemNamed(item);
        if (named is null || exists?.Invoke(named) == true || ItemArtFallback is not { } fallback)
            return named;
        return fallback(item, named);
    }

    private static string? ForItemNamed(ItemId item) => item switch
    {
        ItemId.SoilBlock => "terrain_regolith",
        ItemId.RockBlock => "terrain_rock",
        ItemId.SandBlock => "terrain_sand",
        ItemId.WoodBlock => "item_hull_panel",
        ItemId.CopperOre => "ore_red",
        ItemId.WoodenPickaxe => "tool_survey_drill",
        ItemId.CopperPickaxe => "mining_pack",
        ItemId.WoodenSword => "weapon_shock_baton",
        ItemId.CopperSword => "energy_cutlass",
        ItemId.CopperBow => "laser_rifle",
        ItemId.Arrow => "ammo_energy_cell",
        ItemId.Berry => "nano_banana",
        ItemId.WorkbenchBlock => "nano_fabricator",
        ItemId.FurnaceBlock => "hab_lava",
        ItemId.AnvilBlock => "magma_brazier",
        ItemId.CopperBar => "metal_alloy",
        ItemId.SnowBlock => "tile_icy_soil",
        ItemId.DesertGlassBlock => "tile_scrap_heap",
        ItemId.JungleRootBlock => "terrain_vine",
        ItemId.FungalBlock => "terrain_fungal",
        ItemId.EmberStoneBlock => "tile_obsidian",
        ItemId.GloamRockBlock => "tile_caustic_crystal",
        ItemId.RiftSoilBlock => "tile_radiated_soil",
        ItemId.PrismGrassBlock => "terrain_prism",
        ItemId.AncientBrickBlock => "wall_cracked",
        ItemId.Trailcap => "void_helmet",
        ItemId.Trailcoat => "heavy_astro_suit",
        ItemId.Trailboots => "scout_boots",
        ItemId.HearthCharm => "swarm_gloves",
        ItemId.FestivalMask => "banana_uniform",
        ItemId.RestorativeTonic => "luminous_spores",
        ItemId.ManaTonic => "item_crystal_tonic",
        ItemId.CobaltOre => "ore_blue",
        ItemId.ViridiumOre => "ore_green",
        ItemId.EnergyCrystal => "energy_crystals",
        ItemId.PlasmaPistol => "plasma_pistol",
        ItemId.SniperRifle => "sniper_rifle",
        ItemId.GrenadeLauncher => "grenade_launcher",
        ItemId.RocketLauncher => "rocket_launcher",
        ItemId.MiniTurret => "mini_turret",
        ItemId.HydroponicsTray => "hydroponics_tray",
        ItemId.SolarPanel => "solar_panel",
        ItemId.OxygenUnit => "oxygen_unit",
        _ when DynamicItemSprite(item) is { } dynamic => dynamic,
        _ => NamedItems.GetValueOrDefault(item.ToString()) ?? ConventionName(item.ToString())
    };

    /// <summary>Kill banners and per-boss caches, trophies, relics and core-chips (dynamic ids): one kitbashed sprite each.</summary>
    private static string? DynamicItemSprite(ItemId item)
    {
        if (Enum.IsDefined(item))
            return null;
        if (PlayerItems.BannerKindOf(item) is { } banner)
            return "item_banner_" + SnakeCase(banner.ToString());
        if (PlayerSystemsContent.BagBoss(item) is { } bag)
            return "item_salvage_cache_" + SnakeCase(bag.ToString());
        if (PlayerSystemsContent.TrophyBoss(item) is { } trophy)
            return "item_trophy_" + SnakeCase(trophy.ToString());
        if (PlayerSystemsContent.RelicBoss(item) is { } relic)
            return "item_relic_" + SnakeCase(relic.ToString());
        if (PlayerSystemsContent.ExclusiveBoss(item) is { } exclusive)
            return "item_core_chip_" + SnakeCase(exclusive.ToString());
        return null;
    }

    /// <summary>
    /// Surface flora and debris per biome, chosen from the exposed top tile (pattern only:
    /// plants are picked by the ground they grow on). Render-only; not part of the simulation.
    /// </summary>
    public static string[]? DecorationsFor(TileId surface) => surface switch
    {
        TileId.Grass => ["fern_plant", "mossy_stone", "luminous_spores", "crystalline_flora", "astro_timber"],
        TileId.Snow => ["snow_rocks_a", "snow_rocks_b", "tundra_lichen_a", "tundra_lichen_b", "saxifrage_plant", "frost_crystal", "snow_bank"],
        TileId.DesertSand => ["broken_girder", "junk_rock", "mutant_grass", "mutant_flora", "salvaged_electronics"],
        TileId.EmberRock or TileId.IrradiatedRock or TileId.CoreCrust =>
            ["flora_uranium_crystal", "flora_rad_lichen", "flora_glow_reeds", "flora_irradiated_boulder", "deco_core_vent"],
        TileId.CorruptRock or TileId.CrimsonSoil => ["radiant_fungus", "mutant_vinelash", "radiant_flora", "caustic_rock"],
        TileId.JungleGrass => ["vinelash", "fern_plant", "luminous_spores", "mutant_grass"],
        TileId.FungalSoil => ["luminous_spores", "radiant_fungus", "spore_tall", "spore_small"],
        TileId.Rock => ["mossy_stone", "spore_crystal"],
        _ => NamedDecorations.GetValueOrDefault(surface.ToString())
    };

    /// <summary>
    /// Sprites for the real grass-plant tiles (flora parity: <c>TileId.GrassPlant</c> and <c>TileId.TallGrassPlant</c>), by the
    /// ground they grow on. These plants are simulated tiles that grow, get cut and regrow, so the render-only layer no
    /// longer draws them (see <see cref="DebrisFor"/>).
    /// </summary>
    public static string[]? PlantSpritesFor(TileId ground) => ground switch
    {
        TileId.Grass => GrasslandPlants,
        TileId.JungleGrass => JunglePlants,
        TileId.CanopyGrass => CanopyPlants,
        TileId.GlowshroomGrass => GlowshroomPlants,
        TileId.PrismaticGrass or TileId.HallowGrass => CrystalPlants,
        TileId.OvergrowthSoil or TileId.CrimsonSoil => BlightPlants,
        _ => null
    };

    // Shared arrays: the renderer asks for these once per visible plant per frame.
    private static readonly string[] GrasslandPlants = ["fern_plant", "luminous_spores", "crystalline_flora"];
    private static readonly string[] JunglePlants = ["vinelash", "fern_plant", "luminous_spores", "mutant_grass"];
    private static readonly string[] CanopyPlants = ["flora_giant_leaf", "flora_pitcher_plant", "flora_thorn_bush", "flora_bloom_flower"];
    private static readonly string[] GlowshroomPlants = ["flora_shelf_fungus", "flora_spore_puffball", "flora_biolum_moss", "flora_mycelium_tendrils"];
    private static readonly string[] CrystalPlants = ["flora_prism_flower", "flora_light_reed", "flora_halo_bush"];
    private static readonly string[] BlightPlants = ["radiant_fungus", "mutant_vinelash", "radiant_flora"];

    private static readonly Dictionary<TileId, string[]?> DebrisCache = new();

    /// <summary>
    /// The render-only surface layer: <see cref="DecorationsFor"/> without the plants that now grow as real tiles on that
    /// ground (rocks, logs, crystals and debris stay decorative). Cached per ground tile.
    /// </summary>
    public static string[]? DebrisFor(TileId surface)
    {
        lock (DebrisCache)
        {
            if (DebrisCache.TryGetValue(surface, out var cached))
                return cached;
            var all = DecorationsFor(surface);
            var plants = PlantSpritesFor(surface);
            var debris = all is null || plants is null ? all : all.Where(sprite => Array.IndexOf(plants, sprite) < 0).ToArray();
            if (debris is { Length: 0 })
                debris = null;
            DebrisCache[surface] = debris;
            return debris;
        }
    }

    /// <summary>Flora for biome ground tiles added after this table, matched by tile name.</summary>
    private static readonly Dictionary<string, string[]> NamedDecorations = new(StringComparer.Ordinal)
    {
        ["AcidSand"] = ["flora_tube_coral", "flora_kelp_strand", "flora_salt_spire", "flora_tide_pool"],
        ["ShellSand"] = ["flora_shell_pile", "flora_driftwood", "flora_tube_coral"],
        ["SaltCrust"] = ["flora_salt_spire", "flora_shell_pile"],
        ["CoralReef"] = ["flora_tube_coral", "flora_kelp_strand"],
        ["GlowshroomGrass"] = ["flora_giant_glowshroom", "flora_shelf_fungus", "flora_spore_puffball", "flora_glow_vine", "flora_biolum_moss", "flora_mycelium_tendrils"],
        ["MyceliumStone"] = ["flora_mycelium_tendrils", "flora_spore_puffball"],
        ["PrismaticGrass"] = ["flora_crystal_tree", "flora_prism_flower", "flora_light_reed", "flora_singing_crystal", "flora_halo_bush", "flora_pearl_shell_plant"],
        ["PearlStone"] = ["flora_singing_crystal", "flora_pearl_shell_plant"],
        ["CanopyGrass"] = ["flora_canopy_tree", "flora_giant_leaf", "flora_pitcher_plant", "flora_thorn_bush", "flora_bloom_flower", "flora_hanging_vines"],
        ["Mud"] = ["flora_giant_leaf", "flora_thorn_bush", "flora_pitcher_plant"],
        ["HiveComb"] = ["flora_bloom_flower"],
        ["PrecursorBrick"] = ["deco_precursor_statue", "deco_energy_pylon", "deco_glyph_tablet"],
        ["Alabaster"] = ["deco_geode_cluster"],
        ["MiteSilk"] = ["deco_mite_silk_cocoon"],
    };

    /// <summary>Height in tiles for a decoration sprite; rocks and debris sit lower than plants.</summary>
    public static float DecorationHeight(string sprite) => sprite switch
    {
        "flora_canopy_tree" or "flora_crystal_tree" or "flora_giant_glowshroom" => 3.2f,
        "flora_kelp_strand" or "flora_glow_vine" or "flora_light_reed" or "deco_precursor_statue" or "deco_energy_pylon" => 2.0f,
        "radiant_fungus" or "mutant_vinelash" or "molten_reeds" or "frost_crystal" or "vinelash" or "spore_tall" => 1.9f,
        "snow_rocks_a" or "snow_rocks_b" or "lava_rock" or "pyrocite_rock" or "caustic_rock" or "junk_rock" => 1.3f,
        _ => 1.1f
    };

    private static readonly Dictionary<EnemyKind, string> EnemySprites = new()
    {
        [EnemyKind.MossSlime] = "swarm_bug",
        [EnemyKind.CaveMoth] = "geiger_bug",
        [EnemyKind.CaveBat] = "sky_hawk",
        [EnemyKind.RockGolem] = "security_droid",
        [EnemyKind.Wraith] = "glow_stalker",
        [EnemyKind.StoneSentinel] = "overseer_mech",
        [EnemyKind.RockMite] = "rock_mite",
        [EnemyKind.SkyRay] = "sky_ray",
        [EnemyKind.SporeRat] = "spore_rat",
        [EnemyKind.AstroBeast] = "astro_beast",
        [EnemyKind.AlphaSkitter] = "alpha_skitter",
        [EnemyKind.DamagedMiningDrone] = "damaged_mining_drone",
        [EnemyKind.PlantOvergrowth] = "plant_overgrowth",
        [EnemyKind.ScoutMech] = "scout_mech",
        [EnemyKind.CyberSwarmLeader] = "cyber_swarm_leader",
        [EnemyKind.RenegadeOverseer] = "renegade_overseer",
        [EnemyKind.MutatedFlora] = "mutated_flora",
        [EnemyKind.Abomination] = "abomination",
        [EnemyKind.WarshipCore] = "warship_core",
        [EnemyKind.SolarFlareEntity] = "solar_flare_entity",
        [EnemyKind.VoidStalker] = "void_stalker",
        [EnemyKind.AsteroidMinerMech] = "asteroid_miner_mech",
        [EnemyKind.CyberPirateCaptain] = "cyber_pirate_captain",
        [EnemyKind.AstroGod] = "astro_god"
    };

    /// <summary>Creatures added after this table, matched by enum name.</summary>
    private static readonly Dictionary<string, string> NamedCreatures = new(StringComparer.Ordinal)
    {
        ["VineCrawler"] = "vinelash",
        ["SporeDrifter"] = "luminous_spores",
    };

    public static string ForEnemy(EnemyAgent enemy) => ForEnemy(enemy.Kind);

    public static string ForEnemy(EnemyKind kind) =>
        EnemySprites.TryGetValue(kind, out var sprite) ? sprite
            : NamedCreatures.GetValueOrDefault(kind.ToString()) ?? SnakeCase(kind.ToString());

    /// <summary>Frame sets from the animation sheets: (idle frames, move frames, attack frames).</summary>
    private static readonly Dictionary<EnemyKind, (string[] Idle, string[] Move, string[] Attack)> EnemyAnimations = new()
    {
        [EnemyKind.AlphaSkitter] = (Frames("alpha_skitter_walk", 1), Frames("alpha_skitter_walk", 6), Frames("alpha_skitter_walk", 6)),
        [EnemyKind.VoidStalker] = (["void_stalker_phase_in", "void_stalker_phase_out"], ["void_stalker_strike_1", "void_stalker_strike_2"],
            ["void_stalker_attack_1", "void_stalker_attack_2", "void_stalker_strike_1", "void_stalker_strike_2"]),
        [EnemyKind.CyberSwarmLeader] = (["cyber_swarm_leader_command", "cyber_swarm_leader_return"], ["cyber_swarm_leader_command", "cyber_swarm_leader_return"],
            ["cyber_swarm_leader_charge_1", "cyber_swarm_leader_charge_2", "cyber_swarm_leader_shoot_1", "cyber_swarm_leader_shoot_2"]),
        [EnemyKind.AstroGod] = (["astro_god_rising", "astro_god_pulse"], ["astro_god_rising", "astro_god_pulse"],
            ["astro_god_summon", "astro_god_discharge_1", "astro_god_discharge_2", "astro_god_dissipation"]),
        [EnemyKind.Abomination] = (["abomination_final_idle", "abomination_idle_1"], ["abomination_final_walk", "abomination_final_idle"],
            ["abomination_slap", "abomination_smash", "abomination_beam_charge", "abomination_beam"]),
        [EnemyKind.RadLeech] = (Frames("crystal_beast_idle", 6), Frames("crystal_beast_walk", 6), Frames("crystal_beast_attack", 6)),
    };

    private static string[] Frames(string prefix, int count) =>
        Enumerable.Range(1, count).Select(index => $"{prefix}_{index}").ToArray();

    /// <summary>Renderer-supplied check for whether a sprite exists (generated animation frames are optional).</summary>
    public static Func<string, bool>? SpriteExists { get; set; }

    /// <summary>
    /// Frame for an actor drawn from a base sprite with generated frames (tools/comfy/gen_anim.py):
    /// <c>&lt;base&gt;_idle_1..2</c>, <c>_move_1..4</c>, <c>_attack_1..2</c>. Falls back to the base sprite when a set is missing.
    /// </summary>
    public static string AnimatedFrame(string baseSprite, bool moving, bool attacking, double now, long seed)
    {
        if (SpriteExists is not { } exists)
            return baseSprite;
        var (state, count, rate) = attacking ? ("attack", 2, 8.0) : moving ? ("move", 4, 9.0) : ("idle", 2, 2.5);
        var frame = $"{baseSprite}_{state}_{(int)((now * rate + (seed & 0xFF) * 0.37) % count) + 1}";
        if (exists(frame))
            return frame;
        // Missing state: idle falls back to the first move frame, everything else to the base sprite.
        var first = $"{baseSprite}_move_1";
        return !moving && !attacking && exists(first) ? first : baseSprite;
    }

    /// <summary>Current animation frame for an enemy, or its static sprite when it has no frame set.</summary>
    public static string EnemyFrame(EnemyAgent enemy, double now)
    {
        if (!EnemyAnimations.TryGetValue(enemy.Kind, out var set))
            return AnimatedFrame(ForEnemy(enemy), MathF.Abs(enemy.Motion.Velocity.X) > 0.3f || enemy.Archetype.Flying,
                enemy.State == EnemyState.Telegraphing, now, enemy.Id);
        var frames = enemy.State == EnemyState.Telegraphing ? set.Attack
            : MathF.Abs(enemy.Motion.Velocity.X) > 0.3f || enemy.Archetype.Flying ? set.Move
            : set.Idle;
        var rate = enemy.State == EnemyState.Telegraphing ? 10.0 : 7.0;
        return frames[(int)((now * rate + enemy.Id) % frames.Length)];
    }

    /// <summary>Visual scale relative to the collision box; sheet art has margins and limbs.</summary>
    public static float EnemyDrawScale(EnemyAgent enemy) => enemy.Kind switch
    {
        EnemyKind.StoneSentinel => 1.9f,
        EnemyKind.RockGolem => 1.35f,
        _ when enemy.IsBoss => 1.25f,
        _ => 1.5f
    };

    private static readonly string[] CrewSprites =
        ["head_researcher", "security_commander", "astro_botanist", "chief_engineer", "trade_consul"];

    public static string ForCrew(long id) => CrewSprites[(int)(Math.Abs(id) % CrewSprites.Length)];

    public static IReadOnlyList<string> CrewSpriteNames => CrewSprites;

    /// <summary>Enemies whose frames come from the original animation sheets (not generated).</summary>
    public static bool HasHandMadeFrames(EnemyKind kind) => EnemyAnimations.ContainsKey(kind);

    /// <summary>Picks a colonist frame: front when idle, alternating side frames when walking.</summary>
    public static string ColonistFrame(Vector2 velocity, bool grounded, double timeSeconds)
    {
        if (MathF.Abs(velocity.X) < 0.4f)
            return grounded ? "colonist_front" : "colonist_side_a";
        if (!grounded)
            return "colonist_side_b";
        return ((int)(timeSeconds * 6.0) & 1) == 0 ? "colonist_side_a" : "colonist_side_b";
    }
}
