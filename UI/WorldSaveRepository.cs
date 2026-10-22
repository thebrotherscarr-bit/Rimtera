using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Separate per-world JSON snapshots with version gates and atomic replacement. The world's cells go in a
/// binary sidecar (<c>.rimtera.cells</c>, raw deflate) named by the envelope, so even the largest worlds save
/// and load without building multi-megabyte JSON strings. Older saves keep base64 cells inside the JSON.
/// </summary>
public sealed class WorldSaveRepository
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public WorldSaveRepository(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rimtera", "saves");
    }

    public bool Exists(Guid worldId) => File.Exists(GetPath(worldId));

    /// <summary>
    /// Removes a world's save from the active list. The file is moved to a "deleted" folder
    /// beside the saves rather than erased, so an accidental delete can be recovered by hand.
    /// </summary>
    public bool Delete(Guid worldId)
    {
        var path = GetPath(worldId);
        if (!File.Exists(path))
            return false;
        var archive = Path.Combine(_directory, "deleted");
        Directory.CreateDirectory(archive);
        var stamp = $"{worldId:N}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        File.Move(path, Path.Combine(archive, stamp + ".rimtera.json"), overwrite: true);
        if (File.Exists(GetCellsPath(worldId)))
            File.Move(GetCellsPath(worldId), Path.Combine(archive, stamp + ".rimtera.cells"), overwrite: true);
        return true;
    }

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
        Directory.CreateDirectory(_directory);
        var path = GetPath(worldId);
        var temporary = path + ".tmp";
        string? cellsFile = null;
        if (snapshot.CellData is { } cells)
        {
            // Cells first: the envelope only points at the sidecar once the sidecar is complete.
            var cellsPath = GetCellsPath(worldId);
            File.WriteAllBytes(cellsPath + ".tmp", cells);
            File.Move(cellsPath + ".tmp", cellsPath, overwrite: true);
            cellsFile = Path.GetFileName(cellsPath);
        }
        var envelope = new WorldSaveFile(
            WorldSaveFile.CurrentContainerVersion,
            worldId,
            DateTimeOffset.UtcNow,
            snapshot,
            characterId,
            characterName,
            cellsFile);
        using (var stream = File.Create(temporary))
            JsonSerializer.Serialize(stream, envelope, _options);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Reads the full save envelope, including the character association used by Continue/Load.</summary>
    public bool TryReadFile(Guid worldId, out WorldSaveFile? file)
    {
        file = null;
        if (worldId == Guid.Empty)
            return false;
        var path = GetPath(worldId);
        if (!File.Exists(path))
            return false;
        try
        {
            WorldSaveFile? envelope;
            using (var stream = File.OpenRead(path))
                envelope = JsonSerializer.Deserialize<WorldSaveFile>(stream, _options);
            if (envelope is null || envelope.ContainerVersion != WorldSaveFile.CurrentContainerVersion ||
                envelope.WorldId != worldId || envelope.Snapshot?.FormatVersion != GameSnapshot.CurrentFormatVersion)
                return false;
            if (envelope.CellsFile is not null)
            {
                // Only the sidecar this world's save names, inside the saves folder.
                var cellsPath = GetCellsPath(worldId);
                if (!string.Equals(envelope.CellsFile, Path.GetFileName(cellsPath), StringComparison.OrdinalIgnoreCase) || !File.Exists(cellsPath))
                    return false;
                envelope = envelope with { Snapshot = envelope.Snapshot with { CellData = File.ReadAllBytes(cellsPath) } };
            }
            file = envelope;
            return true;
        }
        catch (IOException) { return false; }
        catch (JsonException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public bool TryLoad(Guid worldId, out GameSnapshot? snapshot)
    {
        if (TryReadFile(worldId, out var file) && file is not null)
        {
            snapshot = file.Snapshot;
            return true;
        }
        snapshot = null;
        return false;
    }

    private string GetPath(Guid worldId) => Path.Combine(_directory, $"{worldId:N}.rimtera.json");

    private string GetCellsPath(Guid worldId) => Path.Combine(_directory, $"{worldId:N}.rimtera.cells");
}
