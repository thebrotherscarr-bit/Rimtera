using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Sparse wire layers: each cell may carry up to three independent wires (layer 1, 2, 3 = bits 0, 1, 2).
/// Wires of one layer connect through the four edge neighbours into networks. Actuators live in the
/// cell's own flag bits (<see cref="WorldCell.HasActuator"/>). The renderer's wire view reads
/// <see cref="Cells"/> (and <see cref="Revision"/> to cache).
/// </summary>
public sealed class WireMap
{
    public const int LayerCount = 3;
    private readonly Dictionary<GridPoint, byte> _cells = new();

    /// <summary>Every wired cell and its layer bits (bit 0 = layer 1).</summary>
    public IReadOnlyDictionary<GridPoint, byte> Cells => _cells;

    /// <summary>Bumped on every wire change.</summary>
    public long Revision { get; private set; }

    public int Count => _cells.Count;

    public static byte Bit(int layer) => (byte)(1 << Math.Clamp(layer - 1, 0, LayerCount - 1));

    public byte LayersAt(GridPoint cell) => _cells.GetValueOrDefault(cell);

    public bool Has(GridPoint cell, int layer) => (LayersAt(cell) & Bit(layer)) != 0;

    /// <summary>Lays a wire; false if that layer is already there.</summary>
    public bool Place(GridPoint cell, int layer)
    {
        var current = LayersAt(cell);
        var bit = Bit(layer);
        if ((current & bit) != 0)
            return false;
        _cells[cell] = (byte)(current | bit);
        Revision++;
        return true;
    }

    /// <summary>Cuts one layer; false if it was not there.</summary>
    public bool Remove(GridPoint cell, int layer)
    {
        var current = LayersAt(cell);
        var bit = Bit(layer);
        if ((current & bit) == 0)
            return false;
        var next = (byte)(current & ~bit);
        if (next == 0)
            _cells.Remove(cell);
        else
            _cells[cell] = next;
        Revision++;
        return true;
    }

    /// <summary>Cuts every layer at a cell; returns how many wires were removed.</summary>
    public int RemoveAll(GridPoint cell)
    {
        if (!_cells.Remove(cell, out var bits))
            return 0;
        Revision++;
        return System.Numerics.BitOperations.PopCount(bits);
    }

    public void Clear()
    {
        _cells.Clear();
        Revision++;
    }

    /// <summary>Every cell connected to <paramref name="start"/> through wires of <paramref name="layer"/> (including it).</summary>
    public List<GridPoint> Network(GridPoint start, int layer, int limit = 20_000)
    {
        var result = new List<GridPoint>();
        var bit = Bit(layer);
        if ((LayersAt(start) & bit) == 0)
            return result;
        var seen = new HashSet<GridPoint> { start };
        var frontier = new Queue<GridPoint>();
        frontier.Enqueue(start);
        while (frontier.Count > 0 && result.Count < limit)
        {
            var point = frontier.Dequeue();
            result.Add(point);
            foreach (var next in new[]
                     {
                         new GridPoint(point.X + 1, point.Y), new GridPoint(point.X - 1, point.Y),
                         new GridPoint(point.X, point.Y + 1), new GridPoint(point.X, point.Y - 1)
                     })
                if ((LayersAt(next) & bit) != 0 && seen.Add(next))
                    frontier.Enqueue(next);
        }
        return result;
    }

    /// <summary>Flattened save form: x, y, bits triples.</summary>
    public long[] Capture()
    {
        var data = new long[_cells.Count * 3];
        var i = 0;
        foreach (var (cell, bits) in _cells.OrderBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X))
        {
            data[i++] = cell.X;
            data[i++] = cell.Y;
            data[i++] = bits;
        }
        return data;
    }
}
