using System.Numerics;

namespace Rimtera.World;

public enum PlacementAnchorRule
{
    None,
    SolidBelow,
    SolidAdjacent
}

public enum PlacementFailureReason
{
    None,
    InvalidDefinition,
    OutOfBounds,
    Occupied,
    NotReplaceable,
    LiquidBlocked,
    MissingWallBacking,
    MissingAnchor,
    MissingAdjacentBacking,
    AlreadyPresent,
    OutOfReach,
    BlockedLineOfSight,
    InsufficientToolPower,
    ActorOverlap
}

/// <summary>
/// Placement properties are data, not hard-coded tile-ID branches. ReplaceableTiles
/// is an explicit allow-list; a null list never means “replace anything.”
/// </summary>
public sealed record PlacementDefinition(
    TileId TileId,
    int Width = 1,
    int Height = 1,
    PlacementAnchorRule AnchorRule = PlacementAnchorRule.SolidBelow,
    bool RequireWallBacking = false,
    bool AllowLiquid = false,
    bool AllowReplacement = false,
    IReadOnlySet<TileId>? ReplaceableTiles = null);

public readonly record struct PlacementActorContext(
    Vector2 Position,
    float ReachTiles,
    bool RequireLineOfSight = true,
    Vector2 ActorSize = default);

public enum ReplacementDropPolicy : byte
{
    ReturnPreviousTile,
    SuppressPreviousTileDrop
}

public sealed record TileReplacementRules(
    IReadOnlySet<TileId> ReplaceableTiles,
    int RequiredToolPower,
    bool AllowLiquid = false,
    ReplacementDropPolicy DropPolicy = ReplacementDropPolicy.ReturnPreviousTile);

/// <summary>
/// CellsToReframe is the bounded tile region consumed by TileFramingSystem after a write.
/// Rendering remains separate from the simulation's stored atlas coordinates.
/// </summary>
public sealed record PlacementResult(
    bool Success,
    PlacementFailureReason Failure,
    IReadOnlyList<GridPoint> ChangedCells,
    IReadOnlyList<GridPoint> CellsToReframe,
    TileId? PreviousTile = null,
    IReadOnlyList<TileId>? ReplacedTiles = null);

