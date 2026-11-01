using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Equipping people (0.4.0): residents take Terraria's gear from your bag (a weapon, armour, two accessories); armour gives
/// defense by Terraria's half-defense rule and life; a weapon makes them a guard who fights with it; gear is saved and drops
/// where they fall.
/// </summary>
public static class ResidentGearSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Flat(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 120, height: 56) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        game.Player.Motion.Position = new Vector2(8f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.SetTimeOfDay(10f);
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static TownNpc Housed(GameSimulation game, TownNpcKind kind, float x)
    {
        var npc = game.AddTownNpc(kind, new Vector2(x, Floor - 3f), bound: false);
        npc.Motion.Position = new Vector2(x, Floor - npc.Motion.Size.Y - 0.01f);
        npc.HomeCell = new GridPoint((int)x, Floor - 2);
        return npc;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- what goes where ----
        var game = Flat(9851);
        check(game.ResidentSlotFor(ItemId.CopperSword) == ResidentGearSlot.Weapon && game.ResidentSlotFor(ItemId.ScrapPistol) == ResidentGearSlot.Weapon &&
              game.ResidentSlotFor(ItemId.Trailcap) == ResidentGearSlot.Head && game.ResidentSlotFor(ItemId.Trailcoat) == ResidentGearSlot.Body &&
              game.ResidentSlotFor(ItemId.HeatWard) == ResidentGearSlot.Accessory && game.ResidentSlotFor(ItemId.CopperPickaxe) is null &&
              game.ResidentSlotFor(ItemId.RockBlock) is null,
            "weapons, armour pieces and accessories go in a resident's gear; tools and blocks do not");

        // ---- handing gear over: from the bag, a swap gives the old piece back, two accessories ----
        var armorer = Housed(game, TownNpcKind.Armorer, 40f);
        foreach (var item in new[] { ItemId.Trailcap, ItemId.Trailcoat, ItemId.Trailboots, ItemId.ScavHelmet, ItemId.HeatWard, ItemId.HearthCharm, ItemId.KineticPlateMkI })
            game.Player.Inventory.Add(item, 1);
        foreach (var item in new[] { ItemId.ScavHelmet, ItemId.Trailcoat, ItemId.Trailboots, ItemId.HeatWard, ItemId.HearthCharm })
            game.EquipResident(armorer, item);
        var swapped = game.EquipResident(armorer, ItemId.Trailcap);
        var helmBack = game.Player.Inventory.Count(ItemId.ScavHelmet) == 1 && game.Player.Inventory.Count(ItemId.Trailcap) == 0;
        var third = game.EquipResident(armorer, ItemId.KineticPlateMkI);
        var accessories = new[] { armorer.Gear.GetValueOrDefault(ResidentGearSlot.Accessory), armorer.Gear.GetValueOrDefault(ResidentGearSlot.Accessory2) };
        var refusedAi = game.EquipResident(Housed(game, TownNpcKind.StationAi, 70f), ItemId.HearthCharm).Contains("cannot");
        check(armorer.Gear[ResidentGearSlot.Head] == ItemId.Trailcap && swapped.Contains("hands back") && helmBack &&
              accessories.Contains(ItemId.KineticPlateMkI) && accessories.Distinct().Count() == 2 && refusedAi,
            $"a resident takes gear from your bag; a swap hands the old piece back; they wear two accessories (a third replaces one); the station AI wears nothing ({third})");

        // ---- defense and life: Terraria's half-defense rule; armour life; a full set's bonus ----
        var defense = game.ResidentDefense(armorer);
        var expected = TownNpc.BaseDefense + 1 + 2 + 1 + 2 /* trail set */ + game.Items[accessories[0]].Defense + game.Items[accessories[1]].Defense;
        var before = armorer.HitPoints;
        game.HurtTownNpc(armorer, 20f);
        var lost = before - armorer.HitPoints;
        check(defense == expected && MathF.Abs(lost - (20f - defense / 2f)) < 0.01f && armorer.MaximumHitPoints == TownNpc.BaseHitPoints + 8f &&
              MathF.Abs(game.ResidentDamageTaken(armorer, 1f) - 1f) < 0.001f,
            $"residents have Terraria's town-NPC 250 life and 15 defense, and armour and accessories add to it (DEF {defense}, with the trail set's bonus) and life (max {armorer.MaximumHitPoints:0}); a hit loses half the defense ({lost:0.0} of 20), at least 1 gets through");

        // ---- a weapon makes them a guard, and they fight with it ----
        var unarmed = Flat(9852);
        var meek = Housed(unarmed, TownNpcKind.Cook, 60f);
        var meekGuards = meek.Work.Priority(WorkType.Guard) != WorkCatalog.Off;
        unarmed.Player.Inventory.Add(ItemId.CopperSword, 1);
        unarmed.EquipResident(meek, ItemId.CopperSword);
        var (swordHit, swordReach, _) = unarmed.ResidentAttack(meek);
        var fists = GameSimulation.GuardStrikeDamage(meek.Stats);
        unarmed.Player.Inventory.Add(ItemId.ScrapPistol, 1);
        unarmed.EquipResident(meek, ItemId.ScrapPistol);
        var (_, gunReach, _) = unarmed.ResidentAttack(meek);
        var slime = unarmed.SpawnCreatureAt(EnemyKind.MossSlime, new Vector2(66f, Floor - 1.2f));
        var health = slime.HitPoints;
        var guarded = false;
        for (var i = 0; i < 600 && slime.IsAlive; i++)
        {
            unarmed.Tick(new GameInput(new MovementInput(0f)));
            guarded |= meek.Activity.StartsWith("Guarding");
        }
        check(meek.Work.Priority(WorkType.Guard) == WorkCatalog.Highest && swordHit > 0f && swordReach < gunReach &&
              gunReach == GameSimulation.RangedGuardReach && guarded && (!slime.IsAlive || slime.HitPoints < health),
            $"handing a weapon makes anyone a guard (the cook guarded before: {meekGuards}); they fight with it, a sword up close ({swordHit:0} a blow, fists {fists:0}) " +
            $"and a gun from {gunReach:0} tiles (slime {(slime.IsAlive ? $"{slime.HitPoints:0}/{health:0}" : "dead")})");

        // ---- saved with them; dropped where they fall ----
        var reloaded = GameSimulation.Restore(game.CaptureSnapshot());
        var again = reloaded.TownNpcs.Single(npc => npc.Kind == TownNpcKind.Armorer);
        var sameGear = again.Gear.OrderBy(pair => pair.Key).SequenceEqual(armorer.Gear.OrderBy(pair => pair.Key)) &&
                       MathF.Abs(again.HitPoints - armorer.HitPoints) < 0.01f && again.MaximumHitPoints == armorer.MaximumHitPoints;
        var worn = armorer.Gear.Count;
        game.HurtTownNpc(armorer, 1e6f);
        var dropped = game.GroundItems.Count(item => item.ItemId is ItemId.Trailcap or ItemId.Trailcoat or ItemId.Trailboots or ItemId.HeatWard or
            ItemId.HearthCharm or ItemId.KineticPlateMkI);
        check(sameGear && worn == 5 && dropped == 5,
            $"a resident's gear and hurt are saved with the world, and their gear drops where they fall ({dropped} of {worn} pieces on the ground)");
    }
}
