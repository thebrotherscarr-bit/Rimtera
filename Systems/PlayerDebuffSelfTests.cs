using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// 0.1.9 combat core, stream B: creature debuffs on the player (IC-3), the extra creature afflictions (EN-17), contact
/// boxes (EN-12), the hurt-path polish (IC-18) and the consistency fixes (IC-21). See GameSimulation.Debuffs.cs.
/// </summary>
public static class PlayerDebuffSelfTests
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
            data.WallId = WallIds.None;
            game.World.SetCell(cell, data);
        }
        game.Player.Motion.Position = new Vector2(30f, 20f - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        foreach (var agent in game.Settlers)
            agent.Motion.Position = new Vector2(10f, 20f - agent.Motion.Size.Y - 0.01f);
        // Bare body: no worn armour or accessories, so defense and immunities come only from what a test puts on.
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            game.Player.Equipment.Set(slot, null);
        Tick(game, 3);
        return game;
    }

    private static void Tick(GameSimulation game, int ticks = 1, float horizontal = 0f, PlayerAction action = PlayerAction.None,
        GridPoint? target = null)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(horizontal), action, target));
    }

    private static Vector2 Center(GameSimulation game) => game.Player.Motion.Position + game.Player.Motion.Size / 2f;

    /// <summary>A creature parked far outside its alert radius (so it never comes for the player on its own).</summary>
    private static EnemyAgent Far(GameSimulation game, EnemyKind kind)
    {
        var enemy = game.SpawnCreature(kind, new Vector2(72f, 18f));
        enemy.Motion.Position = new Vector2(72f, 20f - enemy.Motion.Size.Y - 0.01f);
        enemy.Motion.Velocity = Vector2.Zero;
        return enemy;
    }

    public static void Run(Action<bool, string> check)
    {
        InflictedDebuffs(check);
        DebuffEffects(check);
        CreatureAfflictions(check);
        ContactBoxes(check);
        HurtPath(check);
        Consistency(check);
    }

    // ------------------------------------------------------------------ IC-3: creatures inflict, accessories block

    private static void InflictedDebuffs(Action<bool, string> check)
    {
        var listed = CreatureInflictions.All;
        check(listed.Count >= 40 && listed.All(pair => EnemyArchetypes.Contains(pair.Key) &&
                  EnemyArchetypes.Get(pair.Key).Inflicts.SequenceEqual(pair.Value)) &&
              listed.Values.SelectMany(list => list).Select(entry => entry.Status).Distinct().Count() >= 12 &&
              listed.Values.SelectMany(list => list).All(entry => entry.Chance is > 0f and <= 1f && entry.Seconds > 0f),
            $"creature archetypes carry their inflicted debuffs ({listed.Count} kinds, every status from poison to silence)");

        var game = NewGame(9301);
        var spider = Far(game, EnemyKind.SilkSpiderMite);
        var (_, chance, _) = spider.Archetype.Inflicts.Single(entry => entry.Status == StatusId.Poison);
        var spawn = game.Player.Motion.Position;
        int Poisonings()
        {
            var count = 0;
            for (var i = 0; i < 200; i++)
            {
                game.Player.Statuses.Remove(StatusId.Poison);
                game.Player.Respawn(spawn);
                if (game.ProbeContactHit(spider, 10f) > 0f && game.Player.Statuses.IsActive(StatusId.Poison))
                    count++;
            }
            return count;
        }
        var poisoned = Poisonings();
        var expected = chance * 200f;
        check(poisoned >= expected * 0.75f && poisoned <= expected * 1.25f,
            $"a poisonous creature poisons at its listed chance over 200 contacts ({poisoned} of 200, listed {chance:P0})");

        game.Player.Equipment.Set(EquipmentSlot.Accessory, ItemId.FilterImplant);
        var blocked = Poisonings();
        game.Player.Equipment.Set(EquipmentSlot.Accessory, null);
        check(blocked == 0, $"an immunity accessory (filter implant) blocks the poison ({blocked} of 200)");

        // Debuffs ride on a creature's shots too.
        game.Player.Statuses.Remove(StatusId.Poison);
        game.Player.Respawn(spawn);
        game.Player.ImmunitySeconds = 0f;
        game.ProbeFireHostileBolt(Center(game) - new Vector2(1.6f, 0f), new Vector2(8f, 0f), 6f, EnemyKind.EventToxicCloud);
        Tick(game, 20);
        check(game.Player.Statuses.IsActive(StatusId.Poison), "a creature's bolt carries its debuffs onto the player");

        // Every new debuff is a debuff: red in the buff bar and never cancelled by a right-click.
        StatusId[] added = [StatusId.Bleeding, StatusId.Chilled, StatusId.Slow, StatusId.Confused, StatusId.Darkness, StatusId.Weak,
            StatusId.BrokenArmor, StatusId.Silenced, StatusId.Venom, StatusId.Frostburn, StatusId.CursedFire];
        foreach (var id in added)
            game.Player.Statuses.Apply(id, 1f);
        check(added.All(StatusEffectSystem.IsDebuff) &&
              added.All(id => game.ActiveBuffs.Any(buff => buff.Status == id && buff.IsDebuff)) &&
              added.All(id => !game.CancelBuff(id.ToString())) && added.All(game.Player.Statuses.IsActive),
            "the creature debuffs show as debuffs and cannot be cancelled");
        foreach (var id in added)
            game.Player.Statuses.Remove(id);
    }

    // ------------------------------------------------------------------ IC-3: what each debuff does

    private static void DebuffEffects(Action<bool, string> check)
    {
        // Terraria-unit damage over time: -30 units = 15 HP/s, -16 = 8, -24 = 12.
        var system = new StatusEffectSystem();
        var venom = new StatusEffectController();
        venom.Apply(StatusId.Venom, 2f);
        var oneVitalHour = venom.Advance(1f); // 12.5 real seconds
        check(system.DamagePerSecond(StatusId.Venom) == 15f && system.DamagePerSecond(StatusId.Frostburn) == 8f &&
              system.DamagePerSecond(StatusId.CursedFire) == 12f && MathF.Abs(oneVitalHour - 187.5f) < 0.01f,
            $"venom, frostburn and cursed fire burn in Terraria units (15/8/12 HP a second; venom {oneVitalHour:0.#} over 12.5 s)");

        // Chilled and slow: the strongest applies.
        var game = NewGame(9302);
        game.Player.Statuses.Apply(StatusId.Chilled, 2f);
        var chilled = game.DebuffMoveFactor;
        game.Player.Statuses.Apply(StatusId.Slow, 2f);
        var both = game.DebuffMoveFactor;
        Tick(game, 90, horizontal: 1f);
        var slowSpeed = MathF.Abs(game.Player.Motion.Velocity.X);
        game.Player.Statuses.Remove(StatusId.Chilled);
        game.Player.Statuses.Remove(StatusId.Slow);
        Tick(game, 90, horizontal: 1f);
        var plainSpeed = MathF.Abs(game.Player.Motion.Velocity.X);
        check(chilled == 0.75f && both == 0.5f && plainSpeed > 0f && slowSpeed / plainSpeed is > 0.4f and < 0.6f,
            $"chilled ×0.75; slow and chilled together ×0.5, not ×0.375 (run {slowSpeed:0.0} vs {plainSpeed:0.0} tiles/s)");

        // Confused: left and right swap.
        game = NewGame(9303);
        game.Player.Statuses.Apply(StatusId.Confused, 1f);
        Tick(game, 30, horizontal: 1f);
        var confusedVelocity = game.Player.Motion.Velocity.X;
        game.Player.Statuses.Remove(StatusId.Confused);
        Tick(game, 60, horizontal: 1f);
        check(confusedVelocity < -0.5f && game.Player.Motion.Velocity.X > 0.5f,
            $"confusion inverts left and right (pressing right: {confusedVelocity:0.0} confused, {game.Player.Motion.Velocity.X:0.0} clear)");

        // Broken armour halves defense; weak takes 4 and cuts melee damage; the plating accessory blocks broken armour.
        game.Player.Statuses.Apply(StatusId.Fortified, 1f);
        var defense = game.TotalDefense;
        game.Player.Statuses.Apply(StatusId.BrokenArmor, 1f);
        var broken = game.TotalDefense;
        game.Player.Statuses.Remove(StatusId.BrokenArmor);
        var melee = game.CombatStats.MeleeMultiplier;
        game.Player.Statuses.Apply(StatusId.Weak, 1f);
        var weakDefense = game.TotalDefense;
        var weakMelee = game.CombatStats.MeleeMultiplier;
        game.Player.Statuses.Remove(StatusId.Weak);
        game.Player.Equipment.Set(EquipmentSlot.Accessory, ItemId.KineticPlateMkII);
        var resisted = !game.Player.Statuses.Apply(StatusId.BrokenArmor, 1f, game.Player.Stats.ImmunityTags);
        game.Player.Equipment.Set(EquipmentSlot.Accessory, null);
        game.Player.Statuses.Remove(StatusId.Fortified);
        check(defense >= 10 && broken == defense / 2 && weakDefense == defense - 4 && MathF.Abs(weakMelee - melee * 0.8f) < 0.001f && resisted,
            $"broken armour halves TotalDefense ({defense} → {broken}); weak is −4 defense and ×0.8 melee; plating blocks broken armour");

        // Darkness dims the light; a flood visor blocks it.
        game.Player.Statuses.Apply(StatusId.Darkness, 1f);
        var dimmed = game.DarknessDimming;
        game.Player.Statuses.Remove(StatusId.Darkness);
        game.Player.Equipment.Set(EquipmentSlot.Accessory, ItemId.FloodVisor);
        var visorBlocks = !game.Player.Statuses.Apply(StatusId.Darkness, 1f, game.Player.Stats.ImmunityTags);
        game.Player.Equipment.Set(EquipmentSlot.Accessory, null);
        check(dimmed > 0f && game.DarknessDimming == 0f && visorBlocks, "darkness dims the light the renderer draws; the flood visor blocks it");

        // Bleeding: no regeneration at all.
        float Regained(bool bleeding)
        {
            var hurt = NewGame(9304);
            hurt.Player.RestorePersistentState(hurt.Player.MaximumHitPoints - 40f, 1f, 1f); // hurt without wounds
            hurt.Player.Statuses.Apply(StatusId.Rejuvenation, 3f);
            if (bleeding)
                hurt.Player.Statuses.Apply(StatusId.Bleeding, 3f);
            var before = hurt.Player.HitPoints;
            for (var step = 0; step < 150; step++)
            {
                foreach (var enemy in hurt.Enemies.Where(enemy => enemy.IsAlive).ToArray())
                    enemy.TakeDamage(1e7f); // nothing may interrupt the regeneration being measured
                Tick(hurt, 10);
            }
            return hurt.Player.HitPoints - before;
        }
        var (clean, bled) = (Regained(false), Regained(true));
        check(clean > 0.5f && bled <= 0.001f, $"bleeding stops natural and status regeneration ({clean:0.0} HP regained clean, {bled:0.0} bleeding)");

        // Silenced: no Tech use.
        game = NewGame(9305);
        var staff = game.Items.All.First(item => item.UseKind == ItemUseKind.Tech && item.ChargeCost > 0 && item.Style == WeaponStyle.Staff);
        game.Player.Inventory.Add(staff.Id, 1);
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(staff.Id));
        var aim = new GridPoint((int)Center(game).X + 6, (int)Center(game).Y);
        game.Player.Statuses.Apply(StatusId.Silenced, 1f);
        var charge = game.Charge;
        var shots = game.Projectiles.Count;
        Tick(game, 1, action: PlayerAction.UseSelected, target: aim);
        var silenced = game.Charge == charge && game.Projectiles.Count == shots;
        game.Player.Statuses.Remove(StatusId.Silenced);
        Tick(game, 40);
        Tick(game, 1, action: PlayerAction.UseSelected, target: aim);
        check(silenced && game.Charge < charge, $"silence stops Tech use ({staff.Name} fires only once the silence ends)");
    }

    // ------------------------------------------------------------------ EN-17: creature afflictions

    private static void CreatureAfflictions(Action<bool, string> check)
    {
        var game = NewGame(9306);
        var venomed = Far(game, EnemyKind.AstroBeast);
        var poisoned = game.SpawnCreature(EnemyKind.AstroBeast, new Vector2(66f, 18f));
        poisoned.Motion.Position = new Vector2(66f, 20f - poisoned.Motion.Size.Y - 0.01f);
        game.AfflictEnemy(venomed, StatusId.Venom);
        game.AfflictEnemy(poisoned, StatusId.Poison);
        var (venomBefore, poisonBefore) = (venomed.HitPoints, poisoned.HitPoints);
        Tick(game, 120);
        var (venomLoss, poisonLoss) = (venomBefore - venomed.HitPoints, poisonBefore - poisoned.HitPoints);
        check(venomLoss >= GameSimulation.CreatureVenomDps * 2f - 0.5f && venomLoss <= GameSimulation.CreatureVenomDps * 2.5f + 0.5f &&
              MathF.Abs(venomLoss / poisonLoss - 4f) < 0.2f,
            $"venom burns a creature at {GameSimulation.CreatureVenomDps:0} a second, four times poison ({venomLoss:0.#} vs {poisonLoss:0.#} over 2 s)");

        game.AfflictEnemy(venomed, StatusId.Corroded);
        check(GameSimulation.CorrodedDefense(venomed, 20) == 20 - GameSimulation.CorrodedDefenseShred &&
              GameSimulation.CorrodedDefense(venomed, 6) == 0 && GameSimulation.CorrodedDefense(poisoned, 20) == 20 &&
              GameSimulation.ProbeCreatureDefense(venomed) == Math.Max(0, venomed.Archetype.Defense - GameSimulation.CorrodedDefenseShred),
            $"corroded strips {GameSimulation.CorrodedDefenseShred} defense from a creature (never below 0)");

        var paying = EnemyArchetypes.All.First(archetype => !archetype.IsBoss && !archetype.Passive && archetype.CreditDrop >= 4).Kind;
        var mark = Far(game, paying);
        var plainCredits = GameSimulation.ProbeCreditDrop(mark);
        game.AfflictEnemy(mark, StatusId.SalvageMark);
        var markedCredits = GameSimulation.ProbeCreditDrop(mark);
        check(plainCredits >= 4 && markedCredits == plainCredits + (int)MathF.Ceiling(plainCredits * GameSimulation.SalvageMarkCreditShare),
            $"the salvage mark adds credits on the kill ({plainCredits} → {markedCredits})");

        var wet = Far(game, EnemyKind.AstroBeast);
        var lit = game.AfflictEnemy(wet, StatusId.Burning);
        game.AfflictEnemy(wet, StatusId.Wet);
        var doused = !wet.HasAffliction(StatusId.Burning);
        var relit = game.AfflictEnemy(wet, StatusId.Burning);
        var swimmer = game.SpawnCreature(EnemyKind.AstroBeast, new Vector2(60f, 18f));
        swimmer.Motion.Position = new Vector2(60f, 20f - swimmer.Motion.Size.Y - 0.01f);
        for (var x = 58; x <= 63; x++)
        for (var y = 17; y <= 19; y++)
        {
            var cell = game.World.GetCell(new GridPoint(x, y));
            cell.LiquidKind = LiquidKind.Water;
            cell.LiquidDepth = 1f;
            game.World.SetCell(new GridPoint(x, y), cell);
        }
        game.AfflictEnemy(swimmer, StatusId.Burning);
        Tick(game, 2);
        check(lit && doused && !relit && !swimmer.HasAffliction(StatusId.Burning) && swimmer.HasAffliction(StatusId.Wet),
            "wet puts burning out and keeps it out; a creature in water gets wet");

        var catalog = new ItemCatalog();
        check(GameSimulation.WeaponAffliction(catalog[ItemId.VenomRounds]) is { Status: StatusId.Venom } &&
              GameSimulation.WeaponAffliction(catalog[ItemId.AcidFlask]) is { Status: StatusId.Corroded } &&
              GameSimulation.WeaponAffliction(catalog[ItemId.ScrapChakram]) is { Status: StatusId.SalvageMark } &&
              GameSimulation.WeaponAffliction(catalog[ItemId.CodexTidalSurge]) is { Status: StatusId.Wet },
            "venom ammo, the acid flask, the scrap chakram and the tidal codex carry venom, corroded, the salvage mark and wet");
    }

    // ------------------------------------------------------------------ EN-12: contact boxes

    private static void ContactBoxes(Action<bool, string> check)
    {
        var game = NewGame(9307);
        var golem = game.SpawnCreature(EnemyKind.BasaltGolem, new Vector2(40f, 18f));
        var player = game.Player.Motion;
        void Place(float overlap) =>
            golem.Motion.Position = new Vector2(player.Position.X + player.Size.X - overlap, player.Position.Y + player.Size.Y - golem.Motion.Size.Y);
        Place(0.2f);
        var overlapping = game.ProbeContactTouches(golem);
        Place(0.05f);
        var shallow = game.ProbeContactTouches(golem);
        Place(-0.05f);
        var apart = game.ProbeContactTouches(golem);
        Place(0.2f);
        golem.Motion.Velocity = Vector2.Zero;
        golem.AttackCooldown = 0f;
        game.Player.ImmunitySeconds = 0f;
        var before = game.Player.HitPoints;
        Tick(game, 1);
        check(golem.Archetype.Size == new Vector2(1.6f, 2f) && overlapping && !shallow && !apart && game.Player.HitPoints < before,
            "a 1.6×2 creature overlapping the player box by 0.2 tiles hits (its box inset 0.1); a 0.05 graze or a gap does not");

        var worm = game.SpawnCreature(EnemyKind.TunnelWyrm, new Vector2(60f, 10f));
        var side = Math.Max(0.3f, worm.Motion.Size.X * 0.9f);
        var feet = player.Position.Y + player.Size.Y;
        var centerX = player.Position.X + player.Size.X / 2f;
        var atFeet = game.ProbeSegmentTouches(worm, new Vector2(centerX, feet + side / 2f - 0.25f));
        var below = game.ProbeSegmentTouches(worm, new Vector2(centerX, feet + side / 2f + 0.05f));
        var oldRule = Vector2.Distance(new Vector2(centerX, feet + side / 2f - 0.25f), Center(game)) < 0.9f;
        check(atFeet && !below && !oldRule, "a worm segment touching the player's feet hits (tested against the player's box, not the center)");
    }

    // ------------------------------------------------------------------ IC-18: variance and immune pass-through

    private static void HurtPath(Action<bool, string> check)
    {
        var game = NewGame(9308);
        var beast = Far(game, EnemyKind.MossSlime);
        var spawn = game.Player.Motion.Position;
        var hits = new List<float>();
        for (var i = 0; i < 1000; i++)
        {
            game.Player.Respawn(spawn);
            hits.Add(game.ProbeContactHit(beast, 60f));
        }
        var (min, max, mean) = (hits.Min(), hits.Max(), hits.Average());
        check(min >= 60f * 0.85f - 0.51f && max <= 60f * 1.15f + 0.51f && min <= 60f * 0.87f && max >= 60f * 1.13f &&
              MathF.Abs((float)mean - 60f) < 1.5f && hits.Distinct().Count() >= 15,
            $"1,000 contact hits on a still player spread over ±15% ({min:0}–{max:0}, mean {mean:0.0} of 60)");

        game.Player.Respawn(spawn);
        Tick(game, 2);
        game.Player.ImmunitySeconds = 2f;
        var health = game.Player.HitPoints;
        var bolt = game.ProbeFireHostileBolt(Center(game) - new Vector2(1.5f, 0f), new Vector2(6f, 0f), 20f);
        Tick(game, 36);
        var passed = game.Projectiles.Contains(bolt) && bolt.Position.X > game.Player.Motion.Position.X + game.Player.Motion.Size.X;
        check(passed && game.Player.HitPoints >= health && MathF.Abs(game.Player.Motion.Velocity.X) < 0.01f,
            "a hostile bolt passes through an immune player: it flies on, does no damage and does not push");

        game.Player.ImmunitySeconds = 0f;
        var struck = game.ProbeFireHostileBolt(Center(game) - new Vector2(1.5f, 0f), new Vector2(6f, 0f), 20f);
        health = game.Player.HitPoints;
        Tick(game, 36);
        var lost = health - game.Player.HitPoints;
        check(!game.Projectiles.Contains(struck) && lost > 0f &&
              game.RecentMessages.Any(message => message.Contains($"hit you for {lost:0}")),
            $"a bolt that lands is spent and its message shows the damage applied ({lost:0})");
    }

    // ------------------------------------------------------------------ IC-21: consistency

    private static void Consistency(Action<bool, string> check)
    {
        var catalog = new ItemCatalog();
        var loadout = new EquipmentLoadout();
        loadout.Set(EquipmentSlot.Accessory6, ItemId.CloseQuartersEmblemMkIII);
        var open = false;
        loadout.Slot6Gate = () => open;
        var locked = CombatStatsCalculator.Calculate(loadout, catalog).MeleeMultiplier;
        open = true;
        var unlocked = CombatStatsCalculator.Calculate(loadout, catalog).MeleeMultiplier;
        check(locked == 1f && unlocked > 1f, $"a locked sixth-slot emblem adds no class damage (×{locked:0.00} locked, ×{unlocked:0.00} open)");

        var game = NewGame(9309);
        var rifle = catalog[ItemId.SniperRifle];
        game.Player.Inventory.Add(ItemId.Arrow, 999);
        var target = new GridPoint((int)Center(game).X + 8, (int)Center(game).Y);
        var shots = Enumerable.Range(0, 400).SelectMany(_ => game.ProbeAttackRanged(rifle, target)).ToArray();
        var plain = shots.Where(shot => !shot.Critical).Select(shot => shot.Damage).ToArray();
        check(rifle.Tier == 0 && shots.Length == 400 && plain.Max() / plain.Min() is > 1.25f and < 1.45f && plain.Distinct().Count() >= 5 &&
              shots.Any(shot => shot.Critical) && shots.All(shot => shot.Class == DamageClass.Ranged),
            $"a tier-0 energy gun shot varies ±15% and can crit ({plain.Min():0}–{plain.Max():0}, {shots.Count(shot => shot.Critical)} crits of 400)");
    }
}
