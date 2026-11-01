using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>The dev console's typing state in the window (0.3.6): the line being typed and the walk through history.</summary>
public sealed class DevConsoleView(GameSimulation game)
{
    public GameSimulation Game { get; } = game;
    public DevConsole Console { get; } = new(game);
    public bool IsOpen { get; set; }
    public string Input { get; private set; } = "";
    private int _historyIndex = -1;

    public void Type(char c)
    {
        if (Input.Length < 200)
            Input += c;
    }

    public void Backspace()
    {
        if (Input.Length > 0)
            Input = Input[..^1];
    }

    public void Submit()
    {
        Console.Execute(Input);
        Input = "";
        _historyIndex = -1;
    }

    /// <summary>Up (-1) walks back through the commands entered, down (+1) forward to an empty line.</summary>
    public void HistoryStep(int direction)
    {
        var history = Console.History;
        if (history.Count == 0)
            return;
        _historyIndex = _historyIndex < 0 ? (direction < 0 ? history.Count - 1 : -1) : _historyIndex + direction;
        if (_historyIndex >= history.Count)
            _historyIndex = -1;
        _historyIndex = Math.Max(-1, _historyIndex);
        Input = _historyIndex < 0 ? "" : history[_historyIndex];
    }
}
