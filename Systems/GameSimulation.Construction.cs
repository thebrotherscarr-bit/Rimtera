using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>A saved blueprint: a cell and the block item meant for it.</summary>
public sealed record BlueprintSnapshot(int X, int Y, ItemId Item);

/// <summary>
/// Blueprints and the Build job giver (0.2.7, docs/DESIGN_COLONY_ESCAPE.md §3.3 "Build"). With blueprint mode on (the
/// Build tab), placing a block marks the cell for it instead (clicking a marked cell again unmarks it) and spends nothing.
/// Build crew (Construction skill) fetch the blocks from chests near home and place them, nearest first; the player still
/// decides what goes where.
/// </summary>
public sealed partial class GameSimulation
{
    public const int MaxBlueprints = 400;
    /// <summary>Seconds to place one block at construction speed 1 (Construction 10).</summary>
    public const float BuildBaseSeconds = 2f;
    /// <summary>Blocks a builder fetches at a time.</summary>
    public const int BuildCarry = 20;

    private readonly Dictionary<GridPoint, ItemId> _blueprints = new();

    /// <summary>While on, placing a block marks a blueprint instead (the Build tab's toggle).</summary>
    public bool BlueprintMode { get; set; }

    public IReadOnlyDictionary<GridPoint, ItemId> Blueprints => _blueprints;

    /// <summary>Blocks a blueprint can ask for: single-cell placeable tiles (not doors, liquids tools or travel pieces).</summary>
    public bool CanBlueprint(ItemDefinition item) =>
        item.UseKind == ItemUseKind.PlaceTile && item.PlacedTile != TileId.Air && Tiles.Contains(item.PlacedTile) &&
        !ContentTiles.Has(item.PlacedTile, TileTraits.Door) && !TravelContent.IsGlideWalk(item.PlacedTile);

    /// <summary>Marks a blueprint for <paramref name="item"/> at a cell, or removes the one already there.</summary>
    public bool TryToggleBlueprint(ItemDefinition item, GridPoint cell)
    {
        if (!CanBlueprint(item) || !World.Contains(cell))
            return false;
        if (_blueprints.TryGetValue(cell, out var marked))
        {
            _blueprints.Remove(cell);
            if (marked == item.Id)
            {
                PostMessage("Blueprint removed.");
                return true;
            }
        }
        if (World.TileAt(cell.X, cell.Y) == item.PlacedTile)
        {
            PostMessage($"There is already {item.Name.ToLowerInvariant()} there.");
            return true;
        }
        if (_blueprints.Count >= MaxBlueprints)
        {
            PostMessage($"Too many blueprints ({MaxBlueprints}).");
            return true;
        }
        _blueprints[cell] = item.Id;
        PostMessage($"Blueprint: {item.Name}.");
        return true;
    }

    public void ClearBlueprints() => _blueprints.Clear();

    /// <summary>Seconds one block takes a builder: the base time over their construction speed.</summary>
    public static float BuildSeconds(Rimtera.Characters.ColonistStats stats) => BuildBaseSeconds / Math.Max(0.2f, stats.ConstructionSpeed);

