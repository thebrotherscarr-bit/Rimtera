using System.Numerics;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>Pixels plus a fraction of the parent dimension, resolved during layout.</summary>
public readonly record struct UiLength(float Pixels, float Percent = 0f)
{
    public float Resolve(float parentSize) => Pixels + Percent * parentSize;
    public static UiLength Px(float pixels) => new(pixels);
    public static UiLength Pct(float fraction) => new(0f, fraction);
    public static UiLength Mixed(float pixels, float fraction) => new(pixels, fraction);
}

public readonly record struct UiThickness(float Left, float Top, float Right, float Bottom)
{
    public static UiThickness Uniform(float value) => new(value, value, value, value);
    public static UiThickness Zero => default;
}

public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public Vector2 Position => new(X, Y);
    public Vector2 Size => new(Width, Height);
    public bool Contains(Vector2 point) => point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;
    public UiRect Inset(UiThickness padding) => new(
        X + padding.Left,
        Y + padding.Top,
        Math.Max(0f, Width - padding.Left - padding.Right),
        Math.Max(0f, Height - padding.Top - padding.Bottom));
}

public sealed record UiStyle
{
    public UiLength Left { get; init; }
    public UiLength Top { get; init; }
    public UiLength Width { get; init; } = UiLength.Pct(1f);
    public UiLength Height { get; init; } = UiLength.Pct(1f);
    public float MinWidth { get; init; }
    public float MaxWidth { get; init; } = float.PositiveInfinity;
    public float MinHeight { get; init; }
    public float MaxHeight { get; init; } = float.PositiveInfinity;
    public float HAlign { get; init; }
    public float VAlign { get; init; }
    public UiThickness Padding { get; init; }
    public UiThickness Margin { get; init; }
    public bool OverflowHidden { get; init; }
}

public enum UiPointerButton : byte { Left, Right }

public readonly record struct UiInputFrame(
    Vector2 PointerPosition,
    bool LeftDown = false,
    bool RightDown = false,
    int ScrollDelta = 0,
    bool ConfirmPressed = false,
    bool BackPressed = false,
    int NavigationX = 0,
    int NavigationY = 0,
    TimeSpan? Elapsed = null);

public readonly record struct UiPointerEvent(UiElement Target, Vector2 Position, UiPointerButton Button, bool IsDoubleClick);

/// <summary>
/// Retained-mode element. Layout is recursive; hit testing walks children in reverse
/// draw order so the visually topmost interactive child receives the pointer.
/// </summary>
public class UiElement
{
    private readonly List<UiElement> _children = new();

    public UiElement(string? id = null, UiStyle? style = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? $"element-{Guid.NewGuid():N}" : id;
        Style = style ?? new UiStyle();
    }

