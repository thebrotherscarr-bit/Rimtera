using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The landing site is safe in full-size worlds: no sky island hangs over it, and the player starts standing on the real
/// ground even when a plant grows on the spawn column (seeds 3 and 113257 once spawned in a cave or under an island).
/// </summary>
public static class SpawnSafetySelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var details = new List<string>();
        var safe = true;
        foreach (var seed in new[] { 3, 113257 })
        {
            var game = new GameSimulation(seed, 1750, 900);
            var x = game.SpawnColumn;
            var top = 1;
            while (top < game.World.Height - 1 && !game.Tiles[game.World.GetCell(new GridPoint(x, top)).TileId].IsSolid)
                top++;
            var feet = game.Player.Motion.Position.Y + game.Player.Motion.Size.Y;
            var islandOverhead = game.Features?.Islands.Any(island => x >= island.Left - 3 && x <= island.Right + 3) ?? false;
            var onGround = Math.Abs(feet - top) < 1.5f;
            safe &= onGround && !islandOverhead;
            details.Add($"seed {seed}: feet {feet:0.0}, ground {top}, island {islandOverhead}");
        }
        check(safe, $"the player lands on the open ground with clear sky above ({string.Join("; ", details)})");
    }
}
