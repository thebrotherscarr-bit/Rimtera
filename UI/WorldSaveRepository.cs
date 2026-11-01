using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Rimtera.Systems;

namespace Rimtera.UI;

public sealed record WorldSaveFile(
    int ContainerVersion,
    Guid WorldId,
    DateTimeOffset SavedAt,
    GameSnapshot Snapshot,
    Guid? CharacterId = null,
    string? CharacterName = null,
    string? CellsFile = null)
{
    public const int CurrentContainerVersion = 1;
}

/// <summary>
/// Separate per-world JSON snapshots with version gates and atomic replacement. The world's cells go in a binary sidecar
/// (raw deflate) named by the envelope, so even the largest worlds save and load without building multi-megabyte JSON strings.
/// Older saves keep base64 cells inside the JSON.
/// <para>
/// Save safety (PU-11): each save writes a new stamped sidecar <c>&lt;id&gt;.&lt;stamp&gt;.cells</c> first, then the envelope that
/// names it (the previous envelope becomes <c>&lt;id&gt;.rimtera.json.bak</c>), then deletes the sidecars neither envelope names.
/// A crash at any point leaves a consistent envelope and sidecar pair. Older formats load through
/// <see cref="SaveMigrations"/>; newer ones are refused with a message.
/// </para>
/// </summary>
public sealed class WorldSaveRepository
{
    private readonly string _directory;
    private readonly object _writeGate = new();
    private long _lastStamp;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public WorldSaveRepository(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rimtera", "saves");
    }

    public string Directory => _directory;

    /// <summary>Test hook: runs after the new sidecar is written and before the envelope is (a crash simulated here must leave the old pair loadable).</summary>
    internal Action? AfterCellsWritten { get; set; }

    public bool Exists(Guid worldId) => File.Exists(GetPath(worldId));

    /// <summary>True when the world has a backup of its previous save (<c>.rimtera.json.bak</c>).</summary>
    public bool HasBackup(Guid worldId) => worldId != Guid.Empty && File.Exists(SafeFile.BackupPath(GetPath(worldId)));

    /// <summary>
    /// Removes a world's save from the active list. The files are moved to a "deleted" folder beside the saves rather than
    /// erased, so an accidental delete can be recovered by hand.
    /// </summary>
    public bool Delete(Guid worldId)
    {
        var path = GetPath(worldId);
        if (!File.Exists(path) && !HasBackup(worldId))
            return false;
        var archive = Path.Combine(_directory, "deleted");
        System.IO.Directory.CreateDirectory(archive);
        var stamp = $"{worldId:N}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        lock (_writeGate)
        {
            if (File.Exists(path))
                File.Move(path, Path.Combine(archive, stamp + ".rimtera.json"), overwrite: true);
            if (File.Exists(SafeFile.BackupPath(path)))
                File.Move(SafeFile.BackupPath(path), Path.Combine(archive, stamp + ".rimtera.json.bak"), overwrite: true);
            foreach (var cells in SidecarsOf(worldId))
                File.Move(cells, Path.Combine(archive, stamp + "." + Path.GetFileName(cells)), overwrite: true);
        }
        return true;
    }