    public string Id { get; }
    public UiElement? Parent { get; private set; }
    public IReadOnlyList<UiElement> Children => _children;
    public UiStyle Style { get; set; }
    public UiRect Bounds { get; private set; }
    public UiRect InnerBounds { get; private set; }
    public bool IsVisible { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public bool IgnoresMouseInteraction { get; set; }
    public bool PassThroughMouseInteraction { get; set; }
    public bool IsMouseHovering { get; internal set; }
    public bool IsFocused { get; internal set; }
    public virtual string? Text { get; set; }
    public string? SemanticRole { get; set; }

    public event Action<UiPointerEvent>? Clicked;
    public event Action<UiPointerEvent>? DoubleClicked;
    public event Action<UiElement>? MouseEntered;
    public event Action<UiElement>? MouseExited;
    public event Action<UiElement, int>? Scrolled;
    public event Action<UiElement>? Activated;

    public void Add(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (ReferenceEquals(child, this) || IsAncestorOf(child))
            throw new InvalidOperationException("Adding this element would create a UI cycle.");
        child.Parent?._children.Remove(child);
        child.Parent = this;
        _children.Add(child);
    }

    public bool Remove(UiElement child)
    {
        if (!_children.Remove(child))
            return false;
        child.Parent = null;
        return true;
    }

    public void ClearChildren()
    {
        foreach (var child in _children)
            child.Parent = null;
        _children.Clear();
    }

    public void OnClick(Action<UiPointerEvent> callback) => Clicked += callback;
    public void OnDoubleClick(Action<UiPointerEvent> callback) => DoubleClicked += callback;
    public void OnMouseEnter(Action<UiElement> callback) => MouseEntered += callback;
    public void OnMouseExit(Action<UiElement> callback) => MouseExited += callback;
    public void OnScroll(Action<UiElement, int> callback) => Scrolled += callback;
    public void OnActivate(Action<UiElement> callback) => Activated += callback;

    internal void RaiseClick(UiPointerEvent input) => Clicked?.Invoke(input);
    internal void RaiseDoubleClick(UiPointerEvent input) => DoubleClicked?.Invoke(input);
    internal void RaiseScroll(int delta) => Scrolled?.Invoke(this, delta);
    internal void RaiseActivate() => Activated?.Invoke(this);

    internal void SetHover(bool hovered)
    {
        if (IsMouseHovering == hovered)
            return;
        IsMouseHovering = hovered;
        if (hovered)
            MouseEntered?.Invoke(this);
        else
            MouseExited?.Invoke(this);
    }

    internal UiElement? HitTest(Vector2 position, UiRect clip)
    {
        if (!IsVisible || (Style.OverflowHidden && !Intersects(Bounds, clip)))
            return null;

        var childClip = Style.OverflowHidden ? Intersect(clip, InnerBounds) : clip;
        for (var i = _children.Count - 1; i >= 0; i--)
        {
            var hit = _children[i].HitTest(position, childClip);
            if (hit is not null && !hit.PassThroughMouseInteraction)
                return hit;
        }
        return !IgnoresMouseInteraction && Bounds.Contains(position) && clip.Contains(position) ? this : null;
    }

    internal void Layout(UiRect parentInner)
    {
        if (!IsVisible)
            return;

        var availableWidth = Math.Max(0f, parentInner.Width - Style.Margin.Left - Style.Margin.Right);
        var availableHeight = Math.Max(0f, parentInner.Height - Style.Margin.Top - Style.Margin.Bottom);
        var width = Math.Clamp(Style.Width.Resolve(parentInner.Width), Style.MinWidth, Style.MaxWidth);
        var height = Math.Clamp(Style.Height.Resolve(parentInner.Height), Style.MinHeight, Style.MaxHeight);
        var x = parentInner.X + (availableWidth - width) * ClampAlign(Style.HAlign) + Style.Left.Resolve(parentInner.Width) + Style.Margin.Left;
        var y = parentInner.Y + (availableHeight - height) * ClampAlign(Style.VAlign) + Style.Top.Resolve(parentInner.Height) + Style.Margin.Top;
        Bounds = new UiRect(x, y, Math.Max(0f, width), Math.Max(0f, height));
        InnerBounds = Bounds.Inset(Style.Padding);
        foreach (var child in _children)
            child.Layout(InnerBounds);
    }

    internal IEnumerable<UiElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in _children)
        foreach (var descendant in child.DescendantsAndSelf())
            yield return descendant;
    }

    private bool IsAncestorOf(UiElement candidate)
    {
        for (var current = Parent; current is not null; current = current.Parent)
            if (ReferenceEquals(current, candidate))
                return true;
        return false;
    }

    private static float ClampAlign(float value) => Math.Clamp(float.IsFinite(value) ? value : 0f, 0f, 1f);
    private static bool Intersects(UiRect a, UiRect b) => a.X < b.Right && a.Right > b.X && a.Y < b.Bottom && a.Bottom > b.Y;
    private static UiRect Intersect(UiRect a, UiRect b)
    {
        var x = Math.Max(a.X, b.X);
        var y = Math.Max(a.Y, b.Y);
        return new UiRect(x, y, Math.Max(0f, Math.Min(a.Right, b.Right) - x), Math.Max(0f, Math.Min(a.Bottom, b.Bottom) - y));
    }
}

public class UiState : UiElement
{
    public UiState(string id, bool noGamepadSupport = false, UiStyle? style = null) : base(id, style) => NoGamepadSupport = noGamepadSupport;
    public bool NoGamepadSupport { get; }
    public virtual void OnEnter() { }
    public virtual void OnExit() { }
}

/// <summary>State owner, history, focus navigation, pointer dispatch, and 500 ms double click.</summary>
public sealed class UserInterface
{
    private readonly Stack<UiState> _history = new();
    private readonly Dictionary<UiState, UiElement?> _focusByState = new();
    private UiElement? _hovered;
    private UiElement? _leftDownTarget;
    private UiElement? _rightDownTarget;
    private UiElement? _lastClickTarget;
    private TimeSpan _lastClickAt;
    private TimeSpan _elapsed;
    private bool _leftWasDown;
    private bool _rightWasDown;

