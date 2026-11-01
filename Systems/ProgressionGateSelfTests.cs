using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Progression gates (docs/roadmap/0.1.7.md stream B, 0.2.2): the Abyss Leviathan waits for the Fracture (PC-3), station
/// inheritance is transitive (PC-18), the precursor temple is sealed until the top drills (WT-1, D-35), and no tier 6+
/// item reaches a Classic character before the Fracture (the Astral crate, the Astral fishing rod, the Phase dirk, the
/// Vault core, void darts and void fuel).
/// </summary>
public static class ProgressionGateSelfTests
{
    private const int Left = 10, Top = 4, Right = 60, Floor = 30;

    public static void Run(Action<bool, string> check)
    {
        AbyssLeviathan(check);
        StationInheritance(check);
        TempleSeal(check);
        TierSixGates(check);
        AuditDates(check);
    }

    // ------------------------------------------------------------------ PC-3

    private static void AbyssLeviathan(Action<bool, string> check)
    {
        var game = NewArena(7701);
        var acid = new FishingContext(LiquidKind.Acid, BiomeZone.AcidCoast, FishingDepth.Surface, 100, 40f, false, 0f);
        game.Player.Inventory.Add(ItemId.AbyssBait, 1);
        var random = new Random(3);
        var early = Enumerable.Range(0, 500).Count(_ => game.RollCatch(acid, random).Creature == EnemyKind.AbyssLeviathan);
        var kept = game.Player.Inventory.Count(ItemId.AbyssBait) == 1;
        var hinted = game.RecentMessages.Any(message => message.Contains(GameSimulation.AbyssQuietMessage, StringComparison.Ordinal));
        game.TriggerFracture();
        var after = game.RollCatch(acid with { Fractured = true }, random);
        check(early == 0 && kept && hinted && after.Creature == EnemyKind.AbyssLeviathan && game.Player.Inventory.Count(ItemId.AbyssBait) == 0,
            $"PC-3: abyss bait in acid hooks no Leviathan before the Fracture ({early} in 500 rolls; bait kept: {kept}; hint: {hinted}), " +
            $"and hooks it once the crust has fractured ({after.Creature})");

        var bait = new CraftingSystem(game.Items).FindByOutput(ItemId.AbyssBait).Single();
        check(bait.Ingredients.Any(group => group.Alternatives.Contains(ItemId.FlightEssence)) &&
              bait.Ingredients.All(group => !group.Alternatives.Contains(ItemId.VoidEssence)),
            "PC-3: abyss bait is made with flight essence (after the Fracture), not void essence (coastal hab pods hold it before)");
    }

    // ------------------------------------------------------------------ PC-18

    private static void StationInheritance(Action<bool, string> check)
    {
        var closure = CraftingStationId.AstralAssembler.ImpliedClosure();
        var game = NewArena(7702);
        var at = new GridPoint(PlayerColumn(game) + 2, Floor - 1);
        game.World.SetTile(at, TileId.AstralAssembler);
        var environment = new CraftingEnvironment(game.Player.Motion.Position + game.Player.Motion.Size / 2f, game.World, game.Tiles);
        var nearby = new CraftingSystem(game.Items).GetNearbyStations(environment);
        CraftingStationId[] expected = [CraftingStationId.AstralAssembler, CraftingStationId.Workbench, CraftingStationId.FieldWorkbench,
            CraftingStationId.Furnace, CraftingStationId.OreRefineryMk2, CraftingStationId.Anvil, CraftingStationId.VoidForge];
        check(expected.All(nearby.Contains) && expected.Skip(1).All(closure.Contains) &&
              CraftingStationId.Kiln.ImpliedClosure().Contains(CraftingStationId.Furnace) &&
              CraftingStationId.BioLab.ImpliedClosure().Contains(CraftingStationId.ChemLab) &&
              !closure.Contains(CraftingStationId.AstralAssembler) && !closure.Contains(CraftingStationId.ChemLab),
            $"PC-18: a lone Astral Assembler counts as the Nano-Fabricator, the Ore Refinery and the Ore Refinery Mk2 through the " +
            $"stations it implies (nearby: {string.Join(", ", nearby.OrderBy(id => id))}); the Kiln still counts as the Refinery and " +
            "the Bio Lab as the Chem Lab");
    }

