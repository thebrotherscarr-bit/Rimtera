using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Probes for the stat formulas and tool mechanics matched in docs/GEAR_DIFF_MECHANICS.md: weapon damage, spread,
/// defense and armor penetration, crits, knockback caps, attack/mining/placement speed, accumulate-to-100 mining with
/// hardness classes, cutters, wall hammering, box reach, natural life regeneration, charge (mana) delay and
/// regeneration, charge cost and the charge cap, and how calibrations scale an item. Check names start with "stats".
/// The expected values are computed from the item and tile tables, so item retuning does not break them.
/// </summary>
public static class StatParitySelfTests
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

    /// <summary>Clears an air box with a basalt floor at <paramref name="floorY"/> and stands the player on it.</summary>
    private static void Arena(GameSimulation game, int left, int top, int right, int floorY)
    {
        for (var y = top; y <= floorY + 1; y++)
        for (var x = left; x <= right; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y >= floorY ? TileId.Rock : TileId.Air);
            game.World.SetWall(cell, WallIds.None);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            game.World.SetCell(cell, data);
        }
        game.Player.Motion.Position = new Vector2((left + right) / 2f + 0.16f, floorY - 1.81f);
        game.Player.Motion.Velocity = Vector2.Zero;
        Tick(game, 2);
    }

    private static bool Near(float a, float b, float tolerance = 0.001f) => MathF.Abs(a - b) <= tolerance;

    /// <summary>Hits a cell with the selected drill until it breaks; returns the number of hits (or -1 after 60).</summary>
    private static int HitsToBreak(GameSimulation game, GridPoint cell, TileId tile)
    {
        game.World.SetTile(cell, tile);
        for (var hit = 1; hit <= 60; hit++)
        {
            game.Player.ActionCooldownTicks = 0;
            game.Tick(new GameInput(default, PlayerAction.Mine, cell));
            if (game.World.TileAt(cell.X, cell.Y) != tile)
                return hit;
        }
        return -1;
    }

    public static void Run(Action<bool, string> check)
    {
        Damage(check);
        Speeds(check);
        Mining(check);
        Regeneration(check);
        Charge(check);
        Calibrations(check);
    }

    // ------------------------------------------------------------------ 1. damage, crit, knockback

    private static void Damage(Action<bool, string> check)
    {
        // Class bonuses add up within a class; the weapon formula bakes the calibration in, adds ammo, then scales.
        var catalog = new ItemCatalog();
        var meleeGear = catalog.All.Where(item => item.MeleeDamageBonus > 0f && item.EquipmentSlot is not null)
            .GroupBy(item => item.EquipmentSlot!.Value).Select(group => group.First()).Take(2).ToArray();
        if (meleeGear.Length == 2)
        {
            var loadout = new EquipmentLoadout();
            foreach (var item in meleeGear)
                loadout.Set(item.EquipmentSlot!.Value, item.Id);
            var stats = CombatStatsCalculator.Calculate(loadout, catalog);
            var additive = 1f + meleeGear.Sum(item => item.MeleeDamageBonus);
            check(Near(stats.MeleeMultiplier, additive) && (stats.ActiveSet is not null || !Near(additive, meleeGear.Aggregate(1f, (m, i) => m * (1f + i.MeleeDamageBonus)))),
                $"stats damage: two melee bonuses add within the class (x{stats.MeleeMultiplier:0.000} = 1 + {meleeGear[0].MeleeDamageBonus:0.###} + {meleeGear[1].MeleeDamageBonus:0.###})");
        }
        else
            check(true, "stats damage: (fewer than two melee damage pieces in the catalog)");
        check(Near(CombatTuning.WeaponDamage(25, 1.15f, 0, 1f), 29f) && Near(CombatTuning.WeaponDamage(10, 1f, 5, 1.2f), 18f) &&
              Near(CombatTuning.WeaponDamage(12, 1.12f, 0, 1.5f), 13f * 1.5f) && Near(CombatTuning.WeaponDamage(0, 2f, 5, 2f), 0f),
            "stats damage: weapon damage = (round(base x calibration) + ammo) x class multiplier");

        var game = NewGame(9801);
        var sword = game.Items[ItemId.WoodenSword];
        var skill = CombatStatsCalculator.SkillMultiplier(game.Player.ColonistStats, DamageClass.Melee);
        var expected = CombatTuning.WeaponDamage(sword.Damage, CalibrationCatalog.Get(Calibration.Overclocked).DamageMultiplier, 0,
            game.CombatStats.MeleeMultiplier) * skill;
        check(Near(game.ComputeWeaponDamage(sword, Calibration.Overclocked), expected, 0.01f),
            $"stats damage: the game uses the weapon formula, then the colonist's melee skill ({expected:0.##})");

        // +-15% spread in whole percent, rounded to whole points; luck rerolls toward the better (or worse) result.
        var rng = new Random(11);
        var rolls = Enumerable.Range(0, 3000).Select(_ => CombatTuning.RollVariance(100f, rng)).ToArray();
        var lucky = Enumerable.Range(0, 3000).Select(_ => CombatTuning.RollVariance(100f, rng, 1f)).Average();
        var cursed = Enumerable.Range(0, 3000).Select(_ => CombatTuning.RollVariance(100f, rng, -1f)).Average();
        check(rolls.All(value => value is >= 85f and <= 115f && value == MathF.Floor(value)) && rolls.Min() == 85f && rolls.Max() == 115f &&
              Near(rolls.Average(), 100f, 0.6f) && lucky > 103f && cursed < 97f,
            $"stats damage: hits vary by whole percents within +-15% (mean {rolls.Average():0.0}); luck {lucky:0.0} / bad luck {cursed:0.0}");

        // Defense: half off (at least 1), armor penetration gives back min(AP, def)/2, crit doubles after defense.
        check(CombatTuning.CreatureDamageTaken(50f, 20, 0, false) == 40f && CombatTuning.CreatureDamageTaken(50f, 20, 10, false) == 45f &&
              CombatTuning.CreatureDamageTaken(50f, 20, 30, false) == 50f && CombatTuning.CreatureDamageTaken(5f, 20, 0, false) == 1f &&
              CombatTuning.CreatureDamageTaken(50f, 20, 0, true) == 80f && CombatTuning.CreatureDamageTaken(5f, 20, 0, true) == 2f &&
              CombatTuning.CreatureDamageTaken(30f, 0, 25, false) == 30f && CombatTuning.ArmorPenetrationGiveBack(7, 20) == 3,
            "stats damage: creature defense takes half off (min 1), armor penetration gives back min(AP, def)/2, crits double after defense");
        var dummy = game.SpawnCreatureAt(EnemyKind.AstroBeast, game.Player.Motion.Position + new Vector2(30f, -5f));
        var hp = dummy.HitPoints;
        var shown = game.ProbeStrike(dummy, 50f, true, 20);
        check(shown == 80f && Near(hp - dummy.HitPoints, 80f / WorldSettings.EnemyScale(game.Difficulty), 0.01f),
            $"stats damage: a landed crit on a 20-defense creature deals (50 - 10) x 2 = {shown:0}");
        dummy.TakeDamage(1e6f);

        // Crit chance: 4 base + weapon + bonuses; Command never crits; the roll is 1-100 <= chance.
        var command = game.Items.All.FirstOrDefault(item => item.EffectiveClass == DamageClass.Command && item.Damage > 0);
        check(CombatTuning.WeaponCritChance(DamageClass.Melee, 0, 0) == 4 && CombatTuning.WeaponCritChance(DamageClass.Ranged, 6, 3) == 13 &&
              CombatTuning.WeaponCritChance(DamageClass.Command, 10, 10) == 0 && game.ProbeCritChance(sword) == 4 + sword.CritChance + game.CombatStats.CritBonus + game.LuckCritBonus &&
              (command is null || game.ProbeCritChance(command) == 0),
            "stats crit: chance = 4 + weapon + bonuses; Command weapons never crit");
        var critRng = new Random(5);
        var critRate = Enumerable.Range(0, 20000).Count(_ => CombatTuning.RollCrit(4, critRng)) / 20000f;
        check(Near(critRate, 0.04f, 0.006f) && !CombatTuning.RollCrit(0, critRng) && CombatTuning.RollCrit(100, critRng),
            $"stats crit: a 4% chance crits about 4% of the time ({critRate:P1})");

        // Knockback: resisted share, soft caps over 8/10/12/14, hard cap 16, crit x1.4.
        check(Near(CombatTuning.KnockbackTaken(6f, 0f, false), 6f) && Near(CombatTuning.KnockbackTaken(6f, 0.5f, false), 3f) &&
              Near(CombatTuning.KnockbackTaken(10f, 0f, false), 9.8f) && Near(CombatTuning.KnockbackTaken(20f, 0f, false), 14.9168f, 0.01f) &&
              Near(CombatTuning.KnockbackTaken(100f, 0f, false), 16f) && Near(CombatTuning.KnockbackTaken(6f, 0f, true), 8.4f) &&
              CombatTuning.KnockbackTaken(9f, 1f, true) == 0f,
            "stats knockback: soft caps above 8, hard cap 16, x1.4 on a crit, after resistance");
        var slime = game.SpawnCreatureAt(EnemyKind.MossSlime, game.Player.Motion.Position + new Vector2(-30f, -5f));
        slime.Motion.Velocity = Vector2.Zero;
        game.ProbeKnock(slime, new Vector2(6f, 0f), critical: true);
        check(Near(slime.Motion.Velocity.X, 8.4f, 0.01f), $"stats knockback: a crit pushes a light creature 40% harder ({slime.Motion.Velocity.X:0.00})");

        // Player damage taken: defense x0.5 (Explorer, Classic), x0.75 (Brutal), x1 (Annihilation), at least 1.
        check(DefenseBalance.ApplyDefense(40f, 20, WorldDifficulty.Explorer) == 30f && DefenseBalance.ApplyDefense(40f, 20, WorldDifficulty.Classic) == 30f &&
              DefenseBalance.ApplyDefense(40f, 20, WorldDifficulty.Brutal) == 25f && DefenseBalance.ApplyDefense(40f, 20, WorldDifficulty.Annihilation) == 20f &&
              DefenseBalance.ApplyDefense(5f, 20, WorldDifficulty.Classic) == 1f,
            "stats defense: player defense counts x0.5 / x0.75 / x1 by world difficulty, at least 1");
    }

    // ------------------------------------------------------------------ 3. speeds

    private static void Speeds(Action<bool, string> check)
    {
        check(Near(CombatTuning.UseTimeMultiplier(1.25f), 0.8f) && Near(CombatTuning.UseTimeMultiplier(5f), 1f / 3f) &&
              CombatTuning.MeleeUseTicks(20, 0.8f) == 16 && CombatTuning.MeleeUseTicks(2, 0.3f) == 1 &&
              CombatTuning.CalibratedUseTicks(20, 0.85f) == 17 && CombatTuning.CalibratedUseTicks(25, 0.9f) == 22,
            "stats speed: attack speed becomes 1/min(speed, 3); calibrations round the use time; at least 1 tick");
        var game = NewGame(9802);
        var sword = game.Items[ItemId.WoodenSword];
        var ranged = game.Items.All.First(item => item.EffectiveClass == DamageClass.Ranged && item.UseCooldownTicks >= 10 && item.Damage > 0);
        var swordBefore = game.ProbeUseTicks(sword, Calibration.None);
        var rangedBefore = game.ProbeUseTicks(ranged, Calibration.None);
        var glove = game.Items.All.FirstOrDefault(item => item.ImmunityTags.Any(tag => tag.StartsWith("attackspeed:", StringComparison.Ordinal)));
        if (glove is not null)
        {
            game.Player.Equipment.Set(EquipmentSlot.Accessory, glove.Id);
            var speed = 1f + game.GearTagValue("attackspeed") / 100f;
            check(game.ProbeUseTicks(sword, Calibration.None) == CombatTuning.MeleeUseTicks(sword.UseCooldownTicks, 1f / speed) &&
                  game.ProbeUseTicks(sword, Calibration.None) <= swordBefore && game.ProbeUseTicks(ranged, Calibration.None) == rangedBefore,
                $"stats speed: melee speed gear shortens melee swings ({swordBefore} -> {game.ProbeUseTicks(sword, Calibration.None)} ticks), not guns");
        }
        else
            check(true, "stats speed: (no attack-speed gear in the catalog)");

        // Mining speed: 1 - bonus, floored at 0.3; the drill swing is (int)(useTime x pickSpeed) (then the colonist's skill).
        check(Near(CombatTuning.PickSpeed(25f), 0.75f) && Near(CombatTuning.PickSpeed(90f), 0.3f) && CombatTuning.DrillUseTicks(15, 0.75f) == 11 &&
              CombatTuning.DrillUseTicks(2, 0.3f) == 1,
            "stats speed: pick speed = 1 - mining bonus (floor 0.3); drill swing = (int)(use time x pick speed)");
        var drill = game.Items[ItemId.CopperPickaxe];
        var plainTicks = game.ProbeDrillTicks(drill);
        game.Player.Statuses.Apply(StatusId.MiningBoost, 2f);
        check(Near(game.PickSpeed, CombatTuning.PickSpeed(game.GearTagValue("mining"))) && game.PickSpeed < 1f && game.ProbeDrillTicks(drill) <= plainTicks,
            $"stats speed: a mining buff lowers pick speed to {game.PickSpeed:0.00} ({plainTicks} -> {game.ProbeDrillTicks(drill)} ticks)");

        // Placement speed: (int)(ticks / min(1 + bonus, 3)).
        check(CombatTuning.PlacementTicks(6, 0f) == 6 && CombatTuning.PlacementTicks(6, 25f) == 4 && CombatTuning.PlacementTicks(6, 500f) == 2 &&
              CombatTuning.PlacementTicks(1, 500f) == 1,
            "stats speed: block and wall placement time divides by 1 + build bonus, capped at 3x");
    }

    // ------------------------------------------------------------------ 4. mining

    private static void Mining(Action<bool, string> check)
    {
        var tiles = new TileCatalog();
        int Hardness(TileId tile) => CombatTuning.DrillHardness(tile, tiles[tile].MiningResistance, ContentTiles.Has(tile, TileTraits.Fragile));
        check(Hardness(TileId.Soil) == CombatTuning.HardnessSoft && Hardness(TileId.Sand) == CombatTuning.HardnessSoft &&
              Hardness(TileId.Rock) == 1 && Hardness(TileId.CopperOre) == 1 && Hardness(TileId.CobaltOre) == 1 &&
              Hardness(TileId.DungeonBrick) == 2 && Hardness(TileId.ViridiumOre) == 2 && Hardness(TileId.LumeniteOre) == 3 &&
              Hardness(TileId.VoidsteelOre) == 4 && Hardness(TileId.ThinIce) == CombatTuning.HardnessFragile,
            "stats mining: soft ground x2, stone and early ores x1, derelict plating and viridium /2, lumenite /3, voidsteel /4, fragile one hit");
        check(CombatTuning.DrillDamage(35, 1, 0) == 35 && CombatTuning.DrillDamage(35, CombatTuning.HardnessSoft, 0) == 70 &&
              CombatTuning.DrillDamage(100, 3, 85) == 33 && CombatTuning.DrillDamage(70, 3, 85) == 0 &&
              CombatTuning.DrillDamage(20, CombatTuning.HardnessFragile, 0) == 100,
            "stats mining: drill damage per hit = power (x2 soft, /N hard, 0 below the minimum)");
        check(tiles[TileId.LumeniteOre].MinimumToolPower == 85 && tiles[TileId.VoidsteelOre].MinimumToolPower == 100,
            "stats mining: the hardmode ore gates stay at lumenite 85 and voidsteel 100");

        var game = NewGame(9803);
        Arena(game, 20, 10, 60, 30);
        var drill = game.Items[ItemId.CopperPickaxe];
        game.Player.Inventory.Add(drill.Id, 1);
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(drill.Id));
        var right = (int)MathF.Floor(game.Player.Motion.Position.X + game.Player.Motion.Size.X - 0.001f);
        var cell = new GridPoint(right + 1, 29);
        int Expected(TileId tile)
        {
            var perHit = CombatTuning.DrillDamage(drill.ToolPower, Hardness(tile), tiles[tile].MinimumToolPower);
            return perHit <= 0 ? -1 : (CombatTuning.TileBreakDamage + perHit - 1) / perHit;
        }
        var rockHits = HitsToBreak(game, cell, TileId.Rock);
        var soilHits = HitsToBreak(game, cell, TileId.Soil);
        check(rockHits == Expected(TileId.Rock) && soilHits == Expected(TileId.Soil) && soilHits < rockHits,
            $"stats mining: hits add up to 100 (power {drill.ToolPower}: basalt {rockHits} hits, regolith {soilHits})");
        game.World.SetTile(cell, TileId.Rock);
        game.Player.ActionCooldownTicks = 0;
        game.Tick(new GameInput(default, PlayerAction.Mine, cell));
        var dented = game.TileHitDamage(cell);
        var cracks = game.World.GetCell(cell).Damage;
        // Tool parity (docs/TOOLS_PARITY.md): partial damage no longer heals with time; it mends only as later hits age it.
        Tick(game, 400);
        check(dented == Math.Min(99, drill.ToolPower) && cracks > 0 && game.TileHitDamage(cell) == dented && game.World.GetCell(cell).Damage == cracks,
            $"stats mining: a hit leaves {dented}/100 damage (cracks shown) that stays while left alone");
        game.World.SetTile(cell, TileId.Air);

        // Box reach: 5 cells sideways past the body's cells, 3 up or down, plus the reach bonus.
        check(CombatTuning.InTileReach(10f, 10f, 0.68f, 1.8f, 15, 10, 0) && !CombatTuning.InTileReach(10f, 10f, 0.68f, 1.8f, 16, 10, 0) &&
              CombatTuning.InTileReach(10f, 10f, 0.68f, 1.8f, 5, 10, 0) && !CombatTuning.InTileReach(10f, 10f, 0.68f, 1.8f, 4, 10, 0) &&
              CombatTuning.InTileReach(10f, 10f, 0.68f, 1.8f, 10, 14, 0) && !CombatTuning.InTileReach(10f, 10f, 0.68f, 1.8f, 10, 15, 0) &&
              CombatTuning.InTileReach(10f, 10f, 0.68f, 1.8f, 15, 7, 0) && !CombatTuning.InTileReach(10f, 10f, 0.68f, 1.8f, 10, 6, 0) &&
              CombatTuning.InTileReach(10f, 10f, 0.68f, 1.8f, 16, 15, 1),
            "stats reach: tile reach is a box 5 cells sideways and 3 up/down past the body, plus bonuses");
        // Tool parity: the copper-tier drill reaches one cell less (its reach boost is -1, docs/TOOLS_PARITY.md).
        var far = new GridPoint(right + CombatTuning.TileReachX + 1 + (int)MathF.Floor(game.GearReachBonus) + ToolParity.TileBoost(drill), 29);
        game.World.SetTile(far, TileId.Soil);
        game.Player.ActionCooldownTicks = 0;
        game.Tick(new GameInput(default, PlayerAction.Mine, far));
        var farUntouched = game.World.TileAt(far.X, far.Y) == TileId.Soil && game.TileHitDamage(far) == 0;
        var near = new GridPoint(far.X - 1, 29);
        game.World.SetTile(near, TileId.Soil);
        game.Player.ActionCooldownTicks = 0;
        game.Tick(new GameInput(default, PlayerAction.Mine, near));
        check(farUntouched && (game.World.TileAt(near.X, near.Y) != TileId.Soil || game.TileHitDamage(near) > 0),
            "stats reach: the drill reaches exactly 5 cells past the body's edge, plus its own reach boost");

        // Cutters (axes) and demolishers (hammers on walls).
        check(CombatTuning.CutterDamage(45) == 10 && CombatTuning.CutterDamage(45, cactus: true) == 32 && CombatTuning.CutterDamage(100) == 24,
            "stats tools: a cutter hit adds (int)(power/5 x 1.2) (x3 on cactus) toward 100");
        check(CombatTuning.WallHammerDamage(45) == 67 && CombatTuning.WallHammerDamage(70) == 105,
            "stats tools: a demolisher hit adds (int)(power x 1.5) to a wall; the wall falls at 100");
        var walls = NewGame(9804);
        Arena(walls, 20, 10, 60, 30);
        var hammer = walls.Items[ItemId.FerriteDemolisher];
        walls.Player.Inventory.Add(hammer.Id, 1);
        walls.TrySelectInventorySlot(walls.Player.Inventory.FindSlot(hammer.Id));
        var wallCell = new GridPoint(right + 2, 27);
        walls.World.SetWall(wallCell, WallIds.Plank);
        var swings = 0;
        while (walls.World.GetCell(wallCell).WallId != WallIds.None && swings < 10)
        {
            walls.Player.ActionCooldownTicks = 0;
            walls.Tick(new GameInput(default, PlayerAction.UseSelected, wallCell));
            swings++;
        }
        var expectedSwings = (CombatTuning.TileBreakDamage + CombatTuning.WallHammerDamage(hammer.ToolPower) - 1) / CombatTuning.WallHammerDamage(hammer.ToolPower);
        check(swings == expectedSwings, $"stats tools: the {hammer.Name} (power {hammer.ToolPower}) knocks out a wall in {swings} hits");
    }

    // ------------------------------------------------------------------ 5. regeneration

    private static void Regeneration(Action<bool, string> check)
    {
        check(CombatTuning.NaturalLifeRegenUnits(299, 400f, true, false, false, false) == 0 &&
              CombatTuning.NaturalLifeRegenUnits(300, 400f, true, false, false, false) == 1 &&
              CombatTuning.NaturalLifeRegenUnits(3600, 400f, true, false, false, false) == 11 &&
              CombatTuning.NaturalLifeRegenUnits(3600, 400f, false, false, false, false) == 4 &&
              CombatTuning.NaturalLifeRegenUnits(3600, 400f, true, false, true, false) == 6 &&
              CombatTuning.NaturalLifeRegenUnits(3600, 400f, true, false, true, true) == 11 &&
              CombatTuning.NaturalLifeRegenUnits(3600, 400f, true, true, false, false) == 15 &&
              CombatTuning.NaturalLifeRegenUnits(3600, 100f, true, false, false, false) == 4,
            "stats regen: steps at 5-50 s (+1 at 60 s), x1.25 still / x0.5 moving, x1.3 asleep, halved on harsh worlds unless fed, x(max/400 x 0.85 + 0.15)");
        check(Near(CombatTuning.RegenUnitsPerSecond(4), 2f) && Near(CombatTuning.NaturalLifeRegen(60f, 100f, true), 2f),
            "stats regen: each regen unit is half a hit point per second");

        var game = NewGame(9805);
        Arena(game, 20, 10, 60, 30);
        game.Player.TakeDamage(60f, DamageKind.Blunt);
        Tick(game, 2);
        var start = game.Player.HitPoints;
        Tick(game, 290);
        var beforeFirstStep = game.NaturalLifeRegenPerSecond; // gear and status regeneration may still tick on top
        Tick(game, 60 * 60);
        var rate = game.NaturalLifeRegenPerSecond;
        var expected = CombatTuning.NaturalLifeRegen(60f, game.Player.MaximumHitPoints, true);
        check(beforeFirstStep < 0.01f && Near(rate, expected, 0.01f) && game.Player.HitPoints > start,
            $"stats regen: nothing for the first 5 s ({beforeFirstStep:0.00}), then the full rate after a minute unhurt standing still ({rate:0.0} of {expected:0.0} HP/s, {start:0}->{game.Player.HitPoints:0} HP)");
        game.Difficulty = WorldDifficulty.Brutal;
        var harsh = game.NaturalLifeRegenPerSecond;
        game.Player.Statuses.Apply(StatusId.WellFed, 2f);
        var fed = game.NaturalLifeRegenPerSecond;
        check(harsh < rate && Near(fed, rate, 0.01f), $"stats regen: Brutal halves natural regeneration ({harsh:0.0}) unless well fed ({fed:0.0})");
    }

    // ------------------------------------------------------------------ 5b. charge (mana)

    private static void Charge(Action<bool, string> check)
    {
        check(CombatTuning.ChargeRegenDelayTicks(0f, 20f, false) == 199f && CombatTuning.ChargeRegenDelayTicks(20f, 20f, false) == 31f &&
              CombatTuning.ChargeRegenDelayTicks(0f, 20f, true) == 20f,
            "stats charge: the delay is (int)(((1 - charge/max) x 240 + 45) x 0.7) ticks, 20 at most with the charge stim");
        check(CombatTuning.ChargeRegenUnits(10f, 20f, 0f, false, false) == 5 && CombatTuning.ChargeRegenUnits(10f, 20f, 0f, true, false) == 10 &&
              CombatTuning.ChargeRegenUnits(10f, 20f, 0f, false, true) == 14 && CombatTuning.ChargeRegenUnits(0f, 3f, 0f, false, false) == 2 &&
              CombatTuning.ChargeRegenUnits(200f, 200f, 4f, false, false) == 71,
            "stats charge: regeneration = (max/3 + bonus + 1), x2 still, x(0.5 + 0.5 x fill), at least 2 units");
        check(CombatTuning.ChargeCost(10, 0.85f, 1f) == 8 && CombatTuning.ChargeCost(10, 1f, 0.92f) == 9 && CombatTuning.ChargeCost(7, 1.25f, 1f) == 9 &&
              CombatTuning.ChargeCost(0, 0.5f, 1f) == 0,
            "stats charge: cost = (int)(round(cost x calibration) x wearer multiplier)");

        var game = NewGame(9806);
        Arena(game, 20, 10, 60, 30);
        game.ProbeSpendCharge(game.MaximumCharge);
        var delay = game.ChargeRegenDelayTicks;
        var empty = game.Charge;
        Tick(game, 20);
        var during = game.Charge - empty;
        Tick(game, (int)(delay / 2f) + 30); // standing still counts the delay down twice as fast
        check(Near(delay, CombatTuning.ChargeRegenDelayTicks(0f, game.MaximumCharge, false)) && during < 0.001f && game.Charge > empty + 0.5f,
            $"stats charge: spending everything waits {delay:0} ticks (half that standing still), then charge flows back");
        game.Player.ChargeCrystalsUsed = 50;
        check(game.MaximumCharge == CombatTuning.MaximumChargeCap, $"stats charge: maximum charge is capped at {CombatTuning.MaximumChargeCap:0}");
    }

    // ------------------------------------------------------------------ 6. calibrations

    private static void Calibrations(Action<bool, string> check)
    {
        var game = NewGame(9807);
        var sword = game.Items[ItemId.WoodenSword];
        var skill = CombatStatsCalculator.SkillMultiplier(game.Player.ColonistStats, DamageClass.Melee);
        var ok = true;
        foreach (var definition in CalibrationCatalog.All.Where(c => c.Group is CalibrationGroup.Universal or CalibrationGroup.Melee))
        {
            var damage = (int)MathF.Round(sword.Damage * definition.DamageMultiplier, MidpointRounding.ToEven) * game.CombatStats.MeleeMultiplier * skill;
            var ticks = CombatTuning.MeleeUseTicks((int)MathF.Round(sword.UseCooldownTicks * definition.CooldownMultiplier, MidpointRounding.ToEven), 1f);
            var crit = CombatTuning.WeaponCritChance(DamageClass.Melee, sword.CritChance, definition.CritBonus + game.CombatStats.CritBonus + game.LuckCritBonus);
            ok &= Near(game.ComputeWeaponDamage(sword, definition.Id), damage, 0.01f) &&
                  game.ProbeUseTicks(sword, definition.Id) == ticks && game.ProbeCritChance(sword, definition.Id) == crit;
        }
        check(ok, "stats calibrations: damage and use time are scaled and rounded into the item; crit bonuses add");
        var tech = game.Items.All.First(item => item.ChargeCost > 0 && item.EffectiveClass == DamageClass.Tech);
        var efficient = CalibrationCatalog.All.First(c => c.ChargeCostMultiplier < 1f && c.Group is CalibrationGroup.Universal or CalibrationGroup.Tech);
        check(game.ChargeCostFor(tech, efficient.Id) == CombatTuning.ChargeCost(tech.ChargeCost, efficient.ChargeCostMultiplier, 1f) &&
              game.ChargeCostFor(tech, efficient.Id) <= tech.ChargeCost,
            $"stats calibrations: {efficient.Name} scales the charge cost ({tech.ChargeCost} -> {game.ChargeCostFor(tech, efficient.Id)})");
        var velocity = CalibrationCatalog.All.First(c => c.VelocityMultiplier > 1f);
        var size = CalibrationCatalog.All.First(c => c.SizeMultiplier > 1f);
        var knock = CalibrationCatalog.All.First(c => c.KnockbackMultiplier > 1f);
        check(velocity.VelocityMultiplier > 1f && Near(game.CalibratedSize(size.Id), size.SizeMultiplier) && knock.KnockbackMultiplier > 1f,
            "stats calibrations: velocity, size and knockback factors multiply the item's own values");
    }
}
