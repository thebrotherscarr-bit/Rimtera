using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Deterministic probes for MECHANICS_PARITY.md section C (combat, health and spawning), one block per behaviour.
/// Results are recorded in docs/parity/C_combat.md.
/// </summary>
public static class ParityCombatTests
{
    private static GameSimulation NewGame(int seed, int width = 80, int height = 48)
    {
        var game = new GameSimulation(seed: seed, width: width, height: height) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static void Tick(GameSimulation game, int ticks = 1, PlayerAction action = PlayerAction.None, GridPoint? target = null)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(default, i == 0 ? action : PlayerAction.None, target));
    }

    /// <summary>Clears an air box with a rock floor and stands the player on it.</summary>
    private static void Arena(GameSimulation game, int left, int top, int right, int floorY)
    {
        for (var y = top; y <= floorY + 1; y++)
        for (var x = left; x <= right; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y >= floorY ? TileId.Rock : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            game.World.SetCell(cell, data);
        }
        game.Player.Motion.Position = new Vector2((left + right) / 2f, floorY - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        Tick(game, 2);
    }

    private static GridPoint CellOf(Vector2 point) => new((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y));

    private static Vector2 Center(EnemyAgent enemy) => enemy.Motion.Position + enemy.Motion.Size / 2f;

    public static void Run(Action<bool, string> check)
    {
        // ---- C1 damage spread and crits ----
        var rolls = NewGame(9701);
        var plain = Enumerable.Range(0, 400).Select(_ => rolls.ProbeRollHit(100f, 0)).ToArray();
        check(plain.All(value => value is >= 85f - 0.01f and <= 115f + 0.01f) && plain.Min() < 90f && plain.Max() > 110f &&
              Math.Abs(plain.Average() - 100f) < 2f,
            $"C1 weapon damage varies up to 15% per hit ({plain.Min():0.0}-{plain.Max():0.0} of 100)");
        var crits = Enumerable.Range(0, 200).Select(_ => rolls.ProbeRollHit(100f, 100)).ToArray();
        check(crits.All(value => value is >= 169.9f and <= 230.1f) && rolls.LastHitCritical,
            "C1 a critical hit doubles the (varied) damage");
        var catalog = new ItemCatalog();
        var critGear = catalog.All.FirstOrDefault(item => item.CritBonus > 0 && item.EquipmentSlot is { } slot && slot < EquipmentSlot.Accessory);
        var critWeapon = catalog.All.First(item => item.CritChance > 0 && item.Damage > 0);
        var critGame = NewGame(9702);
        var bare = critGame.ProbeCritChance(critWeapon);
        if (critGear is not null)
            critGame.Player.Equipment.Set(critGear.EquipmentSlot!.Value, critGear.Id);
        var baseCrit = critWeapon.EffectiveClass == DamageClass.Command ? 0 : CombatTuning.BaseCritChance;
        check(bare == baseCrit + critWeapon.CritChance &&
              critGame.ProbeCritChance(critWeapon) == baseCrit + critWeapon.CritChance + critGame.CombatStats.CritBonus &&
              (critGear is null || critGame.CombatStats.CritBonus > 0),
            "C1 crit chance is 4% base plus the weapon's own plus worn crit bonuses");

        // ---- C2 knockback and resistance ----
        var knock = NewGame(9703);
        float Push(EnemyKind kind)
        {
            var enemy = knock.SpawnCreatureAt(kind, new Vector2(10f, 5f));
            enemy.Motion.Velocity = Vector2.Zero;
            knock.ProbeKnock(enemy, new Vector2(6f, 0f));
            return enemy.Motion.Velocity.X;
        }
        var light = Push(EnemyKind.MossSlime);
        var heavy = Push(EnemyKind.AstroBeast);
        var gate = Push(EnemyKind.StoneSentinel);
        var final = Push(EnemyKind.AstroGod);
        check(Math.Abs(light - 6f) < 0.01f && heavy < light && heavy > 0f && gate < 1f && final == 0f,
            $"C2 knockback is cut by resistance (light {light:0.0}, heavy {heavy:0.0}, gate boss {gate:0.00}, final boss {final:0})");

        // ---- C3 per-hit immunity: a swing hits once, piercing shots hit each creature once ----
        var swing = NewGame(9704);
        Arena(swing, 10, 4, 70, 30);
        swing.Player.Inventory.Add(ItemId.FerriteLongblade, 1);
        swing.TrySelectInventorySlot(swing.Player.Inventory.FindSlot(ItemId.FerriteLongblade));
        var dummy = swing.SpawnCreatureAt(EnemyKind.AstroBeast, swing.Player.Motion.Position + new Vector2(1.1f, 0.2f));
        var hits = 0;
        var last = dummy.HitPoints;
        for (var t = 0; t < 30; t++)
        {
            Tick(swing, 1, t == 0 ? PlayerAction.UseSelected : PlayerAction.None, CellOf(Center(dummy)));
            if (dummy.HitPoints < last - 0.001f)
                hits++;
            last = dummy.HitPoints;
        }
        check(hits == 1, $"C3 one broadsword swing hits a creature exactly once ({hits} hits)");
        var pierce = NewGame(9705);
        Arena(pierce, 10, 4, 70, 30);
        var target = pierce.SpawnCreatureAt(EnemyKind.AstroBeast, pierce.Player.Motion.Position + new Vector2(5f, 0.3f));
        var before = target.HitPoints;
        pierce.ProbeFireProjectile(Center(target) - new Vector2(4f, 0f), new Vector2(12f, 0f), 10f, pierce: 3);
        Tick(pierce, 40);
        var marked = NewGame(9706).SpawnCreatureAt(EnemyKind.MossSlime, new Vector2(10f, 5f));
        marked.MarkHitBy(-1, 0.4f);
        // The ±15% roll lands before the creature's defense comes off (0.1.9: creatures have defense).
        var pierceLow = CombatTuning.CreatureDamageTaken(8f, target.Archetype.Defense, 0, false);
        var pierceHigh = CombatTuning.CreatureDamageTaken(12f, target.Archetype.Defense, 0, false);
        check(before - target.HitPoints >= pierceLow - 0.01f && before - target.HitPoints <= pierceHigh + 0.01f && !marked.CanBeHitBy(-1) && marked.CanBeHitBy(7),
            $"C3 a piercing shot hits a creature once ({before - target.HitPoints:0.#} of 10 +-15% less defense {target.Archetype.Defense}) and immunity is per hit source");

        // ---- C4 defense (verified; DefenseSelfTests holds the detail) ----
        var guard = NewGame(9707);
        check(Math.Abs(guard.GearProbeHit(20f) - 20f) < 0.01f && DefenseBalance.ApplyDefense(3f, 80, WorldDifficulty.Classic) >= 1f,
            "C4 defense subtracts a share and leaves at least 1 damage");

        // ---- C5 life regeneration ----
        var regen = NewGame(9708);
        Arena(regen, 10, 4, 70, 30);
        regen.Player.TakeDamage(50f, DamageKind.Blunt);
        var hurt = regen.Player.HitPoints;
        Tick(regen, 120); // 2 s: no natural regeneration before the first 5 s step
        var early = regen.Player.HitPoints - hurt;
        Tick(regen, 60 * 9); // to 11 s (two steps)
        var a = regen.Player.HitPoints;
        Tick(regen, 60);
        var rateEarly = regen.Player.HitPoints - a;
        Tick(regen, 60 * 29); // to 41 s (six steps)
        var b = regen.Player.HitPoints;
        Tick(regen, 60);
        var rateLate = regen.Player.HitPoints - b;
        var belowMax = regen.Player.HitPoints < regen.Player.MaximumHitPoints;
        regen.Player.TakeDamage(5f, DamageKind.Blunt);
        Tick(regen, 2);
        var afterHit = regen.SecondsSinceHurt;
        var c = regen.Player.HitPoints;
        Tick(regen, 60);
        check(early < 0.01f && rateEarly > 0f && belowMax && rateLate > rateEarly * 1.5f &&
              afterHit < 0.1f && regen.Player.HitPoints - c < 0.01f,
            $"C5 regeneration waits after a hurt, grows with time unhurt ({rateEarly:0.00}/s -> {rateLate:0.00}/s) and stops on a hit");
        check(CombatTuning.NaturalLifeRegen(20f, 100f, true) > CombatTuning.NaturalLifeRegen(20f, 100f, false),
            "C5 regeneration is faster standing still");
        var boosted = NewGame(9709);
        Arena(boosted, 10, 4, 70, 30);
        boosted.Player.TakeDamage(30f, DamageKind.Blunt);
        boosted.Player.Statuses.Apply(StatusId.Regeneration, 5f);
        var boostedStart = boosted.Player.HitPoints;
        Tick(boosted, 120);
        check(boosted.Player.HitPoints - boostedStart > 0.01f, "C5 regeneration bonuses add on top (they work even inside the delay)");

        // ---- C6 charge regeneration and charge sickness ----
        var charge = NewGame(9710);
        Arena(charge, 10, 4, 70, 30);
        charge.ProbeSpendCharge(30f);
        var spent = charge.Charge;
        Tick(charge, 30); // 0.5 s
        var during = charge.Charge - spent;
        Tick(charge, 60 * 3);
        check(during < 0.01f && charge.Charge > spent + 1f,
            "C6 charge waits a short delay after spending, then flows back");
        check(CombatTuning.ChargeRegenDelayTicks(18f, 20f, false) < CombatTuning.ChargeRegenDelayTicks(0f, 20f, false) &&
              CombatTuning.ChargeRegenUnits(5f, 20f, 0f, false, false) < CombatTuning.ChargeRegenUnits(15f, 20f, 0f, false, false) &&
              CombatTuning.ChargeRegenUnits(10f, 20f, 0f, true, false) > CombatTuning.ChargeRegenUnits(10f, 20f, 0f, false, false),
            "C6 the delay is longer the emptier the pool; regeneration is faster when fuller and when still");
        var sick = NewGame(9711);
        var tech = sick.Items.All.First(item => item.EffectiveClass == DamageClass.Tech && item.Damage > 0);
        var techBefore = sick.ComputeWeaponDamage(tech);
        sick.ProbeSpendCharge(60f);
        sick.Player.Inventory.Add(ItemId.ChargeCellSmall, 1);
        var drank = sick.QuickCharge();
        check(drank && sick.ChargeSicknessSeconds > 0f && sick.ComputeWeaponDamage(tech) < techBefore,
            "C6 charge restoratives bring charge sickness (less Tech damage for a while)");

        // ---- C7 quick heal picks the smallest potion that covers the hurt ----
        var healers = catalog.All.Where(item => item.HealAmount > 0f && item.GrantsStatus is null).OrderBy(item => item.HealAmount)
            .DistinctBy(item => item.HealAmount).ToArray();
        if (healers.Length >= 2)
        {
            var heal = NewGame(9712);
            foreach (var item in healers)
                heal.Player.Inventory.Add(item.Id, 1);
            heal.Player.TakeDamage(healers[0].HealAmount - 2f, DamageKind.Blunt);
            heal.QuickHeal();
            check(heal.Player.Inventory.Count(healers[0].Id) == 0 && healers.Skip(1).All(item => heal.Player.Inventory.Count(item.Id) == 1) &&
                  heal.PotionSick && !heal.QuickHeal(),
                "C7 quick heal uses the smallest potion that heals enough; potion sickness blocks the next");
        }
        else
            check(true, "C7 quick heal (single healing item in the catalog)");

        // ---- C8 death: credits share, tombstone ----
        int CreditsLeft(CharacterDifficulty difficulty, out int onGround, out bool tomb)
        {
            var dead = NewGame(9713);
            Arena(dead, 10, 4, 70, 30);
            dead.Player.Inventory.Add(ItemId.Credits, 100);
            dead.Player.TakeDamage(1e6f);
            Tick(dead, 2);
            tomb = dead.Signs.Any(pair => dead.World.GetCell(pair.Key).TileId == TileId.Tombstone);
            dead.RespawnPlayer(difficulty);
            onGround = dead.GroundItems.Where(item => item.ItemId == ItemId.Credits).Sum(item => item.Count);
            return dead.Player.Inventory.Count(ItemId.Credits);
        }
        var classicLeft = CreditsLeft(CharacterDifficulty.Classic, out var classicGround, out var tombstone);
        var ironLeft = CreditsLeft(CharacterDifficulty.Ironman, out var ironGround, out _);
        var explorerLeft = CreditsLeft(CharacterDifficulty.Explorer, out var explorerGround, out _);
        check(tombstone && classicLeft == 60 && classicGround == 40 && ironLeft == 0 && ironGround == 100 && explorerLeft == 100 && explorerGround == 0,
            $"C8 death drops a share of credits where you fell (Classic {classicGround}, Ironman {ironGround}, Explorer {explorerGround}) and leaves a tombstone");

        // ---- C9/C10 spawning: safe radius, rate, cap, despawn ----
        (int Count, bool Safe, bool Open) Spawns(Action<GameSimulation>? setup, int seed = 9714)
        {
            var game = NewGame(seed, 220, 70);
            setup?.Invoke(game);
            var seen = new HashSet<long>(game.Enemies.Select(enemy => enemy.Id));
            var count = 0;
            var safe = true;
            var open = true;
            for (var attempt = 0; attempt < 500; attempt++)
            {
                game.ProbeSpawnTick(0.65f);
                foreach (var enemy in game.Enemies.Where(enemy => seen.Add(enemy.Id)).ToArray())
                {
                    count++;
                    safe &= MathF.Abs(enemy.Motion.Position.X - game.Player.Motion.Position.X) >= 18f;
                    for (var y = (int)enemy.Motion.Position.Y; y < enemy.Motion.Position.Y + enemy.Motion.Size.Y - 0.01f; y++)
                    for (var x = (int)enemy.Motion.Position.X; x < enemy.Motion.Position.X + enemy.Motion.Size.X - 0.01f; x++)
                        open &= !game.Tiles[game.World.GetCell(new GridPoint(x, y)).TileId].IsSolid;
                    enemy.TakeDamage(1e6f);
                }
            }
            return (count, safe, open);
        }
        var day = Spawns(null);
        var night = Spawns(game =>
        {
            game.ProbeAdvanceHours(13f);
            foreach (var boss in game.Enemies.Where(enemy => enemy.IsBoss))
                boss.TakeDamage(1e6f);
        });
        var town = Spawns(game =>
        {
            foreach (var kind in new[] { TownNpcKind.Quartermaster, TownNpcKind.Medic, TownNpcKind.StationAi })
                game.AddTownNpc(kind, game.Player.Motion.Position + new Vector2(3f, 0f), bound: false);
        });
        check(day.Count > 0 && day.Safe && night.Safe, $"C9 spawns land outside the safe radius ({day.Count} day spawns)");
        check(night.Count > day.Count && town.Count < day.Count,
            $"C9 more spawns at night ({night.Count}) than by day ({day.Count}); fewer in a populated town ({town.Count})");
        var factors = CombatTuning.SpawnFactors(false, true, false, 0, false);
        var blight = CombatTuning.SpawnFactors(false, false, true, 0, false);
        var nightF = CombatTuning.SpawnFactors(true, false, false, 0, false);
        check(factors.Rate > 1f && factors.Cap > 1f && blight.Rate > 1f && nightF.Cap > 1f,
            "C9 rate and cap rise underground, at night and in the blight");
        var drift = NewGame(9715, 220, 70);
        var px = drift.Player.Motion.Position;
        var stray = drift.SpawnCreatureAt(EnemyKind.MossSlime, px + new Vector2(70f, -4f));
        var eventUnit = drift.SpawnCreatureAt(EnemyKind.MossSlime, px + new Vector2(-70f, -4f));
        eventUnit.EventTag = Enum.GetValues<WorldEventKind>()[0];
        var farBoss = drift.SpawnCreatureAt(EnemyKind.StoneSentinel, px + new Vector2(75f, -6f));
        for (var t = 0; t < 8; t++)
            drift.ProbeSpawnTick(0.5f);
        check(!drift.Enemies.Contains(stray) && drift.Enemies.Contains(eventUnit) && drift.Enemies.Contains(farBoss),
            "C9 far creatures despawn; bosses and event units stay");
        check(day.Open && night.Open, "C10 spawns appear in open space (ground walkers on ground, flyers in clear air)");

        // ---- C11 contact damage, immunity frames, harmless critters ----
        var contact = NewGame(9716);
        Arena(contact, 10, 4, 70, 30);
        var full = contact.Player.HitPoints;
        contact.SpawnCreatureAt(EnemyKind.MossSlime, contact.Player.Motion.Position + new Vector2(0.2f, 1.1f));
        var touched = false;
        for (var t = 0; t < 60 && !touched; t++)
        {
            Tick(contact);
            touched = contact.Player.HitPoints < full;
        }
        var immune = contact.Player.IsImmune;
        var critter = NewGame(9717);
        Arena(critter, 10, 4, 70, 30);
        var calm = critter.Player.HitPoints;
        critter.SpawnCreatureAt(EnemyKind.NeoTerraDeer, critter.Player.Motion.Position + new Vector2(0.2f, 0.6f));
        Tick(critter, 180);
        check(touched && immune && critter.Player.HitPoints >= calm, "C11 touching a creature hurts (then immunity frames); critters never hurt");

        // ---- C12 bosses leave ----
        var lonely = NewGame(9718);
        Arena(lonely, 10, 4, 70, 30);
        lonely.TrySummonNextBoss();
        lonely.Player.TakeDamage(1e6f);
        Tick(lonely, 30);
        var stillThere = lonely.BossBlocksRespawn;
        Tick(lonely, 60 * 4);
        check(stillThere && !lonely.Enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive), "C12 a boss leaves once the player is dead");
        var dawn = NewGame(9719);
        dawn.ProbeAdvanceHours(13f); // 21:00
        var nightBoss = dawn.SpawnCreatureAt(EnemyArchetypes.EventBosses[0], dawn.Player.Motion.Position + new Vector2(8f, -6f));
        var gateBoss = dawn.SpawnCreatureAt(EnemyKind.StoneSentinel, dawn.Player.Motion.Position + new Vector2(-8f, -3f));
        dawn.ProbeSpawnTick(0.01f);
        var atNight = dawn.Enemies.Contains(nightBoss);
        dawn.ProbeAdvanceHours(9.5f); // 06:30, crossing dawn
        dawn.ProbeSpawnTick(0.01f);
        check(atNight && !dawn.Enemies.Contains(nightBoss) && dawn.Enemies.Contains(gateBoss), "C12 a dusk boss leaves at dawn; others stay");
        check(CombatTuning.SpawnFactors(false, false, false, 0, true).Rate < 0.5f, "C12 regular spawns drop while a boss is alive");
    }
}