    /// <summary>Writes a save (safe to call from a worker thread; concurrent saves of one repository run one at a time).</summary>
    public void Save(
        Guid worldId,
        GameSnapshot snapshot,
        Guid? characterId = null,
        string? characterName = null)
    {
        if (worldId == Guid.Empty)
            throw new ArgumentException("A world ID is required.", nameof(worldId));
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.FormatVersion != GameSnapshot.CurrentFormatVersion)
            throw new InvalidDataException("Unsupported Rimtera snapshot version.");
        snapshot = snapshot.WithCompressedCells(); // an autosave's deferred compression runs here, on the worker
        lock (_writeGate)
        {
            System.IO.Directory.CreateDirectory(_directory);
            var path = GetPath(worldId);
            string? cellsFile = null;
            if (snapshot.CellData is { } cells)
            {
                // 1. The new stamped sidecar; the current envelope still names the old one, which stays untouched.
                var cellsPath = Path.Combine(_directory, $"{worldId:N}.{NextStamp():x16}.cells");
                File.WriteAllBytes(cellsPath + ".tmp", cells);
                File.Move(cellsPath + ".tmp", cellsPath, overwrite: true);
                cellsFile = Path.GetFileName(cellsPath);
            }
            AfterCellsWritten?.Invoke();
            // 2. The envelope naming it; the previous envelope becomes the backup.
            var envelope = new WorldSaveFile(
                WorldSaveFile.CurrentContainerVersion,
                worldId,
                DateTimeOffset.UtcNow,
                snapshot,
                characterId,
                characterName,
                cellsFile);
            SafeFile.Write(path, stream => JsonSerializer.Serialize(stream, envelope, _options));
            // 3. Sidecars neither the save nor its backup names.
            RemoveStaleSidecars(worldId, cellsFile);
        }
    }

    private long NextStamp()
    {
        var stamp = Math.Max(DateTime.UtcNow.Ticks, _lastStamp + 1);
        _lastStamp = stamp;
        return stamp;
    }

    private void RemoveStaleSidecars(Guid worldId, string? current)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (current is not null)
            keep.Add(current);
        if (ReadCellsFileName(SafeFile.BackupPath(GetPath(worldId))) is { } backup)
            keep.Add(backup);
        foreach (var sidecar in SidecarsOf(worldId))
        {
            if (keep.Contains(Path.GetFileName(sidecar)))
                continue;
            try
            {
                File.Delete(sidecar);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A sidecar that cannot be deleted now is retried on the next save.
            }
        }
    }

    /// <summary>Every cell sidecar of a world on disk: stamped ones and the older unstamped <c>&lt;id&gt;.rimtera.cells</c>.</summary>
    private IEnumerable<string> SidecarsOf(Guid worldId) =>
        System.IO.Directory.Exists(_directory)
            ? System.IO.Directory.EnumerateFiles(_directory, $"{worldId:N}.*.cells").Where(path => IsSidecarName(worldId, Path.GetFileName(path))).ToArray()
            : [];

    private static bool IsSidecarName(Guid worldId, string name) =>
        string.Equals(name, $"{worldId:N}.rimtera.cells", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(name, $"^{worldId:N}\\.[0-9a-f]{{16}}\\.cells$", RegexOptions.IgnoreCase);

    /// <summary>The <c>CellsFile</c> an envelope names, read without building the snapshot (null when none or unreadable).</summary>
    private static string? ReadCellsFileName(string envelopePath)
    {
        if (!File.Exists(envelopePath))
            return null;
        try
        {
            var reader = new Utf8JsonReader(File.ReadAllBytes(envelopePath));
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                if (!reader.Read())
                    return null;
                if (string.Equals(name, nameof(WorldSaveFile.CellsFile), StringComparison.OrdinalIgnoreCase))
                    return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                reader.Skip();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
        return null;
    }

    /// <summary>Reads the full save envelope, including the character association used by Continue/Load.</summary>
    public bool TryReadFile(Guid worldId, out WorldSaveFile? file) => TryReadFile(worldId, out file, out _);

    /// <summary>
    /// Reads the save (or, with <paramref name="backup"/>, its backup), migrating older formats. On failure
    /// <paramref name="error"/> says why: damaged, missing its cells, or made by a newer version.
    /// </summary>
    public bool TryReadFile(Guid worldId, out WorldSaveFile? file, out string? error, bool backup = false)
    {
        file = null;
        error = null;
        if (worldId == Guid.Empty)
        {
            error = "no world";
            return false;
        }
        var path = backup ? SafeFile.BackupPath(GetPath(worldId)) : GetPath(worldId);
        if (!File.Exists(path))
        {
            error = backup ? "there is no backup" : "the save file is missing";
            return false;
        }
        try
        {
            WorldSaveFile? envelope;
            using (var stream = File.OpenRead(path))
                envelope = JsonSerializer.Deserialize<WorldSaveFile>(stream, _options);
            if (envelope?.Snapshot is null || envelope.WorldId != worldId)
            {
                error = "the save file is damaged";
                return false;
            }
            if (envelope.ContainerVersion > WorldSaveFile.CurrentContainerVersion)
            {
                error = "it was saved by a newer version of Rimtera. Update the game to play it";
                return false;
            }
            if (SaveMigrations.RefusalFor(envelope.Snapshot.FormatVersion) is { } refusal)
            {
                error = refusal;
                return false;
            }
            var snapshot = SaveMigrations.Upgrade(envelope.Snapshot, envelope.CharacterId);
            if (envelope.CellsFile is { } cellsName)
            {
                // Only a sidecar of this world, inside the saves folder.
                var cellsPath = Path.Combine(_directory, cellsName);
                if (!IsSidecarName(worldId, cellsName) || !File.Exists(cellsPath))
                {
                    error = "the world's cell data is missing";
                    return false;
                }
                snapshot = snapshot with { CellData = File.ReadAllBytes(cellsPath) };
            }
            file = envelope with { Snapshot = snapshot };
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            error = ex is JsonException ? "the save file is damaged" : ex.Message;
            return false;
        }
    }

    public bool TryLoad(Guid worldId, out GameSnapshot? snapshot) => TryLoad(worldId, out snapshot, out _);

    public bool TryLoad(Guid worldId, out GameSnapshot? snapshot, out string? error, bool backup = false)
    {
        if (TryReadFile(worldId, out var file, out error, backup) && file is not null)
        {
            snapshot = file.Snapshot;
            return true;
        }
        snapshot = null;
        return false;
    }

    private string GetPath(Guid worldId) => Path.Combine(_directory, $"{worldId:N}.rimtera.json");
}

/// <summary>
/// When and how the window saves (PU-11, PU-12, D-42). Autosaves capture the snapshot on the game thread and serialise and
/// write it on a worker; a tick that comes while the last autosave is still writing is skipped. Closing the window always
/// saves an active world, whatever the Autosave setting. Every write goes through <see cref="WorldSaveRepository"/>, so the
/// backup rotation applies.
/// </summary>
public sealed class SaveCoordinator
{
    private readonly WorldSaveRepository _saves;
    private Task? _pending;

    public SaveCoordinator(WorldSaveRepository saves) => _saves = saves ?? throw new ArgumentNullException(nameof(saves));

    /// <summary>True while a background save is still writing.</summary>
    public bool Busy => _pending is { IsCompleted: false };

    /// <summary>The background save in flight (or the last one), for callers that must wait.</summary>
    public Task? Pending => _pending;

    /// <summary>Autosaves skipped because the previous one was still writing.</summary>
    public int Skipped { get; private set; }

    /// <summary>The last background save's error, if it failed.</summary>
    public string? LastError { get; private set; }

    /// <summary>D-42: closing the window saves an active world even with Autosave off.</summary>
    public static bool SavesOnClose(bool worldActive, bool autosaveEnabled) => worldActive;

    /// <summary>
    /// Starts an autosave: <paramref name="game"/> is captured now (call on the game thread) and the files are written on a
    /// worker. <paramref name="afterWorld"/> (for example the map writer) runs on the worker after the world. Returns false
    /// and skips this tick when a save is still running.
    /// </summary>
    public bool TryBeginAutosave(GameSimulation game, Guid worldId, Guid? characterId, string? characterName, Action? afterWorld = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (Busy)
        {
            Skipped++;
            return false;
        }
        var snapshot = game.CaptureSnapshot(binaryCells: true, deferCellCompression: true);
        _pending = Task.Run(() =>
        {
            try
            {
                _saves.Save(worldId, snapshot, characterId, characterName);
                afterWorld?.Invoke();
                LastError = null;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                LastError = ex.Message;
            }
        });
        return true;
    }

    /// <summary>Saves now on the calling thread (after any background save finishes). Throws on failure.</summary>
    public void SaveNow(GameSimulation game, Guid worldId, Guid? characterId, string? characterName, Action? afterWorld = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        WaitForPending();
        _saves.Save(worldId, game.CaptureSnapshot(binaryCells: true), characterId, characterName);
        afterWorld?.Invoke();
    }

    /// <summary>Waits for a background save still writing (the repository would serialise the writes anyway).</summary>
    public void WaitForPending()
    {
        try
        {
            _pending?.Wait();
        }
        catch (AggregateException)
        {
        }
    }
}
