using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// 0.2.2 stream C (the 0.1.7 fix list's boss and event lifecycle): the boss behaviour suite (BO-24) and the checks for
/// PC-1, PC-2, PC-4, BO-6, BO-9, BO-17, BO-18, EV-1, EV-2 and EN-7. Headless worlds on small maps; called from
/// <see cref="CoreSelfTests.Run"/>, never from a module initializer.
/// </summary>
public static class BossSelfTests
{
    /// <summary>
    /// Behaviours the suite knows are still broken: reported, never failed, so the suite could land before its fixes
    /// (BO-24). BO-6 landed with it, so the list is empty.
    /// </summary>
    public static IReadOnlyList<string> KnownFailing { get; } = [];

    private static GameSimulation NewGame(int seed, int width = 160, int height = 72, CharacterProfile? profile = null)
    {
        var game = new GameSimulation(seed: seed, width: width, height: height, colonist: profile) { FamilySpawnsEnabled = false };
        Calm(game);
        return game;
    }

    private static void Calm(GameSimulation game)
    {
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e7f);
        game.Player.Heal(1e6f);
        Tick(game);
    }

    private static void Tick(GameSimulation game, int ticks = 1, PlayerAction action = PlayerAction.None)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f), i == 0 ? action : PlayerAction.None, new GridPoint(1, 1)));
    }

    private static void Use(GameSimulation game, ItemId item)
    {
        game.Player.ActionCooldownTicks = 0;
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
        Tick(game, 1, PlayerAction.UseSelected);
    }

    private static void Defeat(GameSimulation game, EnemyKind kind)
    {
        var enemy = game.SpawnCreature(kind, game.Player.Motion.Position + new Vector2(14f, -6f));
        enemy.TakeDamage(1e7f);
        Tick(game);
        game.Player.Heal(1e6f);
    }

    private static EnemyAgent Summon(GameSimulation game, EnemyKind kind, BossSpawnSource source = BossSpawnSource.SummonItem)
    {
        if (!game.TrySpawnBoss(kind, source, out var boss, out var refusal))
            throw new InvalidOperationException($"{kind} refused: {refusal}");
        return boss!;
    }

    private static bool Alive(GameSimulation game, EnemyKind kind) => game.Enemies.Any(enemy => enemy.Kind == kind && enemy.IsAlive);

    /// <summary>Kills every living creature tagged with <paramref name="kind"/> (and lets the defeat logic run).</summary>
    private static int KillTagged(GameSimulation game, WorldEventKind kind, bool bosses = true)
    {
        var tagged = game.Enemies.Where(enemy => enemy.IsAlive && enemy.EventTag == kind && (bosses || !enemy.IsBoss)).ToArray();
        foreach (var enemy in tagged)
            enemy.TakeDamage(1e7f);
        Tick(game);
        game.Player.Heal(1e6f);
        return tagged.Length;
    }

    /// <summary>Spawns and kills <paramref name="count"/> creatures of an event's roster, tagged with it.</summary>
    private static void EventKills(GameSimulation game, WorldEventKind kind, int count)
    {
        var roster = game.WorldEvents.Spec(kind).Roster;
        for (var i = 0; i < count; i++)
        {
            var creature = game.SpawnCreature(roster[i % roster.Length].Kind, game.Player.Motion.Position + new Vector2(10f, -4f));
            creature.EventTag = kind;
            creature.TakeDamage(1e7f);
            Tick(game);
            game.Player.Heal(1e6f);
        }
    }

    private static int Holding(GameSimulation game, ItemId item) =>
        game.Player.Inventory.Count(item) + game.GroundItems.Where(ground => ground.ItemId == item).Sum(ground => ground.Count);

    private static GameSimulation RoundTrip(GameSimulation game) =>
        GameSimulation.Restore(System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(game.CaptureSnapshot()))!);

    public static void Run(Action<bool, string> check)
    {
        GameSimulation.FastClock = true;
        var known = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition && KnownFailing.Contains(name))
            {
                known.Add(name);
                Console.WriteLine($"KNOWN-FAIL {name}");
                return;
            }
            check(condition, name);
        }

        Lifecycle(Check);
        Progression(Check);
        Events(Check);
        Saves(Check);
        Families(Check);
        check(known.Count == KnownFailing.Count, $"BO-24 the known-failing list is reported, not failed ({known.Count} known: {string.Join("; ", known)})");
    }

    // ================================================================== BO-24 / BO-6 / BO-9 / BO-18

    private static void Lifecycle(Action<bool, string> check)
    {
        // ---- Dawn: night bosses summoned at 02:00 are gone at 07:00; a day boss stays; the Eclipse Lord leaves at dusk ----
        var dawn = NewGame(9301);
        dawn.SetTimeOfDay(2f);
        var night = new[]
        {
            EnemyKind.StoneSentinel, EnemyKind.MoonEater, EnemyKind.HollowRegent, EnemyKind.RimeSeraph, EnemyKind.CyberSwarmLeader,
            EnemyKind.WarshipCore, EnemyKind.RenegadeOverseer
        }.Concat(EnemyArchetypes.EventBosses).ToArray();
        foreach (var kind in night)
            Summon(dawn, kind);
        var dayBoss = Summon(dawn, EnemyKind.AlphaSkitter);
        var deathsBefore = dawn.RecentDeaths.Next;
        var defeatedBefore = dawn.DefeatedBosses.Count;
        var groundBefore = dawn.GroundItems.Count;
        dawn.ProbeAdvanceHours(5f); // 02:00 → 07:00
        var records = new List<DeathRecord>();
        var cursor = deathsBefore;
        dawn.RecentDeaths.Drain(ref cursor, records);
        var gone = night.Where(kind => !Alive(dawn, kind)).ToArray();
        check(gone.Length == night.Length && dawn.Enemies.Contains(dayBoss) &&
              records.Count(record => record.Reason == DeathReason.Despawned) == night.Length &&
              dawn.DefeatedBosses.Count == defeatedBefore && dawn.GroundItems.Count == groundBefore,
            $"BO-6 dawn: the Overseer, hard trio, Moon Eater, Hollow Regent, Rime Seraph and the dusk four summoned at 02:00 are gone " +
            $"at 07:00 ({gone.Length}/{night.Length}), recorded as despawned, no defeat, no drops; a day boss stays");
        dawn.SetTimeOfDay(14f);
        var eclipse = Summon(dawn, EnemyKind.EclipseLord);
        dawn.ProbeAdvanceHours(5f); // 14:00 → 19:00
        check(!dawn.Enemies.Contains(eclipse) && BossLeaveRules.For(EnemyKind.EclipseLord) == (BossLeaveRule.AtDusk | BossLeaveRule.OnTargetLost) &&
              !BossLeaveRules.For(EnemyKind.EclipseLord).HasFlag(BossLeaveRule.AtDawn) &&
              BossLeaveRules.For(EnemyKind.AstroGod) == BossLeaveRule.None &&
              BossLeaveRules.For(EnemyKind.FalloutHivemind).HasFlag(BossLeaveRule.LeaveBiome) &&
              EnemyArchetypes.All.Where(archetype => archetype.IsBoss && archetype.Kind != EnemyKind.AstroGod)
                  .All(archetype => BossLeaveRules.For(archetype.Kind).HasFlag(BossLeaveRule.OnTargetLost)),
            "BO-6 the Eclipse Lord (a day boss) leaves at dusk; every boss but the Astro-God leaves on a lost target; the Hive-mind keeps to its biome");
        check(GameSimulation.BoundaryPassed(WorldEventMoment.Dawn, 2f, 7f, 5f) && !GameSimulation.BoundaryPassed(WorldEventMoment.Dawn, 7f, 12f, 5f) &&
              GameSimulation.BoundaryPassed(WorldEventMoment.Dusk, 23f, 19f, 20f) && GameSimulation.BoundaryPassed(WorldEventMoment.Dusk, 17f, 18f, 1f),
            "BO-6 BoundaryPassed(Dawn|Dusk) detects the clock crossing 06:00 and 18:00, across midnight too");

        // ---- Distance: a boss 188 tiles away is gone within 30 s; the summon item works right after ----
        var far = NewGame(9302, width: 420);
        far.Player.Inventory.Add(ItemId.SummonReactorKey, 2);
        var px = far.Player.Motion.Position.X;
        var awayX = px + 188f < far.World.Width - 8 ? px + 188f : px - 188f;
        var stray = far.SpawnCreatureAt(EnemyKind.StoneSentinel, new Vector2(awayX, far.Player.Motion.Position.Y - 8f));
        var seconds = 0f;
        while (far.Enemies.Contains(stray) && seconds < 40f)
        {
            far.ProbeSpawnTick(0.25f);
            seconds += 0.25f;
        }
        var keys = far.Player.Inventory.Count(ItemId.SummonReactorKey);
        Use(far, ItemId.SummonReactorKey);
        check(!far.Enemies.Contains(stray) && seconds <= 30f && !far.DefeatedBosses.Contains(EnemyKind.StoneSentinel) &&
              Alive(far, EnemyKind.StoneSentinel) && far.Player.Inventory.Count(ItemId.SummonReactorKey) == keys - 1,
            $"BO-6 a boss 188 tiles away loses its target and leaves within 30 s ({seconds:0.##} s); its summon item works right after");

        // Out of sight: a boss walled off beyond 30 tiles gives up after 30 s; one in plain view keeps fighting.
        var blind = NewGame(9303, width: 200);
        var center = blind.Player.Motion.Position;
        var hidden = blind.SpawnCreatureAt(EnemyKind.AlphaSkitter, center + new Vector2(45f, 0f));
        for (var y = 0; y < blind.World.Height; y++)
            blind.World.SetTile(new GridPoint((int)center.X + 20, y), TileId.Rock);
        var hiddenSeconds = 0f;
        while (blind.Enemies.Contains(hidden) && hiddenSeconds < 40f)
        {
            hidden.Motion.Position = center + new Vector2(45f, -2f);
            blind.ProbeSpawnTick(0.5f);
            hiddenSeconds += 0.5f;
        }
        check(!blind.Enemies.Contains(hidden) && hiddenSeconds >= BossLeaveRules.SightLostSeconds - 0.01f && hiddenSeconds <= 31f,
            $"BO-6 a walled-off boss beyond {BossLeaveRules.SightCheckDistance:0} tiles leaves after {BossLeaveRules.SightLostSeconds:0} s out of sight ({hiddenSeconds:0.#} s)");

        // ---- Death: bosses leave on player death ----
        var lonely = NewGame(9304);
        Summon(lonely, EnemyKind.ScoutMech);
        lonely.Player.TakeDamage(1e6f);
        Tick(lonely, 60 * 4);
        check(!lonely.Enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive) && !lonely.DefeatedBosses.Contains(EnemyKind.ScoutMech) &&
              lonely.RecentMessages.Any(message => message.Contains("Scout Mech has left", StringComparison.Ordinal)) &&
              lonely.RecentDeaths.Snapshot().Any(record => record.Kind == EnemyKind.ScoutMech && record.Reason == DeathReason.Despawned),
            "BO-24 bosses leave on player death: \"X has left\", a despawn record, not a defeat");

        // ---- Refusal: existing refusals hold and the item is not consumed; D-11 same kind refuses, a different kind may join ----
        var refuse = NewGame(9305);
        refuse.SetTimeOfDay(22f);
        refuse.Player.Inventory.Add(ItemId.SwarmResonator, 1);
        refuse.Player.Inventory.Add(ItemId.MoonEaterLure, 1);
        refuse.Player.Inventory.Add(ItemId.SummonReactorKey, 2);
        refuse.Player.Inventory.Add(ItemId.SummonScoutBeacon, 1);
        refuse.Player.Inventory.Add(ItemId.ProtocolKey, 1);
        Use(refuse, ItemId.ProtocolKey);
        var keyHeld = refuse.Player.Inventory.Count(ItemId.ProtocolKey) == 1 && !Alive(refuse, EnemyKind.ProtocolDragon);
        Use(refuse, ItemId.SwarmResonator);
        Use(refuse, ItemId.MoonEaterLure);
        var lureMessage = refuse.LastMessage;
        var heldRefused = refuse.Player.Inventory.Count(ItemId.SwarmResonator) == 1 && refuse.Player.Inventory.Count(ItemId.MoonEaterLure) == 1 &&
                          !refuse.Enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive);
        Use(refuse, ItemId.SummonReactorKey);
        Use(refuse, ItemId.SummonReactorKey);
        var sameKind = refuse.LastMessage;
        var keysLeft = refuse.Player.Inventory.Count(ItemId.SummonReactorKey);
        Use(refuse, ItemId.SummonScoutBeacon);
        check(heldRefused && keyHeld && lureMessage == "The lure stays dark until the Fracture." &&
              sameKind?.Contains("already here", StringComparison.Ordinal) == true && keysLeft == 1 &&
              refuse.Enemies.Count(enemy => enemy.Kind == EnemyKind.StoneSentinel && enemy.IsAlive) == 1 &&
              Alive(refuse, EnemyKind.ScoutMech) && refuse.Player.Inventory.Count(ItemId.SummonScoutBeacon) == 0 &&
              refuse.LatestBoss?.Kind == EnemyKind.ScoutMech && refuse.ActiveBosses.Count == 2,
            "BO-24/BO-9 refusals keep the item (the Fracture gates, the Moon Eater lure, the Protocol key before the Colossus); a same-kind " +
            "summon is refused; a different boss " +
            "may join (D-11) and the bar's boss is the latest");

        // ---- Loot on difficulty: Explorer, Classic and Brutal kills all drop ----
        var lootOk = true;
        var lootLine = new List<string>();
        foreach (var difficulty in new[] { WorldDifficulty.Explorer, WorldDifficulty.Classic, WorldDifficulty.Brutal })
        {
            var loot = NewGame(9306);
            loot.Difficulty = difficulty;
            var before = loot.Player.Inventory.Slots.Sum(stack => stack.Count) + loot.GroundItems.Sum(item => item.Count);
            Summon(loot, EnemyKind.AlphaSkitter).TakeDamage(1e9f);
            Tick(loot, 3);
            var gained = loot.Player.Inventory.Slots.Sum(stack => stack.Count) + loot.GroundItems.Sum(item => item.Count) - before;
            lootOk &= gained > 0 && loot.DefeatedBosses.Contains(EnemyKind.AlphaSkitter);
            lootLine.Add($"{difficulty} {gained}");
        }
        check(lootOk, $"BO-24 boss loot exists on Explorer, Classic and Brutal ({string.Join(", ", lootLine)})");

        // ---- BO-18: the challenge key is a --kit / creative tool and goes through the item refusals ----
        var plain = NewGame(9307);
        var plainRefused = !plain.TrySummonSentinel() && !plain.Enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive);
        var source = NewGame(9308);
        var late = EnemyArchetypes.BossProgression.Where(kind => kind is not (EnemyKind.WarshipCore or EnemyKind.AstroGod)).ToArray();
        var keyGame = GameSimulation.Restore(source.CaptureSnapshot() with { DefeatedBosses = late, FractureActive = false });
        keyGame.DebugSummonKeyEnabled = true;
        var keyRefused = !keyGame.TrySummonSentinel();
        var keyMessage = keyGame.LastMessage ?? "";
        var creativeProfile = CharacterDraft.CreateRandom(9309, "Maker").ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow) with
            { Difficulty = CharacterDifficulty.Creative };
        var creative = NewGame(9309, profile: creativeProfile);
        check(plainRefused && keyRefused && !Alive(keyGame, EnemyKind.WarshipCore) && keyMessage.Contains("fractures", StringComparison.Ordinal) &&
              creative.TrySummonSentinel() && Alive(creative, EnemyKind.StoneSentinel),
            $"BO-18 the challenge key works only in creative or a --kit play-test, and cannot summon the Warship Core before the Fracture ({keyMessage})");
    }

    // ================================================================== PC-1 / PC-2 / PC-4

    private static void Progression(Action<bool, string> check)
    {
        // ---- PC-1: no Moon Eater before the Fracture; the Rust Moon completes on its quota ----
        var moon = NewGame(9310);
        moon.Flags.Set("player.health.120");
        moon.SetTimeOfDay(20f);
        var started = moon.WorldEvents.TryStart(WorldEventKind.RustMoon, moon, forced: true, out _);
        var lures = moon.Player.Inventory.Count(ItemId.MoonEaterLure);
        EventKills(moon, WorldEventKind.RustMoon, 25);
        var noBoss = !moon.Enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive);
        var completed = !moon.WorldEvents.IsActive(WorldEventKind.RustMoon) && moon.WorldEvents.Completions(WorldEventKind.RustMoon) == 1 &&
                        moon.Flags.IsSet(moon.WorldEvents.Spec(WorldEventKind.RustMoon).FlagName) &&
                        moon.Player.Inventory.Count(ItemId.MoonEaterLure) == lures + 1;
        moon.TriggerFracture();
        moon.SetTimeOfDay(20f);
        var again = moon.WorldEvents.TryStart(WorldEventKind.RustMoon, moon, forced: true, out _);
        EventKills(moon, WorldEventKind.RustMoon, 25);
        var eater = moon.Enemies.FirstOrDefault(enemy => enemy.Kind == EnemyKind.MoonEater && enemy.IsAlive);
        check(started && noBoss && completed && again && eater is { EventTag: WorldEventKind.RustMoon } &&
              moon.WorldEvents.IsActive(WorldEventKind.RustMoon),
            "PC-1 a forced Rust Moon before the Fracture: 25 kills bring no boss and the moon completes with its rewards; after it, the Moon Eater comes");
        var preDrops = NpcDropRegistry.Roll(EnemyKind.MoonEater, NpcDropContext.Default, new Random(1));
        var postDrops = NpcDropRegistry.Roll(EnemyKind.MoonEater, NpcDropContext.Default with { Fracture = true }, new Random(1));
        check(preDrops.All(drop => drop.Item != ItemId.VoidEssence) && preDrops.Any(drop => drop.Item == ItemId.OxidizedShard) &&
              postDrops.Any(drop => drop.Item == ItemId.VoidEssence) && postDrops.All(drop => drop.Item != ItemId.OxidizedShard),
            "PC-1 the Moon Eater drops void essence only after the Fracture (rust scrap before it)");

        // ---- PC-2: the Defense Protocol finale follows progression ----
        (bool Completed, int Waves, bool AnyBoss, bool Ogre, bool Dragon) Protocol(GameSimulation game)
        {
            game.SetTimeOfDay(10f);
            game.WorldEvents.TryStart(WorldEventKind.DefenseProtocol, game, forced: true, out _);
            var state = game.WorldEvents.Active.Single(active => active.Kind == WorldEventKind.DefenseProtocol);
            var waves = game.WorldEvents.Spec(WorldEventKind.DefenseProtocol).WavesFor(state);
            bool anyBoss = false, ogre = false, dragon = false;
            for (var round = 0; round < 400 && game.WorldEvents.IsActive(WorldEventKind.DefenseProtocol); round++)
            {
                anyBoss |= game.Enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive);
                dragon |= Alive(game, EnemyKind.ProtocolDragon);
                var current = game.WorldEvents.Active.FirstOrDefault(active => active.Kind == WorldEventKind.DefenseProtocol);
                ogre |= current is { BossSpawned: true } && game.Enemies.Any(enemy => enemy.Kind == EnemyKind.EventProtocolOgre && enemy.IsAlive &&
                                                                                      enemy.EventTag == WorldEventKind.DefenseProtocol);
                if (KillTagged(game, WorldEventKind.DefenseProtocol) == 0)
                    EventKills(game, WorldEventKind.DefenseProtocol, 1);
            }
            return (game.WorldEvents.Completions(WorldEventKind.DefenseProtocol) == 1, waves, anyBoss, ogre, dragon);
        }
        var fresh = Protocol(NewGame(9311));
        var fracturedGame = NewGame(9312);
        fracturedGame.TriggerFracture();
        var fractured = Protocol(fracturedGame);
        var lateGame = NewGame(9313);
        lateGame.TriggerFracture();
        lateGame.Flags.Set($"boss.{WorldFlags.Key(EnemyKind.VaultColossus)}.down");
        var lateRun = Protocol(lateGame);
        check(fresh is { Completed: true, Waves: 3, AnyBoss: false, Dragon: false } &&
              fractured is { Completed: true, Waves: 5, AnyBoss: false, Ogre: true, Dragon: false } &&
              lateRun is { Completed: true, Waves: 7, Dragon: true },
            $"PC-2 the Defense Protocol tiers its finale: a fresh world runs 3 waves and no boss ({fresh}); after the Fracture 5 waves and " +
            $"the Protocol ogre ({fractured}); with the Vault Colossus down 7 waves and the Protocol Dragon ({lateRun})");

        // ---- PC-4: no pre-Fracture incursion boss and no early upgrade drops ----
        var dusk = NewGame(9314);
        Defeat(dusk, EnemyKind.ScoutMech);
        var probeRefused = !dusk.ProbeSpawnEventBoss() && dusk.EventBossPool().Length == 0 && !dusk.IncursionEligible;
        var incursionBoss = false;
        for (var step = 0; step < 20 * 4; step++) // 20 days in 6-hour steps (each dusk is crossed)
        {
            dusk.ProbeAdvanceHours(6f);
            incursionBoss |= dusk.Enemies.Any(enemy => enemy.IsAlive && EnemyArchetypes.EventBosses.Contains(enemy.Kind));
        }
        check(probeRefused && !incursionBoss,
            "PC-4 after the Scout Mech and before the Fracture, forced and natural dusk incursions spawn no boss");
        var marrow = NewGame(9315);
        marrow.Difficulty = WorldDifficulty.Brutal; // upgrades always drop on Brutal, so any early one would show
        for (var kill = 0; kill < 500; kill++)
        {
            marrow.SpawnCreature(EnemyKind.VoidStalker, marrow.Player.Motion.Position + new Vector2(6f, -4f)).TakeDamage(1e9f);
            Tick(marrow);
            if (kill % 50 == 49)
            {
                marrow.Player.Heal(1e6f);
                if (Holding(marrow, ItemId.NaniteMarrow) > 0)
                    break;
            }
        }
        var earlyMarrow = Holding(marrow, ItemId.NaniteMarrow);
        marrow.TriggerFracture();
        marrow.SpawnCreature(EnemyKind.VoidStalker, marrow.Player.Motion.Position + new Vector2(6f, -4f)).TakeDamage(1e9f);
        Tick(marrow, 3);
        check(earlyMarrow == 0 && Holding(marrow, ItemId.NaniteMarrow) >= 1,
            "PC-4 a Void Stalker forced before the Fracture never drops nanite marrow (500 Brutal kills); after it, it does");
    }

    // ================================================================== EV-1 / EV-2 / BO-9 finale

    private static void Events(Action<bool, string> check)
    {
        // ---- EV-1: obelisk shields never time out ----
        var siege = NewGame(9320, width: 240);
        Defeat(siege, GameSimulation.RitualBoss);
        var obelisks = siege.Enemies.Where(enemy => GameSimulation.Obelisks.Any(entry => entry.Obelisk == enemy.Kind)).ToArray();
        for (var hour = 0; hour < 200; hour++)
            siege.ProbeAdvanceHours(1f); // 200 game hours, no kills (the sieges' old 48-hour timer ran out at 48)
        Tick(siege, 40);
        siege.Player.Heal(1e6f);
        var allShielded = obelisks.Length == 4 && obelisks.All(obelisk => obelisk.IsAlive && obelisk.Shielded) &&
                          GameSimulation.Obelisks.All(entry => siege.WorldEvents.IsActive(entry.Event));
        siege.WorldEvents.Stop(WorldEventKind.RiftObelisk, siege);
        Tick(siege, 40);
        var missingKept = obelisks.Single(obelisk => obelisk.Kind == EnemyKind.RiftObelisk).Shielded &&
                          siege.WorldEvents.IsActive(WorldEventKind.RiftObelisk);
        var ember = siege.WorldEvents.Active.Single(state => state.Kind == WorldEventKind.EmberObelisk);
        EventKills(siege, WorldEventKind.EmberObelisk, Math.Max(0, ember.KillsNeeded - ember.Kills));
        Tick(siege, 40);
        var emberDown = !obelisks.Single(obelisk => obelisk.Kind == EnemyKind.EmberObelisk).Shielded &&
                        obelisks.Where(obelisk => obelisk.Kind != EnemyKind.EmberObelisk).All(obelisk => obelisk.Shielded);
        check(allShielded && missingKept && emberDown &&
              GameSimulation.Obelisks.All(entry => siege.WorldEvents.Spec(entry.Event).NoExpiry),
            "EV-1 the obelisk sieges never time out: 200 game hours with no quota kills leave all four shielded; a missing siege " +
            "state keeps its shield; the quota of kills near one obelisk drops only that shield");

        // ---- EV-2: one event system ----
        var run = NewGame(9321);
        Defeat(run, EnemyKind.ScoutMech);
        run.TriggerFracture();
        var names = run.WorldEvents.Specs.Select(spec => spec.Name).ToHashSet();
        var foreign = new HashSet<string>();
        var seen = 0;
        for (var step = 0; step < 40 * 48; step++)
        {
            run.ProbeAdvanceHours(0.5f);
            foreach (var message in run.RecentMessages.Where(message => message.StartsWith("Event started: ", StringComparison.Ordinal)))
            {
                seen++;
                var name = message["Event started: ".Length..].TrimEnd('.');
                if (!names.Contains(name))
                    foreign.Add(name);
            }
            foreach (var boss in run.Enemies.Where(enemy => enemy.IsBoss && enemy.IsAlive).ToArray())
                boss.TakeDamage(1e9f);
            if (step % 48 == 0)
            {
                Tick(run);
                run.Player.Heal(1e6f);
            }
        }
        check(foreign.Count == 0 && run.Flags.Get("boss.spawns.dusk_incursion") > 0,
            $"EV-2 a 40-day run: every 'Event started' message comes from a WorldEventKind ({seen} seen, foreign: " +
            $"{string.Join(", ", foreign)}); dusk incursions still come after the Fracture ({run.Flags.Get("boss.spawns.dusk_incursion")})");

        var mods = NewGame(9322);
        mods.TriggerFracture();
        mods.Flags.Set("player.health.120"); // the Rust Moon's eligibility
        mods.SetTimeOfDay(20f);
        mods.WorldEvents.TryStart(WorldEventKind.RustMoon, mods, forced: true, out _);
        mods.WorldEvents.TryStart(WorldEventKind.DefenseProtocol, mods, forced: true, out _);
        var product = mods.WorldEvents.Active.Aggregate(1f, (total, state) => total * mods.WorldEvents.Spec(state.Kind).SpawnMultiplier);
        var population = mods.WorldEvents.Active.Aggregate(1f, (total, state) => total * mods.WorldEvents.Spec(state.Kind).PopulationMultiplier);
        var reloaded = RoundTrip(mods);
        check(Math.Abs(mods.EventSpawnFactors.Spawn - product) < 1e-4f && Math.Abs(mods.EventSpawnFactors.Population - population) < 1e-4f &&
              product > 1f && reloaded.ActiveEvents.Select(state => state.Kind).OrderBy(kind => kind)
                  .SequenceEqual(mods.ActiveEvents.Select(state => state.Kind).OrderBy(kind => kind)) && reloaded.ActiveEvents.Count == 2,
            $"EV-2 the spawn multiplier is the WorldEventSystem product ({product:0.##}); save and load keep every visible event");

        // ---- BO-9: an event finale while a different boss lives gives two bosses ----
        var both = NewGame(9323);
        both.TriggerFracture();
        both.Flags.Set("player.health.120"); // the Rust Moon's eligibility
        both.SetTimeOfDay(20f);
        Summon(both, EnemyKind.ScoutMech);
        both.WorldEvents.TryStart(WorldEventKind.RustMoon, both, forced: true, out _);
        EventKills(both, WorldEventKind.RustMoon, 25);
        var bossKinds = both.ActiveBosses.Select(boss => boss.Kind).ToArray();
        check(bossKinds.Length == 2 && bossKinds.Contains(EnemyKind.ScoutMech) && bossKinds.Contains(EnemyKind.MoonEater) &&
              both.LatestBoss?.Kind == EnemyKind.MoonEater && both.Flags.Get("boss.spawns.event_finale") == 1,
            "BO-9 an event finale while a different boss lives gives two bosses (the Moon Eater is the latest)");
    }

    // ================================================================== BO-17

    private static void Saves(Action<bool, string> check)
    {
        var fight = NewGame(9330);
        fight.TriggerFracture();
        fight.Flags.Set("player.health.120"); // the Rust Moon's eligibility
        fight.SetTimeOfDay(21f);
        fight.Player.Inventory.Add(ItemId.SummonReactorKey, 1);
        Summon(fight, EnemyKind.StoneSentinel);
        var bossFactors = fight.CurrentSpawnFactors();
        fight.WorldEvents.TryStart(WorldEventKind.RustMoon, fight, forced: true, out _);
        EventKills(fight, WorldEventKind.RustMoon, 25);
        var midFight = fight.Enemies.Count(enemy => enemy.IsBoss && enemy.IsAlive);
        var loaded = RoundTrip(fight);
        var noBoss = !loaded.Enemies.Any(enemy => enemy.IsBoss);
        var factors = loaded.CurrentSpawnFactors();
        var rearmed = loaded.WorldEvents.Active.Single(state => state.Kind == WorldEventKind.RustMoon) is { BossSpawned: false };
        Use(loaded, ItemId.SummonReactorKey);
        var summoned = Alive(loaded, EnemyKind.StoneSentinel) && loaded.Player.Inventory.Count(ItemId.SummonReactorKey) == 0;
        // An old save that still lists a living boss loads without it.
        var oldSave = fight.CaptureSnapshot();
        var legacy = GameSimulation.Restore(oldSave with
        {
            Enemies = [.. oldSave.Enemies ?? [], new EnemyAgent(900_000, EnemyKind.ScoutMech, fight.Player.Motion.Position).CaptureState()]
        });
        check(midFight == 2 && noBoss && factors.Rate > bossFactors.Rate && rearmed && summoned && !legacy.Enemies.Any(enemy => enemy.IsBoss),
            "BO-17 save and quit mid-fight drops the living bosses: no boss on load, the normal spawn rate back, the event's finale " +
            "re-armed and the summon item works (old saves with a boss load without it)");
    }

    // ================================================================== EN-7

    private static void Families(Action<bool, string> check)
    {
        int FamilySpawns(GameSimulation game, int attempts, EnemyKind? only = null)
        {
            var spawned = 0;
            var known = game.Enemies.Select(enemy => enemy.Id).ToHashSet();
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                game.ProbeFamilySpawn();
                foreach (var enemy in game.Enemies.Where(enemy => enemy.IsAlive && known.Add(enemy.Id)).ToArray())
                {
                    if (enemy.Family != EnemyAiFamily.Default && (only is null || enemy.Kind == only))
                        spawned++;
                    enemy.TakeDamage(1e9f);
                }
                if (attempt % 200 == 199)
                    Tick(game);
            }
            return spawned;
        }

        var profile = CharacterDraft.CreateRandom(9340, "Maker").ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow) with
            { Difficulty = CharacterDifficulty.Creative };
        var creative = NewGame(9340, profile: profile);
        creative.FamilySpawnsEnabled = true;
        var normal = FamilySpawns(creative, 200);
        creative.CreativeSpawnRate = 0f;
        var before = creative.Enemies.Count(enemy => enemy.IsAlive);
        var known = creative.Enemies.Select(enemy => enemy.Id).ToHashSet();
        var appeared = 0;
        for (var tick = 0; tick < 4 * 60 * 30; tick++) // 30 minutes of real time at 0.25 s steps
        {
            creative.Tick(new GameInput(new MovementInput(0f)), 0.25f);
            appeared += creative.Enemies.Count(enemy => known.Add(enemy.Id));
        }
        var probed = FamilySpawns(creative, 200);
        check(normal > 0 && appeared == 0 && probed == 0,
            $"EN-7 creative spawn rate 0: no family or ambient spawns over 30 minutes ({appeared}) or 200 family attempts ({probed}; {normal} at rate 1)");

        var calm = NewGame(9341);
        calm.FamilySpawnsEnabled = true;
        calm.SetTimeOfDay(21f);
        calm.WorldEvents.TryStart(WorldEventKind.AuroraNight, calm, forced: true, out _);
        var auroraSpawns = calm.WorldEvents.IsActive(WorldEventKind.AuroraNight) ? FamilySpawns(calm, 300) : -1;
        calm.WorldEvents.Stop(WorldEventKind.AuroraNight, calm);
        var afterAurora = FamilySpawns(calm, 300);
        check(auroraSpawns == 0 && afterAurora > 0,
            $"EN-7 a NoHostiles event (Aurora Night): no hostile family spawns ({auroraSpawns}; {afterAurora} without it)");

        var deep = NewGame(9342);
        deep.FamilySpawnsEnabled = true;
        var x = (int)deep.Player.Motion.Position.X;
        var feet = (int)(deep.World.Height * 0.7f);
        for (var y = feet - 4; y <= feet; y++)
        for (var dx = -3; dx <= 3; dx++)
            deep.World.SetTile(new GridPoint(x + dx, y), TileId.Air);
        deep.Player.Motion.Position = new Vector2(x, feet + 1 - deep.Player.Motion.Size.Y - 0.01f);
        Tick(deep, 2);
        var wyrmsBefore = FamilySpawns(deep, 10_000, EnemyKind.TunnelWyrm);
        deep.TriggerFracture();
        var wyrmsAfter = FamilySpawns(deep, 2_000, EnemyKind.TunnelWyrm);
        check(deep.CurrentBiome.Underground && wyrmsBefore == 0 && wyrmsAfter > 0,
            $"EN-7 before the Fracture no tunnel wyrm in 10,000 family attempts underground ({wyrmsAfter} in 2,000 after it)");
    }
}