    // ------------------------------------------------------------------ WT-1

    private static void TempleSeal(Action<bool, string> check)
    {
        var game = NewArena(7703);
        var power = ContentTiles.PrecursorBrickToolPower;
        var brick = new GridPoint(PlayerColumn(game) + 2, Floor - 1);
        var glyph = new GridPoint(PlayerColumn(game) - 2, Floor - 1);
        game.World.SetTile(brick, TileId.PrecursorBrick);
        game.World.SetTile(glyph, TileId.PrecursorGlyph);
        Select(game, ItemId.VoidAuger); // the best drill before the Vault Colossus
        Once(game, PlayerAction.Mine, brick);
        var refused = game.World.GetCell(brick) is { TileId: TileId.PrecursorBrick, Damage: 0 } &&
                      game.LastMessage == ContentTiles.PrecursorSealMessage;
        Once(game, PlayerAction.Mine, glyph);
        var glyphRefused = game.World.GetCell(glyph) is { TileId: TileId.PrecursorGlyph, Damage: 0 };
        Select(game, ItemId.AstralDrill);
        MineUntilBroken(game, brick);
        check(power == GearCurve.DrillPowerFromTerraria(210) && game.Tiles[TileId.PrecursorBrick].MinimumToolPower == power &&
              game.Tiles[TileId.PrecursorGlyph].MinimumToolPower == power && game.Items[ItemId.VoidAuger].ToolPower < power &&
              game.Items[ItemId.AstralDrill].ToolPower >= power && refused && glyphRefused &&
              game.World.TileAt(brick.X, brick.Y) == TileId.Air &&
              GameSimulation.IsExplosionProof(TileId.PrecursorBrick) && GameSimulation.IsExplosionProof(TileId.PrecursorGlyph),
            $"WT-1: the precursor temple's brick and glyph need drill power {power} (Terraria's 210): the void auger " +
            $"({game.Items[ItemId.VoidAuger].ToolPower}) is refused with the seal message, the astral drill " +
            $"({game.Items[ItemId.AstralDrill].ToolPower}) breaks it, and explosions never do");
    }

    // ------------------------------------------------------------------ the six tier 6+ leaks

