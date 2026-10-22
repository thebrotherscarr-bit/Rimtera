using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// One connected pocket of open (non-solid) cells. A region counts as a room when it is
/// enclosed (never reaches the world edge), small enough to be a building or a small cave,
/// and almost every cell is either wall-backed or has something solid overhead.
/// </summary>
public sealed class RoomInfo
{
    private readonly List<int> _cells;

    internal RoomInfo(int id, List<int> cells)
    {
        Id = id;
        _cells = cells;
    }

    public int Id { get; }
    public int CellCount { get; internal set; }
    public bool TouchesMapEdge { get; internal set; }
    /// <summary>Cells with open sky straight above them (no solid tile anywhere higher in the column).</summary>
    public int OpenRoofCells { get; internal set; }
    /// <summary>Cells with a background wall or a roof overhead.</summary>
    public int ShelteredCells { get; internal set; }
    public bool IsRoom { get; internal set; }
    public Vector2 Centroid { get; internal set; }
    /// <summary>Indoor temperature in °C; NaN until the temperature model first visits the room.</summary>
    public float Temperature { get; internal set; } = float.NaN;
    public float OpenRoofFraction => CellCount == 0 ? 1f : OpenRoofCells / (float)CellCount;
    /// <summary>
    /// Large regions (outdoors, huge caverns) are not tracked cell by cell; after local edits their
    /// counts are approximate. They are never rooms.
    /// </summary>
    public bool IsLarge { get; internal set; }
    /// <summary>False once a local rebuild has replaced this region.</summary>
    public bool IsLive { get; internal set; } = true;

    /// <summary>Row-major cell indices; kept for every tracked (non-large) region.</summary>
    internal IReadOnlyList<int> CellIndices => _cells;
    internal List<int> MutableCells => _cells;
}

/// <summary>
/// Converts world float positions to grid cells and region ids, flood-filling open cells into
/// regions bounded by solid tiles. After tile edits only the regions around the edited cells are
/// rebuilt (a bounded flood fill from the dirty cells); a full rebuild runs on the first build,
/// after bulk loads, or when a burst of edits exceeds <see cref="IncrementalDirtyLimit"/>.
/// Room temperatures carry across rebuilds cell by cell.
/// </summary>
public sealed class RegionMap
{
    public const int MaxRoomCells = 400;
    public const float MinimumShelteredFraction = 0.9f;
    /// <summary>Components up to this size are tracked exactly; anything bigger is a large region.</summary>
    public const int SmallRegionLimit = 1024;
    /// <summary>More dirty cells than this in one update fall back to a full rebuild.</summary>
    public const int IncrementalDirtyLimit = 512;

    private const int Unassigned = -2;
    private const int SolidLabel = -1;

    private readonly TileGrid _world;
    private readonly TileCatalog _tiles;
    private int[] _labels;
    private readonly List<RoomInfo> _regions = new();
    private readonly List<RoomInfo> _live = new();
    private bool _liveDirty = true;
    private int[] _skyLine;
    private long _builtRevision = -1;
    private long _observedChanges;
    private readonly List<int> _dirty = new();
    private readonly bool[] _dirtyMark;
    private readonly int[] _visitStamp;
    private int _stamp;
    private readonly List<int> _queue = new();
    private readonly HashSet<int> _knownLarge = new();
    private readonly sbyte[] _barrier; // 0 unknown, 1 open, 2 barrier (cached per tile id)

