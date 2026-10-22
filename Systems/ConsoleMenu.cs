using Rimtera.UI;

namespace Rimtera.Systems;

/// <summary>Terminal host for the retained menu tree and its world/character/options workflows.</summary>
public static class ConsoleMenu
{
    public static int Run()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("The interactive menu requires a terminal.");
            return 2;
        }

        var flow = new MenuFlow();
        var saveRepository = flow.Saves;
        var renderer = new ConsoleUiRenderer(flow.Ui.Viewport.Width, flow.Ui.Viewport.Height);
        var running = true;
        flow.ExitRequested += () => running = false;
        flow.WorldLaunchRequested += (world, character) =>
        {
            GameSimulation? restored = null;
            if (saveRepository.Exists(world.Id))
            {
                try
                {
                    if (!saveRepository.TryLoad(world.Id, out var snapshot) || snapshot is null)
                        throw new InvalidDataException("the save file exists but could not be read");
                    restored = GameSimulation.Restore(snapshot, character);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    Console.Clear();
                    Console.WriteLine($"Saved world could not be loaded: {ex.Message}");
                    Console.WriteLine("Press any key to return to the menu.");
                    Console.ReadKey(intercept: true);
                    return;
                }
            }

            void Save(GameSimulation game)
            {
                try { saveRepository.Save(world.Id, game.CaptureSnapshot(binaryCells: true), character.Id, character.Name); }
                catch (IOException ex)
                {
                    Console.Error.WriteLine($"Could not save {world.Name}: {ex.Message}");
                    Thread.Sleep(900);
                }
            }

            Console.Clear();
            Console.WriteLine($"Entering {world.Name} as {character.Name} (seed {world.Seed}){(restored is null ? "" : " · saved progress") }...");
            Thread.Sleep(700);
            ConsoleSandbox.Run(world.Seed, restored, Save, flow.Preferences.AutoSave, world.Width, world.Height, character);
            flow.Open();
        };
        flow.Open();
        Console.CursorVisible = false;
        try
        {
            renderer.Draw(flow.Ui.BuildDrawCommands());
            while (running)
            {
                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(40);
                    continue;
                }

                var keyInfo = Console.ReadKey(intercept: true);
                var key = keyInfo.Key;
                if (flow.Ui.CurrentState is TextPromptState)
                {
                    switch (key)
                    {
                        case ConsoleKey.Escape:
                            flow.CancelTextPrompt();
                            break;
                        case ConsoleKey.Enter:
                            flow.SubmitTextPrompt();
                            break;
                        case ConsoleKey.Backspace:
                            flow.EditText(TextEditCommand.Backspace);
                            break;
                        case ConsoleKey.Delete:
                            flow.EditText(TextEditCommand.Delete);
                            break;
                        case ConsoleKey.LeftArrow:
                            flow.EditText(TextEditCommand.MoveLeft);
                            break;
                        case ConsoleKey.RightArrow:
                            flow.EditText(TextEditCommand.MoveRight);
                            break;
                        case ConsoleKey.Home:
                            flow.EditText(TextEditCommand.Home);
                            break;
                        case ConsoleKey.End:
                            flow.EditText(TextEditCommand.End);
                            break;
                        default:
                            if (!char.IsControl(keyInfo.KeyChar))
                                flow.InsertText(keyInfo.KeyChar.ToString());
                            break;
                    }
                    renderer.Draw(flow.Ui.BuildDrawCommands());
                    continue;
                }
                if (flow.Ui.CurrentState is BindingCaptureState)
                {
                    if (key == ConsoleKey.Escape)
                        flow.CancelBindingCapture();
                    else
                        flow.CaptureBinding(key);
                    renderer.Draw(flow.Ui.BuildDrawCommands());
                    continue;
                }

                var profile = flow.InputProfile;
                var input = InputBindingResolver.IsBound(profile, "MenuUp", key) ? new UiInputFrame(default, NavigationY: -1) :
                    InputBindingResolver.IsBound(profile, "MenuDown", key) ? new UiInputFrame(default, NavigationY: 1) :
                    InputBindingResolver.IsBound(profile, "MenuLeft", key) ? new UiInputFrame(default, NavigationX: -1) :
                    InputBindingResolver.IsBound(profile, "MenuRight", key) ? new UiInputFrame(default, NavigationX: 1) :
                    InputBindingResolver.IsBound(profile, "MenuConfirm", key) ? new UiInputFrame(default, ConfirmPressed: true) :
                    InputBindingResolver.IsBound(profile, "MenuBack", key) || InputBindingResolver.IsBound(profile, "Quit", key)
                        ? new UiInputFrame(default, BackPressed: true)
                        : new UiInputFrame(default);
                flow.Handle(input);
                if (running)
                    renderer.Draw(flow.Ui.BuildDrawCommands());
            }
        }
        finally
        {
            Console.CursorVisible = true;
            Console.ResetColor();
            Console.Clear();
        }
        Console.WriteLine("Rimtera closed.");
        return 0;
    }
}