    private static void TierSixGates(Action<bool, string> check)
    {
        // The Astral crate: a legendary water catch, only after the Fracture.
        var random = new Random(5);
        var water = new FishingContext(LiquidKind.Water, BiomeZone.Grassland, FishingDepth.Surface, 100, 60f, false, 0f);
        var before = Enumerable.Range(0, 200).Select(_ => FishingTables.Pick(water, FishingRarity.Legendary, random)).ToHashSet();
        var after = Enumerable.Range(0, 200).Select(_ => FishingTables.Pick(water with { Fractured = true }, FishingRarity.Legendary, random)).ToHashSet();
        var game = NewArena(7704);
        var live = game.GetFishingContext(new GridPoint(PlayerColumn(game), Floor - 2), LiquidKind.Water, 100, 40f);
        check(!before.Contains(ItemId.AstralCrate) && after.Contains(ItemId.AstralCrate) && !live.Fractured,
            $"the Astral crate (tier 6) bites only after the Fracture (legendary water catches before: {string.Join(", ", before)}; " +
            $"after: {string.Join(", ", after)}), and the game's fishing context carries the world's Fracture");

        // The Astral fishing rod: the 30th quest pays it only once the Fracture has happened.
        var warden = game.AddTownNpc(TownNpcKind.TideWarden, game.Player.Motion.Position, bound: false);
        game.Flags.Increment("quests.fishing", GameSimulation.AstralRodQuests - 1);
        bool TurnIn()
        {
            typeof(GameSimulation).GetField("_lastQuestTurnInDay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(game, -1);
            game.Player.Inventory.Add(game.TodaysQuestFish, 1);
            return game.TurnInQuestFish(warden);
        }
        var thirtieth = TurnIn();
        var rodEarly = game.Player.Inventory.Count(ItemId.AstralFishingRod);
        game.TriggerFracture();
        var thirtyFirst = TurnIn();
        var rodAfter = game.Player.Inventory.Count(ItemId.AstralFishingRod);
        TurnIn();
        check(thirtieth && thirtyFirst && rodEarly == 0 && rodAfter == 1 && game.Player.Inventory.Count(ItemId.AstralFishingRod) == 1,
            $"the Astral fishing rod (tier 7) waits for the Fracture: quest 30 before it pays none ({rodEarly}), the first quest after " +
            $"it pays one ({rodAfter}) and only once");

        // The Phase dirk: the vengeful wisp's rare drop waits for the Fracture.
        var dirks = NewArena(7705);
        int Dirks() => dirks.Player.Inventory.Count(ItemId.PhaseDirk) + dirks.GroundItems.Where(item => item.ItemId == ItemId.PhaseDirk).Sum(item => item.Count);
        void KillWisps(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var wisp = dirks.SpawnCreature(EnemyKind.VengefulWisp, dirks.Player.Motion.Position + new Vector2(i % 2 == 0 ? 12f : -12f, -3f));
                wisp.TakeDamage(1e7f);
                Idle(dirks, 1);
            }
        }
        KillWisps(300);
        var dirkEarly = Dirks();
        dirks.TriggerFracture();
        KillWisps(300);
        check(ArsenalItems.MobDropWaitsForFracture(ItemId.PhaseDirk) && dirkEarly == 0 && Dirks() > 0,
            $"the Phase dirk (tier 6) never drops from vengeful wisps before the Fracture ({dirkEarly} in 300 kills) and does after it " +
            $"({Dirks()} in 300 kills)");

        // The Vault core, void darts and void fuel: a post-Fracture input in the recipe.
        var crafting = new CraftingSystem(game.Items);
        bool Needs(ItemId output, ItemId input) => crafting.FindByOutput(output).All(recipe =>
            recipe.Ingredients.Any(group => group.Alternatives.All(alternative => alternative == input)));
        check(Needs(ItemId.VaultCore, ItemId.LumeniteBar) && Needs(ItemId.VoidDarts, ItemId.BlightEssence) && Needs(ItemId.VoidFuel, ItemId.BlightEssence),
            "the Vault core needs lumenite bars, and void darts and void fuel need blight essence: inputs that only exist after the Fracture");
    }

    // ------------------------------------------------------------------ the audit dates them

