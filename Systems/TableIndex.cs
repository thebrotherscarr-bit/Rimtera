using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Where the world's tables stand (0.8.2, the performance pass). A room is only housing with a table in it, so the hourly
/// search for a free house validates the rooms holding one instead of every cave pocket in the world. Kept current from the
/// grid's cell events; an edit the events did not see (a bulk edit, a load) is noticed from the grid's revision and the
/// index rescans, the same way <see cref="RegionMap"/> notices one.
/// </summary>
public sealed class TableIndex
{
    private readonly TileGrid _world;
    private readonly HashSet<int> _cells = [];
    private readonly sbyte[] _isTable;
    private long _syncedRevision = -1, _observed;

    public TableIndex(TileGrid world)
    {
        _world = world;
        _isTable = new sbyte[Enum.GetValues<TileId>().Max(id => (int)id) + 1];
        _world.CellChanged += OnCellChanged;
    }

    /// <summary>Full rescans so far (tests).</summary>
    public int Rescans { get; private set; }

    /// <summary>The cell indices (y × width + x) holding a table.</summary>
    public IReadOnlyCollection<int> Cells
    {
        get
        {
            if (_syncedRevision < 0 || _world.TileRevision != _syncedRevision + _observed)
                Rescan();
            return _cells;
        }
    }

    private bool IsTable(TileId id)
    {
        var index = (int)id;
        if ((uint)index >= (uint)_isTable.Length)
            return ContentTiles.Has(id, TileTraits.Table);
        var cached = _isTable[index];
        if (cached == 0)
            _isTable[index] = cached = (sbyte)(ContentTiles.Has(id, TileTraits.Table) ? 2 : 1);
        return cached == 2;
    }

    private void OnCellChanged(int x, int y, in WorldCell before, in WorldCell after)
    {
        if (before.TileId == after.TileId && before.WallId == after.WallId)
            return;
        _observed++;
        if (_syncedRevision < 0)
            return;
        var index = y * _world.Width + x;
        if (IsTable(after.TileId))
            _cells.Add(index);
        else
            _cells.Remove(index);
    }

    private void Rescan()
    {
        _cells.Clear();
        var cells = _world.Cells;
        for (var i = 0; i < cells.Length; i++)
            if (IsTable(cells[i].TileId))
                _cells.Add(i);
        _syncedRevision = _world.TileRevision;
        _observed = 0;
        Rescans++;
    }
}
