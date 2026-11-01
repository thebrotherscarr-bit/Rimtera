using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Self-tests for the arsenal pass: category targets, obtainability, styles/ammo, gear effects, potions, calibrations.</summary>
public static class ArsenalSelfTests
{
    private static void Tick(GameSimulation game, PlayerAction action = PlayerAction.None, GridPoint? target = null) =>
        game.Tick(new GameInput(new MovementInput(0f), action, target));

    private static void Use(GameSimulation game, ItemId item, Calibration calibration = Calibration.None)
    {
        game.Player.Inventory.Add(item, 1, calibration);
        game.Player.ActionCooldownTicks = 0;
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
        Tick(game, PlayerAction.UseSelected, new GridPoint((int)game.Player.Motion.Position.X, (int)game.Player.Motion.Position.Y));
    }

    public static void Run(Action<bool, string> check)
    {
        var catalog = new ItemCatalog();
        var crafting = new CraftingSystem(catalog);
        var counts = ItemCensus.Count(catalog);
        int C(params string[] keys) => keys.Sum(key => counts.GetValueOrDefault(key));
        var arsenal = ArsenalItems.AllIds.ToArray();

        // ---- 1. Category targets (ITEMS_DIFF.md) ----
        var targets = new (string Name, int Have, int Want)[]
        {
            ("broadswords", C("weapon: broadsword"), 40), ("shortswords", C("weapon: shortsword"), 10), ("spears", C("weapon: spear"), 15),
            ("yoyos", C("weapon: yoyo"), 18), ("boomerangs", C("weapon: boomerang"), 10), ("flails", C("weapon: flail"), 12),
            ("whips", C("weapon: whip"), 15), ("bows", C("weapon: bow"), 30), ("guns+shotguns", C("weapon: gun", "weapon: shotgun"), 30),
            ("launchers", C("weapon: launcher"), 12), ("thrown", C("weapon: thrown"), 30),
            ("tech", C("weapon: staff", "weapon: tome", "weapon: beam"), 60), ("summons", C("weapon: summonstaff", "weapon: sentrystaff"), 35),
            ("picks/drills", C("tool: drill", "tool: pickaxe"), 25), ("axes", C("cutter"), 20), ("hammers", C("demolisher"), 15),
            ("ammo", C("ammo"), 50), ("accessories", C("accessory"), 150), ("wings", C("accessory: wings"), 30),
            ("potions", C("potion/buff"), 50), ("food", C("food"), 40), ("armor sets", ItemCensus.CompleteArmorSets(catalog), 50),
            ("statuses", Enum.GetValues<StatusId>().Length, 45), ("calibrations", CalibrationCatalog.All.Count(), 50)
        };
        var short_ = targets.Where(target => target.Have < target.Want).ToArray();
        check(short_.Length == 0,
            $"arsenal categories meet their targets (short: {string.Join(", ", short_.Select(t => $"{t.Name} {t.Have}/{t.Want}"))})");

        // ---- 2. Band and definitions ----
        check(arsenal.Length > 650 && arsenal.All(catalog.Contains) && arsenal.All(ArsenalItems.Owns) &&
              arsenal.Distinct().Count() == arsenal.Length && arsenal.All(id => catalog[id].Tier is >= 1 and <= 7),
            "arsenal items are registered in their own id band with a tier");

        // ---- 3. Every weapon has a style; ranged weapons name an ammo family that exists ----
        var weapons = arsenal.Select(id => catalog[id]).Where(item => item.UseKind is ItemUseKind.Melee or ItemUseKind.Ranged or
            ItemUseKind.Tech or ItemUseKind.Command).ToArray();
        var ammoKinds = catalog.All.Where(item => item.UseKind == ItemUseKind.Ammunition).Select(item => item.AmmoKind).ToHashSet();
        var shooters = weapons.Where(item => item.Style is WeaponStyle.Bow or WeaponStyle.Gun or WeaponStyle.Shotgun or WeaponStyle.Launcher).ToArray();
        check(weapons.Length >= 317 && weapons.All(item => item.Style != WeaponStyle.None) &&
              shooters.All(item => item.UsesAmmo != AmmoClass.None && ammoKinds.Contains(item.UsesAmmo)) &&
              weapons.Where(item => item.Style is WeaponStyle.SummonStaff or WeaponStyle.SentryStaff)
                  .All(item => item.Summons is { } kind && MinionCatalog.Get(kind).IsSentry == (item.Style == WeaponStyle.SentryStaff)) &&
              weapons.Where(item => item.Style == WeaponStyle.Thrown).All(item => item.Thrown && item.MaxStack >= 99),
            "every arsenal weapon has a style, shooters fire an ammo family that exists, summons match drone/sentry, thrown weapons stack");

        // ---- 4. Obtainability ----
        var unobtainable = arsenal.Where(id => crafting.FindByOutput(id).Count == 0 && !ArsenalItems.LootSources.ContainsKey(id)).ToArray();
        var badSources = ArsenalItems.LootSources.Where(pair =>
        {
            var parts = pair.Value.Split(':');
            return parts[0] switch
            {
                "boss" => !Enum.TryParse<EnemyKind>(parts[1], out var boss) || !EnemyArchetypes.Contains(boss) || !EnemyArchetypes.Get(boss).IsBoss,
                "mob" => !Enum.TryParse<EnemyKind>(parts[1], out var mob) || !EnemyArchetypes.Contains(mob) || EnemyArchetypes.Get(mob).IsBoss ||
                         !int.TryParse(parts[2], out var pct) || pct is < 1 or > 100,
                "cache" => false,
                "shop" => !Enum.TryParse<TownNpcKind>(parts[1], out _),
                _ => true
            };
        }).Select(pair => pair.Key).ToArray();
        var recipeInputsOk = arsenal.SelectMany(crafting.FindByOutput)
            .All(recipe => recipe.Ingredients.All(group => group.Alternatives.All(catalog.Contains) && group.Quantity > 0));
        check(unobtainable.Length == 0 && badSources.Length == 0 && recipeInputsOk &&
              ArsenalItems.BossPools.Count >= 15 && ArsenalItems.CacheItems.Any() && ArsenalItems.ShopItems(TownNpcKind.Demolitionist).Any(),
            $"every arsenal item has a recipe or a valid drop/cache/shop source (unobtainable: {string.Join(",", unobtainable.Take(5))}; bad: {string.Join(",", badSources.Take(5))})");

        // ---- 5. Tier scaling: ladder damage rises with tier inside each style family ----
        bool Rising(string suffix) => arsenal.Where(id => id.ToString().EndsWith(suffix, StringComparison.Ordinal)).Select(id => catalog[id])
            .GroupBy(item => item.Tier).OrderBy(group => group.Key).Select(group => group.Average(item => item.Damage))
            .Zip(arsenal.Where(id => id.ToString().EndsWith(suffix, StringComparison.Ordinal)).Select(id => catalog[id])
                .GroupBy(item => item.Tier).OrderBy(group => group.Key).Select(group => group.Average(item => item.Damage)).Skip(1))
            .All(pair => pair.Second > pair.First);
        check(Rising("Longblade") && Rising("RailBow") && Rising("EmitterStaff") && Rising("DroneUplink") && Rising("Sidearm") &&
              catalog[ItemId.AstralLongblade].Damage > catalog[ItemId.ViridiumKatana].Damage &&
              catalog[ItemId.FerriteLongblade].Damage is >= 12 and <= 22,
            "ladder weapon damage rises tier by tier and sits beside existing gear of the same tier");

        // ---- 6. Armor sets: bonuses and a special for every set ----
        var setIds = catalog.All.Where(item => item.ArmorSetId is not null).Select(item => item.ArmorSetId!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        check(ArsenalItems.NewSetIds.All(id => ArmorSets.Get(id) is not null && ArsenalItems.SetSpecials.TryGetValue(id, out var s) && s.Special != SetSpecial.None) &&
              setIds.All(ArsenalItems.SetSpecials.ContainsKey) &&
              ArsenalItems.NewSetIds.Select(id => ArmorSets.Get(id)!.Class).Distinct().Count() == 5,
            "every armor set has a stat bonus and a special effect; new sets cover all four classes");

        // ---- 7. Dodge (gear + set + buff) ----
        var dodger = new GameSimulation(seed: 7101, width: 80, height: 48);
        foreach (var id in new[] { ItemId.AstralParagonMask, ItemId.AstralParagonSuit, ItemId.AstralParagonBoots, ItemId.GhostCloak, ItemId.PhaseCloak, ItemId.EvasionSerum })
            Use(dodger, id);
        var plain = new GameSimulation(seed: 7101, width: 80, height: 48);
        for (var i = 0; i < 120; i++)
        {
            dodger.GearProbeHit(0.2f);
            plain.GearProbeHit(0.2f);
        }
        check(dodger.ActiveSetSpecial.Special == SetSpecial.Dodge && dodger.Player.Statuses.IsActive(StatusId.Evasion) &&
              dodger.GearDodges is > 20 and < 80 && plain.GearDodges == 0,
            $"dodge from set special, cloaks and the evasion serum avoids hits ({dodger.GearDodges}/120)");

        // ---- 8. Thorns and endurance ----
        var thorny = new GameSimulation(seed: 7102, width: 80, height: 48);
        var thornsBefore = thorny.CombatStats.Thorns;
        Use(thorny, ItemId.BarbMesh);
        Use(thorny, ItemId.ThornskinSerum);
        var tank = new GameSimulation(seed: 7103, width: 80, height: 48);
        Use(tank, ItemId.EnduranceStim);
        var tanked = tank.GearProbeHit(20f);
        check(Math.Abs(thorny.CombatStats.Thorns - thornsBefore - 0.5f) < 0.001f && Math.Abs(tanked - 18f) < 0.01f,
            "thorn mesh + thornskin reflect 50% of contact damage; the endurance stim removes 10% of a hit");

        // ---- 9. Knockback immunity ----
        var anchored = new GameSimulation(seed: 7104, width: 80, height: 48);
        var pushedBefore = anchored.GearProbeKnockback(new Vector2(3f, -2f));
        Use(anchored, ItemId.GravAnchor);
        check(pushedBefore != Vector2.Zero && anchored.KnockbackImmune && anchored.GearProbeKnockback(new Vector2(3f, -2f)) == Vector2.Zero,
            "the grav anchor makes the player immune to knockback");

        // ---- 10. Night vision light ----
        var lit = new GameSimulation(seed: 7105, width: 80, height: 48);
        var darkLights = lit.PlayerLightSources.Count;
        Use(lit, ItemId.FloodVisor);
        check(darkLights == 0 && lit.PlayerLightBonus >= 11f && lit.PlayerLightSources.Any(light => Math.Abs(light.Radius - lit.PlayerLightBonus) < 0.01f),
            "a night-vision visor adds a player light (PlayerLightBonus) the renderer receives");

        // ---- 11. Class-damage potion and well-fed tiers ----
        var brew = new GameSimulation(seed: 7106, width: 80, height: 48);
        var blade = brew.Items[ItemId.CobaltLongblade];
        var before = brew.ComputeWeaponDamage(blade);
        Use(brew, ItemId.CloseQuartersTonic);
        var boosted = brew.ComputeWeaponDamage(blade);
        Use(brew, ItemId.GrilledSkitterLeg);
        var fedOnce = brew.Player.Statuses.IsActive(StatusId.WellFed);
        Use(brew, ItemId.ColonyFeast);
        check(brew.Player.Statuses.IsActive(StatusId.CloseQuartersBoost) && Math.Abs(boosted / before - 1.1f) < 0.01f &&
              fedOnce && brew.Player.Statuses.IsActive(StatusId.Gourmet) && !brew.Player.Statuses.IsActive(StatusId.WellFed),
            "the close-quarters tonic adds 10% melee damage; richer food replaces the lower well-fed tier");

        // ---- 12. Calibrations respect class ----
        var rolls = new Random(99);
        var melee = catalog[ItemId.CobaltLongblade];
        var gun = catalog[ItemId.CobaltSidearm];
        var staff = catalog[ItemId.CobaltEmitterStaff];
        var charm = catalog[ItemId.KineticPlateMkI];
        var meleeRolls = Enumerable.Range(0, 300).Select(_ => CalibrationCatalog.Get(CalibrationCatalog.Roll(rolls, melee))).ToArray();
        var gunRolls = Enumerable.Range(0, 300).Select(_ => CalibrationCatalog.Get(CalibrationCatalog.Roll(rolls, gun))).ToArray();
        var staffRolls = Enumerable.Range(0, 300).Select(_ => CalibrationCatalog.Get(CalibrationCatalog.Roll(rolls, staff))).ToArray();
        var charmRolls = Enumerable.Range(0, 300).Select(_ => CalibrationCatalog.Get(CalibrationCatalog.Roll(rolls, charm))).ToArray();
        check(meleeRolls.All(c => c.Group is CalibrationGroup.Universal or CalibrationGroup.Melee && c.ChargeCostMultiplier >= 1f) &&
              meleeRolls.Any(c => c.Group == CalibrationGroup.Melee) &&
              gunRolls.All(c => c.Group is CalibrationGroup.Universal or CalibrationGroup.Ranged) && gunRolls.Any(c => c.VelocityMultiplier != 1f) &&
              staffRolls.All(c => c.Group is CalibrationGroup.Universal or CalibrationGroup.Tech) && staffRolls.Any(c => c.ChargeCostMultiplier < 1f) &&
              charmRolls.All(c => c.Group == CalibrationGroup.Accessory) &&
              CalibrationCatalog.Roll(new Random(1), catalog[ItemId.FerriteBolts]) == Calibration.None,
            "calibration rolls only pick calibrations valid for the item's class (accessories get accessory calibrations)");

        // ---- 13. Accessory calibrations apply while worn and survive unequipping ----
        var tuned = new GameSimulation(seed: 7107, width: 80, height: 48);
        var plainMelee = tuned.CombatStats.MeleeMultiplier;
        Use(tuned, ItemId.KineticPlateMkI, Calibration.Menacing);
        var worn = tuned.CombatStats.MeleeMultiplier;
        var slot = EquipmentLoadout.AccessorySlots.First(s => tuned.Player.Equipment[s] == ItemId.KineticPlateMkI);
        tuned.TryUnequipEquipment(slot);
        var back = tuned.Player.Inventory.Slots.Any(stack => stack.ItemId == ItemId.KineticPlateMkI && stack.Calibration == Calibration.Menacing);
        check(Math.Abs(worn - plainMelee - 0.04f) < 0.001f && back && Math.Abs(tuned.CombatStats.MeleeMultiplier - plainMelee) < 0.001f,
            "a Menacing accessory adds 4% damage while worn and keeps its calibration when unequipped");

        // ---- 14. Wings, mining, reach, slots 4-5 count ----
        var flyer = new GameSimulation(seed: 7108, width: 80, height: 48);
        var fuelBefore = flyer.MaximumRocketFuel;
        foreach (var id in new[] { ItemId.LifeMonitor, ItemId.DepthMeter, ItemId.CompassChip, ItemId.MinersGauntlet, ItemId.NovaWings })
            Use(flyer, id);
        check(fuelBefore == 0f && flyer.MaximumRocketFuel >= 4.2f && flyer.Player.Equipment[EquipmentSlot.Accessory5] == ItemId.NovaWings &&
              flyer.Player.Stats.ImmunityTags.Contains("glide") && Math.Abs(flyer.GearMiningFactor - 1.15f) < 0.001f,
            "wings in the fifth accessory slot add flight time and glide; the miner's gauntlet speeds mining");

        // ---- 15. Ammo families ----
        var ammo = arsenal.Select(id => catalog[id]).Where(item => item.UseKind == ItemUseKind.Ammunition).ToArray();
        check(ammo.Length >= 45 && new[] { AmmoClass.Arrow, AmmoClass.Bullet, AmmoClass.Rocket, AmmoClass.Dart, AmmoClass.Fuel }
                  .All(kind => ammo.Count(item => item.AmmoKind == kind) >= 5) &&
              ammo.All(item => item.MaxStack == ContentItems.StackSize && item.AmmoDamage > 0 && crafting.FindByOutput(item.Id).Any(recipe => recipe.OutputCount >= 15)) &&
              ammo.Any(item => item.Pierce > 0) && ammo.Any(item => item.AmmoHoming > 0f) && ammo.Any(item => item.ExplosionRadius > 0f) &&
              ammo.Any(item => item.GrantsStatus == StatusId.Burning),
            "ammo covers arrows, bullets, rockets, darts and fuel, crafts in stacks, and carries pierce/homing/explosive/burn flavour");
    }
}