    public UiState? CurrentState { get; private set; }
    public UiRect Viewport { get; private set; }
    public bool IsVisible { get; set; } = true;
    public Vector2 MousePosition { get; private set; }
    public int HistoryCount => _history.Count;
    public UiElement? FocusedElement { get; private set; }

    public void SetViewport(float width, float height)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width < 0 || height < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        Viewport = new UiRect(0, 0, width, height);
        CurrentState?.Layout(Viewport);
    }

    /// <summary>True when <paramref name="point"/> is over one of the current screen's elements rather than its empty backdrop.</summary>
    public bool IsOverInterface(Vector2 point)
    {
        if (!IsVisible || CurrentState is null)
            return false;
        CurrentState.Layout(Viewport);
        var hit = CurrentState.HitTest(point, Viewport);
        return hit is not null && !ReferenceEquals(hit, CurrentState);
    }

    public void SetState(UiState? next, bool saveCurrent = true)
    {
        if (ReferenceEquals(CurrentState, next))
            return;
        if (CurrentState is { } current)
        {
            _hovered?.SetHover(false);
            current.OnExit();
            _focusByState[current] = FocusedElement;
            if (saveCurrent)
                _history.Push(current);
        }
        CurrentState = next;
        _hovered = _leftDownTarget = _rightDownTarget = null;
        FocusedElement = null;
        if (next is not null)
        {
            next.OnEnter();
            next.Layout(Viewport);
            FocusedElement = _focusByState.GetValueOrDefault(next) ?? FindFocusable(next);
            UpdateFocusFlags();
        }
    }

    public bool GoBack()
    {
        if (_history.Count == 0)
            return false;
        var previous = _history.Pop();
        SetState(previous, saveCurrent: false);
        return true;
    }

    public void ClearHistory() => _history.Clear();

    public void Focus(UiElement? element)
    {
        if (element is not null && (CurrentState is null || !CurrentState.DescendantsAndSelf().Contains(element) || !IsFocusable(element)))
            return;
        FocusedElement = element;
        if (CurrentState is not null)
            _focusByState[CurrentState] = element;
        UpdateFocusFlags();
    }

    public void Update(UiInputFrame input)
    {
        if (!IsVisible || CurrentState is null)
            return;

        _elapsed += input.Elapsed ?? TimeSpan.FromSeconds(1d / 60d);
        MousePosition = input.PointerPosition;
        CurrentState.Layout(Viewport);
        var hit = CurrentState.HitTest(input.PointerPosition, Viewport);
        if (!ReferenceEquals(hit, _hovered))
        {
            _hovered?.SetHover(false);
            _hovered = hit;
            _hovered?.SetHover(true);
        }

        if (input.LeftDown && !_leftWasDown)
            _leftDownTarget = hit;
        else if (!input.LeftDown && _leftWasDown)
        {
            if (_leftDownTarget is { IsEnabled: true } down && ReferenceEquals(down, hit))
            {
                var doubleClick = ReferenceEquals(_lastClickTarget, down) && _elapsed - _lastClickAt < TimeSpan.FromMilliseconds(500);
                var pointerEvent = new UiPointerEvent(down, input.PointerPosition, UiPointerButton.Left, doubleClick);
                down.RaiseClick(pointerEvent);
                if (doubleClick)
                    down.RaiseDoubleClick(pointerEvent);
                else
                {
                    _lastClickTarget = down;
                    _lastClickAt = _elapsed;
                }
                Focus(down);
            }
            _leftDownTarget = null;
        }

        if (input.RightDown && !_rightWasDown)
            _rightDownTarget = hit;
        else if (!input.RightDown && _rightWasDown)
        {
            if (_rightDownTarget is { IsEnabled: true } right && ReferenceEquals(right, hit))
                right.RaiseClick(new UiPointerEvent(right, input.PointerPosition, UiPointerButton.Right, false));
            _rightDownTarget = null;
        }

        if (input.ScrollDelta != 0 && hit is not null)
        {
            for (var current = hit; current is not null; current = current.Parent)
                current.RaiseScroll(input.ScrollDelta);
        }
        if (input.BackPressed && GoBack())
            return;
        if (input.NavigationX != 0 || input.NavigationY != 0)
            MoveFocus(input.NavigationX, input.NavigationY);
        if (input.ConfirmPressed && FocusedElement is { IsEnabled: true } focused)
            focused.RaiseActivate();

        _leftWasDown = input.LeftDown;
        _rightWasDown = input.RightDown;
    }

    public IReadOnlyList<UiDrawCommand> BuildDrawCommands()
    {
        if (!IsVisible || CurrentState is null)
            return Array.Empty<UiDrawCommand>();
        var result = new List<UiDrawCommand>();
        AppendDrawCommands(CurrentState, result);
        return result;
    }

    private void MoveFocus(int x, int y)
    {
        if (CurrentState is null || CurrentState.NoGamepadSupport)
            return;
        var focusable = CurrentState.DescendantsAndSelf().Where(IsFocusable).ToArray();
        if (focusable.Length == 0)
            return;
        var currentIndex = Array.IndexOf(focusable, FocusedElement);
        var step = x < 0 || y < 0 ? -1 : 1;
        Focus(focusable[(currentIndex + step + focusable.Length) % focusable.Length]);
    }

    private void UpdateFocusFlags()
    {
        if (CurrentState is null)
            return;
        foreach (var element in CurrentState.DescendantsAndSelf())
            element.IsFocused = ReferenceEquals(element, FocusedElement);
    }

    private static UiElement? FindFocusable(UiElement root) => root.DescendantsAndSelf().FirstOrDefault(IsFocusable);
    private static bool IsFocusable(UiElement element) => element.IsVisible && element.IsEnabled && element.SemanticRole is "button" or "slot" or "input" or "toggle";

    private static void AppendDrawCommands(UiElement element, List<UiDrawCommand> output)
    {
        if (!element.IsVisible)
            return;
        if (element is UiPanelElement panel)
            output.Add(new UiDrawCommand(UiDrawKind.Panel, panel.Bounds, panel.Text ?? string.Empty, panel.Color, panel.IsFocused, panel.IsMouseHovering));
        else if (element is UiTextElement text)
            output.Add(new UiDrawCommand(UiDrawKind.Text, text.Bounds, text.Text ?? string.Empty, text.Color, text.IsFocused, text.IsMouseHovering));
        else if (element is UiButtonElement button)
            output.Add(new UiDrawCommand(UiDrawKind.Button, button.Bounds, button.Text ?? string.Empty, button.Color, button.IsFocused, button.IsMouseHovering));
        else if (element is UiTextInputElement input)
            output.Add(new UiDrawCommand(UiDrawKind.Input, input.Bounds, input.GetDisplayText(), input.Color, input.IsFocused, input.IsMouseHovering, input.CaretIndex));
        else if (element is UiEquipmentSlotElement equipment)
            output.Add(new UiDrawCommand(UiDrawKind.ItemSlot, equipment.Bounds, equipment.Label, equipment.Color, equipment.IsFocused, equipment.IsMouseHovering,
                Stack: equipment.Item is { } worn ? new ItemStack(worn, 1) : null));
        else if (element is UiItemSlotElement slot)
            output.Add(new UiDrawCommand(UiDrawKind.ItemSlot, slot.Bounds, slot.GetDisplayText(), slot.Color, slot.IsFocused, slot.IsMouseHovering,
                Stack: (uint)slot.SlotIndex < (uint)slot.Inventory.Slots.Count ? slot.Inventory.Slots[slot.SlotIndex] : null,
                Selected: slot.ShowSelection && slot.SlotIndex == slot.Inventory.SelectedSlot, Badge: slot.Badge));
        else if (element is UiStackSlotElement single)
            output.Add(new UiDrawCommand(UiDrawKind.ItemSlot, single.Bounds, single.Label, single.Color, single.IsFocused, single.IsMouseHovering,
                Stack: single.Stack is { IsEmpty: false } held ? held : null));
        foreach (var child in element.Children)
            AppendDrawCommands(child, output);
    }
}

