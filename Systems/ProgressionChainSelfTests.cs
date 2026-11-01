using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Self-tests for the boss/ore progression-chain fixes (docs/PROGRESSION_DIFF.md gaps G0, G1, G2, G4, G5, G6, G12):
/// Creative death credits, the pre-Fracture radiated-heart avenger, Fracture gates on hard-tier optional bosses and
/// dusk incursions, the altar-breaker capability and the hardmode ore ladder, the Astral sigil's obelisk gate, the
/// Overseer's natural patrol, and the Scout Mech dungeon gate.
/// </summary>
public static class ProgressionChainSelfTests
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
        Tick(game);
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

    private static void Defeat(GameSimulation game, EnemyKind kind)
    {
        var enemy = game.SpawnCreature(kind, game.Player.Motion.Position + new Vector2(14f, -6f));
        enemy.TakeDamage(1e7f);
        Tick(game);
        game.Player.Heal(1e6f);
    }

    private static void BreakHeart(GameSimulation game, TileId heart, int x)
    {
        for (var dy = 0; dy < 2; dy++)
        for (var dx = 0; dx < 2; dx++)
            game.World.SetTile(new GridPoint(x + dx, 3 + dy), heart);
        game.ForceBreakTile(new GridPoint(x + 1, 4));
    }

    private static bool Needs(CraftingSystem crafting, ItemId output, ItemId ingredient) =>
        crafting.FindByOutput(output).Any(recipe => recipe.Ingredients.Any(group => group.Alternatives.Contains(ingredient)));

    public static void Run(Action<bool, string> check)
    {
        // ---- G0: Creative drops no credits; Salvager is handled without a crash ----
        int CreditsOnGround(CharacterDifficulty difficulty)
        {
            var dead = NewGame(9801);
            dead.Player.Inventory.Add(ItemId.Credits, 100);
            dead.Player.TakeDamage(1e6f);
            Tick(dead, 2);
            dead.RespawnPlayer(difficulty);
            return dead.GroundItems.Where(item => item.ItemId == ItemId.Credits).Sum(item => item.Count);
        }
        var creativeGround = CreditsOnGround(CharacterDifficulty.Creative);
        var salvagerGround = CreditsOnGround(CharacterDifficulty.Salvager);
        check(creativeGround == 0 && salvagerGround is > 0 and < 100,
            $"G0 Creative characters drop no credits on death ({creativeGround}); Salvager drops a Classic-like share ({salvagerGround})");

        // ---- G1: radiated hearts summon the easy-tier Fallout Hive-mind before the Fracture ----
        var hive = EnemyArchetypes.Get(EnemyKind.FalloutHivemind);
        var hearts = NewGame(9802, WorldBlight.Radiated);
        for (var h = 0; h < 3; h++)
            BreakHeart(hearts, TileId.ReactorCore, 20 + h * 5);
        var avenger = hearts.Enemies.FirstOrDefault(enemy => enemy.IsBoss && enemy.IsAlive);
        check(hive.Tier == BossTier.Easy && hive.Flying && hive.HitPoints is >= 9000f and <= 17000f &&
              !EnemyArchetypes.BossProgression.Contains(EnemyKind.FalloutHivemind) && GameSimulation.LastEasyBoss == EnemyKind.ScoutMech &&
              avenger?.Kind == EnemyKind.FalloutHivemind && hearts.Enemies.All(enemy => enemy.Kind != EnemyKind.CyberSwarmLeader),
            "G1 before the Fracture the third radiated heart summons the easy-tier Fallout Hive-mind, not the Cyber-Swarm Leader");

        var hiveDropsTier = NpcDropRegistry.GetDrops(EnemyKind.FalloutHivemind)
            .All(drop => hearts.Items[drop.Item].Tier <= 3 && drop.Item is not (ItemId.ViridiumOre or ItemId.BlightEssence));
        var spore = NewGame(9803, WorldBlight.Overgrowth);
        BreakHeart(spore, TileId.SporeHeart, 30);
        check(hiveDropsTier && NpcDropRegistry.GetDrops(EnemyKind.FalloutHivemind).Count > 0 &&
              spore.Player.Inventory.Count(ItemId.ViridiumOre) == 0 && spore.Player.Inventory.Count(ItemId.CobaltOre) > 0,
            "G1 the Hive-mind drops tier 2-3 loot, and hearts give cobalt instead of viridium before the Fracture");

        var late = NewGame(9804, WorldBlight.Radiated);
        late.TriggerFracture();
        Calm(late);
        for (var h = 0; h < 3; h++)
            BreakHeart(late, TileId.ReactorCore, 20 + h * 5);
        check(late.RadiatedHeartAvenger == EnemyKind.CyberSwarmLeader &&
              late.Enemies.Any(enemy => enemy.Kind == EnemyKind.CyberSwarmLeader && enemy.IsAlive),
            "G1 after the Fracture radiated hearts summon the Cyber-Swarm Leader as before");

        var early = NewGame(9805, WorldBlight.Radiated);
        Defeat(early, EnemyKind.CyberSwarmLeader);
        check(!early.DefeatedBosses.Contains(EnemyKind.CyberSwarmLeader) && !early.HardTrioDefeated,
            "G1 a hard-trio boss felled before the Fracture does not count toward the hard trio");

        // ---- G2: hard-tier optional bosses wait for the Fracture ----
        var gate = NewGame(9806);
        ItemId[] gatedSummons = [ItemId.TaintedTissue, ItemId.PrismCatalyst, ItemId.RoyalNectar];
        var refused = true;
        foreach (var summon in gatedSummons)
        {
            gate.Player.Inventory.Add(summon, 1);
            Use(gate, summon);
            refused &= gate.Player.Inventory.Count(summon) == 1 && !gate.Enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive) &&
                       gate.RecentMessages.Any(message => message.Contains("Fracture") || message.Contains("fractures"));
        }
        gate.TriggerFracture();
        Calm(gate);
        Use(gate, ItemId.TaintedTissue);
        var abomination = gate.Enemies.Any(enemy => enemy.Kind == EnemyKind.Abomination && enemy.IsAlive);
        check(refused && abomination,
            $"G2 Tainted tissue, the Prism catalyst and Royal nectar refuse before the Fracture and work after it ({refused}, {abomination}: {string.Join(" | ", gate.RecentMessages)})");

        var altar = NewGame(9807);
        var shardsBefore = altar.Player.Inventory.Count(ItemId.PrismShard);
        altar.World.SetTile(new GridPoint(40, 5), TileId.CrystalAltar);
        altar.ForceBreakTile(new GridPoint(40, 5));
        Tick(altar, 5);
        var shards = altar.Player.Inventory.Count(ItemId.PrismShard) - shardsBefore +
                     altar.GroundItems.Where(item => item.ItemId == ItemId.PrismShard).Sum(item => item.Count);
        var altarMessage = altar.RecentMessages.Any(message => message.Contains("Fracture"));
        altar.World.SetTile(new GridPoint(50, 5), TileId.BloomHive);
        altar.ForceBreakTile(new GridPoint(50, 5));
        check(shards >= 3 && altarMessage && !altar.Enemies.Any(enemy => enemy.Kind is EnemyKind.PrismEmpress or EnemyKind.BloomMatriarch) &&
              altar.RecentMessages.Any(message => message.Contains("matriarch sleeps")),
            $"G2 breaking a crystal altar ({shards} shards) or a bloom hive before the Fracture gives loot but no boss ({altarMessage}: {string.Join(" | ", altar.RecentMessages)})");

        var dusk = NewGame(9808);
        var emptyEarly = dusk.EventBossPool().Length == 0 && !dusk.ProbeSpawnEventBoss() && !dusk.Enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive);
        Defeat(dusk, EnemyKind.ScoutMech);
        var afterScout = dusk.EventBossPool();
        check(emptyEarly && EnemyArchetypes.EventBosses.All(kind => EnemyArchetypes.Get(kind).HitPoints >= GameSimulation.PreFractureEventBossMaxHitPoints) &&
              afterScout.Length == EnemyArchetypes.EventBosses.Count && dusk.ProbeSpawnEventBoss(),
            "G2 pre-Fracture dusk incursions bring no 1000+ HP event boss until the Scout Mech has fallen");

        // ---- G4: altar-breaker capability, ore ladder, Alloy Anvil behind the Fracture ----
        var tools = NewGame(9809);
        bool Smash(ItemId id) => GameSimulation.CanSmashAltars(tools.Items[id]);
        check(Smash(ItemId.CoreBreaker) && Smash(ItemId.LumeniteDrill) && Smash(ItemId.VoidsteelPick) && Smash(ItemId.AstralDemolisher) &&
              !Smash(ItemId.ViridiumDrill) && !Smash(ItemId.ViridiumPick) && !Smash(ItemId.SeismicHammer) && !Smash(ItemId.PrismBore) &&
              tools.Items[ItemId.ViridiumDrill].ToolPower >= HardmodeContent.CoreBreakerPower &&
              GameSimulation.HasToolCapability(ItemId.CoreBreaker, GameSimulation.AltarBreakerCapability),
            "G4 only tools with the altar-breaker capability (core breaker and later) smash altars; the viridium drill does not");

        check(tools.Tiles[TileId.LumeniteOre].MinimumToolPower == 85 && tools.Tiles[TileId.VoidsteelOre].MinimumToolPower == 100 &&
              tools.Items[ItemId.ViridiumDrill].ToolPower >= 85 && tools.Items[ItemId.CoreBreaker].ToolPower < 85 &&
              tools.Items[ItemId.LumeniteDrill].ToolPower >= 100 && tools.Items[ItemId.ViridiumDrill].ToolPower < 100,
            "G4 lumenite ore needs a viridium drill (85) and voidsteel a lumenite drill (100)");

        check(Needs(tools.Crafting, ItemId.AlloyAnvil, ItemId.FissionSlag) && tools.Items[ItemId.FissionSlag].IsMaterial &&
              NpcDropRegistry.GetDrops(GameSimulation.CoreGateBoss).Any(drop => drop.Item == ItemId.FissionSlag) &&
              NpcDropRegistry.GetDrops(GameSimulation.CoreGateBoss).Any(drop => drop.Item == ItemId.CoreBreaker),
            "G4 the Alloy Anvil needs fission slag, dropped by the Fission Tyrant alongside the core breaker");

        // ---- G5: the Astral sigil waits for the obelisks ----
        var sigil = NewGame(9810);
        sigil.Player.Inventory.Add(ItemId.SummonAstralSigil, 1);
        Use(sigil, ItemId.SummonAstralSigil);
        var sigilRefused = sigil.AstralCountdown == 0f && sigil.Player.Inventory.Count(ItemId.SummonAstralSigil) == 1;
        sigil.Flags.Set("obelisks.down");
        Use(sigil, ItemId.SummonAstralSigil);
        check(sigilRefused && sigil.AstralCountdown > 0f,
            "G5 the Astral sigil refuses until the obelisks are down");

        // ---- G6: the Overseer patrols to a ready colony at dusk ----
        var patrol = NewGame(9811);
        var weakRefused = !patrol.TrySpawnOverseerPatrol(0.0);
        patrol.Player.LifeCoresUsed = PlayerCharacter.MaximumLifeCores;
        foreach (var piece in patrol.Items.All.Where(item => item.EquipmentSlot is EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Legs &&
                                                             item.Defense > 0)
                     .GroupBy(item => item.EquipmentSlot).Select(group => group.OrderByDescending(item => item.Defense).First()))
            patrol.Player.Equipment.Set(piece.EquipmentSlot!.Value, piece.Id);
        var ready = patrol.ReadyForOverseerPatrol;
        var highRollRefused = !patrol.TrySpawnOverseerPatrol(0.99);
        var came = patrol.TrySpawnOverseerPatrol(0.0);
        var messages = patrol.RecentMessages.ToList();
        var warned = messages.FindIndex(message => message.Contains("searchlight")) is var warn and >= 0 &&
                     messages.FindIndex(message => message.Contains("patrol!")) > warn;
        var overseer = patrol.Enemies.FirstOrDefault(enemy => enemy.Kind == EnemyKind.StoneSentinel && enemy.IsAlive);
        overseer?.TakeDamage(1e7f);
        Tick(patrol);
        var cooldownRefused = !patrol.TrySpawnOverseerPatrol(0.0);
        check(weakRefused && ready && highRollRefused && came && warned && overseer is not null && cooldownRefused &&
              patrol.DefeatedBosses.Contains(EnemyKind.StoneSentinel) && patrol.Flags.Get("overseer.patrol.arrivals") == 1,
            "G6 once max HP >= 140 and defence >= 6 a dusk roll brings the Overseer with a warning, only until it is defeated");

        // ---- G12: the dungeon opens after the Scout Mech; the Reactor key needs a heart fragment ----
        check(PhysicsContent.DungeonGateBoss == EnemyKind.ScoutMech &&
              Needs(tools.Crafting, ItemId.SummonReactorKey, ItemId.HeartFragment),
            "G12 the Dungeon Guardian stands down after the Scout Mech, and the Reactor key needs a blight heart fragment");
    }
}
