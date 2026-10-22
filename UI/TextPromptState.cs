namespace Rimtera.UI;

public enum TextEditCommand : byte { Backspace, Delete, MoveLeft, MoveRight, Home, End }

/// <summary>
/// In-game modal text field. It is a normal retained UI state, so names are
/// entered on the game surface rather than in a native Windows text box.
/// </summary>
public sealed class TextPromptState : UiState
{
    private readonly UiTextInputElement _input;
    private bool _completed;

    public TextPromptState(
        string title,
        string description,
        string initial,
        string hint,
        int maximumLength = 32)
        : base("text-prompt", style: new UiStyle { Width = UiLength.Pct(1f), Height = UiLength.Pct(1f) })
    {
        var panel = new UiPanelElement("text-prompt-panel", new UiStyle
        {
            Width = UiLength.Pct(0.9f), Height = UiLength.Px(236f),
            MaxWidth = 640f, MinWidth = 420f,
            HAlign = 0.5f, VAlign = 0.5f,
            Padding = UiThickness.Uniform(18f),
            OverflowHidden = true
        });
        Add(panel);

        panel.Add(new UiTextElement(title, "text-prompt-title", new UiStyle
        {
            Width = UiLength.Pct(1f), Height = UiLength.Px(32f), Top = UiLength.Px(4f), HAlign = 0.5f
        }) { Color = UiColor.HeaderBlue });

        panel.Add(new UiTextElement(description, "text-prompt-description", new UiStyle
        {
            Width = UiLength.Pct(1f), Height = UiLength.Px(20f), Top = UiLength.Px(40f)
        }));

        _input = new UiTextInputElement(title, initial, "text-prompt-input", new UiStyle
        {
            Width = UiLength.Pct(1f), Height = UiLength.Px(44f), Top = UiLength.Px(68f),
            Padding = new UiThickness(12f, 9f, 12f, 9f)
        })
        {
            MaxLength = maximumLength,
            Placeholder = title
        };
        _input.OnClick(_ =>
        {
            _input.MoveCaretToEnd();
            Changed?.Invoke();
        });
        panel.Add(_input);

        panel.Add(new UiTextElement(hint, "text-prompt-hint", new UiStyle
        {
            Width = UiLength.Pct(1f), Height = UiLength.Px(20f), Top = UiLength.Px(118f)
        }) { Color = new UiColor(168, 178, 202) });

        var accept = new UiButtonElement("OK", Submit, "text-prompt-accept", new UiStyle
        {
            Left = UiLength.Mixed(-138f, 0.5f), Top = UiLength.Px(150f),
            Width = UiLength.Px(132f), Height = UiLength.Px(38f)
        });
        var cancel = new UiButtonElement("Cancel", Cancel, "text-prompt-cancel", new UiStyle
        {
            Left = UiLength.Mixed(6f, 0.5f), Top = UiLength.Px(150f),
            Width = UiLength.Px(132f), Height = UiLength.Px(38f)
        });
        panel.Add(accept);
        panel.Add(cancel);
    }

    public string Value => _input.Value;
    public UiTextInputElement Input => _input;

    public event Action<string>? Accepted;
    public event Action? Cancelled;
    public event Action? Changed;

    public void Insert(string text)
    {
        if (!_completed && _input.Insert(text))
            Changed?.Invoke();
    }

    public bool ApplyEdit(TextEditCommand command)
    {
        if (_completed)
            return false;

        var changed = command switch
        {
            TextEditCommand.Backspace => _input.Backspace(),
            TextEditCommand.Delete => _input.Delete(),
            TextEditCommand.MoveLeft => MoveCaret(-1),
            TextEditCommand.MoveRight => MoveCaret(1),
            TextEditCommand.Home => MoveCaretToStart(),
            TextEditCommand.End => MoveCaretToEnd(),
            _ => false
        };
        if (changed)
            Changed?.Invoke();
        return changed;
    }

    public void Submit()
    {
        if (_completed)
            return;
        _completed = true;
        Accepted?.Invoke(Value.Trim());
    }

    public void Cancel()
    {
        if (_completed)
            return;
        _completed = true;
        Cancelled?.Invoke();
    }

    public override void OnEnter() => _input.MoveCaretToEnd();

    private bool MoveCaret(int delta)
    {
        var before = _input.CaretIndex;
        _input.MoveCaret(delta);
        return before != _input.CaretIndex;
    }

    private bool MoveCaretToStart()
    {
        var before = _input.CaretIndex;
        _input.MoveCaretToStart();
        return before != _input.CaretIndex;
    }

    private bool MoveCaretToEnd()
    {
        var before = _input.CaretIndex;
        _input.MoveCaretToEnd();
        return before != _input.CaretIndex;
    }
}

/// <summary>
/// In-game key capture for the controls screen. The next key press is bound
/// without opening a native dialog or asking the player to type a key name.
/// </summary>
public sealed class BindingCaptureState : UiState
{
    private bool _completed;

    public BindingCaptureState(string action, string current)
        : base("binding-capture", style: new UiStyle { Width = UiLength.Pct(1f), Height = UiLength.Pct(1f) })
    {
        ActionName = action;
        var panel = new UiPanelElement("binding-capture-panel", new UiStyle
        {
            Width = UiLength.Pct(0.86f), Height = UiLength.Px(204f),
            MaxWidth = 540f, MinWidth = 380f,
            HAlign = 0.5f, VAlign = 0.5f,
            Padding = UiThickness.Uniform(18f),
            OverflowHidden = true
        });
        Add(panel);

        panel.Add(new UiTextElement($"Bind {action}", "binding-capture-title", new UiStyle
        {
            Width = UiLength.Pct(1f), Height = UiLength.Px(32f), Top = UiLength.Px(4f), HAlign = 0.5f
        }) { Color = UiColor.HeaderBlue });

        panel.Add(new UiTextElement($"Current: {current}", "binding-capture-current", new UiStyle
        {
            Width = UiLength.Pct(1f), Height = UiLength.Px(20f), Top = UiLength.Px(40f)
        }));

        panel.Add(new UiTextElement("Press a key to bind it. Escape cancels.", "binding-capture-hint", new UiStyle
        {
            Width = UiLength.Pct(1f), Height = UiLength.Px(20f), Top = UiLength.Px(68f)
        }) { Color = new UiColor(168, 178, 202) });

        panel.Add(new UiButtonElement("Cancel", Cancel, "binding-capture-cancel", new UiStyle
        {
            Left = UiLength.Mixed(-66f, 0.5f), Top = UiLength.Px(112f),
            Width = UiLength.Px(132f), Height = UiLength.Px(38f)
        }));
    }

    public string ActionName { get; }

    public event Action<ConsoleKey>? Captured;
    public event Action? Cancelled;

    public void Capture(ConsoleKey key)
    {
        if (_completed)
            return;
        _completed = true;
        Captured?.Invoke(key);
    }

    public void Cancel()
    {
        if (_completed)
            return;
        _completed = true;
        Cancelled?.Invoke();
    }
}