/// <summary>Minimal cell-based terminal adapter for retained UI draw commands.</summary>
internal sealed class ConsoleUiRenderer(float logicalWidth, float logicalHeight) : IUiRenderer
{
    public void Draw(IReadOnlyList<UiDrawCommand> commands)
    {
        var width = Math.Max(40, Console.WindowWidth);
        var height = Math.Max(14, Console.WindowHeight);
        Console.SetCursorPosition(0, 0);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.BackgroundColor = ConsoleColor.Black;
        Console.Clear();

        foreach (var command in commands)
        {
            var x = ToColumn(command.Bounds.X, width);
            var y = ToRow(command.Bounds.Y, height);
            var right = Math.Clamp(ToColumn(command.Bounds.Right, width), x + 1, width - 1);
            var bottom = Math.Clamp(ToRow(command.Bounds.Bottom, height), y + 1, height - 1);
            switch (command.Kind)
            {
                case UiDrawKind.Panel:
                    DrawPanel(x, y, right, bottom);
                    break;
                case UiDrawKind.Button:
                    Console.ForegroundColor = command.Focused || command.Hovered ? ConsoleColor.Yellow : ConsoleColor.White;
                    WriteAt(x + 2, y + Math.Max(0, (bottom - y) / 2), Fit(command.Text, right - x - 4));
                    break;
                case UiDrawKind.Text:
                    Console.ForegroundColor = command.Text.Contains("navigate", StringComparison.OrdinalIgnoreCase)
                        ? ConsoleColor.DarkGray : ConsoleColor.Cyan;
                    var centered = command.Text.Contains("↑/↓", StringComparison.Ordinal) || command.Text.Contains("←/→", StringComparison.Ordinal);
                    var textX = centered ? x + Math.Max(0, (right - x - command.Text.Length) / 2) : x + 2;
                    WriteAt(textX, y + Math.Max(0, (bottom - y) / 2), Fit(command.Text, right - textX - 1));
                    break;
                case UiDrawKind.ItemSlot:
                    Console.ForegroundColor = command.Focused ? ConsoleColor.Yellow : ConsoleColor.Gray;
                    WriteAt(x, y, Fit(command.Text, right - x));
                    break;
                case UiDrawKind.Input:
                    Console.ForegroundColor = command.Focused ? ConsoleColor.Yellow : ConsoleColor.White;
                    WriteAt(x + 2, y + Math.Max(0, (bottom - y) / 2), Fit(command.Text, right - x - 4));
                    break;
            }
        }
        Console.ForegroundColor = ConsoleColor.DarkGray;
        WriteAt(1, height - 1, "Rimtera · local profiles and preferences");
        Console.ResetColor();
    }

    private int ToColumn(float logical) => ToColumn(logical, Math.Max(40, Console.WindowWidth));
    private int ToColumn(float logical, int width) => Math.Clamp((int)MathF.Round(logical / logicalWidth * (width - 1)), 0, width - 1);
    private int ToRow(float logical, int height) => Math.Clamp((int)MathF.Round(logical / logicalHeight * (height - 1)), 0, height - 1);

    private static void DrawPanel(int left, int top, int right, int bottom)
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        WriteAt(left, top, "╭" + new string('─', Math.Max(0, right - left - 1)) + "╮");
        for (var row = top + 1; row < bottom; row++)
        {
            WriteAt(left, row, "│");
            WriteAt(right, row, "│");
        }
        WriteAt(left, bottom, "╰" + new string('─', Math.Max(0, right - left - 1)) + "╯");
    }

    private static string Fit(string text, int max) => max <= 0 ? string.Empty : text.Length <= max ? text : text[..max];

    private static void WriteAt(int x, int y, string text)
    {
        if (y < 0 || y >= Console.WindowHeight || x < 0 || x >= Console.WindowWidth)
            return;
        Console.SetCursorPosition(x, y);
        var safe = Fit(text, Console.WindowWidth - x);
        Console.Write(safe.PadRight(Math.Min(safe.Length, Console.WindowWidth - x)));
    }
}
