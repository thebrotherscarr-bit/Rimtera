using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Outposts (0.7.1): founding one (people making a home far from the base) and abandoning one write a letter and go in the
/// chronicle; the History tab lists the outposts, where they lie, how many live there and whether they are guarded; a world
/// from before 0.7.1 starts counting from what stands, without letters.
/// </summary>
public static class OutpostLifeSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Flat(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 500, height: 64) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        game.SetTimeOfDay(10f);
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static TownNpc Living(GameSimulation game, TownNpcKind kind, int x)
    {
        var npc = game.AddTownNpc(kind, new Vector2(x, Floor - 3f), bound: false);
        npc.Motion.Position = new Vector2(x, Floor - npc.Motion.Size.Y - 0.01f);
        npc.HomeCell = new GridPoint(x, Floor - 2);
        return npc;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- founding an outpost writes a letter; the History tab lists it ----
        var game = Flat(9971);
        Living(game, TownNpcKind.StationAi, 60);
        game.CheckTownArrivals(); // the first check counts what stands
        var settler = Living(game, TownNpcKind.Settler, 300);
        game.CheckTownArrivals();
        var founded = game.Letters.LastOrDefault();
        var history = game.History().Select(line => line.Text).ToArray();
        check(founded is { Kind: LetterKind.Good, Title: "Outpost founded" } && founded.Body.Contains(settler.Name) &&
              founded.Body.Contains("east of the base") && founded.Body.Contains("Nobody there is armed") && game.OutpostsFounded == 1 &&
              history.Any(line => line.StartsWith("Outpost 1 · 240 tiles east of the base · 1 living there, unguarded")) &&
              game.Chronicle.Any(entry => entry.Text.StartsWith("An outpost was founded")),
            $"founding an outpost writes a letter ('{founded?.Body}') and the History tab lists it, unguarded until someone there is armed");

        // ---- abandoning one writes a letter too ----
        game.HurtTownNpc(settler, 1e6f);
        game.CheckTownArrivals();
        var abandoned = game.Letters.LastOrDefault();
        var after = game.History().Select(line => line.Text).ToArray();
        check(abandoned is { Kind: LetterKind.BadNews, Title: "Outpost abandoned" } && after.Contains("None standing now.") && game.OutpostsFounded == 1,
            "when an outpost's last resident is gone it is abandoned, with a letter; the History tab remembers it was founded");

        // ---- a world from before 0.7.1: what stands is counted, without letters ----
        var old = Flat(9972);
        Living(old, TownNpcKind.StationAi, 60);
        Living(old, TownNpcKind.Settler, 300);
        Living(old, TownNpcKind.Settler, 420);
        old.CheckTownArrivals();
        check(old.Letters.All(letter => letter.Title != "Outpost founded") && old.OutpostsFounded == 2 && old.Outposts.Count == 2,
            "a world from before 0.7.1 counts the outposts it already has without writing letters for them");
    }
}
