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
/// upkeep (NC-2, D-48), loses nothing to a mental break (NC-7); unhappy residents stay (NC-9); the HUD status line is clean
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

    private static readonly MethodInfo AddWanderer = typeof(GameSimulation)
        .GetMethod("AddWanderer", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>Adds crew members until one has the Medical skill; returns it (null when none turned up).</summary>
    private static ColonistAgent? AddMedic(GameSimulation game)
    {
        for (var index = 0; index < 24; index++)
        {
            var agent = (ColonistAgent)AddWanderer.Invoke(game, [index, game.Player.Motion.Position])!;
            if (agent.ColonistStats.Level(ColonistSkill.Medical) >= 1)
                return agent;
        }
        return null;
    }

    private static void Place(GameSimulation game, ColonistAgent agent, float x)
    {
        agent.Motion.Position = new Vector2(x, Floor - agent.Motion.Size.Y - 0.01f);
        agent.Motion.Velocity = Vector2.Zero;
        agent.HomePosition = agent.Motion.Position;
        agent.WanderTarget = agent.Motion.Position;
    }

    public static void Run(Action<bool, string> check)
    {
        NeverAutoTended(check);
        NoPlayerNeeds(check);
        BreaksNeverCostThePlayer(check);
        UnhappyResidentsStay(check);
        HudStatusLineIsClean(check);
        ResidentHitPointsSaved(check);
    }

    // ---- E1 · NC-1: crew tend each other, never the player ----
    private static void NeverAutoTended(Action<bool, string> check)
    {
        var game = Arena(9701);
        var medic = AddMedic(game);
        // The patient: a second crew member, not a medic, wounded beside the player.
        var patient = (ColonistAgent)AddWanderer.Invoke(game, [40, game.Player.Motion.Position])!;
        foreach (var agent in game.Colonists.Where(agent => !ReferenceEquals(agent, medic) && !ReferenceEquals(agent, patient)).ToArray())
            agent.TakeDamage(1e6f); // only the medic and the patient stay
        var playerTorso = game.Player.Health.FindPart("torso")!;
        game.Player.Health.ApplyDamageToPart(playerTorso, 4f, DamageKind.Cut);
        patient.Health.ApplyDamageToPart(patient.Health.FindPart("torso")!, 4f, DamageKind.Cut);
        var playerWounded = game.Player.Health.NeedsTending;
        var patientWounded = patient.Health.NeedsTending;
        var playerTendedEver = false;
        var tendingPlayer = false;
        var messages = Run(game, 48f, () =>
        {
            if (medic is not null)
                Place(game, medic, game.Player.Motion.Position.X + 1.2f);
            Place(game, patient, game.Player.Motion.Position.X - 1.2f);
            game.Player.Heal(1e6f); // keep the player standing; healing HP does not tend wounds
            playerTendedEver |= game.Player.Health.Injuries.Any(injury => injury.IsTended);
            tendingPlayer |= game.Colonists.Any(agent => agent.Activity == $"Tending {game.Player.Name}");
        });
        var patientTended = !patient.Health.NeedsTending || patient.Health.Injuries.Any(injury => injury.IsTended) ||
                            messages.Any(message => message.Contains($"tended {patient.Name}", StringComparison.Ordinal));
        check(medic is not null && playerWounded && patientWounded && !playerTendedEver && !tendingPlayer &&
              !messages.Any(message => message.Contains($"tended {game.Player.Name}", StringComparison.Ordinal)) && patientTended,
            $"NC-1: a wounded player beside a crew medic for 48 h is never tended, while a wounded crewmate beside them is " +
            $"(medic {medic is not null}, player tended {playerTendedEver}, crewmate tended {patientTended})");
    }

    // ---- E2 · NC-2 (D-48): no needs upkeep for the player; food gives buffs ----
    private static void NoPlayerNeeds(Action<bool, string> check)
    {
        var game = Arena(9702);
        game.AddStartingCrewForChecks();
        foreach (var agent in game.Colonists)
            Place(game, agent, 60f + agent.SocialKey * 3f);
        var needs = game.Player.Needs;
        var before = (needs.FoodReserve, needs.RestReserve, needs.JoyReserve, needs.ComfortReserve);
        var crewFoodBefore = game.Colonists[0].Needs.FoodReserve;
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
        check(game.Colonists.All(agent => !agent.IsAlive || agent.Needs.FoodReserve < crewFoodBefore),
            "NC-2: colonists keep their needs (their food still runs down)");

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

    // ---- E3 · NC-7: mental breaks never cost the player ----
    private static void BreaksNeverCostThePlayer(Action<bool, string> check)
    {
        var allKept = true;
        var detail = new List<string>();
        foreach (var kind in new[] { MentalBreakKind.Tantrum, MentalBreakKind.FoodBinge, MentalBreakKind.SadWander,
                     MentalBreakKind.HideInRoom, MentalBreakKind.Daze })
        {
            var game = Arena(9710 + (int)kind);
            game.AddStartingCrewForChecks();
            var agent = game.Colonists[0];
            foreach (var other in game.Colonists.Skip(1).ToArray())
                other.TakeDamage(1e6f);
            Place(game, agent, 34f);
            var stations = new[] { (new GridPoint(33, Floor - 1), TileId.Workbench), (new GridPoint(35, Floor - 1), TileId.Furnace),
                (new GridPoint(37, Floor - 1), TileId.Anvil) };
            foreach (var (cell, tile) in stations)
                game.World.SetTile(cell, tile);
            game.Player.Inventory.Add(ItemId.Berry, 20);
            var berries = game.Player.Inventory.Count(ItemId.Berry);
            agent.Needs.SetReserves(0.3f, agent.Needs.RestReserve, agent.Needs.JoyReserve, agent.Needs.ComfortReserve);
            agent.Mind.Breaker.Start(kind, MentalBreakSeverity.Extreme, 30f);
            var stillBreaking = true;
            Run(game, 24f, () =>
            {
                game.Player.Heal(1e6f);
                game.Player.Motion.Position = new Vector2(34.5f, Floor - game.Player.Motion.Size.Y - 0.01f);
                if (agent.IsAlive && !agent.Mind.Breaker.IsBreaking)
                    stillBreaking = false;
            });
            var tilesKept = stations.All(pair => game.World.GetCell(pair.Item1).TileId == pair.Item2 &&
                                                 game.World.GetCell(pair.Item1).Damage == 0);
            var berriesKept = game.Player.Inventory.Count(ItemId.Berry) == berries;
            allKept &= tilesKept && berriesKept && stillBreaking;
            detail.Add($"{kind}: tiles {tilesKept}, berries {berriesKept}, breaking {stillBreaking}");
        }
        check(allKept, $"NC-7: a crewmate in each kind of break for 24 h beside a Workbench, Furnace and Anvil leaves every tile " +
                       $"whole and the Berries in the player's pack untouched ({string.Join("; ", detail)})");

        // A binge forages instead: the binging crewmate still eats.
        var binge = Arena(9720);
        binge.AddStartingCrewForChecks();
        var eater = binge.Colonists[0];
        Place(binge, eater, 34f);
        eater.Needs.SetReserves(0.1f, 0.9f, 0.7f, 0.7f);
        eater.Mind.Breaker.Start(MentalBreakKind.FoodBinge, MentalBreakSeverity.Extreme, 30f);
        binge.Player.Inventory.Add(ItemId.Berry, 5);
        var packBefore = binge.Player.Inventory.Count(ItemId.Berry);
        Run(binge, 2f, () => binge.Player.Heal(1e6f));
        check(eater.Needs.FoodReserve > 0.1f && binge.Player.Inventory.Count(ItemId.Berry) == packBefore,
            $"NC-7: a food binge forages (food {eater.Needs.FoodReserve:0.00}) and never eats from the player's pack");
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
        var fresh = game.Colonists.Count == 0 && line == expected && game.Day == 1;
        AddWanderer.Invoke(game, [0, game.Player.Motion.Position]);
        var withCrew = HudStatusLine.Build(game);
        game.Player.Inventory.Add(ItemId.FerriteDroneUplink, 1);
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(ItemId.FerriteDroneUplink));
        var holdingRemote = HudStatusLine.Build(game);
        check(fresh && !line.Contains("Mood") && !line.Contains("Crew") && !line.Contains("Drones") && !line.Contains("Sentries") &&
              withCrew == expected + HudStatusLine.Separator + "Crew 1" && holdingRemote.Contains("Drones 0/") && !holdingRemote.Contains("Sentries"),
            $"NC-16: a fresh world's status line reads '{line}'; one crew member adds 'Crew 1' ('{withCrew}'); a held drone remote shows drones ('{holdingRemote}')");
    }

    // ---- E6 · NC-20: residents' hit points are saved ----
    private static void ResidentHitPointsSaved(Action<bool, string> check)
    {
        var game = Arena(9750);
        var resident = game.AddTownNpc(TownNpcKind.Medic, new Vector2(50f, Floor - 2f), bound: false);
        game.HurtTownNpc(resident, resident.MaximumHitPoints - 40f);
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
