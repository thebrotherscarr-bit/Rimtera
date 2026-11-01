using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>A dependency-free terminal frontend for the integrated simulation.</summary>
public static class ConsoleSandbox
{
    public static int Run(int seed = 48271, GameSimulation? restoredGame = null,
        Action<GameSimulation>? saveCallback = null, bool autoSave = false,
        int width = 160, int height = 72, CharacterProfile? character = null)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("The interactive frontend requires a terminal. Run `dotnet run -- --demo` for a headless simulation.");
            return 2;
        }

        var game = restoredGame ?? new GameSimulation(seed, width, height, character);
        var horizontal = 0f;
        var movementHold = 0;
        var frame = 0;
        var running = true;
        var uiOpen = false;
        var uiNeedsRedraw = true;
        var menus = new MenuFlow();
        menus.Open();
        var uiRenderer = new ConsoleUiRenderer(menus.Ui.Viewport.Width, menus.Ui.Viewport.Height);
        Console.CursorVisible = false;
        Console.Clear();

        try
        {
            while (running && !game.IsOver)
            {
                var action = PlayerAction.None;
                var jump = false;
                var dash = false;

                while (Console.KeyAvailable)
                {
                    var keyInfo = Console.ReadKey(intercept: true);
                    var key = keyInfo.Key;
                    if (uiOpen)
                    {
                        if (menus.Ui.CurrentState is TextPromptState)
                        {
                            switch (key)
                            {
                                case ConsoleKey.Escape:
                                    menus.CancelTextPrompt();
                                    break;
                                case ConsoleKey.Enter:
                                    menus.SubmitTextPrompt();
                                    break;
                                case ConsoleKey.Backspace:
                                    menus.EditText(TextEditCommand.Backspace);
                                    break;
                                case ConsoleKey.Delete:
                                    menus.EditText(TextEditCommand.Delete);
                                    break;
                                case ConsoleKey.LeftArrow:
                                    menus.EditText(TextEditCommand.MoveLeft);
                                    break;
                                case ConsoleKey.RightArrow:
                                    menus.EditText(TextEditCommand.MoveRight);
                                    break;
                                case ConsoleKey.Home:
                                    menus.EditText(TextEditCommand.Home);
                                    break;
                                case ConsoleKey.End:
                                    menus.EditText(TextEditCommand.End);
                                    break;
                                default:
                                    if (!char.IsControl(keyInfo.KeyChar))
                                        menus.InsertText(keyInfo.KeyChar.ToString());
                                    break;
                            }
                            uiNeedsRedraw = true;
                            continue;
                        }
                        if (menus.Ui.CurrentState is BindingCaptureState)
                        {
                            if (key == ConsoleKey.Escape)
                                menus.CancelBindingCapture();
                            else
                                menus.CaptureBinding(key);
                            uiNeedsRedraw = true;
                            continue;
                        }

                        if (menus.Ui.CurrentState is InventoryUiState inventory && TryGetHotbarIndex(menus.InputProfile, key, out var hotbar))
                        {
                            inventory.SelectHotbarSlot(hotbar);
                            uiNeedsRedraw = true;
                            continue;
                        }

                        var profile = menus.InputProfile;
                        var uiInput = InputBindingResolver.IsBound(profile, "MenuUp", key) ? new UiInputFrame(default, NavigationY: -1) :
                            InputBindingResolver.IsBound(profile, "MenuDown", key) ? new UiInputFrame(default, NavigationY: 1) :
                            InputBindingResolver.IsBound(profile, "MenuLeft", key) ? new UiInputFrame(default, NavigationX: -1) :
                            InputBindingResolver.IsBound(profile, "MenuRight", key) ? new UiInputFrame(default, NavigationX: 1) :
                            InputBindingResolver.IsBound(profile, "MenuConfirm", key) ? new UiInputFrame(default, ConfirmPressed: true) :
                            InputBindingResolver.IsBound(profile, "MenuBack", key) ? new UiInputFrame(default, BackPressed: true) :
                            new UiInputFrame(default);
                        menus.Handle(uiInput);
                        uiNeedsRedraw = true;
                        if (!uiOpen)
                            break;
                        continue;
                    }

                    var bindings = menus.InputProfile;
                    if (InputBindingResolver.IsBound(bindings, "MoveLeft", key)) { horizontal = -1f; movementHold = 8; }
                    else if (InputBindingResolver.IsBound(bindings, "MoveRight", key)) { horizontal = 1f; movementHold = 8; }
                    else if (InputBindingResolver.IsBound(bindings, "Jump", key)) jump = true;
                    // The console has no mouse, so a few keys are fixed here: X dash, M mine, E use, R replace, Tab next
                    // item, P pause, Q quit (the graphical game spends those keys on Terraria's layout).
                    else if (InputBindingResolver.IsBound(bindings, "Dash", key) || key == ConsoleKey.X) dash = true;
                    else if (key == ConsoleKey.M) action = PlayerAction.Mine;
                    else if (InputBindingResolver.IsBound(bindings, "UseSelected", key) || key == ConsoleKey.E) action = PlayerAction.UseSelected;
                    else if (InputBindingResolver.IsBound(bindings, "Eat", key)) action = PlayerAction.Eat;
                    else if (InputBindingResolver.IsBound(bindings, "ReplaceTile", key) || key == ConsoleKey.R) action = PlayerAction.ReplaceSelected;
                    else if (InputBindingResolver.IsBound(bindings, "Craft", key)) game.TryCraftNext();
                    else if (InputBindingResolver.IsBound(bindings, "CraftPrevious", key)) game.CycleCraftableRecipe(-1);
                    else if (InputBindingResolver.IsBound(bindings, "CraftNext", key)) game.CycleCraftableRecipe(1);
                    else if (InputBindingResolver.IsBound(bindings, "Guide", key)) game.ConsultGuide();
                    else if (InputBindingResolver.IsBound(bindings, "CycleInventory", key) || key == ConsoleKey.Tab) game.SelectNextInventoryItem();
                    else if (InputBindingResolver.IsBound(bindings, "Colonist", key))
                    {
                        uiOpen = true;
                        menus.ShowColonistSheet(game.Player.Colonist, game, () =>
                        {
                            uiOpen = false;
                            Console.Clear();
                        });
                        uiNeedsRedraw = true;
                    }
                    else if (InputBindingResolver.IsBound(bindings, "SummonBoss", key)) game.TrySummonSentinel();
                    else if (TryGetHotbarIndex(bindings, key, out var slot)) game.TrySelectInventorySlot(slot);
                    else if (InputBindingResolver.IsBound(bindings, "Inventory", key))
                    {
                        uiOpen = true;
                        menus.ShowInventory(new InventoryUiState(game.Player.Inventory), () =>
                        {
                            uiOpen = false;
                            Console.Clear();
                        });
                        uiNeedsRedraw = true;
                    }
                    else if (InputBindingResolver.IsBound(bindings, "Pause", key) || key == ConsoleKey.P)
                    {
                        uiOpen = true;
                        menus.ShowPauseMenu(
                            resume: () => { uiOpen = false; Console.Clear(); },
                            openInventory: () => menus.ShowInventory(new InventoryUiState(game.Player.Inventory)),
                            saveAndExit: () => running = false,
                            openColonist: () => menus.ShowColonistSheet(game.Player.Colonist, game));
                        uiNeedsRedraw = true;
                    }
                    else if (key == ConsoleKey.Q) running = false;
                }

                if (!running)
                    break;
                if (uiOpen)
                {
                    if (uiNeedsRedraw)
                    {
                        uiRenderer.Draw(menus.Ui.BuildDrawCommands());
                        uiNeedsRedraw = false;
                    }
                    Thread.Sleep(16);
                    continue;
                }

                if (movementHold > 0)
                    movementHold--;
                else
                    horizontal = 0f;

                var target = GetAimTile(game);
                var previousTick = game.TickNumber;
                game.Tick(new GameInput(
                    new MovementInput(horizontal, JumpPressed: jump, DashPressed: dash),
                    action,
                    target));
                if (autoSave && saveCallback is not null && game.TickNumber != previousTick && game.TickNumber % 600 == 0)
                    saveCallback(game);
                if ((frame++ % 2) == 0)
                    Render(game);
                Thread.Sleep(16);
            }
        }
        finally
        {
            Console.CursorVisible = true;
            Console.ResetColor();
            Console.WriteLine();
        }

        saveCallback?.Invoke(game);

        Console.WriteLine(game.IsOver ? "You were defeated. The world remains." : "Session ended.");
        return 0;
    }

    private static bool TryGetHotbarIndex(InputProfile profile, ConsoleKey key, out int index)
    {
        for (var slot = 0; slot < 10; slot++)
        {
            if (InputBindingResolver.IsBound(profile, $"Hotbar{slot + 1}", key))
            {
                index = slot;
                return true;
            }
        }
        index = -1;
        return false;
    }

    public static void PrintDemo()
    {
        var game = new GameSimulation();
        for (var i = 0; i < 600; i++)
        {
            var horizontal = i < 180 ? 1f : i < 300 ? 0f : -1f;
            game.Tick(new GameInput(new MovementInput(horizontal, JumpPressed: i == 60)));
        }

        var player = game.Player;
        Console.WriteLine($"Rimtera sandbox: {game.World.Width}x{game.World.Height}, day {game.Day} at {game.ClockHour:00.0}h.");
        Console.WriteLine($"Tiles: regolith {game.World.CountTiles(TileId.Soil)}, basalt {game.World.CountTiles(TileId.Rock)}, ferrite {game.World.CountTiles(TileId.CopperOre)}, cobalt {game.World.CountTiles(TileId.CobaltOre)}, viridium {game.World.CountTiles(TileId.ViridiumOre)}, crystal {game.World.CountTiles(TileId.EnergyCrystal)}, liquid {game.World.CountLiquidCells()} cells.");
        Console.WriteLine($"Player: ({player.Motion.Position.X:0.0},{player.Motion.Position.Y:0.0}), HP {player.HitPoints:0}/{player.MaximumHitPoints:0}, food {player.Needs.FoodReserve:P0}; actors {game.Enemies.Count}, active events {game.ActiveEvents.Count}.");
        Console.WriteLine("Run `dotnet run -- --play` for the interactive sandbox, or `dotnet run -- --self-test` for core checks.");
    }

    private static GridPoint GetAimTile(GameSimulation game)
    {
        var player = game.Player.Motion;
        var x = (int)MathF.Floor(player.Position.X + player.Size.X / 2f + player.Facing * 2.5f);
        var y = (int)MathF.Floor(player.Position.Y + player.Size.Y * 0.55f);
        return new GridPoint(Math.Clamp(x, 0, game.World.Width - 1), Math.Clamp(y, 0, game.World.Height - 1));
    }

    private static void Render(GameSimulation game)
    {
        var viewWidth = Math.Max(20, Console.WindowWidth - 1);
        var viewHeight = Math.Max(8, Console.WindowHeight - 7);
        var player = game.Player;
        var centerX = (int)MathF.Floor(player.Motion.Position.X + player.Motion.Size.X / 2f);
        var centerY = (int)MathF.Floor(player.Motion.Position.Y + player.Motion.Size.Y / 2f);
        var startX = Math.Clamp(centerX - viewWidth / 2, 0, Math.Max(0, game.World.Width - viewWidth));
        var startY = Math.Clamp(centerY - viewHeight / 2, 0, Math.Max(0, game.World.Height - viewHeight));
        var actorMap = game.Enemies.Where(enemy => enemy.IsAlive)
            .GroupBy(enemy => (X: (int)MathF.Floor(enemy.Motion.Position.X + enemy.Motion.Size.X / 2f),
                               Y: (int)MathF.Floor(enemy.Motion.Position.Y + enemy.Motion.Size.Y / 2f)))
            .ToDictionary(group => group.Key, group => group.First().Kind);

        Console.SetCursorPosition(0, 0);
        for (var row = 0; row < viewHeight; row++)
        {
            var y = startY + row;
            var line = new char[viewWidth];
            Array.Fill(line, ' ');
            for (var column = 0; column < viewWidth; column++)
            {
                var x = startX + column;
                if (x >= game.World.Width || y >= game.World.Height)
                    continue;
                var cell = game.World.GetCell(new GridPoint(x, y));
                line[column] = cell.LiquidDepth > 0.1f ? '~' : cell.TileId switch
                {
                    TileId.Air => cell.WallId == 0 ? ' ' : '.',
                    TileId.Soil => 's',
                    TileId.Rock => '#',
                    TileId.CopperOre => 'c',
                    TileId.Grass => 'g',
                    TileId.Sand => ':',
                    TileId.Wood => 'w',
                    TileId.Bedrock => 'X',
                    TileId.Workbench => '+',
                    TileId.Furnace => 'F',
                    TileId.Anvil => 'A',
                    TileId.CobaltOre => 'k',
                    TileId.ViridiumOre => 'v',
                    TileId.EnergyCrystal => '%',
                    TileId.MiniTurret => 'T',
                    TileId.HydroponicsTray => 'h',
                    TileId.SolarPanel => '=',
                    TileId.OxygenUnit => 'O',
                    _ => '?'
                };
            }

            foreach (var projectile in game.Projectiles)
            {
                var x = (int)MathF.Floor(projectile.Position.X) - startX;
                var yPos = (int)MathF.Floor(projectile.Position.Y) - startY;
                if ((uint)x < (uint)viewWidth && yPos == row)
                    line[x] = '*';
            }

            foreach (var (position, kind) in actorMap)
            {
                if (position.Y != y)
                    continue;
                var x = position.X - startX;
                if ((uint)x < (uint)viewWidth)
                    line[x] = EnemyGlyph(kind);
            }

            var guideX = (int)MathF.Floor(game.Guide.Position.X) - startX;
            var guideY = (int)MathF.Floor(game.Guide.Position.Y) - startY;
            if (guideY == row && (uint)guideX < (uint)viewWidth)
                line[guideX] = 'G';

            var playerX = (int)MathF.Floor(player.Motion.Position.X + player.Motion.Size.X / 2f) - startX;
            var playerY = (int)MathF.Floor(player.Motion.Position.Y + player.Motion.Size.Y / 2f) - startY;
            if (playerY == row && (uint)playerX < (uint)viewWidth)
                line[playerX] = '@';
            Console.Write(line);
            Console.WriteLine();
        }

        var selected = player.Inventory.SelectedItem;
        var count = player.Inventory.SelectedStack.Count;
        var events = game.ActiveEvents.Count == 0 ? "clear" : string.Join(',', game.ActiveEvents.Select(item => item.Kind));
        var selectedRecipe = game.SelectedRecipe?.Id ?? "none";
        WriteHud($"Day {game.Day} {game.ClockHour:00.0}h | HP {player.HitPoints:000}/{player.MaximumHitPoints:000} | mood {player.Mood:P0} | food {player.Needs.FoodReserve:P0} rest {player.Needs.RestReserve:P0} joy {player.Needs.JoyReserve:P0} | colonists {game.Colonists.Count} | mobs {game.Enemies.Count} | {events}", viewWidth);
        WriteHud($"Slot {player.Inventory.SelectedSlot + 1}: {selected?.Name ?? "empty"} x{count} | {game.LastMessage}", viewWidth);
        WriteHud("A/D move Space jump X dash | 1-0 slot, Tab next | M mine E use R replace | Esc inventory P pause Q quit", viewWidth);
        WriteHud($"Recipe {selectedRecipe} | [ ] choose C craft V guide F eat B boss Q quit", viewWidth);
        WriteHud("@ player * shot s bug m moth-ray b spore bat r mite y sky-ray z rat B boss | regolith s basalt # ferrite c cobalt k viridium v crystal % water ~ fabricator + refinery F forge A", viewWidth);
    }

    /// <summary>Lowercase for small creatures, uppercase for large ones, B for bosses, Z for the Astro-God.</summary>
    private static char EnemyGlyph(EnemyKind kind) => kind switch
    {
        EnemyKind.MossSlime => 's',
        EnemyKind.CaveMoth => 'm',
        EnemyKind.CaveBat => 'b',
        EnemyKind.RockGolem => 'D',
        EnemyKind.Wraith => 'w',
        EnemyKind.RockMite => 'r',
        EnemyKind.SkyRay => 'y',
        EnemyKind.SporeRat => 'z',
        EnemyKind.AstroBeast => 'H',
        EnemyKind.AstroGod => 'Z',
        _ when EnemyArchetypes.Get(kind).IsBoss => 'B',
        _ => '?'
    };

    private static void WriteHud(string text, int width)
    {
        if (text.Length > width)
            text = text[..width];
        Console.WriteLine(text.PadRight(width));
    }
}
