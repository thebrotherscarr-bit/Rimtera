using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// 0.1.9 C1 (IC-2): the healing and charge potion ladders, their gates and shops, potion sickness (60 s, 45 s with the
/// Bio-weave), quick heal's pick, cooked food's small heal and the balance check against each band's creatures.
/// </summary>
public static class HealingLadderSelfTests
{
    /// <summary>The audit stage each rung's recipe may not come before (Chem Lab, then the first boss, the Alloy Anvil, the Void Forge).</summary>
    private static readonly (ItemId Item, string Gate)[] Gates =
    [
        (ItemId.MedStim, "Built Chem Lab"),
        (ItemId.TraumaStim, "Defeated " + EnemyArchetypes.Get(EnemyKind.StoneSentinel).DisplayName),
        (ItemId.NaniteStim, "Built Alloy Anvil"),
        (ItemId.PhoenixStim, "Built Void Forge"),
        (ItemId.ChargeCellMedium, "Defeated " + EnemyArchetypes.Get(EnemyKind.StoneSentinel).DisplayName),
        (ItemId.ChargeCellLarge, "Built Alloy Anvil"),
        (ItemId.ChargeCellPeak, "Built Void Forge"),
        (ItemId.RestorationStim, "Built Bio Lab"),
    ];

    public static void Run(Action<bool, string> check)
    {
        var items = new ItemCatalog();

        // ---- The ladders exist ----
        var heals = ContentItems.HealingLadder.Select(rung => items[rung.Item].HealAmount).ToArray();
        var charges = ContentItems.ChargeLadder.Select(rung => items[rung.Item].ChargeRestore).ToArray();
        var restoration = items[ItemId.RestorationStim];
        check(heals.SequenceEqual([50f, 100f, 150f, 200f]) && charges.SequenceEqual([40f, 100f, 200f, 300f]) &&
              restoration is { HealAmount: 90f, PotionSicknessSeconds: 45f } &&
              ContentItems.HealingLadder.All(rung => items[rung.Item].UseKind == ItemUseKind.Consumable) &&
              ContentItems.HealingLadder.Zip(ContentItems.HealingLadder.Skip(1)).All(pair => items[pair.First.Item].Tier < items[pair.Second.Item].Tier),
            "healing ladder 50/100/150/200, charge ladder 40/100/200/300 and a 90-hit-point restoration stim, tiers rising");

        // ---- Each rung is craftable only after its gate ----
        var audit = CraftingAudit.Run();
        var crafting = audit.Crafting;
        var late = new List<string>();
        foreach (var (item, gate) in Gates)
        {
            var gateStage = audit.Stages.ToList().FindIndex(stage => stage == gate);
            var recipes = crafting.FindByOutput(item).ToArray();
            var earliest = recipes.Length == 0 ? -1 : recipes.Min(recipe => CraftableStage(audit, recipe));
            if (gateStage < 0 || recipes.Length == 0 || earliest < gateStage || earliest == int.MaxValue || !audit.Has(item))
                late.Add($"{item} (gate '{gate}' at {gateStage}, craftable at {earliest})");
        }
        check(late.Count == 0, "every ladder rung is craftable, and only after its gate: Chem Lab, the first boss (Bio Lab), the Alloy Anvil, the Void Forge" +
                               (late.Count == 0 ? "" : $" (wrong: {string.Join("; ", late)})"));

        // ---- Shops: the Medic sells the healing ladder and the Quartermaster the charge ladder, by flag ----
        var medic = TownNpcCatalog.Get(TownNpcKind.Medic).Shop;
        var quartermaster = TownNpcCatalog.Get(TownNpcKind.Quartermaster).Shop;
        check(medic.Any(line => line.Item == ItemId.TraumaStim && line.RequiredFlag == "boss.stone_sentinel.down") &&
              medic.Any(line => line.Item == ItemId.NaniteStim && line.RequiredFlag is not null) &&
              medic.Any(line => line.Item == ItemId.PhoenixStim && line.RequiredFlag == "boss.void_cantor.down") &&
              medic.Any(line => line.Item == ItemId.RestorationStim) &&
              quartermaster.Any(line => line.Item == ItemId.ChargeCellMedium && line.RequiredFlag is not null) &&
              quartermaster.Any(line => line.Item == ItemId.ChargeCellLarge && line.RequiredFlag is not null) &&
              quartermaster.Any(line => line.Item == ItemId.ChargeCellPeak && line.RequiredFlag == "boss.void_cantor.down"),
            "the Medic stocks the healing ladder and the Quartermaster the charge ladder, each rung behind a progress flag");

        // ---- Quick heal: the smallest rung that covers the missing hit points, otherwise the largest ----
        var ladder = ContentItems.HealingLadder.Select(rung => items[rung.Item]).ToArray();
        ItemId? Pick(float missing) => GameSimulation.PickHealingItem(ladder, missing)?.Id;
        check(Pick(30f) == ItemId.MedStim && Pick(90f) == ItemId.TraumaStim && Pick(180f) == ItemId.PhoenixStim &&
              Pick(400f) == ItemId.PhoenixStim && Pick(120f) == ItemId.NaniteStim,
            "quick heal picks the Med stim at 30 missing, the Trauma stim at 90 and the Phoenix stim at 180 (the largest beyond 200)");
        var live = new List<ItemId>();
        foreach (var missing in new[] { 30f, 90f, 180f })
        {
            var game = NewGame(9501);
            game.Player.LifeCoresUsed = PlayerCharacter.MaximumLifeCores;
            game.Player.Heal(1e6f);
            foreach (var rung in ContentItems.HealingLadder)
                game.Player.Inventory.Add(rung.Item, 1);
            game.Player.TakeDamage(missing);
            var before = ContentItems.HealingLadder.ToDictionary(rung => rung.Item, rung => game.Player.Inventory.Count(rung.Item));
            if (game.QuickHeal())
                live.AddRange(ContentItems.HealingLadder.Where(rung => game.Player.Inventory.Count(rung.Item) < before[rung.Item]).Select(rung => rung.Item));
        }
        check(live.SequenceEqual([ItemId.MedStim, ItemId.TraumaStim, ItemId.PhoenixStim]),
            $"in play, quick heal drinks the Med stim, Trauma stim and Phoenix stim at 30, 90 and 180 missing (drank {string.Join(", ", live)})");

        // ---- Potion sickness: 60 s, 45 s with the Bio-weave; the restoration stim's own 45 s ----
        float Sickness(bool accessory, ItemId stim)
        {
            var game = NewGame(9502);
            if (accessory)
                game.Player.Equipment.Set(EquipmentSlot.Accessory, ContentItems.PotionSicknessAccessory);
            game.Player.TakeDamage(60f);
            game.Player.Inventory.Add(stim, 1);
            game.QuickHeal();
            return game.ActiveBuffs.FirstOrDefault(buff => buff.Status == StatusId.PotionSickness)?.RemainingSeconds ?? 0f;
        }
        var plain = Sickness(false, ItemId.MedStim);
        var charmed = Sickness(true, ItemId.MedStim);
        var restored = Sickness(false, ItemId.RestorationStim);
        check(Math.Abs(plain - 60f) < 1.5f && Math.Abs(charmed - 45f) < 1.5f && Math.Abs(restored - 45f) < 1.5f &&
              items[ContentItems.PotionSicknessAccessory].ImmunityTags.Contains("potion-sickness:0.75"),
            $"potion sickness lasts 60 s, 45 s with the Bio-weave's potion-sickness:0.75 tag, 45 s after a restoration stim ({plain:0.#}/{charmed:0.#}/{restored:0.#})");
        var blocked = NewGame(9503);
        blocked.Player.LifeCoresUsed = PlayerCharacter.MaximumLifeCores;
        blocked.Player.Heal(1e6f);
        blocked.Player.TakeDamage(150f);
        blocked.Player.Inventory.Add(ItemId.NaniteStim, 2);
        check(blocked.QuickHeal() && !blocked.QuickHeal() && blocked.Player.Inventory.Count(ItemId.NaniteStim) == 1,
            "a higher rung brings the same sickness: a second quick heal is refused");

        // ---- Quick charge: the smallest cell that covers the missing charge ----
        var cells = NewGame(9504);
        foreach (var rung in ContentItems.ChargeLadder)
            cells.Player.Inventory.Add(rung.Item, 1);
        cells.Player.ChargeCrystalsUsed = PlayerItems.MaximumChargeCrystals; // a full charge bar (Terraria's 200 mana)
        var maxCharge = cells.MaximumCharge;
        SetCharge(cells, maxCharge - 30f);
        var usedSmall = cells.QuickCharge() && cells.Player.Inventory.Count(ItemId.ChargeCellSmall) == 0;
        var afterSmall = cells.Charge;
        check(usedSmall && cells.Player.Inventory.Count(ItemId.ChargeCellMedium) == 1,
            $"quick charge uses the smallest charge cell that covers the missing charge (max {maxCharge:0}, now {afterSmall:0})");

        // ---- Cooked food heals a little and feeds; raw food does not heal ----
        var cook = NewGame(9505);
        cook.Player.TakeDamage(50f);
        var hurt = cook.Player.HitPoints;
        Use(cook, ItemId.SnowWolfSteak);
        var afterSteak = cook.Player.HitPoints;
        Use(cook, ItemId.Berry);
        var cookedFoods = items.All.Where(item => item.UseKind == ItemUseKind.Food && item.FoodHeal > 0f).ToArray();
        check(Math.Abs(afterSteak - hurt - 10f) < 0.5f && cook.Player.Statuses.IsActive(StatusId.WellFed) &&
              Math.Abs(cook.Player.HitPoints - afterSteak) < 0.5f && items[ItemId.Berry].FoodHeal == 0f &&
              items[ItemId.AstroBurger].FoodHeal == 20f && cookedFoods.Length >= 20 &&
              cookedFoods.All(item => item.GrantsStatus is StatusId.WellFed or StatusId.Satiated or StatusId.Gourmet) &&
              !cookedFoods.Any(item => item.HealAmount > 0f),
            $"cooked food heals 10/15/20 by well-fed tier and feeds; raw food heals nothing; food never counts as a healing stim ({cookedFoods.Length} dishes)");

        // ---- Balance: in each band, the median creature hit is at most half that band's potion ----
        var bands = new (ItemId Stim, string Label, float Low, float High)[]
        {
            (ItemId.MedStim, "HP < 150", 0f, 150f), (ItemId.TraumaStim, "HP 150-400", 150f, 400f),
            (ItemId.NaniteStim, "HP 400-1000", 400f, 1000f), (ItemId.PhoenixStim, "HP 1000+", 1000f, float.MaxValue)
        };
        var report = new List<string>();
        var balanced = true;
        foreach (var (stim, label, low, high) in bands)
        {
            var hits = EnemyArchetypes.All.Where(archetype => !archetype.IsBoss && !archetype.Passive && archetype.AttackDamage > 0f &&
                                                              archetype.HitPoints >= low && archetype.HitPoints < high)
                .Select(archetype => archetype.AttackDamage * DefenseBalance.EnemyHitScale(archetype)).OrderBy(hit => hit).ToArray();
            var median = hits.Length == 0 ? 0f : hits[hits.Length / 2];
            balanced &= hits.Length > 0 && median <= items[stim].HealAmount * 0.5f;
            report.Add($"{label}: {median:0.#} vs {items[stim].HealAmount:0}");
        }
        check(balanced, $"balance: each band's median creature hit is at most half its potion ({string.Join("; ", report)})");
    }

