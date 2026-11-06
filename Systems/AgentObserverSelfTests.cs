namespace Rimtera.Systems;

/// <summary>The AI player's text view (docs/AGENT_PLAY.md): read-only, deterministic, and complete enough to act on.</summary>
public static class AgentObserverSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var game = new GameSimulation(seed: 4417, width: 160, height: 80) { FamilySpawnsEnabled = false };
        var first = AgentObserver.Describe(game);
        var second = AgentObserver.Describe(game);
        check(first == second, "the agent's text view is deterministic: two looks at the same tick match");
        check(first.Contains("legend:") && first.Contains('@') && first.Contains("inventory:") && first.Contains("map: column 0"),
            "the agent's text view has a header, the player, an inventory line, a legend and its map origin");
        var tick = game.TickNumber;
        AgentObserver.Describe(game, 40, 20);
        check(game.TickNumber == tick, "looking at the world does not advance it");

        var small = new GameSimulation(seed: 4418, width: 120, height: 56) { FamilySpawnsEnabled = false };
        var wide = AgentObserver.Describe(small, 60, 30);
        var rows = wide.Split('\n').SkipWhile(line => !line.StartsWith("legend:", StringComparison.Ordinal)).Skip(1)
            .Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToList();
        check(rows.Count <= 56 && rows.All(row => row.Length <= 120), "a view wider than the world is cut to the world's edge");
    }
}
