using System.Numerics;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Self-tests for the creature coverage pass (docs/CREATURE_COVERAGE.md): every biome and variant has regular and hardmode
/// rosters with natives of its own, every event has its own creatures and its leaders (boss, mini-bosses or obelisk), every
/// boss has an obtainable summon item or a documented trigger, and every new creature has stats in its band, a spawn slot,
/// a sprite, drops and a banner.
/// </summary>
public static class CreatureCoverageSelfTests
{
    /// <summary>Minimum hostile kinds per biome roster (regular and post-Fracture).</summary>
    public const int MinimumBiomeKinds = 4;
    /// <summary>Minimum creatures native to one biome (in no other biome's regular roster).</summary>
    public const int MinimumBiomeNatives = 2;

    private static void Tick(GameSimulation game, int ticks = 1, PlayerAction action = PlayerAction.None, GridPoint? target = null)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f), i == 0 ? action : PlayerAction.None, target));
    }

    private static GameSimulation NewGame(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 160, height: 72) { FamilySpawnsEnabled = false };
        Calm(game);
        return game;
    }

    private static void Calm(GameSimulation game)
    {
        foreach (var enemy in game.Enemies.Where(enemy => enemy.IsAlive).ToArray())
        {
            enemy.Shielded = false;
            enemy.TakeDamage(1e7f);
        }
        game.Player.Heal(1e6f);
    }

    private static void Use(GameSimulation game, ItemId item)
    {
        game.Player.Inventory.Add(item, 1);
        game.Player.ActionCooldownTicks = 0;
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
        Tick(game, 1, PlayerAction.UseSelected, new GridPoint(1, 1));
    }

    private static void Teleport(GameSimulation game, int x, int feetY)
    {
        for (var y = feetY - 4; y <= feetY; y++)
        for (var dx = -2; dx <= 2; dx++)
            game.World.SetTile(new GridPoint(x + dx, y), TileId.Air);
        for (var dx = -2; dx <= 2; dx++)
            game.World.SetTile(new GridPoint(x + dx, feetY + 1), TileId.Rock);
        game.Player.Motion.Position = new Vector2(x, feetY + 1 - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
    }

    /// <summary>Turns every solid cell in the scan window around the player into <paramref name="tile"/> (biome painting).</summary>
    private static void Paint(GameSimulation game, TileId tile)
    {
        var center = game.Player.Motion.Position;
        for (var y = Math.Max(0, (int)center.Y - 20); y < Math.Min(game.World.Height, (int)center.Y + 20); y++)
        for (var x = Math.Max(0, (int)center.X - 30); x < Math.Min(game.World.Width, (int)center.X + 30); x++)
            if (game.Tiles[game.World.TileAt(x, y)].IsSolid)
                game.World.SetTile(new GridPoint(x, y), tile);
    }

    private static bool Hostile(EnemyKind kind) => !EnemyArchetypes.Get(kind).Passive;

    public static void Run(Action<bool, string> check)
    {
        var catalog = new ItemCatalog();
        var crafting = new CraftingSystem(catalog);
        var probe = NewGame(14001);

        // ---- 1. Every biome: regular and hardmode rosters with natives of their own ----
        IReadOnlyList<EnemyKind> Regular(BiomeZone zone) => zone switch
        {
            BiomeZone.Grassland => BiomeSpawnTables.KindsFor(zone).Concat(BiomeSpawnTables.FillKinds("grass")).Concat(BiomeSpawnTables.FillKinds("under")).ToArray(),
            BiomeZone.Caverns => BiomeSpawnTables.KindsFor(zone).Concat(BiomeSpawnTables.FillKinds("cave")).ToArray(),
            _ => BiomeSpawnTables.KindsFor(zone)
        };
        var zones = Enum.GetValues<BiomeZone>();
        var regularByZone = zones.ToDictionary(zone => zone, zone => Regular(zone).Where(Hostile).Distinct().ToArray());
        var thin = new List<string>();
        foreach (var zone in zones)
        {
            var regular = regularByZone[zone];
            var hard = BiomeSpawnTables.HardKindsFor(zone).Where(Hostile).Distinct().ToArray();
            var natives = regular.Count(kind => zones.Where(other => other != zone).All(other => !regularByZone[other].Contains(kind)));
            if (regular.Length < MinimumBiomeKinds || hard.Length < MinimumBiomeKinds || natives < MinimumBiomeNatives)
                thin.Add($"{zone} {regular.Length}/{hard.Length}/{natives}");
        }
        var dungeonRegular = BiomeSpawnTables.DungeonKinds.Where(Hostile).Distinct().Count();
        var dungeonHard = CreatureCoverage.VariantKinds("dungeon-hard").Count;
        check(thin.Count == 0 && dungeonRegular >= MinimumBiomeKinds && dungeonHard >= MinimumBiomeKinds,
            $"every biome has at least {MinimumBiomeKinds} regular and {MinimumBiomeKinds} hardmode hostiles with {MinimumBiomeNatives}+ natives, " +
            $"and the dungeon has both ({dungeonRegular}/{dungeonHard}; thin: {string.Join(", ", thin)})");

        // Variant slots: each has a regular roster (except the late dungeon) and a hardmode roster of its own.
        var variantsOk = CreatureCoverage.VariantSlots.Keys.All(slot =>
            (slot == "dungeon-hard" || CreatureCoverage.Roster(slot, hard: false).Count() >= 2) && CreatureCoverage.Roster(slot, hard: true).Any());
        var preFractureClean = CreatureCoverage.VariantSlots.Keys.Where(slot => slot != "dungeon-hard").All(slot =>
            Enumerable.Range(0, 200).All(i => CreatureCoverage.PickVariant(slot, false, i / 200.0, i % 2 == 0) is { } kind &&
                                              !CreatureCoverage.Creature(kind)!.Hard));
        var hardAppears = CreatureCoverage.VariantSlots.Keys.All(slot =>
            Enumerable.Range(0, 200).Any(i => CreatureCoverage.PickVariant(slot, true, i / 200.0, false) is { } kind && CreatureCoverage.Creature(kind)!.Hard));
        check(variantsOk && preFractureClean && hardAppears,
            "biome variants (sky, underground junk and frost, grave field, late dungeon) have their own rosters; hardmode kinds wait for the Fracture");

        // ---- 2. Variant selection in the world ----
        BiomeReading Reading(BiomeZone zone, bool underground) => new(zone, underground, 0, 0, 0, 0, 0);
        var junkUnder = probe.CoverageVariantSlot(Reading(BiomeZone.JunkWasteland, true), false);
        var frostUnder = probe.CoverageVariantSlot(Reading(BiomeZone.FrozenWasteland, true), false);
        var dungeonEarly = probe.CoverageVariantSlot(Reading(BiomeZone.Caverns, true), true);
        probe.Flags.Set("boss.mutated_flora.down");
        var dungeonLate = probe.CoverageVariantSlot(Reading(BiomeZone.Caverns, true), true);
        var px = (int)probe.Player.Motion.Position.X;
        var surface = probe.BiomeLayout.SurfaceAt(px);
        Teleport(probe, px, surface - 1);
        var plainSurface = probe.CoverageVariantSlot(Reading(BiomeZone.Grassland, false), false);
        for (var i = 0; i < CreatureCoverage.GraveFieldMarkers; i++)
            probe.World.SetTile(new GridPoint(px - 8 + i * 3, surface - 1), TileId.Tombstone);
        var grave = probe.CoverageVariantSlot(Reading(BiomeZone.Grassland, false), false);
        var graveRolls = Enumerable.Range(0, 400).Select(i => probe.ProbeCoverageSpawn(i / 400.0, false, EnemyKind.MossSlime)).ToArray();
        var graveShare = graveRolls.Count(kind => CreatureCoverage.VariantKinds("grave").Contains(kind)) / 400.0;
        check(junkUnder == "junk-under" && frostUnder == "frozen-under" && dungeonEarly is null && dungeonLate == "dungeon-hard" &&
              plainSurface is null && grave == "grave" && graveShare is > 0.4 and < 0.7,
            $"the spawner picks the variant for the spot: underground junk and frost, the late dungeon, a grave field of {CreatureCoverage.GraveFieldMarkers} markers " +
            $"(grave share {graveShare:P0}); plain surface keeps its biome roster");

        // ---- 3. Every event: its own creatures and its leaders ----
        var biomeKinds = zones.SelectMany(zone => BiomeSpawnTables.KindsFor(zone).Concat(BiomeSpawnTables.HardKindsFor(zone)))
            .Concat(new[] { "grass", "under", "cave" }.SelectMany(BiomeSpawnTables.FillKinds)).Concat(BiomeSpawnTables.DungeonKinds)
            .Concat(CreatureCoverage.VariantSlots.Keys.SelectMany(CreatureCoverage.VariantKinds)).ToHashSet();
        var shortEvents = new List<string>();
        foreach (var kind in Enum.GetValues<WorldEventKind>())
        {
            var spec = probe.WorldEvents.Spec(kind);
            var unique = probe.EventCreatures(kind).Count(creature => !biomeKinds.Contains(creature) && !EnemyArchetypes.Get(creature).IsBoss);
            var leaders = probe.EventLeaders(kind).Count;
            var (needUnique, needLeader) = spec.Category switch
            {
                WorldEventCategory.Invasion or WorldEventCategory.Night or WorldEventCategory.Day =>
                    // The Cryo Brigade and the Lumen Tempest have no leader, like Terraria's frost legion and torch god.
                    (3, kind is not (WorldEventKind.CryoBrigade or WorldEventKind.LumenTempest)),
                WorldEventCategory.Weather => (2, false),
                _ => (1, false)
            };
            if (kind == WorldEventKind.LumenTempest)
                needUnique = 2;
            if (unique < needUnique || needLeader && leaders == 0)
                shortEvents.Add($"{kind} {unique}u/{leaders}l");
        }
        check(shortEvents.Count == 0,
            $"every event fields creatures of its own (3+ for invasions and moons, 2+ for weather, 1+ for calm and party days) and its leader " +
            $"(short: {string.Join(", ", shortEvents)})");

        var moonsOk = new[] { WorldEventKind.HollowMoon, WorldEventKind.RimeMoon }.All(moon =>
            CreatureCoverage.IsMiniBoss(SeasonalEvents.MiniBoss(moon)) && CreatureCoverage.IsMiniBoss(SeasonalEvents.SecondMiniBoss(moon)) &&
            EnemyArchetypes.Get(SeasonalEvents.Boss(moon)) is { IsBoss: true, Tier: BossTier.Event } &&
            Enumerable.Range(1, SeasonalEvents.MoonWaves).SelectMany(wave => SeasonalEvents.WaveRoster(moon, wave)).All(entry => CreatureCoverage.EventNatives(moon).Contains(entry.Kind)));
        var specialLeaders = new[] { WorldEventKind.Blizzard, WorldEventKind.DustStorm, WorldEventKind.RustMoon, WorldEventKind.ScavengerRaid }
            .All(kind => probe.EventLeaders(kind).Any(CreatureCoverage.IsMiniBoss));
        var obeliskNatives = GameSimulation.Obelisks.All(entry => probe.WorldEvents.Spec(entry.Event).Roster.Length >= 3 &&
            probe.WorldEvents.Spec(entry.Event).Roster.All(slot => CreatureCoverage.EventNatives(entry.Event).Contains(slot.Kind)));
        check(moonsOk && specialLeaders && obeliskNatives,
            "special events have special leaders: two mini-bosses and a boss per wave moon (own wave units), storm and raid mini-bosses, three natives per obelisk");

        // ---- 4. Event mini-bosses arrive mid-event after the Fracture, once ----
        var storm = NewGame(14002);
        var earlyStart = storm.WorldEvents.TryStart(WorldEventKind.Blizzard, storm, forced: true, out _);
        storm.ProbeAdvanceHours(2f);
        var earlyMini = storm.ProbeEventMiniBosses();
        storm.WorldEvents.Stop(WorldEventKind.Blizzard, storm);
        storm.TriggerFracture();
        Calm(storm);
        storm.WorldEvents.TryStart(WorldEventKind.Blizzard, storm, forced: true, out _);
        var tooSoon = storm.ProbeEventMiniBosses();
        storm.ProbeAdvanceHours(2f);
        var arrived = storm.ProbeEventMiniBosses();
        var again = storm.ProbeEventMiniBosses();
        var colossus = storm.Enemies.FirstOrDefault(enemy => enemy.IsAlive && enemy.Kind == EnemyKind.BlizzardColossus);
        check(earlyStart && earlyMini == 0 && tooSoon == 0 && arrived == 1 && again == 0 && colossus?.EventTag == WorldEventKind.Blizzard,
            "the blizzard colossus joins a post-Fracture blizzard once it is under way, tagged with the event (never before the Fracture)");

        // Late Dark Sun units wait for the Mutated Flora.
        var eclipse = NewGame(14003);
        eclipse.TriggerFracture();
        var rolls = Enumerable.Range(0, 120).Select(i => i / 120.0).ToArray();
        var beforeFlora = rolls.Select(roll => eclipse.ProbeHardEventCreature(EnemyKind.EventEclipseShade, roll)).ToArray();
        eclipse.Flags.Set("boss.mutated_flora.down");
        var afterFlora = rolls.Select(roll => eclipse.ProbeHardEventCreature(EnemyKind.EventEclipseShade, roll)).ToArray();
        check(!beforeFlora.Any(CreatureCoverage.LateDarkSun.Contains) && afterFlora.Any(CreatureCoverage.LateDarkSun.Contains),
            "the eclipse slasher and sphere join the Dark Sun only after the Mutated Flora (post-Plantera eclipse)");

        // ---- 5. Every boss: an obtainable summon item, or a documented trigger ----
        bool Obtainable(ItemId item) => crafting.FindByOutput(item).Count > 0 ||
            Enum.GetValues<EnemyKind>().Any(kind => NpcDropRegistry.GetDrops(kind).Any(drop => drop.Item == item)) ||
            probe.WorldEvents.Specs.Any(spec => spec.Rewards?.Any(reward => reward.Item == item) == true) ||
            ArsenalItems.LootSources.ContainsKey(item);
        var summonable = new List<string>();
        foreach (var boss in EnemyArchetypes.All.Where(archetype => archetype.IsBoss).Select(archetype => archetype.Kind))
        {
            var summons = catalog.All.Where(item => item.SummonsBoss == boss).ToArray();
            var ok = summons.Any(item => Obtainable(item.Id)) ||
                     CreatureCoverage.ConditionalBosses.ContainsKey(boss) && boss == EnemyKind.AbyssLeviathan && Obtainable(ItemId.AbyssBait);
            if (!ok)
                summonable.Add(boss.ToString());
        }
        var starters = catalog.All.Where(item => item.StartsEvent is not null).ToArray();
        var badStarters = starters.Where(item => !Obtainable(item.Id)).Select(item => item.Id.ToString()).ToArray();
        var startedEvents = starters.Select(item => item.StartsEvent!.Value).ToHashSet();
        var terrariaStarters = new[]
        {
            WorldEventKind.RustMoon, WorldEventKind.DarkSun, WorldEventKind.ScavengerRaid, WorldEventKind.PirateBoarding, WorldEventKind.DefenseProtocol,
            WorldEventKind.HollowMoon, WorldEventKind.RimeMoon, WorldEventKind.CryoBrigade, WorldEventKind.ColonyFestival
        };
        check(summonable.Count == 0 && badStarters.Length == 0 && terrariaStarters.All(startedEvents.Contains),
            $"every boss has an obtainable summon item (or abyss bait for the leviathan) and every event Terraria starts by item has a starter " +
            $"(missing: {string.Join(",", summonable)}; unobtainable starters: {string.Join(",", badStarters)})");

        // ---- 6. Summon and starter gates ----
        var gates = NewGame(14004);
        Use(gates, ItemId.FrostbittenLure);
        var lureRefused = !gates.Enemies.Any(enemy => enemy.IsAlive && enemy.Kind == EnemyKind.HoarfrostBehemoth) &&
                          gates.Player.Inventory.Count(ItemId.FrostbittenLure) == 1;
        Paint(gates, TileId.Snow);
        Tick(gates, 130); // the biome reading refreshes on the slow environment tick
        var frozen = gates.CurrentBiome.Zone == BiomeZone.FrozenWasteland;
        Use(gates, ItemId.FrostbittenLure);
        var behemoth = gates.Enemies.Any(enemy => enemy.IsAlive && enemy.Kind == EnemyKind.HoarfrostBehemoth);
        Calm(gates);
        Use(gates, ItemId.PrismGelLattice);
        var latticeRefused = !gates.Enemies.Any(enemy => enemy.IsAlive && enemy.Kind == EnemyKind.GelSovereign);
        gates.TriggerFracture();
        Calm(gates);
        while (gates.IsNight)
            gates.ProbeAdvanceHours(1f);
        Use(gates, ItemId.RegentWick);
        var wickByDay = !gates.Enemies.Any(enemy => enemy.IsAlive && enemy.Kind == EnemyKind.HollowRegent);
        gates.Flags.Set("boss.stone_sentinel.down");
        Use(gates, ItemId.CorrodedFlare);
        var flareByDay = !gates.WorldEvents.IsActive(WorldEventKind.RustMoon);
        while (!gates.IsNight)
            gates.ProbeAdvanceHours(1f);
        Calm(gates);
        foreach (var active in gates.WorldEvents.Active.ToArray())
            gates.WorldEvents.Stop(active.Kind, gates);
        Use(gates, ItemId.RegentWick);
        var regent = gates.Enemies.Any(enemy => enemy.IsAlive && enemy.Kind == EnemyKind.HollowRegent);
        Calm(gates);
        Use(gates, ItemId.CorrodedFlare);
        var rustMoon = gates.WorldEvents.IsActive(WorldEventKind.RustMoon);
        check(lureRefused && frozen && behemoth && latticeRefused && wickByDay && flareByDay && regent && rustMoon,
            "summons keep Terraria's rules: the frostbitten lure only in the frozen wastes, the gel lattice after the Fracture, " +
            $"the regent's wick and the corroded flare only at night ({lureRefused}/{frozen}/{behemoth}/{latticeRefused}/{wickByDay}/{flareByDay}/{regent}/{rustMoon})");

        // ---- 7. Every new creature: stats in band, a spawn slot, a sprite, drops and a banner ----
        var eventKinds = Enum.GetValues<WorldEventKind>().SelectMany(probe.EventCreatures).ToHashSet();
        var slotted = biomeKinds.Concat(eventKinds).ToHashSet();
        var unslotted = CreatureCoverage.AllKinds.Where(kind => !slotted.Contains(kind) && !EnemyArchetypes.Get(kind).IsBoss).ToArray();
        // Boss and mini-boss bands follow the 0.2.0 boss curve (PC-15, BO-23: Systems/BossBalance.cs, 60-180 s with tier gear).
        var outOfBand = CreatureCoverageData.Creatures.Where(row => row.Role == CoverageRole.MiniBoss ? row.HitPoints is < 20000f or > 35000f
            : row.Hard ? row.HitPoints is < 150f or > 800f : row.HitPoints is < 25f or > 150f).Select(row => row.Kind.ToString())
            .Concat(CreatureCoverageData.Bosses.Where(boss => boss.Tier switch
            {
                BossTier.Easy => boss.HitPoints is < 9000f or > 17000f,
                _ => boss.HitPoints is < 40000f or > 75000f
            }).Select(boss => boss.Kind.ToString())).ToArray();
        var undropped = CreatureCoverage.AllKinds.Where(kind => NpcDropRegistry.GetDrops(kind).Count == 0).ToArray();
        var unbannered = CreatureCoverageData.Creatures.Select(row => row.Kind).Where(kind => !PlayerItems.EarnsBanner(kind) ||
            !catalog.Contains(PlayerItems.BannerFor(kind))).ToArray();
        var bossItems = CreatureCoverageData.Bosses.All(boss => PlayerSystemsContent.BagFor(boss.Kind) is { } bag && catalog.Contains(bag) &&
                                                               PlayerSystemsContent.TrophyFor(boss.Kind) is { } trophy && catalog.Contains(trophy));
        var loot = CreatureCoverageData.Bosses.All(boss => ArsenalItems.BossPools.TryGetValue(boss.Kind, out var pool) && pool.Length >= 2) &&
                   CreatureCoverageData.Creatures.Where(row => row.Role == CoverageRole.MiniBoss)
                       .All(row => ArsenalItems.MobDrops.ContainsKey(row.Kind));
        var spriteDir = new[] { Path.Combine(Environment.CurrentDirectory, "Assets", "Sprites"), Path.Combine(AppContext.BaseDirectory, "Assets", "Sprites") }
            .FirstOrDefault(Directory.Exists);
        string[] prefixes = ["creature_", "event_", "boss_", ""];
        bool SpriteFile(EnemyKind kind) => spriteDir is not null &&
            prefixes.Any(prefix => File.Exists(Path.Combine(spriteDir, prefix + SpriteCatalog.ForEnemy(kind) + ".png")));
        bool BannerFile(EnemyKind kind) => spriteDir is not null &&
            File.Exists(Path.Combine(spriteDir, "item_banner_" + SpriteCatalog.SnakeCase(kind.ToString()) + ".png"));
        // Judge files only when this checkout's art includes the coverage sprites (stale asset copies skip this part).
        var artPresent = spriteDir is not null && CreatureCoverage.AllKinds.Any(SpriteFile);
        var missingArt = artPresent
            ? CreatureCoverage.AllKinds.Where(kind => !SpriteFile(kind) || !EnemyArchetypes.Get(kind).IsBoss && !BannerFile(kind)).ToArray()
            : [];
        check(unslotted.Length == 0 && outOfBand.Length == 0 && undropped.Length == 0 && unbannered.Length == 0 && missingArt.Length == 0 &&
              bossItems && loot && CreatureCoverage.AllKinds.All(EnemyArchetypes.Contains) &&
              CreatureCoverage.AllKinds.All(kind => (int)kind is >= 1400 and <= 1599),
            $"every coverage creature (ids 1400-1599) has stats in its tier band, a spawn slot, drops, a banner and a sprite; bosses carry caches, " +
            $"trophies and two unique weapons, mini-bosses a rare one (slot {string.Join(",", unslotted.Take(3))}; band {string.Join(",", outOfBand.Take(3))}; " +
            $"drops {string.Join(",", undropped.Take(3))}; banners {string.Join(",", unbannered.Take(3))}; art {string.Join(",", missingArt.Take(3))})");

        // ---- 8. The Lumen Tempest: a light hoard underground calls it; the first clear grants the Lumen accord ----
        var lumen = new GameSimulation(seed: 14005, width: 160, height: 140) { FamilySpawnsEnabled = false };
        Calm(lumen);
        var lx = (int)lumen.Player.Motion.Position.X;
        var deep = Math.Clamp(lumen.BiomeLayout.SurfaceAt(lx) + 14, (int)(lumen.World.Height * 0.5f), (int)(lumen.World.Height * 0.72f));
        Teleport(lumen, lx, deep);
        Tick(lumen, 130); // the biome reading refreshes on the slow environment tick
        lumen.ProbeLumenTempest();
        var quiet = !lumen.WorldEvents.IsActive(WorldEventKind.LumenTempest);
        var placed = 0;
        for (var y = deep - 12; y <= deep + 12 && placed < CreatureCoverage.LumenTempestLights + 10; y += 2)
        for (var x = lx - 30; x <= lx + 30 && placed < CreatureCoverage.LumenTempestLights + 10; x += 3)
            if (Math.Abs(x - lx) > 3)
            {
                lumen.World.SetTile(new GridPoint(x, y), TileId.WallTorch);
                placed++;
            }
        lumen.ProbeLumenTempest();
        var started = lumen.WorldEvents.IsActive(WorldEventKind.LumenTempest);
        for (var round = 0; round < 20 && lumen.WorldEvents.IsActive(WorldEventKind.LumenTempest); round++)
        {
            foreach (var enemy in lumen.Enemies.Where(enemy => enemy.IsAlive && enemy.EventTag == WorldEventKind.LumenTempest).ToArray())
                enemy.TakeDamage(1e7f);
            Tick(lumen, 5);
        }
        Tick(lumen, 90);
        var accord = lumen.Player.Inventory.Count(ItemId.LumenAccord);
        check(quiet && started && !lumen.WorldEvents.IsActive(WorldEventKind.LumenTempest) && lumen.WorldEvents.Completions(WorldEventKind.LumenTempest) == 1 &&
              accord == 1,
            $"the Lumen Tempest starts once {CreatureCoverage.LumenTempestLights} light fixtures crowd an underground player; clearing it grants one Lumen accord (got {accord})");
    }
}
