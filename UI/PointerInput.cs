namespace Rimtera.UI;

/// <summary>Who a mouse press belongs to: the world (use or interact) or an interface (the hub, the HUD, the map).</summary>
public enum PointerOwner : byte { None, World, Interface }

/// <summary>
/// The one mouse-button state the game window keeps, built only from its own MouseDown and MouseUp events and cleared
/// when the window loses focus or mouse capture. Terraria reads the mouse once per frame and feeds the UI and the world
/// from that single reading (<c>Main.mouseLeft</c>); this is Rimtera's equivalent. Before it, the UI read the live
/// hardware state (<c>Control.MouseButtons</c>) while the world read the events, so posted clicks never pressed a
/// button and a lost release could leave the world "using" forever.
/// </summary>
public sealed class MouseButtonState
{
    public bool LeftDown { get; private set; }
    public bool RightDown { get; private set; }
    public PointerOwner LeftOwner { get; private set; }
    public PointerOwner RightOwner { get; private set; }
    public bool AnyDown => LeftDown || RightDown;

    public void Press(UiPointerButton button, PointerOwner owner)
    {
        if (button == UiPointerButton.Left)
        {
            LeftDown = true;
            LeftOwner = owner;
        }
        else
        {
            RightDown = true;
            RightOwner = owner;
        }
    }

    public void Release(UiPointerButton button)
    {
        if (button == UiPointerButton.Left)
        {
            LeftDown = false;
            LeftOwner = PointerOwner.None;
        }
        else
        {
            RightDown = false;
            RightOwner = PointerOwner.None;
        }
    }

    /// <summary>Focus or capture lost: every button counts as released, so nothing stays held.</summary>
    public void Clear()
    {
        Release(UiPointerButton.Left);
        Release(UiPointerButton.Right);
    }

    /// <summary>
    /// Terraria's <c>controlUseItem = MouseLeft &amp;&amp; !mouseInterface</c>: the left button went down on the world and the
    /// pointer is not over an interface now (dragging onto a panel pauses the use; back over the world it resumes).
    /// </summary>
    public bool WorldUseHeld(bool overInterface) => LeftDown && LeftOwner == PointerOwner.World && !overInterface;

    /// <summary>The left button as the interface sees it: only presses that began on an interface.</summary>
    public bool InterfaceLeftDown => LeftDown && LeftOwner == PointerOwner.Interface;
    public bool InterfaceRightDown => RightDown && RightOwner == PointerOwner.Interface;
}

/// <summary>
/// Terraria's held use button: an item with auto-reuse keeps being used while the button is held; one without it is
/// used once per press (<c>releaseUseItem</c> must be seen between uses).
/// </summary>
public sealed class UseButtonGate
{
    private bool _wasHeld;

    /// <summary>Called once per tick with the button state; true when this tick should use the item.</summary>
    public bool Update(bool held, bool autoReuse)
    {
        var fresh = held && !_wasHeld;
        _wasHeld = held;
        return held && (fresh || autoReuse);
    }

    public void Reset() => _wasHeld = false;
}

/// <summary>
/// Terraria's right-click stack split (<c>ItemSlot.RightClick</c>, <c>Main.stackSplit</c>, <c>stackDelay</c>,
/// <c>superFastStack</c>): the press takes one item; holding takes the next after 30 ticks, then every
/// <c>stackDelay</c> ticks. The delay starts at 7 and drops by one after 30, 25, 20, 15 and 10 held ticks (then every
/// 5) down to 2, after which each step takes one more item at a time.
/// </summary>
public sealed class StackSplitRepeater
{
    public const int FirstRepeatTicks = 30;
    private bool _held;
    private int _cooldown;
    private int _delay = 7;
    private int _counter;
    private int _superFast;

    /// <summary>Called once per tick; returns how many items to take this tick (0 for none).</summary>
    public int Tick(bool held)
    {
        if (!held)
        {
            _held = false;
            _cooldown = 0;
            _delay = 7;
            _counter = 0;
            _superFast = 0;
            return 0;
        }
        if (!_held)
        {
            _held = true;
            _cooldown = FirstRepeatTicks;
            return 1;
        }
        _counter++;
        var threshold = _delay switch { 7 => 30, 6 => 25, 5 => 20, 4 => 15, 3 => 10, _ => 5 };
        if (_counter >= threshold)
        {
            _counter = 0;
            _delay--;
            if (_delay < 2)
            {
                _delay = 2;
                _superFast++;
            }
        }
        if (--_cooldown > 0)
            return 0;
        _cooldown = _delay;
        return 1 + _superFast;
    }
}
