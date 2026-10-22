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
              WorldSettings.Name(CharacterDifficulty.Salvager) == "Salvager" &&
              salvage.GetRespawnDelay(CharacterDifficulty.Salvager) == salvage.GetRespawnDelay(CharacterDifficulty.Classic),
            $"a Salvager death drops all {before} carried items (not worn gear) and 40% of credits at the death site; walking back recovers {recovered}");

        // ---- G16: Annihilation scaling, defense and relics ----
        var defenseHit = DefenseBalance.ApplyDefense(40f, 20, WorldDifficulty.Annihilation);
        check(WorldSettings.EnemyScale(WorldDifficulty.Annihilation) == 2f && WorldSettings.EnemyScale(WorldDifficulty.Brutal) == 1.4f &&
              DefenseBalance.DefenseFactor(WorldDifficulty.Annihilation) == 1f && DefenseBalance.DefenseFactor(WorldDifficulty.Brutal) == 0.75f &&
              defenseHit == 20f && WorldSettings.Name(WorldDifficulty.Annihilation) == "Annihilation",
            "Annihilation doubles creature strength and counts defense in full (Brutal stays x1.4 and three quarters)");
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
    }
}
