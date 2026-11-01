using System.Numerics;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// 0.1.9 stream D (PE-4, PE-5, PE-7, PE-14): the simulation's sound events, death records and hit records. A headless
/// game is driven through mining a block to breaking, placing a wall, crafting, drinking, eating, a door, a container,
/// a splash, Tech and Command uses, an explosion, a double jump and the player's death; each emits its event once, and a
/// plain jump or landing emits nothing. Despawns are not kills, a forced crit records a crit, and the animator's dust
/// stays under the particle cap with a drill held for ten seconds.
/// </summary>
public static class SoundEventSelfTests
{
    private const int Floor = 30;

    private static GameSimulation Arena(int seed, int width = 80, int height = 48)
    {
        var game = new GameSimulation(seed: seed, width: width, height: height) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        for (var y = 6; y <= Floor + 4; y++)
        for (var x = 10; x <= width - 10; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y >= Floor ? TileId.Rock : TileId.Air);
            game.World.SetWall(cell, WallIds.None);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            data.Shape = TileShape.Full;
            game.World.SetCell(cell, data);
        }
        game.Player.Motion.Position = new Vector2(40.14f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        Idle(game, 6);
        return game;
    }

    private static void Idle(GameSimulation game, int ticks, MovementInput movement = default)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(movement));
    }

    private static void Press(GameSimulation game, PlayerAction action, GridPoint target)
    {
        for (var i = 0; i < 120 && game.Player.ActionCooldownTicks > 0; i++)
            Idle(game, 1);
        game.Tick(new GameInput(new MovementInput(0f), action, target));
    }

    private static void Select(GameSimulation game, ItemId id, int count = 1)
    {
        if (game.Player.Inventory.Count(id) < count)
            game.Player.Inventory.Add(id, count);
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(id));
    }

    /// <summary>The events emitted since <paramref name="cursor"/> (the cursor moves on).</summary>
    private static List<SoundEvent> Since(GameSimulation game, ref long cursor)
    {
        var list = new List<SoundEvent>();
        game.SoundEvents.Drain(ref cursor, list);
        return list;
    }

    public static void Run(Action<bool, string> check)
    {
        Actions(check);
        Movement(check);
        Deaths(check);
        Hits(check);
        Dust(check);
        Rules(check);
    }

    private static void Actions(Action<bool, string> check)
    {
        var game = Arena(9901);
        var cursor = game.SoundEvents.Next;
        var right = (int)MathF.Floor(game.Player.Motion.Position.X + game.Player.Motion.Size.X) + 1;

        // Mine a block to breaking: digs on every hit, one break with its material.
        var block = new GridPoint(right, Floor - 1);
        game.World.SetTile(block, TileId.Rock);
        Select(game, ItemId.CopperPickaxe);
        for (var i = 0; i < 400 && game.World.TileAt(block.X, block.Y) != TileId.Air; i++)
            game.Tick(new GameInput(new MovementInput(0f), PlayerAction.Mine, block));
        var mined = Since(game, ref cursor);
        var digs = mined.Count(sound => sound.Kind == SoundEventKind.Dig);
        var breaks = mined.Where(sound => sound.Kind == SoundEventKind.Break).ToList();
        check(game.World.TileAt(block.X, block.Y) == TileId.Air && digs >= 2 && breaks.Count == 1 && breaks[0].Variant == "stone" &&
              breaks[0].Detail == (int)TileId.Rock && mined.All(sound => sound.Kind is SoundEventKind.Dig or SoundEventKind.Break or SoundEventKind.Pickup),
            $"sound events: mining basalt to breaking digs on every hit ({digs}) and breaks once as stone ({string.Join(", ", breaks.Select(b => b.Variant))})");

        // The broken block's drop is picked up with a pickup event; credits pick up as coins.
        Idle(game, 90);
        var picked = Since(game, ref cursor);
        game.CollectOrDrop(ItemId.Credits, 5, game.Player.Motion.Position);
        var coins = Since(game, ref cursor);
        check(picked.Count(sound => sound.Kind == SoundEventKind.Pickup) >= 1 &&
              coins.Count == 1 && coins[0] is { Kind: SoundEventKind.Pickup, Variant: "coin" },
            "sound events: picking up the broken block emits a pickup; credits pick up as coins");

        // Place a background wall.
        var wallItem = game.Items.All.First(item => item.UseKind == ItemUseKind.PlaceWall && item.PlacedWall != WallIds.None);
        Select(game, wallItem.Id, 5);
        var wallCell = new GridPoint(right, Floor - 1);
        Press(game, PlayerAction.UseSelected, wallCell);
        var walls = Since(game, ref cursor);
        check(game.World.GetCell(wallCell).WallId == wallItem.PlacedWall &&
              walls.Count(sound => sound.Kind == SoundEventKind.Place) == 1 && walls.Single(sound => sound.Kind == SoundEventKind.Place).Variant == "wall",
            $"sound events: placing a {wallItem.Name} emits one wall place");

        // Craft a hand recipe: one craft event, and no pickup for the crafted result.
        var recipe = game.Crafting.Recipes.First(candidate => (candidate.RequiredStations is null || candidate.RequiredStations.Count == 0) &&
                                                              candidate.RequiredBiomeTags is not { Count: > 0 } && candidate.RequiredLiquids is not { Count: > 0 } &&
                                                              !candidate.RequiresGuideUnlock);
        foreach (var ingredient in recipe.Ingredients)
            game.Player.Inventory.Add(ingredient.Alternatives[0], ingredient.Quantity);
        var crafted = game.TryCraft(recipe.Id);
        var crafts = Since(game, ref cursor);
        check(crafted.Success && crafts.Count == 1 && crafts[0].Kind == SoundEventKind.Craft && crafts[0].Variant == "craft",
            $"sound events: crafting {game.Items[recipe.Output].Name} by hand emits one craft and no pickup ({string.Join(", ", crafts.Select(c => c.Kind))})");

        // Drink a healing item and a buff potion; eat food.
        var stim = game.Items.All.First(item => item.UseKind == ItemUseKind.Consumable && item.HealAmount > 0f && item.ChargeRestore <= 0f &&
                                                item.SummonsBoss is null && item.StartsEvent is null);
        Select(game, stim.Id);
        Press(game, PlayerAction.UseSelected, wallCell);
        var drinks = Since(game, ref cursor);
        Select(game, ItemId.RestorativeTonic);
        Press(game, PlayerAction.UseSelected, wallCell);
        var tonic = Since(game, ref cursor);
        Select(game, ItemId.Berry);
        Press(game, PlayerAction.Eat, wallCell);
        var meals = Since(game, ref cursor);
        check(drinks.Count(sound => sound.Kind == SoundEventKind.Drink) == 1 && tonic.Count(sound => sound.Kind == SoundEventKind.Drink) == 1 &&
              meals.Count(sound => sound.Kind == SoundEventKind.Eat) == 1,
            $"sound events: drinking a {stim.Name} and a tonic each emit one drink; eating emits one eat");

        // A door opens and closes; a container opens and (when its panel closes) shuts.
        var door = new GridPoint(right + 2, Floor - 1);
        for (var dy = 0; dy < 3; dy++)
            game.World.SetTile(new GridPoint(door.X, door.Y - dy), TileId.PlankDoor);
        game.Interact(door);
        var opened = Since(game, ref cursor);
        game.Interact(door);
        var closed = Since(game, ref cursor);
        check(opened.Count == 1 && opened[0].Kind == SoundEventKind.DoorOpen && closed.Count == 1 && closed[0].Kind == SoundEventKind.DoorClose,
            "sound events: a door emits one open, then one close");
        var crate = new GridPoint(right - 4, Floor - 1);
        game.World.SetTile(crate, TileId.StorageCrate);
        var container = game.Interact(crate);
        var lid = Since(game, ref cursor);
        game.CloseContainer(crate);
        var shut = Since(game, ref cursor);
        check(container.Kind == WorldInteractionKind.ContainerOpened && lid.Count == 1 && lid[0].Kind == SoundEventKind.ChestOpen &&
              shut.Count == 1 && shut[0].Kind == SoundEventKind.ChestClose,
            "sound events: a container emits one open, and one close when its panel closes");

        // Tech: a charge-powered staff emits one use per discharge; Command: a drone remote emits one use per deploy.
        var staff = game.Items.All.First(item => item.UseKind == ItemUseKind.Tech && item.Style is WeaponStyle.Staff && item.ChargeCost > 0 &&
                                                 item.ChargeCost < game.Charge);
        Select(game, staff.Id);
        Press(game, PlayerAction.UseSelected, new GridPoint(right + 6, Floor - 2));
        var tech = Since(game, ref cursor);
        Select(game, ItemId.ScoutDroneRemote);
        Press(game, PlayerAction.UseSelected, new GridPoint(right + 3, Floor - 2));
        var command = Since(game, ref cursor);
        check(tech.Count(sound => sound is { Kind: SoundEventKind.Use, Variant: "tech" }) == 1 &&
              command.Count(sound => sound is { Kind: SoundEventKind.Use, Variant: "command" }) == 1 && game.ActiveDrones == 1,
            $"sound events: discharging a {staff.Name} and launching a drone each emit one use (tech, command)");

        // An explosion.
        game.Explode(new Vector2(right + 12f, Floor - 3f), 2f, 0, 0f);
        var blast = Since(game, ref cursor);
        check(blast.Count(sound => sound.Kind == SoundEventKind.Explosion) == 1, "sound events: an explosion emits one explosion");
    }

    private static void Movement(Action<bool, string> check)
    {
        // D-59: a plain jump and its landing are silent.
        var game = Arena(9902);
        var cursor = game.SoundEvents.Next;
        Idle(game, 1, new MovementInput(0f, JumpPressed: true, JumpHeld: true));
        Idle(game, 20, new MovementInput(0f, JumpHeld: true));
        var airborne = !game.Player.Motion.Grounded;
        Idle(game, 120);
        var jump = Since(game, ref cursor);
        check(airborne && game.Player.Motion.Grounded && jump.Count == 0,
            $"sound events: a plain jump and landing emit nothing (D-59) ({string.Join(", ", jump.Select(sound => sound.Kind))})");

        // A double jump emits one.
        game.Player.Inventory.Add(ItemId.DoubleJumpCanister, 1);
        game.Player.Equipment.Set(EquipmentSlot.Accessory, ItemId.DoubleJumpCanister);
        Idle(game, 3);
        cursor = game.SoundEvents.Next;
        Idle(game, 1, new MovementInput(0f, JumpPressed: true, JumpHeld: true));
        Idle(game, 11, new MovementInput(0f, JumpHeld: true));
        Idle(game, 1, new MovementInput(0f, JumpPressed: true, JumpHeld: true));
        Idle(game, 2);
        var doubled = Since(game, ref cursor);
        Idle(game, 200);
        check(doubled.Count(sound => sound.Kind == SoundEventKind.DoubleJump) == 1, "sound events: a double jump emits one double jump");

        // Entering and leaving water splash once each.
        cursor = game.SoundEvents.Next;
        var center = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
        var pool = new List<GridPoint>();
        for (var y = (int)center.Y - 2; y < Floor; y++)
        for (var x = (int)center.X - 2; x <= (int)center.X + 2; x++)
            pool.Add(new GridPoint(x, y));
        foreach (var cell in pool)
        {
            var data = game.World.GetCell(cell);
            data.LiquidKind = LiquidKind.Water;
            data.LiquidDepth = 1f;
            game.World.SetCell(cell, data);
        }
        Idle(game, 3);
        var entered = Since(game, ref cursor);
        foreach (var cell in pool)
        {
            var data = game.World.GetCell(cell);
            data.LiquidKind = LiquidKind.None;
            data.LiquidDepth = 0f;
            game.World.SetCell(cell, data);
        }
        Idle(game, 3);
        var left = Since(game, ref cursor);
        check(entered.Count(sound => sound is { Kind: SoundEventKind.Splash, Variant: "water", Detail: 1 }) == 1 &&
              left.Count(sound => sound is { Kind: SoundEventKind.Splash, Variant: "water", Detail: 0 }) == 1,
            "sound events: entering and leaving water each splash once");

        // The player's death emits one.
        cursor = game.SoundEvents.Next;
        game.Player.TakeDamage(1e6f);
        Idle(game, 30);
        var died = Since(game, ref cursor);
        check(died.Count(sound => sound.Kind == SoundEventKind.PlayerDeath) == 1, "sound events: the player's death emits one death");
    }

    private static void Deaths(Action<bool, string> check)
    {
        // PE-5: twenty creatures wandering off out of sight are not kills.
        var game = Arena(9903, 220, 70);
        var deaths = game.RecentDeaths.Next;
        var origin = game.Player.Motion.Position;
        var leavers = new List<EnemyAgent>();
        for (var i = 0; i < 20; i++)
            leavers.Add(game.SpawnCreatureAt(EnemyKind.MossSlime, origin + new Vector2(i % 2 == 0 ? 70f + i : -70f - i, -4f)));
        for (var t = 0; t < 10; t++)
            game.ProbeSpawnTick(0.5f);
        var records = new List<DeathRecord>();
        game.RecentDeaths.Drain(ref deaths, records);
        var animator = new ActorAnimator();
        animator.Update(game, 0, 1f / 60f);
        check(leavers.All(enemy => !game.Enemies.Contains(enemy)) && records.Count(record => record.Reason == DeathReason.Despawned) == 20 &&
              records.All(record => record.Reason != DeathReason.Died),
            $"death records: 20 creatures despawning off-screen record 20 despawns and no deaths ({records.Count} records)");

        // A kill is a death; a bug-net catch is a catch.
        var kills = Arena(9904);
        var cursor = kills.RecentDeaths.Next;
        var animated = new ActorAnimator();
        animated.Update(kills, 0, 1f / 60f);
        var victim = kills.SpawnCreatureAt(EnemyKind.MossSlime, kills.Player.Motion.Position + new Vector2(4f, -1f));
        Idle(kills, 1);
        animated.Update(kills, 0.02, 1f / 60f);
        victim.TakeDamage(1e6f);
        Idle(kills, 2);
        animated.Update(kills, 0.04, 1f / 60f);
        var burst = animated.ParticleCount;
        var killed = new List<DeathRecord>();
        kills.RecentDeaths.Drain(ref cursor, killed);
        check(killed.Count == 1 && killed[0].Reason == DeathReason.Died && killed[0].Kind == EnemyKind.MossSlime && burst > 0,
            $"death records: a kill records one death and the animator bursts ({burst} particles)");

        var catchable = EnemyArchetypes.All.FirstOrDefault(archetype => archetype.Catchable && Enum.TryParse<ItemId>(archetype.Kind.ToString(), out _));
        if (catchable is not null)
        {
            var net = Arena(9905);
            var netCursor = net.RecentDeaths.Next;
            var soundCursor = net.SoundEvents.Next;
            var critter = net.SpawnCreatureAt(catchable.Kind, net.Player.Motion.Position + new Vector2(1.6f, 0f));
            Select(net, ItemId.BugNet);
            var at = critter.Motion.Position + critter.Motion.Size / 2f;
            Press(net, PlayerAction.UseSelected, new GridPoint((int)at.X, (int)at.Y));
            var caught = new List<DeathRecord>();
            net.RecentDeaths.Drain(ref netCursor, caught);
            var sounds = Since(net, ref soundCursor);
            check(caught.Count == 1 && caught[0].Reason == DeathReason.Caught && sounds.Any(sound => sound.Kind == SoundEventKind.Catch),
                $"death records: netting a {catchable.DisplayName} records a catch, not a death");
        }
    }

    private static void Hits(Action<bool, string> check)
    {
        // PE-14: a forced crit records a crit; a plain hit does not; afflicted creatures that took no hit read as damage over time.
        var game = Arena(9906);
        var cursor = game.HitRecords.Next;
        var target = game.SpawnCreatureAt(EnemyKind.AstroBeast, game.Player.Motion.Position + new Vector2(3f, -0.5f));
        game.ProbeStrike(target, 10f, critical: true);
        game.ProbeStrike(target, 10f, critical: false);
        var hits = new List<HitRecord>();
        game.HitRecords.Drain(ref cursor, hits);
        check(hits.Count == 2 && hits[0] is { Critical: true, Kind: HitKind.Direct } && hits[0].TargetId == target.Id && hits[0].Amount > 0f &&
              !hits[1].Critical,
            $"hit records: a forced crit records a crit hit ({hits.FirstOrDefault().Amount:0} damage), a plain hit does not");

        var animator = new ActorAnimator();
        animator.Update(game, 0, 1f / 60f);
        game.ProbeStrike(target, 12f, critical: true);
        animator.Update(game, 0.016, 1f / 60f);
        game.ProbeStrike(target, 12f, critical: true);
        animator.Update(game, 0.032, 1f / 60f);
        var texts = animator.Texts.ToList();
        check(texts.Count == 1 && texts[0].Critical && texts[0].Kind == HitKind.Direct,
            $"combat text: a crit draws a crit number, and rapid hits on one target stack into it ({string.Join(", ", texts.Select(t => t.Text))})");

        check(CombatTextRules.ClassifyCreatureLoss(false, false, true) == (HitKind.Dot, false) &&
              CombatTextRules.ClassifyCreatureLoss(true, true, true) == (HitKind.Direct, true) &&
              CombatTextRules.ClassifyCreatureLoss(false, false, false) == (HitKind.Direct, false) &&
              CombatTextRules.ClassifyPlayerLoss(false, true, false) == HitKind.Dot &&
              CombatTextRules.ClassifyPlayerLoss(true, true, false) == HitKind.Direct &&
              CombatTextRules.ClassifyPlayerLoss(false, false, false) == HitKind.Direct,
            "combat text: unhit losses on afflicted creatures and poisoned players read as damage over time; hits and falls do not");

        // Charge from an item floats up in blue (a charge record).
        var charged = Arena(9907);
        var chargeCursor = charged.HitRecords.Next;
        charged.ProbeSpendCharge(charged.MaximumCharge);
        var cell = charged.Items.All.First(item => item.ChargeRestore > 0f && item.HealAmount <= 0f && item.UseKind == ItemUseKind.Consumable);
        Select(charged, cell.Id);
        Press(charged, PlayerAction.UseSelected, new GridPoint(45, Floor - 1));
        var gains = new List<HitRecord>();
        charged.HitRecords.Drain(ref chargeCursor, gains);
        check(gains.Count(hit => hit.Kind == HitKind.Charge && hit.Amount > 0f) == 1, $"hit records: drinking a {cell.Name} records one charge gain");
    }

    private static void Dust(Action<bool, string> check)
    {
        // PE-7: a drill held for ten seconds keeps kicking up dust, and the particle pool never passes its cap of 700.
        var game = Arena(9908);
        var animator = new ActorAnimator();
        animator.Update(game, 0, 1f / 60f);
        Select(game, ItemId.CopperPickaxe);
        var right = (int)MathF.Floor(game.Player.Motion.Position.X + game.Player.Motion.Size.X) + 1;
        var target = new GridPoint(right, Floor - 1);
        var most = 0;
        var any = 0;
        for (var tick = 1; tick <= 600; tick++)
        {
            if (game.World.TileAt(target.X, target.Y) == TileId.Air)
                game.World.SetTile(target, tick % 2 == 0 ? TileId.Rock : TileId.Soil);
            game.Tick(new GameInput(new MovementInput(0f), PlayerAction.Mine, target));
            animator.Update(game, tick / 60.0, 1f / 60f);
            most = Math.Max(most, animator.ParticleCount);
            any += animator.ParticleCount > 0 ? 1 : 0;
        }
        for (var i = 0; i < 1000; i++)
            animator.Emit(Vector2.Zero, Vector2.Zero, System.Drawing.Color.White, 10.1, 1f, 2f);
        check(any > 60 && most <= 700 && animator.ParticleCount == 700,
            $"dust: a drill held 10 s kicks up dust (busiest frame {most} particles) and the pool holds at its cap of 700");

        var tiles = new TileCatalog();
        check(SoundEventQueue.Material(tiles[TileId.Rock]) == "stone" && SoundEventQueue.Material(tiles[TileId.Soil]) == "soil" &&
              SoundEventQueue.Material(tiles[TileId.Wood]) == "metal" && SoundEventQueue.Material(tiles[TileId.JungleGrass]) == "plant" &&
              SoundEventQueue.Material(tiles[TileId.CopperOre]) == "stone",
            "dust: tiles sound and crumble by material (basalt and ore stone, regolith soil, hull panel metal, vine mat plant)");
    }

    private static void Rules(Action<bool, string> check)
    {
        // Sound spam: each kind plays at most every few hundredths of a second.
        var limiter = new SoundRateLimiter();
        var played = 0;
        for (var i = 0; i < 60; i++)
            played += limiter.Allow("dig", i / 600.0) ? 1 : 0; // 60 digs in 0.1 s
        check(played is >= 1 and <= 2 && limiter.Allow("break", 0.05), $"rate limits: 60 digs in 0.1 s play {played} sounds; other kinds are unaffected");

        // A queue nobody reads stays bounded, and one tick never doubles a sound.
        var queue = new SoundEventQueue { Tick = 1 };
        queue.Emit(SoundEventKind.Drink, "", Vector2.Zero);
        queue.Emit(SoundEventKind.Drink, "", Vector2.Zero);
        queue.Tick = 2;
        for (var i = 0; i < 5000; i++)
        {
            queue.Tick = 3 + i;
            queue.Emit(SoundEventKind.Dig, "stone", Vector2.Zero);
        }
        long fresh = 0;
        var held = new List<SoundEvent>();
        queue.Drain(ref fresh, held);
        check(held.Count == queue.Capacity && queue.Next == 5001, $"sound queue: one tick never doubles a sound, and an unread queue keeps only its last {queue.Capacity}");

        // Every event the simulation can emit has a cue that falls back to a real sound.
        var game = new GameSimulation(seed: 9909, width: 80, height: 48);
        var missing = new List<string>();
        foreach (var kind in Enum.GetValues<SoundEventKind>())
        foreach (var variant in new[] { "", "stone", "soil", "plant", "glass", "metal", "wall", "tech", "command", "smelt", "forge", "brew", "weave",
                     "craft", "buy", "sell", "coin", "water", "lava", "acid", "fuel", "nectar" })
        {
            if (AudioDirector.CueFor(game, new SoundEvent(kind, variant, Vector2.Zero)) is not { } cue)
                continue;
            var at = cue.Cue;
            for (var step = 0; step < 12 && at is not null && !AudioEngine.SynthesisedCues.Contains(at); step++)
                at = SoundCatalog.Fallback(at);
            if (at is null)
                missing.Add($"{kind}/{variant}->{cue.Cue}");
        }
        foreach (var extra in new[] { "catch_critter", "death_sting", "impact_crit", "potion", "chest_open", "chest_close", "eat" })
        {
            var at = extra;
            for (var step = 0; step < 12 && at is not null && !AudioEngine.SynthesisedCues.Contains(at); step++)
                at = SoundCatalog.Fallback(at);
            if (at is null)
                missing.Add(extra);
        }
        check(missing.Count == 0, $"sound cues: every simulation sound event plays a cue that reaches a synthesised effect (missing: {string.Join(", ", missing.Take(6))})");
    }
}