    private static void AuditDates(Action<bool, string> check)
    {
        var audit = CraftingAudit.Run();
        var gated = CraftingAudit.RunGated();
        int Stage(CraftingAudit.Result result, ItemId item) => result.Items.TryGetValue(item, out var reach) ? reach.Stage : int.MaxValue;
        int Defeat(CraftingAudit.Result result, EnemyKind boss) => result.Stages.ToList().IndexOf("Defeated " + EnemyArchetypes.Get(boss).DisplayName);
        ItemId[] six = [ItemId.AstralCrate, ItemId.AstralFishingRod, ItemId.PhaseDirk, ItemId.VaultCore, ItemId.VoidDarts, ItemId.VoidFuel];
        var early = six.Where(item => Stage(audit, item) < audit.FractureStage || Stage(gated, item) < gated.FractureStage).ToArray();
        check(early.Length == 0 && six.All(item => !CraftingAudit.EarlyGearWhitelist.ContainsKey(item)),
            $"--craft-audit dates the Astral crate, Astral fishing rod, Phase dirk, Vault core, void darts and void fuel at or after the " +
            $"Fracture and none needs the early-gear whitelist (early: {string.Join(", ", early)}; " +
            $"{string.Join(", ", six.Select(item => $"{item}@{audit.StageOf(item)}"))})");

        var leviathanLoot = audit.Items.Where(entry => entry.Value.Via == "defeat " + EnemyArchetypes.Get(EnemyKind.AbyssLeviathan).DisplayName)
            .Select(entry => entry.Key).ToArray();
        check(audit.BossReach.TryGetValue(EnemyKind.AbyssLeviathan, out var reach) && reach >= audit.FractureStage &&
              Defeat(audit, EnemyKind.AbyssLeviathan) > audit.FractureStage && Stage(audit, ItemId.AbyssBait) >= audit.FractureStage &&
              leviathanLoot.Length > 0 && leviathanLoot.All(item => Stage(audit, item) > audit.FractureStage),
            $"PC-3: the audit reaches the Abyss Leviathan only after the Fracture (reach {reach}, Fracture {audit.FractureStage}) and " +
            $"dates its drops after it ({string.Join(", ", leviathanLoot)})");

        var colossus = Defeat(audit, EnemyKind.VaultColossus);
        var drillsBefore = audit.Items.Where(entry => entry.Value.Stage <= colossus && audit.Catalog[entry.Key].UseKind == ItemUseKind.Pickaxe)
            .Select(entry => audit.Catalog[entry.Key].ToolPower).DefaultIfEmpty(0).Max();
        check(Stage(audit, ItemId.PrecursorBrickBlock) > colossus && drillsBefore < ContentTiles.PrecursorBrickToolPower &&
              audit.Stages.Contains("Drill power " + ContentTiles.PrecursorBrickToolPower),
            $"WT-1: no drill before the Vault Colossus breaks the temple brick (best {drillsBefore} < {ContentTiles.PrecursorBrickToolPower}), " +
            $"so the audit dates the brick after it ({audit.StageOf(ItemId.PrecursorBrickBlock)}; Colossus at stage {colossus})");
    }

    // ------------------------------------------------------------------ helpers (as in ParityBuildTests)

    private static GameSimulation NewArena(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 80, height: 48) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        for (var y = Top; y <= Floor + 1; y++)
        for (var x = Left; x <= Right; x++)
        {
            var point = new GridPoint(x, y);
            game.World.SetTile(point, y >= Floor ? TileId.Rock : TileId.Air);
            var cell = game.World.GetCell(point);
            cell.LiquidDepth = 0f;
            cell.LiquidKind = LiquidKind.None;
            cell.WallId = WallIds.None;
            cell.Flags = 0;
            game.World.SetCell(point, cell);
        }
        game.Player.Motion.Position = new Vector2(30f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        Idle(game, 2);
        return game;
    }

    private static int PlayerColumn(GameSimulation game) => (int)MathF.Floor(game.Player.Motion.Position.X + game.Player.Motion.Size.X / 2f);

    private static void Idle(GameSimulation game, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f)));
    }

    private static void Select(GameSimulation game, ItemId item)
    {
        if (game.Player.Inventory.Count(item) == 0)
            game.Player.Inventory.Add(item, 1);
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
    }

    private static void Once(GameSimulation game, PlayerAction action, GridPoint target)
    {
        while (game.Player.ActionCooldownTicks > 0)
            Idle(game, 1);
        game.Tick(new GameInput(new MovementInput(0f), action, target));
    }

    private static void MineUntilBroken(GameSimulation game, GridPoint target, int maxTicks = 2400)
    {
        for (var i = 0; i < maxTicks && game.World.TileAt(target.X, target.Y) != TileId.Air; i++)
            game.Tick(new GameInput(new MovementInput(0f), PlayerAction.Mine, target));
    }
}