public enum UiDrawKind : byte { Panel, Text, Button, ItemSlot, Input }
public readonly record struct UiColor(byte R, byte G, byte B, byte A = 255)
{
    public static UiColor PanelBlue => new(33, 43, 79);
    public static UiColor HeaderBlue => new(73, 94, 171);
    public static UiColor ButtonDefault => new(56, 68, 105);
    public static UiColor ButtonHover => new(90, 112, 177);
}
public readonly record struct UiDrawCommand(
    UiDrawKind Kind,
    UiRect Bounds,
    string Text,
    UiColor Color,
    bool Focused,
    bool Hovered,
    int CaretIndex = -1,
    ItemStack? Stack = null,
    bool Selected = false,
    string? Badge = null);

public sealed class UiPanelElement(string? id = null, UiStyle? style = null) : UiElement(id, style)
{
    public UiColor Color { get; set; } = UiColor.PanelBlue;
}

public sealed class UiTextElement(string text, string? id = null, UiStyle? style = null) : UiElement(id, style)
{
    public UiColor Color { get; set; } = new(238, 240, 248);
    public float Scale { get; set; } = 1f;
    public override string? Text { get; set; } = text;
}

public sealed class UiButtonElement : UiElement
{
    public UiColor Color { get; set; } = UiColor.ButtonDefault;
    public float TextScale { get; set; } = 0.9f;
    public override string? Text { get; set; }
    public string? GamepadSnapLabel { get; set; }
    public Action? Command { get; set; }

