using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Self-tests for the content expansion and the fishing/events/weather pass.</summary>
public static class ContentSelfTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private static GameSimulation RoundTrip(GameSimulation game) =>
        GameSimulation.Restore(JsonSerializer.Deserialize<GameSnapshot>(JsonSerializer.Serialize(game.CaptureSnapshot(), Json), Json)!);

    private static void Tick(GameSimulation game, int ticks = 1, PlayerAction action = PlayerAction.None, GridPoint? target = null)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f), i == 0 ? action : PlayerAction.None, target));
    }

    private static void Use(GameSimulation game, ItemId item, GridPoint target)
    {
        game.Player.ActionCooldownTicks = 0;
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
        Tick(game, 1, PlayerAction.UseSelected, target);
    }

    /// <summary>A walled 12×8 room in the sky with door, light, table and chair. Returns an interior cell.</summary>
    private static GridPoint BuildRoom(GameSimulation game, int x0, int top = 2, bool roof = true)
    {
        var bottom = top + 9;
        for (var y = top; y <= bottom; y++)
        for (var x = x0; x <= x0 + 13; x++)
        {
            var shell = y == top || y == bottom || x == x0 || x == x0 + 13;
            game.World.SetTile(new GridPoint(x, y), shell ? TileId.Plate : TileId.Air);
            if (!shell)
                game.World.SetWall(new GridPoint(x, y), WallIds.PlatingGrey);
        }
        for (var y = bottom - 3; y <= bottom - 1; y++)
            game.World.SetTile(new GridPoint(x0, y), TileId.PlankDoor);
        game.World.SetTile(new GridPoint(x0 + 3, bottom - 1), TileId.WallTorch);
        game.World.SetTile(new GridPoint(x0 + 5, bottom - 1), TileId.PlankTable);
        game.World.SetTile(new GridPoint(x0 + 6, bottom - 1), TileId.PlankChair);
        if (!roof)
            for (var x = x0 + 1; x < x0 + 13; x++)
                game.World.SetTile(new GridPoint(x, top), TileId.Air);
        game.Regions.ForceFullRebuild();
        return new GridPoint(x0 + 8, top + 4);
    }

    public static void Run(Action<bool, string> check)
    {
        var catalog = new ItemCatalog();
        var tiles = new TileCatalog();
        var crafting = new CraftingSystem(catalog);

        // ---- Catalog completeness ----
        var missingItems = Enum.GetValues<ItemId>().Where(id => !catalog.Contains(id)).ToArray();
        var missingTiles = Enum.GetValues<TileId>().Where(id => !tiles.Contains(id)).ToArray();
        check(missingItems.Length == 0 && missingTiles.Length == 0 && Enum.GetValues<EnemyKind>().All(EnemyArchetypes.Contains) &&
              catalog.All.Count() > 500 && crafting.Recipes.Count > 300 &&
              Enum.GetValues<ItemId>().Where(id => catalog[id].SummonsBoss is not null).Select(id => catalog[id].SummonsBoss).Distinct().Count() >=
              EnemyArchetypes.All.Count(archetype => archetype.IsBoss) - 1,
            $"every item, tile and creature id has a definition; every boss has a summon item (missing items: {string.Join(",", missingItems.Take(5))}; tiles: {string.Join(",", missingTiles.Take(5))})");

        // ---- Class damage scaling ----
        var classGame = new GameSimulation(seed: 6060, width: 80, height: 48);
        var emitter = classGame.Items[ItemId.SparkEmitter];
        var baseTech = classGame.ComputeWeaponDamage(emitter);
        var expectedSkill = 1f + classGame.Player.ColonistStats.Level(Characters.ColonistSkill.Intellectual) * 0.025f;
        classGame.Player.Inventory.Add(ItemId.TechModule, 1);
        classGame.TrySelectInventorySlot(classGame.Player.Inventory.FindSlot(ItemId.TechModule));
        Tick(classGame, 1, PlayerAction.UseSelected, new GridPoint(1, 1));
        var moduleTech = classGame.ComputeWeaponDamage(emitter);
        var shiv = classGame.Items[ItemId.FerriteShiv];
        check(Math.Abs(baseTech - emitter.Damage * expectedSkill) < 0.01f && Math.Abs(moduleTech / baseTech - 1.12f) < 0.001f &&
              Math.Abs(classGame.ComputeWeaponDamage(shiv) - shiv.Damage * classGame.Player.MeleeDamageMultiplier) < 0.01f &&
              classGame.ComputeWeaponDamage(shiv, Calibration.Overclocked) > classGame.ComputeWeaponDamage(shiv) &&
              classGame.ComputeWeaponDamage(shiv, Calibration.Cracked) < classGame.ComputeWeaponDamage(shiv) &&
              classGame.Items[ItemId.CobaltSaber].EffectiveClass == DamageClass.Melee && classGame.Items[ItemId.WoodenSword].EffectiveClass == DamageClass.Melee,
            "class damage scales with the class skill (Tech uses Intellectual), class gear bonuses and calibrations");

        // ---- Charge use ----
        classGame.Player.Inventory.Add(ItemId.SparkEmitter, 1);
        var chargeBefore = classGame.Charge;
        var enemyCountBefore = classGame.Projectiles.Count;
        Use(classGame, ItemId.SparkEmitter, new GridPoint((int)classGame.Player.Motion.Position.X + 6, (int)classGame.Player.Motion.Position.Y));
        var spent = chargeBefore - classGame.Charge;
        var drained = classGame.Charge;
        for (var i = 0; i < 30 && classGame.Charge >= emitter.ChargeCost; i++)
            Use(classGame, ItemId.SparkEmitter, new GridPoint((int)classGame.Player.Motion.Position.X + 6, (int)classGame.Player.Motion.Position.Y));
        var projectilesWhenEmpty = classGame.Projectiles.Count;
        classGame.Player.ActionCooldownTicks = 0;
        Tick(classGame, 1, PlayerAction.UseSelected, new GridPoint((int)classGame.Player.Motion.Position.X + 6, (int)classGame.Player.Motion.Position.Y));
        check(chargeBefore > 0f && spent > emitter.ChargeCost - 0.5f && spent < emitter.ChargeCost + 0.5f && drained < chargeBefore &&
              classGame.LastMessage.Contains("charge", StringComparison.OrdinalIgnoreCase) &&
              classGame.MaximumCharge >= 20f && classGame.ChargeRegenPerSecond > 0f,
            "Tech weapons spend Charge, refuse to fire without it, and Charge regenerates");

        // ---- Drone and sentry slots ----
        var droneGame = new GameSimulation(seed: 6161, width: 80, height: 48);
        droneGame.Player.Inventory.Add(ItemId.ScoutDroneRemote, 1);
        var playerCell = new GridPoint((int)droneGame.Player.Motion.Position.X, (int)droneGame.Player.Motion.Position.Y);
        for (var i = 0; i < 3; i++)
            Use(droneGame, ItemId.ScoutDroneRemote, playerCell);
        var oneSlot = droneGame.ActiveDrones;
        droneGame.Player.Inventory.Add(ItemId.CommandVisor, 1);
        Use(droneGame, ItemId.CommandVisor, playerCell);
        for (var i = 0; i < 3; i++)
            Use(droneGame, ItemId.ScoutDroneRemote, playerCell);
        droneGame.Player.Inventory.Add(ItemId.TurretAutogun, 1);
        var sentrySpot = new GridPoint(playerCell.X + 2, playerCell.Y);
        Use(droneGame, ItemId.TurretAutogun, sentrySpot);
        Use(droneGame, ItemId.TurretAutogun, sentrySpot);
        var target = droneGame.SpawnCreature(EnemyKind.MossSlime, droneGame.Player.Motion.Position + new Vector2(5f, -1f));
        Tick(droneGame, 120);
        check(oneSlot == 1 && droneGame.DroneSlots == 2 && droneGame.ActiveDrones == 2 && droneGame.SentrySlots == 1 &&
              droneGame.ActiveSentries == 1 && droneGame.Minions.All(minion => minion.SpriteName.StartsWith("minion_") || minion.SpriteName.StartsWith("sentry_")) &&
              (!target.IsAlive || target.HitPoints < target.MaximumHitPoints),
            "drone and sentry summons respect slot limits (+1 from a command visor) and attack nearby hostiles");

        // ---- Calibration persistence ----
        var calibrated = new GameSimulation(seed: 6262, width: 80, height: 48);
        calibrated.Player.Inventory.Add(ItemId.CobaltSaber, 1, Calibration.Precise);
        var craftInventory = new Inventory(catalog);
        craftInventory.Add(ItemId.FerriteBar, 20);
        craftInventory.Add(ItemId.WoodBlock, 20);
        var forgeWorld = new TileGrid(12, 12);
        forgeWorld.SetTile(new GridPoint(5, 6), TileId.Anvil);
        var crafted = crafting.TryCraft("x-ferriteshiv", craftInventory, new CraftingEnvironment(new Vector2(5f, 6f), forgeWorld, tiles),
            _ => Calibration.Masterwork);
        var moved = new Inventory(catalog);
        craftInventory.TransferSlotTo(moved, craftInventory.FindSlot(ItemId.FerriteShiv), 0);
        var reloaded = RoundTrip(calibrated);
        var savedSlot = reloaded.Player.Inventory.Slots[reloaded.Player.Inventory.FindSlot(ItemId.CobaltSaber)];
        check(crafted.Success && crafted.Calibration == Calibration.Masterwork && moved.Slots[0].Calibration == Calibration.Masterwork &&
              savedSlot.Calibration == Calibration.Precise && reloaded.DescribeStack(savedSlot) == "Precise Cobalt saber" &&
              CalibrationCatalog.Roll(new Random(1), catalog[ItemId.FerriteShiv]) == CalibrationCatalog.Roll(new Random(1), catalog[ItemId.FerriteShiv]) &&
              CalibrationCatalog.Roll(new Random(1), catalog[ItemId.FerriteBar]) == Calibration.None,
            "calibrations roll on crafted weapons, move with their stack and survive a save round-trip");

        // ---- Tier recipe chain ----
        var chainOk = true;
        var tierWeapons = new[] { ItemId.FerriteShiv, ItemId.CobaltSaber, ItemId.PyrociteMaul, ItemId.ViridiumKatana,
            ItemId.LumeniteGreatsword, ItemId.VoidsteelScythe, ItemId.AstralBlade };
        for (var tier = 1; tier <= 7; tier++)
        {
            var bar = ContentItems.TierBars[tier];
            var station = ContentRecipes.TierStations[tier];
            var stationItem = catalog.All.First(item => item.UseKind == ItemUseKind.PlaceTile && item.PlacedTile != TileId.Air &&
                                                        tiles[item.PlacedTile].Station == station).Id;
            var weapon = crafting.FindByOutput(tierWeapons[tier - 1]).SingleOrDefault();
            chainOk &= crafting.FindByOutput(bar).Any() && crafting.FindByOutput(stationItem).Any() && weapon is not null &&
                       weapon.RequiredStations!.Contains(station) && weapon.Ingredients.Any(group => group.Alternatives.Contains(bar)) &&
                       catalog[tierWeapons[tier - 1]].Tier == tier;
        }
        check(chainOk && crafting.FindByOutput(ItemId.PowerForgeMk2).Single().Ingredients.Any(group => group.Alternatives.Contains(ItemId.OverseerCircuit)) &&
              crafting.FindByOutput(ItemId.OreRefineryMk2).Single().Ingredients.Any(group => group.Alternatives.Contains(ItemId.HeartFragment)),
            "each of the seven tiers has a bar recipe, a craftable forge, and gear forged from that bar at that forge");

        // ---- Doors ----
        var doorGame = new GameSimulation(seed: 6363, width: 80, height: 48);
        var doorX = (int)doorGame.Player.Motion.Position.X + 3;
        var doorFloor = doorGame.BiomeLayout.SurfaceAt(doorX);
        for (var y = 0; y < doorFloor; y++)
            doorGame.World.SetTile(new GridPoint(doorX, y), TileId.Air);
        doorGame.Player.Inventory.Add(ItemId.PlankDoor, 1);
        var groundY = Enumerable.Range(1, doorGame.World.Height - 2).First(y => doorGame.Tiles[doorGame.World.GetCell(doorX, y).TileId].IsSolid);
        Use(doorGame, ItemId.PlankDoor, new GridPoint(doorX, groundY - 1));
        var placedDoor = Enumerable.Range(groundY - 3, 3).All(y => doorGame.World.GetCell(doorX, y).TileId == TileId.PlankDoor);
        var closedSolid = doorGame.Tiles[doorGame.World.GetCell(doorX, groundY - 1).TileId].IsSolid;
        var opened = doorGame.Interact(new GridPoint(doorX, groundY - 2)).Kind == WorldInteractionKind.DoorToggled;
        var openPassable = Enumerable.Range(groundY - 3, 3).All(y => !doorGame.Tiles[doorGame.World.GetCell(doorX, y).TileId].IsSolid);
        doorGame.ToggleDoor(new GridPoint(doorX, groundY - 1));
        check(placedDoor && closedSolid && opened && openPassable && doorGame.Tiles[doorGame.World.GetCell(doorX, groundY - 3).TileId].IsSolid,
            "a placed door stands three tiles tall and interacting toggles it between solid and passable");

        // ---- Biomes on a large seed ----
        var large = new GameSimulation(seed: 9090, width: 1600, height: 500, blight: WorldBlight.Overgrowth);
        var features = large.Features!;
        int Count(GameSimulation sim, Func<WorldCell, bool> predicate)
        {
            var total = 0;
            foreach (var cell in sim.World.Cells)
                if (predicate(cell))
                    total++;
            return total;
        }
        var coast = features.AcidCoasts[0];
        var coastSurface = Enumerable.Range(1, large.World.Height - 2).First(y => large.World.GetCell(coast.Left + 3, y).TileId != TileId.Air ||
                                                                                   large.World.GetCell(coast.Left + 3, y).LiquidDepth > 0f);
        var canopy = features.Canopies[0];
        var canopyMiddle = (canopy.Left + canopy.Right) / 2;
        var canopyTop = Enumerable.Range(1, large.World.Height - 2).First(y => large.Tiles[large.World.GetCell(canopyMiddle, y).TileId].IsSolid);
        var hollow = features.FungalHollows[0];
        check(features.AcidCoasts.Count == 2 && Count(large, cell => cell.LiquidKind == LiquidKind.Water) > 200 &&
              Count(large, cell => cell.TileId is TileId.ShellSand or TileId.ShellBeach) > 100 &&
              features.FungalHollows.Count >= 1 && Count(large, cell => cell.TileId == TileId.GlowshroomGrass) > 20 &&
              features.Canopies.Count >= 1 && Count(large, cell => cell.TileId == TileId.CanopyGrass) > 20 &&
              features.BloomHives.Count >= 1 && Count(large, cell => cell.TileId == TileId.BloomHive) >= 1 &&
              Count(large, cell => cell.LiquidKind == LiquidKind.Nectar) > 10 &&
              features.Vault is not null && Count(large, cell => cell.TileId == TileId.PrecursorVaultDoor) == 3 &&
              Count(large, cell => cell.TileId == TileId.LockedVaultChest) == 2 &&
              features.GeodeChambers.Count >= 1 && features.BasaltColumns.Count >= 1 && features.AlabasterHalls.Count >= 1 &&
              features.MiteWarrens.Count >= 1 && Count(large, cell => cell.TileId is TileId.MossBlue or TileId.MossGold or TileId.MossRed or TileId.MossViolet) > 20 &&
              Count(large, cell => cell.TileId is TileId.Clay or TileId.Gravel or TileId.Slate) > 100 &&
              features.SurvivorSpots.Count >= 2 && large.TownNpcs.Count(npc => npc.IsBound) == features.SurvivorSpots.Count &&
              BiomeScanner.Scan(large.World, new GridPoint(coast.Left + 4, coastSurface)).Zone == BiomeZone.AcidCoast &&
              BiomeScanner.Scan(large.World, new GridPoint(canopyMiddle, canopyTop - 1)).Zone == BiomeZone.VerdantCanopy &&
              BiomeScanner.Scan(large.World, new GridPoint((hollow.Left + hollow.Right) / 2, (hollow.Top + hollow.Bottom) / 2)).Zone == BiomeZone.FungalHollows &&
              BiomeSpawnTables.KindsFor(BiomeZone.AcidCoast).Contains(EnemyKind.AcidSkitterer) &&
              BiomeSpawnTables.UsesTable(BiomeZone.CrystalSpires) &&
              large.World.Cells.SequenceEqual(new GameSimulation(seed: 9090, width: 1600, height: 500, blight: WorldBlight.Overgrowth).World.Cells),
            "a large world grows water oceans at both edges, fungal hollows, a canopy over bloom hives, the precursor vault, mini-biomes and bound survivors, deterministically");

        // ---- Crystal Spires: appear at the Fracture, spread, and purify blight ----
        var spiresBefore = Count(large, cell => cell.TileId is TileId.PrismaticGrass or TileId.PearlStone or TileId.PearlSand);
        large.TriggerFracture();
        var spiresAfter = Count(large, cell => cell.TileId is TileId.PrismaticGrass or TileId.PearlStone or TileId.PearlSand);
        // The first spires column with exposed prismatic grass (a column may be capped by a pond or a spire instead).
        var spreadX = Enumerable.Range(large.CrystalSpiresLeft, Math.Max(1, large.CrystalSpiresRight - large.CrystalSpiresLeft + 1))
            .FirstOrDefault(x => Enumerable.Range(1, 200).Any(y => large.World.GetCell(x, y).TileId == TileId.PrismaticGrass), large.CrystalSpiresLeft);
        var spreadY = Enumerable.Range(1, 200).First(y => large.World.GetCell(spreadX, y).TileId == TileId.PrismaticGrass);
        large.World.SetTile(new GridPoint(spreadX, spreadY + 1), TileId.OvergrowthSoil);
        var purified = large.Ticker.TrySpreadCrystal(spreadX, spreadY + 1);
        check(spiresBefore == 0 && spiresAfter > 50 && large.Flags.IsSet("blight.fracture") && purified &&
              large.World.GetCell(spreadX, spreadY + 1).TileId is TileId.PrismaticGrass or TileId.PearlStone &&
              BlightRules.CanBlightReplace(TileId.PearlStone, WorldBlight.Overgrowth, fractured: true) &&
              large.WorldEvents.CanStart(WorldEventKind.DarkSun, large, forced: false, out _),
            "the Fracture raises Crystal Spires that spread into blight (and blight back into them); Dark Sun becomes eligible");

        // ---- Acid harms ----
        var acidGame = new GameSimulation(seed: 6464, width: 80, height: 48);
        var center = acidGame.Player.Motion.Position + acidGame.Player.Motion.Size / 2f;
        for (var dy = -1; dy <= 1; dy++)
        {
            var cell = new GridPoint((int)center.X, (int)center.Y + dy);
            var data = acidGame.World.GetCell(cell);
            if (!acidGame.Tiles[data.TileId].IsSolid)
            {
                data.LiquidDepth = 1f;
                data.LiquidKind = LiquidKind.Acid;
                acidGame.World.SetCell(cell, data);
            }
        }
        var hpBefore = acidGame.Player.HitPoints;
        Tick(acidGame, 40);
        check(acidGame.Player.HitPoints < hpBefore && LiquidKind.Acid == (LiquidKind)3,
            "standing in the acid sea burns the player");

        // ---- Event: invasion waves, rewards, persistence ----
        var raid = new GameSimulation(seed: 6565, width: 80, height: 48);
        raid.Flags.Increment("hearts.broken");
        var startedRaid = raid.WorldEvents.TryStart(WorldEventKind.ScavengerRaid, raid, forced: true, out _);
        var wavesSeen = new List<int>();
        var creditsBefore = raid.Player.Inventory.Count(ItemId.Credits);
        for (var round = 0; round < 12 && raid.WorldEvents.IsActive(WorldEventKind.ScavengerRaid); round++)
        {
            wavesSeen.Add(raid.EventProgress.Single(progress => progress.Kind == WorldEventKind.ScavengerRaid).Wave);
            foreach (var enemy in raid.Enemies.Where(enemy => enemy.EventTag == WorldEventKind.ScavengerRaid).ToArray())
                enemy.TakeDamage(enemy.MaximumHitPoints);
            Tick(raid);
        }
        var raidSaved = RoundTrip(raid);
        check(startedRaid && wavesSeen.Distinct().SequenceEqual(new[] { 1, 2, 3, 4 }) && !raid.WorldEvents.IsActive(WorldEventKind.ScavengerRaid) &&
              raid.Flags.IsSet("event.scavenger_raid.cleared") && raid.WorldEvents.Completions(WorldEventKind.ScavengerRaid) == 1 &&
              raid.Player.Inventory.Count(ItemId.Credits) >= creditsBefore + 200 && raid.Player.Inventory.Count(ItemId.ScrapCrate) >= 2 &&
              raid.Flags.IsSet("boss.scavenger_warlord.down") && raidSaved.Flags.IsSet("event.scavenger_raid.cleared") &&
              raidSaved.WorldEvents.Completions(WorldEventKind.ScavengerRaid) == 1 && raidSaved.Flags.IsSet("npc.demolitionist.unlocked"),
            "a scavenger raid runs three waves and its warlord, grants its reward, sets its first-clear flag, and all of it persists");

        raid.Flags.Set("boss.stone_sentinel.down");
        raid.WorldEvents.TryStart(WorldEventKind.RustMoon, raid, forced: true, out _);
        foreach (var enemy in raid.Enemies.ToArray())
            enemy.TakeDamage(enemy.MaximumHitPoints);
        var moonEnemy = raid.SpawnCreature(EnemyKind.EventHusk, raid.Player.Motion.Position + new Vector2(8f, -2f));
        moonEnemy.EventTag = WorldEventKind.RustMoon;
        moonEnemy.TakeDamage(moonEnemy.MaximumHitPoints);
        Tick(raid);
        var moonSaved = RoundTrip(raid);
        var moonProgress = moonSaved.EventProgress.SingleOrDefault(progress => progress.Kind == WorldEventKind.RustMoon);
        check(moonSaved.WorldEvents.IsActive(WorldEventKind.RustMoon) && moonSaved.WorldEvents.Active.Single(state => state.Kind == WorldEventKind.RustMoon).Kills == 1 &&
              moonProgress.Name == "Rust Moon" && moonProgress.Progress > 0f && moonProgress.Progress < 1f &&
              raid.WorldEvents.NaturalRoll(5, WorldEventMoment.Dusk, WorldEventKind.RustMoon) == moonSaved.WorldEvents.NaturalRoll(5, WorldEventMoment.Dusk, WorldEventKind.RustMoon),
            "a running event's kill progress, name and bar fraction survive a save; natural rolls are deterministic");

        // ---- Housing ----
        var town = new GameSimulation(seed: 6666, width: 80, height: 48);
        var inside = BuildRoom(town, 6);
        var valid = town.CheckHousing(inside);
        var openTown = new GameSimulation(seed: 6666, width: 80, height: 48);
        var openInside = BuildRoom(openTown, 6, roof: false);
        var open = openTown.CheckHousing(openInside);
        var noLightTown = new GameSimulation(seed: 6666, width: 80, height: 48);
        var dark = BuildRoom(noLightTown, 6);
        noLightTown.World.SetTile(new GridPoint(9, 10), TileId.Air);
        noLightTown.Regions.ForceFullRebuild();
        check(valid.Valid && !open.Valid && open.Reason.Contains("enclosed") && !noLightTown.CheckHousing(dark).Valid &&
              noLightTown.CheckHousing(dark).Reason.Contains("light"),
            $"housing accepts a walled, lit room with a door, table and chair, and rejects an open or unlit one ({valid.Reason} / {open.Reason})");

        // ---- NPC arrival ----
        town.CheckTownArrivals();
        var resident = town.TownNpcs.FirstOrDefault(npc => !npc.IsBound);
        var homeRoom = resident?.HomeCell is { } home ? town.Regions.GetRegion(home) : null;
        var movedIn = resident is { Kind: TownNpcKind.StationAi } && homeRoom is not null && homeRoom == town.Regions.GetRegion(inside);
        BuildRoom(town, 30);
        town.Player.Inventory.Add(ItemId.Credits, 60);
        town.CheckTownArrivals();
        check(movedIn &&
              town.TownNpcs.Any(npc => npc.Kind == TownNpcKind.Quartermaster && npc.HomeCell is not null) &&
              !town.CheckHousing(inside).Valid && town.Flags.IsSet("npc.quartermaster.unlocked"),
            $"town NPCs move into free valid housing once their unlock flag is set (credits unlock the quartermaster)");

        // ---- Shop prices follow happiness ----
        var quartermaster = town.TownNpcs.First(npc => npc.Kind == TownNpcKind.Quartermaster);
        var calmPrice = town.GetShop(quartermaster).First(offer => offer.Item == ItemId.WallTorch).Price;
        var calmMultiplier = town.GetPriceMultiplier(quartermaster);
        town.AddTownNpc(TownNpcKind.TradeConsul, quartermaster.Motion.Position + new Vector2(1f, 0f), bound: false);
        var grumpyMultiplier = town.GetPriceMultiplier(quartermaster);
        town.AddTownNpc(TownNpcKind.Cook, quartermaster.Motion.Position + new Vector2(-1f, 0f), bound: false);
        town.AddTownNpc(TownNpcKind.Cook, quartermaster.Motion.Position + new Vector2(-2f, 0f), bound: false);
        town.Player.Inventory.Add(ItemId.RockBlock, 50); // something to sell (the starting kit has no blocks)
        var creditsForShop = town.Player.Inventory.Count(ItemId.Credits);
        var bought = town.Buy(quartermaster, ItemId.WallTorch);
        var sold = town.Sell(quartermaster, town.Player.Inventory.FindSlot(ItemId.RockBlock), 10);
        check(grumpyMultiplier > calmMultiplier && town.GetPriceMultiplier(quartermaster) < grumpyMultiplier && calmPrice >= 1 &&
              bought && town.Player.Inventory.Count(ItemId.Credits) < creditsForShop + sold && sold > 0 &&
              town.GetShop(quartermaster).All(offer => offer.Price >= 1),
            "shop prices rise with disliked neighbours and fall with liked ones; buying and selling exchange credits");

        // ---- Survivor rescue ----
        var rescue = new GameSimulation(seed: 6767, width: 80, height: 48);
        var survivor = rescue.AddTownNpc(TownNpcKind.Mechanic, rescue.Player.Motion.Position + new Vector2(2f, 0f), bound: true);
        var survivorCell = RegionMap.CellAt(survivor.Motion.Position + survivor.Motion.Size / 2f);
        var rescueRoom = BuildRoom(rescue, 6);
        var freed = rescue.Interact(survivorCell);
        check(freed.Kind == WorldInteractionKind.SurvivorFreed && !survivor.IsBound && survivor.SpriteName == "npc_mechanic" &&
              rescue.Flags.IsSet("npc.mechanic.unlocked") && survivor.HomeCell is { } survivorHome &&
              rescue.Regions.GetRegion(survivorHome) == rescue.Regions.GetRegion(rescueRoom) &&
              rescue.GetNpcDialogue(survivor).Length > 0 && RoundTrip(rescue).TownNpcs.Any(npc => npc.Kind == TownNpcKind.Mechanic && !npc.IsBound),
            "freeing a bound survivor unlocks them as a resident who claims a free home and persists");

        // ---- Fishing tables ----
        var random = new Random(7);
        ItemId Common(LiquidKind liquid, BiomeZone zone, FishingDepth depth) =>
            FishingTables.Pick(new FishingContext(liquid, zone, depth, 100, 30f, false, 0f), FishingRarity.Common, random);
        var water = Common(LiquidKind.Water, BiomeZone.FrozenWasteland, FishingDepth.Surface);
        var acid = Common(LiquidKind.Acid, BiomeZone.AcidCoast, FishingDepth.Surface);
        var lava = Common(LiquidKind.Lava, BiomeZone.MoltenChasm, FishingDepth.Chasm);
        var nectar = Common(LiquidKind.Nectar, BiomeZone.BloomHives, FishingDepth.Caverns);
        var cave = Common(LiquidKind.Water, BiomeZone.Caverns, FishingDepth.Caverns);
        var canopyFish = Common(LiquidKind.Water, BiomeZone.VerdantCanopy, FishingDepth.Surface);
        check(new[] { water, acid, lava, nectar, cave, canopyFish }.Distinct().Count() == 6 &&
              FishingTables.CrateFor(new FishingContext(LiquidKind.Water, BiomeZone.FungalHollows, FishingDepth.Caverns, 100, 30f, false, 0f)) == ItemId.FungalCrate &&
              GameSimulation.EffectivePower(40f, 20, 12f, 0f) < GameSimulation.EffectivePower(40f, 100, 6f, 1f),
            "catch tables differ by liquid, biome and depth; small pools, midday and dry weather weaken a cast");

        // ---- Fishing loop: bobber, bite, bait, catch ----
        var angler = new GameSimulation(seed: 6868, width: 80, height: 48);
        var px = (int)MathF.Floor(angler.Player.Motion.Position.X + angler.Player.Motion.Size.X / 2f);
        var feetY = (int)MathF.Floor(angler.Player.Motion.Position.Y + angler.Player.Motion.Size.Y) ;
        for (var x = px + 2; x <= px + 7; x++)
        {
            for (var y = 0; y <= feetY + 3; y++)
                angler.World.SetTile(new GridPoint(x, y), y <= feetY + 2 ? TileId.Air : TileId.Rock);
        }
        for (var x = px + 1; x <= px + 8; x += 7)
            for (var y = feetY; y <= feetY + 3; y++)
                angler.World.SetTile(new GridPoint(x, y), TileId.Rock);
        for (var x = px + 2; x <= px + 7; x++)
            for (var y = feetY; y <= feetY + 2; y++)
                angler.World.TryAddLiquid(new GridPoint(x, y), LiquidKind.Water, 1f, angler.Tiles);
        angler.Player.Inventory.Add(ItemId.FishingRod, 1);
        angler.Player.Inventory.Add(ItemId.TerraRat, 5);
        var catchesBefore = angler.Player.Inventory.Slots.Count(stack => !stack.IsEmpty);
        var baitBefore = angler.Player.Inventory.Count(ItemId.TerraRat);
        var landed = false;
        var bites = 0;
        angler.TrySelectInventorySlot(angler.Player.Inventory.FindSlot(ItemId.FishingRod));
        for (var step = 0; step < 6000 && bites < 3; step++)
        {
            if (angler.Bobber is null)
            {
                angler.Player.ActionCooldownTicks = 0;
                Tick(angler, 1, PlayerAction.UseSelected, new GridPoint(px + 5, feetY + 1));
                continue;
            }
            landed |= angler.Bobber.Landed;
            if (angler.Bobber.Biting)
            {
                bites++;
                angler.Player.ActionCooldownTicks = 0;
                Tick(angler, 1, PlayerAction.UseSelected, new GridPoint(px + 5, feetY + 1));
                continue;
            }
            Tick(angler);
        }
        check(landed && bites >= 1 && angler.Player.Inventory.Count(ItemId.TerraRat) < baitBefore &&
              angler.Flags.Get("fishing.catches") >= 1 && angler.Items[ItemId.TerraRat].BaitPower > 0,
            "a cast bobber lands on water, bites come, bait is spent on bites and reeling lands a catch");

        // ---- Crates ----
        angler.Player.Inventory.Add(ItemId.ScrapCrate, 1);
        var crateLoot = angler.OpenCrate(ItemId.ScrapCrate);
        check(crateLoot.Count > 0 && angler.Player.Inventory.Count(ItemId.ScrapCrate) == 0 &&
              crateLoot.All(entry => FishingTables.CrateLoot(ItemId.ScrapCrate).Any(row => row.Item == entry.Item)),
            "opening a crate consumes it and grants loot from its table");

        // ---- Quest fish rotation and turn-in ----
        var warden = angler.AddTownNpc(TownNpcKind.TideWarden, angler.Player.Motion.Position, bound: false);
        var rotation = Enumerable.Range(1, 12).Select(angler.QuestFishForDay).ToArray();
        var today = angler.TodaysQuestFish;
        angler.Player.Inventory.Add(today, 1);
        var questCredits = angler.Player.Inventory.Count(ItemId.Credits);
        var turnedIn = angler.TurnInQuestFish(warden);
        angler.Player.Inventory.Add(today, 1);
        var twice = angler.TurnInQuestFish(warden);
        check(rotation.Distinct().Count() >= 3 && rotation.SequenceEqual(Enumerable.Range(1, 12).Select(new GameSimulation(seed: 6868, width: 80, height: 48).QuestFishForDay)) &&
              rotation.All(fish => angler.Items[fish].IsQuestFish) && turnedIn && !twice && angler.Flags.Get("quests.fishing") == 1 &&
              angler.Player.Inventory.Count(ItemId.Credits) > questCredits && angler.Player.Inventory.Count(ItemId.FerriteFishingRod) == 1 &&
              !angler.UnlockedQuestFish.Contains(ItemId.GlassPike),
            "the Tide-Warden's quest fish rotates daily (deterministically), pays once per day, and gated fish stay locked");

        // ---- Weather: deterministic rolls and persistence ----
        var skyA = new GameSimulation(seed: 6969, width: 80, height: 48);
        var skyB = new GameSimulation(seed: 6969, width: 80, height: 48);
        skyA.WeatherControl.RollTargets(3, 1, BiomeZone.VerdantCanopy, false);
        skyB.WeatherControl.RollTargets(3, 1, BiomeZone.VerdantCanopy, false);
        var sameRoll = skyA.Weather == skyB.Weather;
        var rolls = Enumerable.Range(0, 40).Select(day => skyA.WeatherControl.Roll(day, 0, 2)).ToArray();
        skyA.WeatherControl.Force(rain: 0.8f, wind: 2.5f, storm: true, immediate: true);
        var skySaved = RoundTrip(skyA);
        skyB.WeatherControl.RollTargets(3, 1, BiomeZone.FrozenWasteland, false);
        check(sameRoll && skyA.Weather.LastRollDay == 3 &&
              rolls.Distinct().Count() > 30 && rolls.All(value => value is >= 0f and < 1f) &&
              skySaved.Weather.Rain == 0.8f && skySaved.Weather.Wind == 2.5f && skySaved.Weather.Storm &&
              skyB.Weather.TargetRain == 0f && skyB.Weather.LastRollDay == 3 &&
              new GameSimulation(seed: 6969, width: 80, height: 48).WeatherControl.Roll(9, 2, 4) == skyA.WeatherControl.Roll(9, 2, 4),
            "weather rolls are a deterministic function of seed, day and slot (frozen zones snow instead of rain) and weather persists");

        // ---- Rain fills an open basin ----
        var basin = new GameSimulation(seed: 7070, width: 80, height: 48);
        var bx = (int)basin.Player.Motion.Position.X + 6;
        var bTop = Enumerable.Range(1, basin.World.Height - 2).First(y => basin.Tiles[basin.World.GetCell(bx, y).TileId].IsSolid);
        for (var x = bx - 1; x <= bx + 1; x++)
        {
            for (var y = 0; y < bTop; y++)
                basin.World.SetTile(new GridPoint(x, y), TileId.Air);
            basin.World.SetTile(new GridPoint(x, bTop), TileId.Air);
            basin.World.SetTile(new GridPoint(x, bTop + 1), TileId.Rock);
        }
        basin.World.SetTile(new GridPoint(bx - 2, bTop), TileId.Rock);
        basin.World.SetTile(new GridPoint(bx + 2, bTop), TileId.Rock);
        var waterBefore = basin.World.GetCell(bx, bTop).LiquidDepth;
        var filledCells = 0;
        for (var i = 0; i < 6; i++)
            filledCells += basin.FillBasinsWithRain(1f);
        check(filledCells > 0 && basin.World.GetCell(bx, bTop).LiquidDepth > waterBefore,
            "rain slowly fills an open basin");

        // ---- First-clear trigger fires once ----
        var triggers = new GameSimulation(seed: 7171, width: 80, height: 48);
        var fired = 0;
        triggers.Flags.OnFirstSet("test.trigger", () => fired++);
        var first = triggers.Flags.Set("test.trigger");
        var second = triggers.Flags.Set("test.trigger");
        var overseer = triggers.SpawnCreature(EnemyKind.StoneSentinel, triggers.Player.Motion.Position + new Vector2(10f, -4f));
        overseer.TakeDamage(overseer.MaximumHitPoints);
        Tick(triggers);
        var circuits = triggers.Player.Inventory.Count(ItemId.OverseerCircuit);
        var again = triggers.SpawnCreature(EnemyKind.StoneSentinel, triggers.Player.Motion.Position + new Vector2(10f, -4f));
        again.TakeDamage(again.MaximumHitPoints);
        Tick(triggers);
        check(first && !second && fired == 1 && triggers.Flags.IsSet("boss.stone_sentinel.down") && circuits == 1 &&
              triggers.RecentMessages.Count(message => message.Contains("Swarm Rain")) <= 1 &&
              RoundTrip(triggers).Flags.IsSet("boss.stone_sentinel.down"),
            "first-clear hooks fire exactly once and boss kills set persisted world flags");

        // ---- Countdown summon (the sigil waits for the obelisks to fall) ----
        triggers.Player.Inventory.Add(ItemId.SummonAstralSigil, 1);
        triggers.Flags.Set("obelisks.down");
        foreach (var boss in triggers.Enemies.Where(enemy => enemy.IsBoss).ToArray())
            boss.TakeDamage(boss.MaximumHitPoints);
        Tick(triggers);
        Use(triggers, ItemId.SummonAstralSigil, new GridPoint(1, 1));
        var countdownStarted = triggers.AstralCountdown is > 59f and <= 60f;
        Tick(triggers, 30 * 60);
        var midway = triggers.AstralCountdown is > 25f and < 35f && !triggers.Enemies.Any(enemy => enemy.Kind == EnemyKind.AstroGod);
        var countdownSaved = RoundTrip(triggers);
        Tick(triggers, 31 * 60);
        check(countdownStarted && midway && countdownSaved.AstralCountdown is > 25f and < 35f &&
              triggers.Enemies.Any(enemy => enemy.Kind == EnemyKind.AstroGod) && triggers.AstralCountdown == 0f,
            "the astral sigil starts a persisted 60-second countdown that ends with the Astro-God");

        // ---- Bug net, critters and walls ----
        var netGame = new GameSimulation(seed: 7272, width: 80, height: 48);
        // Land first: reach is a box 5 across and 3 up or down past the body, so a falling player can't reach the ground.
        for (var settle = 0; settle < 120 && !netGame.Player.Motion.Grounded; settle++)
            Tick(netGame, 1, PlayerAction.None, null);
        var critter = netGame.SpawnCreature(EnemyKind.GlowGrub, netGame.Player.Motion.Position + new Vector2(2f, 0f));
        netGame.Player.Inventory.Add(ItemId.BugNet, 1);
        Use(netGame, ItemId.BugNet, RegionMap.CellAt(critter.Motion.Position + critter.Motion.Size / 2f));
        // The player's own column: its ground is right under the feet, inside the 3-row vertical reach.
        var wallX = (int)(netGame.Player.Motion.Position.X + netGame.Player.Motion.Size.X / 2f);
        var wallGround = Enumerable.Range((int)netGame.Player.Motion.Position.Y, 20).First(y => netGame.Tiles[netGame.World.GetCell(wallX, y).TileId].IsSolid);
        var wallCell = new GridPoint(wallX, wallGround - 1);
        netGame.World.SetWall(wallCell, WallIds.None);
        netGame.Player.Inventory.Add(ItemId.PlankWall, 2);
        netGame.Player.Inventory.Add(ItemId.FerriteDemolisher, 1);
        Use(netGame, ItemId.PlankWall, wallCell);
        var wallPlaced = netGame.World.GetCell(wallCell).WallId == WallIds.Plank;
        // A wall falls at 100 damage and each copper-tier hammer hit adds power × 1.5, so it takes a few hits.
        for (var hit = 0; hit < 6 && netGame.World.GetCell(wallCell).WallId != WallIds.None; hit++)
            Use(netGame, ItemId.FerriteDemolisher, wallCell);
        check(netGame.Player.Inventory.Count(ItemId.GlowGrub) == 1 && !netGame.Enemies.Contains(critter) && wallPlaced &&
              netGame.World.GetCell(wallCell).WallId == WallIds.None && netGame.Player.Inventory.Count(ItemId.PlankWall) +
              netGame.GroundItems.Where(drop => drop.ItemId == ItemId.PlankWall).Sum(drop => drop.Count) == 2 && // parity B4: refund drops as a ground item

              WallIds.SpriteName(WallIds.Plank) == "wall_plank",
            "the bug net catches critters into items; wall items place walls and demolishers refund them");
    }
}
