using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Information gear (Terraria's info accessories: compass, depth meter, metal detector, lifeform analyzer). An item
/// tagged "position-readout", "depth-readout", "ore-readout" or "life-readout" works while worn or carried: its line
/// shows in <see cref="InfoReadouts"/> (for a HUD), and the half-second pass announces changes worth knowing — a new
/// depth layer, a better ore coming into range, a dangerous creature drawing near, every 250 m crossed.
/// </summary>
public sealed partial class GameSimulation
{
    public const int OreScanRadius = 20;
    public const float LifeScanRadius = 40f;
    public const int CompassAnnounceStep = 250;

    private FishingDepth? _announcedDepth;
    private ItemId? _announcedOre;
    private readonly HashSet<long> _announcedCreatures = new();
    private int? _announcedCompassStep;

    /// <summary>True when worn gear or any carried item carries the information tag.</summary>
    public bool HasInfoGear(string tag)
    {
        if (GearHasTag(tag))
            return true;
        foreach (var stack in Player.Inventory.Slots)
            if (!stack.IsEmpty && Items.Contains(stack.ItemId) && Items[stack.ItemId].ImmunityTags.Contains(tag))
                return true;
        return false;
    }

    /// <summary>One line per active information item (empty without any).</summary>
    public IReadOnlyList<string> InfoReadouts
    {
        get
        {
            var lines = new List<string>();
            var cell = RegionMap.CellAt(PlayerCenter);
            if (HasInfoGear("position-readout"))
            {
                var offset = cell.X - SpawnColumn;
                lines.Add(offset == 0 ? "Compass: at the landing site" : $"Compass: {Math.Abs(offset)} m {(offset > 0 ? "east" : "west")}");
            }
            if (HasInfoGear("depth-readout"))
            {
                var depth = BiomeLayout.SurfaceAt(Math.Clamp(cell.X, 0, World.Width - 1)) - cell.Y;
                lines.Add($"Depth meter: {Math.Abs(depth)} m {(depth >= 0 ? "above surface" : "below surface")} ({LayerName(CurrentDepthLayer(cell))})");
            }
            if (HasInfoGear("ore-readout"))
                lines.Add(BestOreNearby(cell) is { } ore ? $"Ore scanner: {Items[ore].Name.ToLowerInvariant()} nearby" : "Ore scanner: no ore nearby");
            if (HasInfoGear("life-readout"))
            {
                var near = CreaturesNear(LifeScanRadius).ToArray();
                var strongest = near.Where(enemy => !enemy.IsPassive).OrderByDescending(enemy => enemy.Archetype.HitPoints).FirstOrDefault();
                lines.Add(near.Length == 0 ? "Life monitor: no creatures nearby"
                    : $"Life monitor: {near.Length} creature{(near.Length == 1 ? "" : "s")} nearby{(strongest is null ? "" : $", strongest {strongest.DisplayName}")}");
            }
            return lines;
        }
    }

    private FishingDepth CurrentDepthLayer(GridPoint cell) =>
        FishingTables.DepthAt(BiomeLayout, Math.Clamp(cell.X, 0, World.Width - 1), Math.Clamp(cell.Y, 0, World.Height - 1), World.Height);

    private static string LayerName(FishingDepth depth) => depth switch
    {
        FishingDepth.Surface => "surface",
        FishingDepth.Underground => "underground",
        FishingDepth.Caverns => "caverns",
        _ => "molten chasm"
    };

    private IEnumerable<EnemyAgent> CreaturesNear(float radius) =>
        _enemies.Where(enemy => enemy.IsAlive && Vector2.Distance(enemy.Motion.Position, PlayerCenter) <= radius);

    /// <summary>The hardest (highest-tier) ore or gem tile within <see cref="OreScanRadius"/> tiles (as its drop item).</summary>
    public ItemId? BestOreNearby(GridPoint center)
    {
        ItemId? best = null;
        var bestValue = 0;
        for (var y = center.Y - OreScanRadius; y <= center.Y + OreScanRadius; y++)
        for (var x = center.X - OreScanRadius; x <= center.X + OreScanRadius; x++)
        {
            var point = new GridPoint(x, y);
            if (!World.Contains(point))
                continue;
            var tile = World.GetCell(point).TileId;
            if (!IsScannableOre(tile) || TileDropTable.GetDrop(tile) is not { } drop || !Items.Contains(drop))
                continue;
            // Harder ores are the better finds (drill hardness tracks the ore tiers); value breaks ties.
            var value = DrillHardness(tile) * 1000 + Math.Min(999, Items[drop].CreditValue);
            if (value > bestValue)
                (best, bestValue) = (drop, value);
        }
        return best;
    }

    private static bool IsScannableOre(TileId tile)
    {
        var name = tile.ToString();
        return name.EndsWith("Ore", StringComparison.Ordinal) || name.EndsWith("Gem", StringComparison.Ordinal) ||
               tile == TileId.EnergyCrystal;
    }

    /// <summary>Half-second pass: information gear announces what changed (one-line hook from the achievement pass).</summary>
    internal void UpdateInfoGear()
    {
        var cell = RegionMap.CellAt(PlayerCenter);
        if (HasInfoGear("depth-readout"))
        {
            var layer = CurrentDepthLayer(cell);
            if (_announcedDepth is { } previous && previous != layer)
                PostMessage($"Depth meter: entering the {LayerName(layer)}.");
            _announcedDepth = layer;
        }
        else
            _announcedDepth = null;

        if (HasInfoGear("ore-readout"))
        {
            var ore = BestOreNearby(cell);
            if (ore is { } found && ore != _announcedOre)
                PostMessage($"Ore scanner: {Items[found].Name.ToLowerInvariant()} detected nearby.");
            _announcedOre = ore;
        }
        else
            _announcedOre = null;

        if (HasInfoGear("life-readout"))
        {
            foreach (var enemy in CreaturesNear(LifeScanRadius))
                if (!enemy.IsPassive && (enemy.IsBoss || enemy.Archetype.HitPoints >= 300f) && _announcedCreatures.Add(enemy.Id))
                    PostMessage($"Life monitor: {enemy.DisplayName} nearby!");
            _announcedCreatures.RemoveWhere(id => !_enemies.Any(enemy => enemy.Id == id && enemy.IsAlive));
        }

        if (HasInfoGear("position-readout"))
        {
            var step = (cell.X - SpawnColumn) / CompassAnnounceStep;
            if (_announcedCompassStep is { } previous && previous != step && step != 0)
                PostMessage($"Compass: {Math.Abs(step * CompassAnnounceStep)} m {(step > 0 ? "east" : "west")} of the landing site.");
            _announcedCompassStep = step;
        }
        else
            _announcedCompassStep = null;
    }
}