    public UiButtonElement(string text, string? id = null, UiStyle? style = null) : base(id, style)
    {
        SemanticRole = "button";
        Text = text;
    }

    public UiButtonElement(string text, Action command, string? id = null, UiStyle? style = null) : this(text, id, style)
    {
        SemanticRole = "button";
        Command = command;
        OnClick(_ => Command?.Invoke());
        OnActivate(_ => Command?.Invoke());
    }
}

/// <summary>One armor, accessory or vanity slot of the player's equipment; empty slots show their label.</summary>
public sealed class UiEquipmentSlotElement : UiElement
{
    private readonly EquipmentLoadout _loadout;

    public UiEquipmentSlotElement(string? id, EquipmentLoadout loadout, EquipmentSlot slot, bool vanity, string label, UiStyle? style = null)
        : base(id, style)
    {
        SemanticRole = "slot";
        _loadout = loadout ?? throw new ArgumentNullException(nameof(loadout));
        Slot = slot;
        Vanity = vanity;
        Label = label;
    }

    public EquipmentSlot Slot { get; }
    public bool Vanity { get; }
    public string Label { get; }
    public UiColor Color { get; set; } = new(43, 48, 64);
    public ItemId? Item => Vanity ? _loadout[(VanitySlot)(byte)Slot] : _loadout[Slot];
}

/// <summary>A lone slot showing a stack that lives outside any inventory (the trash); empty, it shows its label.</summary>
public sealed class UiStackSlotElement : UiElement
{
    private readonly Func<ItemStack> _stack;

    public UiStackSlotElement(string? id, string label, Func<ItemStack> stack, UiStyle? style = null) : base(id, style)
    {
        SemanticRole = "slot";
        Label = label;
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
    }

    public string Label { get; }
    public ItemStack Stack => _stack();
    public UiColor Color { get; set; } = new(43, 48, 64);
}

public sealed class UiItemSlotElement : UiElement
{
    public Inventory Inventory { get; }
    public int SlotIndex { get; }
    public UiColor Color { get; set; } = new(43, 48, 64);
    /// <summary>A small corner label, such as the hotbar number.</summary>
    public string? Badge { get; set; }
    /// <summary>Whether the inventory's selected slot is highlighted (off for chests, which have no selection).</summary>
    public bool ShowSelection { get; set; } = true;

    public UiItemSlotElement(string? id, Inventory inventory, int slotIndex, UiStyle? style = null) : base(id, style)
    {
        SemanticRole = "slot";
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        SlotIndex = slotIndex;
    }

    public string GetDisplayText()
    {
        if ((uint)SlotIndex >= (uint)Inventory.Slots.Count)
            return "Invalid slot";
        var stack = Inventory.Slots[SlotIndex];
        var marker = SlotIndex == Inventory.SelectedSlot ? ">" : " ";
        return stack.IsEmpty ? $"{marker}[{SlotIndex + 1}]" : $"{marker}[{SlotIndex + 1}] {stack.ItemId} x{stack.Count}";
    }
}

public interface IUiRenderer
{
    void Draw(IReadOnlyList<UiDrawCommand> commands);
}

public sealed class DebugUiRenderer : IUiRenderer
{
    public IReadOnlyList<UiDrawCommand> LastCommands { get; private set; } = Array.Empty<UiDrawCommand>();
    public void Draw(IReadOnlyList<UiDrawCommand> commands) => LastCommands = commands.ToArray();
}
