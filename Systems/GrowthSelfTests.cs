using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Self-tests for character growth (docs/PROGRESSION_DIFF.md G3, G7-G11): lifebloom pods and their post-Fracture growth,
/// the auxiliary socket and the Annihilation sixth slot, permanent upgrades, glide rigs versus climbing wings, the hook
/// ladder and the Creative colony supply drop.
/// </summary>
public static class GrowthSelfTests
{
    private static GameSimulation NewGame(int seed, CharacterProfile? profile = null)
    {
        var game = new GameSimulation(seed: seed, width: 80, height: 48, colonist: profile) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e7f);
        return game;
    }

    private static void Tick(GameSimulation game, int ticks = 1, PlayerAction action = PlayerAction.None, GridPoint? target = null)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f), i == 0 ? action : PlayerAction.None, target));
    }

    private static void Use(GameSimulation game, ItemId item)
    {
        game.Player.Inventory.Add(item, 1);
        game.Player.ActionCooldownTicks = 0;
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
        Tick(game, 1, PlayerAction.UseSelected, new GridPoint(1, 1));
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- G7: lifebloom pods ----
        var life = NewGame(9101);
        var baseMax = life.Player.MaximumHitPoints;
        Use(life, ItemId.LifebloomPod);
        var refusedEarly = life.LifebloomPodsUsed == 0;
        life.Player.LifeCoresUsed = PlayerCharacter.MaximumLifeCores;
        for (var i = 0; i < GrowthItems.MaximumLifeblooms + 1; i++)
            Use(life, ItemId.LifebloomPod);
        check(refusedEarly && life.LifebloomPodsUsed == GrowthItems.MaximumLifeblooms &&
              Math.Abs(life.Player.MaximumHitPoints - (baseMax + 100f + 100f)) < 0.01f && life.Player.Inventory.Count(ItemId.LifebloomPod) == 2,
            "lifebloom pods need every life core first and cap at +100 (300 total)");
        var lifeCopy = NewGame(9101);
        lifeCopy.ApplyCharacter(life.CaptureCharacter());
        var lifeLoaded = GameSimulation.Restore(life.CaptureSnapshot(), life.PlayerProfile);
        check(lifeCopy.LifebloomPodsUsed == 20 && lifeLoaded.LifebloomPodsUsed == 20 &&
              Math.Abs(lifeLoaded.Player.MaximumHitPoints - life.Player.MaximumHitPoints) < 0.01f,
            "lifebloom pods persist in the character file and the world snapshot");

        var grid = new TileGrid(40, 40);
        for (var x = 0; x < 40; x++)
            grid.SetTile(new GridPoint(x, 30), TileId.Mud);
        var ticker = new WorldTicker(grid, new TileCatalog(), 5);
        int Pods() => Enumerable.Range(0, 40).Count(x => grid.TileAt(x, 29, TileId.Air) == TileId.LifebloomPod);
        for (var i = 0; i < 400; i++)
            ticker.UpdateCell(i % 40, 30);
        var podsBefore = Pods();
        ticker.Fractured = true;
        for (var i = 0; i < 4000; i++)
            ticker.UpdateCell(i * 7 % 40, 30);
        var podXs = Enumerable.Range(0, 40).Where(x => grid.TileAt(x, 29, TileId.Air) == TileId.LifebloomPod).ToArray();
        check(podsBefore == 0 && podXs.Length >= 1 && podXs.Zip(podXs.Skip(1)).All(pair => pair.Second - pair.First > 10) && ContentTiles.GetDrop(TileId.LifebloomPod) == ItemId.LifebloomPod,
            "lifebloom pods grow on underground canopy mud only after the Fracture, spaced apart, and drop the pod");

        // ---- G8: auxiliary socket / Annihilation sixth slot ----
        var slots = NewGame(9102);
        var accessories = new[] { ItemId.LifeMonitor, ItemId.DepthMeter, ItemId.CompassChip, ItemId.MinersGauntlet, ItemId.NovaWings, ItemId.HeatWard };
        foreach (var id in accessories.Take(5))
            Use(slots, id);
        var lockedSixth = !slots.AccessorySlot6Unlocked && slots.Player.Equipment[EquipmentSlot.Accessory6] is null &&
                          slots.ActiveAccessorySlots.Count == 5;
        var defenseBefore = slots.Player.Stats.Defense;
        Use(slots, ItemId.AuxiliarySocket);
        Use(slots, ItemId.HeatWard);
        check(lockedSixth && slots.AuxSocketUsed && slots.AccessorySlot6Unlocked && slots.ActiveAccessorySlots.Count == 6 &&
              slots.Player.Equipment[EquipmentSlot.Accessory6] == ItemId.HeatWard && slots.Player.Stats.Defense >= defenseBefore + 2,
            "the auxiliary socket opens a sixth accessory slot that equips and counts toward stats");
        var slotsLoaded = GameSimulation.Restore(slots.CaptureSnapshot(), slots.PlayerProfile);
        var slotsCopy = NewGame(9102);
        slotsCopy.ApplyCharacter(slots.CaptureCharacter());
        check(slotsLoaded.AuxSocketUsed && slotsLoaded.Player.Equipment[EquipmentSlot.Accessory6] == ItemId.HeatWard &&
              slotsCopy.AuxSocketUsed && slotsCopy.Player.Equipment[EquipmentSlot.Accessory6] == ItemId.HeatWard,
            "the auxiliary socket and the sixth slot's accessory persist (snapshot and character file)");
        var annihilation = NewGame(9103);
        annihilation.Difficulty = WorldDifficulty.Annihilation;
        foreach (var id in accessories)
            Use(annihilation, id);
        check(annihilation.AccessorySlot6Unlocked && !annihilation.AuxSocketUsed &&
              annihilation.Player.Equipment[EquipmentSlot.Accessory6] == ItemId.HeatWard,
            "an Annihilation world opens the sixth accessory slot for free");
        var tyrant = NewGame(9104);
        tyrant.Difficulty = WorldDifficulty.Brutal;
        tyrant.SpawnCreature(EnemyKind.FissionTyrant, tyrant.Player.Motion.Position + new Vector2(6f, -2f)).TakeDamage(1e7f);
        Tick(tyrant, 3);
        tyrant.Player.Heal(1e6f);
        check(tyrant.GroundItems.Any(drop => drop.ItemId == ItemId.AuxiliarySocket) || tyrant.Player.Inventory.Count(ItemId.AuxiliarySocket) > 0,
            "the Fission Tyrant leaves an auxiliary socket on Brutal");

        // ---- G9: permanent upgrades ----
        var up = NewGame(9105);
        var stats0 = up.Player.Stats;
        var fish0 = up.GearFishingBonus;
        var mining0 = up.GearMiningFactor;
        var light0 = up.PlayerLightBonus;
        foreach (var item in GrowthItems.UpgradeItems.Values)
            Use(up, item);
        Use(up, ItemId.NaniteMarrow);
        var stats1 = up.Player.Stats;
        check(up.PermanentUpgrades == (PermanentUpgrade)0x7F && stats1.Defense == stats0.Defense + 4 &&
              stats1.ManaRegenerationPerHour > stats0.ManaRegenerationPerHour && up.GearFishingBonus > fish0 &&
              up.GearMiningFactor > mining0 && up.PlayerLightBonus > light0 && up.CraftingStationReachBonus > 0f &&
              up.GrowthLuckBonus > 0f && up.LumenAccordActive && up.LumenAccordTint is not null &&
              up.Player.Inventory.Count(ItemId.NaniteMarrow) == 1,
            "each permanent upgrade applies once (defense, charge regen, fishing, mining, light, station reach, luck)");
        var upCopy = NewGame(9105);
        upCopy.ApplyCharacter(up.CaptureCharacter());
        var upLoaded = GameSimulation.Restore(up.CaptureSnapshot(), up.PlayerProfile);
        check(upCopy.PermanentUpgrades == up.PermanentUpgrades && upLoaded.PermanentUpgrades == up.PermanentUpgrades &&
              upCopy.Player.Stats.Defense == stats1.Defense,
            "permanent upgrades persist in the character file and the world snapshot");

        // ---- G3: glide rigs versus climbing wings ----
        float FuelWith(ItemId wings)
        {
            var game = NewGame(9106);
            Use(game, wings);
            return game.MaximumRocketFuel;
        }
        var catalog = new ItemCatalog();
        check(FuelWith(ItemId.FerriteWingpack) == 0f && FuelWith(ItemId.ScrapWings) == 0f &&
              catalog[ItemId.FerriteWingpack].ImmunityTags.Contains("glide-rig") && catalog[ItemId.FerriteWingpack].ImmunityTags.Contains("featherfall") &&
              FuelWith(ItemId.ViridiumWingpack) > 0f && FuelWith(ItemId.SalvagedThrusterFins) > 0f &&
              FuelWith(ItemId.SalvagedThrusterFins) < FuelWith(ItemId.ViridiumWingpack),
            "pre-Fracture wings are glide rigs with no climb; essence wings and the rare thruster fins climb");

        // ---- G10: hook ladder ----
        var ladder = GrowthItems.HookLadder;
        var hooks = NewGame(9107);
        Use(hooks, ItemId.ScrapTether);
        var scrapSpec = hooks.ActiveHook;
        hooks.TryFireGrapple(hooks.Player.Motion.Position + new Vector2(0.3f, -40f));
        Tick(hooks, 90);
        var scrapGone = hooks.Grapple is null;
        Use(hooks, ItemId.PhaseTether);
        check(ladder.Count == 4 && ladder.Select(spec => spec.Range).Distinct().Count() == 4 &&
              ladder.Zip(ladder.Skip(1)).All(pair => pair.Second.Range > pair.First.Range) &&
              GrowthItems.HookFor(ItemId.TwinLash)!.HookCount == 2 && GrowthItems.HookFor(ItemId.PhaseTether)!.InstantRetract &&
              scrapSpec?.Item == ItemId.ScrapTether && scrapGone && hooks.ActiveHook?.Item == ItemId.PhaseTether &&
              new CraftingSystem(catalog).Recipes.Any(recipe => recipe.Output == ItemId.TwinLash),
            "the hook ladder has rising range, the grapple reads the equipped hook, and the twin lash is craftable");

        // ---- G11: Creative colony supply drop ----
        var previous = CharacterFileStore.Active;
        var directory = Path.Combine(Path.GetTempPath(), "rimtera-growth-" + Guid.NewGuid().ToString("N"));
        try
        {
            CharacterFileStore.Active = new CharacterFileStore(directory);
            var profile = CharacterDraft.CreateRandom(9108, "Planner").ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow) with
                { Difficulty = CharacterDifficulty.Creative };
            var first = NewGame(9108, profile);
            var granted = first.CreativeKitGranted && first.Player.Inventory.Count(ItemId.RecallCapsule) == 3 &&
                          first.Player.Inventory.Count(ItemId.CobaltDrill) == 1 && first.Player.Inventory.Count(ItemId.GlowSticks) == 100;
            first.SaveCharacterFile();
            var second = NewGame(9109, profile);
            var plain = NewGame(9110);
            check(granted && second.CreativeKitGranted && second.Player.Inventory.Count(ItemId.RecallCapsule) == 3 &&
                  !plain.CreativeKitGranted && plain.Player.Inventory.Count(ItemId.RecallCapsule) == 0,
                "a Creative character gets the colony supply drop once (not again in a second world); others never");
        }
        finally
        {
            CharacterFileStore.Active = previous;
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
