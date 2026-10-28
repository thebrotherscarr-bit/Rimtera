using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The crafting progression (docs/CRAFTING_PROGRESSION.md, `--craft-audit`): a fresh Classic character fells a tree and
/// crafts the first station by hand; every station and every item outside <see cref="CraftingAudit.DeliberateExceptions"/>
/// (and the difficulty-only boss rewards <see cref="CraftingAudit.ExceptionFor"/> names) is obtainable; no material is a dead
/// end; no recipe arrives later than its tier; and the stations come in Terraria's order around the existing gates.
/// </summary>
public static class CraftingAuditSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        // ---- The opening, in a real game: chop a tree, then craft the Nano-Fabricator by hand ----
        var previous = CharacterFileStore.Active;
        CharacterFileStore.Active = null;
        try
        {
            var profile = CharacterDraft.CreateRandom(7310, "Fresh").ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow) with
                { Difficulty = CharacterDifficulty.Classic };
            var game = new GameSimulation(seed: 7310, width: 120, height: 60, colonist: profile) { FamilySpawnsEnabled = false };
            var kitHasCutter = game.Player.Inventory.Slots.Any(stack => !stack.IsEmpty && game.Items[stack.ItemId].UseKind == ItemUseKind.Cutter);
            var noStationInKit = !game.Player.Inventory.Slots.Any(stack => !stack.IsEmpty &&
                                                                          game.Items[stack.ItemId].PlacedTile is var tile and not TileId.Air &&
                                                                          game.Tiles.Contains(tile) && game.Tiles[tile].Station != CraftingStationId.None);
            // Grow one tree on a grass patch beside the landing site, then fell it at the base with the starting hatchet's power.
            var x = (int)game.Player.Motion.Position.X + 6;
            var ground = 0;
            while (ground < game.World.Height - 1 && !game.Tiles[game.World.TileAt(x, ground)].IsSolid)
                ground++;
            ground = Math.Clamp(ground, 22, game.World.Height - 3);
            for (var dx = -6; dx <= 6; dx++)
            for (var y = ground - 20; y <= ground + 1; y++)
            {
                var point = new GridPoint(x + dx, y);
                game.World.SetTile(point, y == ground ? TileId.Grass : y > ground ? TileId.Rock : TileId.Air);
                var cell = game.World.GetCell(point);
                cell.LiquidDepth = 0f;
                cell.LiquidKind = LiquidKind.None;
                game.World.SetCell(point, cell);
            }
            var grew = TilePhysics.TryGrowTree(game.World, game.Tiles, x, ground - 1, new Random(3), null, 8, 8);
            var before = game.Player.Inventory.Count(ItemId.Timber);
            game.ForceBreakTile(new GridPoint(x, ground - 1));
            var timber = game.Player.Inventory.Count(ItemId.Timber) - before +
                         game.GroundItems.Where(item => item.ItemId == ItemId.Timber).Sum(item => item.Count);
            foreach (var loose in game.GroundItems.Where(item => item.ItemId == ItemId.Timber).ToArray())
                game.Player.Inventory.Add(ItemId.Timber, loose.Count);
            var felled = grew ? 1 : 0;
            var fabricator = game.TryCraft("workbench");
            game.Player.Inventory.Add(ItemId.Timber, 3);
            var panel = game.TryCraft("hull-panel");
            check(kitHasCutter && noStationInKit && felled > 0 && timber >= 10 && fabricator.Success && game.Player.Inventory.Count(ItemId.WorkbenchBlock) == 1 &&
                  panel.Success && game.Player.Inventory.Count(ItemId.WoodBlock) == 1,
                $"a fresh Classic character fells a tree ({felled} grown, {timber} timber) and crafts the Nano-Fabricator by hand from 10 timber, " +
                $"and presses timber into hull panels by hand ({fabricator.Message} / {panel.Message})");
        }
        finally
        {
            CharacterFileStore.Active = previous;
        }

        // ---- The opening, as a fixed point over the recipe data ----
        var opening = CraftingAudit.RunOpening();
        check(opening.StationsFromTree.Contains(CraftingStationId.Workbench) && opening.FromTree.Contains(ItemId.WorkbenchBlock) &&
              opening.FromTree.Contains(ItemId.WoodBlock) && opening.FromTree.Contains(ItemId.PlankPlatform),
            $"the kit plus one felled tree reaches the Nano-Fabricator, hull panels and platforms (stations: {string.Join(", ", opening.StationsFromTree)})");
        CraftingStationId[] firstStations = [CraftingStationId.Workbench, CraftingStationId.Furnace, CraftingStationId.Anvil];
        check(firstStations.All(opening.StationsFromSurface.Contains) && opening.FromSurface.Contains(ItemId.WallTorch) &&
              opening.FromSurface.Contains(ItemId.CopperBar) && opening.FromSurface.Contains(ItemId.FerriteBar),
            $"with surface digging too, the Ore Refinery and Power Forge follow, with torches and bars (Terraria: work bench, furnace, anvil; " +
            $"stations: {string.Join(", ", opening.StationsFromSurface)})");

        // ---- The whole progression ----
        var audit = CraftingAudit.Run();
        var items = audit.Catalog;
        check(audit.Unreachable.Count == 0 && audit.OnlyAfterBlocked.Count == 0,
            $"every item is obtainable from a fresh Classic character except the deliberate exceptions " +
            $"({audit.Unreachable.Count} unreachable: {string.Join(", ", audit.Unreachable.Take(12))}; behind blocked milestones: " +
            $"{string.Join(", ", audit.OnlyAfterBlocked.Take(12))})");
        check(audit.UnbuildableStations.Count == 0 && audit.BlockedMilestones.Count == 0 &&
              Enum.GetValues<CraftingStationId>().Where(station => station != CraftingStationId.None).All(audit.Stations.ContainsKey),
            $"every crafting station can be built and every progression milestone is reached " +
            $"(unbuildable: {string.Join(", ", audit.UnbuildableStations)}; blocked: {string.Join(", ", audit.BlockedMilestones)})");
        check(audit.DeadEnds.Count == 0 && audit.Cycles.Count == 0 && audit.LateRecipes.Count == 0,
            $"no material is a dead end, no recipe chain only feeds itself, and no recipe arrives later than its tier " +
            $"(dead ends: {string.Join(", ", audit.DeadEnds)}; cycles: {string.Join(" | ", audit.Cycles)}; late: " +
            $"{string.Join(", ", audit.LateRecipes.Select(late => late.RecipeId))})");

        // The exceptions are explicit and limited to difficulty-only boss rewards.
        var excepted = items.All.Where(item => CraftingAudit.ExceptionFor(item.Id, items) is not null).Select(item => item.Id).ToArray();
        var bosses = EnemyArchetypes.All.Where(archetype => archetype.IsBoss).Select(archetype => archetype.Kind).ToArray();
        var expected = bosses.SelectMany(boss => new[] { PlayerSystemsContent.BagFor(boss), PlayerSystemsContent.ExclusiveFor(boss), PlayerSystemsContent.RelicFor(boss) })
            .OfType<ItemId>().Where(items.Contains).Append(ItemId.AuxiliarySocket).Concat(CraftingAudit.DeliberateExceptions.Keys).ToHashSet();
        check(excepted.ToHashSet().SetEquals(expected) && CraftingAudit.DeliberateExceptions.Count == 0 &&
              CraftingAudit.DeliberateDeadEnds.Keys.All(id => items[id].Value > 0),
            $"the only exceptions are Brutal/Annihilation boss rewards (salvage caches, their core-chips, relics, the auxiliary socket): " +
            $"{excepted.Length} items; deliberate dead ends are sellable fishing junk ({string.Join(", ", CraftingAudit.DeliberateDeadEnds.Keys)})");

        // ---- Stations in Terraria's order, around the existing gates ----
        int Stage(CraftingStationId station) => audit.Stations.GetValueOrDefault(station, int.MaxValue);
        int StageNamed(string name) => audit.Stages.ToList().FindIndex(stage => stage == name) is var at and >= 0 ? at : int.MaxValue;
        var fracture = StageNamed("Defeated " + EnemyArchetypes.Get(GameSimulation.CoreGateBoss).DisplayName);
        var lumeniteGate = StageNamed("Drill power " + new TileCatalog()[TileId.LumeniteOre].MinimumToolPower);
        var astroGod = StageNamed("Defeated " + EnemyArchetypes.Get(EnemyKind.AstroGod).DisplayName);
        var cantor = StageNamed("Defeated " + EnemyArchetypes.Get(GameSimulation.RitualBoss).DisplayName);
        check(Stage(CraftingStationId.Workbench) < Stage(CraftingStationId.Furnace) && Stage(CraftingStationId.Furnace) <= Stage(CraftingStationId.Anvil) &&
              Stage(CraftingStationId.Anvil) < fracture && Stage(CraftingStationId.TinkerBench) < fracture &&
              Stage(CraftingStationId.AlloyAnvil) > fracture && Stage(CraftingStationId.NanoFabricatorMk2) > Stage(CraftingStationId.AlloyAnvil) &&
              Stage(CraftingStationId.VoidForge) > Stage(CraftingStationId.NanoFabricatorMk2) &&
              Stage(CraftingStationId.AstralAssembler) > Stage(CraftingStationId.VoidForge) && Stage(CraftingStationId.AstralAssembler) > cantor &&
              fracture < lumeniteGate && lumeniteGate < astroGod,
            $"stations come in Terraria's order: Nano-Fabricator, Ore Refinery, Power Forge before the Fracture; Alloy Anvil, Nano-Fabricator Mk2, " +
            $"Void Forge after it, and the Astral Assembler (Ancient Manipulator) after the Void Cantor (cultist analog) (stages {Stage(CraftingStationId.Workbench)}/{Stage(CraftingStationId.Furnace)}/" +
            $"{Stage(CraftingStationId.Anvil)} | Fracture {fracture} | {Stage(CraftingStationId.AlloyAnvil)}/{Stage(CraftingStationId.NanoFabricatorMk2)}/" +
            $"{Stage(CraftingStationId.VoidForge)}/{Stage(CraftingStationId.AstralAssembler)}, Void Cantor {cantor})");

        // The ore and Fracture gates hold: no lumenite, voidsteel or astral material before the Fracture.
        int ItemStage(ItemId item) => audit.Items.TryGetValue(item, out var reach) ? reach.Stage : int.MaxValue;
        ItemId[] hardmode = [ItemId.LumeniteOre, ItemId.LumeniteBar, ItemId.VoidsteelOre, ItemId.VoidsteelBar, ItemId.AstralOre, ItemId.AstralBar,
            ItemId.FissionSlag, ItemId.ViridiumDrill, ItemId.LumeniteDrill, ItemId.AstralDrill];
        check(hardmode.All(item => ItemStage(item) >= fracture) && ItemStage(ItemId.AstralBar) > lumeniteGate &&
              FishingTables.CrateLoot(ItemId.AstralCrate, fractured: false).All(entry => !FishingTables.FractureCrateLoot.Contains(entry.Item)) &&
              FishingTables.CrateLoot(ItemId.AstralCrate, fractured: true).Any(entry => entry.Item == ItemId.AstralOre),
            $"no lumenite, voidsteel or astral material (and no hardmode drill) is obtainable before the Fracture; crates hold hardmode bars only " +
            $"after it ({string.Join(", ", hardmode.Select(item => $"{item}@{audit.StageOf(item)}"))})");
    }
}
