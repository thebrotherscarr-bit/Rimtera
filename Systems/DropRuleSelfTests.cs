using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// 0.1.9 C2-C5: drop rules with chances and conditions and the event units' drops (EN-11), hearts and charge orbs (EN-10),
/// boss potions and hearts (BO-11) and the 9999 stack-size pass (IC-19).
/// </summary>
public static class DropRuleSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        // ---- Chances: a 1/500 rule lands within 0.5-3x its expected rate over 50,000 rolls ----
        var rare = new NpcDropDefinition(EnemyKind.EventGrunt, ItemId.WarlordsMedal).OneIn(500);
        var random = new Random(9601);
        var hits = Enumerable.Range(0, 50_000).Count(_ => rare.RollChance(random));
        var certain = new NpcDropDefinition(EnemyKind.EventGrunt, ItemId.ScrapIronOre, 2);
        check(hits is >= 50 and <= 300 && Math.Abs(rare.Chance - 0.002) < 1e-9 && certain.Guaranteed &&
              Enumerable.Range(0, 100).All(_ => certain.RollChance(random)),
            $"a 1/500 rule hits {hits} times in 50,000 rolls (expected 100, allowed 50-300); a plain rule always drops");

        // ---- Conditions refuse off-condition ----
        var none = new HashSet<WorldEventKind>();
        var day = new NpcDropContext(false, false, WorldDifficulty.Classic, none);
        var night = day with { Night = true };
        var fractured = day with { Fracture = true };
        var brutal = day with { Difficulty = WorldDifficulty.Brutal };
        var raid = day with { Events = new HashSet<WorldEventKind> { WorldEventKind.ScavengerRaid } };
        NpcDropDefinition Rule(DropCondition conditions) => certain with { Conditions = conditions };
        check(Rule(DropCondition.Fracture).Allows(fractured) && !Rule(DropCondition.Fracture).Allows(day) &&
              Rule(DropCondition.PreFracture).Allows(day) && !Rule(DropCondition.PreFracture).Allows(fractured) &&
              Rule(DropCondition.Night).Allows(night) && !Rule(DropCondition.Night).Allows(day) &&
              Rule(DropCondition.Day).Allows(day) && !Rule(DropCondition.Day).Allows(night) &&
              (certain with { MinimumDifficulty = WorldDifficulty.Brutal }).Allows(brutal) &&
              !(certain with { MinimumDifficulty = WorldDifficulty.Brutal }).Allows(day) &&
              certain.During(WorldEventKind.ScavengerRaid).Allows(raid) && !certain.During(WorldEventKind.ScavengerRaid).Allows(day) &&
              !certain.During(WorldEventKind.PirateBoarding).Allows(raid) &&
              !Rule(DropCondition.Fracture | DropCondition.Night).Allows(fractured) && Rule(DropCondition.Fracture | DropCondition.Night).Allows(fractured with { Night = true }),
            "drop conditions (Fracture, before it, night, day, difficulty, event) refuse off-condition and combine");
        var signature = NpcDropRegistry.GetDrops(EnemyKind.EventScavengerGrunt).Single(drop => drop.Item == ItemId.WarlordsMedal);
        var offEvent = Enumerable.Range(0, 3000).Sum(_ => NpcDropRegistry.Roll(EnemyKind.EventScavengerGrunt, day, random).Count(drop => drop.Item == ItemId.WarlordsMedal));
        var inEvent = Enumerable.Range(0, 3000).Sum(_ => NpcDropRegistry.Roll(EnemyKind.EventScavengerGrunt, raid, random).Count(drop => drop.Item == ItemId.WarlordsMedal));
        var materials = Enumerable.Range(0, 50).All(_ => NpcDropRegistry.Roll(EnemyKind.EventScavengerGrunt, day, random).Any(drop => drop.Item == ItemId.ScrapIronOre));
        check(signature is { Denominator: 200, Event: WorldEventKind.ScavengerRaid } && offEvent == 0 && inEvent > 0 && materials &&
              ReferenceEquals(NpcDropRegistry.Roll(EnemyKind.MossSlime, day, random), NpcDropRegistry.GetDrops(EnemyKind.MossSlime)),
            $"rolling a creature's drops: the raid signature drops only during its raid ({inEvent} in 3,000 raid kills), materials always, plain tables untouched");

        // ---- The event units all drop items ----
        var eventUnits = Enum.GetValues<EnemyKind>().Where(kind => kind.ToString().StartsWith("Event", StringComparison.Ordinal) &&
                                                                   kind.ToString() != "EventBosses").ToArray();
        var catalog = new ItemCatalog();
        var empty = eventUnits.Where(kind => !NpcDropRegistry.GetDrops(kind).Any(drop => catalog.Contains(drop.Item) && drop.Item != ItemId.Credits)).ToArray();
        var signatures = NpcDropRegistry.EventUnits.SelectMany(NpcDropRegistry.GetDrops).Where(drop => drop.Conditions.HasFlag(DropCondition.DuringEvent))
            .Select(drop => drop.Item).Distinct().ToArray();
        check(eventUnits.Length >= 48 && empty.Length == 0 && signatures.Length >= 5 &&
              signatures.All(item => ArsenalItems.LootSources.ContainsKey(item)),
            $"all {eventUnits.Length} event units drop items: their event's materials, and {signatures.Length} invasions add a rare arsenal signature" +
            (empty.Length == 0 ? "" : $" (no drops: {string.Join(", ", empty)})"));

        // ---- The crafting audit still finds every item, with conditional drops gated ----
        var audit = CraftingAudit.Run();
        check(audit.Unreachable.Count == 0 && audit.Has(ItemId.HeartPickup) && audit.Has(ItemId.ChargeOrb) &&
              audit.Items[ItemId.WarlordsMedal].Stage <= audit.Stages.Count,
            "--craft-audit still reports every item obtainable (event drops and pickups included)");

        // ---- Hearts: over 200 kills at half health, 15-60 drop; none at full health ----
        var hurt = NewGame(9602);
        hurt.Player.TakeDamage(hurt.Player.MaximumHitPoints / 2f);
        var halfChance = hurt.HeartDropChance;
        var hearts = KillMany(hurt, 200, ItemId.HeartPickup);
        var healthy = NewGame(9603);
        var fullHearts = KillMany(healthy, 200, ItemId.HeartPickup);
        check(hearts is >= 15 and <= 60 && fullHearts == 0 && halfChance > healthy.HeartDropChance && healthy.HeartDropChance == 0f,
            $"over 200 kills at half health {hearts} hearts drop (15-60, chance {halfChance:0.###}); none at full health");
        var drained = NewGame(9604);
        drained.Player.ChargeCrystalsUsed = PlayerItems.MaximumChargeCrystals;
        SetCharge(drained, 0f);
        var orbs = KillMany(drained, 200, ItemId.ChargeOrb);
        var fullOrbs = KillMany(healthy, 100, ItemId.ChargeOrb);
        check(orbs >= 30 && fullOrbs == 0, $"charge orbs drop for an emptied charge bar ({orbs} in 200 kills), never for a full one");

        // ---- A pickup heals or charges on touch and never stacks in the inventory ----
        var touch = NewGame(9605);
        touch.Player.TakeDamage(60f);
        touch.Player.ChargeCrystalsUsed = PlayerItems.MaximumChargeCrystals;
        SetCharge(touch, 0f);
        var hp = touch.Player.HitPoints;
        touch.SpawnGroundItem(ItemId.HeartPickup, 1, Center(touch));
        touch.SpawnGroundItem(ItemId.HeartPickup, 1, Center(touch));
        touch.SpawnGroundItem(ItemId.ChargeOrb, 1, Center(touch));
        touch.CollectOrDrop(ItemId.HeartPickup, 1, Center(touch)); // a drop at the player's feet still lands as a pickup
        var groundBefore = touch.GroundItems.Count(item => catalog[item.ItemId].IsInstantPickup);
        Step(touch, 30);
        check(groundBefore == 4 && Math.Abs(touch.Player.HitPoints - hp - 60f) < 1f && touch.Charge is >= 99f and < 160f &&
              touch.Player.Inventory.Count(ItemId.HeartPickup) == 0 && touch.Player.Inventory.Count(ItemId.ChargeOrb) == 0 &&
              touch.GroundItems.All(item => !catalog[item.ItemId].IsInstantPickup) && touch.HeartsPickedUp == 3 &&
              catalog[ItemId.HeartPickup].MaxStack == 1,
            $"hearts heal 20 and charge orbs restore 100 on touch, one by one; they never enter the inventory or stack " +
            $"({groundBefore} lying, +{touch.Player.HitPoints - hp:0.#} hp, charge {touch.Charge:0.#}, {touch.HeartsPickedUp} hearts)");
        var bagged = NewGame(9606);
        bagged.Player.TakeDamage(30f);
        var bagHp = bagged.Player.HitPoints;
        bagged.Player.Inventory.Add(ItemId.HeartPickup, 1);
        bagged.Player.ActionCooldownTicks = 0;
        bagged.TrySelectInventorySlot(bagged.Player.Inventory.FindSlot(ItemId.HeartPickup));
        bagged.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, new GridPoint((int)Center(bagged).X + 2, (int)Center(bagged).Y)));
        check(bagged.Player.Inventory.Count(ItemId.HeartPickup) == 0 && bagged.Player.HitPoints > bagHp + 15f && !bagged.PotionSick,
            "a heart that reached a bag anyway is used up at once, with no potion sickness");

        // ---- Boss potions and hearts on every difficulty ----
        var shortfalls = new List<string>();
        var bosses = EnemyArchetypes.All.Where(archetype => archetype.IsBoss).ToArray();
        foreach (var difficulty in new[] { WorldDifficulty.Classic, WorldDifficulty.Brutal, WorldDifficulty.Annihilation })
        {
            var game = NewGame(9607 + (int)difficulty);
            game.Difficulty = difficulty;
            foreach (var boss in bosses)
            {
                game.ClearAuditLoot();
                var (stim, _, _) = GameSimulation.BossPotions(boss.Tier);
                var agent = game.SpawnCreature(boss.Kind, Center(game) + new Vector2(6f, -3f));
                for (var round = 0; round < 6 && game.Enemies.Any(enemy => enemy.IsAlive && enemy.IsBoss); round++)
                {
                    foreach (var enemy in game.Enemies.Where(enemy => enemy.IsAlive).ToArray())
                        enemy.TakeDamage(1e7f);
                    Step(game, 1);
                }
                var potions = game.Player.Inventory.Count(stim) + game.GroundItems.Where(item => item.ItemId == stim).Sum(item => item.Count);
                var bossHearts = game.GroundItems.Count(item => item.ItemId == ItemId.HeartPickup);
                if (potions < 5 || bossHearts < 3)
                    shortfalls.Add($"{difficulty} {boss.Kind}: {potions} {stim}, {bossHearts} hearts");
                game.Player.Heal(1e6f);
            }
        }
        check(shortfalls.Count == 0 && GameSimulation.BossPotions(BossTier.Gate).Stim == ItemId.MedStim &&
              GameSimulation.BossPotions(BossTier.Hard).Stim == ItemId.TraumaStim && GameSimulation.BossPotions(BossTier.Event).Stim == ItemId.NaniteStim &&
              GameSimulation.BossPotions(BossTier.Final).Stim == ItemId.PhoenixStim,
            $"every one of {bosses.Length} bosses leaves at least 5 stims of its tier and 3 hearts on Classic, Brutal and Annihilation" +
            (shortfalls.Count == 0 ? "" : $" (short: {string.Join("; ", shortfalls.Take(8))})"));

        // ---- Stack sizes: 9999 for every stackable; equipment and tools stay single ----
        var unstacked = catalog.All.Where(item => item.MaxStack > 1 && item.EquipmentSlot is null && item.Id != ItemId.Credits &&
                                                  item.MaxStack != ContentItems.StackSize).Select(item => item.Id).ToArray();
        var single = catalog.All.Where(item => item.EquipmentSlot is not null ||
                                               item.UseKind is ItemUseKind.Pickaxe or ItemUseKind.Cutter or ItemUseKind.Demolisher or ItemUseKind.BugNet ||
                                               // weapons, except the thrown ones that are spent as they are used (Terraria stacks those too)
                                               item.EffectiveClass != DamageClass.None && !item.Thrown && item.Style != WeaponStyle.Thrown &&
                                               item.UseKind is ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Tech or ItemUseKind.Command)
            .Where(item => item.MaxStack != 1).Select(item => item.Id).ToArray();
        var bag = new Inventory(catalog, slotCount: 2);
        var filled = bag.Add(ItemId.RockBlock, ContentItems.StackSize) == ContentItems.StackSize && bag.Slots[0].Count == ContentItems.StackSize;
        check(unstacked.Length == 0 && single.Length == 0 && filled && catalog[ItemId.MedStim].MaxStack == 9999 &&
              catalog[ItemId.Arrow].MaxStack == 9999 && catalog[ItemId.RockBlock].MaxStack == 9999 &&
              ContentItems.StackSize.ToString().Length == 4,
            "every non-equipment stackable stacks to 9999 (a four-digit count, credits aside); weapons, armor, accessories and tools stay at 1" +
            (unstacked.Length + single.Length == 0 ? "" : $" (wrong: {string.Join(", ", unstacked.Concat(single).Take(12))})"));
    }

    /// <summary>Kills <paramref name="kills"/> slimes well away from the player and counts the pickups of a kind left lying.</summary>
    private static int KillMany(GameSimulation game, int kills, ItemId pickup)
    {
        var before = game.GroundItems.Count(item => item.ItemId == pickup);
        for (var i = 0; i < kills; i++)
        {
            var slime = game.SpawnCreature(EnemyKind.MossSlime, Center(game) + new Vector2(i % 2 == 0 ? 14f : -14f, -2f));
            slime.TakeDamage(1e7f);
            Step(game, 1);
        }
        return game.GroundItems.Count(item => item.ItemId == pickup) - before;
    }

    private static GameSimulation NewGame(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 96, height: 56) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e7f);
        Step(game, 1);
        return game;
    }

    private static Vector2 Center(GameSimulation game) => game.Player.Motion.Position + game.Player.Motion.Size / 2f;

    private static void Step(GameSimulation game, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f), PlayerAction.None, null));
    }

    private static void SetCharge(GameSimulation game, float value) =>
        typeof(GameSimulation).GetField("_charge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(game, value);
}
