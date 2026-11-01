using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Food as the one need (0.5.5): food runs down; residents eat from storage near home, the most filling first; hunger slows; starving hurts but never kills; saved.</summary>
public static class HungerSelfTests
{
    private const int Floor = 40;

    public static void Run(Action<bool, string> check)
    {
        var game = new GameSimulation(seed: 9898, width: 120, height: 56) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        game.SetTimeOfDay(10f);
        var chest = new GridPoint(52, Floor - 1);
        game.World.SetTile(chest, TileId.StorageCrate);
        var cook = game.AddTownNpc(TownNpcKind.Cook, new Vector2(50f, Floor - 3f), bound: false);
        cook.Motion.Position = new Vector2(50f, Floor - cook.Motion.Size.Y - 0.01f);
        cook.HomeCell = new GridPoint(50, Floor - 2);
        foreach (var type in Enum.GetValues<WorkType>())
            cook.Work.Set(type, WorkCatalog.Off);
        var ai = game.AddTownNpc(TownNpcKind.StationAi, new Vector2(70f, Floor - 3f), bound: false);
        for (var i = 0; i < 600; i++)
            game.Tick(new GameInput(new MovementInput(0f)));
        var drained = cook.Food is > 0.98f and < 1f && ai.Food == 1f;

        // ---- below half they eat from the chest near home, the most filling first ----
        game.GetContainer(chest)!.Add(ItemId.Berry, 5);
        game.GetContainer(chest)!.Add(ItemId.ProteinRation, 1);
        cook.Food = 0.3f;
        var ate = false;
        for (var i = 0; i < 1200 && !ate; i++)
        {
            game.Tick(new GameInput(new MovementInput(0f)));
            ate = game.GetContainer(chest)!.Count(ItemId.ProteinRation) == 0;
        }
        check(drained && ate && cook.Food > 0.3f && game.GetContainer(chest)!.Count(ItemId.Berry) == 5 && game.Flags.Get("colony.meals") >= 1,
            $"food runs down (the station AI does not eat); below half a resident eats from the chest near home, the most filling first (food now {cook.Food:0.00})");

        // ---- hungry, slower; starving, hurt but never killed by hunger ----
        var worker = (IStationWorker)cook;
        cook.Food = 0.9f;
        var fed = worker.SpeedFor(WorkType.Cook);
        cook.Food = 0.1f;
        var hungry = worker.SpeedFor(WorkType.Cook);
        game.GetContainer(chest)!.TryConsume(ItemId.Berry, 5);
        cook.Food = 0f;
        for (var i = 0; i < 60 * 600; i++)
            game.Tick(new GameInput(new MovementInput(0f)));
        check(hungry < fed * 0.75f && cook.IsAlive && MathF.Abs(cook.HitPoints - cook.MaximumHitPoints * 0.1f) < 1f &&
              game.Inspect(new InspectTarget(InspectKind.Resident, cook.Id))!.Lines.Any(line => line.Contains("starving")),
            $"a hungry resident works slower ({fed:0.00} → {hungry:0.00}); a starving one loses life down to a tenth but hunger alone never kills ({cook.HitPoints:0}/{cook.MaximumHitPoints:0})");

        var reloaded = GameSimulation.Restore(game.CaptureSnapshot());
        check(reloaded.TownNpcs.Single(npc => npc.Kind == TownNpcKind.Cook).Food == 0f,
            "food is saved with each person");
    }
}
