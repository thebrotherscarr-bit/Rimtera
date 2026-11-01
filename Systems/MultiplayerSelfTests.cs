using System.Drawing;
using System.Numerics;
using System.Text.Json;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Multiplayer groundwork (0.3.11, docs/MULTIPLAYER.md): player slots and commands, the game state changing only when the
/// world advances, a recorded session replaying to the same world, split-screen viewports, local devices and cameras.
/// </summary>
public static class MultiplayerSelfTests
{
    private static GameSimulation NewGame() => new(seed: 7711, width: 160, height: 72) { FamilySpawnsEnabled = false };

    /// <summary>A scripted session: walk, jump, dig and use what is held, for a few hundred steps.</summary>
    private static GameInput Scripted(GameSimulation game, int step)
    {
        var feet = new GridPoint((int)MathF.Floor(game.Player.Motion.Position.X + game.Player.Motion.Size.X / 2f), (int)MathF.Floor(game.Player.Motion.Position.Y + game.Player.Motion.Size.Y));
        return (step / 40 % 4) switch
        {
            0 => new GameInput(new MovementInput(1f, JumpPressed: step % 40 == 5, JumpHeld: step % 40 < 12)),
            1 => new GameInput(new MovementInput(0f), PlayerAction.Mine, new GridPoint(feet.X + 1, feet.Y)),
            2 => new GameInput(new MovementInput(-1f)),
            _ => new GameInput(new MovementInput(0f), PlayerAction.UseSelected, new GridPoint(feet.X - 2, feet.Y - 1))
        };
    }

