using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Rimtera.Entities;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>
/// The play bridge (docs/AGENT_PLAY.md): lets an outside AI player drive the real game window while a person watches.
/// A tiny HTTP server on 127.0.0.1 takes requests; every touch of the simulation happens on the window's own tick (Pump), so
/// the game stays single-threaded and deterministic. The world only advances while an /act command is running, so a slow model
/// never has the player standing in a fight. Started by <c>--agent-play</c>; nothing here runs otherwise.
/// </summary>
public static class AgentBridge
{
    private sealed class Job
    {
        public required Func<GameSimulation?, object> Work;
        public readonly TaskCompletionSource<object> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ActiveCommand
    {
        public float Move;
        public bool Jump, Down;
        public PlayerAction Action;
        public GridPoint? Target;
        public int Frames, Elapsed;
        public bool Finished;
        public readonly TaskCompletionSource<string> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly object Gate = new();
    private static readonly Queue<Job> Jobs = new();
    private static ActiveCommand? _command;
    private static DevConsole? _console;
    private static GameSimulation? _consoleGame;
    private static TcpListener? _listener;

    public static bool Active { get; private set; }

    /// <summary>True while a command still has frames to run; the window skips the tick otherwise (the world waits for the agent).</summary>
    public static bool WantsTick
    {
        get { lock (Gate) return _command is { Finished: false } c && c.Elapsed < c.Frames; }
    }

    public static void Start(int port)
    {
        if (Active)
            return;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Active = true;
        Console.Error.WriteLine($"[agent-play] listening on http://127.0.0.1:{port}  (loopback only)");
        _ = Task.Run(AcceptLoop);
    }

    // ---- window side (runs on the UI thread, once per tick) ----------------------------------------------------------

    /// <summary>Runs queued requests and finishes a command whose frames have all been played.</summary>
    public static void Pump(GameSimulation? game, Func<byte[]?> screenshot)
    {
        if (!Active)
            return;
        ScreenshotProvider = screenshot;
        ActiveCommand? finished = null;
        lock (Gate)
        {
            if (_command is { Finished: false } c && c.Elapsed >= c.Frames)
            {
                c.Finished = true;
                finished = c;
            }
        }
        if (finished is not null)
            finished.Done.TrySetResult(game is null ? "no game" : AgentObserver.Describe(game));
        while (true)
        {
            Job? job;
            lock (Gate)
                job = Jobs.Count > 0 ? Jobs.Dequeue() : null;
            if (job is null)
                break;
            try { job.Done.TrySetResult(job.Work(game)); }
            catch (Exception error) { job.Done.TrySetResult("error: " + error.Message); }
        }
    }

    private static Func<byte[]?>? ScreenshotProvider;

    /// <summary>Replaces the player's input with the agent's while a command is running (one frame per call).</summary>
    public static GameInput Filter(GameSimulation game, GameInput fromPlayer)
    {
        if (!Active)
            return fromPlayer;
        lock (Gate)
        {
            if (_command is not { Finished: false } c || c.Elapsed >= c.Frames)
                return new GameInput(new MovementInput(0f));
            var first = c.Elapsed == 0;
            c.Elapsed++;
            return new GameInput(
                new MovementInput(c.Move, JumpPressed: c.Jump && first, JumpHeld: c.Jump, Vertical: c.Down ? 1f : 0f),
                c.Action,
                c.Target);
        }
    }

    // ---- server side (background threads; they never touch the simulation directly) -------------------------------

    private static async Task AcceptLoop()
    {
        while (_listener is not null)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch { return; }
            _ = Task.Run(() => Handle(client));
        }
    }

