namespace Rimtera.World;

/// <summary>
/// Event-driven liquid flow. Only cells that were woken by a nearby change (a tile mined or
/// placed, liquid poured or moved) are processed; each processed cell moves liquid down, then
/// evens it out sideways. Moving liquid wakes its neighbours, so a pool stays active until it is
/// level and then drops out of the set. Newly woken cells wait in a buffer; each step processes
/// up to <see cref="StepCap"/> of them, bottom rows first. When the buffer grows past
/// <see cref="PanicThreshold"/> (a lake was breached) the simulator falls back to a few
/// whole-world relaxation rounds and re-wakes only the cells that can still move.
/// </summary>
public sealed class ActiveLiquidSimulator
{
    public const float Epsilon = 0.0001f;
    private const float SideTransferLimit = 0.2f;
    private const float SideThreshold = 0.02f;

    private readonly TileGrid _grid;
    private readonly TileCatalog _catalog;
    private readonly bool[] _queued;
    private List<int> _buffer = new();
    private List<int> _spare = new();
    private int _round;
    private bool _suppress;
    private bool _attached;

    public ActiveLiquidSimulator(TileGrid grid, TileCatalog catalog, bool attach = true)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _queued = new bool[grid.Count];
        if (attach)
            Attach();
    }

    /// <summary>Cells processed per <see cref="Step"/>; the rest wait in the buffer.</summary>
    public int StepCap { get; set; } = 12_000;
    /// <summary>Buffered cells that trigger the whole-world fallback.</summary>
    public int PanicThreshold { get; set; } = 80_000;
    /// <summary>Whole-world relaxation rounds per fallback.</summary>
    public int PanicRounds { get; set; } = 3;

    /// <summary>Cells currently waiting to be processed.</summary>
    public int ActiveCount => _buffer.Count;
    /// <summary>Largest buffer seen since creation (or the last <see cref="ResetStatistics"/>).</summary>
    public int PeakActiveCount { get; private set; }
    public int FallbackSettles { get; private set; }
    public long CellsProcessed { get; private set; }
    public long Steps { get; private set; }

    /// <summary>Cells where two liquids met and hardened since the consumer last cleared the list (for reframing).</summary>
    public List<GridPoint> MergedCells { get; } = new();
    /// <summary>Total liquid merges since creation.</summary>
    public long Merges { get; private set; }

    /// <summary>
    /// The block two touching liquids harden into, or null when they do not react: water quenches liquid uranium
    /// (stored as <see cref="LiquidKind.Lava"/>) into vitrified core glass, nectar and water set into hive comb,
    /// acid or nectar and liquid uranium leave cracked core crust, acid and water crust into salt, and liquid uranium
    /// burns the fuel sea into char slag. Fuel and water do not mix: they simply keep to their own cells.
    /// </summary>
    public static TileId? MergeTile(LiquidKind a, LiquidKind b)
    {
        if (a == b || a == LiquidKind.None || b == LiquidKind.None)
            return null;
        var (low, high) = a < b ? (a, b) : (b, a);
        return (low, high) switch
        {
            (LiquidKind.Water, LiquidKind.Lava) => TileId.VitrifiedGlass,
            (LiquidKind.Water, LiquidKind.Nectar) => TileId.HiveComb,
            (LiquidKind.Lava, LiquidKind.Acid) => TileId.CoreCrust,
            (LiquidKind.Water, LiquidKind.Acid) => TileId.SaltCrust,
            (LiquidKind.Lava, LiquidKind.Nectar) => TileId.CoreCrust,
            (LiquidKind.Lava, LiquidKind.Fuel) => TileId.CharSlag,
            _ => null
        };
    }

    private static readonly (int Dx, int Dy)[] MergeOffsets = [(0, 1), (-1, 0), (1, 0), (0, -1)];

    /// <summary>
    /// When the liquid at (x, y) touches a different liquid it reacts with, the smaller body (the lower cell on
    /// a tie) hardens into the merge block and the other loses the same volume. Returns true when it merged.
    /// </summary>
    private bool TryMerge(int x, int y)
    {
        var cell = _grid.GetCell(x, y);
        foreach (var (dx, dy) in MergeOffsets)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (!_grid.Contains(nx, ny))
                continue;
            var neighbor = _grid.GetCell(nx, ny);
            if (neighbor.LiquidUnits == 0 || MergeTile(cell.LiquidKind, neighbor.LiquidKind) is not { } block)
                continue;
            var convertNeighbor = neighbor.LiquidUnits < cell.LiquidUnits || (neighbor.LiquidUnits == cell.LiquidUnits && ny >= y);
            var (hx, hy, hard, other, ox, oy) = convertNeighbor ? (nx, ny, neighbor, cell, x, y) : (x, y, cell, neighbor, nx, ny);
            if (hard.TileId != TileId.Air)
                (hx, hy, hard, other, ox, oy) = (ox, oy, other, hard, hx, hy);
            if (hard.TileId != TileId.Air)
                continue;
            var spent = hard.LiquidUnits;
            hard.LiquidUnits = 0;
            hard.LiquidKind = LiquidKind.None;
            hard.TileId = block;
            hard.Damage = 0;
            hard.FrameMask = 0;
            other.LiquidUnits = (byte)Math.Max(0, other.LiquidUnits - spent);
            if (other.LiquidUnits == 0)
                other.LiquidKind = LiquidKind.None;
            _grid.SetCell(new GridPoint(hx, hy), hard);
            _grid.SetCell(new GridPoint(ox, oy), other);
            MergedCells.Add(new GridPoint(hx, hy));
            Merges++;
            return true;
        }
        return false;
    }

    public void ResetStatistics()
    {
        PeakActiveCount = _buffer.Count;
        FallbackSettles = 0;
        CellsProcessed = 0;
        Steps = 0;
    }

    public void Attach()
    {
        if (_attached)
            return;
        _grid.CellChanged += OnCellChanged;
        _attached = true;
    }

    public void Detach()
    {
        if (!_attached)
            return;
        _grid.CellChanged -= OnCellChanged;
        _attached = false;
    }

    private void OnCellChanged(int x, int y, in WorldCell before, in WorldCell after)
    {
        if (_suppress)
            return;
        if (before.TileId == after.TileId && before.LiquidDepth == after.LiquidDepth && before.LiquidKind == after.LiquidKind)
            return;
        Wake(x, y);
        Wake(x, y - 1);
        Wake(x - 1, y);
        Wake(x + 1, y);
    }

    /// <summary>Queues one cell if it holds liquid.</summary>
    public void Wake(int x, int y)
    {
        if (!_grid.Contains(x, y))
            return;
        var index = y * _grid.Width + x;
        if (_queued[index] || _grid.Cells[index].LiquidDepth <= Epsilon)
            return;
        _queued[index] = true;
        _buffer.Add(index);
        if (_buffer.Count > PeakActiveCount)
            PeakActiveCount = _buffer.Count;
    }

    /// <summary>Wakes every liquid cell that could still move (new worlds, loads, after a fallback).</summary>
    public int WakeMovable()
    {
        var woken = 0;
        var width = _grid.Width;
        var cells = _grid.Cells;
        for (var index = 0; index < cells.Length; index++)
        {
            if (cells[index].LiquidDepth <= Epsilon || _queued[index])
                continue;
            if (!CanMove(index % width, index / width))
                continue;
            _queued[index] = true;
            _buffer.Add(index);
            woken++;
        }
        PeakActiveCount = Math.Max(PeakActiveCount, _buffer.Count);
        return woken;
    }

    private bool CanMove(int x, int y)
    {
        var cell = _grid.GetCell(x, y);
        if (y + 1 < _grid.Height)
        {
            var below = _grid.GetCell(x, y + 1);
            if (!_catalog[below.TileId].BlocksLiquid &&
                (below.LiquidDepth <= Epsilon || below.LiquidKind == cell.LiquidKind) &&
                below.LiquidDepth < TileGrid.MaximumLiquidDepth - Epsilon)
                return true;
        }
        if (cell.LiquidDepth <= SideThreshold)
            return false;
        for (var side = -1; side <= 1; side += 2)
        {
            var nx = x + side;
            if (!_grid.Contains(nx, y))
                continue;
            var neighbor = _grid.GetCell(nx, y);
            if (_catalog[neighbor.TileId].BlocksLiquid ||
                (neighbor.LiquidDepth > Epsilon && neighbor.LiquidKind != cell.LiquidKind))
                continue;
            if (cell.LiquidDepth - neighbor.LiquidDepth > SideThreshold)
                return true;
        }
        return false;
    }

    /// <summary>Processes one batch of woken cells. Returns the liquid volume moved.</summary>
    public float Step()
    {
        Steps++;
        if (_buffer.Count > PanicThreshold)
        {
            Panic();
            return 0f;
        }
        if (_buffer.Count == 0)
            return 0f;

        var batch = _buffer;
        _buffer = _spare;
        _buffer.Clear();
        // Bottom rows first so falling liquid lands before the cells above it move.
        batch.Sort(static (a, b) => b.CompareTo(a));
        var count = Math.Min(batch.Count, StepCap);
        for (var i = 0; i < count; i++)
            _queued[batch[i]] = false;
        for (var i = count; i < batch.Count; i++)
            _buffer.Add(batch[i]);

        var moved = 0f;
        _round++;
        for (var i = 0; i < count; i++)
            moved += ProcessCell(batch[i]);
        CellsProcessed += count;
        batch.Clear();
        _spare = batch;
        PeakActiveCount = Math.Max(PeakActiveCount, _buffer.Count);
        return moved;
    }

    /// <summary>Steps until nothing is active or <paramref name="maxSteps"/> is reached. Returns steps taken.</summary>
    public int RunUntilSettled(int maxSteps, CancellationToken cancellationToken = default)
    {
        var steps = 0;
        while (_buffer.Count > 0 && steps < maxSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Step();
            steps++;
        }
        return steps;
    }

    private float ProcessCell(int index)
    {
        var width = _grid.Width;
        var x = index % width;
        var y = index / width;
        var source = new GridPoint(x, y);
        var cell = _grid.GetCell(source);
        if (cell.LiquidDepth <= Epsilon)
            return 0f;
        if (TryMerge(x, y))
            return 0f;

        var moved = 0f;
        if (y + 1 < _grid.Height)
        {
            moved += _grid.TransferLiquid(source, new GridPoint(x, y + 1), cell.LiquidDepth, _catalog);
            cell = _grid.GetCell(source);
        }
        if (cell.LiquidDepth <= SideThreshold)
            return moved;

        var firstDirection = ((x + _round) & 1) == 0 ? -1 : 1;
        for (var side = 0; side < 2; side++)
        {
            var direction = side == 0 ? firstDirection : -firstDirection;
            var destination = new GridPoint(x + direction, y);
            if (!_grid.Contains(destination))
                continue;
            var neighbor = _grid.GetCell(destination);
            var difference = cell.LiquidDepth - neighbor.LiquidDepth;
            if (difference <= SideThreshold)
                continue;
            moved += _grid.TransferLiquid(source, destination, Math.Min(SideTransferLimit, difference / 2f), _catalog);
            cell = _grid.GetCell(source);
        }
        return moved;
    }

    private void Panic()
    {
        FallbackSettles++;
        foreach (var index in _buffer)
            _queued[index] = false;
        _buffer.Clear();
        _suppress = true;
        try
        {
            LiquidSettler.Settle(_grid, _catalog, PanicRounds);
        }
        finally
        {
            _suppress = false;
        }
        WakeMovable();
    }
}
