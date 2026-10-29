using Rimtera.Entities;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>One sound the game needs: a cue name, how to make it, and the cue it falls back to until it has a file.</summary>
public sealed record SoundCue(
    string Cue,
    string Category,
    string Trigger,
    int Variants,
    float Seconds,
    string Prompt,
    string Fallback = "",
    bool Loop = false,
    float Offset = 0f,
    string Lane = "generate",
    int Bpm = 0,
    string Key = "",
    int Meter = 0);

/// <summary>
/// `--sound-cues`: the audio needs map for tools/audio (docs/AUDIO_FLOW.md). Every weapon style, tool, tile material,
/// interaction, creature, boss, event, biome and UI action gets a cue, derived from the game's own data so new content
/// gets sounds automatically. Each cue names a fallback, and the chain always ends at one of the 23 synthesised effects,
/// so a cue plays something before its file exists. Music and ambience rows carry the style card (tempo, key, meter).
/// </summary>
public static class SoundCatalog
{
    private const string Style = "sci-fi frontier video game sound effect, clean, punchy, close, no music, no speech";

    private static readonly Lazy<IReadOnlyList<SoundCue>> _all = new(Build);
    private static readonly Lazy<Dictionary<string, SoundCue>> _byCue = new(() => _all.Value.ToDictionary(cue => cue.Cue, StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<SoundCue> All => _all.Value;

    public static SoundCue? Get(string cue) => _byCue.Value.GetValueOrDefault(cue);

    /// <summary>The cue to try when <paramref name="cue"/> has no sound; null at the end of the chain.</summary>
    public static string? Fallback(string cue) => Get(cue) is { Fallback.Length: > 0 } entry ? entry.Fallback : null;

    public static int Print()
    {
        Console.WriteLine("cue\tcategory\ttrigger\tvariants\tseconds\tloop\toffset\tlane\tfallback\tbpm\tkey\tmeter\tprompt");
        foreach (var cue in All)
            Console.WriteLine(string.Join('\t', cue.Cue, cue.Category, cue.Trigger, cue.Variants, cue.Seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                cue.Loop ? "yes" : "no", cue.Offset.ToString("0", System.Globalization.CultureInfo.InvariantCulture), cue.Lane, cue.Fallback,
                cue.Bpm, cue.Key, cue.Meter, cue.Prompt));
        return 0;
    }

    // ---- Names the AudioDirector uses at runtime (kept here so the catalog and the triggers cannot drift) ----

    public static string WeaponCue(ItemDefinition item) => item.Style == WeaponStyle.None ? "" :
        $"use_{Snake(item.Style.ToString())}{(item.Tier >= 4 ? "_heavy" : "")}";

    public static string ToolCue(ItemUseKind kind) => kind switch
    {
        ItemUseKind.Pickaxe => "tool_drill",
        ItemUseKind.Cutter => "tool_chop",
        ItemUseKind.Demolisher => "tool_hammer_wall",
        ItemUseKind.BugNet => "tool_net",
        ItemUseKind.Utility => "tool_utility",
        ItemUseKind.Food => "eat",
        ItemUseKind.Consumable => "potion",
        ItemUseKind.PlaceTile => "place",
        ItemUseKind.PlaceWall => "place_wall",
        ItemUseKind.Critter => "critter_release",
        ItemUseKind.Equipment or ItemUseKind.Vanity => "equip",
        _ => ""
    };

    /// <summary>The material class a tile sounds like when struck, broken or placed.</summary>
    public static string TileClass(TileDefinition tile)
    {
        var name = tile.Name.ToLowerInvariant();
        if (tile.Station != CraftingStationId.None || !tile.IsSolid && tile.Id != TileId.Air)
            return "metal";
        if (Has(name, "glass", "window", "pane"))
            return "glass";
        if (Has(name, "crystal", "prism", "shard", "gem"))
            return "crystal";
        if (Has(name, "ice", "snow", "frost", "glacial"))
            return "ice";
        if (Has(name, "sand", "dune", "silt", "ash"))
            return "sand";
        if (Has(name, "wood", "log", "timber", "plank", "bark"))
            return "wood";
        if (Has(name, "leaf", "vine", "spore", "mushroom", "root", "flesh", "hive", "comb", "web", "silk", "gel"))
            return "organic";
        if (Has(name, "plating", "panel", "hull", "metal", "steel", "alloy", "brick", "pipe", "grate", "scrap"))
            return "metal";
        if (Has(name, "ore", "rock", "basalt", "stone", "granite", "marble", "obsidian", "bedrock", "magma"))
            return "stone";
        return "soil";
    }

    public static readonly string[] TileClasses = ["soil", "stone", "metal", "glass", "wood", "organic", "crystal", "ice", "sand"];

    /// <summary>The body a creature is made of, for hit sounds.</summary>
    public static string CreatureMaterial(EnemyArchetype archetype)
    {
        var name = archetype.DisplayName.ToLowerInvariant();
        if (Has(name, "drone", "turret", "bot", "mech", "sentinel", "probe", "construct", "unit", "android", "automaton", "tank"))
            return "metal";
        if (Has(name, "slime", "ooze", "gel", "blob", "jelly", "sludge"))
            return "slime";
        if (Has(name, "crystal", "shard", "prism", "golem", "rock", "stone"))
            return "crystal";
        if (Has(name, "wraith", "ghost", "spirit", "specter", "spectre", "phantom", "shade", "wisp"))
            return "spirit";
        if (archetype.Family == EnemyAiFamily.Caster)
            return "energy";
        if (archetype.Family is EnemyAiFamily.Worm or EnemyAiFamily.SkitteringHopper || Has(name, "mite", "beetle", "crab", "scorpion", "spider", "wasp", "bug", "worm", "shell"))
            return "carapace";
        return "flesh";
    }

    /// <summary>A creature's voice family and size: creature_&lt;family&gt;_&lt;size&gt;.</summary>
    public static string CreatureVoice(EnemyArchetype archetype)
    {
        var family = archetype.Passive ? "critter"
            : archetype.Family switch
            {
                EnemyAiFamily.Worm => "worm",
                EnemyAiFamily.Caster => "caster",
                EnemyAiFamily.Mimic => "mimic",
                EnemyAiFamily.Hopper or EnemyAiFamily.LeapingHopper or EnemyAiFamily.SkitteringHopper => "hopper",
                _ => archetype.Flying || archetype.Family == EnemyAiFamily.DivingFlyer ? "flyer"
                    : CreatureMaterial(archetype) == "metal" ? "machine" : "beast"
            };
        var area = archetype.Size.X * archetype.Size.Y;
        var size = area < 1.1f ? "small" : area < 4f ? "medium" : "large";
        return $"creature_{family}_{size}";
    }

    public static string BossCue(EnemyKind kind, string part) => $"boss_{Snake(kind.ToString())}_{part}";

    public static string EventCue(WorldEventKind kind, string part) => $"event_{Snake(kind.ToString())}_{part}";

    // ---- The catalog ----

    private static IReadOnlyList<SoundCue> Build()
    {
        var cues = new List<SoundCue>();
        void Add(SoundCue cue)
        {
            if (cues.All(existing => !string.Equals(existing.Cue, cue.Cue, StringComparison.OrdinalIgnoreCase)))
                cues.Add(cue);
        }

        AddWeapons(Add);
        AddTools(Add);
        AddTiles(Add);
        AddPlayer(Add);
        AddInteractions(Add);
        AddUi(Add);
        AddCreatures(Add);
        AddEvents(Add);
        AddAmbience(Add);
        AddMusic(Add);
        return cues;
    }

    private static readonly Dictionary<WeaponStyle, (string Fallback, float Seconds, string Sound)> WeaponSounds = new()
    {
        [WeaponStyle.Broadsword] = ("swing", 0.8f, "a wide heavy energy-blade sword swing, whoosh with a faint hum"),
        [WeaponStyle.Shortsword] = ("swing", 0.5f, "a quick short blade stab, sharp thin swish"),
        [WeaponStyle.Spear] = ("swing", 0.7f, "a spear thrust, fast lunging whoosh with a metallic shaft rattle"),
        [WeaponStyle.Yoyo] = ("swing", 1.0f, "a spinning tethered disc thrown out, whirring buzz on a cord"),
        [WeaponStyle.Boomerang] = ("swing", 1.0f, "a spinning boomerang blade thrown, rising and falling whirr"),
        [WeaponStyle.Flail] = ("swing", 1.0f, "a spiked ball on a chain spun and launched, chain links rattling"),
        [WeaponStyle.Whip] = ("swing", 0.7f, "a long energy whip lash ending in a crisp crack"),
        [WeaponStyle.Bow] = ("shoot", 0.7f, "a composite bow string drawn and released, twang and arrow whoosh"),
        [WeaponStyle.Gun] = ("shoot", 0.6f, "a single sci-fi pistol gunshot, tight crack with a short mechanical clack"),
        [WeaponStyle.Shotgun] = ("shoot", 0.9f, "a pump shotgun blast, wide booming crack and pump rack"),
        [WeaponStyle.Launcher] = ("shoot", 1.2f, "a rocket launcher firing, thump and hissing rocket ignition"),
        [WeaponStyle.Thrown] = ("swing", 0.5f, "a small object thrown hard, quick whoosh"),
        [WeaponStyle.Staff] = ("zap", 0.8f, "an energy staff casting a bolt, rising charge and bright zap"),
        [WeaponStyle.Tome] = ("zap", 0.9f, "a datapad releasing a burst of energy bolts, glitchy digital chirps"),
        [WeaponStyle.Beam] = ("zap", 1.5f, "a continuous laser beam, steady crackling electric hum"),
        [WeaponStyle.SummonStaff] = ("zap", 1.2f, "a drone being deployed, servo whir and power-up chime"),
        [WeaponStyle.SentryStaff] = ("place", 1.2f, "a turret being placed and unfolding, heavy clunk and servo lock")
    };

    private static void AddWeapons(Action<SoundCue> add)
    {
        foreach (var (style, (fallback, seconds, sound)) in WeaponSounds)
        {
            var cue = $"use_{Snake(style.ToString())}";
            add(new(cue, "weapons", $"using a {style} weapon", 4, seconds, $"{sound}. {Style}", fallback));
            add(new(cue + "_heavy", "weapons", $"using a tier 4+ {style} weapon", 3, seconds * 1.2f,
                $"{sound}, heavier and more powerful with a deep low-end boom. {Style}", cue, Offset: 1f));
        }
        add(new("swing", "weapons", "any melee swing", 4, 0.6f, $"a fast weapon swing, clean whoosh. {Style}", ""));
        add(new("shoot", "weapons", "any ranged shot", 4, 0.6f, $"a compact sci-fi gunshot. {Style}", ""));
        add(new("zap", "weapons", "any Tech weapon", 4, 0.6f, $"a bright energy zap. {Style}", ""));
        add(new("weapon_reload", "weapons", "a gun or launcher is reloaded", 2, 0.8f, $"a sci-fi magazine reload, slide and click. {Style}", "click"));
        add(new("weapon_empty", "weapons", "using a weapon with no ammo or charge", 2, 0.4f, $"a dry fire click, empty weapon. {Style}", "click"));
        add(new("charge_low", "weapons", "Tech charge runs dry", 2, 0.6f, $"a power-down warning tone, falling electric whine. {Style}", "zap"));
        foreach (var material in new[] { "flesh", "carapace", "metal", "crystal", "slime", "energy", "spirit" })
            add(new($"impact_{material}", "impacts", $"a hit lands on a {material} creature", 3, 0.5f,
                $"a weapon impact hitting {ImpactWords(material)}. {Style}", "hit"));
        add(new("impact_crit", "impacts", "a critical hit lands", 3, 0.5f, $"a sharp heavy critical hit impact with a bright metallic ring. {Style}", "hit", Offset: 1f));
        add(new("projectile_hit_tile", "impacts", "a shot hits the ground", 3, 0.4f, $"a bullet ricochet off rock, short ping and dust. {Style}", "hit", Offset: -2f));
        add(new("explosion", "impacts", "rockets, grenades and explosives", 4, 2.0f, $"a large explosion, deep boom with debris. {Style}", "", Offset: 3f));
    }

    private static string ImpactWords(string material) => material switch
    {
        "flesh" => "flesh, a meaty thwack",
        "carapace" => "a hard insect shell, crunchy crack",
        "metal" => "armored metal plating, clang",
        "crystal" => "a crystal body, glassy chime and shatter",
        "slime" => "a slime creature, wet squelch",
        "energy" => "an energy being, crackling electric pop",
        _ => "a ghostly spirit, hollow airy whoosh"
    };

    private static void AddTools(Action<SoundCue> add)
    {
        add(new("tool_drill", "tools", "mining with a drill", 4, 0.6f, $"a handheld mining drill biting into rock, short grinding whirr. {Style}", "dig"));
        add(new("tool_drill_loop", "tools", "holding a channelled drill", 1, 3.0f, $"a continuous handheld mining drill, steady motor whine and grinding, loopable. {Style}", "tool_drill", Loop: true));
        add(new("tool_chop", "tools", "felling with a cutter", 4, 0.6f, $"a powered cutter chopping into a tree trunk, woody thunk with a buzz. {Style}", "dig"));
        add(new("tool_hammer", "tools", "hammering a block", 3, 0.6f, $"a heavy hammer striking metal and stone, solid clank. {Style}", "dig"));
        add(new("tool_hammer_wall", "tools", "knocking out a background wall", 3, 0.6f, $"a demolition hammer smashing a wall panel, crunchy thud. {Style}", "tool_hammer"));
        add(new("tool_net", "tools", "swinging the bug net", 3, 0.6f, $"a light mesh net swished through the air. {Style}", "swing", Offset: -2f));
        add(new("tool_utility", "tools", "using a bucket, rod, sprayer or wire tool", 2, 0.5f, $"a small handheld gadget activating, soft click and whir. {Style}", "click"));
        add(new("tool_paint", "tools", "spraying paint", 3, 0.6f, $"an aerosol paint spray, short hiss. {Style}", "tool_utility"));
        add(new("tool_wire", "tools", "laying or cutting wire", 3, 0.4f, $"a wire being clipped into place, small snip and electric tick. {Style}", "tool_utility"));
        add(new("bucket_fill", "tools", "scooping liquid with a bucket", 2, 0.8f, $"a metal bucket scooping water, slosh. {Style}", "splash"));
        add(new("bucket_pour", "tools", "pouring liquid", 2, 0.8f, $"pouring liquid out of a bucket, gurgling splash. {Style}", "splash"));
        add(new("fishing_cast", "tools", "casting a fishing line", 3, 0.8f, $"a fishing rod cast, line whizzing out of a reel. {Style}", "swing"));
        add(new("fishing_reel", "tools", "reeling in", 2, 1.0f, $"a fishing reel cranking, ratcheting clicks. {Style}", "click"));
        add(new("bite", "tools", "a fish bites", 2, 0.4f, $"a bobber plunging under water, a plop. {Style}", ""));
        add(new("splash_bobber", "tools", "the bobber lands", 2, 0.4f, $"a small bobber landing on water, light plip. {Style}", "splash"));
        add(new("tool_break", "tools", "a tool fails to break a tile (too hard)", 2, 0.5f, $"a drill bit skidding off very hard rock, harsh metallic scrape. {Style}", "dig"));
    }

    private static void AddTiles(Action<SoundCue> add)
    {
        foreach (var material in TileClasses)
        {
            var words = material switch
            {
                "soil" => "packed alien soil", "stone" => "stone and ore", "metal" => "metal hull plating", "glass" => "glass",
                "wood" => "a wooden panel", "organic" => "soft organic plant matter", "crystal" => "a glowing crystal",
                "ice" => "ice", _ => "loose sand"
            };
            add(new($"dig_{material}", "tiles", $"a tool strikes {material}", 3, 0.4f, $"a tool striking {words}, short crunchy hit. {Style}", "dig", Offset: -2f));
            add(new($"break_{material}", "tiles", $"a {material} block breaks", 3, 0.6f, $"a block of {words} breaking apart, crumbling. {Style}", "break"));
            add(new($"place_{material}", "tiles", $"a {material} block is placed", 3, 0.3f, $"placing a block of {words}, soft solid thunk. {Style}", "place", Offset: -2f));
        }
        add(new("dig", "tiles", "any tool strike", 3, 0.4f, $"a tool striking rock, short crunchy hit. {Style}", "", Offset: -2f));
        add(new("break", "tiles", "any block breaks", 3, 0.6f, $"a block of rock breaking apart, crumbling. {Style}", ""));
        add(new("place", "tiles", "any block is placed", 3, 0.3f, $"placing a block, soft solid thunk. {Style}", "", Offset: -2f));
        add(new("place_wall", "tiles", "a background wall is placed", 3, 0.3f, $"placing a thin wall panel, light tap. {Style}", "place", Offset: -3f));
        add(new("place_furniture", "tiles", "furniture or a station is placed", 3, 0.6f, $"setting down heavy furniture, wooden and metal clunk. {Style}", "place"));
        add(new("plant_grow", "tiles", "a sapling or crop is planted", 2, 0.5f, $"planting a seed into soil, soft patting. {Style}", "place", Offset: -3f));
        add(new("tree_fall", "tiles", "a tree is felled", 3, 1.5f, $"a tree cracking and falling over, creak and crash. {Style}", "break"));
        add(new("grass_cut", "tiles", "tall grass or a plant is cut", 3, 0.4f, $"cutting through tall grass, quick swish and rustle. {Style}", "break", Offset: -3f));
        add(new("pot_break", "tiles", "a pot or container shatters", 3, 0.6f, $"a clay pot shattering. {Style}", "break"));
    }

    private static void AddPlayer(Action<SoundCue> add)
    {
        foreach (var material in TileClasses)
            add(new($"step_{material}", "player", $"walking on {material}", 5, 0.3f, $"a single boot footstep on {material}. {Style}", "", Offset: -6f));
        add(new("jump", "player", "jumping", 4, 0.4f, $"a light jump, short effort breath and boot push-off. {Style}", "", Offset: -2f));
        add(new("jump_double", "player", "a double jump from boots or a cloud", 3, 0.5f, $"a jet boot burst mid-air, puff of thruster. {Style}", "jump"));
        add(new("land", "player", "landing", 4, 0.4f, $"landing on the ground after a jump, boots thud. {Style}", "", Offset: -2f));
        add(new("land_hard", "player", "a hard fall that hurts", 3, 0.5f, $"a heavy hard landing, crunch and grunt. {Style}", "land"));
        add(new("hurt", "player", "the player takes damage", 5, 0.5f, $"a male space-colonist pained grunt, short. {Style}", "", Lane: "record"));
        add(new("death", "player", "the player dies", 3, 1.5f, $"a dramatic death, fading groan and body fall. {Style}", "", Lane: "record"));
        add(new("respawn", "player", "the player respawns", 2, 1.5f, $"a teleporter respawn materialising, rising shimmer. {Style}", "teleport"));
        add(new("breath_out", "player", "breath runs out underwater", 2, 0.8f, $"underwater bubbles, gasping. {Style}", "hurt"));
        add(new("drown", "player", "drowning damage", 2, 0.8f, $"choking underwater bubbles. {Style}", "hurt"));
        add(new("swim", "player", "swimming strokes", 4, 0.5f, $"a swimming stroke, water splash. {Style}", "splash", Offset: -4f));
        add(new("grapple_fire", "player", "firing the grapple", 3, 0.5f, $"a grappling hook firing, cable whizzing out. {Style}", "shoot"));
        add(new("grapple_latch", "player", "the grapple latches on", 3, 0.3f, $"a grappling hook latching into rock, metal clank. {Style}", "hit"));
        add(new("wings_flap", "player", "flying with wings", 3, 0.5f, $"mechanical glider wings beating, whoosh. {Style}", "jump"));
        add(new("rocket_boots", "player", "rocket boots burning", 1, 2.0f, $"continuous small rocket thruster, roaring hiss, loopable. {Style}", "jump", Loop: true));
        add(new("dash", "player", "a dash", 3, 0.4f, $"a quick dash, sharp air rush. {Style}", "swing"));
        add(new("mount_summon", "player", "a mount is summoned", 2, 1.0f, $"a hover vehicle powering up and appearing, rising whine. {Style}", "teleport"));
        add(new("mount_dismiss", "player", "a mount is dismissed", 2, 0.8f, $"a hover vehicle powering down, falling whine. {Style}", "teleport"));
        add(new("level_up", "player", "a max life or charge upgrade is used", 2, 1.2f, $"a power-up upgrade chime, ascending shimmer. {Style}", "craft"));
        add(new("buff_gain", "player", "a buff starts", 2, 0.6f, $"a positive status buff, soft rising chime. {Style}", "pickup"));
        add(new("debuff_gain", "player", "a debuff starts (poison, burning, radiation)", 2, 0.6f, $"a negative status effect, sour falling tone with a sizzle. {Style}", "hurt"));
        add(new("low_health", "player", "health drops low", 1, 1.0f, $"a heartbeat warning thump. {Style}", ""));
    }

    private static void AddInteractions(Action<SoundCue> add)
    {
        (string Cue, string Trigger, float Seconds, string Sound, string Fallback, float Offset)[] rows =
        [
            ("pickup", "picking up an item", 0.3f, "picking up a small item, bright pop", "", -2f),
            ("pickup_coin", "picking up credits", 0.3f, "collecting a credit chip, bright digital coin ping", "coin", -2f),
            ("pickup_heart", "picking up a health drop", 0.4f, "a health pickup, warm soft chime", "pickup", -2f),
            ("pickup_charge", "picking up a charge drop", 0.4f, "an energy pickup, sparkly electric chime", "pickup", -2f),
            ("coin", "buying or selling", 0.4f, "a credit transaction, cash register ping", "", 0f),
            ("craft", "crafting an item", 0.8f, "crafting a tool on a fabricator, quick assembly clicks and whir", "", 0f),
            ("craft_smelt", "crafting at a refinery or furnace", 1.0f, "molten metal in a smelter, roaring furnace whoosh", "craft", 0f),
            ("craft_forge", "crafting at a forge or anvil", 0.8f, "hammering metal on an anvil, ringing clang", "craft", 0f),
            ("craft_brew", "crafting at a lab", 0.8f, "bubbling chemistry glassware, liquid gurgle", "craft", 0f),
            ("craft_weave", "crafting at a loom", 0.8f, "a textile machine, rhythmic shuttle clatter", "craft", 0f),
            ("potion", "drinking a medkit or stim", 0.8f, "drinking from a bottle, gulps and a fizz", "pickup", 0f),
            ("eat", "eating food", 0.8f, "eating crunchy food, chewing bites", "pickup", 0f),
            ("equip", "equipping armor or an accessory", 0.5f, "fastening armor plates, clicks and cloth", "click", -2f),
            ("critter_release", "releasing a caught critter", 0.4f, "a small creature released, soft poof", "pickup", -3f),
            ("door_open", "opening a door", 0.6f, "a sci-fi sliding door opening, pneumatic hiss", "door", 0f),
            ("door_close", "closing a door", 0.6f, "a sci-fi sliding door shutting, hiss and thunk", "door", 0f),
            ("door", "a door moves", 0.6f, "a metal door moving", "", 0f),
            ("chest_open", "opening a storage container", 0.6f, "a metal storage crate lid opening, latch click and creak", "door", 0f),
            ("chest_close", "closing a storage container", 0.5f, "a metal crate lid closing, thunk", "door", 0f),
            ("splash", "entering or leaving liquid", 0.8f, "a body splashing into water", "", 0f),
            ("splash_water", "entering water", 0.8f, "splashing into water", "splash", 0f),
            ("splash_fuel", "entering the fuel sea", 0.8f, "splashing into thick oily liquid, gloopy", "splash", 0f),
            ("splash_acid", "entering acid", 0.8f, "splashing into acid, sizzling hiss", "splash", 0f),
            ("splash_uranium", "entering molten uranium", 0.8f, "plunging into glowing molten liquid, heavy hiss and crackle", "splash", 0f),
            ("teleport", "a recall capsule, pylon or teleporter", 1.2f, "a sci-fi teleport, swelling whoosh and shimmer", "event", 0f),
            ("pylon", "a pylon warp", 1.2f, "a teleport pylon activating, deep hum into shimmer", "teleport", 0f),
            ("wire_switch", "flipping a switch or lever", 0.3f, "a heavy toggle switch, chunky click", "click", 0f),
            ("pressure_plate", "stepping on a pressure plate", 0.3f, "a pressure plate pressed, metal click", "click", 0f),
            ("trap_dart", "a dart trap fires", 0.4f, "a dart trap shooting, sharp pneumatic fft", "shoot", 0f),
            ("trap_spike", "a spike trap", 0.4f, "spikes shooting out of a wall, metal schhk", "hit", 0f),
            ("trap_flame", "a flame trap", 1.0f, "a flamethrower trap bursting, roar of fire", "explosion", -3f),
            ("bed_sleep", "using a bed", 1.0f, "settling into a bunk, cloth rustle and soft sigh", "click", -3f),
            ("npc_arrive", "a town NPC moves in", 1.0f, "a welcoming arrival chime, friendly two-note bell", "event", -2f),
            ("npc_talk", "talking to an NPC", 0.4f, "a short friendly radio chirp", "click", -2f),
            ("shop_buy", "buying from a shop", 0.5f, "a vending terminal purchase, beep and coin", "coin", 0f),
            ("shop_sell", "selling to a shop", 0.5f, "a vending terminal sale, coins dropping", "coin", 0f),
            ("reforge", "recalibrating an item", 1.0f, "a calibration machine humming and a satisfied ding", "craft", 0f),
            ("summon_item", "using a boss summoning item", 2.0f, "an ominous beacon activating, deep rumbling pulse", "boss", 0f),
            ("fracture", "the world enters hardmode", 4.0f, "the planet cracking open, enormous rumble and roar", "thunder", 3f),
            ("thunder", "lightning strikes", 3.0f, "a close thunder crack and rolling rumble", "", 3f),
            ("meteor_impact", "a meteor lands", 3.0f, "a meteor crashing into the ground, huge boom and rumble", "explosion", 3f)
        ];
        foreach (var row in rows)
            add(new(row.Cue, "interactions", row.Trigger, 3, row.Seconds, $"{row.Sound}. {Style}", row.Fallback, Offset: row.Offset));
    }

    private static void AddUi(Action<SoundCue> add)
    {
        const string ui = "clean sci-fi user interface sound, short, soft, subtle, no music";
        (string Cue, string Trigger, string Sound, string Fallback, float Offset)[] rows =
        [
            ("hover", "the cursor moves over a button", "a tiny soft tick", "", -12f),
            ("click", "a button is pressed", "a crisp button click", "", -8f),
            ("open_inventory", "opening the inventory", "a holographic panel sliding open, airy whoosh", "click", -6f),
            ("close_inventory", "closing the inventory", "a holographic panel closing, soft swoosh", "click", -6f),
            ("tab", "switching a tab", "a light tab switch tick", "click", -8f),
            ("error", "an action is refused", "a low error buzz", "click", -6f),
            ("craft_ok", "a recipe is ready", "a positive confirm beep", "click", -6f),
            ("achievement", "an achievement or milestone", "a bright achievement fanfare chime", "craft", -2f),
            ("chat_tick", "a message line appears", "a soft data tick", "hover", -10f),
            ("map_open", "opening the map", "a holographic map unfolding, shimmering whoosh", "click", -6f),
            ("trash", "trashing an item", "a quick disposal crunch", "click", -6f),
            ("item_drag", "picking up a slot item with the cursor", "a small grab tick", "click", -10f),
            ("item_drop", "dropping a slot item", "a small soft put-down tap", "click", -10f),
            ("menu_back", "going back in a menu", "a soft descending blip", "click", -8f)
        ];
        foreach (var row in rows)
            add(new(row.Cue, "ui", row.Trigger, 2, 0.3f, $"{row.Sound}. {ui}", row.Fallback, Offset: row.Offset));
    }

    private static void AddCreatures(Action<SoundCue> add)
    {
        var voices = EnemyArchetypes.All.Where(archetype => !archetype.IsBoss).Select(CreatureVoice).Distinct().OrderBy(name => name);
        foreach (var voice in voices)
        {
            var parts = voice.Split('_');
            var (family, size) = (parts[1], parts[2]);
            var body = family switch
            {
                "critter" => "a small harmless alien animal",
                "worm" => "a giant burrowing alien worm",
                "caster" => "an alien psychic caster",
                "mimic" => "a monster disguised as a metal crate",
                "hopper" => "a hopping alien creature",
                "flyer" => "a flying alien creature with buzzing wings",
                "machine" => "a hostile robot",
                _ => "a feral alien beast"
            };
            var scale = size switch { "small" => "small, high-pitched", "large" => "huge, deep, booming", _ => "medium-sized" };
            add(new($"{voice}_idle", "creatures", $"{family} creatures ({size}) idle", 3, 1.0f, $"{body}, {scale}, idle chitter or growl. {Style}", "", Offset: -4f));
            add(new($"{voice}_hurt", "creatures", $"{family} creatures ({size}) are hit", 3, 0.5f, $"{body}, {scale}, pained screech when hit. {Style}", "hit"));
            add(new($"{voice}_death", "creatures", $"{family} creatures ({size}) die", 3, 1.0f, $"{body}, {scale}, death cry and collapse. {Style}", "death"));
        }
        foreach (var boss in EnemyArchetypes.All.Where(archetype => archetype.IsBoss))
        {
            var name = boss.DisplayName;
            var body = CreatureMaterial(boss) switch
            {
                "metal" => "a colossal war machine", "slime" => "a gigantic slime", "crystal" => "a huge crystal titan",
                "spirit" => "an enormous ghostly entity", "energy" => "a towering psychic being", "carapace" => "a giant armored insect", _ => "a giant alien monster"
            };
            add(new(BossCue(boss.Kind, "roar"), "bosses", $"{name} arrives", 2, 3.0f, $"{body} ({name}) awakening with a terrifying roar. {Style}", "boss", Offset: 3f));
            add(new(BossCue(boss.Kind, "attack"), "bosses", $"{name} attacks", 3, 1.0f, $"{body} ({name}) lunging attack, powerful. {Style}", "hit", Offset: 1f));
            add(new(BossCue(boss.Kind, "hurt"), "bosses", $"{name} is hit", 3, 0.6f, $"{body} ({name}) taking a hit, deep pained grunt. {Style}", "hit"));
            add(new(BossCue(boss.Kind, "death"), "bosses", $"{name} is defeated", 2, 4.0f, $"{body} ({name}) defeated, dying roar and massive collapse. {Style}", "death", Offset: 3f));
        }
        add(new("boss", "bosses", "any boss arrives", 2, 2.0f, $"an ominous deep boss arrival hit with a low drone. {Style}", "", Offset: 3f));
        add(new("hit", "impacts", "any hit", 3, 0.4f, $"a generic melee impact, solid thud. {Style}", ""));
        add(new("death", "creatures", "any creature dies", 3, 0.8f, $"a creature death, collapse and fading cry. {Style}", ""));
    }

    private static void AddEvents(Action<SoundCue> add)
    {
        foreach (var spec in new WorldEventSystem(1).Specs)
        {
            var mood = spec.Category switch
            {
                WorldEventCategory.Weather => "a weather front rolling in",
                WorldEventCategory.Invasion => "an invasion alarm, urgent klaxon",
                WorldEventCategory.Night => "an eerie night event, ominous swelling drone",
                WorldEventCategory.Party => "a festive celebration fanfare",
                WorldEventCategory.Calm => "a calm peaceful shimmer",
                _ => "a world event announcement, dramatic swell"
            };
            add(new(EventCue(spec.Kind, "start"), "events", $"{spec.Name} begins", 1, 3.0f, $"{mood} for the {spec.Name} event. {Style}", "event", Offset: 2f));
            add(new(EventCue(spec.Kind, "end"), "events", $"{spec.Name} ends", 1, 2.5f, $"a resolving victory stinger, the {spec.Name} event is over. {Style}", "event"));
        }
        add(new("event", "events", "any event begins", 2, 1.5f, $"a world event announcement swell. {Style}", ""));
        add(new("boss_spawn", "events", "a boss is summoned", 1, 3.0f, $"a boss summoning, deep rising rumble and impact. {Style}", "boss", Offset: 3f));
    }

    private static readonly (string Theme, string Place)[] AmbientPlaces =
    [
        ("grassland", "open alien grassland with wind and strange insects"),
        ("forest", "an alien forest with rustling leaves and distant creature calls"),
        ("canopy", "a dense humid jungle with dripping water, insects and exotic birds"),
        ("coast", "a shore of a thick fuel sea, slow heavy waves"),
        ("frozen", "a frozen wasteland, icy howling wind, creaking ice"),
        ("junk", "a junkyard wasteland, metal creaks, wind through scrap, distant clanks"),
        ("radiated", "an irradiated zone, geiger counter crackle and low electric hum"),
        ("overgrowth", "a creeping overgrown blight, wet organic squelches and low throbbing"),
        ("crystal", "a crystal field, soft glassy chimes and shimmer"),
        ("fungal", "a glowing fungus cavern, soft pops and dripping spores"),
        ("hives", "inside a hive, droning buzzing insects"),
        ("warrens", "silk-strung mite nests, skittering and clicking"),
        ("vault", "inside an ancient precursor vault, deep machine hum and echoes"),
        ("underground", "a deep cave, water drips and distant rumbles"),
        ("molten", "a molten chasm, bubbling magma and roaring heat"),
        ("core", "the irradiated planet core, heavy throbbing hum and crackle"),
        ("space", "the edge of space, thin high wind and a quiet hum"),
        ("town", "a small frontier colony, gentle machinery and distant chatter")
    ];

    private static void AddAmbience(Action<SoundCue> add)
    {
        const string loop = "seamless ambient background loop, field recording style, no music, no speech";
        foreach (var (theme, place) in AmbientPlaces)
        {
            add(new($"amb_{theme}_day", "ambience", $"daytime in {theme}", 1, 20f, $"{place}, daytime. {loop}", "", Loop: true, Offset: -8f));
            if (theme is not ("underground" or "molten" or "core" or "fungal" or "hives" or "warrens" or "vault"))
                add(new($"amb_{theme}_night", "ambience", $"night in {theme}", 1, 20f, $"{place}, at night, quieter, crickets and nocturnal calls. {loop}", $"amb_{theme}_day", Loop: true, Offset: -8f));
        }
        (string Cue, string Sound)[] weather =
        [
            ("amb_rain_light", "light rain pattering"), ("amb_rain_heavy", "heavy rainstorm downpour"), ("amb_wind", "strong gusting wind"),
            ("amb_blizzard", "a howling blizzard"), ("amb_sandstorm", "a roaring sandstorm with hissing grains"), ("amb_acid_rain", "sizzling acid rain"),
            ("amb_waterfall", "a nearby waterfall"), ("amb_uranium_flow", "flowing molten uranium, bubbling hiss"), ("amb_machinery", "running colony machinery, steady hum and clanks"),
            ("amb_underwater", "underwater, muffled bubbling"), ("amb_fire", "a crackling campfire")
        ];
        foreach (var (cue, sound) in weather)
            add(new(cue, "ambience", cue[4..].Replace('_', ' '), 1, 20f, $"{sound}. {loop}", "", Loop: true, Offset: -6f));
    }

    /// <summary>
    /// The music slots, Terraria-shaped: one track per biome for day, night and (where it exists) underground, one per
    /// world event, one per boss tier plus the final boss, and title, town and space. Each style card follows the
    /// procedural theme of the same name (AudioEngine.MusicState) so a generated track and its synth fallback agree.
    /// </summary>
    private static readonly (string Theme, int Bpm, string Key, int Meter, string Card, bool Surface, bool Underground)[] MusicThemes =
    [
        ("title", 112, "G major", 4, "heroic retro synth-pop overture, warm analog synths, gated drums, orchestral strings, sense of adventure", false, false),
        ("grassland", 126, "A major", 4, "upbeat space-frontier funk, slap bass, clavinet, bright strings, optimistic exploration", true, true),
        ("forest", 112, "C# minor", 4, "laid-back funk groove, muted guitar, warm electric piano, curious and green", true, false),
        ("canopy", 136, "B major", 4, "driving baroque strings over tribal percussion, Vivaldi-like runs, lush jungle", true, true),
        ("coast", 112, "C major", 3, "romantic waltz, sweeping strings, harp, gentle waves, airy shimmer", true, false),
        ("frozen", 120, "D major", 4, "crystalline baroque counterpoint, harpsichord and bells, cold airy shimmer, half-time beat", true, true),
        ("junk", 124, "A minor", 4, "gritty breakbeat funk, wah guitar, scrapyard metal percussion, swagger", true, true),
        ("radiated", 134, "D minor", 4, "tense baroque fugue over breakbeats, harmonic minor, synth strings, danger", true, true),
        ("overgrowth", 104, "F minor", 4, "dark slinky funk, swung beat, eerie pads, creeping blight", true, true),
        ("crystal", 130, "E major", 4, "sparkling baroque arpeggios, glassy bells, breakbeat, wonder", true, true),
        ("fungal", 104, "Bb minor", 3, "dreamy glowing waltz, music box, soft strings, mysterious", false, true),
        ("hives", 120, "G minor", 4, "buzzing pizzicato strings, nervous rhythms, warm drones", false, true),
        ("warrens", 116, "E minor", 4, "skittering staccato strings, tense pulses, cobweb atmosphere", false, true),
        ("vault", 96, "C minor", 4, "ancient precursor ambience, deep choir pads, slow synth pulses, awe", false, true),
        ("underground", 118, "G minor", 4, "brooding baroque cave music, half-time drums, cello and harpsichord, echoing", false, true),
        ("molten", 144, "E minor", 4, "furious rock and Vivaldi strings, distorted guitars, phrygian dominant, heat", false, true),
        ("core", 144, "E minor", 4, "relentless industrial rock, distorted guitars, throbbing synth bass, radiation", false, true),
        ("space", 90, "F major", 4, "vast ambient space music, slow synth pads, distant bells, weightless", false, false),
        ("town", 100, "D major", 4, "cozy frontier settlement theme, acoustic guitar, warm strings, light funk", false, false),
        ("ocean", 108, "C major", 3, "underwater waltz, slow strings and harp, muffled shimmer", false, false)
    ];

    private static void AddMusic(Action<SoundCue> add)
    {
        const string music = "instrumental video game soundtrack, loopable, no vocals";
        foreach (var (theme, bpm, key, meter, card, surface, underground) in MusicThemes)
        {
            var slot = surface ? $"mus_{theme}_day" : $"mus_{theme}";
            add(new(slot, "music", surface ? $"{theme}, daytime" : theme, 1, 150f, $"{card}. {music}", "", Loop: true, Offset: -4f, Bpm: bpm, Key: key, Meter: meter));
            if (surface)
                add(new($"mus_{theme}_night", "music", $"{theme}, at night", 1, 150f, $"{card}, nocturnal, softer and sparser, slower. {music}", slot,
                    Loop: true, Offset: -4f, Bpm: (int)(bpm * 0.94f), Key: key, Meter: meter));
            if (surface && underground)
                add(new($"mus_{theme}_underground", "music", $"{theme}, underground", 1, 150f, $"{card}, deeper and darker, cavernous reverb. {music}", "mus_underground",
                    Loop: true, Offset: -4f, Bpm: (int)(bpm * 0.92f), Key: key, Meter: meter));
        }
        foreach (var spec in new WorldEventSystem(1).Specs)
        {
            var (bpm, key, card) = spec.Category switch
            {
                WorldEventCategory.Invasion => (150, "C minor", "urgent battle music, pounding drums, brass stabs, alarm synths"),
                WorldEventCategory.Night => (128, "D minor", "sinister night siege music, eerie choir pads, driving strings"),
                WorldEventCategory.Weather => (110, "A minor", "stormy atmospheric music, rolling timpani, swirling strings"),
                WorldEventCategory.Party => (120, "F major", "festive celebration music, funky brass and claps"),
                WorldEventCategory.Calm => (90, "E major", "serene glowing music, soft bells and pads"),
                _ => (132, "E minor", "dramatic event music, rising strings and percussion")
            };
            add(new($"mus_event_{Snake(spec.Kind.ToString())}", "music", $"during {spec.Name}", 1, 150f, $"{card}, theme for the {spec.Name} event. {music}", "mus_boss_easy",
                Loop: true, Offset: -3f, Bpm: bpm, Key: key, Meter: 4));
        }
        (BossTier Tier, int Bpm, string Key, string Card)[] bosses =
        [
            (BossTier.Gate, 150, "D minor", "first boss battle, aggressive rock and strings"),
            (BossTier.Easy, 156, "E minor", "intense boss battle, driving rock, Vivaldi strings, brass"),
            (BossTier.Hard, 164, "C# minor", "hardmode boss battle, heavy industrial metal, epic choir"),
            (BossTier.Event, 160, "F# minor", "event boss battle, frantic percussion and synth leads"),
            (BossTier.Final, 170, "B minor", "final boss battle, epic orchestral metal, choir, overwhelming")
        ];
        foreach (var (tier, bpm, key, card) in bosses)
            add(new($"mus_boss_{Snake(tier.ToString())}", "music", $"fighting a {tier} boss", 1, 150f, $"{card}. {music}", tier == BossTier.Gate ? "" : "mus_boss_gate",
                Loop: true, Offset: -2f, Bpm: bpm, Key: key, Meter: 4));
    }

    private static bool Has(string text, params string[] words) => words.Any(text.Contains);

    internal static string Snake(string name)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && builder.Length > 0)
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }
}
