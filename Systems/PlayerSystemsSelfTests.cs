using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Deterministic checks for the player and combat systems pass (GameSimulation.PlayerSystems.cs): creature afflictions,
/// armour shred, luck, aggro, salvage caches with trophies and relics, emotes, sifting and dyes.
/// </summary>
public static class PlayerSystemsSelfTests
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
        game.Player.Motion.Position = new Vector2(30f, 20f - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        foreach (var agent in game.Settlers)
            agent.Motion.Position = new Vector2(10f, 20f - agent.Motion.Size.Y - 0.01f);
        Step(game, 3);
        return game;
    }

    private static Vector2 Center(GameSimulation game) => game.Player.Motion.Position + game.Player.Motion.Size / 2f;

    private static void Step(GameSimulation game, int ticks = 1, params EnemyAgent[] pinned)
    {
        var positions = pinned.Select(enemy => enemy.Motion.Position).ToArray();
        for (var i = 0; i < ticks; i++)
        {
            for (var p = 0; p < pinned.Length; p++)
            {
                pinned[p].Motion.Position = positions[p];
                pinned[p].Motion.Velocity = Vector2.Zero;
            }
            game.Player.Heal(1e6f);
            game.Tick(new GameInput(new MovementInput(0f), PlayerAction.None, null));
        }
    }

    private static EnemyAgent Spawn(GameSimulation game, EnemyKind kind, Vector2 offset)
    {
        var enemy = game.SpawnCreature(kind, Center(game) + offset);
        enemy.Motion.Position = Center(game) + offset - enemy.Motion.Size / 2f;
        enemy.Motion.Velocity = Vector2.Zero;
        return enemy;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- Afflictions: a poison bolt poisons, poison ticks, bosses resist ----
        var game = NewGame(9101);
        var beast = Spawn(game, EnemyKind.AstroBeast, new Vector2(3f, -0.4f));
        game.ProbeFireAfflictedBolt(Center(game) + new Vector2(0.6f, 0f), new Vector2(18f, 0f), 1f, StatusId.Poison);
        Step(game, 12, beast);
        var poisoned = beast.HasAffliction(StatusId.Poison);
        var afterHit = beast.HitPoints;
        Step(game, 60, beast);
        check(poisoned && beast.HitPoints < afterHit - 1f && beast.IsAlive,
            "a poison bolt poisons the creature it hits, which then takes damage over time");
        var boss = Spawn(game, EnemyKind.AlphaSkitter, new Vector2(-12f, -3f));
        var resisted = !game.AfflictEnemy(boss, StatusId.Confused);
        var burned = game.AfflictEnemy(boss, StatusId.Burning);
        check(resisted && burned && boss.Afflictions[StatusId.Burning] <= GameSimulation.AfflictionSeconds(StatusId.Burning) * 0.5f + 0.01f &&
              GameSimulation.ResistsAffliction(Spawn(game, EnemyKind.RockGolem, new Vector2(-6f, -3f)), StatusId.Poison),
            "bosses shrug off confusion and keep afflictions half as long; machines resist poison");
        boss.TakeDamage(1e7f);
        Step(game, 2);
        check(GameSimulation.WeaponAffliction(new ItemCatalog().All.First(item => item.UseKind == ItemUseKind.Ammunition &&
                  item.GrantsStatus == StatusId.Burning)) is { Status: StatusId.Burning, Chance: 100 },
            "incendiary ammunition carries burning onto its shots");

        // ---- Armour shred raises damage taken ----
        var shredGame = NewGame(9102);
        var target = Spawn(shredGame, EnemyKind.AstroBeast, new Vector2(3f, -0.4f));
        var before = target.HitPoints;
        shredGame.ProbeFireProjectile(Center(shredGame) + new Vector2(0.6f, 0f), new Vector2(18f, 0f), 10f, 0);
        Step(shredGame, 12, target);
        var plain = before - target.HitPoints;
        shredGame.AfflictEnemy(target, StatusId.ArmorShred);
        before = target.HitPoints;
        shredGame.ProbeFireProjectile(Center(shredGame) + new Vector2(0.6f, 0f), new Vector2(18f, 0f), 10f, 0);
        Step(shredGame, 12, target);
        var shredded = before - target.HitPoints;
        check(plain > 0f && shredded > plain * 1.2f, $"armour shred makes a creature take more damage ({plain:0.0} → {shredded:0.0})");

        // ---- Luck tilts drop rolls, calibrations and comes from gear ----
        int Count(float luck)
        {
            var random = new Random(77);
            var hits = 0;
            for (var i = 0; i < 4000; i++)
                if (GameSimulation.LuckyRoll(random, 20, luck))
                    hits++;
            return hits;
        }
        var (lucky, neutral, unlucky) = (Count(1f), Count(0f), Count(-1f));
        check(lucky > neutral && neutral > unlucky, $"luck shifts drop rolls ({unlucky} < {neutral} < {lucky} of 4000)");
        var weapon = new ItemCatalog().All.First(item => item.Calibratable && item.Tier >= 2);
        float Average(float luck)
        {
            var (first, extra) = (new Random(5), new Random(6));
            var total = 0f;
            for (var i = 0; i < 600; i++)
                total += CalibrationCatalog.Get(GameSimulation.LuckyCalibration(first, extra, weapon, luck)).ValueMultiplier;
            return total / 600f;
        }
        check(Average(1f) > Average(0f) && Average(0f) > Average(-1f), "luck tilts calibration quality up (or down when unlucky)");
        var luckGame = NewGame(9103);
        var baseLuck = luckGame.Luck;
        luckGame.Player.Equipment.Set(EquipmentSlot.Accessory, ItemId.CloverCircuit);
        luckGame.Player.Statuses.Apply(StatusId.Lucky, 2f);
        check(luckGame.Luck >= baseLuck + 0.5f && luckGame.LuckCritBonus >= 2, "a lucky accessory and the fortune tonic raise luck and crit chance");

        // ---- Aggro: a taunting player draws a creature off a colonist ----
        var aggroGame = NewGame(9104);
        aggroGame.AddStartingCrewForChecks();
        var colonist = aggroGame.Settlers.First(agent => agent.IsAlive);
        var hunter = Spawn(aggroGame, EnemyKind.AstroBeast, new Vector2(14f, -0.4f));
        colonist.Motion.Position = hunter.Motion.Position + new Vector2(-1.2f, hunter.Motion.Size.Y - colonist.Motion.Size.Y);
        var onColonist = aggroGame.AggroTargetOf(hunter);
        aggroGame.Player.Equipment.Set(EquipmentSlot.Accessory, ItemId.BeaconHarness);
        var onPlayer = aggroGame.AggroTargetOf(hunter);
        aggroGame.Player.Equipment.Set(EquipmentSlot.Accessory, ItemId.ShroudCloak);
        check(onColonist.Kind == AggroTargetKind.TownNpc && onColonist.Id == colonist.Id && onPlayer.Kind == AggroTargetKind.Player &&
              aggroGame.PlayerAggro < 0 && aggroGame.AggroTargetOf(hunter).Kind == AggroTargetKind.TownNpc,
            "creatures pick targets by aggro-weighted distance: taunt gear pulls them off a resident, stealth gear sheds them");

        // ---- Salvage caches (Brutal), trophies and relics ----
        var brutal = NewGame(9105);
        brutal.Difficulty = WorldDifficulty.Brutal;
        var bossKind = EnemyKind.AlphaSkitter;
        var bag = PlayerSystemsContent.BagFor(bossKind)!.Value;
        var brutalBoss = Spawn(brutal, bossKind, new Vector2(10f, -3f));
        brutalBoss.TakeDamage(1e7f);
        Step(brutal, 2);
        var gotBag = brutal.Player.Inventory.Count(bag) == 1;
        var crystals = brutal.Player.Inventory.Count(ItemId.EnergyCrystal);
        var opened = brutal.OpenTreasureBag(bag);
        var exclusive = PlayerSystemsContent.ExclusiveFor(bossKind)!.Value;
        var relic = PlayerSystemsContent.RelicFor(bossKind)!.Value;
        check(gotBag && crystals == 0 && opened && brutal.Player.Inventory.Count(exclusive) == 1 &&
              brutal.Player.Inventory.Count(ItemId.EnergyCrystal) > 0 && brutal.Player.Inventory.Count(bag) == 0 &&
              brutal.GroundItems.All(item => item.ItemId != relic) && brutal.Player.Inventory.Count(relic) == 0,
            "a Brutal boss leaves a salvage cache holding its loot plus an exclusive core-chip (relics are Annihilation-only)");
        var classic = NewGame(9106);
        var classicBoss = Spawn(classic, bossKind, new Vector2(10f, -3f));
        classicBoss.TakeDamage(1e7f);
        Step(classic, 2);
        check(classic.Player.Inventory.Count(bag) == 0 && classic.Player.Inventory.Count(ItemId.EnergyCrystal) > 0 &&
              classic.GroundItems.All(item => item.ItemId != bag && item.ItemId != relic),
            "a Classic boss drops its loot directly, with no cache and no relic");
        var trophy = PlayerSystemsContent.TrophyFor(bossKind)!.Value;
        var decor = NewGame(9107);
        decor.Player.Inventory.Add(trophy, 1);
        decor.Player.Inventory.Add(relic, 1);
        var feet = new GridPoint((int)Center(decor).X, 19);
        var trophyCell = new GridPoint(feet.X + 2, 19);
        var relicCell = new GridPoint(feet.X - 2, 19);
        decor.ProbePlace(trophy, trophyCell);
        decor.ProbePlace(relic, relicCell);
        var placed = decor.World.GetCell(trophyCell).TileId == TileId.BossTrophy && decor.World.GetCell(relicCell).TileId == TileId.BossRelic &&
                     decor.PlacedDecorItem(trophyCell) == trophy && decor.Player.Inventory.Count(trophy) == 0;
        decor.World.SetTile(trophyCell, TileId.Air);
        Step(decor, 60);
        check(placed && (decor.Player.Inventory.Count(trophy) == 1 || decor.GroundItems.Any(item => item.ItemId == trophy)),
            "boss trophies and relics place as decor and a broken trophy gives back its own item");

        // ---- Emotes ----
        var emoteGame = NewGame(9108);
        emoteGame.PlayerEmote(EmoteKind.Happy);
        var shown = emoteGame.Emotes.Any(bubble => bubble.ActorId == GameSimulation.PlayerActorId && bubble.Kind == EmoteKind.Happy);
        Step(emoteGame, (int)(GameSimulation.EmoteSeconds * 60f) + 20);
        check(shown && emoteGame.Emotes.All(bubble => bubble.ActorId != GameSimulation.PlayerActorId),
            "an emote bubble shows over the player and expires after a few seconds");

        // ---- Sifting ----
        var siftGame = NewGame(9109);
        siftGame.Player.Inventory.Add(ItemId.SiltBlock, 30);
        var noSifter = siftGame.Sift(ItemId.SiltBlock) is null;
        siftGame.World.SetTile(new GridPoint((int)Center(siftGame).X + 2, 19), TileId.Sifter);
        var finds = new List<ItemId>();
        for (var i = 0; i < 25; i++)
            if (siftGame.Sift(ItemId.SiltBlock) is { } find)
                finds.Add(find.Item);
        check(noSifter && finds.Count == 25 && finds.Any(item => item.ToString().EndsWith("Ore", StringComparison.Ordinal)) &&
              siftGame.Player.Inventory.Count(ItemId.SiltBlock) == 5,
            "sifting silt at a sifter turns up ores (and needs the station)");

        // ---- Dyes ----
        var dyeGame = NewGame(9110);
        var armour = new ItemCatalog().All.First(item => item.UseKind == ItemUseKind.Equipment && item.EquipmentSlot == EquipmentSlot.Body);
        dyeGame.Player.Inventory.Add(ItemId.CrimsonDye, 1);
        dyeGame.Player.Inventory.Add(ItemId.AzureDye, 1);
        var refused = !dyeGame.ApplyDye(ItemId.CrimsonDye, DyeSlot.Body);
        dyeGame.Player.Equipment.Set(EquipmentSlot.Body, armour.Id);
        var undyed = dyeGame.DyeTint(DyeSlot.Body) is null;
        dyeGame.ApplyDye(ItemId.CrimsonDye, DyeSlot.Body);
        var crimson = dyeGame.DyeTint(DyeSlot.Body);
        dyeGame.ApplyDye(ItemId.AzureDye, DyeSlot.Body);
        check(refused && undyed && crimson == PlayerSystemsContent.DyeColors[ItemId.CrimsonDye] &&
              dyeGame.DyeTint(DyeSlot.Body) == PlayerSystemsContent.DyeColors[ItemId.AzureDye] &&
              dyeGame.Player.Inventory.Count(ItemId.CrimsonDye) == 1,
            "a dye on equipped armour sets its tint; re-dyeing returns the old dye");
    }
}
