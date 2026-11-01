using System.Drawing;
using System.Numerics;

namespace Rimtera.UI;

/// <summary>
/// One player's camera (0.3.11, multiplayer groundwork; docs/MULTIPLAYER.md): it centres on its player every frame, as in
/// Terraria, or eases toward them when Camera smoothing is set (frame-rate independent); a teleport snaps it. Each local
/// player gets their own in split-screen co-op (1.1), drawn in their own viewport (<see cref="SplitScreen"/>).
/// </summary>
public sealed class PlayerCamera
{
    /// <summary>How fast the eased camera catches up, across and up/down.</summary>
    public const float FollowX = 14f, FollowY = 9f;
    /// <summary>A jump farther than this (tiles) snaps instead of easing.</summary>
    public const float SnapDistance = 24f;

    public Vector2 Center { get; private set; }
    public bool Ready { get; private set; }

    /// <summary>Moves toward <paramref name="target"/> for one frame.</summary>
    public void Follow(Vector2 target, float smoothing, float frameDelta)
    {
        if (!Ready || smoothing <= 0f || Vector2.Distance(Center, target) > SnapDistance)
            Center = target;
        else
            Center += new Vector2((target.X - Center.X) * (1f - MathF.Exp(-frameDelta * FollowX / smoothing)),
                (target.Y - Center.Y) * (1f - MathF.Exp(-frameDelta * FollowY / smoothing)));
        Ready = true;
    }

    /// <summary>The top-left tile of a view <paramref name="tilesWide"/> by <paramref name="tilesHigh"/>, kept inside the world.</summary>
    public Vector2 TopLeft(float tilesWide, float tilesHigh, int worldWidth, int worldHeight) => new(
        Math.Clamp(Center.X - tilesWide / 2f, 0f, Math.Max(0f, worldWidth - tilesWide)),
        Math.Clamp(Center.Y - tilesHigh / 2f, 0f, Math.Max(0f, worldHeight - tilesHigh)));
}

/// <summary>
/// Split-screen layout for local co-op (1.1): one player gets the whole screen; two stack top and bottom (wide views suit a
/// side-scroller); three put the first on top and the others side by side below; four take the quarters.
/// </summary>
public static class SplitScreen
{
    public static IReadOnlyList<Rectangle> Viewports(int players, Rectangle screen)
    {
        var halfW = screen.Width / 2;
        var halfH = screen.Height / 2;
        Rectangle Cell(int x, int y, int w, int h) => new(screen.X + x, screen.Y + y, w, h);
        return Math.Clamp(players, 1, Systems.GameSimulation.MaxLocalPlayers) switch
        {
            1 => [screen],
            2 => [Cell(0, 0, screen.Width, halfH), Cell(0, halfH, screen.Width, screen.Height - halfH)],
            3 => [Cell(0, 0, screen.Width, halfH), Cell(0, halfH, halfW, screen.Height - halfH), Cell(halfW, halfH, screen.Width - halfW, screen.Height - halfH)],
            _ => [Cell(0, 0, halfW, halfH), Cell(halfW, 0, screen.Width - halfW, halfH),
                  Cell(0, halfH, halfW, screen.Height - halfH), Cell(halfW, halfH, screen.Width - halfW, screen.Height - halfH)]
        };
    }
}

/// <summary>Where a local player's input comes from.</summary>
public enum InputDevice : byte { KeyboardMouse, Gamepad1, Gamepad2, Gamepad3, Gamepad4 }

/// <summary>A local player: their slot in the game and their input device.</summary>
public readonly record struct LocalPlayer(int Slot, InputDevice Device);

/// <summary>
/// Who is playing on this machine (0.3.11 groundwork for split-screen co-op in 1.1): player 1 on the keyboard and mouse;
/// others join on a free device, up to four, and each device drives one player. Only player 1 plays until co-op arrives.
/// </summary>
public sealed class LocalPlayerSetup
{
    private readonly List<LocalPlayer> _players = [new LocalPlayer(0, InputDevice.KeyboardMouse)];

    public IReadOnlyList<LocalPlayer> Players => _players;

    /// <summary>Adds a player on <paramref name="device"/> in the first free slot; null when four play or the device is taken.</summary>
    public LocalPlayer? Join(InputDevice device)
    {
        if (_players.Count >= Systems.GameSimulation.MaxLocalPlayers || _players.Any(player => player.Device == device))
            return null;
        var slot = Enumerable.Range(0, Systems.GameSimulation.MaxLocalPlayers).First(index => _players.All(player => player.Slot != index));
        var joined = new LocalPlayer(slot, device);
        _players.Add(joined);
        _players.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        return joined;
    }

    /// <summary>Removes a player (player 1 always stays).</summary>
    public bool Leave(int slot) => slot != 0 && _players.RemoveAll(player => player.Slot == slot) > 0;

    /// <summary>The player a device drives, if any.</summary>
    public LocalPlayer? For(InputDevice device) => _players.Cast<LocalPlayer?>().FirstOrDefault(player => player!.Value.Device == device);
}
