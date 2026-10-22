using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// World systems, town NPC and inventory parity probes (docs/MECHANICS_PARITY.md section D, results in
/// docs/parity/D_world.md). Each probe checks an observable behaviour on a small world; the tuning is Rimtera's own.
/// </summary>
public static class ParityWorldTests
{
    private static GameSimulation NewGame(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 80, height: 48) { FamilySpawnsEnabled = false };
        Calm(game);
        return game;
    }

    /// <summary>Keeps long clock runs safe: no hostiles, full health.</summary>
    private static void Calm(GameSimulation game)
    {
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        game.Player.Heal(1e6f);
    }

    private static void Step(GameSimulation game, float delta = 0.25f, PlayerAction action = PlayerAction.None)
    {
        game.Tick(new GameInput(new MovementInput(0f), action), delta);
        Calm(game);
    }

    /// <summary>Ticks until <paramref name="until"/> holds (or a safety cap); returns real seconds spent.</summary>
    private static float RunUntil(GameSimulation game, Func<bool> until, float delta = 0.25f, int cap = 6000)
    {
        var seconds = 0f;
        for (var i = 0; i < cap && !until(); i++)
        {
            Step(game, delta);
            seconds += delta;
        }
        return seconds;
    }

    private enum Entrance { Door, Platform, None }

    /// <summary>A 12×8-interior plated room (walls, light, table, chair, entrance on the left) whose inside cell is returned.</summary>
    private static GridPoint BuildRoom(GameSimulation game, int x0, int top = 2, int width = 14, int height = 10,
        Entrance entrance = Entrance.Door, bool light = true, bool table = true, bool chair = true, bool walls = true)
    {
        var right = x0 + width - 1;
        var bottom = top + height - 1;
        for (var y = top; y <= bottom; y++)
        for (var x = x0; x <= right; x++)
        {
            var shell = y == top || y == bottom || x == x0 || x == right;
            game.World.SetTile(new GridPoint(x, y), shell ? TileId.Plate : TileId.Air);
            if (!shell && walls)
                game.World.SetWall(new GridPoint(x, y), WallIds.PlatingGrey);
        }
        var opening = entrance switch { Entrance.Door => TileId.PlankDoor, Entrance.Platform => TileId.PlankPlatform, _ => TileId.Plate };
        if (entrance == Entrance.Platform)
            game.World.SetTile(new GridPoint(x0 + 2, top), opening); // a platform hatch in the ceiling
        else
            for (var y = bottom - 3; y <= bottom - 1; y++)
                game.World.SetTile(new GridPoint(x0, y), opening);
        if (light)
            game.World.SetTile(new GridPoint(x0 + 3, bottom - 1), TileId.WallTorch);
        if (table)
            game.World.SetTile(new GridPoint(x0 + 5, bottom - 1), TileId.PlankTable);
        if (chair)
            game.World.SetTile(new GridPoint(x0 + 6, bottom - 1), TileId.PlankChair);
        game.Regions.ForceFullRebuild();
        return new GridPoint(x0 + width / 2, top + height / 2 - 1);
    }

    public static void Run(Action<bool, string> check)
    {
        DayNight(check);
        TownNpcArrivals(check);
        SoloStartAndCrew(check);
        Housing(check);
        Happiness(check);
        InventoryStacking(check);
        Crafting(check);
        Events(check);
        Weather(check);
        Fishing(check);
        Saving(check);
    }

    // ---- D1: day/night, moon phase, blood-moon-like event, sleeping ----
    private static void DayNight(Action<bool, string> check)
    {
        var game = NewGame(9401);
        float day = 0f, night = 0f;
        var phases = new List<int>();
        var phaseSteady = true;
        var lastNightPhase = -1;
        var wasNight = game.IsNight;
        for (var i = 0; i < 8000 && game.Day < 4; i++)
        {
            Step(game);
            if (game.IsNight)
            {
                night += 0.25f;
                if (!wasNight)
                    phases.Add(lastNightPhase = game.MoonPhase);
                else if (game.MoonPhase != lastNightPhase)
                    phaseSteady = false;
            }
            else
                day += 0.25f;
            wasNight = game.IsNight;
        }
        var advances = phases.Zip(phases.Skip(1), (a, b) => (b - a + GameSimulation.MoonPhaseCount) % GameSimulation.MoonPhaseCount).ToArray();
        check(day > night * 1.3f && night > 0f && phases.Count >= 2 && advances.All(step => step == 1) && phaseSteady,
            $"D1 a full cycle has a longer day than night ({day:0}s vs {night:0}s real) and the moon phase advances once per night ({string.Join(",", phases)})");

        // Rust Moon: a small natural chance rolled only at dusk, once eligible.
        var spec = game.WorldEvents.Spec(WorldEventKind.RustMoon);
        var hits = Enumerable.Range(1, 400).Count(d => game.WorldEvents.NaturalRoll(d, WorldEventMoment.Dusk, WorldEventKind.RustMoon) < spec.Chance);
        var seed = Enumerable.Range(1, 500).First(s =>
            new WorldEventSystem(s ^ 0x3E7E47).NaturalRoll(1, WorldEventMoment.Dusk, WorldEventKind.RustMoon) < spec.Chance);
        var moonGame = new GameSimulation(seed: seed, width: 80, height: 48) { FamilySpawnsEnabled = false };
        moonGame.Flags.Set("boss.stone_sentinel.down");
        moonGame.WorldEvents.Advance(0f, WorldEventMoment.Dawn, moonGame);
        var notAtDawn = !moonGame.WorldEvents.IsActive(WorldEventKind.RustMoon);
        moonGame.WorldEvents.Advance(0f, WorldEventMoment.Dusk, moonGame);
        check(spec.Moment == WorldEventMoment.Dusk && spec.Chance is > 0f and < 0.25f && hits is > 15 and < 100 && notAtDawn &&
              moonGame.WorldEvents.IsActive(WorldEventKind.RustMoon),
            $"D1 the Rust Moon (blood-moon-like) has a small chance ({hits}/400 nights) rolled at nightfall only");

        // Sleeping in a bed at night speeds time up.
        var sleeper = NewGame(9402);
        RunUntil(sleeper, () => sleeper.IsNight && sleeper.ClockHour is > 19f and < 23f);
        var cell = RegionMap.CellAt(sleeper.Player.Motion.Position + sleeper.Player.Motion.Size / 2f);
        sleeper.World.SetTile(new GridPoint(cell.X + 1, cell.Y), TileId.BunkBed);
        var slept = sleeper.TrySleep();
        var before = sleeper.ElapsedGameHours;
        Step(sleeper);
        var asleepHours = sleeper.ElapsedGameHours - before;
        sleeper.WakeUp();
        before = sleeper.ElapsedGameHours;
        Step(sleeper);
        var awakeHours = sleeper.ElapsedGameHours - before;
        check(slept && asleepHours > awakeHours * 3f,
            $"D1 sleeping in a bed at night makes time pass faster ({asleepHours:0.00} vs {awakeHours:0.00} h per second)");
    }

    // ---- Terraria's opening: just you and the guide; crew arrive into housing; the storyteller waits ----
    private static void SoloStartAndCrew(Action<bool, string> check)
    {
        var fresh = new GameSimulation(seed: 9420, width: 80, height: 48);
        var nearHologram = fresh.Guide.Position + new Vector2(0.5f, 0.5f);
        check(fresh.Colonists.Count == 0 && fresh.TownNpcs.All(npc => npc.IsBound) && !fresh.GuideMovedIn &&
              fresh.IsNearGuide(nearHologram) && fresh.LastMessage.Contains("touches down", StringComparison.Ordinal),
            "a new world starts with just you and the station AI's hologram, as Terraria starts with you and the Guide");

        var quiet = NewGame(9421);
        RunUntil(quiet, () => quiet.ElapsedGameHours >= 60f, cap: 400000);
        var raid = new RaidIncident();
        var context = new StoryContext(Day: 9, ClockHour: 12f, Biome: BiomeZone.Grassland, ColonistCount: 0, DefeatedBosses: 0,
            ColonyWealth: 0f, BossActive: false, RadiatedNearby: false, PlayerHealthFraction: 1f);
        check(quiet.IncidentLog.Count == 0 && !raid.CanFire(context, quiet.Storyteller) && raid.CanFire(context with { DefeatedBosses = 1 }, quiet.Storyteller) &&
              !new SolarFlareIncident().CanFire(context with { Day = 3 }, quiet.Storyteller),
            "the storyteller stays quiet for the first three days, raids wait for the first boss, weather waits for day four");

        var colony = NewGame(9422);
        BuildRoom(colony, 4, width: 12);
        var secondHouse = BuildRoom(colony, 22, width: 12);
        BuildRoom(colony, 40, width: 12);
        colony.CheckTownArrivals(); // the station AI (the guide) moves in
        var guideHome = colony.TownNpcs.FirstOrDefault(npc => npc.Kind == TownNpcKind.StationAi)?.HomeCell;
        var guideFollows = colony.GuideMovedIn && guideHome is { } home &&
                           Vector2.Distance(colony.GuidePosition, new Vector2(home.X + 0.5f, home.Y + 0.5f)) < 3f &&
                           colony.IsNearGuide(new Vector2(home.X + 0.5f, home.Y)) && !colony.IsNearGuide(colony.Guide.Position + new Vector2(60f, 0f));
        var crewBefore = colony.Colonists.Count;
        // A second housed resident makes room for one crew member. Other residents it draws (merchant, consul, gels)
        // compete for the free houses, so build a second storey of them; crew take one once the newcomers are housed.
        var second = colony.AddTownNpc(TownNpcKind.Quartermaster, colony.StandingAt(secondHouse), bound: false);
        second.HomeCell = secondHouse;
        foreach (var x0 in new[] { 4, 22, 40, 58 })
            BuildRoom(colony, x0, top: 16, width: 12);
        for (var i = 0; i < 24 && colony.Colonists.Count == 0; i++)
            colony.CheckTownArrivals();
        var recruit = colony.Colonists.FirstOrDefault();
        var feetOnFloor = recruit is not null && colony.Regions.GetRegion(RegionMap.CellAt(recruit.Motion.Position + recruit.Motion.Size / 2f)) is not null &&
                          !PawnMovementSystem.OverlapsSolid(recruit.Motion.Position, recruit.Motion.Size, colony.World, colony.Tiles);
        check(guideFollows && crewBefore == 0 && colony.Colonists.Count == 1 && colony.Colonists.Count <= colony.CrewCapacity && feetOnFloor,
            "the guide moves into the first house and is found there; crew arrive like town NPCs, into a free house, one per two housed residents");
    }

    // ---- D2: town NPCs move in (day only, one at a time), die and come back ----
    private static void TownNpcArrivals(Action<bool, string> check)
    {
        var day = NewGame(9410);
        BuildRoom(day, 6);
        BuildRoom(day, 30);
        day.Player.Inventory.Add(ItemId.Credits, 60);
        day.CheckTownArrivals();
        var afterFirst = day.TownNpcs.Count(npc => !npc.IsBound);
        day.CheckTownArrivals();
        var afterSecond = day.TownNpcs.Count(npc => !npc.IsBound && npc.HomeCell is not null);
        var homes = day.TownNpcs.Where(npc => npc.HomeCell is not null).Select(npc => day.Regions.GetRegion(npc.HomeCell!.Value)).ToArray();
        check(afterFirst == 1 && afterSecond == 2 && homes.Distinct().Count() == 2 &&
              day.TownNpcs.Any(npc => npc.Kind == TownNpcKind.Quartermaster),
            $"D2 unlocked NPCs move into free valid houses one per check ({afterFirst} then {afterSecond}), each in its own room");

        // Without its condition an NPC never arrives, even with free housing.
        var locked = NewGame(9411);
        BuildRoom(locked, 6);
        BuildRoom(locked, 30);
        locked.CheckTownArrivals();
        locked.CheckTownArrivals();
        check(locked.TownNpcs.All(npc => npc.Kind != TownNpcKind.Quartermaster) && locked.TownNpcs.Count(npc => !npc.IsBound) == 1,
            "D2 each NPC needs its own unlock condition (no credits, no quartermaster)");

        // Arrivals wait for daylight.
        var night = NewGame(9412);
        RunUntil(night, () => night.IsNight && night.ClockHour > 19f);
        BuildRoom(night, 6);
        night.CheckTownArrivals();
        var nightCount = night.TownNpcs.Count;
        RunUntil(night, () => !night.IsNight);
        RunUntil(night, () => night.TownNpcs.Count > 0, cap: 400);
        check(nightCount == 0 && night.TownNpcs.Count == 1 && !night.IsNight,
            "D2 houses are checked during the day only: nobody moves in at night, someone does after dawn");

        // A slain resident returns after a delay if a valid house is free.
        var resident = day.TownNpcs.First(npc => npc.Kind == TownNpcKind.Quartermaster);
        var hurtBy = day.SpawnCreature(EnemyKind.EventHusk, resident.Motion.Position);
        var hpBefore = resident.HitPoints;
        Step(day, 1f / 60f);
        var contactHurt = resident.HitPoints < hpBefore || !resident.IsAlive;
        hurtBy.TakeDamage(1e6f);
        var diedAt = day.ElapsedGameHours;
        var died = day.HurtTownNpc(resident, 1e6f);
        var goneNow = day.TownNpcs.All(npc => npc.Kind != TownNpcKind.Quartermaster);
        day.CheckTownArrivals();
        var notYet = day.TownNpcs.All(npc => npc.Kind != TownNpcKind.Quartermaster);
        var respawnHour = day.TownNpcRespawnHour(TownNpcKind.Quartermaster);
        RunUntil(day, () => day.TownNpcs.Any(npc => npc.Kind == TownNpcKind.Quartermaster), cap: 8000);
        var back = day.TownNpcs.FirstOrDefault(npc => npc.Kind == TownNpcKind.Quartermaster);
        check(contactHurt && died && goneNow && notYet && back is { HomeCell: not null } && day.ElapsedGameHours >= respawnHour &&
              day.ElapsedGameHours >= diedAt + GameSimulation.TownRespawnHours - 1f,
            $"D2 hostiles hurt residents; a slain resident is gone, waits out its respawn delay, then moves back into a valid house");
    }

    // ---- D3: housing reasons and room claims ----
    private static void Housing(Action<bool, string> check)
    {
        string Reason(Func<GameSimulation, GridPoint> build)
        {
            var game = NewGame(9420);
            return game.CheckHousing(build(game)).Reason;
        }
        var ok = Reason(g => BuildRoom(g, 6));
        var platform = NewGame(9420);
        var hatch = platform.CheckHousing(BuildRoom(platform, 6, entrance: Entrance.Platform));
        var noEntrance = Reason(g => BuildRoom(g, 6, entrance: Entrance.None));
        var dark = Reason(g => BuildRoom(g, 6, light: false));
        var bare = Reason(g => BuildRoom(g, 6, walls: false));
        var noTable = Reason(g => BuildRoom(g, 6, table: false));
        var noChair = Reason(g => BuildRoom(g, 6, chair: false));
        var small = Reason(g => BuildRoom(g, 6, width: 9, height: 7));
        var large = Reason(g => BuildRoom(g, 4, width: 34, height: 20));
        check(ok.Contains("suitable") && hatch.Valid && noEntrance.Contains("door") && dark.Contains("light") && bare.Contains("walls") &&
              noTable.Contains("table") && noChair.Contains("chair") && small.Contains("too small") && large.Contains("too large"),
            $"D3 housing reports why a room fails and a platform hatch counts as an entrance ({ok} | {hatch.Reason} | {noEntrance} | {dark} | {bare} | {noTable} | {noChair} | {small} | {large})");

        var shared = NewGame(9421);
        var inside = BuildRoom(shared, 6);
        shared.CheckTownArrivals();
        var claimed = shared.CheckHousing(inside);
        shared.Player.Inventory.Add(ItemId.Credits, 60);
        shared.CheckTownArrivals();
        check(shared.TownNpcs.Count(npc => npc.HomeCell is not null) == 1 && !claimed.Valid && claimed.Reason.Contains("claimed"),
            "D3 a resident claims its room and a second NPC cannot share it");
    }

    // ---- D4: happiness changes prices ----
    private static void Happiness(Action<bool, string> check)
    {
        var game = NewGame(9430);
        BuildRoom(game, 6);
        BuildRoom(game, 30);
        game.Player.Inventory.Add(ItemId.Credits, 60);
        game.CheckTownArrivals();
        game.CheckTownArrivals();
        var quartermaster = game.TownNpcs.First(npc => npc.Kind == TownNpcKind.Quartermaster);
        var alone = game.GetPriceMultiplier(quartermaster);
        for (var i = 0; i < 5; i++)
            game.AddTownNpc(TownNpcKind.Cook, quartermaster.Motion.Position + new Vector2(i * 0.5f, 0f), bound: false);
        var crowded = game.GetPriceMultiplier(quartermaster);
        check(crowded != alone && crowded is >= 0.75f and <= 1.5f,
            $"D4 neighbours and crowding change an NPC's happiness and so its prices ({alone:0.00} -> {crowded:0.00})");
    }

    // ---- D5: inventory pickup order, stack maximums, credits ----
    private static void InventoryStacking(Action<bool, string> check)
    {
        var game = NewGame(9440);
        var inventory = new Inventory(game.Items, 50);
        inventory.PlaceIntoSlot(20, new ItemStack(ItemId.RockBlock, 5));
        inventory.Add(ItemId.RockBlock, 10);
        var ontoExisting = inventory.Slots[20].Count == 15 && inventory.Slots[0].IsEmpty;
        inventory.Add(ItemId.WoodBlock, 3);
        var hotbarFirst = inventory.Slots[0].ItemId == ItemId.WoodBlock;
        for (var slot = 1; slot < Inventory.HotbarSlots; slot++)
            inventory.PlaceIntoSlot(slot, new ItemStack(ItemId.WallTorch, 1));
        inventory.Add(ItemId.Berry, 2);
        var thenMain = inventory.FindSlot(ItemId.Berry) == Inventory.HotbarSlots;

        var max = game.Items[ItemId.RockBlock].MaxStack;
        var bag = new Inventory(game.Items, 10);
        bag.Add(ItemId.RockBlock, max * 2 + 5);
        var counts = bag.Slots.Where(stack => !stack.IsEmpty).Select(stack => stack.Count).ToArray();
        var perKind = game.Items[ItemId.CopperPickaxe].MaxStack == 1 && max > 1;
        bag.Add(ItemId.Credits, 30);
        bag.Add(ItemId.Credits, 45);
        var creditStacks = bag.Slots.Count(stack => !stack.IsEmpty && stack.ItemId == ItemId.Credits);
        check(ontoExisting && hotbarFirst && thenMain,
            "D5 pickups top up existing stacks first, then fill the hotbar, then the main inventory");
        check(counts.SequenceEqual(new[] { max, max, 5 }) && perKind && creditStacks == 1 && bag.Count(ItemId.Credits) == 75,
            $"D5 stacks cap at a per-kind maximum (rock {max}, tools 1) and credits combine into one stack");
    }

    // ---- D6: crafting needs every station in range and the materials ----
    private static void Crafting(Action<bool, string> check)
    {
        var game = NewGame(9450);
        var crafting = new CraftingSystem(game.Items);
        crafting.Register(new RecipeDefinition("parity_two_station", ItemId.CopperBar, 2,
            [IngredientRequirement.Exact(ItemId.CopperOre, 3)],
            new HashSet<CraftingStationId> { CraftingStationId.Workbench, CraftingStationId.Furnace }));
        var at = new Vector2(40.5f, 6.5f);
        for (var y = 3; y <= 9; y++)
        for (var x = 30; x <= 52; x++)
            game.World.SetTile(new GridPoint(x, y), TileId.Air);
        var env = new CraftingEnvironment(at, game.World, game.Tiles);
        var inventory = new Inventory(game.Items, 20);
        inventory.Add(ItemId.CopperOre, 6);
        inventory.Add(ItemId.CopperBar, 1);
        game.World.SetTile(new GridPoint(42, 7), TileId.Workbench);
        var oneStation = crafting.TryCraft("parity_two_station", inventory, env);
        game.World.SetTile(new GridPoint(38, 7), TileId.Furnace);
        var both = crafting.TryCraft("parity_two_station", inventory, env);
        var farEnv = env with { PlayerPosition = new Vector2(50.5f, 6.5f) };
        var far = crafting.TryCraft("parity_two_station", inventory, farEnv);
        var stackedOutput = inventory.Count(ItemId.CopperBar) == 3 && inventory.Slots.Count(stack => stack.ItemId == ItemId.CopperBar && !stack.IsEmpty) == 1;
        inventory.TryConsume(ItemId.CopperOre, inventory.Count(ItemId.CopperOre) - 2);
        var short_ = crafting.TryCraft("parity_two_station", inventory, env);
        check(!oneStation.Success && both.Success && !far.Success && stackedOutput && !short_.Success,
            "D6 a recipe crafts only with every required station in range and all materials; output stacks into the inventory");
    }

    // ---- D7: events start, count kills, end on waves/timer and block sleep ----
    private static void Events(Action<bool, string> check)
    {
        var game = NewGame(9460);
        game.Flags.Increment("hearts.broken");
        var started = game.WorldEvents.TryStart(WorldEventKind.ScavengerRaid, game, forced: true, out _);
        var waves = new List<int>();
        for (var round = 0; round < 12 && game.WorldEvents.IsActive(WorldEventKind.ScavengerRaid); round++)
        {
            waves.Add(game.EventProgress.Single(progress => progress.Kind == WorldEventKind.ScavengerRaid).Wave);
            foreach (var enemy in game.Enemies.Where(enemy => enemy.EventTag == WorldEventKind.ScavengerRaid).ToArray())
                enemy.TakeDamage(enemy.MaximumHitPoints);
            game.Tick(new GameInput(new MovementInput(0f)));
        }
        check(started && waves.Distinct().Count() >= 3 && !game.WorldEvents.IsActive(WorldEventKind.ScavengerRaid),
            $"D7 an invasion advances wave by wave on kills and ends when its waves are cleared ({string.Join(",", waves.Distinct())})");

        var timed = NewGame(9461);
        timed.WorldEvents.TryStart(WorldEventKind.AuroraNight, timed, forced: true, out _);
        var hours = timed.WorldEvents.Spec(WorldEventKind.AuroraNight).DurationHours;
        timed.WorldEvents.Advance(hours + 0.1f, null, timed);
        check(!timed.WorldEvents.IsActive(WorldEventKind.AuroraNight), "D7 a timed event ends when its timer runs out");

        var sleeper = NewGame(9462);
        RunUntil(sleeper, () => sleeper.IsNight && sleeper.ClockHour is > 19f and < 23f);
        var cell = RegionMap.CellAt(sleeper.Player.Motion.Position + sleeper.Player.Motion.Size / 2f);
        sleeper.World.SetTile(new GridPoint(cell.X + 1, cell.Y), TileId.BunkBed);
        sleeper.Flags.Set("boss.stone_sentinel.down");
        var asleep = sleeper.TrySleep();
        sleeper.WorldEvents.TryStart(WorldEventKind.RustMoon, sleeper, forced: true, out _);
        Step(sleeper, 0.1f);
        var woken = !sleeper.IsSleeping;
        var refused = !sleeper.TrySleep();
        check(asleep && woken && refused && sleeper.EventBlocksSleep,
            "D7 you cannot sleep through an event: a starting Rust Moon wakes you and beds refuse you until it ends");
    }

    // ---- D8: weather ----
    private static void Weather(Action<bool, string> check)
    {
        var weather = new WeatherSystem(9470);
        int rainy = 0, dry = 0, storms = 0, windy = 0, stormWithoutHeavyRain = 0;
        for (var day = 1; day <= 60; day++)
        for (var slot = 0; slot < WeatherSystem.RollHours.Length; slot++)
        {
            weather.RollTargets(day, slot, BiomeZone.Grassland, false);
            var state = weather.State;
            if (state.TargetRain > 0f) rainy++; else dry++;
            if (state.Storm) storms++;
            if (state.Storm && state.TargetRain <= 0.75f) stormWithoutHeavyRain++;
            if (MathF.Abs(state.TargetWind) > 0.5f) windy++;
        }
        check(rainy > 10 && dry > 10 && storms > 0 && stormWithoutHeavyRain == 0 && windy > 10,
            $"D8 rain starts and stops at random ({rainy} wet / {dry} dry rolls), storms come with heavy rain, wind varies");

        var game = NewGame(9471);
        RunUntil(game, () => game.ClockHour is >= 11f and < 13f);
        var clear = game.SkyLightLevel;
        game.WeatherControl.Force(rain: 0.9f, storm: true, immediate: true);
        var rain = game.SkyLightLevel;
        check(rain < clear * 0.85f && clear > 0.9f,
            $"D8 rain and storms reduce surface light ({clear:0.00} -> {rain:0.00})");
    }

    // ---- D9: fishing ----
    private static void Fishing(Action<bool, string> check)
    {
        var game = NewGame(9480);
        var baits = game.Items.All.Where(item => item.BaitPower > 0).OrderBy(item => item.BaitPower).ToArray();
        var weak = baits.First();
        var strong = baits.Last();
        var chances = GameSimulation.BaitConsumeChance(weak.BaitPower) > GameSimulation.BaitConsumeChance(strong.BaitPower) &&
                      GameSimulation.BaitConsumeChance(1000) > 0f;
        var rod = game.Items[ItemId.FishingRod];
        foreach (var slot in Enumerable.Range(0, game.Player.Inventory.SlotCount))
            if (!game.Player.Inventory.Slots[slot].IsEmpty && game.Items[game.Player.Inventory.Slots[slot].ItemId].BaitPower > 0)
                game.Player.Inventory.TakeFromSlot(slot);
        var bare = game.GetFishingPower(rod);
        game.Player.Inventory.Add(weak.Id, 1);
        var baited = game.GetFishingPower(rod);
        check(chances && baited == bare + weak.BaitPower && bare >= rod.FishingPower,
            $"D9 bait is consumed less often the stronger it is; fishing power = rod + bait + bonuses ({bare} -> {baited})");

        var context = new FishingContext(LiquidKind.Water, BiomeZone.Grassland, FishingDepth.Surface, 100, 60f, false, 0f);
        HashSet<ItemId> Catches(FishingContext ctx)
        {
            var random = new Random(5);
            var set = new HashSet<ItemId>();
            for (var i = 0; i < 400; i++)
                set.Add(FishingTables.Pick(ctx, FishingRarity.Common, random));
            return set;
        }
        var water = Catches(context);
        var acid = Catches(context with { Liquid = LiquidKind.Acid });
        var deep = Catches(context with { Depth = FishingDepth.Caverns });
        var frozen = Catches(context with { Zone = BiomeZone.FrozenWasteland });
        check(!water.SetEquals(acid) && (!water.SetEquals(deep) || !water.SetEquals(frozen)),
            "D9 the catch depends on liquid, depth and biome");

        var days = Enumerable.Range(1, 20).Select(game.QuestFishForDay).Distinct().Count();
        check(days > 1 && game.UnlockedQuestFish.Contains(game.TodaysQuestFish),
            $"D9 a daily quest fish rotates by day ({days} different fish over 20 days)");
    }

    // ---- D10: saving keeps world clock, NPCs and events ----
    private static void Saving(Action<bool, string> check)
    {
        var game = NewGame(9490);
        BuildRoom(game, 6);
        game.CheckTownArrivals();
        Step(game);
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var json = System.Text.Json.JsonSerializer.Serialize(game.CaptureSnapshot(), options);
        var loaded = GameSimulation.Restore(System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>(json, options)!);
        check(loaded.Day == game.Day && MathF.Abs(loaded.ClockHour - game.ClockHour) < 0.01f && loaded.MoonPhase == game.MoonPhase &&
              loaded.TownNpcs.Count == game.TownNpcs.Count,
            "D10 a save round-trip keeps the clock, the moon phase and the residents");
    }
}
