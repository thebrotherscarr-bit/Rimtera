using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Flora parity in the simulation (docs/FLORA_PARITY.md): felling drops per segment, shaking a tree on a cutter hit, cutting
/// plants with swings and shots, and grass plants giving way to placed blocks. The rules and numbers live in
/// <see cref="FloraParity"/>.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>Axe field of the cutter that last hit a tree (0 for blasts and debug breaks); read once by the next fell.</summary>
    private int _floraAxe;

    /// <summary>Trees shaken today, by base cell.</summary>
    public int TreesShakenToday => _shakenTrees.Count(entry => entry.Value == Day);

    // ------------------------------------------------------------------ hooks called from the tool and combat paths

    /// <summary>
    /// A cutter hit on a tree part (ref. KillTile with fail=true on a shakeable tile → ShakeTree): remembers the cutter's axe
    /// power for the fell drops and shakes the tree (once per tree per day).
    /// </summary>
    private void OnCutterHitFlora(GridPoint point, TileId tile, ItemDefinition item)
    {
        if (!TilePhysics.IsTreePart(tile))
            return;
        _floraAxe = FloraParity.AxeField(item.ToolPower);
        var shaken = ShakeTree(point);
        if (shaken.Length > 0)
            PostMessage(shaken);
    }

    /// <summary>Cuts the cuttable plants around a melee target (ref. ItemCheck_CutTiles over the swing's hitbox).</summary>
    private void CutPlantsAround(GridPoint target, int radius = 1) => CutPlantsInSquare(target, radius);

    /// <summary>Cuts every cuttable plant in the square of <paramref name="radius"/> around <paramref name="target"/>, as a swing would; returns how many.</summary>
    public int CutPlantsInSquare(GridPoint target, int radius)
    {
        var cut = 0;
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
            if (CutPlantAt(new GridPoint(target.X + dx, target.Y + dy), FloraCut.Swing))
                cut++;
        if (cut > 0)
            FlushPhysicsReframe();
        return cut;
    }

    /// <summary>Cuts the cuttable plants along a blade from <paramref name="origin"/> (shaped melee swings).</summary>
    private void CutPlantsAlong(Vector2 origin, Vector2 direction, float reach)
    {
        var cut = false;
        for (var distance = 0.5f; distance <= reach; distance += 0.5f)
        {
            var at = origin + direction * distance;
            cut |= CutPlantAt(new GridPoint((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y)), FloraCut.Swing);
        }
        if (cut)
            FlushPhysicsReframe();
    }

    /// <summary>
    /// Cuts the plant in one cell if it is cuttable and not standing in a planter (ref. CanCutTile, Projectile.CutTilesAt).
    /// Herbs, pots and wild plants drop by their own rules; grass plants and vines leave nothing. True when something was cut.
    /// </summary>
    private bool CutPlantAt(GridPoint point, FloraCut how)
    {
        if (!World.Contains(point))
            return false;
        var tile = World.TileAt(point.X, point.Y);
        if (!FloraParity.IsCuttable(tile) || FloraParity.IsPlanter(World.TileAt(point.X, point.Y + 1, TileId.Air)))
            return false;
        World.SetTile(point, TileId.Air);
        _physicsDirty.Add(point);
        if (how == FloraCut.Projectile)
            FlushPhysicsReframe();
        if (!FloraParity.IsGrassPlant(tile) && tile is not (TileId.Vine or TileId.LongMoss))
            HandleBrokenTile(point, tile);
        return true;
    }

    /// <summary>A placed block replaces the grass plant in its cell (ref. PlaceThing_Tiles_PlaceIt_KillGrassForSolids).</summary>
    private void ClearPlantForPlacement(ItemDefinition item, GridPoint point)
    {
        if (item.PlacedTile == TileId.Air || !World.Contains(point) || Player.ActionCooldownTicks > 0 ||
            !FloraParity.IsReplaceablePlant(World.TileAt(point.X, point.Y)) || !IsInReach(point))
            return;
        World.SetTile(point, TileId.Air);
        _physicsDirty.Add(point);
    }

    // ------------------------------------------------------------------ felling

    /// <summary>
    /// Fells the tree above a cut trunk segment (ref. KillTile_GetTreeDrops, applied to every felled tile): every trunk
    /// segment, the leafy top and the two leafy branches beside the top segment each drop one wood, plus one more when
    /// <c>Next(35) ≤ axe</c> or on one roll in 3; the top and branches each drop a sapling on one roll in 2 (not canopy or
    /// shroom trees). Shroom segments shed a glow spore on one roll in 2; geode segments a gem on one roll in 10 and their
    /// top and branches the crown's gem on one roll in 2.
    /// </summary>
    private void FellTree(GridPoint point, TileId broken)
    {
        var family = FloraParity.FamilyOf(broken);
        var timber = ContentFillFlora.TimberFor(broken);
        var axe = _floraAxe;
        _floraAxe = 0;
        var parts = TilePhysics.CollectTreeAbove(World, point);
        var segments = 1;
        var topY = point.Y;
        var crown = new HashSet<GridPoint>();
        foreach (var part in parts)
        {
            var tile = World.TileAt(part.X, part.Y);
            if (!TilePhysics.IsTreePart(tile))
                continue;
            if (TilePhysics.IsTrunk(tile))
            {
                segments++;
                if (part.X == point.X)
                    topY = Math.Min(topY, part.Y);
            }
            else
                crown.Add(part);
        }
        var crownTile = crown.Count > 0 ? World.TileAt(crown.First().X, crown.First().Y) : TileId.Air;
        var leafy = 0;
        if (crown.Count > 0)
        {
            leafy = 1;
            if (crown.Contains(new GridPoint(point.X - 1, topY)))
                leafy++;
            if (crown.Contains(new GridPoint(point.X + 1, topY)))
                leafy++;
        }
        foreach (var part in parts)
            if (TilePhysics.IsTreePart(World.TileAt(part.X, part.Y)))
                ClearPhysicsCell(part);

        var random = PhysicsRandom;
        var wood = 0;
        for (var i = 0; i < segments + leafy; i++)
            wood += FloraParity.SegmentWood(axe, random);
        var saplings = 0;
        if (FloraParity.FellDropsSaplings(family))
            for (var i = 0; i < leafy; i++)
                if (random.Next(FloraParity.TopSaplingOneIn) == 0)
                    saplings++;
        BreakDrop(point, timber, wood);
        if (saplings > 0)
            BreakDrop(point, ItemId.Sapling, saplings);
        switch (family)
        {
            case TreeFamily.Shroom:
            {
                var spores = 0;
                for (var i = 0; i < segments; i++)
                    if (random.Next(2) == 0)
                        spores++;
                if (spores > 0)
                    BreakDrop(point, ItemId.GlowSpore, spores);
                break;
            }
            case TreeFamily.Geode:
            {
                var gem = ContentFillFlora.GemCrowns.FirstOrDefault(entry => entry.Canopy == crownTile).Gem;
                if (gem == default)
                    gem = ContentFillFlora.GemCrowns[Math.Abs(point.X * 7 + point.Y * 13) % ContentFillFlora.GemCrowns.Length].Gem;
                var gems = 0;
                for (var i = 0; i < segments; i++)
                    if (random.Next(10) == 0)
                        gems++;
                for (var i = 0; i < leafy; i++)
                    if (random.Next(2) == 0)
                        gems++;
                if (gems > 0)
                    BreakDrop(point, gem, gems);
                break;
            }
            case TreeFamily.Crystal:
                BreakDrop(point, ItemId.PrismShard, 1 + random.Next(3)); // Rimtera extra: prismwood sheds shards
                break;
        }
        FlushPhysicsReframe();
        PostMessage($"Felled a tree: {wood} {Items[timber].Name.ToLowerInvariant()} and {saplings} sapling{(saplings == 1 ? "" : "s")}.");
    }

    // ------------------------------------------------------------------ shaking

    /// <summary>
    /// Shakes the tree that <paramref name="cell"/> belongs to (ref. ShakeTree): only a grown tree with a leafy crown shakes,
    /// once per tree per day, and the first matching roll of <see cref="FloraParity.RollShake"/> falls from the crown —
    /// a sapling, spores, wood, credits, a critter or fruit. Returns a message, or an empty string when nothing fell.
    /// </summary>
    public string ShakeTree(GridPoint cell)
    {
        if (!World.Contains(cell) || !TilePhysics.IsTreePart(World.TileAt(cell.X, cell.Y)))
            return string.Empty;
        var baseCell = cell;
        // Walk to the tree's base: down through canopy and trunk.
        for (var guard = 0; guard < 40 && World.Contains(baseCell.X, baseCell.Y + 1) &&
                            TilePhysics.IsTreePart(World.TileAt(baseCell.X, baseCell.Y + 1)); guard++)
            baseCell = new GridPoint(baseCell.X, baseCell.Y + 1);
        var trunk = World.TileAt(baseCell.X, baseCell.Y);
        if (!TilePhysics.IsTrunk(trunk))
            return string.Empty;
        var top = baseCell;
        while (top.Y > 0 && TilePhysics.IsTrunk(World.TileAt(top.X, top.Y - 1)))
            top = new GridPoint(top.X, top.Y - 1);
        // Only a leafy crown shakes, and not one buried in blocks (ref. ShakeTree @169–@201).
        if (!TilePhysics.IsCanopy(World.TileAt(top.X, top.Y - 1, TileId.Air)) && !TilePhysics.IsCanopy(World.TileAt(top.X - 1, top.Y, TileId.Air)) &&
            !TilePhysics.IsCanopy(World.TileAt(top.X + 1, top.Y, TileId.Air)))
            return string.Empty;
        for (var dy = -2; dy <= 2; dy++)
        for (var dx = -2; dx <= 2; dx++)
            if (World.Contains(top.X + dx, top.Y - 1 + dy) && Tiles[World.TileAt(top.X + dx, top.Y - 1 + dy)].IsSolid)
                return string.Empty;
        if (_shakenTrees.TryGetValue(baseCell, out var day) && day == Day)
            return string.Empty;
        if (_shakenTrees.Count > 512)
            foreach (var stale in _shakenTrees.Where(entry => entry.Value != Day).Select(entry => entry.Key).ToArray())
                _shakenTrees.Remove(stale); // yesterday's shakes no longer matter
        _shakenTrees[baseCell] = Day;
        var family = FloraParity.FamilyOf(trunk);
        var crown = new GridPoint(top.X, Math.Max(0, top.Y - 1));
        var at = new Vector2(crown.X + 0.5f, crown.Y + 0.5f);
        var roll = FloraParity.RollShake(family, FloraConditions, PhysicsRandom);
        switch (roll.Kind)
        {
            case ShakeKind.Sapling:
                CollectOrDrop(ItemId.Sapling, roll.Count, at);
                return roll.Count == 1 ? "A sapling tumbles out of the crown." : "Saplings tumble out of the crown.";
            case ShakeKind.Spores:
                CollectOrDrop(ItemId.GlowSpore, roll.Count, at);
                return "Glow spores drift down from the cap.";
            case ShakeKind.Wood:
                CollectOrDrop(ContentFillFlora.TimberFor(trunk), roll.Count, at);
                return "A loose branch falls.";
            case ShakeKind.Credits:
                CollectOrDrop(ItemId.Credits, roll.Count, at);
                return $"Something glints as it falls: {roll.Count} credits.";
            case ShakeKind.Critter or ShakeKind.NightCritter or ShakeKind.Moth:
            {
                var kind = roll.Kind switch
                {
                    ShakeKind.NightCritter => EnemyKind.DuskGlider,
                    ShakeKind.Moth => EnemyKind.SunbeamMoth,
                    _ => PhysicsRandom.Next(2) == 0 ? EnemyKind.ThistleFinch : EnemyKind.RegolithVole
                };
                var critter = SpawnCreature(kind, at);
                return $"A {critter.DisplayName.ToLowerInvariant()} bursts out of the leaves.";
            }
            case ShakeKind.Fruit:
                CollectOrDrop(ItemId.Berry, roll.Count, at);
                return "Fruit drops from the branches.";
            default:
                return string.Empty;
        }
    }
}