    public RegionMap(TileGrid world, TileCatalog tiles)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        _labels = new int[world.Count];
        _skyLine = new int[world.Width];
        _dirtyMark = new bool[world.Count];
        _visitStamp = new int[world.Count];
        _barrier = new sbyte[Enum.GetValues<TileId>().Max(id => (int)id) + 1];
        _world.CellChanged += OnCellChanged;
    }

    /// <summary>Number of rebuilds (full or local) so far (handy for HUD/debug overlays).</summary>
    public int BuildCount { get; private set; }
    /// <summary>Number of full rebuilds so far.</summary>
    public int FullBuildCount { get; private set; }
    /// <summary>Cells visited by the most recent local rebuild (work measure).</summary>
    public int LastIncrementalWork { get; private set; }

    public IReadOnlyList<RoomInfo> Regions
    {
        get
        {
            EnsureCurrent();
            if (_liveDirty)
            {
                _live.Clear();
                foreach (var region in _regions)
                    if (region.IsLive)
                        _live.Add(region);
                _liveDirty = false;
            }
            return _live;
        }
    }

    public IEnumerable<RoomInfo> Rooms => Regions.Where(region => region.IsRoom);

    /// <summary>Task 2.3: a world-space float position becomes the grid cell containing it.</summary>
    public static GridPoint CellAt(Vector2 worldPosition) =>
        new((int)MathF.Floor(worldPosition.X), (int)MathF.Floor(worldPosition.Y));

    /// <summary>Region id of a cell, or -1 for solid or out-of-world cells.</summary>
    public int GetRegionId(GridPoint cell)
    {
        if (!_world.Contains(cell))
            return -1;
        EnsureCurrent();
        return _labels[cell.Y * _world.Width + cell.X];
    }

    public int GetRegionId(Vector2 worldPosition) => GetRegionId(CellAt(worldPosition));

    public RoomInfo? GetRegion(GridPoint cell)
    {
        var id = GetRegionId(cell);
        return id < 0 ? null : _regions[id];
    }

    /// <summary>The enclosed room containing a position, or null outdoors/in big caves.</summary>
    public RoomInfo? GetRoomAt(Vector2 worldPosition) => GetRegion(CellAt(worldPosition)) is { IsRoom: true } room ? room : null;

    /// <summary>Rebuilds (locally when possible) if any tile or wall changed. Returns true when it rebuilt.</summary>
    public bool EnsureCurrent()
    {
        if (_builtRevision == _world.TileRevision)
            return false;
        var silent = _world.TileRevision != _builtRevision + _observedChanges;
        if (_builtRevision < 0 || silent || _dirty.Count > IncrementalDirtyLimit)
            Rebuild();
        else
            UpdateLocal();
        return true;
    }

    /// <summary>Forces a full flood-fill rebuild (tests and debug tools).</summary>
    public void ForceFullRebuild() => Rebuild();

    private void OnCellChanged(int x, int y, in WorldCell before, in WorldCell after)
    {
        if (before.TileId == after.TileId && before.WallId == after.WallId)
            return;
        _observedChanges++;
        if (_builtRevision < 0)
            return;
        if (before.WallId == after.WallId && Bars(before.TileId) == Bars(after.TileId))
            return;
        var index = y * _world.Width + x;
        if (_dirtyMark[index])
            return;
        _dirtyMark[index] = true;
        _dirty.Add(index);
    }

    private bool IsSolidIndex(int index) => Bars(_world.Cells[index].TileId);

    /// <summary>
    /// Room boundaries: solid blocks plus platforms and doors (open or shut), so a house framed by a platform or a
    /// door entrance is still one enclosed room and a door never merges two rooms.
    /// </summary>
    private bool Bars(TileId id)
    {
        var index = (int)id;
        if ((uint)index >= (uint)_barrier.Length)
            return _tiles[id].IsSolid;
        var cached = _barrier[index];
        if (cached == 0)
            _barrier[index] = cached = (sbyte)(_tiles[id].IsSolid || ContentTiles.Has(id, TileTraits.Platform | TileTraits.Door | TileTraits.OpenDoor) ? 2 : 1);
        return cached == 2;
    }

    private void ClearDirty()
    {
        foreach (var index in _dirty)
            _dirtyMark[index] = false;
        _dirty.Clear();
    }

    private void Rebuild()
    {
        var width = _world.Width;
        var height = _world.Height;
        var previousLabels = _labels;
        var previousRegions = _regions.ToArray();
        var hadPrevious = _builtRevision >= 0;
        var cellsView = _world.Cells;

        // Sky line: the first solid row of each column. Anything above it is under open sky.
        var skyLine = new int[width];
        for (var x = 0; x < width; x++)
        {
            skyLine[x] = height;
            for (var y = 0; y < height; y++)
            {
                if (Bars(cellsView[y * width + x].TileId))
                {
                    skyLine[x] = y;
                    break;
                }
            }
        }
        _skyLine = skyLine;

        var labels = new int[_world.Count];
        Array.Fill(labels, Unassigned);
        _regions.Clear();
        var stack = new Stack<int>();
        for (var start = 0; start < labels.Length; start++)
        {
            if (labels[start] != Unassigned)
                continue;
            if (IsSolidIndex(start))
            {
                labels[start] = SolidLabel;
                continue;
            }

            var id = _regions.Count;
            var cells = new List<int>();
            var region = new RoomInfo(id, cells);
            var stats = new RegionStats();
            labels[start] = id;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var index = stack.Pop();
                var x = index % width;
                var y = index / width;
                stats.Add(this, index);
                if (cells.Count <= SmallRegionLimit)
                    cells.Add(index);
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }

            stats.ApplyTo(region, large: stats.Count > SmallRegionLimit);
            _regions.Add(region);

            void Visit(int nx, int ny)
            {
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    return;
                var next = ny * width + nx;
                if (labels[next] != Unassigned)
                    return;
                if (IsSolidIndex(next))
                {
                    labels[next] = SolidLabel;
                    return;
                }
                labels[next] = id;
                stack.Push(next);
            }
        }

        // Carry temperatures: each new room averages whatever its cells measured before.
        if (hadPrevious)
        {
            foreach (var room in _regions.Where(region => region.IsRoom))
            {
                double total = 0;
                var samples = 0;
                foreach (var index in room.CellIndices)
                {
                    var old = previousLabels[index];
                    if (old < 0 || old >= previousRegions.Length || !previousRegions[old].IsRoom ||
                        float.IsNaN(previousRegions[old].Temperature))
                        continue;
                    total += previousRegions[old].Temperature;
                    samples++;
                }
                // Mostly-new rooms start at the outdoor value the temperature model assigns (NaN).
                if (samples * 2 >= room.CellCount)
                    room.Temperature = (float)(total / samples);
            }
        }

        _labels = labels;
        _builtRevision = _world.TileRevision;
        _observedChanges = 0;
        ClearDirty();
        _liveDirty = true;
        BuildCount++;
        FullBuildCount++;
    }

    /// <summary>Running statistics for one region during a flood fill.</summary>
    private struct RegionStats
    {
        public int Count;
        private int _open;
        private int _sheltered;
        private long _sumX;
        private long _sumY;
        private bool _edge;

        public void Add(RegionMap map, int index)
        {
            var width = map._world.Width;
            var x = index % width;
            var y = index / width;
            Count++;
            _sumX += x;
            _sumY += y;
            if (x == 0 || y == 0 || x == width - 1 || y == map._world.Height - 1)
                _edge = true;
            var roofed = y > map._skyLine[x];
            if (!roofed)
                _open++;
            if (roofed || map._world.Cells[index].WallId != 0)
                _sheltered++;
        }

        public readonly void ApplyTo(RoomInfo region, bool large)
        {
            region.CellCount = Count;
            region.TouchesMapEdge = _edge;
            region.OpenRoofCells = _open;
            region.ShelteredCells = _sheltered;
            region.Centroid = Count == 0 ? Vector2.Zero : new Vector2(_sumX / (float)Count + 0.5f, _sumY / (float)Count + 0.5f);
            region.IsLarge = large;
            region.IsRoom = !large && !_edge && Count <= MaxRoomCells && _sheltered >= MinimumShelteredFraction * Count;
            if (large)
                region.MutableCells.Clear();
        }
    }

    /// <summary>
    /// Local rebuild: kill the tracked regions that touch dirty cells (or whose roof line moved),
    /// then bounded flood fills from the dirty neighbourhood relabel those cells. Fills that reach
    /// more than <see cref="SmallRegionLimit"/> cells join an existing large region.
    /// </summary>
    private void UpdateLocal()
    {
        var width = _world.Width;
        var height = _world.Height;
        var work = 0;
        var seeds = new List<int>();
        var splitSeeds = new List<int>();
        var affected = new HashSet<int>();

        // 1. Roof line per touched column.
        var columns = new HashSet<int>();
        foreach (var index in _dirty)
            columns.Add(index % width);
        foreach (var x in columns)
        {
            var old = _skyLine[x];
            var sky = height;
            for (var y = 0; y < height; y++)
            {
                if (IsSolidIndex(y * width + x))
                {
                    sky = y;
                    break;
                }
            }
            if (sky == old)
                continue;
            _skyLine[x] = sky;
            var low = Math.Min(sky, old);
            var high = Math.Min(height - 1, Math.Max(sky, old));
            for (var y = low; y <= high; y++)
            {
                var index = y * width + x;
                var label = _labels[index];
                if (label >= 0 && !_regions[label].IsLarge)
                    affected.Add(label);
            }
            work += high - low + 1;
        }

        // 2. Dirty cells and their neighbours.
        foreach (var index in _dirty)
        {
            var x = index % width;
            var y = index / width;
            var nowSolid = IsSolidIndex(index);
            var oldLabel = _labels[index];
            if (oldLabel >= 0 && !_regions[oldLabel].IsLarge)
                affected.Add(oldLabel);
            seeds.Add(index);
            foreach (var (nx, ny) in Neighbours(x, y))
            {
                var next = ny * width + nx;
                var label = _labels[next];
                if (label < 0)
                    continue;
                seeds.Add(next);
                if (!_regions[label].IsLarge)
                    affected.Add(label);
                else if (nowSolid)
                    splitSeeds.Add(next);
            }
        }

        // 3. Kill affected tracked regions; remember old labels for temperature carry-over.
        var oldRoom = new Dictionary<int, RoomInfo>();
        foreach (var id in affected)
        {
            var region = _regions[id];
            region.IsLive = false;
            foreach (var index in region.CellIndices)
            {
                if (region.IsRoom)
                    oldRoom[index] = region;
                _labels[index] = Unassigned;
            }
            work += region.CellCount;
        }
        foreach (var index in _dirty)
        {
            if (IsSolidIndex(index))
                _labels[index] = SolidLabel;
            else if (_labels[index] == SolidLabel)
                _labels[index] = Unassigned;
        }

        // 4. Bounded fills. Split seeds sit in large regions next to a newly solid cell.
        var firstNewId = _regions.Count;
        _knownLarge.Clear();
        foreach (var seed in seeds)
        {
            if (_labels[seed] == Unassigned && !IsSolidIndex(seed))
                work += Fill(seed, oldRoom, firstNewId);
        }
        foreach (var seed in splitSeeds)
        {
            var label = _labels[seed];
            if (label >= 0 && label < firstNewId && _regions[label].IsLarge && !_knownLarge.Contains(seed))
                work += Fill(seed, oldRoom, firstNewId);
        }

        LastIncrementalWork = work;
        _builtRevision = _world.TileRevision;
        _observedChanges = 0;
        ClearDirty();
        _liveDirty = true;
        BuildCount++;
    }

    private IEnumerable<(int X, int Y)> Neighbours(int x, int y)
    {
        if (x > 0) yield return (x - 1, y);
        if (x < _world.Width - 1) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y < _world.Height - 1) yield return (x, y + 1);
    }

    /// <summary>
    /// Flood fill from <paramref name="seed"/>. Phase 1 walks unassigned cells and cells of large
    /// regions, up to <see cref="SmallRegionLimit"/>: if it runs dry the component is exact and
    /// becomes a new tracked region. Otherwise phase 2 labels the remaining unassigned cells with
    /// the large region it met (or a new large region). Returns the cells visited.
    /// </summary>
    private int Fill(int seed, Dictionary<int, RoomInfo> oldRoom, int firstNewId)
    {
        var width = _world.Width;
        var height = _world.Height;
        _stamp += 2;
        if (_stamp >= int.MaxValue - 4)
        {
            Array.Clear(_visitStamp);
            _stamp = 2;
        }
        var stamp = _stamp;
        _queue.Clear();
        _queue.Add(seed);
        _visitStamp[seed] = stamp;
        var largeSeen = -1;
        var head = 0;
        var overflow = false;
        while (head < _queue.Count)
        {
            var index = _queue[head++];
            var label = _labels[index];
            if (label >= 0 && label < firstNewId && _regions[label].IsLarge && largeSeen < 0)
                largeSeen = label;
            if (_queue.Count > SmallRegionLimit)
            {
                overflow = true;
                break;
            }
            var x = index % width;
            var y = index / width;
            if (x > 0) Push(index - 1, includeLarge: true);
            if (x < width - 1) Push(index + 1, includeLarge: true);
            if (y > 0) Push(index - width, includeLarge: true);
            if (y < height - 1) Push(index + width, includeLarge: true);
        }

        if (!overflow)
        {
            var id = _regions.Count;
            var cells = new List<int>(_queue);
            var region = new RoomInfo(id, cells);
            foreach (var index in cells)
            {
                var old = _labels[index];
                if (old >= 0 && old < firstNewId && _regions[old].IsLarge)
                    _regions[old].CellCount = Math.Max(0, _regions[old].CellCount - 1);
                _labels[index] = id;
            }
            var stats = new RegionStats();
            foreach (var index in cells)
                stats.Add(this, index);
            stats.ApplyTo(region, large: false);
            if (region.IsRoom)
            {
                double total = 0;
                var samples = 0;
                foreach (var index in cells)
                {
                    if (oldRoom.TryGetValue(index, out var previous) && !float.IsNaN(previous.Temperature))
                    {
                        total += previous.Temperature;
                        samples++;
                    }
                }
                if (samples * 2 >= region.CellCount && samples > 0)
                    region.Temperature = (float)(total / samples);
            }
            _regions.Add(region);
            return cells.Count;
        }

        // Phase 2: keep labelling unassigned cells only (bounded by the killed regions), then merge.
        var visited = _queue.Count;
        var unassigned = new List<int>();
        foreach (var index in _queue)
            if (_labels[index] == Unassigned)
            {
                unassigned.Add(index);
                _visitStamp[index] = stamp + 1;
            }
            else
                _knownLarge.Add(index); // connected to a large component for the rest of this update
        var frontierStart = 0;
        _queue.Clear();
        _queue.AddRange(unassigned);
        while (frontierStart < _queue.Count)
        {
            var index = _queue[frontierStart++];
            var x = index % width;
            var y = index / width;
            if (x > 0) Inspect(index - 1);
            if (x < width - 1) Inspect(index + 1);
            if (y > 0) Inspect(index - width);
            if (y < height - 1) Inspect(index + width);
        }

        RoomInfo target;
        if (largeSeen >= 0)
        {
            target = _regions[largeSeen];
            target.CellCount += _queue.Count;
        }
        else
        {
            target = new RoomInfo(_regions.Count, new List<int>());
            _regions.Add(target);
            var stats = new RegionStats();
            foreach (var index in _queue)
                stats.Add(this, index);
            stats.ApplyTo(target, large: true);
        }
        foreach (var index in _queue)
            _labels[index] = target.Id;
        return visited + _queue.Count;

        void Push(int next, bool includeLarge)
        {
            if (_visitStamp[next] == stamp)
                return;
            var label = _labels[next];
            if (label == SolidLabel)
                return;
            if (label == Unassigned)
            {
                if (IsSolidIndex(next))
                {
                    _labels[next] = SolidLabel;
                    return;
                }
            }
            else if (!(includeLarge && label < firstNewId && _regions[label].IsLarge))
            {
                return;
            }
            _visitStamp[next] = stamp;
            _queue.Add(next);
        }

        void Inspect(int next)
        {
            var label = _labels[next];
            if (label == Unassigned)
            {
                if (IsSolidIndex(next))
                {
                    _labels[next] = SolidLabel;
                    return;
                }
                if (_visitStamp[next] == stamp + 1)
                    return;
                _visitStamp[next] = stamp + 1;
                _queue.Add(next);
            }
            else if (label >= 0 && label < firstNewId && _regions[label].IsLarge && largeSeen < 0)
            {
                largeSeen = label;
            }
        }
    }
}
