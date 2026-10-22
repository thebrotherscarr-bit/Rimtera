using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Self-tests for the easy-mode → hardmode progression (docs/HARDMODE_DIFF.md): the core gate boss and the Fracture's
/// V scars, the core breaker and the altar ore cycle, hardmode rosters, essences, the hard trio's summons and night
/// arrivals, the post-hard chain flags, and hardmode stat scaling with the boss damage ceiling.
/// </summary>
public static class HardmodeSelfTests
{
    private static GameSimulation NewGame(int seed, WorldBlight? blight = null, int width = 160, int height = 72)
    {
        var game = new GameSimulation(seed: seed, width: width, height: height, blight: blight) { FamilySpawnsEnabled = false };
        Calm(game);
        return game;
    }

    private static void Calm(GameSimulation game)
    {
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e7f);
        game.Player.Heal(1e6f);
    }

    private static void Tick(GameSimulation game, int ticks = 1, PlayerAction action = PlayerAction.None, GridPoint? target = null)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f), i == 0 ? action : PlayerAction.None, target));
    }

    private static void Use(GameSimulation game, ItemId item)
    {
        game.Player.ActionCooldownTicks = 0;
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
        Tick(game, 1, PlayerAction.UseSelected, new GridPoint(1, 1));
    }

    /// <summary>Kills every living boss (and anything else hostile) and lets the defeat logic run.</summary>
    private static void KillAll(GameSimulation game, Func<EnemyAgent, bool>? filter = null)
    {
        foreach (var enemy in game.Enemies.Where(enemy => enemy.IsAlive && (filter?.Invoke(enemy) ?? true)).ToArray())
        {
            enemy.Shielded = false;
            enemy.TakeDamage(1e7f);
        }
        Tick(game);
        game.Player.Heal(1e6f);
    }

    private static void Defeat(GameSimulation game, EnemyKind kind)
    {
        var enemy = game.SpawnCreature(kind, game.Player.Motion.Position + new Vector2(14f, -6f));
        enemy.TakeDamage(1e7f);
        Tick(game);
        game.Player.Heal(1e6f);
    }

    /// <summary>Clears an air pocket around <paramref name="feet"/> with a floor under it and stands the player there.</summary>
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

    private static int Count(GameSimulation game, Func<TileId, bool> match)
    {
        var count = 0;
        foreach (var cell in game.World.Cells)
            if (match(cell.TileId))
                count++;
        return count;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- 1. Core gate boss → Fracture → V scars and prism spread ----
        var core = NewGame(9101, WorldBlight.Radiated);
        core.Player.Inventory.Add(ItemId.CoreIgniter, 2);
        Use(core, ItemId.CoreIgniter);
        var refusedEarly = !core.Enemies.Any(enemy => enemy.IsBoss) && core.Player.Inventory.Count(ItemId.CoreIgniter) == 2;
        Defeat(core, GameSimulation.LastEasyBoss);
        var oldTriggerGone = !core.FractureActive;
        Use(core, ItemId.CoreIgniter);
        var refusedOnSurface = !core.Enemies.Any(enemy => enemy.IsBoss) && core.RecentMessages.Any(message => message.Contains("Irradiated Core"));
        Teleport(core, 80, (int)(core.World.Height * 0.88f));
        Use(core, ItemId.CoreIgniter);
        var tyrant = core.Enemies.FirstOrDefault(enemy => enemy.Kind == GameSimulation.CoreGateBoss);
        var summoned = tyrant is not null && core.Player.Inventory.Count(ItemId.CoreIgniter) == 1;
        KillAll(core);
        var prismCells = Count(core, tile => tile is TileId.PrismaticGrass or TileId.PearlStone or TileId.PearlSand);
        var leftBlight = Enumerable.Range(0, core.World.Height).Sum(y => Enumerable.Range(0, core.World.Width / 2)
            .Count(x => BlightRules.IsBlightTile(core.World.GetCell(x, y).TileId)));
        check(refusedEarly && oldTriggerGone && refusedOnSurface && summoned && core.FractureActive &&
              core.FractureCellsConverted > 20 && core.FracturePrismCells > 20 && prismCells >= core.FracturePrismCells &&
              leftBlight > 20 && core.Player.Inventory.Count(ItemId.CoreBreaker) == 1 &&
              core.Flags.IsSet("boss.fission_tyrant.down") && core.HardmodeStage == "fractured",
            "the core igniter needs the last easy boss and the Irradiated Core; the Fission Tyrant's death fractures the world " +
            "with a blight scar and a mirrored prism scar and drops the core breaker");

        // Prism spreads after the Fracture, underground too.
        var deepPrism = Enumerable.Range((int)(core.World.Height * 0.5f), core.World.Height / 3)
            .SelectMany(y => Enumerable.Range(1, core.World.Width - 2).Select(x => new GridPoint(x, y)))
            .FirstOrDefault(point => core.World.GetCell(point).TileId == TileId.PearlStone, new GridPoint(-1, -1));
        var conversionsBefore = core.Ticker.CrystalConversions;
        if (deepPrism.X >= 0)
        {
            for (var dx = -2; dx <= 2; dx++)
            for (var dy = -2; dy <= 2; dy++)
                if ((dx, dy) != (0, 0) && core.World.Contains(deepPrism.X + dx, deepPrism.Y + dy))
                    core.World.SetTile(new GridPoint(deepPrism.X + dx, deepPrism.Y + dy), TileId.Rock);
            for (var i = 0; i < 300; i++)
                core.Ticker.UpdateCell(deepPrism.X, deepPrism.Y);
        }
        check(deepPrism.X >= 0 && core.Ticker.CrystalConversions > conversionsBefore && core.Ticker.Fractured,
            "after the Fracture the prism biome spreads into underground rock (and competes with the blight)");

        // ---- 2. Altars need the core breaker ----
        var altarGame = NewGame(9102, WorldBlight.Radiated);
        altarGame.TriggerFracture();
        var center = altarGame.Player.Motion.Position + altarGame.Player.Motion.Size / 2f;
        var ax = (int)MathF.Floor(center.X) + 1;
        var ay = (int)MathF.Floor(center.Y) - 1;
        for (var dy = 0; dy < 2; dy++)
        for (var dx = 0; dx < 2; dx++)
            altarGame.World.SetTile(new GridPoint(ax + dx, ay + dy), TileId.RadAltar);
        altarGame.Player.Inventory.Add(ItemId.CopperPickaxe, 1);
        altarGame.Player.Inventory.Add(ItemId.CoreBreaker, 1);
        altarGame.TrySelectInventorySlot(altarGame.Player.Inventory.FindSlot(ItemId.CopperPickaxe));
        for (var i = 0; i < 40; i++)
        {
            altarGame.Player.ActionCooldownTicks = 0;
            Tick(altarGame, 1, PlayerAction.Mine, new GridPoint(ax, ay));
        }
        var copperRefused = altarGame.AltarsSmashed == 0 && altarGame.World.GetCell(ax, ay).TileId == TileId.RadAltar &&
                            altarGame.RecentMessages.Any(message => message.Contains("core breaker"));
        altarGame.TrySelectInventorySlot(altarGame.Player.Inventory.FindSlot(ItemId.CoreBreaker));
        for (var i = 0; i < 400 && altarGame.AltarsSmashed == 0; i++)
        {
            altarGame.Player.ActionCooldownTicks = 0;
            Tick(altarGame, 1, PlayerAction.Mine, new GridPoint(ax, ay));
        }
        check(copperRefused && altarGame.AltarsSmashed == 1 && GameSimulation.CanSmashAltars(altarGame.Items[ItemId.CoreBreaker]) &&
              GameSimulation.CanSmashAltars(altarGame.Items[ItemId.VoidsteelPick]) &&
              !GameSimulation.CanSmashAltars(altarGame.Items[ItemId.UraniumPick]),
            "after the Fracture altars still refuse ordinary drills; the core breaker (or a stronger drill) smashes them");

        // ---- 3. Smashes cycle three ore tiers with less yield, each releasing a core spirit ----
        var cycle = NewGame(9103, WorldBlight.Overgrowth, 320, 140);
        cycle.TriggerFracture();
        var messages = new List<string>();
        var seeds = new List<int>();
        for (var smash = 0; smash < 4; smash++)
        {
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
                cycle.World.SetTile(new GridPoint(40 + smash * 6 + dx, 3 + dy), TileId.RootAltar);
            cycle.ForceBreakTile(new GridPoint(40 + smash * 6, 3));
            messages.Add(string.Join("|", cycle.RecentMessages.TakeLast(4)));
            seeds.Add(cycle.LastAltarOreSeeds.Count);
        }
        check(cycle.AltarsSmashed == 4 && messages[0].Contains("viridium", StringComparison.OrdinalIgnoreCase) &&
              messages[1].Contains("lumenite", StringComparison.OrdinalIgnoreCase) &&
              messages[2].Contains("voidsteel", StringComparison.OrdinalIgnoreCase) &&
              messages[3].Contains("viridium", StringComparison.OrdinalIgnoreCase) &&
              GameSimulation.AltarYieldFactor(4) < GameSimulation.AltarYieldFactor(1) && seeds[3] <= seeds[0] &&
              cycle.Flags.Get("altars.spirits") == 4 && cycle.Enemies.Count(enemy => enemy.Kind == EnemyKind.FissionWisp) >= 4 &&
              cycle.Flags.IsSet("ore.voidsteel"),
            "altar smashes cycle viridium → lumenite → voidsteel with diminishing veins and each releases a fission wisp");

        // ---- 4. Hardmode rosters ----
        var rostersDiffer = BiomeSpawnTables.HardZones.All(zone =>
            BiomeSpawnTables.HardKindsFor(zone).Except(BiomeSpawnTables.KindsFor(zone)).Any() &&
            BiomeSpawnTables.HardKindsFor(zone).Contains(BiomeSpawnTables.Pick(zone, 0.1, night: false, hardmode: true)) &&
            BiomeSpawnTables.Pick(zone, 0.1, night: false, hardmode: false) == BiomeSpawnTables.Pick(zone, 0.1, night: false));
        var mimicsHardOnly = BiomeSpawnTables.HardKindsFor(BiomeZone.Caverns).Contains(EnemyKind.CacheMimic) &&
                             Enum.GetValues<BiomeZone>().All(zone => !BiomeSpawnTables.KindsFor(zone).Contains(EnemyKind.CacheMimic));
        check(rostersDiffer && mimicsHardOnly && BiomeSpawnTables.HardZones.Count() >= 14,
            "after the Fracture every biome rolls from a stronger roster of its own; underground cache mimics only appear in hardmode");

        // Hard tiers of the older events.
        var easyEvent = NewGame(9104);
        var hardEvent = NewGame(9105);
        hardEvent.TriggerFracture();
        var rolls = Enumerable.Range(0, 200).Select(i => i / 200.0).ToArray();
        check(rolls.All(roll => easyEvent.ProbeHardEventCreature(EnemyKind.EventHusk, roll) == EnemyKind.EventHusk) &&
              rolls.Any(roll => GameSimulation.IsHardEventUpgrade(hardEvent.ProbeHardEventCreature(EnemyKind.EventHusk, roll))) &&
              rolls.Any(roll => hardEvent.ProbeHardEventCreature(EnemyKind.EventScavengerGrunt, roll) == EnemyKind.NeoTerraReaver) &&
              hardEvent.WorldEvents.CanStart(WorldEventKind.DarkSun, hardEvent, forced: true, out _),
            "after the Fracture the Rust Moon, raids and the defence protocol field stronger creatures; the Dark Sun eclipse is eligible");

        // ---- 5. Essences drop and craft ----
        var essence = NewGame(9106, WorldBlight.Overgrowth);
        essence.TriggerFracture();
        var deepY = (int)(essence.World.Height * 0.6f);
        for (var y = deepY - 3; y <= deepY; y++)
        for (var x = 100; x <= 106; x++)
            essence.World.SetTile(new GridPoint(x, y), TileId.Air);
        var skyX = 30;
        for (var i = 0; i < 40; i++)
        {
            foreach (var (kind, at) in new[]
                     {
                         (EnemyKind.PrismKnight, new Vector2(101f, deepY - 2f)), (EnemyKind.EventAbomination, new Vector2(103f, deepY - 2f)),
                         (EnemyKind.SkyRay, new Vector2(skyX, essence.BiomeLayout.SurfaceAt(skyX) - 8f))
                     })
                essence.SpawnCreature(kind, at).TakeDamage(1e7f);
            Tick(essence);
            essence.Player.Heal(1e6f);
        }
        var dropped = new[] { "prism_essence", "blight_essence", "flight_essence" }
            .Select(key => essence.Flags.Get($"essence.{key}.dropped")).ToArray();
        var catalog = new ItemCatalog();
        var tiles = new TileCatalog();
        var crafting = new CraftingSystem(catalog);
        var bench = new TileGrid(12, 12);
        bench.SetTile(new GridPoint(5, 6), TileId.AlloyAnvil);
        var pack = new Inventory(catalog);
        pack.Add(ItemId.ViridiumBar, 6);
        pack.Add(ItemId.BlightEssence, 6);
        pack.Add(ItemId.PowerCell, 3);
        var resonator = crafting.TryCraft("x-swarmresonator", pack, new CraftingEnvironment(new Vector2(5f, 6f), bench, tiles));
        bool Needs(ItemId output, ItemId ingredient) =>
            crafting.FindByOutput(output).Any(recipe => recipe.Ingredients.Any(group => group.Alternatives.Contains(ingredient)));
        check(dropped.All(count => count > 0) && essence.Flags.Get("essence.prism_essence.dropped") < 40 &&
              resonator.Success && pack.Count(ItemId.SwarmResonator) == 1 && pack.Count(ItemId.BlightEssence) == 0 &&
              Needs(ItemId.ViridiumWingpack, ItemId.FlightEssence) && Needs(ItemId.RenegadeCredentials, ItemId.PrismEssence) &&
              Needs(ItemId.WarshipTransponder, ItemId.FlightEssence) && Needs(ItemId.VoidHymnal, ItemId.BlightEssence) &&
              Needs(ItemId.VoidsteelScythe, ItemId.PrismEssence) && Needs(ItemId.AlloyAnvil, ItemId.ViridiumBar) &&
              Needs(ItemId.VoidForge, ItemId.LumeniteBar) && crafting.FindByOutput(ItemId.VoidsteelScythe).All(recipe =>
                  recipe.RequiredStations!.Contains(CraftingStationId.VoidForge)),
            $"underground prism and blight creatures and sky flyers drop essences ({string.Join("/", dropped)}); essences craft " +
            "summons, wings and top gear at the hardmode anvil and forge");

        // ---- 6. Hard-tier summons and natural night arrivals ----
        var trio = NewGame(9107);
        trio.Player.Inventory.Add(ItemId.SwarmResonator, 2);
        Use(trio, ItemId.SwarmResonator);
        var trioRefused = !trio.Enemies.Any(enemy => enemy.IsBoss) && !trio.TrySpawnHardBossAtDusk(0.01);
        trio.TriggerFracture();
        Use(trio, ItemId.SwarmResonator);
        var trioSummoned = trio.Enemies.Any(enemy => enemy.Kind == EnemyKind.CyberSwarmLeader && enemy.IsAlive);
        KillAll(trio);
        var essenceFromBoss = trio.Player.Inventory.Count(ItemId.BlightEssence) >= 20;
        var natural = trio.TrySpawnHardBossAtDusk(0.01);
        var arrival = trio.Enemies.FirstOrDefault(enemy => enemy.IsBoss && enemy.IsAlive);
        var warned = trio.RecentMessages.Any(message => message.Contains("Heavy footfalls") || message.Contains("Engines drone"));
        KillAll(trio);
        var rateLimited = !trio.TrySpawnHardBossAtDusk(0.01) && !trio.TrySpawnHardBossAtDusk(0.99);
        check(trioRefused && trioSummoned && essenceFromBoss && natural && warned &&
              arrival is { Kind: EnemyKind.RenegadeOverseer or EnemyKind.WarshipCore } && rateLimited &&
              trio.Flags.Get("hardmode.nightboss.arrivals") == 1,
            "hard-tier summons wait for the Fracture; afterwards an undefeated hard boss can arrive at dusk with a warning, " +
            "rate-limited, and each drops essences");

        // ---- 7. The post-hard chain ----
        var chain = NewGame(9108, WorldBlight.Radiated);
        Defeat(chain, GameSimulation.CoreGateBoss);
        chain.CheckTownArrivals();
        var isotopist = chain.Flags.IsSet("npc.isotopist.unlocked");
        foreach (var kind in GameSimulation.HardTrio)
            Defeat(chain, kind);
        Tick(chain, 40);
        var bulbs = Count(chain, tile => tile == TileId.CanopyBulb);
        var bulb = Enumerable.Range(0, chain.World.Count).Select(index => new GridPoint(index % chain.World.Width, index / chain.World.Width))
            .FirstOrDefault(point => chain.World.GetCell(point).TileId == TileId.CanopyBulb, new GridPoint(-1, -1));
        if (bulb.X >= 0)
            chain.ForceBreakTile(bulb);
        var bulbBoss = chain.Enemies.Any(enemy => enemy.Kind == GameSimulation.BulbBoss && enemy.IsAlive);
        KillAll(chain);
        var key = chain.Player.Inventory.Count(ItemId.VaultKey) == 1;
        chain.Player.Inventory.Add(ItemId.VaultCore, 2);
        Use(chain, ItemId.VaultCore);
        var vaultRefused = !chain.Enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive);
        var playerCell = chain.Player.Motion.Position + chain.Player.Motion.Size / 2f;
        var door = new GridPoint((int)playerCell.X + 2, (int)playerCell.Y);
        chain.World.SetTile(door, TileId.PrecursorVaultDoor);
        chain.Interact(door);
        var opened = chain.Flags.IsSet("vault.opened");
        Use(chain, ItemId.VaultCore);
        var colossus = chain.Enemies.Any(enemy => enemy.Kind == EnemyKind.VaultColossus && enemy.IsAlive);
        KillAll(chain);
        var site = chain.RitualSite;
        Teleport(chain, site.X, chain.BiomeLayout.SurfaceAt(site.X) - 1);
        Tick(chain, 40);
        var cantor = chain.Enemies.Any(enemy => enemy.Kind == GameSimulation.RitualBoss && enemy.IsAlive);
        KillAll(chain, enemy => enemy.IsBoss);
        var obelisks = chain.Enemies.Where(enemy => GameSimulation.Obelisks.Any(entry => entry.Obelisk == enemy.Kind)).ToArray();
        var shielded = obelisks.Length == 4 && obelisks.All(obelisk => obelisk.Shielded && obelisk.TakeDamage(500f) == 0f);
        foreach (var (eventKind, _) in GameSimulation.Obelisks)
        {
            var roster = chain.WorldEvents.Spec(eventKind).Roster;
            for (var i = 0; i < WorldEventSystem.ObeliskShieldKills; i++)
            {
                var creature = chain.SpawnCreature(roster[i % roster.Length].Kind, chain.Player.Motion.Position + new Vector2(12f, -4f));
                creature.EventTag = eventKind;
                creature.TakeDamage(1e7f);
                Tick(chain);
            }
        }
        Tick(chain, 40);
        var shieldsDown = obelisks.All(obelisk => !obelisk.Shielded);
        foreach (var obelisk in obelisks)
            obelisk.TakeDamage(1e7f);
        Tick(chain);
        var countdown = chain.AstralCountdown > 50f && chain.Flags.IsSet("obelisks.down");
        for (var second = 0; second < 61; second++) { Tick(chain, 60); chain.Player.Heal(1e6f); } // ambient spawns must not end the countdown run
        var god = chain.Enemies.Any(enemy => enemy.Kind == EnemyKind.AstroGod);
        chain.CheckTownArrivals();
        check(isotopist && chain.Flags.IsSet("hardmode.trio.down") && bulbs > 0 && bulbBoss && key && vaultRefused && opened &&
              colossus && chain.Flags.IsSet("ritual.ready") && cantor && shielded && shieldsDown && countdown && god &&
              chain.Flags.IsSet("npc.relic_warden.unlocked") && chain.Flags.IsSet("npc.aeronaut.unlocked") &&
              GameSimulation.Obelisks.All(entry => chain.Flags.IsSet(chain.WorldEvents.Spec(entry.Event).FlagName)),
            $"the chain runs trio → canopy bulbs → bulb boss → vault key → vault boss → dungeon ritual → four shielded obelisks → " +
            $"Astro-God countdown (bulbs {bulbs}, stage {chain.HardmodeStage})");

        // The chain's flags persist in saves.
        var saved = GameSimulation.Restore(System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(chain.CaptureSnapshot()))!);
        check(saved.FractureActive && saved.Flags.IsSet("obelisks.down") && saved.Flags.IsSet("vault.opened") &&
              saved.HardmodeStage == chain.HardmodeStage,
            "every hardmode chain step survives a save round-trip");

        // ---- 8. Scaling after the Fracture, with a boss damage ceiling ----
        var scaling = NewGame(9109, width: 80, height: 48);
        var before = scaling.SpawnCreature(EnemyKind.RockGolem, scaling.Player.Motion.Position + new Vector2(14f, -4f));
        Tick(scaling);
        var easyStats = before.MaximumHitPoints == EnemyArchetypes.Get(EnemyKind.RockGolem).HitPoints;
        scaling.TriggerFracture();
        var after = scaling.SpawnCreature(EnemyKind.RockGolem, scaling.Player.Motion.Position + new Vector2(-14f, -4f));
        Tick(scaling);
        var (classicHealth, classicDamage) = GameSimulation.HardmodeScale(WorldDifficulty.Classic);
        var scaled = Math.Abs(after.MaximumHitPoints - 70f * classicHealth) < 0.01f && Math.Abs(after.AttackDamage - 14f * classicDamage) < 0.01f &&
                     before.MaximumHitPoints == 70f;
        var brutal = NewGame(9110, width: 80, height: 48);
        brutal.Difficulty = WorldDifficulty.Brutal;
        brutal.TriggerFracture();
        var brute = brutal.SpawnCreature(EnemyKind.RockGolem, brutal.Player.Motion.Position + new Vector2(14f, -4f));
        var astro = brutal.SpawnCreature(EnemyKind.AstroGod, brutal.Player.Motion.Position + new Vector2(-20f, -10f));
        Tick(brutal);
        check(easyStats && scaled && brute.MaximumHitPoints > after.MaximumHitPoints &&
              GameSimulation.HardmodeScale(WorldDifficulty.Explorer).Health < classicHealth &&
              astro.MaximumHitPoints == EnemyArchetypes.Get(EnemyKind.AstroGod).HitPoints &&
              astro.AttackDamage <= GameSimulation.BossDamageCap &&
              brutal.ProbeCappedBossHit(astro, 500f) == GameSimulation.BossDamageCap && brutal.ProbeCappedBossHit(brute, 500f) == 500f,
            "after the Fracture regular creatures gain the difficulty's hardmode health and damage; bosses keep theirs, capped per hit");
    }
}