    private static string Fingerprint(GameSimulation game)
    {
        var cells = game.World.Cells.ToArray();
        var hash = 17L;
        foreach (var cell in cells)
            hash = unchecked(hash * 31 + (int)cell.TileId * 7 + cell.WallId);
        var bag = string.Join(",", game.Player.Inventory.Slots.Select(stack => $"{stack.ItemId}:{stack.Count}"));
        var foes = string.Join(",", game.Enemies.Select(enemy => $"{enemy.Kind}@{enemy.Motion.Position.X:0.000},{enemy.Motion.Position.Y:0.000}"));
        return $"{game.TickNumber}|{game.Player.Motion.Position.X:0.0000},{game.Player.Motion.Position.Y:0.0000}|{game.Player.HitPoints:0.00}|{hash}|{bag}|{foes}";
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- slots: one player now, four later; commands go to a slot ----
        var game = NewGame();
        var refused = Enumerable.Range(1, GameSimulation.MaxLocalPlayers - 1).All(slot => !game.Submit(slot, GameSimulation.IdleInput)) &&
                      !game.Submit(-1, GameSimulation.IdleInput);
        check(GameSimulation.MaxLocalPlayers == 4 && game.PlayerSlots == 1 && game.Players.Count == 1 && ReferenceEquals(game.Players[0], game.Player) &&
              game.Submit(0, GameSimulation.IdleInput) && refused,
            "a world has player slots (one until co-op, up to four later); commands are sent to a slot, and a slot the world does not run is refused");

        // ---- the game changes only when it advances, never when a command is sent ----
        var still = NewGame();
        var before = Fingerprint(still);
        still.Submit(0, new GameInput(new MovementInput(1f, JumpPressed: true)));
        var unchanged = Fingerprint(still) == before;
        var x = still.Player.Motion.Position.X;
        still.Advance();
        var walked = still.TickNumber == 1;
        for (var i = 0; i < 30; i++)
        {
            still.Submit(0, new GameInput(new MovementInput(1f)));
            still.Advance();
        }
        var moved = still.Player.Motion.Position.X > x;
        for (var i = 0; i < 90; i++)
            still.Advance(); // nothing sent: the player slides to a stop (Terraria's momentum) and stands still
        var idleX = still.Player.Motion.Position.X;
        for (var i = 0; i < 60; i++)
            still.Advance();
        check(unchanged && walked && moved && MathF.Abs(still.Player.Motion.Position.X - idleX) < 0.01f,
            $"sending a command changes nothing until the world advances; a slot that sends nothing stands still " +
            $"(unchanged {unchanged}, stepped {walked}, walked {x:0.00} → {idleX:0.00}, idle → {still.Player.Motion.Position.X:0.00})");

        // ---- a recorded session replays to the same world, through JSON ----
        var recorded = NewGame();
        recorded.Recorder = new CommandLog();
        for (var step = 0; step < 480; step++)
        {
            recorded.Submit(0, Scripted(recorded, step));
            recorded.Advance();
        }
        var json = JsonSerializer.Serialize(recorded.Recorder.Capture());
        var commands = JsonSerializer.Deserialize<PlayerCommand[]>(json)!;
        var replayed = NewGame();
        replayed.Replay(commands);
        var same = Fingerprint(replayed) == Fingerprint(recorded);
        check(commands.Length == 480 && commands[0].Tick == 0 && commands[^1].Tick == 479 && commands.All(command => command.Slot == 0) && same,
            $"a session is its seed plus its commands: 480 recorded steps saved as JSON replay to the same world " +
            $"({(same ? "identical" : $"{Fingerprint(recorded)} vs {Fingerprint(replayed)}")})");

        // ---- split screen: one to four viewports cover the screen without overlapping ----
        var screen = new Rectangle(0, 0, 1920, 1080);
        var layoutsRight = Enumerable.Range(1, 4).All(players =>
        {
            var views = SplitScreen.Viewports(players, screen);
            var area = views.Sum(view => (long)view.Width * view.Height);
            var overlap = views.Any(a => views.Any(b => !a.Equals(b) && a.IntersectsWith(b)));
            return views.Count == players && area == (long)screen.Width * screen.Height && !overlap && views.All(screen.Contains);
        });
        var two = SplitScreen.Viewports(2, screen);
        check(layoutsRight && two[0].Width == 1920 && two[0].Height == 540 && SplitScreen.Viewports(4, screen)[3] == new Rectangle(960, 540, 960, 540),
            "split-screen gives one to four players their own viewport, covering the screen without overlap (two stack top and bottom)");

        // ---- local players: one device each, up to four; player 1 always plays ----
        var setup = new LocalPlayerSetup();
        var second = setup.Join(InputDevice.Gamepad1);
        var sameDevice = setup.Join(InputDevice.Gamepad1);
        setup.Join(InputDevice.Gamepad2);
        setup.Join(InputDevice.Gamepad3);
        var fifth = setup.Join(InputDevice.Gamepad4);
        var left = setup.Leave(1);
        var rejoined = setup.Join(InputDevice.Gamepad4);
        check(second is { Slot: 1 } && sameDevice is null && fifth is null && left && rejoined is { Slot: 1 } && !setup.Leave(0) &&
              setup.Players.Count == 4 && setup.For(InputDevice.KeyboardMouse) is { Slot: 0 },
            "local players join on a free device into the first free slot, up to four; a device drives one player, and player 1 stays");

        // ---- each player's camera follows them ----
        var camera = new PlayerCamera();
        camera.Follow(new Vector2(100f, 50f), smoothing: 1f, frameDelta: 1f / 60f);
        var snapped = camera.Center == new Vector2(100f, 50f);
        camera.Follow(new Vector2(104f, 50f), smoothing: 1f, frameDelta: 1f / 60f);
        var eased = camera.Center.X is > 100f and < 104f;
        camera.Follow(new Vector2(400f, 50f), smoothing: 1f, frameDelta: 1f / 60f);
        var teleported = camera.Center == new Vector2(400f, 50f);
        var corner = camera.TopLeft(48f, 27f, 420, 300);
        check(snapped && eased && teleported && corner == new Vector2(372f, 36.5f),
            "a player's camera snaps to them at first, eases with smoothing, snaps on a teleport, and keeps its view inside the world");
    }
}