    private static async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var (method, path, body) = await ReadRequest(stream);
                var (status, contentType, payload) = await Route(method, path, body);
                var head = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                await stream.WriteAsync(payload);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("[agent-play] request failed: " + error.Message);
            }
        }
    }

    private static async Task<(string Method, string Path, string Body)> ReadRequest(NetworkStream stream)
    {
        var buffer = new List<byte>();
        var chunk = new byte[4096];
        int headerEnd;
        while (true)
        {
            var read = await stream.ReadAsync(chunk);
            if (read <= 0)
                throw new IOException("closed");
            buffer.AddRange(chunk.AsSpan(0, read).ToArray());
            var text = Encoding.ASCII.GetString(buffer.ToArray());
            headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd >= 0)
                break;
            if (buffer.Count > 65536)
                throw new IOException("header too large");
        }
        var headerText = Encoding.ASCII.GetString(buffer.ToArray(), 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        var contentLength = 0;
        foreach (var line in lines.Skip(1))
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                int.TryParse(line.AsSpan(15).Trim(), out contentLength);
        var bodyStart = headerEnd + 4;
        while (buffer.Count - bodyStart < contentLength)
        {
            var read = await stream.ReadAsync(chunk);
            if (read <= 0)
                break;
            buffer.AddRange(chunk.AsSpan(0, read).ToArray());
        }
        var body = Encoding.UTF8.GetString(buffer.ToArray(), bodyStart, Math.Max(0, Math.Min(contentLength, buffer.Count - bodyStart)));
        return (requestLine[0], requestLine.Length > 1 ? requestLine[1] : "/", body);
    }

    private static Task<object> RunOnWindow(Func<GameSimulation?, object> work)
    {
        var job = new Job { Work = work };
        lock (Gate)
            Jobs.Enqueue(job);
        return job.Done.Task;
    }

    private static async Task<(string Status, string ContentType, byte[] Payload)> Route(string method, string path, string body)
    {
        var route = path.Split('?')[0];
        (string, string, byte[]) Ok(string text) => ("200 OK", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));
        (string, string, byte[]) Bad(string text, string status = "400 Bad Request") => (status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));

        JsonElement args = default;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try { args = JsonDocument.Parse(body).RootElement; }
            catch (JsonException) { return Bad("body is not valid JSON"); }
        }
        string Str(string name, string fallback = "") =>
            args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
        double Num(string name, double fallback = 0) =>
            args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
        bool Flag(string name) =>
            args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        switch (route)
        {
            case "/health":
                return Ok("ok");
            case "/observe":
            {
                var halfWidth = (int)Num("half_width", 32);
                var halfHeight = (int)Num("half_height", 14);
                var result = await RunOnWindow(game => game is null ? "no game yet (the title menu is open)" : AgentObserver.Describe(game, halfWidth, halfHeight));
                return Ok((string)result);
            }
            case "/act":
            {
                var frames = (int)Math.Clamp(Num("frames", 15), 1, 600);
                var command = new ActiveCommand
                {
                    Move = (float)Math.Clamp(Num("move"), -1, 1),
                    Jump = Flag("jump"),
                    Down = Flag("down"),
                    Frames = frames
                };
                var actionName = Str("action", "none");
                if (!Enum.TryParse<PlayerAction>(actionName.Replace("_", ""), ignoreCase: true, out var action))
                    return Bad($"unknown action '{actionName}'; use none, mine, useselected, eat, replaceselected, interact, sleep");
                command.Action = action;
                if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("target_x", out var tx) && args.TryGetProperty("target_y", out var ty) &&
                    tx.ValueKind == JsonValueKind.Number && ty.ValueKind == JsonValueKind.Number)
                    command.Target = new GridPoint(tx.GetInt32(), ty.GetInt32());
                lock (Gate)
                {
                    if (_command is { Finished: false })
                        return Bad("a command is still running", "409 Conflict");
                    _command = command;
                }
                // 60 ticks a second when the window runs; allow for menus or a slow frame before giving up.
                var finished = await Task.WhenAny(command.Done.Task, Task.Delay(TimeSpan.FromSeconds(frames / 20.0 + 10)));
                if (finished != command.Done.Task)
                {
                    lock (Gate)
                        command.Finished = true;
                    return Bad("timed out: is the game window paused or showing a menu?", "504 Gateway Timeout");
                }
                return Ok(await command.Done.Task);
            }
            case "/select":
            {
                var slot = (int)Num("slot", -1);
                var result = await RunOnWindow(game => game is null ? "no game yet" : game.TrySelectInventorySlot(slot) ? $"selected slot {slot}" : $"cannot select slot {slot} (empty or invalid)");
                return Ok((string)result);
            }
            case "/craft":
            {
                var id = Str("id");
                var result = await RunOnWindow(game =>
                {
                    if (game is null) return "no game yet";
                    var crafted = game.TryCraft(id);
                    return $"{(crafted.Success ? "crafted" : "not crafted")}: {crafted.Message}";
                });
                return Ok((string)result);
            }
            case "/console":
            {
                var line = Str("line");
                var result = await RunOnWindow(game =>
                {
                    if (game is null) return "no game yet";
                    if (!ReferenceEquals(game, _consoleGame))
                    {
                        _consoleGame = game;
                        _console = new DevConsole(game);
                    }
                    return string.Join("\n", _console!.Execute(line));
                });
                return Ok((string)result);
            }
            case "/screenshot":
            {
                var result = await RunOnWindow(_ => (object?)ScreenshotProvider?.Invoke() ?? "no screenshot available");
                return result is byte[] png ? ("200 OK", "image/png", png) : Bad((string)result, "503 Service Unavailable");
            }
            default:
                return Bad("routes: GET /health, POST /observe /act /select /craft /console, GET|POST /screenshot", "404 Not Found");
        }
    }
}
