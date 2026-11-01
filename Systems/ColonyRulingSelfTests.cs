using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rimtera.AI;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// 0.2.2 Stream E, the colony ruling (docs/roadmap/0.1.7.md, Stream E): the player is never auto-tended (NC-1), has no needs
/// upkeep (NC-2, D-48); unhappy residents stay (NC-9); the HUD status line is clean
/// (NC-16); residents' hit points are saved (NC-20).
/// </summary>
public static class ColonyRulingSelfTests
{
    private const int Floor = 40;
    /// <summary>The longest step the simulation takes (0.02 vital and, with the fast test clock, clock hours).</summary>
    private const float Step = 0.25f;
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private static GameSimulation Arena(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 120, height: 56) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y >= Floor ? TileId.Rock : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            game.World.SetCell(cell, data);
        }
        game.Player.Motion.Position = new Vector2(30f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        ClearEnemies(game);
        return game;
    }

    private static void ClearEnemies(GameSimulation game)
    {
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e7f);
    }

    /// <summary>Runs <paramref name="hours"/> vital hours in the longest steps, clearing creatures; returns every message posted.</summary>
    private static List<string> Run(GameSimulation game, float hours, Action? eachTick = null, PlayerAction first = PlayerAction.None)
    {
        var messages = new List<string>();
        var ticks = (int)MathF.Ceiling(hours / (Step * GameSimulation.VitalHoursPerRealSecond));
        for (var i = 0; i < ticks; i++)
        {
            eachTick?.Invoke();
            var last = game.LastMessage;
            game.Tick(new GameInput(new MovementInput(0f), i == 0 ? first : PlayerAction.None), Step);
            if (game.LastMessage != last && !string.IsNullOrEmpty(game.LastMessage))
                messages.Add(game.LastMessage);
            ClearEnemies(game);
        }
        return messages;
    }

    public static void Run(Action<bool, string> check)
    {
        NeverAutoTended(check);
        NoPlayerNeeds(check);
        UnhappyResidentsStay(check);
        HudStatusLineIsClean(check);
        ResidentHitPointsSaved(check);
    }

    // ---- E1 · NC-1: residents tend each other, never the player (0.3.10: the crew's medics are residents now) ----
    private static void NeverAutoTended(Action<bool, string> check)
    {
        var game = Arena(9701);
        var medic = game.AddTownNpc(TownNpcKind.Medic, game.Player.Motion.Position + new Vector2(1.2f, 0f), bound: false);
        medic.HomeCell = new GridPoint((int)game.Player.Motion.Position.X + 1, Floor - 1);
        var patient = game.AddStartingCrewForChecks()[0];
        patient.HomeCell = new GridPoint((int)game.Player.Motion.Position.X - 6, Floor - 1);
        var playerTorso = game.Player.Health.FindPart("torso")!;
        game.Player.Health.ApplyDamageToPart(playerTorso, 4f, DamageKind.Cut);
        var playerWounded = game.Player.Health.NeedsTending;
        var playerTendedEver = false;
        var tendingPlayer = false;
        var tendedPatient = false;
        Run(game, 48f, () =>
        {
            patient.HitPoints = Math.Min(patient.HitPoints, 60f); // kept hurt, so there is always someone to tend
            game.Player.Heal(1e6f); // keep the player standing; healing HP does not tend wounds
            playerTendedEver |= game.Player.Health.Injuries.Any(injury => injury.IsTended);
            tendingPlayer |= game.TownNpcs.Any(npc => npc.Activity == $"Tending {game.Player.Name}");
            tendedPatient |= medic.Activity == $"Tending {patient.Name}";
        });
        check(playerWounded && !playerTendedEver && !tendingPlayer && tendedPatient,
            $"NC-1: a wounded player beside a resident medic for 48 h is never tended, while a hurt settler near them is " +
            $"(player tended {playerTendedEver}, settler tended {tendedPatient})");
    }

    // ---- E2 · NC-2 (D-48): no needs upkeep for the player; food gives buffs ----
    private static void NoPlayerNeeds(Action<bool, string> check)
    {
        var game = Arena(9702);
        game.AddStartingCrewForChecks();
        var needs = game.Player.Needs;
        var before = (needs.FoodReserve, needs.RestReserve, needs.JoyReserve, needs.ComfortReserve);
        var messages = Run(game, 144f, () => game.Player.Heal(1e6f));
        var after = (needs.FoodReserve, needs.RestReserve, needs.JoyReserve, needs.ComfortReserve);
        ThoughtId[] needThoughts = [ThoughtId.Hungry, ThoughtId.Starving, ThoughtId.Tired, ThoughtId.Exhausted];
        var noNeedThoughts = !game.Player.Mind.CurrentThoughts.Any(view => needThoughts.Contains(view.Id));
        var baselineFull = MathF.Abs(game.Player.Mind.Mood.Baseline -
                                     ColonistMoodCalculator.Calculate(GameSimulation.PlayerNeedsBaseline(), game.Player.ColonistStats)) < 0.001f;
        var noEdge = !messages.Any(message => message.Contains("on the edge", StringComparison.OrdinalIgnoreCase));
        check(before == after && noNeedThoughts && baselineFull && game.Player.Mind.Breaker.Risk == MentalBreakSeverity.None &&
              game.Player.ActionCooldownScale == 1f && noEdge,
            $"NC-2: after 144 h without eating or sleeping the player's needs have not moved, no hunger or tiredness thought, mood " +
            $"is not moved by needs, no break risk, ActionCooldownScale 1 and no 'on the edge' message " +
            $"(needs {before} -> {after}, thoughts {noNeedThoughts}, baseline {baselineFull}, scale {game.Player.ActionCooldownScale})");

        // Food gives buffs only: cooked food applies its well-fed tier, a nano-banana a short Well Fed.
        var cooked = Arena(9703);
        cooked.Player.Inventory.Add(ItemId.RationBar, 1);
        cooked.TrySelectInventorySlot(cooked.Player.Inventory.FindSlot(ItemId.RationBar));
        Run(cooked, 0.05f, first: PlayerAction.Eat);
        var cookedFed = cooked.Player.Statuses.IsActive(StatusId.WellFed) && cooked.Player.Inventory.Count(ItemId.RationBar) == 0;
        var banana = Arena(9704);
        var bananas = banana.Player.Inventory.Count(ItemId.Berry);
        var empty = Enumerable.Range(0, banana.Player.Inventory.SlotCount).First(slot => banana.Player.Inventory.Slots[slot].IsEmpty);
        banana.TrySelectInventorySlot(empty); // nothing edible selected: Eat takes a nano-banana
        Run(banana, 0.05f, first: PlayerAction.Eat);
        var bananaFed = bananas > 0 && banana.Player.Inventory.Count(ItemId.Berry) == bananas - 1 &&
                        banana.Player.Statuses.IsActive(StatusId.WellFed);
        check(cookedFed && bananaFed,
            $"NC-2: eating cooked food applies Well Fed, and the starter kit's nano-bananas still work as food (Well Fed) " +
            $"(cooked {cookedFed}, banana {bananaFed}, bananas carried {bananas})");
    }

    // ---- E4 · NC-9: unhappy residents stay ----
    private static void UnhappyResidentsStay(Action<bool, string> check)
    {
        var game = Arena(9730);
        var at = new Vector2(50f, Floor - 2f);
        var resident = game.AddTownNpc(TownNpcKind.Quartermaster, at, bound: false);
        resident.HomeCell = new GridPoint(50, Floor - 1);
        for (var i = 0; i < 30; i++)
            game.AddTownNpc(TownNpcKind.Armorer, at + new Vector2(i * 0.3f - 3f, 0f), bound: false); // a crowd of neighbours it has no feeling for
        var happiness = game.GetHappiness(resident);
        for (var hour = 0; hour < 48; hour++)
            game.CheckTownArrivals();
        var present = game.TownNpcs.Contains(resident);
        var multiplier = game.GetPriceMultiplier(resident);
        check(happiness <= -5f && present && MathF.Abs(multiplier - 1.5f) < 0.001f,
            $"NC-9: a miserable resident (happiness {happiness:0.0}) is still home after 48 hourly checks, with prices ×{multiplier:0.00}");
    }

    // ---- E5 · NC-16: the HUD status line ----
    private static void HudStatusLineIsClean(Action<bool, string> check)
    {
        var game = new GameSimulation(seed: 9740, width: 80, height: 48) { FamilySpawnsEnabled = false };
        var line = HudStatusLine.Build(game);
        var expected = string.Join(HudStatusLine.Separator, $"Day {game.Day}", HudStatusLine.FormatClock(game.ClockHour),
            $"DEF {game.Player.Stats.Defense}", $"{game.Player.Inventory.Count(ItemId.Credits)} cr");
        var fresh = game.People.Count == 0 && line == expected && game.Day == 1;
        game.AddStartingCrewForChecks();
        var withCrew = HudStatusLine.Build(game);
        game.Player.Inventory.Add(ItemId.FerriteDroneUplink, 1);
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(ItemId.FerriteDroneUplink));
        var holdingRemote = HudStatusLine.Build(game);
        check(fresh && !line.Contains("Mood") && !line.Contains("People") && !line.Contains("Drones") && !line.Contains("Sentries") &&
              withCrew == expected + HudStatusLine.Separator + "People 2" && holdingRemote.Contains("Drones 0/") && !holdingRemote.Contains("Sentries"),
            $"NC-16: a fresh world's status line reads '{line}'; two settlers add 'People 2' ('{withCrew}'); a held drone remote shows drones ('{holdingRemote}')");
    }

    // ---- E6 · NC-20: residents' hit points are saved ----
    private static void ResidentHitPointsSaved(Action<bool, string> check)
    {
        var game = Arena(9750);
        var resident = game.AddTownNpc(TownNpcKind.Medic, new Vector2(50f, Floor - 2f), bound: false);
        game.HurtTownNpc(resident, resident.MaximumHitPoints - 40f + game.ResidentDefense(resident) / 2f); // through their defense
        var hurt = resident.HitPoints;
        var snapshot = game.CaptureSnapshot();
        var loaded = GameSimulation.Restore(JsonSerializer.Deserialize<GameSnapshot>(JsonSerializer.Serialize(snapshot, Json), Json)!);
        var restored = loaded.TownNpcs.FirstOrDefault(npc => npc.Kind == TownNpcKind.Medic);
        // A save from before the field: no Hp, so the resident loads at full health.
        var legacy = snapshot with
        {
            Content = snapshot.Content! with { TownNpcs = snapshot.Content!.TownNpcs!.Select(npc => npc with { Hp = null }).ToArray() }
        };
        var legacyLoaded = GameSimulation.Restore(legacy).TownNpcs.FirstOrDefault(npc => npc.Kind == TownNpcKind.Medic);
        check(MathF.Abs(hurt - 40f) < 0.01f && restored is not null && MathF.Abs(restored.HitPoints - 40f) < 0.01f &&
              legacyLoaded is not null && legacyLoaded.HitPoints == legacyLoaded.MaximumHitPoints,
            $"NC-20: a resident hurt to 40 HP saves and loads at 40 ({restored?.HitPoints:0.0}); an older save loads them at full health");
    }
}
