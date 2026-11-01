using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Self-tests for town tracks, modes and progression achievements (docs/PROGRESSION_DIFF.md G13-G16): the Field Zoologist,
/// Pigment Trader and Party Planner move-in conditions, the companion permit pet, the colony party, the five progression
/// achievements, Salvager deaths and Annihilation scaling.
/// </summary>
public static class ModesSelfTests
{
    private static GameSimulation NewGame(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 80, height: 48) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e7f);
        for (var y = 4; y <= 21; y++)
        for (var x = 4; x <= 76; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y >= 20 ? TileId.Rock : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            game.World.SetCell(cell, data);
        }
        game.Player.Motion.Position = new Vector2(40f, 20f - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        Tick(game, 3);
        return game;
    }

    private static void Tick(GameSimulation game, int ticks = 1)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f), PlayerAction.None, null));
    }

    private static bool Unlocked(GameSimulation game, TownNpcKind kind) => game.Flags.IsSet($"npc.{WorldFlags.Key(kind)}.unlocked");

    /// <summary>A valid house (walls, light, door, table and chair) whose floor sits on row 19.</summary>
    private static void BuildRoom(GameSimulation game, int x0)
    {
        const int top = 10, bottom = 20;
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
        game.Regions.ForceFullRebuild();
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- G13: Field Zoologist (bestiary at 10%) ----
        var zoo = NewGame(9301);
        zoo.RefreshTownContentUnlocks();
        var lockedAtStart = !Unlocked(zoo, TownNpcKind.FieldZoologist);
        foreach (var kind in Enum.GetValues<EnemyKind>())
        {
            if (zoo.Bestiary.Completion >= TownContent.ZoologistBestiaryShare)
                break;
            for (var i = 0; i < 10; i++)
                zoo.Bestiary.RecordKill(kind, BiomeZone.Grassland);
        }
        zoo.RefreshTownContentUnlocks();
        var zoologist = TownNpcCatalog.Get(TownNpcKind.FieldZoologist);
        check(lockedAtStart && Unlocked(zoo, TownNpcKind.FieldZoologist) && zoologist.Sprite == "npc_field_zoologist" &&
              zoologist.Shop.Any(entry => entry.Item == ItemId.BugNet) && zoologist.Lines.Length > 0,
            $"the Field Zoologist moves in once the bestiary is {TownContent.ZoologistBestiaryShare:P0} complete ({zoo.Bestiary.Completion:P1}) and sells critter goods");

        // ---- G13: Pigment Trader (dye material carried) ----
        var pigment = NewGame(9302);
        pigment.RefreshTownContentUnlocks();
        var pigmentLocked = !Unlocked(pigment, TownNpcKind.PigmentTrader);
        pigment.Player.Inventory.Add(ItemId.DyePetals, 1);
        pigment.RefreshTownContentUnlocks();
        var trader = pigment.AddTownNpc(TownNpcKind.PigmentTrader, pigment.Player.Motion.Position, bound: false);
        check(pigmentLocked && Unlocked(pigment, TownNpcKind.PigmentTrader) && pigment.GetShop(trader).Any(offer => offer.Item == ItemId.CrimsonDye) &&
              TownContent.IsDyeMaterial(ItemId.AzureDye),
            "the Pigment Trader moves in once a dye material is carried and sells dyes");

        // ---- G13: Party Planner (10 residents) and the colony party ----
        var town = NewGame(9303);
        TownNpcKind[] residents =
        [
            TownNpcKind.Quartermaster, TownNpcKind.Medic, TownNpcKind.Armorer, TownNpcKind.ChiefEngineer, TownNpcKind.AstroBotanist,
            TownNpcKind.HeadResearcher, TownNpcKind.TideWarden, TownNpcKind.Demolitionist, TownNpcKind.Tailor
        ];
        foreach (var kind in residents)
            town.AddTownNpc(kind, town.Player.Motion.Position, bound: false);
        town.AddTownNpc(TownNpcKind.GelAzure, town.Player.Motion.Position, bound: false); // gels do not count
        town.RefreshTownContentUnlocks();
        var nineIsNotEnough = !Unlocked(town, TownNpcKind.PartyPlanner);
        town.AddTownNpc(TownNpcKind.Cook, town.Player.Motion.Position, bound: false);
        town.RefreshTownContentUnlocks();
        check(nineIsNotEnough && Unlocked(town, TownNpcKind.PartyPlanner) && town.TownResidentCount == 10,
            $"the Party Planner moves in at {TownContent.PartyPlannerResidents} residents (gels and pets do not count)");

        var noPlanner = !town.StartColonyParty();
        town.AddTownNpc(TownNpcKind.PartyPlanner, town.Player.Motion.Position, bound: false);
        foreach (var agent in town.Colonists)
            agent.Needs.Drain(Rimtera.AI.NeedKind.Joy, 1f);
        town.Player.Needs.Drain(Rimtera.AI.NeedKind.Joy, 1f);
        var started = town.StartColonyParty();
        var joyful = town.Colonists.Where(agent => agent.IsAlive).All(agent => agent.Needs.JoyReserve >= 0.99f) &&
                     town.Player.Needs.JoyReserve >= 0.99f;
        var buffed = town.Player.Statuses.Active.Any(status => status.Id == StatusId.Regeneration);
        var noRepeat = !town.StartColonyParty();
        check(noPlanner && started && town.IsColonyPartyActive && joyful && buffed && noRepeat &&
              town.ColonyPartyHoursLeft is > 0f and <= TownContent.PartyHours + 0.01f,
            $"the Party Planner throws a colony party: joy fills for every colonist, the player regenerates, and it cannot be repeated at once ({town.ColonyPartyHoursLeft:0.0} h left)");

        // ---- G13: companion permit (Quartermaster) brings a town pet ----
        var pets = NewGame(9304);
        var clerk = pets.AddTownNpc(TownNpcKind.Quartermaster, pets.Player.Motion.Position, bound: false);
        var sold = pets.GetShop(clerk).Any(offer => offer.Item == ItemId.CompanionPermit);
        pets.Player.Inventory.Add(ItemId.Credits, 2000);
        var bought = pets.Buy(clerk, ItemId.CompanionPermit);
        var homeless = !pets.FileCompanionPermit() && pets.TownPets.Count == 0;
        BuildRoom(pets, 20);
        Tick(pets, 31); // the half-second town pass files the carried permit
        check(sold && bought && homeless && pets.TownPets.Count == 1 && pets.Player.Inventory.Count(ItemId.CompanionPermit) == 0,
            $"a companion permit bought from the Quartermaster brings a passive town pet once a free house exists ({pets.TownPets.FirstOrDefault()?.Name})");

        // ---- G14: progression achievements ----
        var ach = NewGame(9305);
        (string Flag, string Id)[] flagged =
        [
            ("blight.fracture", "crust-breaker"), ("hardmode.trio.down", "triple-threat"),
            ("boss.vault_colossus.down", "colossus-toppled"), ("obelisks.down", "four-pillars-fall")
        ];
        var noneEarly = flagged.All(entry => !ach.Achievements.IsUnlocked(entry.Id)) && !ach.Achievements.IsUnlocked("full-vitality");
        foreach (var (flag, id) in flagged)
        {
            ach.Flags.Set(flag);
            ach.Achievements.Evaluate(ach);
            check(noneEarly && ach.Achievements.IsUnlocked(id) && AchievementCatalog.Get(id) is not null,
                $"achievement {AchievementCatalog.Get(id)?.Name} unlocks on {flag}");
        }
        ach.Player.LifeCoresUsed = PlayerCharacter.MaximumLifeCores;
        ach.Achievements.Evaluate(ach);
        var coresOnly = ach.Achievements.IsUnlocked("full-vitality");
        ach.LifebloomPodsUsed = GrowthItems.MaximumLifeblooms;
        ach.Achievements.Evaluate(ach);
        check(!coresOnly && ach.Achievements.IsUnlocked("full-vitality") && AchievementTracker.AtLifeCap(ach) &&
              !AchievementTracker.AtLifeCap(NewGame(9306)),
            "Full Vitality waits for every life core and lifebloom pod (300 life)");

        // ---- G15: Salvager drops every carried item at the death site; it can be recovered ----
        var salvage = NewGame(9307);
        salvage.Player.Inventory.Add(ItemId.Credits, 100);
        salvage.Player.Inventory.Add(ItemId.CopperOre, 25);
        salvage.Player.Inventory.Add(ItemId.MedStim, 3);
        salvage.Player.Equipment.Set(EquipmentSlot.Head, ItemId.ScrapSalvagerMask);
        int Carried(GameSimulation game) => game.Player.Inventory.Slots.Where(stack => !stack.IsEmpty && stack.ItemId != ItemId.Credits).Sum(stack => stack.Count);
        var before = Carried(salvage);
        var site = salvage.Player.Motion.Position;
        salvage.Player.TakeDamage(1e6f);
        Tick(salvage, 2);
        salvage.RespawnPlayer(CharacterDifficulty.Salvager);
        var emptied = Carried(salvage) == 0;
        var onGround = salvage.GroundItems.Where(item => item.ItemId != ItemId.Credits).Sum(item => item.Count);
        var credits = salvage.GroundItems.Where(item => item.ItemId == ItemId.Credits).Sum(item => item.Count);
        var nearSite = salvage.GroundItems.All(item => Vector2.Distance(item.Position, site) < 6f);
        var geared = salvage.Player.Equipment[EquipmentSlot.Head] == ItemId.ScrapSalvagerMask;
        salvage.Player.Motion.Position = site;
        salvage.Player.Motion.Velocity = Vector2.Zero;
        Tick(salvage, 240);
        var recovered = Carried(salvage);
        check(emptied && onGround == before && credits == 40 && nearSite && geared && recovered == before &&
              WorldSettings.Name(CharacterDifficulty.Salvager) == "Medium" &&
              salvage.GetRespawnDelay(CharacterDifficulty.Salvager) == salvage.GetRespawnDelay(CharacterDifficulty.Classic),
            $"a Salvager death drops all {before} carried items (not worn gear) and 40% of credits at the death site; walking back recovers {recovered}");

        // ---- 0.1.8 stakes: Easy drops money only; Hard drops everything but tools, and all the money ----
        GameSimulation Died(CharacterDifficulty difficulty)
        {
            var game = NewGame(9308);
            game.Player.Inventory.Add(ItemId.Credits, 100);
            game.Player.Inventory.Add(ItemId.CopperOre, 25);
            game.Player.Inventory.Add(ItemId.MedStim, 3);
            game.Player.TakeDamage(1e6f);
            Tick(game, 2);
            game.RespawnPlayer(difficulty);
            return game;
        }
        int Held(GameSimulation game, ItemId item) => game.Player.Inventory.Count(item);
        int Dropped(GameSimulation game, ItemId item) => game.GroundItems.Where(pile => pile.ItemId == item).Sum(pile => pile.Count);
        var easy = Died(CharacterDifficulty.Classic);
        var hard = Died(CharacterDifficulty.Hard);
        var drillBefore = NewGame(9308).Player.Inventory.Count(ItemId.CopperPickaxe);
        check(Held(easy, ItemId.CopperOre) == 25 && Held(easy, ItemId.MedStim) == 3 && Dropped(easy, ItemId.Credits) is > 0 and < 100 &&
              Held(hard, ItemId.CopperOre) == 0 && Dropped(hard, ItemId.CopperOre) == 25 && Dropped(hard, ItemId.MedStim) == 3 &&
              Dropped(hard, ItemId.Credits) == 100 && drillBefore > 0 && Held(hard, ItemId.CopperPickaxe) == drillBefore &&
              WorldSettings.Name(CharacterDifficulty.Classic) == "Easy" && WorldSettings.Name(CharacterDifficulty.Hard) == "Hard" &&
              WorldSettings.CreationOrder.Take(4).SequenceEqual([CharacterDifficulty.Classic, CharacterDifficulty.Salvager, CharacterDifficulty.Hard, CharacterDifficulty.Ironman]),
            "death drops by difficulty: Easy loses only part of its money; Hard drops every credit and everything but its tools");

        // ---- 0.1.9: right-click cancels a helpful buff, never a debuff ----
        var buffs = NewGame(9309);
        buffs.Player.Statuses.Apply(StatusId.Regeneration, 2f);
        buffs.Player.Statuses.Apply(StatusId.Poison, 2f);
        var cancelledHelp = buffs.CancelBuff(nameof(StatusId.Regeneration));
        var cancelledHarm = buffs.CancelBuff(nameof(StatusId.Poison));
        check(cancelledHelp && !cancelledHarm && buffs.ActiveBuffs.All(buff => buff.Status != StatusId.Regeneration) &&
              buffs.ActiveBuffs.Any(buff => buff.Status == StatusId.Poison),
            "right-clicking a buff cancels a helpful status but not a debuff");

        // ---- 0.1.9: misc equipment slots (pet, light pet, mount, hook) work as if carried ----
        var misc = NewGame(9310);
        var wrongRefused = !misc.SetMisc(MiscSlot.Pet, ItemId.CopperOre, out _);
        var petIn = misc.SetMisc(MiscSlot.Pet, ItemId.PetCrabShell, out _) && misc.SetMisc(MiscSlot.Hook, ItemId.GrappleHook, out _);
        Tick(misc, 2);
        var petOut = misc.ActiveBuffs.Any(buff => buff.Key == "pet") && misc.HasGrapple;
        var savedMisc = GameSimulation.Restore(misc.CaptureSnapshot());
        var kept = savedMisc.MiscEquipped(MiscSlot.Pet) == ItemId.PetCrabShell && savedMisc.MiscEquipped(MiscSlot.Hook) == ItemId.GrappleHook;
        misc.SetMisc(MiscSlot.Pet, null, out _);
        misc.SetMisc(MiscSlot.Hook, null, out _);
        Tick(misc, 2);
        check(wrongRefused && petIn && petOut && kept && misc.ActiveBuffs.All(buff => buff.Key != "pet") && !misc.HasGrapple,
            "an item in a misc slot works as if carried (the pet follows, the hook throws), is saved, and stops when taken out");

        // ---- 0.1.10: coin and ammo slots beside the bag ----
        var pouch = new Inventory(misc.Items, 10, coinSlots: 2, ammoSlots: 2);
        pouch.Add(ItemId.Credits, 40);
        pouch.Add(ItemId.Arrow, 30);
        pouch.Add(ItemId.CopperOre, 5);
        var filled = pouch.Slots[10].ItemId == ItemId.Credits && pouch.Slots[12].ItemId == ItemId.Arrow && pouch.Slots[0].ItemId == ItemId.CopperOre;
        var refusesOre = pouch.PlaceIntoSlot(11, new ItemStack(ItemId.CopperOre, 1)).Count == 1 &&
                         pouch.PlaceIntoSlot(13, new ItemStack(ItemId.CopperOre, 1)).Count == 1;
        var held = new ItemStack(ItemId.CopperOre, 1);
        pouch.SwapWithCursor(11, ref held);
        var swapRefused = held.ItemId == ItemId.CopperOre && pouch.Slots[11].IsEmpty;
        pouch.PlaceIntoSlot(1, new ItemStack(ItemId.Arrow, 10));
        pouch.TryConsume(ItemId.Arrow);
        var ammoFirst = pouch.Slots[12].Count == 29 && pouch.Slots[1].Count == 10;
        pouch.Sort(0);
        var chestBox = new Inventory(misc.Items, 10);
        pouch.MoveAllTo(chestBox);
        var stayPut = pouch.Slots[10].Count == 40 && pouch.Slots[12].Count == 29 && chestBox.Count(ItemId.CopperOre) == 5;
        var coinsKept = NewGame(9311);
        coinsKept.Player.Inventory.Add(ItemId.Credits, 7);
        var coinSlot = coinsKept.Player.Inventory.CoinSlotStart;
        var inCoinSlot = coinsKept.Player.Inventory.Slots[coinSlot].ItemId == ItemId.Credits;
        var reloaded = GameSimulation.Restore(coinsKept.CaptureSnapshot());
        check(filled && refusesOre && swapRefused && ammoFirst && stayPut && inCoinSlot &&
              reloaded.Player.Inventory.Slots[coinSlot] == coinsKept.Player.Inventory.Slots[coinSlot] &&
              coinsKept.Player.Inventory.SlotCount == GameSimulation.PlayerTotalSlots,
            "credits fill the coin slots and ammunition the ammo slots first; those slots refuse anything else, ammo is spent from them first, sort and deposit leave them alone, and they are saved");

        // ---- G16: Annihilation scaling, defense and relics ----
        var defenseHit = DefenseBalance.ApplyDefense(40f, 20, WorldDifficulty.Annihilation);
        check(WorldSettings.EnemyHealthScale(WorldDifficulty.Annihilation) == 3f && WorldSettings.EnemyDamageScale(WorldDifficulty.Annihilation) == 3f &&
              WorldSettings.EnemyHealthScale(WorldDifficulty.Brutal) == 2f && WorldSettings.EnemyDamageScale(WorldDifficulty.Brutal) == 2f &&
              DefenseBalance.DefenseFactor(WorldDifficulty.Annihilation) == 1f && DefenseBalance.DefenseFactor(WorldDifficulty.Brutal) == 0.75f &&
              defenseHit == 20f && WorldSettings.Name(WorldDifficulty.Annihilation) == "Annihilation",
            "D-6: Annihilation triples creature life and damage and counts defense in full (Brutal doubles them and counts three quarters)");
        var annihilation = NewGame(9308);
        annihilation.Difficulty = WorldDifficulty.Annihilation;
        var bossKind = EnemyKind.AlphaSkitter;
        var relic = PlayerSystemsContent.RelicFor(bossKind);
        var center = annihilation.Player.Motion.Position + annihilation.Player.Motion.Size / 2f;
        var boss = annihilation.SpawnCreature(bossKind, center + new Vector2(10f, -3f));
        boss.TakeDamage(1e7f);
        Tick(annihilation, 2);
        var gotRelic = relic is { } relicId && (annihilation.GroundItems.Any(item => item.ItemId == relicId) || annihilation.Player.Inventory.Count(relicId) > 0);
        check(gotRelic && annihilation.BagReplacesBossLoot(boss),
            "an Annihilation boss leaves its salvage cache and a relic (relics drop only on Annihilation)");

        // ---- 0.1.11: Terraria's clock (a 15-minute day and a 9-minute night); buffs keep real time ----
        var fast = GameSimulation.FastClock;
        try
        {
            GameSimulation.FastClock = false;
            var (day, night) = MeasureCycle(9312);
            var buffClock = new GameSimulation(seed: 9313, width: 80, height: 48);
            buffClock.Player.Statuses.Apply(StatusId.Regeneration, 4f);
            var buffBefore = buffClock.ActiveBuffs.First(buff => buff.Status == StatusId.Regeneration).RemainingSeconds;
            for (var i = 0; i < 40; i++)
                buffClock.Tick(new GameInput(new MovementInput(0f)), 0.25f);
            var after = buffClock.ActiveBuffs.First(buff => buff.Status == StatusId.Regeneration).RemainingSeconds;
            GameSimulation.FastClock = true;
            var (fastDay, fastNight) = MeasureCycle(9314);
            check(MathF.Abs(day - 900f) <= 5f && MathF.Abs(night - 540f) <= 5f && MathF.Abs(buffBefore - 50f) < 0.5f && MathF.Abs(buffBefore - after - 10f) < 0.5f &&
                  MathF.Abs(fastDay + fastNight - 250f) <= 5f,
                $"a day lasts 15 real minutes and a night 9 ({day:0} s and {night:0} s); a buff still runs on real seconds; --fast-clock keeps the old 250 s cycle ({fastDay + fastNight:0} s)");
        }
        finally
        {
            GameSimulation.FastClock = fast;
        }

        // ---- 0.1.12: a bed in a proper room is the spawn point; without it, the landing site ----
        var bedroom = NewGame(9316);
        BuildRoom(bedroom, 20);
        bedroom.World.SetTile(new GridPoint(30, 19), TileId.BunkBed);
        bedroom.Regions.ForceFullRebuild();
        bedroom.Player.Motion.Position = new Vector2(28.6f, 20f - bedroom.Player.Motion.Size.Y);
        bedroom.Player.Motion.Velocity = Vector2.Zero;
        bedroom.TrySleep();
        var bedSet = bedroom.BedSpawn == new GridPoint(30, 19);
        var bedKept = GameSimulation.Restore(bedroom.CaptureSnapshot()).BedSpawn == bedroom.BedSpawn;
        bedroom.Player.Motion.Position = new Vector2(60f, bedroom.Player.Motion.Position.Y);
        bedroom.Player.TakeDamage(1e6f);
        Tick(bedroom, 2);
        bedroom.RespawnPlayer(CharacterDifficulty.Classic);
        var wokeInBed = MathF.Abs((bedroom.Player.Motion.Position.X + bedroom.Player.Motion.Size.X / 2f) - 30.5f) < 1.5f && MathF.Abs((bedroom.Player.Motion.Position.Y + bedroom.Player.Motion.Size.Y / 2f) - 18.5f) < 2f;
        bedroom.World.SetTile(new GridPoint(30, 19), TileId.Air);
        bedroom.Player.TakeDamage(1e6f);
        Tick(bedroom, 2);
        var lostMessage = bedroom.RespawnPlayer(CharacterDifficulty.Classic);
        check(bedSet && bedKept && wokeInBed && lostMessage.Contains("missing or obstructed", StringComparison.Ordinal) && bedroom.BedSpawn is null,
            "using a bed in a proper room sets the spawn point (saved with the world); the player respawns beside it, and at the landing site once it is gone");

        // ---- 0.1.11: moons end at dawn and day events at dusk; outlasting one is not clearing it ----
        var skies = new GameSimulation(seed: 9315, width: 80, height: 48) { FamilySpawnsEnabled = false };
        skies.Flags.Set("boss.stone_sentinel.down");
        skies.Flags.Set("blight.fracture");
        skies.WorldEvents.TryStart(WorldEventKind.RustMoon, skies, forced: true, out _);
        skies.WorldEvents.TryStart(WorldEventKind.DarkSun, skies, forced: true, out _);
        skies.WorldEvents.Advance(1f, null, skies);
        var bothRunning = skies.WorldEvents.IsActive(WorldEventKind.RustMoon) && skies.WorldEvents.IsActive(WorldEventKind.DarkSun);
        skies.WorldEvents.Advance(0f, WorldEventMoment.Dawn, skies);
        var moonGone = !skies.WorldEvents.IsActive(WorldEventKind.RustMoon) && skies.WorldEvents.IsActive(WorldEventKind.DarkSun);
        skies.WorldEvents.Advance(0f, WorldEventMoment.Dusk, skies);
        check(bothRunning && moonGone && !skies.WorldEvents.IsActive(WorldEventKind.DarkSun) &&
              skies.WorldEvents.Completions(WorldEventKind.RustMoon) == 0 && skies.WorldEvents.Completions(WorldEventKind.DarkSun) == 0 &&
              !skies.Flags.IsSet(skies.WorldEvents.Spec(WorldEventKind.RustMoon).FlagName),
            "the Rust Moon ends at dawn and the Dark Sun at dusk, and outlasting them does not count as clearing them");
    }

    /// <summary>Real seconds of day and of night over one whole cycle, dawn to dawn.</summary>
    private static (float Day, float Night) MeasureCycle(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 80, height: 48);
        const float step = 0.25f;
        void Tick()
        {
            game.Player.Heal(game.Player.MaximumHitPoints);
            game.Tick(new GameInput(new MovementInput(0f)), step);
        }
        for (var i = 0; i < 20000 && !game.IsNight; i++)
            Tick();
        for (var i = 0; i < 20000 && game.IsNight; i++)
            Tick();
        float day = 0f, night = 0f;
        for (var i = 0; i < 20000 && !game.IsNight; i++, day += step)
            Tick();
        for (var i = 0; i < 20000 && game.IsNight; i++, night += step)
            Tick();
        return (day, night);
    }
}