/// <summary>Configurable tile and wall placement over Rimtera's compact grid.</summary>
public sealed class PlacementSystem
{
    private static readonly GridPoint[] CardinalOffsets =
    [
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0)
    ];

    public PlacementResult TryPlaceTile(
        TileGrid world,
        TileCatalog catalog,
        GridPoint origin,
        PlacementDefinition definition,
        PlacementActorContext? actorContext = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(definition);

        if (!catalog.Contains(definition.TileId) || definition.TileId == TileId.Air ||
            definition.Width < 1 || definition.Height < 1 ||
            definition.Width > world.Width || definition.Height > world.Height)
        {
            return Failure(PlacementFailureReason.InvalidDefinition);
        }

        if (origin.X < 0 || origin.Y < 0 ||
            (long)origin.X + definition.Width > world.Width ||
            (long)origin.Y + definition.Height > world.Height)
        {
            return Failure(PlacementFailureReason.OutOfBounds);
        }

        var target = new Vector2(origin.X + definition.Width * 0.5f, origin.Y + definition.Height * 0.5f);
        var actorFailure = ValidateActorContext(world, catalog, target, actorContext);
        if (actorFailure != PlacementFailureReason.None)
            return Failure(actorFailure);
        if (OverlapsActor(origin, definition.Width, definition.Height, actorContext))
            return Failure(PlacementFailureReason.ActorOverlap);

        var footprint = new List<GridPoint>(definition.Width * definition.Height);
        var replacedTiles = new List<TileId>();
        for (var y = origin.Y; y < origin.Y + definition.Height; y++)
        {
            for (var x = origin.X; x < origin.X + definition.Width; x++)
            {
                var point = new GridPoint(x, y);
                var cell = world.GetCell(point);

                if (cell.TileId == definition.TileId)
                    return Failure(PlacementFailureReason.AlreadyPresent);

                // Grass plants give way to whatever is placed (flora parity).
                if (cell.TileId != TileId.Air && !Rimtera.Systems.FloraParity.IsReplaceablePlant(cell.TileId))
                {
                    if (!definition.AllowReplacement)
                        return Failure(PlacementFailureReason.Occupied);

                    if (definition.ReplaceableTiles is null ||
                        !definition.ReplaceableTiles.Contains(cell.TileId))
                    {
                        return Failure(PlacementFailureReason.NotReplaceable);
                    }
                    replacedTiles.Add(cell.TileId);
                }

                if (!definition.AllowLiquid && cell.LiquidDepth > 0f)
                    return Failure(PlacementFailureReason.LiquidBlocked);

                if (definition.RequireWallBacking && cell.WallId == 0)
                    return Failure(PlacementFailureReason.MissingWallBacking);

                footprint.Add(point);
            }
        }

        if (!HasRequiredAnchor(world, catalog, origin, definition))
            return Failure(PlacementFailureReason.MissingAnchor);

        // Validation is complete before the first write, so a multi-cell placement is atomic.
        foreach (var point in footprint)
            world.SetTile(point, definition.TileId);

        return Success(footprint, TileFramingSystem.GetDirtyRegion(world, footprint), replacedTiles: replacedTiles);
    }

    /// <summary>Replaces one allow-listed solid tile after reach, LOS, liquid, and tool checks.</summary>
    public PlacementResult TryReplaceTile(
        TileGrid world,
        TileCatalog catalog,
        GridPoint point,
        TileId newTileId,
        TileReplacementRules rules,
        int toolPower,
        PlacementActorContext? actorContext = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rules);
        if (!catalog.Contains(newTileId) || newTileId == TileId.Air || !catalog[newTileId].IsSolid ||
            rules.ReplaceableTiles is null || rules.RequiredToolPower < 0 || toolPower < 0 ||
            !Enum.IsDefined(rules.DropPolicy))
            return Failure(PlacementFailureReason.InvalidDefinition);
        if (!world.Contains(point))
            return Failure(PlacementFailureReason.OutOfBounds);

        var cell = world.GetCell(point);
        var oldTileId = cell.TileId;
        if (!catalog.Contains(oldTileId))
            return Failure(PlacementFailureReason.InvalidDefinition);
        if (oldTileId == TileId.Air)
            return Failure(PlacementFailureReason.Occupied);
        if (oldTileId == newTileId)
            return Failure(PlacementFailureReason.AlreadyPresent);
        if (!catalog[oldTileId].IsSolid || !rules.ReplaceableTiles.Contains(oldTileId))
            return Failure(PlacementFailureReason.NotReplaceable);
        if (toolPower < rules.RequiredToolPower)
            return Failure(PlacementFailureReason.InsufficientToolPower);
        if (!rules.AllowLiquid && cell.LiquidDepth > 0f)
            return Failure(PlacementFailureReason.LiquidBlocked);

        var target = new Vector2(point.X + 0.5f, point.Y + 0.5f);
        var actorFailure = ValidateActorContext(world, catalog, target, actorContext, ignoreTargetCell: true);
        if (actorFailure != PlacementFailureReason.None)
            return Failure(actorFailure);
        if (OverlapsActor(point, 1, 1, actorContext))
            return Failure(PlacementFailureReason.ActorOverlap);

        world.SetTile(point, newTileId);
        var changed = new[] { point };
        TileId? previousTile = rules.DropPolicy == ReplacementDropPolicy.ReturnPreviousTile ? oldTileId : null;
        return Success(changed, TileFramingSystem.GetDirtyRegion(world, changed), previousTile);
    }

    /// <summary>
    /// Places a background wall. By default it must touch an existing tile or wall
    /// on one of its four sides. Replacement is separately allow-listed.
    /// </summary>
    public PlacementResult TryPlaceWall(
        TileGrid world,
        GridPoint point,
        ushort wallId,
        bool allowReplacement = false,
        IReadOnlySet<ushort>? replaceableWallIds = null,
        bool requireAdjacentBacking = true,
        PlacementActorContext? actorContext = null,
        TileCatalog? tileCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (wallId == 0)
            return Failure(PlacementFailureReason.InvalidDefinition);
        if (!world.Contains(point))
            return Failure(PlacementFailureReason.OutOfBounds);

        var target = new Vector2(point.X + 0.5f, point.Y + 0.5f);
        var actorFailure = ValidateActorContext(world, tileCatalog, target, actorContext, ignoreTargetCell: true);
        if (actorFailure != PlacementFailureReason.None)
            return Failure(actorFailure);

        var cell = world.GetCell(point);
        if (cell.WallId == wallId)
            return Failure(PlacementFailureReason.AlreadyPresent);
        if (cell.WallId != 0)
        {
            if (!allowReplacement)
                return Failure(PlacementFailureReason.Occupied);
            if (replaceableWallIds is null || !replaceableWallIds.Contains(cell.WallId))
                return Failure(PlacementFailureReason.NotReplaceable);
        }

        if (requireAdjacentBacking && !HasAdjacentBacking(world, point))
            return Failure(PlacementFailureReason.MissingAdjacentBacking);

        cell.WallId = wallId;
        world.SetCell(point, cell);
        var changed = new[] { point };
        return Success(changed, TileFramingSystem.GetDirtyRegion(world, changed));
    }

    private static bool HasRequiredAnchor(
        TileGrid world,
        TileCatalog catalog,
        GridPoint origin,
        PlacementDefinition definition)
    {
        if (definition.AnchorRule == PlacementAnchorRule.None)
            return true;

        if (definition.AnchorRule == PlacementAnchorRule.SolidBelow)
        {
            var supportY = origin.Y + definition.Height;
            for (var x = origin.X; x < origin.X + definition.Width; x++)
            {
                if (!IsSolid(world, catalog, new GridPoint(x, supportY)))
                    return false;
            }

            return true;
        }

        for (var x = origin.X; x < origin.X + definition.Width; x++)
        {
            if (IsSolid(world, catalog, new GridPoint(x, origin.Y - 1)) ||
                IsSolid(world, catalog, new GridPoint(x, origin.Y + definition.Height)))
            {
                return true;
            }
        }

        for (var y = origin.Y; y < origin.Y + definition.Height; y++)
        {
            if (IsSolid(world, catalog, new GridPoint(origin.X - 1, y)) ||
                IsSolid(world, catalog, new GridPoint(origin.X + definition.Width, y)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSolid(TileGrid world, TileCatalog catalog, GridPoint point) =>
        world.Contains(point) && catalog[world.GetCell(point).TileId].IsSolid;

    private static bool HasAdjacentBacking(TileGrid world, GridPoint point)
    {
        foreach (var offset in CardinalOffsets)
        {
            var neighbor = new GridPoint(point.X + offset.X, point.Y + offset.Y);
            if (!world.Contains(neighbor))
                continue;

            var cell = world.GetCell(neighbor);
            if (cell.TileId != TileId.Air && !Rimtera.Systems.FloraParity.IsReplaceablePlant(cell.TileId) || cell.WallId != 0)
                return true;
        }

        return false;
    }

    private static PlacementFailureReason ValidateActorContext(
        TileGrid world,
        TileCatalog? catalog,
        Vector2 target,
        PlacementActorContext? actorContext,
        bool ignoreTargetCell = false)
    {
        if (actorContext is not { } actor)
            return PlacementFailureReason.None;
        if (!float.IsFinite(actor.Position.X) || !float.IsFinite(actor.Position.Y) ||
            !float.IsFinite(actor.ReachTiles) || actor.ReachTiles < 0f ||
            !float.IsFinite(actor.ActorSize.X) || !float.IsFinite(actor.ActorSize.Y) ||
            actor.ActorSize.X < 0f || actor.ActorSize.Y < 0f)
            return PlacementFailureReason.InvalidDefinition;
        if (actor.Position.X < 0f || actor.Position.Y < 0f ||
            actor.Position.X >= world.Width || actor.Position.Y >= world.Height)
            return PlacementFailureReason.InvalidDefinition;
        if (Vector2.DistanceSquared(actor.Position, target) > actor.ReachTiles * actor.ReachTiles)
            return PlacementFailureReason.OutOfReach;
        if (actor.RequireLineOfSight)
        {
            if (catalog is null)
                return PlacementFailureReason.InvalidDefinition;
            if (!HasLineOfSight(world, catalog, actor.Position, target, ignoreTargetCell))
                return PlacementFailureReason.BlockedLineOfSight;
        }
        return PlacementFailureReason.None;
    }

    private static bool OverlapsActor(
        GridPoint origin,
        int width,
        int height,
        PlacementActorContext? actorContext)
    {
        if (actorContext is not { } actor || actor.ActorSize.X <= 0f || actor.ActorSize.Y <= 0f)
            return false;
        var left = actor.Position.X - actor.ActorSize.X * 0.5f;
        var right = actor.Position.X + actor.ActorSize.X * 0.5f;
        var top = actor.Position.Y - actor.ActorSize.Y * 0.5f;
        var bottom = actor.Position.Y + actor.ActorSize.Y * 0.5f;
        return left < origin.X + width && right > origin.X && top < origin.Y + height && bottom > origin.Y;
    }

    private static bool HasLineOfSight(
        TileGrid world,
        TileCatalog? catalog,
        Vector2 origin,
        Vector2 target,
        bool ignoreTargetCell)
    {
        if (catalog is null)
            return true;

        var x = (int)MathF.Floor(origin.X);
        var y = (int)MathF.Floor(origin.Y);
        var endX = (int)MathF.Floor(target.X);
        var endY = (int)MathF.Floor(target.Y);
        var deltaX = Math.Abs(endX - x);
        var deltaY = Math.Abs(endY - y);
        var stepX = x < endX ? 1 : -1;
        var stepY = y < endY ? 1 : -1;
        var error = deltaX - deltaY;
        var first = true;

        while (true)
        {
            var current = new GridPoint(x, y);
            if (!world.Contains(current))
                return false;
            var isTarget = x == endX && y == endY;
            if (!first && !(ignoreTargetCell && isTarget) &&
                catalog[world.GetCell(current).TileId].IsSolid)
                return false;
            if (isTarget)
                return true;

            first = false;
            var doubledError = 2 * error;
            if (doubledError > -deltaY)
            {
                error -= deltaY;
                x += stepX;
            }
            if (doubledError < deltaX)
            {
                error += deltaX;
                y += stepY;
            }
        }
    }

    private static PlacementResult Failure(PlacementFailureReason reason) =>
        new(false, reason, Array.Empty<GridPoint>(), Array.Empty<GridPoint>());

    private static PlacementResult Success(
        IEnumerable<GridPoint> changed,
        IReadOnlyList<GridPoint> frameRegion,
        TileId? previousTile = null,
        IEnumerable<TileId>? replacedTiles = null)
    {
        var changedCells = changed.ToArray();
        var replaced = replacedTiles?.ToArray();
        previousTile ??= replaced is { Length: 1 } ? replaced[0] : null;
        return new(true, PlacementFailureReason.None, changedCells, frameRegion, previousTile, replaced);
    }
}
