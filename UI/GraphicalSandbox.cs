using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Windows.Forms;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>
/// Windowed host for the retained menu flow and the fixed-step simulation.
/// The start menu is intentionally persistent: closing it only hides the UI,
/// it does not discard the menu object, its screen history, or its selection.
/// </summary>
public static partial class GraphicalSandbox
{
    /// <param name="quickstart">Skip the title menu and drop into a fresh world (development aid).</param>
    /// <param name="quickstartSize">World size for <paramref name="quickstart"/>; 800x300 when null.</param>
    /// <param name="continueLast">Skip the title menu and load the most recently played world.</param>
    public static int Run(bool quickstart = false, (int Width, int Height)? quickstartSize = null, bool continueLast = false, string? quickstartKit = null)
    {
        ConsoleHost.HideForGraphicalMode();
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ReportCrash(e.Exception);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new GameForm(quickstart, quickstartSize, continueLast, quickstartKit));
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return 0;
    }

    /// <summary>
    /// Writes crash.log beside the profiles and tells the player, instead of the window vanishing.
    /// The game keeps running when the error came from a single UI event.
    /// </summary>
    private static void ReportCrash(Exception exception)
    {
        var directory = Environment.GetEnvironmentVariable("RIMTERA_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rimtera");
        var path = Path.Combine(directory, "crash.log");
        try
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(path, $"[{DateTimeOffset.Now:u}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            path = "(could not write crash.log)";
        }
        MessageBox.Show($"Rimtera hit an unexpected error:{Environment.NewLine}{exception.Message}{Environment.NewLine}{Environment.NewLine}Details: {path}",
            "Rimtera", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private sealed partial class GameForm : Form
    {
        private readonly MenuFlow _menu;
        private readonly GameCanvas _canvas;
        private readonly HashSet<Keys> _down = new();
        private readonly HashSet<Keys> _menuDown = new();
        private GameSimulation? _game;
        private WorldProfile? _activeWorld;
        private CharacterProfile? _activeCharacter;
        private bool _menuOpen = true;
        private readonly ScreenTransition _transition = new();
        private readonly AudioEngine _audio = new();
        private readonly AudioDirector _audioDirector;
        private float _autosaveSeconds;
        private bool _jump;
        private bool _dash;
        private bool _mine;
        private bool _use;
        private bool _replace;
        private bool _eat;
        private bool _boss;
        private WindowMode _appliedMode;
        private int _appliedWidth;
        private int _appliedHeight;
        private bool _displayApplied;

        public GameForm(bool quickstart = false, (int Width, int Height)? quickstartSize = null, bool continueLast = false, string? quickstartKit = null)
        {
            Text = "Rimtera";
            ClientSize = new Size(1280, 720);
            MinimumSize = new Size(800, 480);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            DoubleBuffered = true;

            var dataDirectory = Environment.GetEnvironmentVariable("RIMTERA_DATA_DIR");
            _menu = string.IsNullOrWhiteSpace(dataDirectory) ? new MenuFlow() : new MenuFlow(dataDirectory: dataDirectory);
            _menu.ExitRequested += Close;
            _menu.WorldLaunchRequested += StartWorld;
            _menu.DisplayPreferencesChanged += ApplyDisplayPreferences;

            _audioDirector = new AudioDirector(_audio);
            _audioDirector.Title();
            _canvas = new GameCanvas(_menu) { Dock = DockStyle.Fill, TabStop = true };
            Controls.Add(_canvas);
            ActiveControl = _canvas;

            _canvas.MouseMove += (_, e) =>
            {
                _canvas.MousePosition = e.Location;
                if (_canvas.FullscreenMapOpen)
                {
                    _canvas.MapMouseMove(e.Location);
                    return;
                }
                ForwardPointer(PointerFrame(e.Location));
            };
            _canvas.MouseLeave += (_, _) => _canvas.MousePosition = null;
            _canvas.MouseDown += (_, e) => OnCanvasMouseDown(e);
            _canvas.MouseUp += (_, e) => OnCanvasMouseUp(e);
            // A press whose release never arrives (capture taken away, the window switched mid-drag) must not stay held.
            _canvas.MouseCaptureChanged += (_, _) =>
            {
                if (!IsHandleCreated)
                    return;
                BeginInvoke(() =>
                {
                    if (!_canvas.Capture && _mouse.AnyDown && Environment.TickCount64 - _lastMouseUpTicks > 50)
                        ReleaseAllPointer();
                });
            };
            _canvas.MouseWheel += (_, e) =>
            {
                // Terraria's wheel: zooms the full map; scrolls the hotbar in the world; with the inventory open it scrolls
                // the crafting list or the list under the mouse (Player.Update → DoScrollingInInventory), never the hotbar.
                if (_canvas.FullscreenMapOpen)
                {
                    _canvas.MapZoom(e.Delta, e.Location);
                    return;
                }
                if (!_menuOpen && _game is not null)
                {
                    var current = _smartSelectReturnSlot ?? _game.PendingHotbarSlot;
                    if (current is < 0 or >= Inventory.HotbarSlots)
                        current = 0;
                    var next = (current + (e.Delta < 0 ? 1 : -1) + Inventory.HotbarSlots) % Inventory.HotbarSlots;
                    SelectHotbar(next);
                    return;
                }
                if (_menuOpen && _menu.Ui.CurrentState is InventoryUiState scrolled && _canvas.TryScrollCrafting(e.Location, e.Delta, scrolled))
                    return;
                ForwardPointer(PointerFrame(e.Location) with { ScrollDelta = e.Delta });
            };

            // The game loop runs whenever the message queue is empty (OnIdle): 60 simulation steps a second on a precise clock.
            timeBeginPeriod(1);
            Application.Idle += OnIdle;

            KeyDown += OnKeyDown;
            KeyPress += OnKeyPress;
            KeyUp += (_, e) =>
            {
                if (IsBoundTo("SmartCursor", e.KeyCode) && _menu.Preferences.SmartCursor == SmartCursorMode.Hold)
                    _canvas.Smart.Enabled = false;
                if (IsBoundTo("SmartSelect", e.KeyCode))
                    EndSmartSelect();
                HandleEmoteKeyUp(e.KeyCode);
                _down.Remove(e.KeyCode);
                _menuDown.Remove(e.KeyCode);
            };
            Deactivate += (_, _) =>
            {
                _down.Clear();
                _menuDown.Clear();
                ReleaseAllPointer();
            };
            Resize += (_, _) =>
            {
                UpdateMenuViewport();
                _canvas.Invalidate();
            };
            Shown += (_, _) =>
            {
                UpdateMenuViewport();
                _canvas.Focus();
            };
            FormClosing += OnFormClosing;

            if (quickstart)
            {
                var (quickWidth, quickHeight) = quickstartSize ?? (800, 300);
                _pendingKit = DevKits.Get(quickstartKit);
                Shown += (_, _) => StartWorld(
                    _menu.Profiles.CreateWorld($"Quickstart {DateTime.Now:MMdd-HHmmss}", Environment.TickCount, quickWidth, quickHeight),
                    _menu.Profiles.CreateCharacter($"Pioneer {DateTime.Now:MMdd-HHmmss}"));
            }
            else if (continueLast)
                Shown += (_, _) => ContinueLastWorld();

            _menu.Open();
            _menuOpen = true;
            ApplyDisplayPreferences();
            SyncCanvas();
        }

        private Vector2 ToUiPoint(Point point) => new(point.X, point.Y);

        private void ApplyDisplayPreferences()
        {
            var preferences = _menu.Preferences;
            var mode = preferences.ScreenMode;
            var modeChanged = !_displayApplied || mode != _appliedMode;
            var sizeChanged = preferences.DisplayWidth != _appliedWidth ||
                              preferences.DisplayHeight != _appliedHeight;

            if (modeChanged)
            {
                TopMost = mode == WindowMode.Fullscreen;
                FormBorderStyle = mode == WindowMode.Windowed
                    ? FormBorderStyle.Sizable
                    : FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                if (mode != WindowMode.Windowed && Screen.FromControl(this) is { } screen)
                    Bounds = screen.Bounds;
            }

            if (mode == WindowMode.Windowed && (modeChanged || sizeChanged))
            {
                // Clear any fullscreen bounds so ClientSize change takes effect.
                Bounds = Rectangle.Empty;
                var screen = Screen.FromControl(this) ?? Screen.PrimaryScreen;
                var width = preferences.DisplayWidth;
                var height = preferences.DisplayHeight;
                if (screen is not null)
                {
                    width = Math.Min(width, screen.WorkingArea.Width);
                    height = Math.Min(height, screen.WorkingArea.Height);
                }
                ClientSize = new Size(Math.Max(800, width), Math.Max(480, height));
                if (modeChanged)
                    CenterToScreen();
            }

            _appliedMode = mode;
            _appliedWidth = preferences.DisplayWidth;
            _appliedHeight = preferences.DisplayHeight;
            _displayApplied = true;
            UpdateMenuViewport();
            _canvas.Invalidate();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Alt && e.KeyCode == Keys.Enter)
            {
                // Alt+Enter toggles fullscreen, hard-wired as Terraria's DoUpdate_AltEnter_ToggleFullscreen.
                ToggleFullscreen();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (_transition.BlocksGameplay)
                return;
            if (_canvas.FullscreenMapOpen)
            {
                // The map takes over input (the player stands still, Player.Update zeroes the controls): WASD pans it and
                // the map zoom keys zoom it, both read while held in Advance; M or Esc closes it. The world keeps running.
                if (IsBoundTo("Map", e.KeyCode) || e.KeyCode == Keys.Escape)
                    _canvas.FullscreenMapOpen = false;
                else
                    _down.Add(e.KeyCode);
                e.Handled = true;
                return;
            }
            if (_menuOpen)
            {
                if (_menu.Ui.CurrentState is TextPromptState)
                {
                    HandleTextPromptKey(e);
                    return;
                }
                if (_menu.Ui.CurrentState is BindingCaptureState)
                {
                    HandleBindingCaptureKey(e);
                    return;
                }

                if (PlaysThroughMenu && ActionsFor(e.KeyCode).Where(IsPlayThroughAction).ToList() is { Count: > 0 } playing &&
                    !IsMenuBound("MenuBack", e.KeyCode))
                {
                    // As in Terraria every gameplay key works with the inventory open (Player.Update runs as usual); the
                    // inventory key closes it. Escape stays the menu's Back, which closes the hub too.
                    var held = !_down.Add(e.KeyCode);
                    if (e.KeyCode == Keys.Menu)
                        e.SuppressKeyPress = true;
                    foreach (var action in playing)
                        if (!held || IsRepeatingAction(action))
                            RunAction(action);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }
                var menuRepeat = !_menuDown.Add(e.KeyCode);
                if (TranslateMenuKey(e.KeyCode) is { } frame)
                {
                    _audioDirector.Ui(frame.ConfirmPressed ? "click" : "hover");
                    if (!menuRepeat || (!frame.ConfirmPressed && !frame.BackPressed))
                    {
                        _menu.Handle(frame);
                        AfterMenuAction();
                    }
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                return;
            }

            var repeat = !_down.Add(e.KeyCode);
            if (e.KeyCode == Keys.Menu)
                e.SuppressKeyPress = true; // Alt is bindable; don't let it open the window menu.
            if (HandleEmoteKey(e))
                return;
            var actions = ActionsFor(e.KeyCode);
            if (actions.Count == 0 && e.KeyCode == Keys.Escape)
            {
                // Escape always gets somewhere: with nothing bound to it, it opens settings.
                OpenPauseMenu();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (!repeat && TryDoubleTapDash(actions))
                _dash = true;
            foreach (var action in actions)
            {
                if (repeat && !IsRepeatingAction(action))
                    continue;
                RunAction(action);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        /// <summary>Carries out one gameplay action from <see cref="ControlCatalog"/>.</summary>
        private void RunAction(string action)
        {
            if (_game is null)
                return;
            var game = _game;
            if (action.StartsWith("Hotbar", StringComparison.Ordinal) && int.TryParse(action[6..], out var hotbar))
            {
                SelectHotbar(hotbar - 1);
                return;
            }
            if (action.StartsWith("Loadout", StringComparison.Ordinal) && int.TryParse(action[7..], out var loadout))
            {
                if (game.SwitchLoadout(loadout - 1))
                    _canvas.Notify($"Loadout {loadout} equipped.");
                if (_menu.Ui.CurrentState is InventoryUiState open)
                    open.Refresh();
                return;
            }
            switch (action)
            {
                case "Jump": _jump = true; break;
                case "Dash": _dash = true; break;
                case "UseSelected": _use = true; break;
                case "ReplaceTile": _replace = true; break;
                case "Eat": _eat = true; break;
                case "Interact": InteractAtCursor(); break;
                case "SmartSelect": break; // re-checked every tick while held (UpdateSmartSelect)
                case "SmartCursor":
                    // Over an interface Ctrl is Ctrl-click (trash), never the smart cursor (Player.Update @3612-3627).
                    if (_canvas.MousePosition is { } overPoint && PointerOverInterface(overPoint))
                        break;
                    if (_menu.Preferences.SmartCursor == SmartCursorMode.Hold)
                        _canvas.Smart.Enabled = true;
                    else
                    {
                        _canvas.Smart.Enabled = !_canvas.Smart.Enabled;
                        _canvas.Notify(_canvas.Smart.Enabled ? "Smart cursor on." : "Smart cursor off.");
                    }
                    break;
                case "ThrowItem":
                    switch (game.TryThrowHeld())
                    {
                        case ThrowOutcome.NothingHeld: _canvas.Notify("Nothing in hand to throw."); break;
                        case ThrowOutcome.Favorite: _canvas.Notify("Favorites can't be thrown."); break;
                    }
                    break;
                case "Grapple":
                    // Terraria's QuickGrapple: every press fires a hook (a new one replaces the old); jumping lets go.
                    if (_canvas.CursorTile() is { } hookAt && !game.TryFireGrapple(new Vector2(hookAt.X + 0.5f, hookAt.Y + 0.5f)))
                        _canvas.Notify("Equip a grappling hook to fire it.");
                    break;
                case "Mount":
                    if (!game.ToggleMount())
                        _canvas.Notify("You need a hover-bike key, on open ground.");
                    break;
                case "QuickHeal":
                    if (!game.QuickHeal())
                        _canvas.Notify(game.PotionSick ? "Still recovering from the last dose." : "No healing item to use.");
                    break;
                case "QuickCharge":
                    if (!game.QuickCharge())
                        _canvas.Notify("No charge item to use.");
                    break;
                case "QuickBuff":
                    var buffs = game.QuickBuff();
                    _canvas.Notify(buffs > 0 ? $"Used {buffs} buff item{(buffs == 1 ? "" : "s")}." : "No buff items to use.");
                    break;
                case "Inventory":
                    if (_menuOpen && _menu.Ui.CurrentState is InventoryUiState)
                        CloseInventory();
                    else
                        OpenInventory();
                    break;
                case "Pause": OpenPauseMenu(); break;
                case "Colonist": OpenColonistSheet(); break;
                case "Bestiary": OpenBestiary(); break;
                case "Achievements": OpenAchievements(); break;
                case "Map":
                    // TryOpeningFullscreenMap closes the inventory first.
                    if (_menuOpen && _menu.Ui.CurrentState is InventoryUiState)
                        CloseInventory();
                    _canvas.OpenFullscreenMap(game);
                    break;
                case "ZoomIn": StepZoom(0.1f); break;
                case "ZoomOut": StepZoom(-0.1f); break;
                case "HideUi": _canvas.HideUi = !_canvas.HideUi; break;
                case "Creative":
                    if (game.IsCreative)
                        OpenCreativeMenu();
                    break;
                case "MapStyle":
                    _canvas.MapView = (GameCanvas.MapStyle)(((int)_canvas.MapView + 1) % 3);
                    _canvas.Notify($"Map: {_canvas.MapView}.");
                    break;
                case "MinimapZoomIn": _canvas.ZoomMinimap(1); break;
                case "MinimapZoomOut": _canvas.ZoomMinimap(-1); break;
                case "ControlsHelp": _canvas.ShowControls = !_canvas.ShowControls; break;
                case "FrameRate":
                    _menu.Preferences.ShowFrameRate = !_menu.Preferences.ShowFrameRate;
                    _menu.SavePreferences();
                    break;
                case "Craft": game.TryCraftNext(); break;
                case "CraftPrevious": game.CycleCraftableRecipe(-1); break;
                case "CraftNext": game.CycleCraftableRecipe(1); break;
                case "CycleInventory": game.SelectNextInventoryItem(); break;
                case "SummonBoss": _boss = true; break;
                case "Sleep":
                    _canvas.Notify(game.TrySleep() ? "You lie down. Time passes quickly until dawn." : "You need a bed nearby, and it must be night.");
                    break;
                case "CheckHousing":
                    if (_canvas.CursorTile() is { } housingCell)
                    {
                        var housing = game.CheckHousing(housingCell);
                        _canvas.Notify(housing.Valid ? "This room is suitable housing." : $"Not valid housing: {housing.Reason}");
                    }
                    break;
                case "WireLayer":
                    game.CycleWireLayer();
                    _canvas.Notify($"Wire layer {game.ActiveWireLayer}.");
                    break;
            }
        }

        // ------------------------------------------------------------ key bindings

        private Dictionary<Keys, List<string>> _keyActions = new();
        private InputProfile? _keyActionsProfile;
        private int _keyActionsRevision = -1;

        /// <summary>The gameplay actions bound to a key (menu-navigation actions excluded), rebuilt when bindings change.</summary>
        private List<string> ActionsFor(Keys key)
        {
            var profile = _menu.InputProfile;
            if (!ReferenceEquals(profile, _keyActionsProfile) || profile.Revision != _keyActionsRevision)
            {
                _keyActions = new Dictionary<Keys, List<string>>();
                foreach (var action in ControlCatalog.All.Where(action => !action.IsMenu))
                {
                    if (!profile.Bindings.TryGetValue(action.Id, out var bindings))
                        continue;
                    foreach (var binding in bindings)
                        if (InputBindingResolver.TryParseKeyCode(binding, out var code))
                            (_keyActions.TryGetValue((Keys)code, out var list) ? list : _keyActions[(Keys)code] = new List<string>()).Add(action.Id);
                }
                _keyActionsProfile = profile;
                _keyActionsRevision = profile.Revision;
            }
            return _keyActions.TryGetValue(key, out var found) ? found : [];
        }

        private bool IsBoundTo(string action, Keys key) => ActionsFor(key).Contains(action);

        /// <summary>True while any key bound to the action is held.</summary>
        private bool Held(string action) => _down.Any(key => IsBoundTo(action, key));

        private string _lastTapAction = string.Empty;
        private long _lastTapTicks;

        /// <summary>Terraria's dash: tapping left or right twice within a quarter second.</summary>
        private bool TryDoubleTapDash(List<string> actions)
        {
            var direction = actions.FirstOrDefault(action => action is "MoveLeft" or "MoveRight");
            if (direction is null)
                return false;
            var now = Environment.TickCount64;
            var doubled = direction == _lastTapAction && now - _lastTapTicks <= 250;
            _lastTapAction = doubled ? string.Empty : direction;
            _lastTapTicks = now;
            return doubled;
        }

        /// <summary>The slot Shift auto-select switched away from, restored when Shift is let go.</summary>
        private int? _smartSelectReturnSlot;

        private void EndSmartSelect()
        {
            var slot = _smartSelectReturnSlot;
            _smartSelectReturnSlot = null;
            if (slot is { } back)
                _game?.RequestHotbarSlot(back);
        }

        private void HandleTextPromptKey(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    _menu.CancelTextPrompt();
                    AfterMenuAction();
                    break;
                case Keys.Enter:
                    _menu.SubmitTextPrompt();
                    AfterMenuAction();
                    break;
                case Keys.Back:
                    _menu.EditText(TextEditCommand.Backspace);
                    AfterMenuAction();
                    break;
                case Keys.Delete:
                    _menu.EditText(TextEditCommand.Delete);
                    AfterMenuAction();
                    break;
                case Keys.Left:
                    _menu.EditText(TextEditCommand.MoveLeft);
                    AfterMenuAction();
                    break;
                case Keys.Right:
                    _menu.EditText(TextEditCommand.MoveRight);
                    AfterMenuAction();
                    break;
                case Keys.Home:
                    _menu.EditText(TextEditCommand.Home);
                    AfterMenuAction();
                    break;
                case Keys.End:
                    _menu.EditText(TextEditCommand.End);
                    AfterMenuAction();
                    break;
                case Keys.Tab:
                case Keys.Up:
                case Keys.Down:
                    break;
                default:
                    // Printable characters arrive through KeyPress and are
                    // inserted into the retained text field.
                    return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void HandleBindingCaptureKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                _menu.CancelBindingCapture();
            }
            else if (InputBindingResolver.NameForKeyCode((int)e.KeyCode) is { } name)
            {
                // Shift, Ctrl and Alt bind too (Terraria puts smart select and smart cursor on them).
                _menu.CaptureBinding(name);
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            AfterMenuAction();
        }

        private void OnKeyPress(object? sender, KeyPressEventArgs e)
        {
            if (!_menuOpen || !_menu.IsTextPromptActive)
                return;
            if (char.IsControl(e.KeyChar))
            {
                e.Handled = true;
                return;
            }

            _menu.InsertText(e.KeyChar.ToString());
            e.Handled = true;
            SyncCanvas();
        }

        private void AfterMenuAction()
        {
            if (IsDisposed || Disposing)
                return;
            _menuOpen = _menu.IsOpen;
            UpdateMenuViewport();
            SyncCanvas();
        }

        /// <summary>
        /// A pointer frame carrying the button state from this window's own mouse events (<see cref="_mouse"/>), and only
        /// for presses that began on an interface: a press on the world never reaches the UI. Reading the live hardware
        /// state instead (Control.MouseButtons) missed posted clicks entirely.
        /// </summary>
        private UiInputFrame PointerFrame(Point location) => new(ToUiPoint(location),
            LeftDown: _mouse.InterfaceLeftDown, RightDown: _mouse.InterfaceRightDown);

        /// <summary>The inventory is open over a running world (Autopause off): the player keeps moving and using items, as in Terraria.</summary>
        private bool PlaysThroughMenu => _menuOpen && _game is not null && !_menu.Preferences.AutoPause && _menu.Ui.CurrentState is InventoryUiState;

        /// <summary>
        /// Gameplay keys with the inventory open: all of them, as in Terraria (Player.Update copies every trigger whatever
        /// the inventory; only chat and sign editing block the hotbar keys). Emotes stay with the world view.
        /// </summary>
        private static bool IsPlayThroughAction(string action) => action is not "Emote";

        /// <summary>Actions a held key repeats (Terraria reads these triggers every tick while held).</summary>
        private static bool IsRepeatingAction(string action) =>
            action is "Jump" or "CraftPrevious" or "CraftNext" or "MinimapZoomIn" or "MinimapZoomOut" or "CycleInventory" or "ZoomIn" or "ZoomOut";

        /// <summary>Camera zoom (Terraria's ViewZoomIn / ViewZoomOut on = and -); the same setting as Options → Zoom.</summary>
        private void StepZoom(float step)
        {
            var zoom = Math.Clamp(MathF.Round((_menu.Preferences.Zoom + step) * 10f) / 10f, 0.6f, 2f);
            if (zoom == _menu.Preferences.Zoom)
                return;
            _menu.Preferences.Zoom = zoom;
            _menu.SavePreferences();
            _canvas.Notify($"Zoom {zoom:P0}");
        }

        private void ToggleFullscreen()
        {
            _menu.Preferences.SetScreenMode(_menu.Preferences.ScreenMode == WindowMode.Fullscreen ? WindowMode.Windowed : WindowMode.Fullscreen);
            _menu.SavePreferences();
            ApplyDisplayPreferences();
        }

        private void ForwardPointer(UiInputFrame frame)
        {
            if (!_menuOpen)
                return;
            _menu.Handle(frame);
            AfterMenuAction();
        }

        // ------------------------------------------------------------ mouse

        private readonly MouseButtonState _mouse = new();
        private readonly UseButtonGate _useGate = new();
        private long _lastMouseUpTicks;
        /// <summary>The world-use state computed this tick (the left button went down on the world, not over an interface).</summary>
        private bool _worldUseHeld;

        private static UiPointerButton? ButtonOf(MouseButtons button) => button switch
        {
            MouseButtons.Left => UiPointerButton.Left,
            MouseButtons.Right => UiPointerButton.Right,
            _ => null
        };

        /// <summary>
        /// True when <paramref name="point"/> is over an interface rather than the world: a hub element or the crafting panel
        /// with the inventory open, the HUD with it closed. Terraria's <c>mouseInterface</c>.
        /// </summary>
        private bool PointerOverInterface(Point point) => _menuOpen
            ? _menu.Ui.IsOverInterface(ToUiPoint(point)) || _canvas.IsOverCraftingPanel(point)
            : _canvas.IsOverHud(point) || _canvas.GuideHintAt(point);

        /// <summary>
        /// Every press is recorded in <see cref="_mouse"/> with its owner, then routed: map, crafting and guide panels and
        /// HUD elements take it as an interface press; otherwise the world gets it (left uses, right interacts or throws
        /// the cursor item). Clicks over UI never reach the world, and world presses never reach the UI.
        /// </summary>
        private void OnCanvasMouseDown(MouseEventArgs e)
        {
            if (ButtonOf(e.Button) is not { } button)
                return;
            _canvas.MousePosition = e.Location;
            if (_canvas.FullscreenMapOpen)
            {
                _mouse.Press(button, PointerOwner.Interface);
                if (button == UiPointerButton.Left && _game is not null && _canvas.TryMapPylonTeleport(_game, e.Location)) return;
                _canvas.MapMouseDown(e.Location);
                return;
            }
            if (button == UiPointerButton.Left && _menuOpen && _game is not null && _menu.Ui.CurrentState is InventoryUiState guiding &&
                HandleGuideClick(guiding, e.Location))
            {
                _mouse.Press(button, PointerOwner.Interface);
                return;
            }
            if (button == UiPointerButton.Left && _menuOpen && _game is not null && _menu.Ui.CurrentState is InventoryUiState &&
                _canvas.TryCraftClick(e.Location, out var recipeId))
            {
                _mouse.Press(button, PointerOwner.Interface);
                if (recipeId is not null && _menu.Ui.CurrentState is InventoryUiState crafting)
                {
                    var crafted = _game.TryCraft(recipeId);
                    crafting.ShowStatus(crafted.Message);
                    if (crafted.Success)
                        BeginCraftHold(recipeId);
                }
                SyncCanvas();
                return;
            }
            // Terraria plays on with the inventory open: a press on the world (not on a panel) uses the held item, or the
            // item on the cursor, and a right press interacts, or throws the cursor item.
            if (PlaysThroughMenu && !PointerOverInterface(e.Location))
            {
                _mouse.Press(button, PointerOwner.World);
                if (button == UiPointerButton.Right)
                    RightClickWorld();
                return;
            }
            if (!_menuOpen && _game is not null)
            {
                if (button == UiPointerButton.Left && HandleGuideHintClick(e.Location) || _canvas.GuideHintAt(e.Location))
                {
                    _mouse.Press(button, PointerOwner.Interface);
                    return;
                }
                if (_canvas.HotbarSlotAt(e.Location) is { } slot)
                {
                    _mouse.Press(button, PointerOwner.Interface);
                    if (button == UiPointerButton.Left)
                        SelectHotbar(slot);
                    return;
                }
                if (_canvas.IsOverHud(e.Location))
                {
                    _mouse.Press(button, PointerOwner.Interface);
                    if (button == UiPointerButton.Right && _canvas.BuffAt(e.Location) is { } buffKey && _game.CancelBuff(buffKey))
                        _audioDirector.Ui("click");
                    return;
                }
                _mouse.Press(button, PointerOwner.World);
                if (button == UiPointerButton.Right)
                    RightClickWorld();
                return;
            }
            _mouse.Press(button, PointerOwner.Interface);
            ForwardPointer(PointerFrame(e.Location));
        }

        private void OnCanvasMouseUp(MouseEventArgs e)
        {
            _lastMouseUpTicks = Environment.TickCount64;
            _canvas.MapMouseUp();
            if (ButtonOf(e.Button) is { } button)
            {
                _mouse.Release(button);
                if (button == UiPointerButton.Left)
                    EndCraftHold();
            }
            ForwardPointer(PointerFrame(e.Location));
        }

        /// <summary>Focus or capture lost: nothing stays pressed, nothing half-clicked fires later.</summary>
        private void ReleaseAllPointer()
        {
            _mouse.Clear();
            _menu.Ui.CancelPointer();
            _useGate.Reset();
            _worldUseHeld = false;
            EndCraftHold();
            EndSmartSelect();
        }

        /// <summary>
        /// A right press on the world: with an item on the cursor it is thrown (Terraria's <c>dropItemCheck</c>), otherwise
        /// doors, containers, beds, NPCs and the station AI answer it.
        /// </summary>
        private void RightClickWorld()
        {
            if (_game is null)
                return;
            if (_game.Player.Inventory.HoldingCursor)
            {
                _game.ThrowCursorItem();
                if (_menu.Ui.CurrentState is InventoryUiState open)
                    open.ShowStatus("Thrown.");
                SyncCanvas();
                return;
            }
            InteractAtCursor();
        }

        /// <summary>A hotbar choice from the player: waits for the current swing, and while Shift auto-select holds a tool it becomes the slot to return to.</summary>
        private void SelectHotbar(int slot)
        {
            if (_game is null)
                return;
            if (_smartSelectReturnSlot is not null)
                _smartSelectReturnSlot = slot;
            else
                _game.RequestHotbarSlot(slot);
        }

        /// <summary>
        /// Shift auto-select, re-checked every tick while the key is held and the mouse is over the world (Terraria's
        /// <c>SmartSelectLookup</c>, run from <c>PickItemSelectionOverride</c> each frame): the hotbar jumps to the item the
        /// game picks for the cell under the cursor and returns to the slot before when Shift is let go.
        /// </summary>
        private void UpdateSmartSelect(bool overInterface)
        {
            if (_game is null)
                return;
            if (!Held("SmartSelect") || overInterface || _canvas.FullscreenMapOpen || _game.Player.Inventory.HoldingCursor)
            {
                EndSmartSelect();
                return;
            }
            if (_game.IsUsingItem || _canvas.CursorTile() is not { } cursor)
                return;
            var inventory = _game.Player.Inventory;
            if (_game.SmartSelectSlot(cursor) is not { } slot)
            {
                EndSmartSelect();
                return;
            }
            _smartSelectReturnSlot ??= inventory.SelectedSlot;
            if (slot != inventory.SelectedSlot)
                _game.TrySelectInventorySlot(slot);
        }

        /// <summary>Closes the inventory hub the way its own close does (the bank closes with it).</summary>
        private void CloseInventory()
        {
            _game?.CloseBank();
            CloseMenu();
        }


        private UiInputFrame? TranslateMenuKey(Keys key)
        {
            if (IsMenuBound("MenuUp", key))
                return new UiInputFrame(default, NavigationY: -1);
            if (IsMenuBound("MenuDown", key))
                return new UiInputFrame(default, NavigationY: 1);
            if (IsMenuBound("MenuLeft", key))
                return new UiInputFrame(default, NavigationX: -1);
            if (IsMenuBound("MenuRight", key))
                return new UiInputFrame(default, NavigationX: 1);
            if (IsMenuBound("MenuConfirm", key))
                return new UiInputFrame(default, ConfirmPressed: true);
            if (IsMenuBound("MenuBack", key))
                return new UiInputFrame(default, BackPressed: true);
            return null;
        }

        private bool IsMenuBound(string action, Keys key) =>
            _menu.InputProfile.Bindings.TryGetValue(action, out var bindings) &&
            bindings.Any(binding => InputBindingResolver.TryParseKeyCode(binding, out var code) && (Keys)code == key);

        private void Advance()
        {
            UpdateCraftHold();
            if (_game is not null)
            {
                _audioDirector.Update(_game, _menu.Preferences);
                AnnounceAchievements();
            }
            else
            {
                _audio.SoundVolume = _menu.Preferences.SoundVolume;
                _audio.MusicVolume = _menu.Preferences.MusicVolume;
                _audioDirector.Title();
            }
            if (_transition.Update(1f / 60f) && _game is not null && _activeCharacter is not null)
            {
                _game.RespawnPlayer(_activeCharacter.Difficulty);
                var landing = _game.Player.Motion.Position;
                _canvas.Map?.RevealArea(_game.World, (int)landing.X, (int)landing.Y, 12);
                _transition.FadeIn();
            }
            if (_transition.BlocksGameplay)
            {
                SyncCanvas();
                return;
            }
            if (_game is not null)
            {
                // Terraria's dropItemCheck: with the inventory gone, a stack still on the cursor goes back to the bag (or
                // drops at the player's feet when the bag is full). Nothing on the cursor is ever lost.
                if (_game.Player.Inventory.HoldingCursor && !(_menuOpen && _menu.Ui.CurrentState is InventoryUiState))
                    _game.ReturnCursorItem();
                if (_menuOpen && _menu.Ui.CurrentState is InventoryUiState hub)
                {
                    hub.TickRightHold(_canvas.MousePosition is { } at ? _menu.Ui.ElementAt(ToUiPoint(at)) : null, _mouse.InterfaceRightDown);
                    hub.RefreshCursorLabel();
                }
            }
            // Autopause off: the world keeps running behind the inventory and colonist sheet.
            var runBehindMenu = _menuOpen && _game is not null && !_menu.Preferences.AutoPause &&
                                _menu.Ui.CurrentState is InventoryUiState or ColonistSheetState;
            _canvas.WorldRunsBehindMenu = runBehindMenu;
            if (runBehindMenu && !PlaysThroughMenu)
            {
                _game!.Tick(new GameInput(new MovementInput(0f)));
                SyncCanvas();
                return;
            }
            if (_menuOpen && !PlaysThroughMenu || _game is null)
            {
                SyncCanvas();
                return;
            }
            // A run that has ended (lost now, or loaded already ended) shows its end screen instead of playing on.
            if (_game.RunOver)
            {
                ShowRunEnd(_game);
                SyncCanvas();
                return;
            }

            var overInterface = _canvas.MousePosition is { } pointer && PointerOverInterface(pointer);
            var mapOpen = _canvas.FullscreenMapOpen;
            if (mapOpen)
            {
                // Player.Update with the full map open: WASD pans it, the map zoom keys zoom it, and the player stands still.
                const float pan = 16f;
                _canvas.PanFullscreenMap((Held("MoveRight") ? pan : 0f) - (Held("MoveLeft") ? pan : 0f),
                    (Held("MoveDown") ? pan : 0f) - (Held("MoveUp") ? pan : 0f));
                if (Held("MinimapZoomIn")) _canvas.ZoomFullscreenMap(1.05f);
                if (Held("MinimapZoomOut")) _canvas.ZoomFullscreenMap(0.95f);
            }
            if (!mapOpen && Held("MapAlphaUp")) _canvas.AdjustMapOpacity(0.015f);
            if (!mapOpen && Held("MapAlphaDown")) _canvas.AdjustMapOpacity(-0.015f);
            UpdateSmartSelect(overInterface);
            // Holding left and right together cancels out (Player.Update @3583-3602).
            var horizontal = mapOpen ? 0f : (Held("MoveRight") ? 1f : 0f) - (Held("MoveLeft") ? 1f : 0f);
            _worldUseHeld = !mapOpen && _mouse.WorldUseHeld(overInterface);
            // Items without auto-reuse are used once per press; the rest keep going while held (Item.autoReuse).
            if (_useGate.Update(_worldUseHeld, ItemUseRules.AutoReuse(_game.Player.Inventory.SelectedItem)) && !_mine && !_use)
            {
                if (_game.Player.Inventory.SelectedItem?.UseKind == ItemUseKind.Pickaxe)
                    _mine = true;
                else
                    _use = true;
            }
            var action = _mine ? PlayerAction.Mine
                : _use ? PlayerAction.UseSelected
                : _replace ? PlayerAction.ReplaceSelected
                : _eat ? PlayerAction.Eat
                : PlayerAction.None;
            if (_boss)
                _game.TrySummonSentinel();
            var aim = AimTile();
            // Terraria's block swap: using a block on a different solid block replaces it (given a strong enough drill).
            if (action == PlayerAction.UseSelected && _game.Player.Inventory.SelectedItem is { UseKind: ItemUseKind.PlaceTile } block &&
                _game.World.Contains(aim) && _game.World.GetCell(aim).TileId is var standing && _game.Tiles[standing].IsSolid &&
                block.PlacedTile != standing)
                action = PlayerAction.ReplaceSelected;

            _game.Tick(new GameInput(
                new MovementInput(horizontal, JumpPressed: _jump && !mapOpen, DashPressed: _dash && !mapOpen,
                    JumpHeld: !mapOpen && Held("Jump"),
                    Vertical: mapOpen ? 0f : Held("MoveUp") ? -1f : Held("MoveDown") ? 1f : 0f),
                action,
                aim));
            _jump = _dash = _mine = _use = _replace = _eat = _boss = false;
            if (_game.IsOver)
            {
                HandlePlayerDeath();
                SyncCanvas();
                return;
            }

            _autosaveSeconds += 1f / 60f;
            if (_menu.Preferences.AutoSave && _autosaveSeconds >= _menu.Preferences.AutosaveMinutes * 60f)
            {
                _autosaveSeconds = 0f;
                SaveGame(showStatus: false);
            }

            SyncCanvas();
        }

        private GridPoint AimTile()
        {
            var game = _game!;
            if (_canvas.CursorTile() is { } cursor)
            {
                if (!_worldUseHeld)
                    _canvas.Smart.ReleaseLock();
                return _canvas.Smart.Resolve(game, cursor, _worldUseHeld)?.Tile ?? cursor;
            }
            var motion = game.Player.Motion;
            var x = (int)MathF.Floor(motion.Position.X + motion.Size.X / 2f + motion.Facing * 2.5f);
            var y = (int)MathF.Floor(motion.Position.Y + motion.Size.Y * 0.55f);
            return new GridPoint(
                Math.Clamp(x, 0, game.World.Width - 1),
                Math.Clamp(y, 0, game.World.Height - 1));
        }

        private async void StartWorld(WorldProfile world, CharacterProfile character)
        {
            if (_transition.BlocksGameplay)
                return;
            var hasSave = _menu.Saves.Exists(world.Id);
            _menu.Close();
            _menuOpen = false;
            _down.Clear();
            _menuDown.Clear();
            _transition.Begin(hasSave ? TransitionKind.Loading : TransitionKind.Generating,
                hasSave ? $"Loading {world.Name}" : $"Generating {world.Name}",
                hasSave ? "Reading save" : "Surveying the landing site");
            SyncCanvas();

            var progress = new Progress<GenerationProgress>(_transition.Report);
            GameSimulation next;
            try
            {
                // A short minimum keeps the generation/loading screen readable on small worlds.
                var minimumScreen = Task.Delay(hasSave ? 600 : 1200);
                next = await Task.Run(() =>
                {
                    if (!hasSave)
                        return CreateSimulation(world, character, progress);
                    if (!_menu.Saves.TryLoad(world.Id, out var snapshot) || snapshot is null)
                        throw new InvalidDataException("the save file exists but could not be read");
                    ((IProgress<GenerationProgress>)progress).Report(new GenerationProgress("Restoring the colony", 0.6, 0.6));
                    return GameSimulation.Restore(snapshot, character);
                });
                await minimumScreen;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                _transition.Clear();
                _menu.Open();
                _menu.SetStatus($"Could not load {world.Name}: {ex.Message}");
                _menuOpen = true;
                UpdateMenuViewport();
                SyncCanvas();
                return;
            }

            _transition.Complete();
            next.Difficulty = world.Difficulty;
            _canvas.FullscreenMapOpen = false;
            _canvas.Map = WorldMapState.Load(MapPath(world, character), next.World.Width, next.World.Height);
            _canvas.Map.SpawnMarker ??= new WorldMapState.GridPointF(next.Player.Motion.Position.X, next.Player.Motion.Position.Y + 1f);
            if (_canvas.Map.RevealedChunkCount == 0)
            {
                var landing = next.Player.Motion.Position;
                _canvas.Map.RevealArea(next.World, (int)landing.X, (int)landing.Y, 24);
            }
            _game = next;
            _pendingKit?.Invoke(next);
            _pendingKit = null;
            _activeWorld = world;
            _activeCharacter = character;
            Text = $"Rimtera - {world.Name}";
            _autosaveSeconds = 0f;
            _down.Clear();
            _menuDown.Clear();
            _canvas.Focus();
            _transition.FadeIn();
            SyncCanvas();
        }

        /// <summary>
        /// Death screen by character difficulty: Explorer and Classic count down and respawn at the
        /// landing site; Ironman saves the world, retires the character, and returns to the title.
        /// </summary>
        private void HandlePlayerDeath()
        {
            if (_game is not null && _canvas.Map is { } deathMap)
                deathMap.DeathMarker = new WorldMapState.GridPointF(_game.Player.Motion.Position.X, _game.Player.Motion.Position.Y);
            _canvas.FullscreenMapOpen = false;
            var character = _activeCharacter;
            var name = character?.Name ?? "Your colonist";
            _down.Clear();
            if (character?.Difficulty == CharacterDifficulty.Ironman)
            {
                TrySaveGame(out _);
                _menu.Profiles.DeleteCharacter(character.Id);
                _activeCharacter = null;
                QuitToTitle($"{name} died. Ironman colonists cannot return.");
                return;
            }
            // Terraria's timer: 10 s, 15 s on Brutal and Annihilation worlds, 3 s for an Explorer.
            var seconds = _game?.GetRespawnDelay(character?.Difficulty ?? default) ?? GameSimulation.BaseRespawnSeconds;
            _transition.BeginCountdown($"{name} was killed", _game?.LastMessage ?? string.Empty, seconds);
        }

        /// <summary>Builds a fresh world from its profile; runs on a worker thread.</summary>
        private static GameSimulation CreateSimulation(WorldProfile world, CharacterProfile character, IProgress<GenerationProgress> progress) =>
            new(world.Seed, world.Width, world.Height, character, world.Hazard, progress, WorldSettings.Resolve(world.Blight, world.Seed));

        private void OpenPauseMenu()
        {
            _down.Clear();
            _menuDown.Clear();
            if (_game is null)
            {
                _menu.Open();
                _menuOpen = true;
                UpdateMenuViewport();
                SyncCanvas();
                return;
            }

            // Terraria's in-game Settings: reached from the inventory, so it doesn't offer the inventory or crew again.
            _menu.ShowPauseMenu(
                resume: CloseMenu,
                openInventory: null,
                saveAndExit: SaveAndQuitToTitle,
                save: () => SaveGame(),
                quitToTitle: SaveAndQuitToTitle,
                exitGame: SaveAndExitGame,
                openCreative: _game?.IsCreative == true ? OpenCreativeMenu : null,
                openFeats: OpenAchievements);
            _menuOpen = true;
            UpdateMenuViewport();
            SyncCanvas();
        }

        private void OpenInventory()
        {
            if (_game is null)
                return;
            _down.Clear();
            _menuDown.Clear();
            ShowStorage(null, null, "Chest");
        }

        private void OpenColonistSheet()
        {
            if (_game is null)
                return;
            _down.Clear();
            _menuDown.Clear();
            _menu.ShowColonistSheet(_game.Player.Colonist, _game, CloseMenu);
            _menuOpen = true;
            UpdateMenuViewport();
            SyncCanvas();
        }

        private void CloseMenu()
        {
            _menu.Close();
            _menuOpen = false;
            _down.Clear();
            _menuDown.Clear();
            _canvas.Focus();
            SyncCanvas();
        }

        private void SaveAndQuitToTitle()
        {
            var worldName = _activeWorld?.Name ?? "World";
            SaveWithScreen(() =>
            {
                QuitToTitle($"{worldName} saved.");
                _transition.FadeIn();
            });
        }

        private void SaveAndExitGame() => SaveWithScreen(() =>
        {
            _game = null;
            _activeWorld = null;
            _activeCharacter = null;
            _transition.Clear();
            Close();
        });

        /// <summary>
        /// Captures the snapshot on the UI thread, writes it on a worker behind the saving
        /// screen, then runs <paramref name="after"/>. A failed save returns to the pause menu.
        /// </summary>
        private async void SaveWithScreen(Action after)
        {
            if (_game is null || _activeWorld is null || _transition.BlocksGameplay)
                return;
            var world = _activeWorld;
            var snapshot = _game.CaptureSnapshot(binaryCells: true);
            var characterId = _activeCharacter?.Id;
            var characterName = _activeCharacter?.Name;
            _menu.Close();
            _menuOpen = false;
            _transition.Begin(TransitionKind.Saving, $"Saving {world.Name}", "Writing the colony record");
            _transition.Report(new GenerationProgress("Writing the colony record", 0.4, 0.4));
            SyncCanvas();
            try
            {
                var map = _canvas.Map;
                var mapPath = _activeCharacter is { } mapCharacter ? MapPath(world, mapCharacter) : null;
                await Task.Run(() =>
                {
                    _menu.Saves.Save(world.Id, snapshot, characterId, characterName);
                    if (map is not null && mapPath is not null)
                        map.Save(mapPath);
                });
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _transition.Clear();
                OpenPauseMenu();
                _menu.SetStatus($"Could not save {world.Name}: {ex.Message}");
                return;
            }
            _transition.Complete();
            _transition.Report(new GenerationProgress("Saved", 1, 1));
            await Task.Delay(250);
            after();
        }

        private void QuitToTitle(string? status = null)
        {
            _game = null;
            _activeWorld = null;
            _activeCharacter = null;
            Text = "Rimtera";
            _autosaveSeconds = 0f;
            _down.Clear();
            _menuDown.Clear();
            _menu.Open();
            if (!string.IsNullOrWhiteSpace(status))
                _menu.SetStatus(status);
            _menuOpen = true;
            UpdateMenuViewport();
            SyncCanvas();
        }

        /// <summary>Right-click / G: doors, containers, vaults, survivors and town NPCs at the cursor.</summary>
        private void InteractAtCursor()
        {
            if (_game is null)
                return;
            var target = _canvas.CursorTile() ?? AimTile();
            if (_game.TryInteractPlayerSystems(target))
                return;
            // Right-clicking the station AI talks to it, as with Terraria's Guide.
            if (Vector2.Distance(new Vector2(target.X + 0.5f, target.Y + 0.5f), _game.GuidePosition) < 1.5f)
            {
                OpenStationAiScreen(StationAiAt(_game.GuidePosition), consult: true);
                return;
            }
            // Right-clicking a bed sleeps in it, as in Terraria 1.4.
            if (_game.World.Contains(target) && ContentTiles.Has(_game.World.TileAt(target.X, target.Y), TileTraits.Bed))
            {
                _canvas.Notify(_game.TrySleep() ? "You lie down. Time passes quickly until dawn." : "You can only sleep in a bed at night.");
                return;
            }
            var result = _game.Interact(target);
            switch (result.Kind)
            {
                case WorldInteractionKind.ContainerOpened when result.Container is { } container:
                    ShowStorage(container, result.Cell, (result.Cell is { } named ? _game.GetChestName(named) : null) ?? "Chest");
                    break;
                case WorldInteractionKind.BankOpened when result.Container is { } bank:
                    ShowStorage(bank, null, "Cargo pod");
                    break;
                case WorldInteractionKind.SignRead when result.Cell is { } signCell:
                    OpenSignScreen(signCell, result.Message);
                    break;
                case WorldInteractionKind.NpcTalk when result.Npc is { } npc:
                    OpenNpcScreen(npc, _game.GetNpcDialogue(npc));
                    break;
                case WorldInteractionKind.None:
                    if (_game.Bobber is not null || _game.Player.Inventory.SelectedItem?.UseKind == ItemUseKind.PlaceTile)
                        _replace = true; // nothing to interact with: keep the old replace-on-right-click behaviour
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(result.Message))
                        _canvas.Notify(result.Message);
                    break;
            }
            SyncCanvas();
        }

        /// <summary>Town NPC conversation: dialogue, shop, selling, and the NPC's service.</summary>
        private void OpenNpcScreen(TownNpc npc, string status)
        {
            if (_game is null)
                return;
            var game = _game;
            if (npc.Kind == TownNpcKind.StationAi && !npc.IsBound)
            {
                var talking = string.IsNullOrWhiteSpace(status) || status == game.GetNpcDialogue(npc);
                OpenStationAiScreen(npc, status: talking ? null : status, consult: talking);
                return;
            }
            var entries = new List<MenuEntry>
            {
                new("Shop", () => OpenShopScreen(npc, string.Empty)),
                new("Sell held item", () =>
                {
                    var slot = game.Player.Inventory.SelectedSlot;
                    var earned = game.Sell(npc, slot);
                    OpenNpcScreen(npc, earned > 0 ? $"Sold for {earned} credits." : "They won't buy that.");
                })
            };
            if (npc.Kind == TownNpcKind.Medic)
                entries.Add(new MenuEntry("Treat my wounds", () => OpenNpcScreen(npc, game.HealAtMedic(npc) ? "Patched up." : "Nothing to treat, or not enough credits.")));
            if (npc.Kind == TownNpcKind.PartyPlanner)
                entries.Add(new MenuEntry(game.IsColonyPartyActive ? $"Party in full swing ({game.ColonyPartyHoursLeft:0.0}h left)" : "Throw a colony party",
                    () => OpenNpcScreen(npc, game.IsColonyPartyActive ? "Already partying!"
                        : game.CanStartColonyParty(out var reason) && game.StartColonyParty() ? "Party time! Everyone's spirits lift." : reason)));
            if (npc.Kind == TownNpcKind.TideWarden)
                entries.Add(new MenuEntry($"Turn in quest fish ({game.Items[game.TodaysQuestFish].Name})",
                    () => OpenNpcScreen(npc, game.TurnInQuestFish(npc) ? "A fine catch. Here is your reward." : "You haven't caught today's fish.")));
            entries.Add(new MenuEntry($"Happiness {npc.Happiness:+0.0;-0.0;0}   ·   prices ×{game.GetPriceMultiplier(npc):0.00}", Enabled: false));
            entries.Add(new MenuEntry("Goodbye", CloseMenu));
            var screen = new MenuScreen("npc-talk", $"{npc.Name} the {npc.Definition.Role}", entries,
                "Enter  choose      ↑ ↓  move      Esc  leave",
                body: string.IsNullOrWhiteSpace(status) ? $"“{game.GetNpcDialogue(npc)}”" : status);
            _down.Clear();
            _menu.ShowDirect(screen, CloseMenu);
            _menuOpen = true;
            UpdateMenuViewport();
            SyncCanvas();
        }

        private void OpenShopScreen(TownNpc npc, string status)
        {
            if (_game is null)
                return;
            var game = _game;
            var credits = game.Player.Inventory.Count(ItemId.Credits);
            var entries = new List<MenuEntry> { new("— Buy —", Enabled: false) };
            entries.AddRange(game.GetShop(npc).Select(offer => new MenuEntry(
                $"{offer.Name}: {offer.Price} credits",
                () => OpenShopScreen(npc, game.Buy(npc, offer.Item) ? $"Bought {offer.Name}." : "Not enough credits or no room."),
                Enabled: credits >= offer.Price)));
            // Terraria's selling: any carried stack (favorites stay), at the price shown; then the buyback list.
            var sellable = Enumerable.Range(0, game.Player.Inventory.SlotCount)
                .Where(slot => !game.Player.Inventory.Slots[slot].IsEmpty && !game.Player.Inventory.IsFavorite(slot) &&
                               game.GetSellPrice(npc, game.Player.Inventory.Slots[slot]) > 0)
                .ToList();
            if (sellable.Count > 0)
                entries.Add(new MenuEntry("— Sell —", Enabled: false));
            foreach (var slot in sellable)
            {
                var stack = game.Player.Inventory.Slots[slot];
                var price = game.GetSellPrice(npc, stack) * stack.Count;
                var name = stack.Count > 1 ? $"{game.Items[stack.ItemId].Name} ×{stack.Count}" : game.Items[stack.ItemId].Name;
                var sellSlot = slot;
                entries.Add(new MenuEntry($"{name}: sell for {price} credits", () =>
                {
                    var earned = game.Sell(npc, sellSlot);
                    OpenShopScreen(npc, earned > 0 ? $"Sold {name} for {earned} credits." : "They won't buy that.");
                }));
            }
            if (game.Buyback.Count > 0)
                entries.Add(new MenuEntry("— Buy back —", Enabled: false));
            for (var index = 0; index < game.Buyback.Count; index++)
            {
                var sold = game.Buyback[index];
                var name = sold.Stack.Count > 1 ? $"{game.Items[sold.Stack.ItemId].Name} ×{sold.Stack.Count}" : game.Items[sold.Stack.ItemId].Name;
                var buybackIndex = index;
                entries.Add(new MenuEntry($"{name}: buy back for {sold.Price} credits",
                    () => OpenShopScreen(npc, game.BuyBack(buybackIndex) ? $"Bought back {name}." : "Not enough credits, or no room."),
                    Enabled: credits >= sold.Price));
            }
            entries.Add(new MenuEntry("Back", () => OpenNpcScreen(npc, string.Empty)));
            var screen = new MenuScreen("npc-shop", $"{npc.Name}'s shop", entries,
                $"You have {credits} credits      Enter  buy      Esc  leave",
                body: string.IsNullOrWhiteSpace(status) ? null : status);
            _menu.ShowDirect(screen, CloseMenu);
            _menuOpen = true;
            UpdateMenuViewport();
            SyncCanvas();
        }

        /// <summary>The map belongs to the character: one file per world per character.</summary>
        private static string MapPath(WorldProfile world, CharacterProfile character)
        {
            var directory = Environment.GetEnvironmentVariable("RIMTERA_DATA_DIR") is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rimtera");
            return Path.Combine(directory, "maps", $"{world.Id:N}_{character.Id:N}.rmap");
        }

        private void SaveMap()
        {
            if (_canvas.Map is not { } map || _activeWorld is null || _activeCharacter is null)
                return;
            try
            {
                map.Save(MapPath(_activeWorld, _activeCharacter));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _canvas.Notify($"Map not saved: {error.Message}");
            }
        }

        private bool SaveGame(bool showStatus = true)
        {
            var saved = TrySaveGame(out var error);
            if (showStatus)
                _menu.SetStatus(saved ? $"Saved {_activeWorld?.Name}." : $"Save failed: {error}");
            return saved;
        }

        private bool TrySaveGame(out string? error)
        {
            error = null;
            if (_game is null || _activeWorld is null)
            {
                error = "no active world";
                return false;
            }
            try
            {
                _menu.Saves.Save(
                    _activeWorld.Id,
                    _game.CaptureSnapshot(binaryCells: true),
                    _activeCharacter?.Id,
                    _activeCharacter?.Name);
                SaveMap();
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }
        }

        private void UpdateMenuViewport()
        {
            var width = _canvas.Width > 0 ? _canvas.Width : ClientSize.Width;
            var height = _canvas.Height > 0 ? _canvas.Height : ClientSize.Height;
            _menu.Ui.SetViewport(width, height);
        }

        private void SyncCanvas()
        {
            _canvas.Game = _game;
            _canvas.KeyBindings = _menu.InputProfile;
            _canvas.Transition = _transition;
            _canvas.MenuOpen = _menuOpen;
            _canvas.ActiveWorldName = _activeWorld?.Name;
            _canvas.ActiveCharacterName = _activeCharacter?.Name;
            _canvas.Invalidate();
        }

        /// <summary>Loads the most recently played saved world with the character it was saved with.</summary>
        private Action<GameSimulation>? _pendingKit;

        private void ContinueLastWorld()
        {
            var world = _menu.Profiles.Worlds.Where(candidate => _menu.Saves.Exists(candidate.Id))
                .OrderByDescending(candidate => candidate.LastPlayed).FirstOrDefault();
            if (world is null)
            {
                _menu.SetStatus("No saved world to continue.");
                return;
            }
            var savedCharacter = _menu.Saves.TryReadFile(world.Id, out var file) ? file?.CharacterId : null;
            var character = _menu.Profiles.Characters.FirstOrDefault(candidate => candidate.Id == savedCharacter)
                            ?? _menu.Profiles.Characters.FirstOrDefault();
            if (character is null)
            {
                _menu.SetStatus("No character to continue with.");
                return;
            }
            StartWorld(world, character);
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            _audio.Dispose();
            Application.Idle -= OnIdle;
            timeEndPeriod(1);
            if (_game is not null && _activeWorld is not null && _menu.Preferences.AutoSave)
                SaveGame(showStatus: false);
        }
    }

    /// <summary>Seconds since start on the high-resolution clock: smooth animation and camera time (TickCount64 only moves every 10-16 ms).</summary>
    internal static class FrameClock
    {
        private static readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();
        public static double Seconds => Watch.Elapsed.TotalSeconds;
    }

    private sealed partial class GameForm
    {
        /// <summary>One simulation step: the game is tuned for 60 a second.</summary>
        private const double StepSeconds = 1.0 / 60.0;
        /// <summary>Steps run back to back at most when catching up (frame skip On), so a long stall can't snowball.</summary>
        private const int MaximumCatchUpSteps = 4;
        private double _loopTime = -1;
        private double _stepDebt;

        /// <summary>
        /// The game loop. A Forms timer at 16 ms really fires every ~31 ms and one step per tick ran the world at about
        /// half speed and stuttered; instead, whenever no window messages wait, this runs as many fixed 60 Hz steps as
        /// the precise clock says are due (each step marks the canvas for repaint, which the message pump then draws)
        /// and otherwise sleeps a millisecond.
        /// </summary>
        private void OnIdle(object? sender, EventArgs e)
        {
            while (!IsDisposed && !PeekMessage(out _, IntPtr.Zero, 0, 0, 0))
            {
                var now = FrameClock.Seconds;
                if (_loopTime < 0)
                    _loopTime = now;
                _stepDebt += Math.Min(0.25, now - _loopTime);
                _loopTime = now;
                var steps = 0;
                var catchUp = _menu.Preferences.FrameSkip switch { FrameSkipMode.Off => 1, FrameSkipMode.Subtle => 2, _ => MaximumCatchUpSteps };
                while (_stepDebt >= StepSeconds && steps < catchUp)
                {
                    Advance();
                    _stepDebt -= StepSeconds;
                    steps++;
                }
                // Frame skip off (or a stall past the catch-up limit): the world slows down rather than jumping ahead.
                if (steps == catchUp)
                    _stepDebt = Math.Min(_stepDebt, StepSeconds);
                if (steps == 0)
                    Thread.Sleep(1);
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr Handle;
            public uint Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public Point Location;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax, uint remove);

        [System.Runtime.InteropServices.DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint milliseconds);

        [System.Runtime.InteropServices.DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint milliseconds);
    }

    private sealed partial class GameCanvas : Control
    {
        private static readonly Pen CrackPen = new(Color.FromArgb(170, 20, 16, 12), 1.5f);

        /// <summary>Crack lines over a partly mined tile: more appear as <paramref name="progress"/> (0–1) rises.</summary>
        private static void DrawTileCracks(Graphics g, Rectangle rect, float progress)
        {
            var stage = Math.Clamp((int)(progress * 4f) + 1, 1, 4);
            float X(float f) => rect.X + rect.Width * f;
            float Y(float f) => rect.Y + rect.Height * f;
            g.DrawLine(CrackPen, X(0.5f), Y(0.1f), X(0.45f), Y(0.55f));
            if (stage >= 2) g.DrawLine(CrackPen, X(0.45f), Y(0.55f), X(0.15f), Y(0.8f));
            if (stage >= 3) g.DrawLine(CrackPen, X(0.45f), Y(0.55f), X(0.85f), Y(0.7f));
            if (stage >= 4) g.DrawLine(CrackPen, X(0.48f), Y(0.3f), X(0.85f), Y(0.15f));
        }

        /// <summary>World tile size in pixels, from the Zoom option (20 px at 100%).</summary>
        private int TileSize => Math.Clamp((int)MathF.Round(20f * _menu.Preferences.Zoom), 12, 40);
        // The world fills the window; HUD elements float over it (hotbar top-left, vitals and map top-right).
        private const int HudHeight = 0;
        private const int VitalsHeight = 82;
        private const int HotbarTop = 26;
        private const int HotbarSlot = 44;
        private readonly MenuFlow _menu;
        // Neo-Terra type: Bahnschrift for interface text, Cascadia Mono for HUD numbers.
        private readonly Font _hud = TechFont("Cascadia Mono SemiBold", "Consolas", 11f, FontStyle.Bold);
        private readonly Font _small = TechFont("Cascadia Mono", "Consolas", 9f, FontStyle.Regular);
        private readonly Font _menuFont = TechFont("Bahnschrift SemiCondensed", "Segoe UI", 12f, FontStyle.Regular);
        private readonly Font _menuTitleFont = TechFont("Bahnschrift SemiBold", "Segoe UI Semibold", 21f, FontStyle.Regular);
        private readonly Font _menuSmallFont = TechFont("Bahnschrift SemiCondensed", "Segoe UI", 9.5f, FontStyle.Regular);
        private readonly Font _mainMenuFont = TechFont("Bahnschrift SemiBold", "Segoe UI Semibold", 17f, FontStyle.Regular);
        private readonly Font _mainMenuActiveFont = TechFont("Bahnschrift SemiBold", "Segoe UI Semibold", 21f, FontStyle.Regular);

        /// <summary>Creates the preferred font, or the fallback family when it is not installed.</summary>
        private static Font TechFont(string family, string fallback, float size, FontStyle style)
        {
            using var probe = new Font(family, size, style);
            return string.Equals(probe.Name, family, StringComparison.OrdinalIgnoreCase)
                ? new Font(family, size, style)
                : new Font(fallback, size, style);
        }

        private static FontFamily LogoFamily()
        {
            try { return new FontFamily("Bahnschrift SemiBold Condensed"); }
            catch (ArgumentException) { return new FontFamily("Segoe UI Black"); }
        }
        private readonly Dictionary<TileId, Brush> _tileBrushes = new();
        private readonly Brush _playerBrush = new SolidBrush(Color.FromArgb(80, 220, 255));
        private readonly Brush _guideBrush = new SolidBrush(Color.FromArgb(255, 220, 90));
        private readonly Brush _enemyBrush = new SolidBrush(Color.FromArgb(230, 80, 80));
        private readonly Brush _bossBrush = new SolidBrush(Color.FromArgb(255, 120, 20));
        private readonly Brush _projectileBrush = new SolidBrush(Color.White);
        private readonly Brush _waterBrush = new SolidBrush(Color.FromArgb(160, 50, 120, 220));
        private readonly Brush _lavaBrush = new SolidBrush(Color.FromArgb(200, 230, 80, 30));
        private readonly Brush _fuelBrush = new SolidBrush(Color.FromArgb(190, 225, 170, 40));
        private readonly Brush _hudBrush = new SolidBrush(Color.White);
        private readonly Brush _hudBack = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
        private readonly Brush _menuDim = new SolidBrush(Color.FromArgb(150, 6, 8, 12));
        /// <summary>The inventory dims the world less, as in Terraria, where it opens over the game.</summary>
        private bool _pausedLight;
        private readonly Brush _menuPanel = new SolidBrush(Color.FromArgb(238, 22, 28, 46));
        private readonly Brush _menuPanelBorder = new SolidBrush(Color.FromArgb(150, 98, 118, 178));
        private readonly Brush _menuButton = new SolidBrush(Color.FromArgb(238, 46, 52, 60));
        private readonly Brush _menuButtonHot = new SolidBrush(Color.FromArgb(245, 70, 80, 92));
        private readonly Brush _menuButtonFocus = new SolidBrush(Color.FromArgb(250, 92, 76, 40));
        private readonly Brush _menuSlot = new SolidBrush(Color.FromArgb(235, 28, 32, 38));
        private readonly Pen _menuFocusPen = new(Color.FromArgb(255, 242, 176, 64), 2f);
        private readonly Pen _menuBorderPen = new(Color.FromArgb(170, 96, 106, 118), 1f);
        private readonly Pen _selectedSlotPen = new(Color.FromArgb(255, 242, 176, 64), 2f);
        private readonly Pen _panelEdgePen = new(Color.FromArgb(200, 92, 100, 112), 2f);
        private readonly Pen _boltPen = new(Color.FromArgb(240, 90, 230, 255), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        private readonly Pen _enemyBoltPen = new(Color.FromArgb(240, 255, 80, 140), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        private readonly Brush _rivetBrush = new SolidBrush(Color.FromArgb(150, 120, 128, 140));
        private readonly Brush _amberBrush = new SolidBrush(Color.FromArgb(242, 176, 64));
        private readonly Brush _dimTextBrush = new SolidBrush(Color.FromArgb(150, 160, 172));
        private readonly Brush _barBack = new SolidBrush(Color.FromArgb(200, 14, 16, 20));
        private readonly Brush _healthBar = new SolidBrush(Color.FromArgb(222, 70, 70));
        private readonly Brush _bossBar = new SolidBrush(Color.FromArgb(186, 92, 232));
        private readonly Brush _foodBar = new SolidBrush(Color.FromArgb(232, 196, 60));
        private readonly Brush _restBar = new SolidBrush(Color.FromArgb(80, 170, 230));
        private readonly Brush _chargeBar = new SolidBrush(Color.FromArgb(120, 110, 255));
        private readonly Brush _caveBrush = new SolidBrush(Color.FromArgb(30, 28, 34));
        private readonly Brush _wallShade = new SolidBrush(Color.FromArgb(150, 10, 12, 18));
        private readonly SpriteAtlas _sprites = InstallItemArt(SpriteAtlas.Load());

        /// <summary>Items without their own sprite get tier-tinted art from a base sprite of their style.</summary>
        private static SpriteAtlas InstallItemArt(SpriteAtlas atlas)
        {
            var art = new ItemArt(atlas);
            SpriteCatalog.ItemArtFallback = GameCanvas.PlayerSystemsItemArt(atlas, art.Fallback);
            var known = new Dictionary<string, bool>(StringComparer.Ordinal);
            SpriteCatalog.SpriteExists = name =>
            {
                if (!known.TryGetValue(name, out var exists))
                    known[name] = exists = atlas.Find(name) is not null;
                return exists;
            };
            return atlas;
        }
        private readonly LightMap _lighting = new();
        private readonly ActorAnimator _animator = new();
        private Bitmap? _pausedWorld;
        private ItemStack? _hoveredSlotStack;
        private GameSimulation? _pausedGame;
        private double _lastWorldFrame;
        public GameCanvas(MenuFlow menu)
        {
            _menu = menu;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(12, 14, 22);
            // The game draws its own cursor (see DrawGameCursor); hide the Windows arrow over the canvas.
            Cursor = HiddenCursor;
            foreach (var tile in Enum.GetValues<TileId>())
                _tileBrushes[tile] = new SolidBrush(ColorFor(tile));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            return key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or
                Keys.Enter or Keys.Escape or Keys.Space or Keys.Tab or
                Keys.Back or Keys.Delete or Keys.Home or Keys.End || base.IsInputKey(keyData);
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public GameSimulation? Game { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool MenuOpen { get; set; } = true;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ScreenTransition? Transition { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool WorldRunsBehindMenu { get; set; }

        private int _frames;
        private long _fpsWindowStart = Environment.TickCount64;
        private int _fps;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Point? MousePosition { get; set; }

        public SmartCursor Smart { get; } = new();

        /// <summary>The player's remembered map for the running world.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public WorldMapState? Map { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool FullscreenMapOpen { get; set; }

        private PointF _mapCenter;
        private float _mapZoom = 2f; // screen pixels per tile
        private Point? _mapDragFrom;
        private Bitmap? _fullMapImage;
        private (int Revision, PointF Center, float Zoom, int W, int H) _fullMapKey;
        private int _minimapRevision = -1;
        private Point _minimapCenter;

        public void OpenFullscreenMap(GameSimulation game)
        {
            FullscreenMapOpen = true;
            var center = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
            _mapCenter = new PointF(center.X, center.Y);
        }

        public void MapMouseDown(Point point) => _mapDragFrom = point;
        public void MapMouseUp() => _mapDragFrom = null;

        public void MapMouseMove(Point point)
        {
            if (_mapDragFrom is not { } from)
                return;
            _mapCenter = new PointF(_mapCenter.X - (point.X - from.X) / _mapZoom, _mapCenter.Y - (point.Y - from.Y) / _mapZoom);
            _mapDragFrom = point;
            Invalidate();
        }

        public void MapZoom(int delta, Point at)
        {
            // Zoom around the cursor so the point under it stays put.
            var before = ScreenToMap(at);
            _mapZoom = Math.Clamp(_mapZoom * (delta > 0 ? 1.25f : 0.8f), 0.12f, 16f);
            var after = ScreenToMap(at);
            _mapCenter = new PointF(_mapCenter.X + before.X - after.X, _mapCenter.Y + before.Y - after.Y);
            Invalidate();
        }

        private PointF ScreenToMap(Point point) =>
            new(_mapCenter.X + (point.X - Width / 2f) / _mapZoom, _mapCenter.Y + (point.Y - Height / 2f) / _mapZoom);

        private PointF MapToScreen(float x, float y) =>
            new(Width / 2f + (x - _mapCenter.X) * _mapZoom, Height / 2f + (y - _mapCenter.Y) * _mapZoom);
        private static readonly Cursor HiddenCursor = CreateHiddenCursor();
        /// <summary>World position (tiles) of the view's top-left corner, including the camera's sub-tile offset.</summary>
        private float _viewStartX;
        private float _viewStartY;
        /// <summary>Smoothed camera centre (tiles) and how quickly it closes on the player per axis (1/s).</summary>
        private Vector2 _camera;
        private bool _cameraReady;
        private const float CameraFollowX = 14f;
        private const float CameraFollowY = 9f;
        private readonly List<(RectangleF Rect, int Slot)> _hotbarHits = new();
        /// <summary>Every HUD panel drawn last frame (vitals, buffs, minimap, status, boss and event bars).</summary>
        private readonly List<RectangleF> _hudRects = new();
        private readonly List<(RectangleF Rect, string RecipeId)> _craftHits = new();
        private RectangleF _craftArea;
        private string? _craftSelected;
        private int _craftScrollRows;
        private readonly List<(string Text, long At)> _messageLog = new();
        private string? _lastLoggedMessage;
        private Bitmap? _minimap;
        private readonly Dictionary<TileId, Color> _tileColors = new();

        /// <summary>World tile under the mouse, if the cursor is over the play area.</summary>
        public GridPoint? CursorTile()
        {
            if (Game is null || MousePosition is not { } mouse || HotbarSlotAt(mouse) is not null)
                return null;
            var x = (int)MathF.Floor(_viewStartX + mouse.X / (float)TileSize);
            var y = (int)MathF.Floor(_viewStartY + mouse.Y / (float)TileSize);
            return x >= 0 && y >= 0 && x < Game.World.Width && y < Game.World.Height ? new GridPoint(x, y) : null;
        }

        /// <summary>
        /// True over any HUD element (hotbar, vitals, buffs, minimap, status panel, boss and event bars). Terraria sets
        /// <c>mouseInterface</c> over all of these, so a click there never uses the held item.
        /// </summary>
        public bool IsOverHud(Point point) => !MenuOpen && !HideUi && (HotbarSlotAt(point) is not null || _hudRects.Any(rect => rect.Contains(point)));

        /// <summary>F11: hides the whole HUD, as Terraria's <c>Main.hideUI</c>.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool HideUi { get; set; }

        /// <summary>Minimap and overlay opacity (PageUp / PageDown, Terraria's MapAlphaUp / MapAlphaDown).</summary>
        public float MinimapOpacity { get; private set; } = 1f;
        public float OverlayOpacity { get; private set; } = 0.55f;

        /// <summary>Changes the opacity of whichever map is showing by <paramref name="step"/> (Terraria: ±0.015 a tick held).</summary>
        public void AdjustMapOpacity(float step)
        {
            if (MapView == MapStyle.Overlay)
                OverlayOpacity = Math.Clamp(OverlayOpacity + step, 0.05f, 1f);
            else if (MapView == MapStyle.Minimap)
                MinimapOpacity = Math.Clamp(MinimapOpacity + step, 0.05f, 1f);
        }

        /// <summary>Pans the fullscreen map by screen pixels (WASD while the map is open, as in Terraria).</summary>
        public void PanFullscreenMap(float dx, float dy)
        {
            _mapCenter = new PointF(_mapCenter.X + dx / _mapZoom, _mapCenter.Y + dy / _mapZoom);
            Invalidate();
        }

        /// <summary>Zooms the fullscreen map about its centre by <paramref name="factor"/> (the map zoom keys, held).</summary>
        public void ZoomFullscreenMap(float factor)
        {
            _mapZoom = Math.Clamp(_mapZoom * factor, 0.12f, 16f);
            Invalidate();
        }

        public int? HotbarSlotAt(Point point)
        {
            foreach (var (rect, slot) in _hotbarHits)
                if (rect.Contains(point))
                    return slot;
            return null;
        }

        /// <summary>
        /// A click on the crafting grid, Terraria-style: an unselected result becomes the selection (and shows its
        /// materials); the selected one is crafted, returned in <paramref name="craftRecipeId"/>. True when the click
        /// landed on the grid.
        /// </summary>
        public bool TryCraftClick(Point point, out string? craftRecipeId)
        {
            craftRecipeId = null;
            foreach (var (rect, id) in _craftHits)
            {
                if (!rect.Contains(point))
                    continue;
                if (id == _craftSelected)
                    craftRecipeId = id;
                else
                    _craftSelected = id;
                Invalidate();
                return true;
            }
            return false;
        }

        /// <summary>True over the crafting panel, which the canvas draws beside the inventory's UI tree.</summary>
        public bool IsOverCraftingPanel(Point point) => _craftArea.Contains(point) || _craftHits.Any(hit => hit.Rect.Contains(point)) ||
                                                        _guideArea.Contains(point) || _guideHits.Any(hit => hit.Rect.Contains(point));

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? ActiveWorldName { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? ActiveCharacterName { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            _profiler.Begin();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.Clear(Color.FromArgb(12, 14, 22));

            if (Game is { } game && MenuOpen && Width > 0 && Height > 0)
            {
                // Under a menu the world is drawn into an opaque image with the dim baked in; the opaque
                // format stops the overlay exposing partly transparent seams. A paused world renders once
                // and is reused; with Autopause off it re-renders every frame.
                var light = _menu.Ui.CurrentState is InventoryUiState;
                if (WorldRunsBehindMenu || _pausedWorld is null || _pausedWorld.Width != Width || _pausedWorld.Height != Height || !ReferenceEquals(_pausedGame, game) ||
                    light != _pausedLight)
                {
                    if (_pausedWorld is null || _pausedWorld.Width != Width || _pausedWorld.Height != Height)
                    {
                        _pausedWorld?.Dispose();
                        _pausedWorld = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
                    }
                    _pausedGame = game;
                    using var world = Graphics.FromImage(_pausedWorld);
                    world.InterpolationMode = InterpolationMode.NearestNeighbor;
                    world.SmoothingMode = g.SmoothingMode;
                    world.Clear(Color.FromArgb(12, 14, 22));
                    DrawWorld(world, game);
                    // The dim is baked in so the screen draws one opaque image under the menu.
                    // Terraria doesn't dim the world behind the inventory, which is played through.
                    if (!light)
                        world.FillRectangle(_menuDim, 0, 0, Width, Height);
                    _pausedLight = light;
                }
                g.DrawImageUnscaled(_pausedWorld, 0, 0);
            }
            else if (Game is { } running)
            {
                _pausedWorld?.Dispose();
                _pausedWorld = null;
                DrawWorld(g, running);
            }
            else
                DrawTitleBackdrop(g);

            if (MenuOpen)
            {
                // The title panorama stays bright; menus over a running world dim it.
                if (Game is null && _menu.Ui.CurrentState?.Id != "main-menu")
                    g.FillRectangle(_menuDim, ClientRectangle);
                _menu.Ui.SetViewport(Width, Height);
                _hoveredSlotStack = null;
                DrawMenu(g);
                if (Game is { } running && _menu.Ui.CurrentState is InventoryUiState inventoryScreen)
                    DrawCraftingPanel(g, running, inventoryScreen);
                if (Game is { } inspecting && _hoveredSlotStack is { } hoveredStack && MousePosition is { } hoverAt &&
                    _menu.Ui.CurrentState is InventoryUiState { Controller.CursorStack.IsEmpty: true })
                    DrawItemTooltip(g, inspecting.Items[hoveredStack.ItemId], hoveredStack.Count, hoverAt);
                if (_menu.Ui.CurrentState?.Id != "main-menu")
                    DrawScreenFade(g, _menu.Ui.CurrentState?.Id);
            }

            if (FullscreenMapOpen && Game is { } mapped && !MenuOpen)
                DrawFullscreenMap(g, mapped);

            if (Transition is { IsActive: true } transition)
                DrawTransition(g, transition);
            DrawGameCursor(g);

            _frames++;
            if (Environment.TickCount64 - _fpsWindowStart >= 1000)
            {
                _fps = _frames;
                _frames = 0;
                _fpsWindowStart = Environment.TickCount64;
            }
            if (_menu.Preferences.ShowFrameRate)
            {
                _profiler.Mark("menus");
                // F10: the frame rate, the window's size in pixels, and where the drawing time goes (ms per frame).
                var report = $"{_fps} FPS   {Width}×{Height} px at {DeviceDpi} dpi   drawing {_profiler.Total:0.0} ms\n{_profiler.Describe()}";
                var size = g.MeasureString(report, _small);
                using var back = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
                g.FillRectangle(back, 2, 2, size.Width + 8, size.Height + 4);
                g.DrawString(report, _small, _amberBrush, 6, 4);
            }
        }

        private void DrawTransition(Graphics g, ScreenTransition transition)
        {
            if (transition.ShowsProgress)
            {
                // Working screens sit on a biome painting (or the title scenery), dimmed, with two bars.
                string[] scenes = ["scene_acid_coast", "scene_fungal_hollows", "scene_crystal_spires", "scene_verdant_canopy", "scene_precursor_vault"];
                var scene = scenes[Math.Abs(transition.Title.GetHashCode()) % scenes.Length];
                if (_sprites.Get(scene) is { } painting)
                {
                    var previousMode = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    var scale = Math.Max(Width / (float)painting.Width, Height / (float)painting.Height);
                    var w = painting.Width * scale;
                    var h = painting.Height * scale;
                    g.DrawImage(painting, (Width - w) / 2f, (Height - h) / 2f, w, h);
                    g.InterpolationMode = previousMode;
                }
                else
                    DrawTitleBackdrop(g);
                using (var dim = new SolidBrush(Color.FromArgb(150, 4, 6, 10)))
                    g.FillRectangle(dim, ClientRectangle);
                var panel = new RectangleF(Width / 2f - 300, Height / 2f - 90, 600, 180);
                DrawRivetedPanel(g, panel);
                DrawCentered(g, transition.Title, new RectangleF(panel.X, panel.Y + 14, panel.Width, 36), _menuTitleFont, _amberBrush);
                DrawCentered(g, transition.Message, new RectangleF(panel.X, panel.Y + 54, panel.Width, 22), _menuFont, Brushes.Gainsboro);
                var overall = new RectangleF(panel.X + 40, panel.Y + 92, panel.Width - 80, 22);
                var pass = new RectangleF(panel.X + 120, panel.Y + 124, panel.Width - 240, 12);
                g.FillRectangle(_barBack, overall);
                g.FillRectangle(_foodBar, overall.X, overall.Y, overall.Width * Math.Clamp(transition.VisualTotal, 0f, 1f), overall.Height);
                g.DrawRectangle(_panelEdgePen, overall.X, overall.Y, overall.Width, overall.Height);
                DrawCentered(g, $"{MathF.Round(Math.Clamp(transition.VisualTotal, 0f, 1f) * 100f):0}%", overall, _small, Brushes.Black);
                g.FillRectangle(_barBack, pass);
                g.FillRectangle(_restBar, pass.X, pass.Y, pass.Width * Math.Clamp(transition.VisualPass, 0f, 1f), pass.Height);
                g.DrawRectangle(_menuBorderPen, pass.X, pass.Y, pass.Width, pass.Height);
                return;
            }

            var alpha = (int)(255 * transition.BlackoutAlpha);
            if (transition.Kind == TransitionKind.Respawning)
            {
                using var tint = new SolidBrush(Color.FromArgb(alpha, 40, 4, 8));
                g.FillRectangle(tint, ClientRectangle);
                var box = new RectangleF(0, Height / 2f - 70, Width, 140);
                DrawCentered(g, transition.Title, new RectangleF(box.X, box.Y, box.Width, 44), _menuTitleFont, _healthBar);
                DrawCentered(g, transition.Message, new RectangleF(box.X, box.Y + 48, box.Width, 24), _menuFont, Brushes.Gainsboro);
                if (transition.CountdownSeconds > 0f)
                    DrawCentered(g, $"Respawning in {MathF.Ceiling(transition.CountdownSeconds):0}",
                        new RectangleF(box.X, box.Y + 80, box.Width, 24), _menuFont, _amberBrush);
                return;
            }

            if (alpha > 0)
            {
                using var black = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
                g.FillRectangle(black, ClientRectangle);
            }
        }

        /// <summary>
        /// Scrolling title panorama: parallax sky, mountains and a terrain strip that cycles through
        /// the biomes, with a walking colonist and a passing hawk. Purely presentational.
        /// </summary>
        private void DrawTitleBackdrop(Graphics g)
        {
            var width = Math.Max(1, Width);
            var height = Math.Max(1, Height);
            var time = FrameClock.Seconds * Math.Max(0.05, _menu.Preferences.BackgroundParallax);
            // Slow day/night cycle (about 80 s), mostly night so the logo reads well.
            var cycle = (Math.Sin(time * Math.PI * 2 / 80.0) + 1) / 2;
            var night = (float)Math.Clamp(0.35 + cycle * 0.75, 0, 1);
            DrawSky(g, width, height, night);

            using (var planet = new SolidBrush(Color.FromArgb(200, 64, 46, 98)))
                g.FillEllipse(planet, width - 300, 50, 220, 220);
            using (var ring = new Pen(Color.FromArgb(150, 150, 120, 200), 3f))
                g.DrawEllipse(ring, width - 340, 140, 300, 40);
            if (_sprites.GetFitted("astro_god", width / 3, height / 2) is { } god)
            {
                using var faint = new ImageAttributes();
                faint.SetColorMatrix(new ColorMatrix { Matrix33 = 0.16f + 0.08f * night });
                g.DrawImage(god, new Rectangle(width / 10, 24, god.Width, god.Height), 0, 0, god.Width, god.Height, GraphicsUnit.Pixel, faint);
            }

            DrawMountainLayer(g, width, height, time * 6, height * 0.56f, 70f, Color.FromArgb(150, 34, 42, 66), 0.006);
            DrawMountainLayer(g, width, height, time * 14, height * 0.66f, 55f, Color.FromArgb(200, 22, 28, 40), 0.011);

            // Terrain strip in world space; each biome stretch is 48 tiles wide.
            const int tile = 32;
            var scroll = time * 28;
            var firstColumn = (int)Math.Floor(scroll / tile);
            var offset = (int)(scroll - firstColumn * tile);
            var groundBase = (int)(height * 0.78f);
            var surfaces = new Dictionary<int, int>();
            for (var screenColumn = -1; screenColumn <= width / tile + 1; screenColumn++)
            {
                var column = firstColumn + screenColumn;
                var x = screenColumn * tile - offset;
                var top = groundBase + (int)(Math.Sin(column * 0.21) * 18 + Math.Sin(column * 0.057) * 30);
                top -= top % 4;
                surfaces[screenColumn] = top;
                var (surface, under) = TitleBiome(column);
                DrawTileSprite(g, SpriteCatalog.ForTile(surface), surface, new Rectangle(x, top, tile, tile), tile);
                for (var y = top + tile; y < height; y += tile)
                {
                    var deep = y > top + tile * 2;
                    DrawTileSprite(g, deep ? "terrain_rock" : SpriteCatalog.ForTile(under), under, new Rectangle(x, y, tile, tile), tile);
                }

                // Deterministic props per world column.
                var hash = unchecked((uint)column * 2654435761u);
                if (hash % 7 == 0 && SpriteCatalog.DecorationsFor(surface) is { } options)
                {
                    var sprite = options[(int)(hash / 7 % (uint)options.Length)];
                    DrawPropOnGround(g, sprite, x + tile / 2f, top, tile * 2, (int)(SpriteCatalog.DecorationHeight(sprite) * tile), (hash & 8) == 0);
                }
                if (column % 97 == 40)
                    DrawPropOnGround(g, "communal_pod", x + tile / 2f, top, 190, 140);
                else if (column % 97 == 46)
                    DrawPropOnGround(g, "solar_panel", x + tile / 2f, top, 80, 80);
            }

            // A colonist walking right at a fixed screen position, and a hawk crossing the sky.
            var walkerColumn = (int)(width * 0.36f / tile);
            if (surfaces.TryGetValue(walkerColumn, out var walkerGround))
            {
                var animated = _sprites.Get("anim_walk_1") is not null;
                var frame = animated ? $"anim_walk_{(int)(time * 9) % 8 + 1}"
                    : ((int)(time * 6) & 1) == 0 ? "colonist_side_a" : "colonist_side_b";
                DrawPropOnGround(g, frame, width * 0.36f, walkerGround, 70, 96, flip: !animated);
            }
            var hawkX = (float)(width + 200 - (time * 90 % (width + 400)));
            var hawkY = (float)(height * 0.28 + Math.Sin(time * 1.7) * 18);
            if (_sprites.GetFitted("sky_hawk", 70, 50) is { } hawk)
                g.DrawImageUnscaled(hawk, (int)hawkX, (int)hawkY);

            if (night > 0.4f)
            {
                using var shade = new SolidBrush(Color.FromArgb((int)(night * 90), 4, 6, 20));
                g.FillRectangle(shade, 0, 0, width, height);
            }
        }

        private static (TileId Surface, TileId Under) TitleBiome(int column)
        {
            var stretch = ((column / 48) % 5 + 5) % 5;
            return stretch switch
            {
                1 => (TileId.Snow, TileId.Snow),
                2 => (TileId.Grass, TileId.Soil),
                3 => (TileId.DesertSand, TileId.DesertSand),
                4 => (TileId.CrimsonSoil, TileId.CorruptRock),
                _ => (TileId.Grass, TileId.Soil)
            };
        }

        private static void DrawMountainLayer(Graphics g, int width, int height, double scroll, float baseline, float amplitude, Color color, double frequency)
        {
            var points = new List<PointF> { new(0, height) };
            for (var x = 0; x <= width; x += 16)
            {
                var worldX = x + scroll;
                var y = baseline - (float)(Math.Abs(Math.Sin(worldX * frequency)) * amplitude + Math.Sin(worldX * frequency * 3.1) * amplitude * 0.25);
                points.Add(new PointF(x, y));
            }
            points.Add(new PointF(width, height));
            using var brush = new SolidBrush(color);
            g.FillPolygon(brush, points.ToArray());
        }

        private void DrawPropOnGround(Graphics g, string sprite, float centerX, int groundY, int maxWidth, int maxHeight, bool flip = false)
        {
            if (_sprites.GetFitted(sprite, maxWidth, maxHeight, flip) is { } bitmap)
                g.DrawImageUnscaled(bitmap, (int)(centerX - bitmap.Width / 2f), groundY - bitmap.Height + 4);
        }

        private void DrawSky(Graphics g, int width, int height, float nightFactor)
        {
            if (!_menu.Preferences.BackgroundEnabled)
            {
                using var plain = new SolidBrush(Blend(Color.FromArgb(70, 100, 130), Color.FromArgb(10, 12, 20), nightFactor));
                g.FillRectangle(plain, 0, 0, width, height);
                return;
            }
            var top = Blend(Color.FromArgb(86, 128, 168), Color.FromArgb(8, 10, 22), nightFactor);
            var bottom = Blend(Color.FromArgb(196, 150, 120), Color.FromArgb(46, 24, 62), nightFactor);
            using var sky = new LinearGradientBrush(new Rectangle(0, 0, width, height), top, bottom, 90f);
            g.FillRectangle(sky, 0, 0, width, height);
            if (nightFactor <= 0.15f)
                return;
            for (var i = 0; i < 110; i++)
            {
                var x = (i * 137 + 41) % width;
                var y = (i * 83 + 29) % Math.Max(1, height * 2 / 3);
                var alpha = (int)((80 + (i * 29) % 150) * nightFactor);
                using var star = new SolidBrush(Color.FromArgb(alpha, 220, 230, 255));
                var size = i % 7 == 0 ? 2.4f : 1.3f;
                g.FillRectangle(star, x, y, size, size);
            }
        }

        private static Color? BiomeSkyTint(BiomeZone zone) => zone switch
        {
            BiomeZone.FrozenWasteland => Color.FromArgb(40, 190, 225, 255),
            BiomeZone.JunkWasteland => Color.FromArgb(40, 170, 110, 60),
            BiomeZone.RadiatedDeeps => Color.FromArgb(50, 60, 200, 80),
            BiomeZone.MoltenChasm => Color.FromArgb(55, 230, 80, 30),
            _ => null
        };

        private static Color Blend(Color day, Color night, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return Color.FromArgb(
                (int)(day.R + (night.R - day.R) * t),
                (int)(day.G + (night.G - day.G) * t),
                (int)(day.B + (night.B - day.B) * t));
        }

        private static float NightFactor(float hour)
        {
            // 0 at midday, 1 between 21h and 4h, smooth ramps at dusk and dawn.
            if (hour >= 7f && hour <= 18f)
                return 0f;
            if (hour > 18f && hour < 21f)
                return (hour - 18f) / 3f;
            if (hour > 4f && hour < 7f)
                return 1f - (hour - 4f) / 3f;
            return 1f;
        }

        private void DrawTileSprite(Graphics g, string? sprite, TileId tile, Rectangle rect, int size)
        {
            if (_sprites.GetScaled(sprite, size, size) is { } bitmap)
                g.DrawImageUnscaled(bitmap, rect.X, rect.Y);
            else
                g.FillRectangle(_tileBrushes[tile], rect);
        }

        private void DrawWorld(Graphics g, GameSimulation game)
        {
            var motion = game.Player.Motion;
            var night = NightFactor(game.ClockHour);
            var now = FrameClock.Seconds;
            var frameDelta = _lastWorldFrame > 0 ? (float)Math.Clamp(now - _lastWorldFrame, 0, 0.1) : 1f / 60f;
            _lastWorldFrame = now;

            // Smooth camera: the centre eases toward the player (frame-rate independent) instead of snapping, and the
            // view scrolls by whole pixels inside a tile rather than jumping a tile at a time. Teleports snap.
            var target = motion.Position + motion.Size / 2f;
            if (!_cameraReady || Vector2.Distance(_camera, target) > 24f)
                _camera = target;
            else
                _camera += new Vector2((target.X - _camera.X) * (1f - MathF.Exp(-frameDelta * CameraFollowX)),
                    (target.Y - _camera.Y) * (1f - MathF.Exp(-frameDelta * CameraFollowY)));
            _cameraReady = true;
            var screenTilesW = Width / (float)TileSize;
            var screenTilesH = (Height - HudHeight) / (float)TileSize;
            var cameraLeft = Math.Clamp(_camera.X - screenTilesW / 2f, 0f, Math.Max(0f, game.World.Width - screenTilesW));
            var cameraTop = Math.Clamp(_camera.Y - screenTilesH / 2f, 0f, Math.Max(0f, game.World.Height - screenTilesH));
            var startX = (int)MathF.Floor(cameraLeft);
            var startY = (int)MathF.Floor(cameraTop);
            var offsetX = (int)MathF.Round((cameraLeft - startX) * TileSize);
            var offsetY = (int)MathF.Round((cameraTop - startY) * TileSize);
            var viewW = Math.Max(1, Width / TileSize + 2);
            var viewH = Math.Max(1, (Height - HudHeight) / TileSize + 2);
            _viewStartX = startX + offsetX / (float)TileSize;
            _viewStartY = startY + offsetY / (float)TileSize;
            _animator.HasAnimationFrames = _sprites.Get("anim_walk_1") is not null;
            _animator.ShowDamageNumbers = _menu.Preferences.DamageNumbers;
            _animator.ParticleScale = (int)_menu.Preferences.Particles;
            _animator.Update(game, now, frameDelta);

            _profiler.Mark("setup");
            DrawBackdrop(g, game, night, now, startX, startY, viewW, viewH); // sky, clouds, parallax (GraphicalSandbox.Backdrop.cs)
            _profiler.Mark("sky");

            // Everything in tile space below is shifted by the camera's sub-tile pixel offset.
            var worldState = g.Save();
            g.TranslateTransform(-offsetX, -offsetY);

            // The static layer (backdrop, walls, blocks, plants, debris, props) comes from cached chunks
            // (GraphicalSandbox.Chunks.cs); the cells that animate or change often are drawn over it here.
            DrawTileChunks(g, game, startX, startY, viewW, viewH);
            _profiler.Mark("tiles");
            for (var row = 0; row < viewH; row++)
            for (var col = 0; col < viewW; col++)
            {
                var x = startX + col;
                var y = startY + row;
                if (x >= game.World.Width || y >= game.World.Height)
                    continue;
                var cell = game.World.GetCell(new GridPoint(x, y));
                if (cell.TileId == TileId.Air && cell.LiquidDepth <= 0.05f && cell.WallId == 0)
                    continue;
                var rect = new Rectangle(col * TileSize, row * TileSize, TileSize, TileSize);
                if (cell.TileId != TileId.Air && IsLiveTile(cell.TileId) && !DrawTravelTile(g, cell, x, y, rect, game))
                    DrawSpecialTile(g, cell, x, y, rect, game);
                if (cell.Damage > 0 && cell.TileId != TileId.Air && game.Tiles[cell.TileId].HitPoints > 0)
                    DrawTileCracks(g, rect, cell.Damage / (float)game.Tiles[cell.TileId].HitPoints); // parity B2: mining progress
                DrawLeisureCell(g, game, x, y, cell, rect); // paint tints (GraphicalSandbox.Leisure.cs)
                if (cell.LiquidDepth > 0.05f)
                {
                    var liquidHeight = Math.Max(2, (int)(TileSize * Math.Clamp(cell.LiquidDepth, 0f, 1f)));
                    var liquidSprite = cell.LiquidKind switch
                    {
                        LiquidKind.Lava => "tile_liquid_uranium", // the core liquid is uranium
                        LiquidKind.Acid => "tile_acid_water",
                        LiquidKind.Nectar => "tile_nectar",
                        LiquidKind.Phase => "tile_phase_liquid",
                        LiquidKind.Fuel => "tile_liquid_fuel", // the fuel seas at the world edges
                        _ => null
                    };
                    if (liquidSprite is not null && _sprites.GetScaled(liquidSprite, TileSize, TileSize) is { } liquidArt)
                        g.DrawImage(liquidArt, new Rectangle(rect.X, rect.Bottom - liquidHeight, rect.Width, liquidHeight),
                            0, TileSize - liquidHeight, TileSize, liquidHeight, GraphicsUnit.Pixel);
                    else
                        g.FillRectangle(cell.LiquidKind switch { LiquidKind.Lava => _lavaBrush, LiquidKind.Fuel => _fuelBrush, _ => _waterBrush },
                            rect.X, rect.Bottom - liquidHeight, rect.Width, liquidHeight);
                }
            }

            _profiler.Mark("live");
            DrawLooseObjects(g, game, startX, startY, now);

            // The station AI waits as a hologram at the landing site until it moves into a house (then it's drawn as a resident).
            if (!game.GuideMovedIn)
                DrawSpriteActor(g, "station_ai", game.Guide.Position, new Vector2(0.8f, 1.6f), 1.5f, false, startX, startY,
                    bob: (float)Math.Sin(now * 2.0) * 2f);

            foreach (var enemy in game.Enemies.Where(enemy => enemy.IsAlive))
            {
                if (enemy.Disguised)
                {
                    // A mimic waits looking exactly like a cabin chest.
                    DrawPropOnGround(g, "furn_cabin_chest", (enemy.Motion.Position.X + enemy.Motion.Size.X / 2f - startX) * TileSize,
                        (int)((enemy.Motion.Position.Y + enemy.Motion.Size.Y - startY) * TileSize), TileSize * 2, TileSize * 2);
                    continue;
                }
                DrawWormBody(g, enemy, startX, startY);
                var flip = enemy.Motion.Velocity.X < -0.05f || (MathF.Abs(enemy.Motion.Velocity.X) <= 0.05f && enemy.Motion.Facing < 0);
                var telegraph = enemy.State == EnemyState.Telegraphing && ((int)(now * 10) & 1) == 0;
                var hit = _menu.Preferences.FlashyEffects && _animator.IsFlashing(enemy.Id, now);
                DrawSpriteActor(g, SpriteCatalog.EnemyFrame(enemy, now), enemy.Motion.Position, enemy.Motion.Size,
                    SpriteCatalog.EnemyDrawScale(enemy), flip, startX, startY, bob: _animator.EnemyBob(enemy, now),
                    flash: telegraph || hit, flashWhite: hit, reference: SpriteCatalog.ForEnemy(enemy));
                if ((enemy.HitPoints < enemy.MaximumHitPoints || enemy.IsBoss) && _menu.Preferences.EnemyHealthBars != HealthBarPlacement.Off)
                    DrawHealthBar(g, enemy.Motion.Position, enemy.Motion.Size, enemy.HitPoints / enemy.MaximumHitPoints,
                        enemy.IsBoss, startX, startY);
            }

            foreach (var projectile in game.Projectiles)
            {
                // Yoyos, boomerangs, flails and thrown weapons are drawn as spinning sprites by DrawWeaponStyles.
                if (projectile.Style is WeaponStyle.Yoyo or WeaponStyle.Boomerang or WeaponStyle.Flail or WeaponStyle.Thrown)
                    continue;
                var px = (projectile.Position.X - startX) * TileSize;
                var py = (projectile.Position.Y - startY) * TileSize;
                var direction = projectile.Velocity.LengthSquared() > 0.001f ? Vector2.Normalize(projectile.Velocity) : Vector2.UnitX;
                var pen = projectile.Team == ProjectileTeam.Player ? _boltPen : _enemyBoltPen;
                g.DrawLine(pen, px, py, px - direction.X * TileSize * 0.9f, py - direction.Y * TileSize * 0.9f);
            }

            foreach (var agent in game.Colonists.Where(agent => agent.IsAlive))
                DrawCrew(g, agent, startX, startY, now);
            foreach (var npc in game.TownNpcs)
            {
                var npcFlip = npc.Motion.Velocity.X < -0.05f;
                DrawSpriteActor(g, SpriteCatalog.AnimatedFrame(npc.SpriteName, MathF.Abs(npc.Motion.Velocity.X) > 0.3f, false, now, npc.Id),
                    npc.Motion.Position, npc.Motion.Size, 1f, npcFlip, startX, startY,
                    MathF.Abs(npc.Motion.Velocity.X) > 0.3f ? (float)Math.Abs(Math.Sin(now * 9 + npc.Id)) * -2f : 0f, reference: npc.SpriteName);
                DrawNameTag(g, npc.IsBound ? $"{npc.Name} (bound)" : npc.Name, npc.Motion.Position, npc.Motion.Size, startX, startY);
            }
            foreach (var minion in game.Minions)
            {
                var size = minion.IsSentry ? 1.6f : 1.1f;
                var bob = minion.IsSentry ? 0f : (float)Math.Sin(now * 4 + minion.Id) * 0.15f;
                DrawSpriteActor(g, minion.SpriteName, minion.Position - new Vector2(size / 2f, size / 2f - bob), new Vector2(size, size),
                    1f, minion.Position.X > game.Player.Motion.Position.X, startX, startY);
            }
            DrawCompanions(g, game, startX, startY, now);
            DrawTravelMount(g, game, startX, startY, now);
            DrawPlayer(g, game, startX, startY, now);
            DrawWeaponStyles(g, game, startX, startY, now);
            DrawBreath(g, game, startX, startY);
            DrawBobber(g, game, startX, startY, now);

            _profiler.Mark("actors");
            // Lighting over tiles and actors: sun or moon from open sky, glowing tiles, lamps.
            var lamps = new List<LightMap.LampLight>
            {
                // The Lumen accord tints the colonist's own light to the biome.
                new(game.Player.Motion.Position + game.Player.Motion.Size / 2f,
                    game.LumenAccordTint is { } lumen ? Color.FromArgb(lumen.R, lumen.G, lumen.B) : Color.FromArgb(255, 236, 200), 0.85f)
            };
            lamps.AddRange(game.Colonists.Where(agent => agent.IsAlive)
                .Select(agent => new LightMap.LampLight(agent.Motion.Position + agent.Motion.Size / 2f, Color.FromArgb(255, 230, 190), 0.6f)));
            if (!game.GuideMovedIn)
                lamps.Add(new LightMap.LampLight(game.Guide.Position, Color.FromArgb(120, 230, 255), 0.7f));
            lamps.AddRange(game.PlayerLightSources.Select(light => new LightMap.LampLight(light.Position, light.Tint switch
            {
                "cyan" => Color.FromArgb(140, 230, 255),
                "amber" => Color.FromArgb(255, 200, 120),
                _ => Color.FromArgb(255, 250, 235)
            }, Math.Clamp(light.Intensity * light.Radius / 7f, 0.4f, 1.4f))));
            var lighting = _menu.Preferences.Lighting;
            _lighting.Subdivision = lighting == LightingQuality.Low ? 1 : 4;
            _lighting.ColouredGlow = lighting == LightingQuality.High;
            // Sky light follows the clock, moon phase and weather (cloud, rain, storms dim the surface).
            _lighting.Compute(game.World, game.Tiles, startX, startY, viewW, viewH, game.SkyLightLevel, lamps, present: lighting != LightingQuality.Off);
            _profiler.Mark("light calc");
            if (lighting != LightingQuality.Off)
                _lighting.Draw(g, 0, 0, TileSize);
            _profiler.Mark("light draw");
            // The map remembers whatever is lit around the player (the light map's padded window).
            Map?.RevealFromLight(game.World, _lighting, startX - LightMap.Padding, startY - LightMap.Padding,
                viewW + LightMap.Padding * 2, viewH + LightMap.Padding * 2);
            DrawGearAfterLight(g, game, _rig, now); // light halos, shield glints, detector pings (GraphicalSandbox.Gear.cs)
            _animator.DrawEffects(g, startX, startY, TileSize, now, _hud);
            DrawLeisure(g, game, startX, startY, now); // glow coating, pets, kites, golf (GraphicalSandbox.Leisure.cs)
            if (!MenuOpen)
                DrawWireOverlay(g, game, startX, startY, viewW, viewH);
            DrawWeather(g, game, startX, startY, now);
            DrawPlayerSystems(g, game, startX, startY, now);
            g.Restore(worldState);

            if (_menu.Preferences.WeatherEffects && game.ToxicFalloutActive && !game.CurrentBiome.Underground)
            {
                using var fallout = new SolidBrush(Color.FromArgb(46, 90, 255, 60));
                g.FillRectangle(fallout, 0, 0, Width, Height - HudHeight);
            }
            if (_menu.Preferences.WeatherEffects && game.SolarFlareActive)
            {
                using var flare = new SolidBrush(Color.FromArgb(34, 255, 170, 60));
                g.FillRectangle(flare, 0, 0, Width, Height - HudHeight);
            }

            _craftHits.Clear();
            _profiler.Mark("effects");
            if (MenuOpen)
            {
                _hudRects.Clear();
                return;
            }
            DrawCursorHighlight(g, game, startX, startY);
            _hudRects.Clear();
            if (HideUi)
            {
                _hotbarHits.Clear();
                _profiler.Mark("hud");
                return;
            }
            DrawHud(g, game);
            DrawBuffBar(g, game);
            if (MapView == MapStyle.Overlay)
                DrawMapOverlay(g, game);
            else if (MapView == MapStyle.Minimap && _menu.Preferences.MinimapEnabled)
                DrawMinimap(g, game);
            DrawStatusPanel(g, game);
            DrawBossBar(g, game);
            DrawEventBars(g, game);
            DrawLeisureHud(g, game); // strokes, wave-moon score, creative (GraphicalSandbox.Leisure.cs)
            if (_menu.Preferences.ShowItemText)
                DrawMessageLog(g, game);
            DrawHotbarTooltip(g, game);
            _profiler.Mark("hud");
        }

        /// <summary>Fishing line from the colonist's hand to the bobber; "!" while a fish is biting.</summary>
        private void DrawBobber(Graphics g, GameSimulation game, int startX, int startY, double now)
        {
            if (game.Bobber is not { } bobber)
                return;
            // The line leaves the rod's tip (or the hand when the rod is not drawn).
            var hand = _rodTip ?? _heldHand ?? _rig.RestHand;
            var dip = bobber.Biting ? (float)Math.Abs(Math.Sin(now * 18)) * 4f : (float)Math.Sin(now * 2) * 1.5f;
            var at = new PointF((bobber.Position.X - startX) * TileSize, (bobber.Position.Y - startY) * TileSize + dip);
            using (var line = new Pen(Color.FromArgb(180, 230, 230, 240), 1f))
                g.DrawBezier(line, hand, new PointF(hand.X, hand.Y + 10), new PointF(at.X, at.Y - 30), at);
            g.FillEllipse(Brushes.White, at.X - 4, at.Y - 4, 8, 8);
            g.FillPie(_healthBar, at.X - 4, at.Y - 4, 8, 8, 180, 180);
            if (bobber.Biting)
                DrawCentered(g, "!", new RectangleF(at.X - 20, at.Y - 34, 40, 24), _menuTitleFont, _amberBrush);
        }

        /// <summary>Clouds, precipitation, dust, fog and lightning over the world (sky effects only above ground).</summary>
        private void DrawWeather(Graphics g, GameSimulation game, int startX, int startY, double now)
        {
            if (!_menu.Preferences.WeatherEffects)
                return;
            var weather = game.Weather;
            var height = Height - HudHeight;
            var outdoors = !game.CurrentBiome.Underground;
            var frozen = game.CurrentBiome.Zone == BiomeZone.FrozenWasteland;
            if (outdoors)
            {
                // Precipitation: streaks slanted by wind; snow in the Frozen Wasteland.
                var drops = (int)(weather.Rain * 260 + weather.Snow * 160);
                var slant = Math.Clamp(weather.Wind * 6f, -14f, 14f);
                using var rain = new Pen(weather.AcidRain ? Color.FromArgb(150, 150, 240, 90) : Color.FromArgb(120, 170, 200, 240), 1.5f);
                using var snow = new SolidBrush(Color.FromArgb(200, 240, 245, 255));
                for (var i = 0; i < drops; i++)
                {
                    var seed = unchecked(i * 7919);
                    var speed = frozen ? 60 : 520 + i % 7 * 40;
                    var x = (float)((seed % Math.Max(1, Width) + now * slant * 20 + startX * TileSize * 0.2) % Math.Max(1, Width));
                    var y = (float)((seed * 13 % Math.Max(1, height) + now * speed) % Math.Max(1, height));
                    if (x < 0) x += Width;
                    if (frozen || weather.Snow > weather.Rain)
                        g.FillEllipse(snow, x + (float)Math.Sin(now * 2 + i) * 6, y, 3, 3);
                    else
                        g.DrawLine(rain, x, y, x + slant, y + 14);
                }
                if (weather.Dust > 0.02f)
                {
                    using var dust = new SolidBrush(Color.FromArgb((int)(weather.Dust * 110), 170, 130, 80));
                    g.FillRectangle(dust, 0, 0, Width, height);
                }
            }
            if (weather.Fog > 0.02f)
            {
                using var fog = new SolidBrush(Color.FromArgb((int)(weather.Fog * 120), 170, 175, 185));
                g.FillRectangle(fog, 0, 0, Width, height);
            }
            foreach (var strike in game.LightningStrikes.Where(strike => strike.AgeSeconds < 0.35f))
            {
                var alpha = (int)(200 * (1f - strike.AgeSeconds / 0.35f));
                using (var flash = new SolidBrush(Color.FromArgb(alpha / 3, 230, 235, 255)))
                    g.FillRectangle(flash, 0, 0, Width, height);
                var bottom = new PointF((strike.Position.X - startX) * TileSize, (strike.Position.Y - startY) * TileSize);
                var random = new Random((int)(strike.Position.X * 31 + strike.Position.Y));
                var points = new List<PointF>();
                for (var y = 0f; y < bottom.Y; y += 24)
                    points.Add(new PointF(bottom.X + random.Next(-18, 19) * (1f - y / Math.Max(1f, bottom.Y)), y));
                points.Add(bottom);
                if (points.Count > 1)
                {
                    using var bolt = new Pen(Color.FromArgb(alpha, 235, 240, 255), 3f);
                    g.DrawLines(bolt, points.ToArray());
                }
            }
        }

        /// <summary>Drifting clouds in the sky layer; cover and speed follow the weather.</summary>
        private void DrawClouds(Graphics g, GameSimulation game, double now)
        {
            if (!_menu.Preferences.BackgroundEnabled)
                return;
            if (DrawSpriteClouds(g, game, now))
                return;
            var cover = game.Weather.CloudCover;
            var count = (int)(cover * 14);
            var height = Height - HudHeight;
            var shade = game.Weather.Storm ? 90 : 230;
            using var brush = new SolidBrush(Color.FromArgb((int)(60 + cover * 120), shade, shade, shade + 10));
            for (var i = 0; i < count; i++)
            {
                var w = 120 + i * 37 % 160;
                var x = (float)(((i * 263 + now * (8 + game.Weather.Wind * 12) * (1 + i % 3 * 0.4)) % (Width + w * 2) + Width + w * 2) % (Width + w * 2)) - w;
                var y = 30 + i * 53 % Math.Max(1, height / 3);
                g.FillEllipse(brush, x, y, w, w * 0.35f);
                g.FillEllipse(brush, x + w * 0.25f, y - w * 0.12f, w * 0.55f, w * 0.38f);
            }
        }

        /// <summary>
        /// Draws an actor with its feet on the bottom of its collision box. With a <paramref name="reference"/> frame (an
        /// actor's idle pose or base sprite) the scale comes from that frame alone and every animation frame keeps it, so a
        /// body never grows or shrinks between frames; without one, the sprite is fitted to the box.
        /// </summary>
        private void DrawSpriteActor(Graphics g, string sprite, Vector2 position, Vector2 size, float scale, bool flip,
            int startX, int startY, float bob = 0f, bool flash = false, bool flashWhite = false, float scaleX = 1f, float scaleY = 1f,
            string? reference = null, ImageAttributes? ghost = null)
        {
            var boxW = size.X * TileSize;
            var boxH = size.Y * TileSize;
            var maxW = (int)MathF.Max(6f, boxW * scale * 1.6f);
            var maxH = (int)MathF.Max(6f, boxH * scale);
            Bitmap? bitmap;
            if (reference is not null && _sprites.Get(reference) is { } model && _sprites.Get(sprite) is { } frame)
            {
                var fit = MathF.Min(maxW / (float)model.Width, maxH / (float)model.Height);
                bitmap = _sprites.GetScaled(sprite, Math.Max(1, (int)MathF.Round(frame.Width * fit)), Math.Max(1, (int)MathF.Round(frame.Height * fit)), flip);
            }
            else
                bitmap = _sprites.GetFitted(sprite, maxW, maxH, flip);
            if (bitmap is null)
            {
                g.FillRectangle(_enemyBrush, (position.X - startX) * TileSize, (position.Y - startY) * TileSize,
                    Math.Max(3f, boxW), Math.Max(3f, boxH));
                return;
            }
            // Feet on the bottom of the collision box, centred horizontally; squash keeps the feet planted.
            var drawW = (int)(bitmap.Width * scaleX);
            var drawH = (int)(bitmap.Height * scaleY);
            var left = (position.X - startX) * TileSize + boxW / 2f - drawW / 2f;
            var top = (position.Y - startY) * TileSize + boxH - drawH + bob;
            var destination = new Rectangle((int)left, (int)top, drawW, drawH);
            if (ghost is not null)
                g.DrawImage(bitmap, destination, 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, ghost);
            else if (flash)
            {
                using var bright = new ImageAttributes();
                bright.SetColorMatrix(flashWhite
                    ? new ColorMatrix { Matrix00 = 1f, Matrix11 = 1f, Matrix22 = 1f, Matrix33 = 1f, Matrix44 = 1f, Matrix40 = 0.75f, Matrix41 = 0.75f, Matrix42 = 0.75f }
                    : new ColorMatrix { Matrix00 = 1f, Matrix11 = 1f, Matrix22 = 1f, Matrix33 = 1f, Matrix44 = 1f, Matrix40 = 0.45f, Matrix41 = 0.25f, Matrix42 = 0.1f });
                g.DrawImage(bitmap, destination, 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, bright);
            }
            else if (drawW == bitmap.Width && drawH == bitmap.Height)
                g.DrawImageUnscaled(bitmap, destination.X, destination.Y);
            else
                g.DrawImage(bitmap, destination);
        }

        private void DrawHealthBar(Graphics g, Vector2 position, Vector2 size, float fraction, bool boss, int startX, int startY)
        {
            var width = MathF.Max(TileSize * 1.5f, size.X * TileSize * (boss ? 1.6f : 1.1f));
            var x = (position.X - startX) * TileSize + size.X * TileSize / 2f - width / 2f;
            var y = _menu.Preferences.EnemyHealthBars == HealthBarPlacement.Below
                ? (position.Y + size.Y - startY) * TileSize + 4
                : (position.Y - startY) * TileSize - TileSize * (boss ? 1.6f : 0.9f);
            g.FillRectangle(_barBack, x, y, width, 4);
            g.FillRectangle(boss ? _bossBar : _healthBar, x, y, width * Math.Clamp(fraction, 0f, 1f), 4);
        }

        private void DrawPlayer(Graphics g, GameSimulation game, int startX, int startY, double now)
        {
            var motion = game.Player.Motion;
            var pose = _animator.PlayerPose(game, now);
            var look = DyedLook(game, CharacterLook.Body(_sprites, pose.Frame, game.Player.Colonist.AppearanceValue.Normalized()));
            // The idle frame fills the collision box's height; every other frame is drawn at that same scale.
            var reference = pose.Frame.StartsWith("anim_", StringComparison.Ordinal) ? "anim_idle" : "colonist_front";
            _rig = RigFor(game, pose, startX, startY);
            UpdateGear(game, now);
            DrawDashGhosts(g, game, look, pose, reference, startX, startY, now);
            DrawGearBehind(g, game, _rig, now); // wings, rocket pack (GraphicalSandbox.Gear.cs)
            DrawSpriteActor(g, look, motion.Position, motion.Size, 1f, pose.Flip, startX, startY, pose.Bob, flash: pose.Flash, flashWhite: true,
                scaleX: pose.ScaleX, scaleY: pose.ScaleY, reference: reference);
            if (!game.Player.IsDowned)
                DrawHeldItem(g, game, _rig, now); // every use style, arm from the shoulder (GraphicalSandbox.HeldItems.cs)
            DrawGearFront(g, game, _rig, now);
        }

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        private void DrawCrew(Graphics g, ColonistAgent agent, int startX, int startY, double now)
        {
            var motion = agent.Motion;
            var flip = motion.Velocity.X < -0.05f;
            var bob = MathF.Abs(motion.Velocity.X) > 0.3f ? (float)Math.Abs(Math.Sin(now * 9 + agent.Id)) * -2f : 0f;
            var crewSprite = SpriteCatalog.AnimatedFrame(SpriteCatalog.ForCrew(agent.Id), MathF.Abs(motion.Velocity.X) > 0.3f, false, now, agent.Id);
            DrawSpriteActor(g, crewSprite, motion.Position, motion.Size, 1f, flip, startX, startY, bob, reference: SpriteCatalog.ForCrew(agent.Id));
            DrawNameTag(g, agent.Profile.Name, motion.Position, motion.Size, startX, startY);
        }

        private void DrawNameTag(Graphics g, string? name, Vector2 position, Vector2 size, int startX, int startY)
        {
            if (string.IsNullOrWhiteSpace(name) || MousePosition is not { } mouse)
                return;
            var x = (position.X - startX) * TileSize;
            var y = (position.Y - startY) * TileSize - size.Y * TileSize * 0.35f;
            // As in Terraria, a resident's name shows while the mouse is over them.
            if (!new RectangleF(x - 4f, (position.Y - startY) * TileSize - 4f, size.X * TileSize + 8f, size.Y * TileSize + 8f).Contains(mouse))
                return;
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var rect = new RectangleF(x - 40f, y - 16f, size.X * TileSize + 80f, 14f);
            g.DrawString(name, _small, Brushes.Black, new RectangleF(rect.X + 1, rect.Y + 1, rect.Width, rect.Height), format);
            g.DrawString(name, _small, Brushes.White, rect, format);
        }

        private void DrawHud(Graphics g, GameSimulation game)
        {
            // Hotbar, top-left, with the selected item's name above it.
            var inventory = game.Player.Inventory;
            var hotbarCount = Math.Min(10, inventory.Slots.Count);
            var item = inventory.SelectedItem;
            var itemName = item is null ? "Empty hands" : inventory.SelectedStack.Count > 1 ? $"{item.Name} ({inventory.SelectedStack.Count})" : item.Name;
            DrawShadowed(g, itemName, _hud, _amberBrush, 12, 5);
            _hotbarHits.Clear();
            for (var i = 0; i < hotbarCount; i++)
            {
                var selected = i == inventory.SelectedSlot;
                var grow = selected ? 4 : 0;
                var rect = new RectangleF(10 + i * (HotbarSlot + 4) - grow / 2f, HotbarTop - grow / 2f, HotbarSlot + grow, HotbarSlot + grow);
                _hotbarHits.Add((rect, i));
                DrawSlot(g, rect, inventory.Slots[i], selected, (i + 1) % 10 + "");
            }

            DrawVitals(g, game);

            // Clock, defence, mood and money: one quiet line in the bottom-right corner.
            var info = $"Day {game.Day}  {FormatClock(game.ClockHour)}   DEF {game.Player.Stats.Defense}   Mood {game.Player.Mood:P0} {game.Player.MoodLabel}   " +
                       $"{game.Player.Inventory.Count(ItemId.Credits)} cr   Crew {game.Colonists.Count(agent => agent.IsAlive)}" +
                       (game.DroneSlots > 0 ? $"   Drones {game.ActiveDrones}/{game.DroneSlots}" : string.Empty) +
                       (game.SentrySlots > 0 ? $"   Sentries {game.ActiveSentries}/{game.SentrySlots}" : string.Empty) +
                       (game.TimeRate > 1.01f ? $"   time ×{game.TimeRate:0}" : string.Empty);
            var infoSize = g.MeasureString(info, _small);
            DrawShadowed(g, info, _small, _hudBrush, Width - infoSize.Width - 10, Height - 22);

            DrawControlsHint(g);
            DrawGuideHint(g, game);
        }

        private static string FormatClock(float hour)
        {
            var h = (int)hour % 24;
            var m = (int)((hour - MathF.Floor(hour)) * 60f);
            return $"{(h % 12 == 0 ? 12 : h % 12)}:{m:00} {(h < 12 ? "AM" : "PM")}";
        }

        private static void DrawShadowed(Graphics g, string text, Font font, Brush brush, float x, float y)
        {
            g.DrawString(text, font, Brushes.Black, x + 1, y + 1);
            g.DrawString(text, font, brush, x, y);
        }

        /// <summary>Life, charge, food and rest in one panel above the minimap; low life pulses.</summary>
        private void DrawVitals(Graphics g, GameSimulation game)
        {
            var panel = new RectangleF(Width - MinimapWidth - 10, 8, MinimapWidth, VitalsHeight - 8);
            _hudRects.Add(panel);
            DrawRivetedPanel(g, panel);
            var x = panel.X + 12;
            var width = panel.Width - 24;
            var life = game.Player.HitPoints / Math.Max(1f, game.Player.MaximumHitPoints);
            var lifeBrush = life < 0.3f && ((Environment.TickCount64 / 300) & 1) == 0 ? _bossBar : _healthBar;
            DrawBar(g, x, panel.Y + 9, width, 16, life, lifeBrush, $"LIFE  {game.Player.HitPoints:0} / {game.Player.MaximumHitPoints:0}");
            if (game.MaximumCharge > 0f)
                DrawBar(g, x, panel.Y + 29, width, 13, game.Charge / game.MaximumCharge, _chargeBar, $"CHARGE  {game.Charge:0} / {game.MaximumCharge:0}");
            var half = (width - 8) / 2f;
            DrawBar(g, x, panel.Y + 47, half, 5, game.Player.Needs.FoodReserve, _foodBar, $"FOOD {game.Player.Needs.FoodReserve:P0}");
            DrawBar(g, x + half + 8, panel.Y + 47, half, 5, game.Player.Needs.RestReserve, _restBar, $"REST {game.Player.Needs.RestReserve:P0}");
        }

        /// <summary>A bar with its label on top (or underneath, for thin bars).</summary>
        private void DrawBar(Graphics g, float x, float y, float width, float height, float fraction, Brush fill, string label)
        {
            g.FillRectangle(_barBack, x, y, width, height);
            g.FillRectangle(fill, x, y, width * Math.Clamp(fraction, 0f, 1f), height);
            g.DrawRectangle(_menuBorderPen, x, y, width, height);
            if (height >= 12)
            {
                DrawCentered(g, label, new RectangleF(x + 1, y, width, height + 2), _small, Brushes.Black);
                DrawCentered(g, label, new RectangleF(x, y - 1, width, height + 2), _small, Brushes.White);
            }
            else
                g.DrawString(label, _small, _dimTextBrush, x - 2, y + height);
        }

        private const double HintSeconds = 90;
        private double _hintStart;
        private GameSimulation? _hintGame;

        /// <summary>Toggled with the key-help key ("/" by default); otherwise the hint shows for the first minute and a half of a session.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowControls { get; set; }

        /// <summary>The player's key bindings, for the help overlay and key prompts.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public InputProfile? KeyBindings { get; set; }

        private bool ControlsHintVisible(out int alpha)
        {
            alpha = 0;
            if (Game is null)
                return false;
            var now = FrameClock.Seconds;
            if (!ReferenceEquals(_hintGame, Game))
            {
                _hintGame = Game;
                _hintStart = now;
            }
            if (ShowControls)
            {
                alpha = 255;
                return true;
            }
            alpha = (int)(255 * Math.Clamp((HintSeconds - (now - _hintStart)) / 5.0, 0, 1));
            return alpha > 0;
        }

        private void DrawControlsHint(Graphics g)
        {
            if (!ControlsHintVisible(out var alpha))
                return;
            var lines = KeyBindings is null ? [] : ControlCatalog.HintLines(KeyBindings);
            var width = lines.Max(line => g.MeasureString(line, _small).Width) + 24;
            var panel = new RectangleF((Width - width) / 2f, Height - 76, width, 46);
            using var back = new SolidBrush(Color.FromArgb(alpha * 200 / 255, 24, 28, 36));
            using var text = new SolidBrush(Color.FromArgb(alpha, 210, 214, 224));
            g.FillRectangle(back, panel);
            for (var i = 0; i < lines.Length; i++)
                DrawCentered(g, lines[i], new RectangleF(panel.X, panel.Y + 6 + i * 18, panel.Width, 18), _small, text);
        }

        private const int MinimapWidth = 280;
        private const int MinimapHeight = 150;
        /// <summary>Map style, cycled with F9 like the reference's map-style key: minimap → overlay → off.</summary>
        public enum MapStyle : byte { Minimap, Overlay, Off }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MapStyle MapView { get; set; } = MapStyle.Minimap;

        /// <summary>Screen pixels per map tile on the minimap and overlay; '=' and '-' change it.</summary>
        private int _minimapScale = 2;
        private int MinimapScale => _minimapScale;

        public void ZoomMinimap(int delta)
        {
            _minimapScale = Math.Clamp(_minimapScale + delta, 1, 6);
            _minimap?.Dispose();
            _minimap = null;
            _overlayMap?.Dispose();
            _overlayMap = null;
        }

        private Bitmap? _overlayMap;
        private int _overlayRevision = -1;
        private Point _overlayCenter;

        /// <summary>Overlay style: the remembered map drawn translucently over the whole world view, centred on the player.</summary>
        private void DrawMapOverlay(Graphics g, GameSimulation game)
        {
            if (Map is null)
                return;
            var scale = MinimapScale;
            var mapW = Math.Max(1, Width / scale);
            var mapH = Math.Max(1, Height / scale);
            var center = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
            var tileCenter = new Point((int)center.X, (int)center.Y);
            if (_overlayMap is null || _overlayMap.Width != mapW || _overlayMap.Height != mapH || Map.Revision != _overlayRevision || tileCenter != _overlayCenter)
            {
                if (_overlayMap is null || _overlayMap.Width != mapW || _overlayMap.Height != mapH)
                {
                    _overlayMap?.Dispose();
                    _overlayMap = new Bitmap(mapW, mapH, PixelFormat.Format32bppArgb);
                }
                Map.Render(_overlayMap, tileCenter.X - mapW / 2, tileCenter.Y - mapH / 2, 1f, (kind, id, y) => MapColor(game, kind, id, y));
                _overlayRevision = Map.Revision;
                _overlayCenter = tileCenter;
            }
            using var fade = new ImageAttributes();
            fade.SetColorMatrix(new ColorMatrix { Matrix33 = OverlayOpacity });
            var previous = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(_overlayMap, new Rectangle(0, 0, mapW * scale, mapH * scale), 0, 0, mapW, mapH, GraphicsUnit.Pixel, fade);
            g.InterpolationMode = previous;
            g.FillEllipse(Brushes.White, Width / 2f - 3, Height / 2f - 3, 6, 6);
        }

        /// <summary>
        /// The in-world indicator under the cursor, as in Terraria: with smart cursor on, brackets on its target (a tile, or a
        /// creature for guns and blades); otherwise only the ghost of a block or furniture about to be placed. There is no
        /// tile outline: the mouse cursor itself is the cursor.
        /// </summary>
        private void DrawCursorHighlight(Graphics g, GameSimulation game, int startX, int startY)
        {
            if (CursorTile() is not { } tile)
                return;
            var smartTarget = Smart.Enabled ? Smart.Resolve(game, tile, useHeld: false) : null;
            DrawPlacementPreview(g, game, smartTarget is { Enemy: null } placed ? placed.Tile : tile, startX, startY);
            if (smartTarget is { } target)
            {
                var pulse = 0.6f + 0.4f * MathF.Sin((float)(Environment.TickCount64 / 160.0));
                using var smartPen = new Pen(Color.FromArgb((int)(255 * pulse), 90, 230, 255), 2f);
                if (target.Enemy is { } enemy)
                {
                    var box = new RectangleF((enemy.Motion.Position.X - startX) * TileSize - 4, (enemy.Motion.Position.Y - startY) * TileSize - 4,
                        enemy.Motion.Size.X * TileSize + 8, enemy.Motion.Size.Y * TileSize + 8);
                    DrawBrackets(g, smartPen, box);
                }
                else
                    DrawBrackets(g, smartPen, new RectangleF((target.Tile.X - startX) * TileSize - 2, (target.Tile.Y - startY) * TileSize - 2, TileSize + 4, TileSize + 4));
                return;
            }
            // Without smart cursor there is no box around the tile: the mouse cursor is the only cursor, as in Terraria.
        }

        /// <summary>Adds a renderer-side line to the message log (UI notices that are not game events).</summary>
        public void Notify(string text)
        {
            _messageLog.Add((text, Environment.TickCount64));
            if (_messageLog.Count > 6)
                _messageLog.RemoveAt(0);
        }

        private static readonly ImageAttributes PreviewAlpha = CreateAlphaAttributes(0.45f);

        private static ImageAttributes CreateAlphaAttributes(float alpha)
        {
            var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
            return attributes;
        }

        /// <summary>
        /// Terraria's Placement Preview: a faint ghost of the held block or wall in the cell it would go into
        /// (the smart cursor's cell when that is on), only where it can actually be placed from here.
        /// </summary>
        private void DrawPlacementPreview(Graphics g, GameSimulation game, GridPoint cell, int startX, int startY)
        {
            if (!_menu.Preferences.PlacementPreview || game.Player.Inventory.SelectedItem is not { } held || !game.World.Contains(cell) ||
                !game.IsInToolReach(cell))
                return;
            var current = game.World.GetCell(cell);
            string? sprite = null;
            if (held.UseKind == ItemUseKind.PlaceTile && held.PlacedTile != TileId.Air && current.TileId == TileId.Air && PropFor(held.PlacedTile) is null)
                sprite = SpriteCatalog.ForTile(held.PlacedTile);
            else if (held.UseKind == ItemUseKind.PlaceWall && held.PlacedWall != 0 && current.WallId == 0)
                sprite = SpriteCatalog.ForWall(held.PlacedWall);
            if (sprite is null || _sprites.GetScaled(sprite, TileSize, TileSize) is not { } image)
                return;
            var rect = new Rectangle((cell.X - startX) * TileSize, (cell.Y - startY) * TileSize, TileSize, TileSize);
            g.DrawImage(image, rect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, PreviewAlpha);
        }

        private readonly Dictionary<TileId, string?> _propCache = new();

        /// <summary>
        /// Sprite for tiles drawn as props on the ground (stations, furniture, structures): the explicit
        /// table first, then any generated station_/furn_/struct_ sprite matching the tile's name.
        /// </summary>
        private string? PropFor(TileId tile)
        {
            if (_propCache.TryGetValue(tile, out var cached))
                return cached;
            var prop = SpriteCatalog.ForStation(tile);
            // A tile with block art is a block; otherwise look for station/furniture/structure art.
            if (prop is null && _sprites.Find(SpriteCatalog.ForTile(tile)) is null)
            {
                var snake = SpriteCatalog.SnakeCase(tile.ToString());
                foreach (var prefix in new[] { "station_", "furn_", "struct_", "deco_" })
                    if (_sprites.Find(prefix + snake) == prefix + snake)
                    {
                        prop = prefix + snake;
                        break;
                    }
            }
            _propCache[tile] = prop;
            return prop;
        }

        private static void DrawBrackets(Graphics g, Pen pen, RectangleF box)
        {
            var arm = Math.Min(8f, Math.Min(box.Width, box.Height) / 3f);
            g.DrawLines(pen, new[] { new PointF(box.Left, box.Top + arm), new PointF(box.Left, box.Top), new PointF(box.Left + arm, box.Top) });
            g.DrawLines(pen, new[] { new PointF(box.Right - arm, box.Top), new PointF(box.Right, box.Top), new PointF(box.Right, box.Top + arm) });
            g.DrawLines(pen, new[] { new PointF(box.Left, box.Bottom - arm), new PointF(box.Left, box.Bottom), new PointF(box.Left + arm, box.Bottom) });
            g.DrawLines(pen, new[] { new PointF(box.Right - arm, box.Bottom), new PointF(box.Right, box.Bottom), new PointF(box.Right, box.Bottom - arm) });
        }

        private static Cursor CreateHiddenCursor()
        {
            using var blank = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
            return new Cursor(blank.GetHicon());
        }

        /// <summary>
        /// The drawn cursor: an outlined arrow whose colour breathes slowly (amber, or cyan with the
        /// smart cursor on), mouse text for whatever is under it in the world, and the carried
        /// inventory stack.
        /// </summary>
        private void DrawGameCursor(Graphics g)
        {
            if (MousePosition is not { } mouse)
                return;
            var time = FrameClock.Seconds;
            var breathe = (float)(0.85 + 0.15 * Math.Sin(time * 3));
            var preset = GamePreferences.CursorColors[Math.Clamp(_menu.Preferences.CursorColor, 0, GamePreferences.CursorColors.Length - 1)];
            var baseColor = Smart.Enabled && Game is not null && !MenuOpen ? Color.FromArgb(90, 230, 255) : Color.FromArgb(preset.R, preset.G, preset.B);
            var k = _menu.Preferences.CursorScale;
            var color = Color.FromArgb((int)(baseColor.R * breathe), (int)(baseColor.G * breathe), (int)(baseColor.B * breathe));

            if (MenuOpen && _menu.Ui.CurrentState is InventoryUiState inventory && !inventory.Controller.CursorStack.IsEmpty &&
                _sprites.GetFitted(SpriteCatalog.ForItem(inventory.Controller.CursorStack.ItemId), 34, 34) is { } carried)
            {
                g.DrawImageUnscaled(carried, mouse.X + 8, mouse.Y + 10);
                if (inventory.Controller.CursorStack.Count > 1)
                    g.DrawString(inventory.Controller.CursorStack.Count.ToString(), _menuSmallFont, Brushes.White, mouse.X + 30, mouse.Y + 34);
            }

            var arrow = new[]
            {
                new PointF(mouse.X, mouse.Y), new PointF(mouse.X, mouse.Y + 17 * k), new PointF(mouse.X + 4.5f * k, mouse.Y + 12.5f * k),
                new PointF(mouse.X + 8 * k, mouse.Y + 19 * k), new PointF(mouse.X + 11 * k, mouse.Y + 17.5f * k), new PointF(mouse.X + 7.5f * k, mouse.Y + 11.5f * k),
                new PointF(mouse.X + 13 * k, mouse.Y + 11.5f * k)
            };
            var previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_menu.Preferences.CursorBorder)
            {
                using var outline = new Pen(Color.FromArgb(230, 10, 10, 14), 3f) { LineJoin = LineJoin.Round };
                g.DrawPolygon(outline, arrow);
            }
            using (var fill = new SolidBrush(color))
                g.FillPolygon(fill, arrow);
            g.SmoothingMode = previous;

            if (!MenuOpen && Game is { } game && MouseTextFor(game, mouse) is { } text)
            {
                var size = g.MeasureString(text, _menuSmallFont);
                var at = new PointF(Math.Min(mouse.X + 18, Width - size.Width - 4), mouse.Y + 20);
                g.DrawString(text, _menuSmallFont, Brushes.Black, at.X + 1, at.Y + 1);
                g.DrawString(text, _menuSmallFont, Brushes.White, at);
            }
        }

        /// <summary>Name of the creature, colonist, or notable tile under the cursor.</summary>
        private string? MouseTextFor(GameSimulation game, Point mouse)
        {
            if (mouse.Y >= Height - HudHeight || HotbarSlotAt(mouse) is not null)
                return null;
            var world = new Vector2(_viewStartX + mouse.X / (float)TileSize, _viewStartY + mouse.Y / (float)TileSize);
            static bool Over(Vector2 point, Vector2 position, Vector2 size, float pad) =>
                point.X >= position.X - pad && point.X <= position.X + size.X + pad && point.Y >= position.Y - pad && point.Y <= position.Y + size.Y + pad;

            foreach (var enemy in game.Enemies)
                if (enemy.IsAlive && Over(world, enemy.Motion.Position, enemy.Motion.Size, 0.3f))
                    return $"{enemy.DisplayName}: {enemy.HitPoints:0}/{enemy.MaximumHitPoints:0}";
            foreach (var colonist in game.Colonists)
                if (colonist.IsAlive && Over(world, colonist.Motion.Position, colonist.Motion.Size, 0.3f))
                    return $"{colonist.Profile.Name} · {colonist.Activity} · {colonist.MoodLabel}";
            if (!game.GuideMovedIn && Vector2.Distance(world, game.Guide.Position) < 1.2f)
                return "Station AI · right-click for tips and the recipe guide";

            if (CursorTile() is { } tile)
            {
                var id = game.World.GetCell(tile).TileId;
                if (id != TileId.Air && game.Tiles.Contains(id) &&
                    (SmartCursor.IsShiny(id) || PropFor(id) is not null || id.ToString().Contains("Heart") ||
                     id.ToString().Contains("Core") || id.ToString().Contains("Altar")))
                    return game.Tiles[id].Name;
            }
            return null;
        }

        /// <summary>Minimap of the remembered map around the player.</summary>
        private void DrawMinimap(Graphics g, GameSimulation game)
        {
            if (Map is null)
                return;
            var mapW = MinimapWidth / MinimapScale;
            var mapH = MinimapHeight / MinimapScale;
            var center = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
            var tileCenter = new Point((int)center.X, (int)center.Y);
            if (_minimap is null || Map.Revision != _minimapRevision || tileCenter != _minimapCenter)
            {
                _minimap ??= new Bitmap(Math.Max(1, mapW), Math.Max(1, mapH), PixelFormat.Format32bppArgb);
                Map.Render(_minimap, tileCenter.X - mapW / 2, tileCenter.Y - mapH / 2, 1f, (kind, id, y) => MapColor(game, kind, id, y));
                _minimapRevision = Map.Revision;
                _minimapCenter = tileCenter;
            }

            var frame = new RectangleF(Width - MinimapWidth - 10, VitalsHeight + 4, MinimapWidth, MinimapHeight);
            _hudRects.Add(frame);
            var previous = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (MinimapOpacity >= 0.999f)
                g.DrawImage(_minimap, frame);
            else
            {
                using var alpha = new ImageAttributes();
                alpha.SetColorMatrix(new ColorMatrix { Matrix33 = MinimapOpacity });
                g.DrawImage(_minimap, Rectangle.Round(frame), 0, 0, _minimap.Width, _minimap.Height, GraphicsUnit.Pixel, alpha);
            }
            g.InterpolationMode = previous;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            foreach (var agent in game.Colonists.Where(agent => agent.IsAlive))
            {
                var cx = frame.X + frame.Width / 2 + (agent.Motion.Position.X - center.X) * MinimapScale;
                var cy = frame.Y + frame.Height / 2 + (agent.Motion.Position.Y - center.Y) * MinimapScale;
                if (frame.Contains(cx, cy))
                    g.FillEllipse(_restBar, cx - 2, cy - 2, 4, 4);
            }
            foreach (var enemy in game.Enemies.Where(enemy => enemy.IsAlive && enemy.IsBoss))
            {
                var ex = frame.X + frame.Width / 2 + (enemy.Motion.Position.X - center.X) * MinimapScale;
                var ey = frame.Y + frame.Height / 2 + (enemy.Motion.Position.Y - center.Y) * MinimapScale;
                if (frame.Contains(ex, ey) && Map.IsRevealed((int)enemy.Motion.Position.X, (int)enemy.Motion.Position.Y))
                    g.FillRectangle(_bossBar, ex - 3, ey - 3, 6, 6);
            }
            g.FillEllipse(Brushes.White, frame.X + frame.Width / 2 - 3, frame.Y + frame.Height / 2 - 3, 6, 6);
            g.DrawRectangle(_panelEdgePen, frame.X, frame.Y, frame.Width, frame.Height);
        }

        /// <summary>Base colour for a remembered map cell; open air shades by depth band.</summary>
        private Color MapColor(GameSimulation game, WorldMapState.CellKind kind, int id, int y)
        {
            switch (kind)
            {
                case WorldMapState.CellKind.Tile:
                    return TileColor((TileId)id);
                case WorldMapState.CellKind.Water:
                    return id == (int)LiquidKind.Fuel ? Color.FromArgb(220, 170, 50) : Color.FromArgb(50, 110, 210); // amber fuel seas
                case WorldMapState.CellKind.Lava:
                    return Color.FromArgb(110, 235, 70); // liquid uranium
                case WorldMapState.CellKind.Wall:
                {
                    var wall = WallColor((ushort)id);
                    return Color.FromArgb(wall.R * 55 / 100, wall.G * 55 / 100, wall.B * 55 / 100);
                }
                default:
                {
                    var depth = y / (float)Math.Max(1, game.World.Height);
                    return depth < 0.32f ? Blend(Color.FromArgb(110, 160, 220), Color.FromArgb(150, 190, 230), depth / 0.32f)
                        : depth < 0.5f ? Color.FromArgb(72, 54, 40)
                        : depth < 0.86f ? Color.FromArgb(48, 46, 54)
                        : Color.FromArgb(70, 26, 20);
                }
            }
        }

        private readonly Dictionary<ushort, Color> _wallColors = new();

        private Color WallColor(ushort wallId)
        {
            if (_wallColors.TryGetValue(wallId, out var cached))
                return cached;
            var color = Color.FromArgb(70, 70, 80);
            if (_sprites.GetScaled(SpriteCatalog.ForWall(wallId), 4, 4) is { } small)
            {
                int r = 0, gr = 0, b = 0;
                for (var yy = 0; yy < 4; yy++)
                for (var xx = 0; xx < 4; xx++)
                {
                    var pixel = small.GetPixel(xx, yy);
                    r += pixel.R; gr += pixel.G; b += pixel.B;
                }
                color = Color.FromArgb(r / 16, gr / 16, b / 16);
            }
            _wallColors[wallId] = color;
            return color;
        }

        /// <summary>Fullscreen map: the remembered world with pan, zoom and icon layers.</summary>
        private void DrawFullscreenMap(Graphics g, GameSimulation game)
        {
            g.Clear(Color.FromArgb(8, 9, 13));
            if (Map is null)
                return;
            var width = Math.Max(1, Width);
            var height = Math.Max(1, Height);
            // The image holds one pixel per screen pixel when zoomed out, one per tile when zoomed in.
            var pixelsPerSample = Math.Max(1f, _mapZoom);
            var imageW = (int)Math.Ceiling(width / pixelsPerSample);
            var imageH = (int)Math.Ceiling(height / pixelsPerSample);
            var key = (Map.Revision, _mapCenter, _mapZoom, width, height);
            if (_fullMapImage is null || _fullMapKey != key)
            {
                if (_fullMapImage is null || _fullMapImage.Width != imageW || _fullMapImage.Height != imageH)
                {
                    _fullMapImage?.Dispose();
                    _fullMapImage = new Bitmap(imageW, imageH, PixelFormat.Format32bppArgb);
                }
                var origin = ScreenToMap(Point.Empty);
                Map.Render(_fullMapImage, (int)Math.Floor(origin.X), (int)Math.Floor(origin.Y), pixelsPerSample / _mapZoom,
                    (kind, id, y) => MapColor(game, kind, id, y));
                _fullMapKey = key;
            }
            var previous = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_fullMapImage, 0, 0, imageW * pixelsPerSample, imageH * pixelsPerSample);
            g.InterpolationMode = previous;
            g.PixelOffsetMode = PixelOffsetMode.Default;

            // Icon layers, back to front: spawn, death marker, colonists, Station AI, bosses, player.
            var iconSize = (int)Math.Clamp(_mapZoom * 3f, 18f, 34f);
            void Icon(string sprite, float x, float y, int size)
            {
                var at = MapToScreen(x, y);
                if (_sprites.GetFitted(sprite, size, size) is { } bitmap)
                    g.DrawImageUnscaled(bitmap, (int)(at.X - bitmap.Width / 2f), (int)(at.Y - bitmap.Height / 2f));
            }
            if (Map.SpawnMarker is { } spawn)
                Icon("struct_colony_pod", spawn.X, spawn.Y, iconSize);
            if (Map.DeathMarker is { } death)
            {
                var at = MapToScreen(death.X, death.Y);
                using var pen = new Pen(Color.FromArgb(230, 230, 60, 60), 3f);
                g.DrawLine(pen, at.X - 7, at.Y - 7, at.X + 7, at.Y + 7);
                g.DrawLine(pen, at.X + 7, at.Y - 7, at.X - 7, at.Y + 7);
            }
            foreach (var agent in game.Colonists.Where(agent => agent.IsAlive))
                Icon(CharacterLook.Face(_sprites, agent.Profile.AppearanceValue.Normalized()), agent.Motion.Position.X, agent.Motion.Position.Y, iconSize - 4);
            if (!game.GuideMovedIn)
                Icon("station_ai", game.Guide.Position.X, game.Guide.Position.Y, iconSize - 4);
            // Town NPCs and bound survivors once their spot has been seen; blight hearts that were found and still stand.
            foreach (var npc in game.TownNpcs)
                if (Map.IsRevealed((int)npc.Motion.Position.X, (int)npc.Motion.Position.Y))
                    Icon(npc.SpriteName, npc.Motion.Position.X, npc.Motion.Position.Y, iconSize - 4);
            if (game.Features is { } features)
                foreach (var heart in features.Hearts)
                    if (Map.IsRevealed(heart.X, heart.Y) && game.World.Contains(heart.X, heart.Y) &&
                        BlightRules.IsHeart(game.World.GetCell(heart.X, heart.Y).TileId))
                        Icon(SpriteCatalog.ForTile(game.World.GetCell(heart.X, heart.Y).TileId) ?? "tile_reactor_core", heart.X + 0.5f, heart.Y + 0.5f, iconSize - 2);
            foreach (var boss in game.Enemies.Where(enemy => enemy.IsAlive && enemy.IsBoss))
                if (Map.IsRevealed((int)boss.Motion.Position.X, (int)boss.Motion.Position.Y))
                    Icon(SpriteCatalog.ForEnemy(boss), boss.Motion.Position.X, boss.Motion.Position.Y, iconSize + 8);
            DrawPylonMapIcons(g, game);
            var player = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
            Icon(CharacterLook.Face(_sprites, game.Player.Colonist.AppearanceValue.Normalized()), player.X, player.Y, iconSize + 4);

            var panel = new RectangleF(12, 12, 360, 54);
            DrawRivetedPanel(g, panel);
            g.DrawString("WORLD MAP", _hud, _amberBrush, panel.X + 12, panel.Y + 8);
            g.DrawString($"Drag or WASD to pan · wheel or +/- to zoom ({_mapZoom:0.##}×) · M or Esc to close", _small, _hudBrush, panel.X + 12, panel.Y + 30);
            if (MousePosition is { } mouse)
            {
                var tile = ScreenToMap(mouse);
                var depth = (int)tile.Y - (int)(game.World.Height * 0.3f);
                g.DrawString($"{(int)tile.X}, {(depth < 0 ? $"{-depth} up" : $"{depth} down")}", _small, _dimTextBrush, mouse.X + 16, mouse.Y + 18);
            }
        }

        /// <summary>Average colour of a tile's sprite, cached, for the minimap.</summary>
        private Color TileColor(TileId tile)
        {
            if (_tileColors.TryGetValue(tile, out var cached))
                return cached;
            var color = ColorFor(tile);
            var sprite = SpriteCatalog.ForTile(tile) ?? PropFor(tile);
            if (_sprites.GetScaled(sprite, 4, 4) is { } small)
            {
                int r = 0, gr = 0, b = 0, n = 0;
                for (var y = 0; y < 4; y++)
                for (var x = 0; x < 4; x++)
                {
                    var pixel = small.GetPixel(x, y);
                    if (pixel.A < 128) continue;
                    r += pixel.R; gr += pixel.G; b += pixel.B; n++;
                }
                if (n > 0)
                    color = Color.FromArgb(r / n, gr / n, b / n);
            }
            _tileColors[tile] = color;
            return color;
        }

        /// <summary>Terraria-style boss bar across the top while a boss is alive.</summary>
        private void DrawBossBar(Graphics g, GameSimulation game)
        {
            var boss = game.Enemies.FirstOrDefault(enemy => enemy.IsAlive && enemy.IsBoss);
            if (boss is null)
                return;
            var bar = new RectangleF(Width / 2f - 260, Height - (ControlsHintVisible(out _) ? 132 : 70), 520, 22);
            _hudRects.Add(new RectangleF(bar.X - 10, bar.Y - 8, bar.Width + 20, bar.Height + 34));
            DrawRivetedPanel(g, new RectangleF(bar.X - 10, bar.Y - 8, bar.Width + 20, bar.Height + 34));
            g.FillRectangle(_barBack, bar);
            g.FillRectangle(_bossBar, bar.X, bar.Y, bar.Width * Math.Clamp(boss.HitPoints / boss.MaximumHitPoints, 0f, 1f), bar.Height);
            g.DrawRectangle(_panelEdgePen, bar.X, bar.Y, bar.Width, bar.Height);
            DrawCentered(g, $"{boss.HitPoints:0} / {boss.MaximumHitPoints:0}", bar, _small, Brushes.White);
            DrawCentered(g, boss.DisplayName, new RectangleF(bar.X, bar.Bottom + 2, bar.Width, 18), _small, _amberBrush);
        }

        /// <summary>Active world events as progress bars under the boss bar, plus the summon countdown.</summary>
        private void DrawEventBars(Graphics g, GameSimulation game)
        {
            // Invasion and event progress stacks up from the bottom-right corner.
            var y = Height - 50f;
            foreach (var progress in game.EventProgress.Take(3))
            {
                var bar = new RectangleF(Width - 330, y, 310, 14);
                _hudRects.Add(new RectangleF(bar.X - 8, bar.Y - 20, bar.Width + 16, bar.Height + 28));
                DrawRivetedPanel(g, new RectangleF(bar.X - 8, bar.Y - 20, bar.Width + 16, bar.Height + 28));
                var label = progress.Waves > 1 ? $"{progress.Name}  ·  wave {progress.Wave}/{progress.Waves}" : progress.Name;
                if (progress.HoursLeft > 0f)
                    label += $"  ·  {progress.HoursLeft:0.0}h left";
                DrawCentered(g, label, new RectangleF(bar.X, bar.Y - 18, bar.Width, 16), _small, _amberBrush);
                g.FillRectangle(_barBack, bar);
                g.FillRectangle(_foodBar, bar.X, bar.Y, bar.Width * Math.Clamp(progress.Progress, 0f, 1f), bar.Height);
                g.DrawRectangle(_menuBorderPen, bar.X, bar.Y, bar.Width, bar.Height);
                y -= 46;
            }
            if (game.AstralCountdown > 0f)
                DrawCentered(g, $"The Astro-God answers in {MathF.Ceiling(game.AstralCountdown):0}…", new RectangleF(0, Height / 3f, Width, 30), _menuTitleFont, _bossBar);
            if (game.IsSleeping)
            {
                using var dim = new SolidBrush(Color.FromArgb(90, 0, 0, 20));
                g.FillRectangle(dim, 0, 0, Width, Height - HudHeight);
                DrawCentered(g, "Sleeping…  time ×4", new RectangleF(0, Height / 2f - 20, Width, 40), _menuTitleFont, Brushes.Gainsboro);
            }
        }

        /// <summary>Recent messages fade out bottom-left, newest last.</summary>
        private void DrawMessageLog(Graphics g, GameSimulation game)
        {
            var now = Environment.TickCount64;
            if (!string.IsNullOrWhiteSpace(game.LastMessage) && game.LastMessage != _lastLoggedMessage)
            {
                _lastLoggedMessage = game.LastMessage;
                _messageLog.Add((game.LastMessage, now));
                if (_messageLog.Count > 6)
                    _messageLog.RemoveAt(0);
            }
            var y = Height - (ControlsHintVisible(out _) ? 84 : 28) - _messageLog.Count * 17;
            foreach (var (text, at) in _messageLog)
            {
                var age = (now - at) / 1000f;
                var alpha = (int)(255 * Math.Clamp(1.5f - age / 6f, 0f, 1f));
                if (alpha > 0)
                {
                    using var shadow = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
                    using var brush = new SolidBrush(Color.FromArgb(alpha, 236, 238, 244));
                    g.DrawString(text, _small, shadow, 13, y + 1);
                    g.DrawString(text, _small, brush, 12, y);
                }
                y += 17;
            }
        }

        /// <summary>Item details beside the cursor when hovering a hotbar slot.</summary>
        private void DrawHotbarTooltip(Graphics g, GameSimulation game)
        {
            if (MousePosition is not { } mouse || HotbarSlotAt(mouse) is not { } slot)
                return;
            var stack = game.Player.Inventory.Slots[slot];
            if (stack.IsEmpty)
                return;
            DrawItemTooltip(g, game.Items[stack.ItemId], stack.Count, mouse);
        }

        private void DrawItemTooltip(Graphics g, ItemDefinition item, int count, Point at)
        {
            var lines = new List<string> { count > 1 ? $"{item.Name} ({count})" : item.Name };
            switch (item.UseKind)
            {
                case ItemUseKind.Melee: lines.Add($"{item.Damage} melee damage"); break;
                case ItemUseKind.Ranged: lines.Add($"{item.Damage} ranged damage · uses Energy cells"); break;
                case ItemUseKind.Pickaxe: lines.Add($"{item.ToolPower}% drill power"); break;
                case ItemUseKind.PlaceTile: lines.Add("Can be placed"); break;
                case ItemUseKind.Food: lines.Add($"Restores {item.FoodRestore:P0} food"); break;
                case ItemUseKind.Equipment: lines.Add($"{item.Defense} defense"); break;
                case ItemUseKind.Consumable: lines.Add("Consumable"); break;
            }
            if (item.UseCooldownTicks > 0 && item.UseKind is ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Pickaxe)
                lines.Add(item.UseCooldownTicks <= 10 ? "Fast speed" : item.UseCooldownTicks <= 24 ? "Average speed" : "Slow speed");
            if (item.IsMaterial)
                lines.Add("Material");
            // Terraria's comparison: how this piece measures against what is worn in its slot or held in hand.
            var comparison = Game is { } game ? CompareLine(game, item) : null;
            if (comparison is not null)
                lines.Add(comparison.Value.Text);
            var plain = lines.Count;
            lines.AddRange(GuideTooltipLines(item.Id));
            var width = lines.Max(line => g.MeasureString(line, _small).Width) + 20;
            var rect = new RectangleF(Math.Min(at.X + 16, Width - width - 4), Math.Max(4, at.Y - 18 - lines.Count * 16), width, lines.Count * 16 + 12);
            DrawRivetedPanel(g, rect);
            for (var i = 0; i < lines.Count; i++)
            {
                var brush = i == 0 ? _amberBrush
                    : comparison is { } line && i == plain - 1 ? line.Better ? _betterBrush : line.Worse ? _healthBar : _dimTextBrush
                    : i >= plain ? _guideTipBrush : _hudBrush;
                g.DrawString(lines[i], _small, brush, rect.X + 10, rect.Y + 6 + i * 16);
            }
        }

        private readonly Brush _betterBrush = new SolidBrush(Color.FromArgb(120, 220, 120));

        /// <summary>
        /// The comparison line for a tooltip: armor against the piece worn in its slot (defense), weapons and drills against
        /// the item in hand (damage, drill power). Null when there is nothing to compare.
        /// </summary>
        private static (string Text, bool Better, bool Worse)? CompareLine(GameSimulation game, ItemDefinition item)
        {
            (string, bool, bool) Line(int difference, string what, string against) => difference == 0
                ? ($"Same {what} as {against}", false, false)
                : ($"{difference:+0;-0} {what} compared with {against}", difference > 0, difference < 0);

            if (item.UseKind == ItemUseKind.Equipment && item.EquipmentSlot is EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Legs)
            {
                var worn = game.Player.Equipment[item.EquipmentSlot.Value];
                if (worn is null)
                    return ($"+{item.Defense} defense (nothing worn there)", item.Defense > 0, false);
                if (worn == item.Id)
                    return null;
                var wornItem = game.Items[worn.Value];
                return Line(item.Defense - wornItem.Defense, "defense", wornItem.Name);
            }
            if (game.Player.Inventory.SelectedItem is not { } held || held.Id == item.Id)
                return null;
            if (item.UseKind is ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Tech &&
                held.UseKind is ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Tech)
                return Line((int)MathF.Round(item.Damage - held.Damage), "damage", held.Name);
            if (item.UseKind == ItemUseKind.Pickaxe && held.UseKind == ItemUseKind.Pickaxe)
                return Line(item.ToolPower - held.ToolPower, "% drill power", held.Name);
            return null;
        }

        /// <summary>Top-right readout: biome, temperature/comfort, shelter, events, recent incidents.</summary>
        private void DrawStatusPanel(Graphics g, GameSimulation game)
        {
            var incidents = game.IncidentLog.Reverse().Take(3).ToArray();
            var lines = 4 + incidents.Length;
            var minimapShown = _menu.Preferences.MinimapEnabled && MapView == MapStyle.Minimap;
            var panel = new RectangleF(Width - 290, minimapShown ? VitalsHeight + MinimapHeight + 12 : VitalsHeight + 4, 280, 22 + lines * 18);
            _hudRects.Add(panel);
            DrawRivetedPanel(g, panel);
            var x = panel.X + 12;
            var y = panel.Y + 10;
            g.DrawString(game.CurrentBiome.DisplayName.ToUpperInvariant(), _hud, _amberBrush, x, y);
            y += 20;

            var comfort = game.PlayerComfort;
            var temperatureBrush = comfort.Dangerous ? _healthBar : comfort.Comfortable ? _hudBrush : _foodBar;
            var feel = comfort.Comfortable ? "comfortable" : comfort.TooCold ? (comfort.Dangerous ? "freezing" : "cold") : (comfort.Dangerous ? "scorching" : "hot");
            g.DrawString($"{game.PlayerAmbientTemperature:0}°C  {feel}  (outside {game.OutdoorTemperature:0}°C)", _small, temperatureBrush, x, y);
            y += 18;

            var room = game.PlayerRoom;
            g.DrawString(room is null ? "Outdoors" : $"Indoors · room of {room.CellCount} cells", _small, _hudBrush, x, y);
            y += 18;

            var events = new List<string>();
            if (game.ToxicFalloutActive) events.Add("Toxic fallout");
            if (game.SolarFlareActive) events.Add("Solar flare: energy weapons offline");
            g.DrawString(events.Count == 0 ? "No active events" : string.Join(" · ", events), _small,
                events.Count == 0 ? _dimTextBrush : _healthBar, x, y);
            y += 18;

            foreach (var incident in incidents)
            {
                using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                g.DrawString($"D{incident.Day} {incident.Hour:00}h  {incident.DisplayName}", _small, _dimTextBrush,
                    new RectangleF(x, y, panel.Width - 24, 16), format);
                y += 18;
            }

            // Worn information gear (compass, depth meter, ...): one small line each under the panel.
            var readoutY = panel.Bottom + 4;
            foreach (var readout in game.InfoReadouts)
            {
                DrawShadowed(g, readout, _small, _hudBrush, panel.X + 4, readoutY);
                readoutY += 16;
            }
        }

        private void DrawMeter(Graphics g, float x, float y, string label, float fraction, Brush fill, string value)
        {
            g.DrawString(label, _small, _amberBrush, x, y - 1);
            var barX = x + 44;
            g.FillRectangle(_barBack, barX, y + 2, 120, 10);
            g.FillRectangle(fill, barX, y + 2, 120 * Math.Clamp(fraction, 0f, 1f), 10);
            g.DrawRectangle(_menuBorderPen, barX, y + 2, 120, 10);
            g.DrawString(value, _small, _hudBrush, barX + 126, y - 1);
        }

        private void DrawSlot(Graphics g, RectangleF rect, ItemStack stack, bool selected, string? hotkey)
        {
            g.FillRectangle(_menuSlot, rect);
            g.DrawRectangle(selected ? _selectedSlotPen : _menuBorderPen, rect.X, rect.Y, rect.Width, rect.Height);
            if (!stack.IsEmpty)
            {
                var iconBox = (int)Math.Min(rect.Width, rect.Height) - 10;
                if (_sprites.GetFitted(SpriteCatalog.ForItem(stack.ItemId), iconBox, iconBox) is { } icon)
                    g.DrawImageUnscaled(icon, (int)(rect.X + (rect.Width - icon.Width) / 2), (int)(rect.Y + (rect.Height - icon.Height) / 2));
                else
                    g.DrawString(stack.ItemId.ToString(), _menuSmallFont, Brushes.White, rect.X + 3, rect.Y + 3);
                if (stack.Count > 1)
                {
                    using var format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Far };
                    g.DrawString(stack.Count.ToString(), _menuSmallFont, Brushes.Black, new RectangleF(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), format);
                    g.DrawString(stack.Count.ToString(), _menuSmallFont, Brushes.White, new RectangleF(rect.X, rect.Y, rect.Width - 2, rect.Height - 2), format);
                }
            }
            if (hotkey is not null)
                g.DrawString(hotkey, _menuSmallFont, _dimTextBrush, rect.X + 2, rect.Y + 1);
            if (stack.Favorite)
                g.DrawString("★", _menuSmallFont, _amberBrush, rect.Right - 14, rect.Y + 1);
        }

        private void DrawRivetedPanel(Graphics g, RectangleF rect)
        {
            using var fill = new LinearGradientBrush(rect.Height > 0 ? rect : new RectangleF(rect.X, rect.Y, 1, 1),
                Color.FromArgb(244, 46, 52, 60), Color.FromArgb(244, 30, 34, 40), 90f);
            g.FillRectangle(fill, rect);
            g.DrawRectangle(_panelEdgePen, rect.X + 1, rect.Y + 1, rect.Width - 3, rect.Height - 3);
            foreach (var (rx, ry) in new[] { (rect.Left + 5, rect.Top + 5), (rect.Right - 9, rect.Top + 5), (rect.Left + 5, rect.Bottom - 9), (rect.Right - 9, rect.Bottom - 9) })
                g.FillEllipse(_rivetBrush, rx, ry, 4, 4);
        }

        private void DrawMenu(Graphics g)
        {
            var screen = _menu.Ui.CurrentState as MenuScreen;
            var entriesByLabel = screen?.Entries
                .GroupBy(entry => entry.Label)
                .ToDictionary(group => group.Key, group => group.First()) ?? new Dictionary<string, MenuEntry>();
            if (screen?.Preview == MenuPreview.Character)
                DrawCharacterPreview(g, PreviewArea());
            else if (screen?.Preview == MenuPreview.World)
                DrawWorldPreview(g, PreviewArea());

            if (screen?.Id == "main-menu")
            {
                DrawMainMenu(g, screen);
                DrawScreenFade(g, screen.Id);
                return;
            }

            foreach (var command in _menu.Ui.BuildDrawCommands())
            {
                var rect = new RectangleF(command.Bounds.X, command.Bounds.Y, command.Bounds.Width, command.Bounds.Height);
                if (command.Kind == UiDrawKind.Button && entriesByLabel.TryGetValue(command.Text, out var cardEntry) &&
                    cardEntry.Tag is CharacterProfile or WorldProfile or CardIcon)
                {
                    DrawCard(g, rect, cardEntry, command.Focused, command.Hovered);
                    continue;
                }
                if (command.Kind == UiDrawKind.Button && screen is not null && entriesByLabel.TryGetValue(command.Text, out var rowEntry) &&
                    rowEntry.Tag is not (CharacterProfile or WorldProfile or CardIcon))
                {
                    DrawMenuRow(g, rect, MenuRows.Parse(rowEntry), rowEntry.Enabled, rowEntry.Adjust is not null, command.Focused, command.Hovered);
                    continue;
                }
                switch (command.Kind)
                {
                    case UiDrawKind.Panel:
                        DrawRivetedPanel(g, rect);
                        break;
                    case UiDrawKind.Button:
                    {
                        var baseColor = ToColor(command.Color);
                        var disabled = baseColor.R < 80 && baseColor.G < 80 && baseColor.B < 90;
                        var active = command.Color == UiColor.ButtonActive;
                        var brush = command.Focused ? _menuButtonFocus
                            : active ? _rowFocus
                            : command.Hovered ? _menuButtonHot
                            : disabled ? _menuButton
                            : new SolidBrush(Slate(baseColor));
                        try
                        {
                            g.FillRectangle(brush, rect);
                        }
                        finally
                        {
                            if (!ReferenceEquals(brush, _menuButton) &&
                                !ReferenceEquals(brush, _menuButtonHot) &&
                                !ReferenceEquals(brush, _menuButtonFocus) &&
                                !ReferenceEquals(brush, _rowFocus))
                                brush.Dispose();
                        }
                        if (command.Focused || active)
                            g.DrawRectangle(_menuFocusPen, rect.X, rect.Y, rect.Width, rect.Height);
                        else
                            g.DrawRectangle(_menuBorderPen, rect.X, rect.Y, rect.Width, rect.Height);
                        DrawCentered(g, command.Text, rect, _menuFont, disabled ? Brushes.Gray : active ? _rowLabelHot : Brushes.White);
                        break;
                    }
                    case UiDrawKind.Text:
                    {
                        var isTitle = rect.Height >= 32f && rect.Width >= 250f && !command.Wrap && command.Align != UiTextAlign.Right;
                        var font = isTitle ? _menuTitleFont : command.Text.Length > 86 || rect.Height <= 18f || command.Centered && rect.Height <= 28f ? _menuSmallFont : _menuFont;
                        var color = isTitle ? Color.FromArgb(242, 176, 64) : ToColor(command.Color);
                        using var brush = new SolidBrush(color);
                        if (command.Wrap)
                        {
                            using var wrapped = new StringFormat
                            {
                                Alignment = command.Align == UiTextAlign.Center ? StringAlignment.Center : StringAlignment.Near,
                                LineAlignment = StringAlignment.Center,
                                Trimming = StringTrimming.EllipsisWord
                            };
                            g.DrawString(command.Text, _menuFont, brush, rect, wrapped);
                        }
                        else if (isTitle || command.Centered || command.Text.Contains('↑') || command.Text.Contains('↓') ||
                            command.Text.Contains('←') || command.Text.Contains('→'))
                            DrawCentered(g, command.Text, rect, font, brush);
                        else
                        {
                            using var format = new StringFormat
                            {
                                Alignment = command.Align == UiTextAlign.Right ? StringAlignment.Far : StringAlignment.Near,
                                LineAlignment = StringAlignment.Center,
                                Trimming = StringTrimming.EllipsisCharacter,
                                FormatFlags = StringFormatFlags.NoWrap
                            };
                            g.DrawString(command.Text, font, brush,
                                new RectangleF(rect.X + 10, rect.Y, Math.Max(0, rect.Width - 20), rect.Height), format);
                        }
                        break;
                    }
                    case UiDrawKind.ItemSlot:
                        if (command.Stack is { } stack)
                        {
                            DrawSlot(g, rect, stack, command.Selected || command.Focused, command.Badge);
                            if (command.Hovered && !stack.IsEmpty)
                                _hoveredSlotStack = stack;
                        }
                        else
                        {
                            g.FillRectangle(_menuSlot, rect);
                            g.DrawRectangle(_menuBorderPen, rect.X, rect.Y, rect.Width, rect.Height);
                            g.DrawString(command.Text, _menuSmallFont, Brushes.White, rect.X + 5, rect.Y + 5);
                        }
                        break;
                    case UiDrawKind.Input:
                    {
                        using var fill = new SolidBrush(ToColor(command.Color, 245));
                        g.FillRectangle(fill, rect);
                        g.DrawRectangle(command.Focused ? _menuFocusPen : _menuBorderPen,
                            rect.X, rect.Y, rect.Width, rect.Height);
                        using var format = new StringFormat
                        {
                            Alignment = StringAlignment.Near,
                            LineAlignment = StringAlignment.Center,
                            Trimming = StringTrimming.EllipsisCharacter,
                            FormatFlags = StringFormatFlags.NoWrap
                        };
                        g.DrawString(command.Text, _menuFont,
                            command.Focused ? Brushes.White : Brushes.LightGray,
                            new RectangleF(rect.X + 12, rect.Y, Math.Max(0, rect.Width - 24), rect.Height),
                            format);
                        break;
                    }
                }
            }
        }

        private readonly Brush _rowFocus = new SolidBrush(Color.FromArgb(150, 92, 72, 30));
        private readonly Brush _rowHover = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
        private readonly Pen _rowRule = new(Color.FromArgb(28, 255, 255, 255), 1f);
        private readonly Brush _rowValue = new SolidBrush(Color.FromArgb(242, 176, 64));
        private readonly Brush _rowLabelHot = new SolidBrush(Color.FromArgb(255, 226, 150));
        private readonly Brush _sliderTrack = new SolidBrush(Color.FromArgb(200, 18, 20, 26));
        private readonly Font _menuHeaderFont = TechFont("Bahnschrift SemiBold", "Segoe UI Semibold", 10f, FontStyle.Regular);

        /// <summary>
        /// One list row, drawn the way Terraria's settings lists read: the setting on the left and its value on the right
        /// (a slider for percentages), section headers as small amber captions over a rule, notes as quiet text, ticks and
        /// choices with real boxes, and actions centred. The selected row is lit amber, the hovered one faintly.
        /// </summary>
        private void DrawMenuRow(Graphics g, RectangleF rect, MenuRowView row, bool enabled, bool adjustable, bool focused, bool hovered)
        {
            using var near = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using var far = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            switch (row.Kind)
            {
                case MenuRowKind.Header:
                {
                    var caption = row.Label.ToUpperInvariant();
                    var size = g.MeasureString(caption, _menuHeaderFont);
                    var y = rect.Y + rect.Height - 10;
                    g.DrawString(caption, _menuHeaderFont, _amberBrush, rect.X + 6, y - size.Height / 2f);
                    using var rule = new Pen(Color.FromArgb(110, 242, 176, 64), 1f);
                    g.DrawLine(rule, rect.X + 14 + size.Width, y, rect.Right - 6, y);
                    return;
                }
                case MenuRowKind.Info:
                    DrawCentered(g, row.Label, rect, _menuSmallFont, _dimTextBrush);
                    return;
            }

            if (focused)
            {
                g.FillRectangle(_rowFocus, rect);
                g.FillRectangle(_amberBrush, rect.X, rect.Y, 3, rect.Height);
            }
            else if (hovered && enabled)
                g.FillRectangle(_rowHover, rect);
            g.DrawLine(_rowRule, rect.X + 6, rect.Bottom, rect.Right - 6, rect.Bottom);
            var labelBrush = !enabled ? Brushes.Gray : focused ? _rowLabelHot : Brushes.White;
            var text = new RectangleF(rect.X + 14, rect.Y, rect.Width - 28, rect.Height);

            switch (row.Kind)
            {
                case MenuRowKind.Action:
                    DrawCentered(g, row.Label, rect, _menuFont, labelBrush);
                    return;
                case MenuRowKind.Check:
                case MenuRowKind.Radio:
                {
                    var box = new RectangleF(rect.X + 14, rect.Y + (rect.Height - 16) / 2f, 16, 16);
                    using var edge = new Pen(enabled ? Color.FromArgb(200, 242, 176, 64) : Color.FromArgb(120, 120, 120), 1.5f);
                    if (row.Kind == MenuRowKind.Check)
                    {
                        g.FillRectangle(_sliderTrack, box);
                        g.DrawRectangle(edge, box.X, box.Y, box.Width, box.Height);
                        if (row.Checked)
                        {
                            using var tick = new Pen(Color.FromArgb(255, 242, 176, 64), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                            g.DrawLines(tick, new[] { new PointF(box.X + 3.5f, box.Y + 8.5f), new PointF(box.X + 6.8f, box.Y + 12f), new PointF(box.X + 12.8f, box.Y + 4f) });
                        }
                    }
                    else
                    {
                        g.FillEllipse(_sliderTrack, box);
                        g.DrawEllipse(edge, box);
                        if (row.Checked)
                            g.FillEllipse(_amberBrush, box.X + 4, box.Y + 4, 8, 8);
                    }
                    var after = new RectangleF(box.Right + 10, rect.Y, rect.Right - box.Right - 24, rect.Height);
                    g.DrawString(row.Label, _menuFont, labelBrush, after, near);
                    if (row.Value is { } detail)
                        g.DrawString(detail, _menuSmallFont, enabled ? _dimTextBrush : Brushes.Gray, after, far);
                    return;
                }
            }

            // A setting: name on the left, value on the right; ‹ › when it can be stepped with left/right.
            g.DrawString(row.Label, _menuFont, labelBrush, text, near);
            var value = row.Value ?? string.Empty;
            var valueText = focused && adjustable ? $"‹  {value}  ›" : value;
            if (row.Slider is { } fraction)
            {
                const float trackWidth = 160f;
                var track = new RectangleF(text.Right - trackWidth, rect.Y + rect.Height / 2f - 4, trackWidth, 8);
                g.FillRectangle(_sliderTrack, track);
                g.FillRectangle(_amberBrush, track.X, track.Y, track.Width * Math.Clamp(fraction, 0f, 1f), track.Height);
                g.DrawRectangle(_menuBorderPen, track.X, track.Y, track.Width, track.Height);
                var knob = track.X + track.Width * Math.Clamp(fraction, 0f, 1f);
                g.FillRectangle(Brushes.White, knob - 2, track.Y - 4, 4, track.Height + 8);
                g.DrawString(value, _menuFont, _rowValue, new RectangleF(text.X, rect.Y, text.Width - trackWidth - 12, rect.Height), far);
            }
            else
                g.DrawString(valueText, _menuFont, !enabled || value is "unbound" or "Off" ? _dimTextBrush : _rowValue, text, far);
        }

        private string? _lastScreenId;
        private long _screenChangedAt;

        /// <summary>A short dark fade whenever the menu screen changes.</summary>
        private void DrawScreenFade(Graphics g, string? screenId)
        {
            if (screenId != _lastScreenId)
            {
                _lastScreenId = screenId;
                _screenChangedAt = Environment.TickCount64;
            }
            var t = (Environment.TickCount64 - _screenChangedAt) / 180f;
            if (t >= 1f)
                return;
            using var fade = new SolidBrush(Color.FromArgb((int)(150 * (1f - t)), 0, 0, 0));
            g.FillRectangle(fade, ClientRectangle);
        }

        /// <summary>The build's version, "0.1.3", from the project's &lt;Version&gt;.</summary>
        private static readonly string GameVersion = typeof(GraphicalSandbox).Assembly.GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}"
            : "dev";

        /// <summary>Title menu: animated logo and free-floating entries instead of a boxed panel.</summary>
        private void DrawMainMenu(Graphics g, MenuScreen screen)
        {
            var commands = _menu.Ui.BuildDrawCommands().ToArray();
            var buttons = commands.Where(command => command.Kind == UiDrawKind.Button).ToArray();
            var top = buttons.Length > 0 ? buttons.Min(command => command.Bounds.Y) : Height / 2f;
            var time = FrameClock.Seconds;

            // Logo: amber gradient lettering with a dark outline, gently bobbing.
            var bob = (float)Math.Sin(time * 1.6) * 4f;
            using (var path = new GraphicsPath())
            {
                using var family = LogoFamily();
                using var format = new StringFormat { Alignment = StringAlignment.Center };
                var logoRect = new RectangleF(0, top - 176 + bob, Width, 120);
                path.AddString("R I M T E R A", family, (int)FontStyle.Regular, 104f, logoRect, format);
                using var outline = new Pen(Color.FromArgb(230, 20, 14, 8), 8f) { LineJoin = LineJoin.Round };
                g.DrawPath(outline, path);
                using var fill = new LinearGradientBrush(logoRect, Color.FromArgb(255, 222, 120), Color.FromArgb(214, 112, 34), 90f);
                g.FillPath(fill, path);
            }
            DrawCentered(g, "A  NEO-TERRA  COLONY  SANDBOX", new RectangleF(0, top - 54 + bob, Width, 24), _menuSmallFont, _dimTextBrush);

            foreach (var command in buttons)
            {
                var rect = new RectangleF(command.Bounds.X, command.Bounds.Y, command.Bounds.Width, command.Bounds.Height);
                var enabled = !(command.Color.R < 80 && command.Color.G < 80 && command.Color.B < 90);
                var active = command.Focused || command.Hovered;
                var font = active ? _mainMenuActiveFont : _mainMenuFont;
                var brush = !enabled ? Brushes.DimGray : active ? _amberBrush : Brushes.Gainsboro;
                DrawCentered(g, command.Text, new RectangleF(rect.X + 2, rect.Y + 2, rect.Width, rect.Height), font, Brushes.Black);
                DrawCentered(g, command.Text, rect, font, brush);
            }

            var status = screen.StatusMessage.Length > 0 ? screen.StatusMessage : screen.Footer;
            DrawCentered(g, status, new RectangleF(0, Height - 64, Width, 20), _menuSmallFont, Brushes.Gainsboro);
            g.DrawString($"Rimtera {GameVersion}", _menuSmallFont, _dimTextBrush, 10, Height - 22);
            using var right = new StringFormat { Alignment = StringAlignment.Far };
            g.DrawString("↑ ↓  move      Enter  choose      Esc  back", _menuSmallFont, _dimTextBrush, new RectangleF(0, Height - 22, Width - 10, 20), right);
        }

        /// <summary>Left-hand area beside a creation screen's (right-shifted) option panel.</summary>
        private RectangleF PreviewArea()
        {
            var width = Math.Min(Width * 0.40f, 460f);
            var height = Math.Min(Height - 120f, 540f);
            return new RectangleF(Width * 0.05f, (Height - height) / 2f, width, height);
        }

        private void DrawCard(Graphics g, RectangleF rect, MenuEntry entry, bool focused, bool hovered)
        {
            g.FillRectangle(focused ? _menuButtonFocus : hovered ? _menuButtonHot : _menuButton, rect);
            g.DrawRectangle(focused ? _menuFocusPen : _menuBorderPen, rect.X, rect.Y, rect.Width, rect.Height);
            var portrait = new RectangleF(rect.X + 6, rect.Y + 4, rect.Height - 8, rect.Height - 8);
            g.FillRectangle(_menuSlot, portrait);
            var parts = entry.Label.Split("  •  ");
            var favorite = false;
            switch (entry.Tag)
            {
                case CharacterProfile character:
                {
                    favorite = character.Favorite;
                    var face = CharacterLook.Face(_sprites, character.AppearanceValue.Normalized());
                    if (_sprites.GetFitted(face, (int)portrait.Width - 4, (int)portrait.Height - 4) is { } bitmap)
                        g.DrawImageUnscaled(bitmap, (int)(portrait.X + (portrait.Width - bitmap.Width) / 2),
                            (int)(portrait.Y + (portrait.Height - bitmap.Height) / 2));
                    break;
                }
                case WorldProfile world:
                {
                    favorite = world.Favorite;
                    DrawWorldIcon(g, portrait, world.Blight, world.Seed);
                    break;
                }
                case CardIcon icon when _sprites.GetFitted(icon.Sprite, (int)portrait.Width - 4, (int)portrait.Height - 4) is { } art:
                {
                    var at = new Rectangle((int)(portrait.X + (portrait.Width - art.Width) / 2), (int)(portrait.Y + (portrait.Height - art.Height) / 2), art.Width, art.Height);
                    if (icon.Locked)
                    {
                        using var shadow = new ImageAttributes();
                        shadow.SetColorMatrix(new ColorMatrix { Matrix00 = 0f, Matrix11 = 0f, Matrix22 = 0f, Matrix33 = 0.9f, Matrix44 = 1f, Matrix40 = 0.3f, Matrix41 = 0.33f, Matrix42 = 0.4f });
                        g.DrawImage(art, at, 0, 0, art.Width, art.Height, GraphicsUnit.Pixel, shadow);
                    }
                    else
                        g.DrawImageUnscaled(art, at.X, at.Y);
                    break;
                }
            }

            var textX = portrait.Right + 10;
            g.DrawString(parts[0], _menuFont, _amberBrush, textX, rect.Y + 8);
            if (favorite)
                g.DrawString("FAVORITE", _menuSmallFont, _dimTextBrush, rect.Right - 78, rect.Y + 8);
            if (parts.Length > 1)
            {
                using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                g.DrawString(string.Join("  ·  ", parts.Skip(1)), _menuSmallFont, Brushes.Gainsboro,
                    new RectangleF(textX, rect.Y + 34, rect.Right - textX - 8, 18), format);
            }
        }

        /// <summary>Tiny biome stack: surface strip over the world's underground hazard.</summary>
        private void DrawWorldIcon(Graphics g, RectangleF area, WorldBlight blight, int seed)
        {
            var size = (int)(area.Width / 4);
            var resolved = WorldSettings.Resolve(blight, seed);
            string[] surface = ["tile_icy_soil", "tile_grassland", BlightSurfaceSprite(resolved), "tile_scrap_heap"];
            const string deep = "tile_obsidian";
            for (var column = 0; column < 4; column++)
            for (var row = 0; row < 4; row++)
            {
                var sprite = row == 0 ? surface[column] : row == 1 ? "terrain_regolith" : row == 2 ? "terrain_rock" : deep;
                if (_sprites.GetScaled(sprite, size, size) is { } tile)
                    g.DrawImageUnscaled(tile, (int)area.X + column * size, (int)area.Y + row * size);
            }
            if (_sprites.GetScaled("tile_molten_lava", size, size / 2) is { } lava)
                g.DrawImageUnscaled(lava, (int)area.X + size, (int)area.Y + size * 4 - size / 2);
        }

        private static string BlightSurfaceSprite(WorldBlight blight) =>
            blight == WorldBlight.Radiated ? "tile_radiated_soil" : "tile_mutant_flora";

        private void DrawCharacterPreview(Graphics g, RectangleF area)
        {
            DrawRivetedPanel(g, area);
            var draft = _menu.CharacterDraft;
            var appearance = draft.Appearance.Normalized();
            g.DrawString(draft.Name, _menuTitleFont, _amberBrush, area.X + 20, area.Y + 16);

            // Face portrait and full-body suit, both recoloured from the draft's appearance.
            var faceBox = new Rectangle((int)area.X + 20, (int)area.Y + 64, 150, 150);
            g.FillRectangle(_menuSlot, faceBox);
            g.DrawRectangle(_menuBorderPen, faceBox);
            if (_sprites.GetFitted(CharacterLook.Face(_sprites, appearance), faceBox.Width - 12, faceBox.Height - 12) is { } face)
                g.DrawImageUnscaled(face, faceBox.X + (faceBox.Width - face.Width) / 2, faceBox.Y + (faceBox.Height - face.Height) / 2);

            var bodyFrame = ((Environment.TickCount64 / 900) % 4) switch
            {
                0 => "colonist_front",
                1 => "colonist_side_a",
                2 => "colonist_back",
                _ => "colonist_side_b"
            };
            var bodyBox = new Rectangle(faceBox.Right + 20, faceBox.Y, (int)area.Right - faceBox.Right - 40, 220);
            if (_sprites.GetFitted(CharacterLook.Body(_sprites, bodyFrame, appearance), bodyBox.Width, bodyBox.Height) is { } body)
                g.DrawImageUnscaled(body, bodyBox.X + (bodyBox.Width - body.Width) / 2, bodyBox.Bottom - body.Height);

            var y = area.Y + 300;
            void Line(string label, string value)
            {
                g.DrawString(label, _menuSmallFont, _dimTextBrush, area.X + 20, y);
                using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                g.DrawString(value, _menuFont, Brushes.White, new RectangleF(area.X + 112, y - 3, area.Width - 130, 22), format);
                y += 28;
            }
            Line("Difficulty", WorldSettings.Name(draft.Difficulty));
            Line("Backstory", draft.BackstoryName);
            Line("Traits", draft.TraitSummary);
            Line("Look", appearance.Summary);
            Line("Focus", draft.FocusName);
            var top = draft.Stats.SkillLevels
                .Select((level, index) => (Level: level, Skill: (ColonistSkill)index))
                .OrderByDescending(item => item.Level)
                .Take(3)
                .Select(item => $"{ColonistCatalog.SkillName(item.Skill)} {item.Level}");
            Line("Best at", string.Join(" · ", top));
        }

        private void DrawWorldPreview(Graphics g, RectangleF area)
        {
            DrawRivetedPanel(g, area);
            var size = _menu.WorldSizeDraft;
            var blight = _menu.WorldBlightDraft;
            g.DrawString(_menu.WorldDraft, _menuTitleFont, _amberBrush, area.X + 20, area.Y + 16);

            // Schematic cross-section: surface biomes left→right over the hazard layer, scaled by size.
            var columns = Math.Clamp(size.Width / 12, 12, 26);
            var rows = Math.Clamp(size.Height / 12, 6, 12);
            var tile = (int)Math.Min((area.Width - 40) / columns, 220f / rows);
            var originX = (int)(area.X + (area.Width - columns * tile) / 2);
            var originY = (int)area.Y + 70;
            var resolved = WorldSettings.Resolve(blight, _menu.WorldSeed);
            var blightSurface = BlightSurfaceSprite(resolved);
            var blightDeep = resolved == WorldBlight.Radiated ? "tile_caustic_crystal" : "tile_mutant_flora";
            var blightColumn = columns * 3 / 5;
            var random = new Random(_menu.WorldSeed);
            for (var column = 0; column < columns; column++)
            {
                var surfaceRow = 1 + (int)(Math.Sin((column + _menu.WorldSeed % 7) * 0.6) * 0.8 + 0.8);
                var third = column * 3 / columns;
                for (var row = surfaceRow; row < rows; row++)
                {
                    string sprite;
                    var inBlight = Math.Abs(column - blightColumn) <= 1;
                    if (column == blightColumn && row > surfaceRow && row < rows - 2)
                        continue; // the chasm shaft
                    if (row == surfaceRow)
                        sprite = inBlight ? blightSurface : third == 0 ? "tile_icy_soil" : third == 1 ? "tile_grassland" : "tile_scrap_heap";
                    else if (row >= rows - 2)
                        sprite = random.Next(4) == 0 ? "tile_molten_lava" : "tile_obsidian";
                    else if (inBlight || (row >= rows - 4 && random.Next(4) == 0))
                        sprite = blightDeep;
                    else
                        sprite = row <= surfaceRow + 1 ? "terrain_regolith" : "terrain_rock";
                    if (_sprites.GetScaled(sprite, tile, tile) is { } bitmap)
                        g.DrawImageUnscaled(bitmap, originX + column * tile, originY + row * tile);
                }
            }

            var y = originY + rows * tile + 24f;
            void Line(string label, string value)
            {
                g.DrawString(label, _menuSmallFont, _dimTextBrush, area.X + 20, y);
                g.DrawString(value, _menuFont, Brushes.White, area.X + 112, y - 3);
                y += 28;
            }
            Line("Size", $"{size.Label}  ({size.Width}×{size.Height})");
            Line("Difficulty", WorldSettings.Name(_menu.WorldDifficultyDraft));
            Line("Blight", blight == WorldBlight.Random ? $"Random ({WorldSettings.Name(resolved)})" : WorldSettings.Name(blight));
            Line("Seed", _menu.WorldSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Line("Biomes", "Frozen · Grassland · Junk · Ocean · Irradiated Core");
        }

        private static void DrawCentered(Graphics g, string text, RectangleF rect, Font font, Brush brush)
        {
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            };
            g.DrawString(text, font, brush, rect, format);
        }

        /// <summary>Desaturates the framework's blue button tints into the Neo-Terra gunmetal palette.</summary>
        private static Color Slate(Color color)
        {
            var luminance = (color.R * 3 + color.G * 6 + color.B) / 10;
            return Color.FromArgb(color.A, Math.Max(0, luminance - 6), Math.Min(255, luminance), Math.Min(255, luminance + 10));
        }

        private static Color ToColor(UiColor color, byte? alpha = null) =>
            Color.FromArgb(alpha ?? color.A, color.R, color.G, color.B);

        private static Color ColorFor(TileId tile) => tile switch
        {
            TileId.Air => Color.Transparent,
            TileId.Soil => Color.FromArgb(120, 82, 48),
            TileId.Grass => Color.FromArgb(70, 160, 70),
            TileId.Rock => Color.FromArgb(95, 95, 110),
            TileId.CopperOre => Color.FromArgb(210, 120, 60),
            TileId.Sand => Color.FromArgb(210, 190, 120),
            TileId.Wood => Color.FromArgb(130, 90, 55),
            TileId.Bedrock => Color.FromArgb(30, 30, 40),
            TileId.Workbench => Color.FromArgb(160, 110, 60),
            TileId.Furnace => Color.FromArgb(180, 80, 50),
            TileId.Anvil => Color.FromArgb(120, 120, 130),
            TileId.Snow => Color.FromArgb(235, 240, 250),
            TileId.DesertSand => Color.FromArgb(205, 160, 90),
            TileId.JungleGrass => Color.FromArgb(40, 130, 80),
            _ => Color.FromArgb(80, 80, 90)
        };
    }
}