    /// <summary>The audit stage from which a recipe can be made: its last station and its slowest ingredient.</summary>
    private static int CraftableStage(CraftingAudit.Result audit, RecipeDefinition recipe)
    {
        var stage = 0;
        foreach (var station in recipe.RequiredStations ?? new HashSet<CraftingStationId>())
            stage = Math.Max(stage, audit.Stations.TryGetValue(station, out var s) ? s : int.MaxValue);
        foreach (var group in recipe.Ingredients)
        {
            var best = group.Alternatives.Select(id => audit.Items.TryGetValue(id, out var reach) ? reach.Stage : int.MaxValue).Min();
            stage = Math.Max(stage, best);
        }
        return stage;
    }

    private static GameSimulation NewGame(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 80, height: 48) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e7f);
        game.Tick(new GameInput(new MovementInput(0f), PlayerAction.None, null));
        return game;
    }

    private static void SetCharge(GameSimulation game, float value) =>
        typeof(GameSimulation).GetField("_charge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(game, value);

    private static void Use(GameSimulation game, ItemId item)
    {
        game.Player.Inventory.Add(item, 1);
        game.Player.ActionCooldownTicks = 0;
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
        var target = new GridPoint((int)game.Player.Motion.Position.X + 2, (int)game.Player.Motion.Position.Y);
        game.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, target));
    }
}
