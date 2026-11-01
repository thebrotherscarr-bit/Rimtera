using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Food as the one need (0.5.5, docs/VISION.md §3.3: no mood or rest for now). A resident's food runs down over about twenty
/// minutes; below half they eat from the chests near home, the most filling food first (the cook's meals, the farmer's
/// harvest, anything you store). Hungry (below a quarter) they work and walk slower; starving (empty) they lose life, though
/// hunger alone never kills. The station AI, travelling traders and town gels do not eat. Food is saved.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>Food lost a real second (full to empty in twenty minutes).</summary>
    public const float FoodPerSecond = 1f / 1200f;
    /// <summary>They go and eat below this; they are hungry (slower) below <see cref="HungryBelow"/>.</summary>
    public const float EatBelow = 0.5f, HungryBelow = 0.25f;
    /// <summary>Life a starving resident loses a second (never below 10% of their life).</summary>
    public const float StarvingDamagePerSecond = 1f;

    /// <summary>Whether a resident eats at all.</summary>
    public static bool Eats(TownNpc npc) => npc.Kind != TownNpcKind.StationAi && !TownTrades.Idle(npc.Kind) || npc.Kind == TownNpcKind.TradeConsul;

    /// <summary>Work and walking pace for a hunger level: 70% while hungry, else full.</summary>
    public static float HungerFactor(float food) => food < HungryBelow ? 0.7f : 1f;

    /// <summary>Each step: food runs down; a starving resident is hurt (but never killed by hunger).</summary>
    private void UpdateHunger(TownNpc npc, float deltaSeconds)
    {
        if (!Eats(npc) || npc.IsBound)
            return;
        npc.Food = Math.Max(0f, npc.Food - FoodPerSecond * deltaSeconds);
        if (npc.Food <= 0f && npc.HitPoints > npc.MaximumHitPoints * 0.1f)
            npc.HitPoints = Math.Max(npc.MaximumHitPoints * 0.1f, npc.HitPoints - StarvingDamagePerSecond * deltaSeconds);
    }

    /// <summary>
    /// A resident below half food walks to the nearest chest near home with food in it and eats the most filling item there.
    /// False when not hungry enough, or there is no food in storage near home.
    /// </summary>
    private bool TryEatFromStorage(TownNpc npc, Vector2 center, float deltaSeconds)
    {
        if (!Eats(npc) || npc.Food >= EatBelow)
            return false;
        GridPoint? pantry = null;
        var nearest = float.MaxValue;
        foreach (var cell in StorageCellsNear(npc.HomePosition))
        {
            if (GetContainer(cell) is not { } chest || !chest.Slots.Any(stack => !stack.IsEmpty && FoodValue(stack.ItemId) > 0f))
                continue;
            var distance = Vector2.DistanceSquared(new Vector2(cell.X + 0.5f, cell.Y + 0.5f), center);
            if (distance < nearest)
            {
                nearest = distance;
                pantry = cell;
            }
        }
        if (pantry is not { } at)
            return false;
        if (!Reaches(center, at))
        {
            npc.Activity = "Going to eat";
            StepColonistToward(npc, center, new Vector2(at.X + 0.5f, at.Y + 0.5f), deltaSeconds, 1f);
            return true;
        }
        var chestInventory = GetContainer(at)!;
        var meal = chestInventory.Slots.Where(stack => !stack.IsEmpty && FoodValue(stack.ItemId) > 0f)
            .OrderByDescending(stack => FoodValue(stack.ItemId)).ThenBy(stack => stack.ItemId).First().ItemId;
        if (!chestInventory.TryConsume(meal, 1))
            return false;
        npc.Food = Math.Min(1f, npc.Food + FoodValue(meal));
        npc.Activity = $"Eating {Items[meal].Name}";
        Flags.Increment("colony.meals");
        return true;
    }
}
