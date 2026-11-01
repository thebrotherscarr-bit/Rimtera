using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The station AI powers the base (0.3.8; 0.7.5 the power grid): every station crafts unpowered; machines run powered when
/// the AI's tier reaches theirs, drawing its power; fuel crystals refill it and raise the tier; hand-built stations need
/// nothing; losing the AI leaves the machines unpowered until an AI core rebuilds it.
/// </summary>
public static class StationPowerSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Site(int seed, CharacterDifficulty difficulty = CharacterDifficulty.Classic)
    {
        var profile = CharacterDraft.CreateRandom(seed, "Keeper").ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow) with { Difficulty = difficulty };
        var game = new GameSimulation(seed: seed, width: 120, height: 56, colonist: profile) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y >= Floor ? TileId.Rock : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            game.World.SetCell(cell, data);
        }
        game.Player.Motion.Position = new Vector2(20f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        game.World.SetTile(new GridPoint(22, Floor - 1), TileId.Workbench);
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- 0.7.5: a new world's AI runs on its landing charge at tier 0; every station crafts unpowered, drawing nothing ----
        var game = Site(9680);
        var startPower = game.StationPower;
        var circuit = game.Crafting.FindByOutput(ItemId.CircuitBoard).First();
        game.Player.Inventory.Add(ItemId.CopperBar, 4);
        game.Player.Inventory.Add(ItemId.EnergyCrystal, 2);
        var crafted = game.TryCraft(circuit.Id).Success;
        var afterUnpowered = game.StationPower;
        var core = game.Crafting.FindByOutput(ItemId.AiCore).First();
        check(startPower == GameSimulation.StartPower && game.StationPowerTier == 0 && crafted && afterUnpowered == startPower &&
              GameSimulation.PowerCost(core) == 0 && GameSimulation.PowerTierOf(CraftingStationId.Sawmill) == 0 &&
              GameSimulation.PowerTierOf(CraftingStationId.AstralAssembler) == 5,
            $"a new world's AI holds {GameSimulation.StartPower} power at tier 0; a machine crafts unpowered and draws nothing ({startPower} → {afterUnpowered}); " +
            "hand-built stations and the AI core need no power");

        // ---- the fuel ladder: a tier's crystal raises the AI to it, and its machines run powered, drawing their tier ----
        var mk2Recipe = game.Crafting.FindByOutput(ItemId.OreRefineryMk2).First();
        var needsTier = game.PowerLock(mk2Recipe);
        game.Player.Inventory.Add(ItemId.FuelCrystalT2, 1);
        var fed = game.FeedStationAi();
        var tierTwo = game.StationPowerTier == 2 && game.PowerLock(mk2Recipe) is null;
        game.Player.Inventory.Add(ItemId.CopperBar, 4);
        game.Player.Inventory.Add(ItemId.EnergyCrystal, 2);
        var poweredBefore = game.StationPower;
        game.TryCraft(circuit.Id);
        var drew = poweredBefore - game.StationPower;
        new DevConsole(game).Execute("power set 0");
        var empty = game.PowerLock(circuit);
        game.Player.Inventory.Add(ItemId.CopperBar, 4);
        game.Player.Inventory.Add(ItemId.EnergyCrystal, 2);
        var stillCrafts = game.TryCraft(circuit.Id).Success;
        game.Player.Inventory.Add(ItemId.CrudeFuelCrystal, 5);
        game.FeedStationAi();
        check(needsTier is { } tierText && tierText.Contains("tier 2") && fed.Contains("Power tier 2") && tierTwo && drew == 1 &&
              empty is { } emptyText && emptyText.Contains("out of power") && stillCrafts && game.StationPower == 5 * 10 &&
              game.PowerLock(circuit) is null && game.StationPowerTier == 2,
            $"a tier's fuel crystal raises the AI to it (\"{needsTier}\"), and a powered machine draws its tier ({drew}); out of power, machines still craft, unpowered; crude crystals recharge without raising the tier");

        // ---- losing the AI: no longer the end; machines dark, research stops, an AI core rebuilds it ----
        var colony = Site(9681);
        var ai = colony.AddTownNpc(TownNpcKind.StationAi, new Vector2(30f, Floor - 3f), bound: false);
        new DevConsole(colony).Execute("defeat StoneSentinel");
        colony.HurtTownNpc(ai, 1e9f);
        var down = !colony.StationAiOnline && !colony.RunOver && colony.PowerLock(circuit) is { } dark && dark.Contains("AI core");
        var before = colony.ResearchProgress("overseer");
        for (var i = 0; i < 300; i++)
            colony.Tick(new GameInput(new MovementInput(0f)));
        var stalled = Math.Abs(colony.ResearchProgress("overseer") - before) < 0.01f;
        var rebuilt = colony.RebuildStationAi() && colony.StationAiOnline && colony.StationPower >= GameSimulation.RebuiltPower &&
                      colony.TownNpcs.Any(npc => npc.Kind == TownNpcKind.StationAi && !npc.IsBound) &&
                      colony.PowerLock(circuit)?.Contains("AI core") != true;
        var explorer = Site(9682, CharacterDifficulty.Explorer);
        var explorerAi = explorer.AddTownNpc(TownNpcKind.StationAi, new Vector2(30f, Floor - 3f), bound: false);
        explorer.HurtTownNpc(explorerAi, 1e9f);
        check(down && stalled && rebuilt && explorer.StationAiOnline && !explorer.RunOver,
            "losing the station AI no longer ends the run: the machines run unpowered and research stops until an AI core rebuilds it (on Explorer it simply comes back)");

        // ---- saves keep power; a world saved before 0.3.8 starts full at the tier its progress uses ----
        var kept = Site(9683);
        new DevConsole(kept).Execute("power set 412");
        new DevConsole(kept).Execute("power tier 3");
        var reloaded = GameSimulation.Restore(kept.CaptureSnapshot());
        var old = Site(9684);
        new DevConsole(old).Execute("defeat StoneSentinel");
        var snapshot = old.CaptureSnapshot();
        var legacy = GameSimulation.Restore(snapshot with
        {
            Content = snapshot.Content! with
            {
                Flags = snapshot.Content!.Flags?.Where(pair => !pair.Key.StartsWith("ai.", StringComparison.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value)
            }
        });
        check(reloaded.StationPower == 412 && reloaded.StationPowerTier == 3 && legacy.StationPower == GameSimulation.MaxPower && legacy.StationPowerTier == 2,
            $"power and tier are saved (412, tier 3); an older save with the Overseer down starts full at tier 2 ({legacy.StationPower}, tier {legacy.StationPowerTier})");
    }
}