    /// <summary>The Build job giver: place the nearest blueprint near home whose block is in hand or in a chest near home.</summary>
    private bool TryBuild(ColonistAgent agent, Vector2 center, float deltaSeconds)
    {
        void Move(Vector2 to) => StepColonistToward(agent, center, to, deltaSeconds, 1f);
        if (_blueprints.Count == 0)
        {
            agent.FarmTarget = null;
            if (!agent.BuildStock.IsEmpty)
                DropBuildStock(agent, center, store: true);
            return false;
        }

        // Blueprints already built (by anyone) are done.
        foreach (var done in _blueprints.Where(pair => Items.Contains(pair.Value) && World.TileAt(pair.Key.X, pair.Key.Y) == Items[pair.Value].PlacedTile)
                     .Select(pair => pair.Key).ToArray())
            _blueprints.Remove(done);

        var claimed = _colonists.Where(other => !ReferenceEquals(other, agent) && other.IsAlive && other.FarmTarget is not null)
            .Select(other => other.FarmTarget!.Value).ToHashSet();
        var open = _blueprints
            .Where(pair => !claimed.Contains(pair.Key) && World.TileAt(pair.Key.X, pair.Key.Y) == TileId.Air &&
                           Vector2.Distance(new Vector2(pair.Key.X + 0.5f, pair.Key.Y + 0.5f), agent.HomePosition) <= OperateRadius)
            .OrderBy(pair => Vector2.DistanceSquared(new Vector2(pair.Key.X + 0.5f, pair.Key.Y + 0.5f), center))
            .ToArray();
        foreach (var (cell, item) in open)
        {
            var inHand = !agent.BuildStock.IsEmpty && agent.BuildStock.ItemId == item;
            if (!inHand)
            {
                if (NearestHolding(agent, item) is not { } chestCell)
                    continue; // nothing to build it with: try the next blueprint
                if (!agent.BuildStock.IsEmpty)
                    DropBuildStock(agent, center, store: true);
                agent.FarmTarget = null;
                agent.Activity = $"Fetching {Items[item].Name.ToLowerInvariant()}";
                if (Reaches(center, chestCell))
                {
                    var chest = GetContainer(chestCell)!;
                    var take = Math.Min(BuildCarry, chest.Count(item));
                    if (take > 0 && chest.TryConsume(item, take))
                        agent.BuildStock = new ItemStack(item, take);
                }
                else
                    Move(new Vector2(chestCell.X + 0.5f, chestCell.Y + 0.5f));
                return true;
            }

            if (agent.FarmTarget != cell)
                agent.FarmProgress = 0f;
            agent.FarmTarget = cell;
            agent.Activity = $"Building {Items[item].Name.ToLowerInvariant()}";
            // Work from beside the cell, never inside it (a block can't go where the builder stands).
            var side = center.X < cell.X + 0.5f ? -1.3f : 1.3f;
            var stand = new Vector2(cell.X + 0.5f + side, cell.Y + 0.5f);
            if (DistanceToRect(center, new Vector2(cell.X, cell.Y), Vector2.One) > 2.8f || OverlapsCell(agent.Motion, cell))
            {
                Move(stand);
                return true;
            }
            Move(center);
            agent.FarmProgress += deltaSeconds;
            if (agent.FarmProgress < BuildSeconds(agent.ColonistStats))
                return true;
            agent.FarmProgress = 0f;
            agent.FarmTarget = null;
            if (PlaceBlueprint(agent, cell, Items[item]))
            {
                var left = agent.BuildStock.Count - 1;
                agent.BuildStock = left > 0 ? agent.BuildStock with { Count = left } : default;
                _blueprints.Remove(cell);
                Flags.Increment("colony.built");
            }
            else
                _blueprints.Remove(cell); // the cell can't take it any more (an actor or a missing anchor): drop the mark
            return true;
        }

        agent.FarmTarget = null;
        if (!agent.BuildStock.IsEmpty)
            DropBuildStock(agent, center, store: true);
        return false;
    }

    private static bool OverlapsCell(PawnMotionState motion, GridPoint cell) =>
        motion.Position.X < cell.X + 1 && motion.Position.X + motion.Size.X > cell.X &&
        motion.Position.Y < cell.Y + 1 && motion.Position.Y + motion.Size.Y > cell.Y;

    /// <summary>Places a blueprint's block the way the player would (anchors, liquids, no actor in the way), then reframes.</summary>
    private bool PlaceBlueprint(ColonistAgent builder, GridPoint cell, ItemDefinition item)
    {
        if (OverlapsCell(Player.Motion, cell) || _colonists.Any(other => other.IsAlive && OverlapsCell(other.Motion, cell)))
            return false;
        var anchor = item.PlacedTile is TileId.Trapdoor or TileId.WallTorch or TileId.CeilingLight or TileId.PaintingPlanet or TileId.ColonyBanner or TileId.BossTrophy
            ? PlacementAnchorRule.SolidAdjacent
            : Tiles[item.PlacedTile].IsSolid ? PlacementAnchorRule.None : PlacementAnchorRule.SolidBelow;
        var result = _placement.TryPlaceTile(World, Tiles, cell, new PlacementDefinition(item.PlacedTile, Height: 1, AnchorRule: anchor, AllowLiquid: true),
            new PlacementActorContext(builder.Motion.Position + builder.Motion.Size / 2f, PlacementReachRadius, RequireLineOfSight: false,
                ActorSize: builder.Motion.Size));
        if (!result.Success)
            return false;
        RememberPlacedDecor(item, cell);
        _framing.ReframeRegion(World, Tiles, result.CellsToReframe);
        return true;
    }

    /// <summary>Puts a builder's blocks back: into a chest near home when <paramref name="store"/> and there is room, else on the ground.</summary>
    private void DropBuildStock(ColonistAgent agent, Vector2 at, bool store = false)
    {
        var stock = agent.BuildStock;
        agent.BuildStock = default;
        if (stock.IsEmpty)
            return;
        if (store && StorageFor(agent, stock.ItemId) is { } chest)
        {
            var stored = GetContainer(chest)?.Add(stock.ItemId, stock.Count) ?? 0;
            stock = stock with { Count = stock.Count - stored };
        }
        if (stock.Count > 0)
            SpawnGroundItem(stock.ItemId, stock.Count, at);
    }

    private BlueprintSnapshot[] CaptureBlueprints() =>
        _blueprints.OrderBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X)
            .Select(pair => new BlueprintSnapshot(pair.Key.X, pair.Key.Y, pair.Value)).ToArray();

    private void RestoreBlueprints(BlueprintSnapshot[]? saved)
    {
        _blueprints.Clear();
        foreach (var entry in saved ?? [])
            if (entry is not null && World.Contains(entry.X, entry.Y) && Items.Contains(entry.Item) && CanBlueprint(Items[entry.Item]) &&
                _blueprints.Count < MaxBlueprints)
                _blueprints[new GridPoint(entry.X, entry.Y)] = entry.Item;
    }
}
