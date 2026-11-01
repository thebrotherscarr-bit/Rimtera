using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// One player's command for one step (0.3.11, multiplayer groundwork; docs/MULTIPLAYER.md): which player slot sent it, the
/// step it was for, the input, and the step length. Commands are the only way players change the game, so a session is its
/// seed plus its commands: replaying them rebuilds the same world (the base for lockstep networking in 1.2).
/// </summary>
public readonly record struct PlayerCommand(long Tick, int Slot, GameInput Input, float DeltaSeconds);

/// <summary>An append-only record of the commands a session ran, in order (for replays, desync checks and, later, the network).</summary>
public sealed class CommandLog
{
    private readonly List<PlayerCommand> _commands = new();

    public IReadOnlyList<PlayerCommand> Commands => _commands;

    public void Add(PlayerCommand command) => _commands.Add(command);

    public PlayerCommand[] Capture() => _commands.ToArray();

    public static CommandLog From(IEnumerable<PlayerCommand> commands)
    {
        var log = new CommandLog();
        foreach (var command in commands)
            log.Add(command);
        return log;
    }
}

public sealed partial class GameSimulation
{
    /// <summary>Local split-screen co-op takes up to four players (1.1); networked play builds on the same slots (1.2).</summary>
    public const int MaxLocalPlayers = 4;

    /// <summary>The player slots this world runs. One until co-op arrives in 1.1; the command path already takes a slot.</summary>
    public int PlayerSlots => 1;

    /// <summary>The players, by slot (slot 0 is <see cref="Player"/>).</summary>
    public IReadOnlyList<PlayerCharacter> Players => [Player];

    private readonly GameInput?[] _pendingCommands = new GameInput?[MaxLocalPlayers];

    /// <summary>When set, every command the world runs is appended here (a replay, a desync check, the network later).</summary>
    public CommandLog? Recorder { get; set; }

    /// <summary>A slot's input standing still (what a player who sent nothing this step does).</summary>
    public static GameInput IdleInput => new(new MovementInput(0f));

    /// <summary>
    /// Queues a player's command for the next step. The game state changes only in <see cref="Advance"/>, never here.
    /// False for a slot this world does not run (any slot but 0 until co-op).
    /// </summary>
    public bool Submit(int slot, in GameInput input)
    {
        if (slot < 0 || slot >= PlayerSlots)
            return false;
        _pendingCommands[slot] = input;
        return true;
    }

    /// <summary>Advances the world one fixed step with each slot's queued command (idle when none was sent), then clears the queue.</summary>
    public GameTickResult Advance(float deltaSeconds = 1f / 60f)
    {
        var input = _pendingCommands[0] ?? IdleInput;
        Array.Clear(_pendingCommands);
        Recorder?.Add(new PlayerCommand(TickNumber, 0, input, deltaSeconds));
        return Step(input, deltaSeconds);
    }

    /// <summary>Runs a recorded session's commands on a world, in order (the world should start where the recording did).</summary>
    public void Replay(IEnumerable<PlayerCommand> commands)
    {
        foreach (var command in commands)
        {
            Submit(command.Slot, command.Input);
            Advance(command.DeltaSeconds);
        }
    }
}
