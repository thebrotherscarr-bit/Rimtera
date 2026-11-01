using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Prisoners and conversion (0.5.6): human raiders surrender into a free cell; the best talker recruits them; settlers and prisoners can be sent away; saved.</summary>
public static class PrisonerSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Flat(int seed, bool house)
    {
        var game = new GameSimulation(seed: seed, width: 160, height: 64) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        game.SetTimeOfDay(10f);
        game.Player.Motion.Position = new Vector2(20f, Floor - game.Player.Motion.Size.Y - 0.01f);
        if (house)
        {
            const int x0 = 60;
            var bottom = Floor - 1;
            var top = bottom - 9;
            for (var y = top; y <= bottom; y++)
            for (var x = x0; x <= x0 + 13; x++)
            {
                var shell = y == top || y == bottom || x == x0 || x == x0 + 13;
                game.World.SetTile(new GridPoint(x, y), shell ? TileId.Plate : TileId.Air);
                if (!shell)
                    game.World.SetWall(new GridPoint(x, y), WallIds.PlatingGrey);
            }
            for (var y = bottom - 3; y <= bottom - 1; y++)
                game.World.SetTile(new GridPoint(x0, y), TileId.PlankDoor);
            game.World.SetTile(new GridPoint(x0 + 3, bottom - 1), TileId.WallTorch);
            game.World.SetTile(new GridPoint(x0 + 5, bottom - 1), TileId.PlankTable);
            game.World.SetTile(new GridPoint(x0 + 6, bottom - 1), TileId.PlankChair);
            game.Regions.ForceFullRebuild();
        }
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static EnemyAgent BeatenRaider(GameSimulation game)
    {
        var raider = game.SpawnCreatureAt(EnemyKind.EventGrunt, new Vector2(30f, Floor - 2f));
        raider.TakeDamage(raider.MaximumHitPoints * 0.9f);
        game.Tick(new GameInput(new MovementInput(0f)));
        return raider;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- a beaten human raider surrenders into a free cell; with none, gets away ----
        var game = Flat(9899, house: true);
        var raider = BeatenRaider(game);
        var prisoner = game.Prisoners.SingleOrDefault();
        var cold = Flat(9900, house: false);
        var runaway = BeatenRaider(cold);
        var beast = game.SpawnCreatureAt(EnemyKind.MossSlime, new Vector2(34f, Floor - 1.2f));
        beast.TakeDamage(beast.MaximumHitPoints * 0.9f);
        game.Tick(new GameInput(new MovementInput(0f)));
        check(!game.Enemies.Contains(raider) && prisoner is { IsPrisoner: true, HomeCell: not null, Adulthood.Id: "wasteland-raider" } &&
              !game.People.Contains(prisoner) && !game.Settlers.Contains(prisoner) && prisoner.Resistance > 0f &&
              !cold.Enemies.Contains(runaway) && cold.Prisoners.Count == 0 && cold.LastMessage.Contains("get away") &&
              game.Enemies.Contains(beast) && game.Inspect(new InspectTarget(InspectKind.Resident, prisoner.Id))!.Lines.Any(line => line.StartsWith("Prisoner")),
            $"a raider brought to {GameSimulation.SurrenderShare:P0} life surrenders and is held in a free room ({prisoner?.Name}, resistance {prisoner?.Resistance:0}); " +
            "with no room free they get away; creatures never surrender");

        // ---- the best talker recruits them, hour by hour ----
        var talker = game.AddTownNpc(TownNpcKind.TradeConsul, new Vector2(40f, Floor - 3f), bound: false);
        talker.HomeCell = new GridPoint(40, Floor - 2);
        var resistance = prisoner!.Resistance;
        var saved = GameSimulation.Restore(game.CaptureSnapshot()).Prisoners.SingleOrDefault();
        var hours = 0;
        for (; hours < 30 && prisoner!.IsPrisoner; hours++)
            game.CheckTownArrivals();
        check(saved is { IsPrisoner: true } && MathF.Abs(saved.Resistance - resistance) < 0.001f && !prisoner.IsPrisoner &&
              game.Settlers.Contains(prisoner) && game.Recruiter == talker && hours <= 10,
            $"the colony's best talker ({talker.Name}) wears a prisoner's resistance down hour by hour; they join as a settler after {hours} hours; prisoners are saved");

        // ---- settlers and prisoners can be sent away; trades cannot ----
        var leaving = game.SendAway(prisoner!);
        var stays = game.SendAway(talker);
        check(leaving.Contains("sent away") && !game.TownNpcs.Contains(prisoner) && stays.Contains("keeps their trade") && game.TownNpcs.Contains(talker),
            $"a settler can be sent away; a trade keeps their place ('{stays}')");
    }
}
