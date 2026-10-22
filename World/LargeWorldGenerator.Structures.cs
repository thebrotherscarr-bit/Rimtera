namespace Rimtera.World;

/// <summary>
/// Batch A generation: the Precursor Dungeon (one hemisphere, guarded until the gate boss falls), buried cabins
/// with chests, lamps and the odd dart trap, giant canopy trees with a hollow root room, weapon shrines, silk-walled
/// mite warrens, surface trees, cacti, vines and herbs, and breakable pots. Sizes are fractions of the world and
/// counts scale with area, so every pass stays linear in area.
/// </summary>
public sealed partial class LargeWorldGenerator
{
    private bool InStructure(int left, int top, int right, int bottom, int margin)
    {
        bool Hit((int Left, int Top, int Right, int Bottom) box) =>
            left < box.Right + margin && right > box.Left - margin && top < box.Bottom + margin && bottom > box.Top - margin;
        return (Features.Dungeon is { } dungeon && Hit(dungeon)) || (Features.Vault is { } vault && Hit(vault)) ||
               (Features.Derelict is { } ship && Hit(ship)) || Features.Cabins.Any(Hit) || Features.GiantTrees.Any(Hit) ||
               Features.BloomHives.Any(Hit);
    }

    private bool AreaProtected(TileGrid grid, int left, int top, int right, int bottom)
    {
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
            if (!grid.Contains(x, y) || y >= _height - 2 || IsProtected(grid.TileAt(x, y)))
                return true;
        return false;
    }

    /// <summary>Hollow box: shell tile on the border, air inside, wall everywhere (protected cells are left alone).</summary>
    private static void Room(TileGrid grid, int left, int top, int right, int bottom, TileId shell, ushort wall, bool keepShell = false)
    {
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            if (!grid.Contains(x, y) || y >= grid.Height - 1)
                continue;
            var border = x == left || x == right || y == top || y == bottom;
            var current = grid.TileAt(x, y);
            if (border)
            {
                if (keepShell && current == TileId.Air)
                    continue;
                if (!IsProtected(current) || current == shell)
                    grid.SetTile(new GridPoint(x, y), shell);
            }
            else if (!IsProtected(current) || current == shell)
            {
                grid.SetTile(new GridPoint(x, y), TileId.Air);
                var cell = grid.GetCell(x, y);
                if (cell.LiquidUnits > 0)
                {
                    cell.LiquidUnits = 0;
                    cell.LiquidKind = LiquidKind.None;
                    grid.SetCell(new GridPoint(x, y), cell);
                }
            }
            grid.SetWall(new GridPoint(x, y), wall);
        }
    }

    private static void Put(TileGrid grid, int x, int y, TileId tile, byte state = 0)
    {
        if (!grid.Contains(x, y))
            return;
        grid.SetTile(new GridPoint(x, y), tile);
        if (state != 0)
        {
            var cell = grid.GetCell(x, y);
            cell.State = state;
            grid.SetCell(new GridPoint(x, y), cell);
        }
    }

    // ---------------------------------------------------------------- the Precursor Dungeon

    private void PrecursorDungeon(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var spawn = Features.SpawnColumn > 0 ? Features.SpawnColumn : _width / 2;
        var hemisphere = random.Next(2) == 0 ? -1 : 1;
        var reach = Math.Clamp(_width / 9, 30, 150);
        var shaftDepth = 0;
        var x0 = 0;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var side = attempt % 2 == 0 ? hemisphere : -hemisphere;
            var fraction = 0.16 + random.NextDouble() * 0.12;
            x0 = side < 0 ? (int)(_width * fraction) : (int)(_width * (1 - fraction));
            if (InCoast(x0) || InCoast(x0 - 8) || InCoast(x0 + 8) || NearSpawn(x0, 0.08) || InBlightZone(x0, 8))
                continue;
            var surface = TopSolid(grid, catalog, x0);
            shaftDepth = Math.Clamp((int)((_moltenTop[x0] - surface) * 0.45), 18, 150);
            var dir = x0 < spawn ? 1 : -1;
            var left = Math.Min(x0 - 8, x0 + dir * reach);
            var right = Math.Max(x0 + 8, x0 + dir * reach);
            if (left < 2 || right > _width - 3 || InStructure(left, surface - 12, right, surface + shaftDepth + 12, 6))
                continue;
            hemisphere = side;
            break;
        }
        if (shaftDepth == 0)
            return;
        var top = TopSolid(grid, catalog, x0);
        var direction = x0 < spawn ? 1 : -1;
        const TileId brick = TileId.DungeonBrickPrecursor;
        var minX = x0 - 7;
        var maxX = x0 + 7;
        var bottomMost = top;

        // The gatehouse above ground, open at ground level on both sides.
        Room(grid, x0 - 7, top - 10, x0 + 7, top + 1, brick, WallIds.Dungeon);
        for (var dy = 1; dy <= 3; dy++)
        {
            Put(grid, x0 - 7, top + 1 - dy, TileId.Air);
            Put(grid, x0 + 7, top + 1 - dy, TileId.Air);
        }
        // Ground under the doors so the entrance is walkable.
        for (var x = x0 - 12; x <= x0 + 12; x++)
            if (grid.Contains(x, top + 1) && !catalog[grid.TileAt(x, top + 1)].IsSolid && !IsProtected(grid.TileAt(x, top + 1)))
                grid.SetTile(new GridPoint(x, top + 1), brick);
        Put(grid, x0 - 4, top - 1, TileId.PotDungeon);
        Put(grid, x0 + 4, top - 1, TileId.PotDungeon);
        Put(grid, x0, top - 7, TileId.CeilingLight);

        // The shaft down, with one-way platforms every few rows (drop through them going down).
        var shaftBottom = Math.Min(_height - 14, top + shaftDepth);
        Room(grid, x0 - 3, top, x0 + 3, shaftBottom, brick, WallIds.Dungeon);
        for (var x = x0 - 2; x <= x0 + 2; x++)
            Put(grid, x, top, TileId.PlankPlatform);
        for (var y = top + 7; y < shaftBottom - 2; y += 7)
            for (var x = x0 - 2; x <= x0 + 2; x++)
                Put(grid, x, y, TileId.MetalPlatform);
        bottomMost = shaftBottom;

        // Corridors branch toward the world's middle (and a short stub away from it), ending in strongrooms.
        var branches = Math.Clamp(shaftDepth / 14, 2, 7);
        for (var i = 1; i <= branches; i++)
        {
            token.ThrowIfCancellationRequested();
            var cy = top + shaftDepth * i / (branches + 1);
            var dir = i % 3 == 0 ? -direction : direction;
            var length = dir == direction ? reach - 12 - random.Next(Math.Max(1, reach / 4)) : 10 + random.Next(8);
            length = Math.Max(10, length);
            var end = x0 + dir * length;
            end = Math.Clamp(end, 8, _width - 9);
            var (cl, cr) = dir > 0 ? (x0 + 3, end) : (end, x0 - 3);
            Room(grid, cl, cy - 4, cr, cy + 1, brick, WallIds.Dungeon);
            // Open the corridor into the shaft.
            for (var dy = 0; dy < 4; dy++)
                Put(grid, dir > 0 ? x0 + 3 : x0 - 3, cy - dy, TileId.Air);
            // Strongroom at the far end.
            var (rl, rr) = dir > 0 ? (end - 2, end + 10) : (end - 10, end + 2);
            rl = Math.Clamp(rl, 2, _width - 3);
            rr = Math.Clamp(rr, 2, _width - 3);
            Room(grid, rl, cy - 7, rr, cy + 1, brick, WallIds.Dungeon, keepShell: true);
            for (var dy = 0; dy < 4; dy++)
                Put(grid, dir > 0 ? rl : rr, cy - dy, TileId.Air);
            var mid = (rl + rr) / 2;
            Put(grid, mid, cy, TileId.DungeonChest);
            Put(grid, mid - 3, cy, TileId.PotDungeon);
            Put(grid, mid + 3, cy, TileId.PotDungeon);
            Put(grid, mid, cy - 5, TileId.CeilingLight);
            // Spaced pots and lamps down the corridor.
            for (var x = Math.Min(cl, cr) + 4; x < Math.Max(cl, cr) - 2; x += 9)
                Put(grid, x, cy, random.Next(3) == 0 ? TileId.WallTorch : TileId.PotDungeon);
            minX = Math.Min(minX, rl);
            maxX = Math.Max(maxX, rr);
        }

        // The great hall at the bottom of the shaft.
        var hallLeft = Math.Clamp(x0 - 12, 2, _width - 3);
        var hallRight = Math.Clamp(x0 + 12, 2, _width - 3);
        Room(grid, hallLeft, shaftBottom - 1, hallRight, shaftBottom + 9, brick, WallIds.Dungeon, keepShell: true);
        for (var x = x0 - 2; x <= x0 + 2; x++)
            Put(grid, x, shaftBottom - 1, TileId.Air);
        Put(grid, x0 - 8, shaftBottom + 8, TileId.DungeonChest);
        Put(grid, x0 + 8, shaftBottom + 8, TileId.DungeonChest);
        for (var x = x0 - 5; x <= x0 + 5; x += 5)
            Put(grid, x, shaftBottom + 8, TileId.PotDungeon);
        Put(grid, x0, shaftBottom + 2, TileId.CeilingLight);
        bottomMost = shaftBottom + 9;
        minX = Math.Min(minX, hallLeft);
        maxX = Math.Max(maxX, hallRight);
        Features.Dungeon = (minX, top - 10, maxX, bottomMost);
    }

    // ---------------------------------------------------------------- buried cabins

    private void BuriedCabins(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var target = N(WorldGenDensity.Cabins);
        for (var attempt = 0; attempt < target * 40 && Features.Cabins.Count < target; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var width = 9 + random.Next(5);
            var height = 6 + random.Next(2);
            var left = random.Next(_width / 16, Math.Max(_width / 16 + 1, _width - _width / 16 - width));
            var lowest = Math.Min(_moltenTop[left], _moltenTop[Math.Min(_width - 1, left + width)]) - height - 6;
            var highest = _surface[left] + 10;
            if (lowest <= highest)
                continue;
            var top = random.Next(highest, lowest);
            var right = left + width;
            var bottom = top + height;
            if (InCoast(left) || InCoast(right) || InStructure(left, top, right, bottom, 6) || AreaProtected(grid, left, top, right, bottom))
                continue;
            var shell = random.Next(3) == 0 ? TileId.HullPlatingGrey : TileId.AstroTimberPlanks;
            Room(grid, left, top, right, bottom, shell, WallIds.Cabin);
            var floor = bottom - 1;
            // A doorway on one side, a shelf of platforms, the chest, a lamp and a little furniture.
            var doorLeft = random.Next(2) == 0;
            for (var dy = 0; dy < 3; dy++)
                Put(grid, doorLeft ? left : right, floor - dy, TileId.Air);
            var chestX = doorLeft ? right - 2 : left + 2;
            Put(grid, chestX, floor, TileId.CabinChest);
            Put(grid, doorLeft ? left + 2 : right - 2, top + 2, TileId.WallTorch);
            Put(grid, (left + right) / 2, floor, random.Next(2) == 0 ? TileId.PlankTable : TileId.PlankChair);
            for (var x = (left + right) / 2 - 1; x <= (left + right) / 2 + 1; x++)
                Put(grid, x, top + 3, TileId.PlankPlatform);
            // Sometimes a pressure plate before the chest is wired to a dart trap in the far wall.
            if (random.Next(3) == 0)
            {
                var plateX = chestX + (doorLeft ? -1 : 1);
                var farWall = doorLeft ? right : left;
                Put(grid, plateX, floor, TileId.PressurePlate);
                Put(grid, farWall, floor - 1, TileId.DartTrap, doorLeft ? (byte)1 : (byte)0);
                for (var x = Math.Min(plateX, farWall); x <= Math.Max(plateX, farWall); x++)
                    Features.TrapWires.Add((new GridPoint(x, floor), 1));
                Features.TrapWires.Add((new GridPoint(farWall, floor - 1), 1));
            }
            Features.Cabins.Add((left, top, right, bottom));
        }
    }

    // ---------------------------------------------------------------- giant canopy trees

    private void GiantCanopyTrees(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var target = N(WorldGenDensity.GiantTrees);
        for (var attempt = 0; attempt < target * 30 && Features.GiantTrees.Count < target; attempt++)
        {
            var x = random.Next(_width / 8, _width - _width / 8);
            var ground = TopSolid(grid, catalog, x);
            var height = 18 + random.Next(9);
            // The heart of each canopy stays open sky (giant trees grow toward its edges).
            if (Features.Canopies.Any(c => Math.Abs(x - (c.Left + c.Right) / 2) < 16))
                continue;
            if (IsFrozen(x, ground) || IsJunk(x, ground) || InCoast(x) || NearSpawn(x, 0.05) || InBlightZone(x, 12) ||
                grid.TileAt(x, ground) is not (TileId.Grass or TileId.JungleGrass or TileId.CanopyGrass or TileId.Soil) ||
                Math.Abs(TopSolid(grid, catalog, x - 4) - ground) > 3 || Math.Abs(TopSolid(grid, catalog, x + 4) - ground) > 3 ||
                ground - height - 8 < 2 || InStructure(x - 10, ground - height - 6, x + 10, ground + 14, 8))
                continue;
            var trunkTop = ground - height;
            // Trunk walls, hollow core with a ladder from the canopy floor to the root room.
            for (var y = trunkTop; y <= ground + 6; y++)
            for (var dx = -2; dx <= 2; dx++)
            {
                var px = x + dx;
                var outer = Math.Abs(dx) == 2;
                if (IsProtected(grid.TileAt(px, y)) && grid.TileAt(px, y) != TileId.GiantTrunk)
                    continue;
                grid.SetTile(new GridPoint(px, y), outer || y == trunkTop ? TileId.GiantTrunk : dx == 0 ? TileId.Ladder : TileId.Air);
                grid.SetWall(new GridPoint(px, y), WallIds.LivingWood);
            }
            // Doorway at ground level.
            for (var dy = 1; dy <= 3; dy++)
                Put(grid, x - 2, ground - dy, TileId.Air);
            // The hollow root room.
            Room(grid, x - 6, ground + 6, x + 6, ground + 12, TileId.GiantTrunk, WallIds.LivingWood);
            Put(grid, x, ground + 6, TileId.Ladder);
            Put(grid, x + 4, ground + 11, TileId.CabinChest);
            Put(grid, x - 4, ground + 8, TileId.WallTorch);
            Put(grid, x - 2, ground + 11, TileId.PotCanopy);
            // Canopy of living leaves around the crown.
            var rx = 8 + random.Next(4);
            var ry = 4 + random.Next(2);
            for (var dy = -ry; dy <= ry; dy++)
            for (var dx = -rx; dx <= rx; dx++)
            {
                var px = x + dx;
                var py = trunkTop - 1 + dy;
                if (!grid.Contains(px, py) || dx * dx / (double)(rx * rx) + dy * dy / (double)(ry * ry) > 1.0 + random.NextDouble() * 0.15)
                    continue;
                if (grid.TileAt(px, py) == TileId.Air)
                    grid.SetTile(new GridPoint(px, py), TileId.GiantLeaf);
            }
            // Roots spreading into the soil.
            for (var root = 0; root < 4; root++)
            {
                var rxp = x + (root < 2 ? -3 : 3);
                var ryp = ground + 2;
                for (var step = 0; step < 10; step++)
                {
                    rxp += root % 2 == 0 ? Math.Sign(rxp - x) : random.Next(-1, 2);
                    ryp += random.Next(0, 2);
                    if (grid.Contains(rxp, ryp) && !IsProtected(grid.TileAt(rxp, ryp)) && catalog[grid.TileAt(rxp, ryp)].IsSolid)
                        grid.SetTile(new GridPoint(rxp, ryp), TileId.GiantTrunk);
                }
            }
            Features.GiantTrees.Add((x - rx, trunkTop - ry - 1, x + rx, ground + 12));
        }
    }

    // ---------------------------------------------------------------- weapon shrines

    private void WeaponShrines(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var target = N(WorldGenDensity.WeaponShrines);
        for (var attempt = 0; attempt < target * 40 && Features.Shrines.Count < target; attempt++)
        {
            var x = random.Next(_width / 10, _width - _width / 10);
            var lowest = _moltenTop[x] - 12;
            var highest = _rockLine[x] + 6;
            if (lowest <= highest)
                continue;
            var y = random.Next(highest, lowest);
            if (InCoast(x) || InStructure(x - 6, y - 5, x + 6, y + 1, 10) || AreaProtected(grid, x - 6, y - 5, x + 6, y + 1) ||
                Features.Shrines.Any(point => Math.Abs(point.X - x) < Math.Min(_width / 8, 90) && Math.Abs(point.Y - y) < 60))
                continue;
            Room(grid, x - 6, y - 5, x + 6, y + 1, TileId.AlabasterBrick, WallIds.NaturalAlabaster);
            Put(grid, x, y, TileId.WeaponShrine);
            Put(grid, x - 3, y, TileId.StandingTorch);
            Put(grid, x + 3, y, TileId.StandingTorch);
            // A narrow way in from one side.
            var side = random.Next(2) == 0 ? -6 : 6;
            for (var dy = 0; dy < 3; dy++)
                Put(grid, x + side, y - dy, TileId.Air);
            Features.Shrines.Add(new GridPoint(x, y));
        }
    }

    // ---------------------------------------------------------------- mite warrens

    private void WebMiteWarrens(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        foreach (var (left, top, right, bottom) in Features.MiteWarrens)
        {
            for (var y = top - 1; y <= bottom + 1; y++)
            for (var x = left - 1; x <= right + 1; x++)
            {
                if (!grid.Contains(x, y) || IsProtected(grid.TileAt(x, y)))
                    continue;
                var tile = grid.TileAt(x, y);
                if (tile is TileId.Air or TileId.MiteSilk or TileId.MiteSilkCocoon)
                    grid.SetWall(new GridPoint(x, y), WallIds.MiteSilk);
            }
            // Extra cocoons on the warren floor.
            for (var i = 0; i < 2; i++)
            {
                var x = random.Next(left + 1, Math.Max(left + 2, right));
                for (var y = top; y < bottom; y++)
                    if (IsCaveFloor(grid, catalog, x, y))
                    {
                        grid.SetTile(new GridPoint(x, y), TileId.MiteSilkCocoon);
                        break;
                    }
            }
        }
    }

    // ---------------------------------------------------------------- flora

    private void Flora(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        // Surface trees, spaced apart; species by ground (palms on junk sand and beaches, pines on snow).
        for (var x = 3; x < _width - 3; x += 4 + random.Next(7))
        {
            token.ThrowIfCancellationRequested();
            var top = TopSolid(grid, catalog, x);
            var ground = grid.TileAt(x, top);
            if (TilePhysics.TreeFor(ground) is not { } species || random.Next(4) == 0 || InBlightZone(x, 2) ||
                grid.GetCell(x, top).Shape != TileShape.Full)
                continue;
            if (species.Trunk == TileId.PalmTrunk && !IsJunk(x, top) && !InCoast(x))
                continue;
            if (TilePhysics.TryGrowTree(grid, catalog, x, top - 1, random))
                Features.Trees++;
        }

        // Cacti on junk dunes.
        for (var x = 3; x < _width - 3; x += 3 + random.Next(6))
        {
            var top = TopSolid(grid, catalog, x);
            if (!IsJunk(x, top) || grid.TileAt(x, top) is not (TileId.DesertSand or TileId.Sand) || random.Next(4) != 0)
                continue;
            var height = 2 + random.Next(3);
            for (var dy = 1; dy <= height && grid.TileAt(x, top - dy) == TileId.Air && grid.GetCell(x, top - dy).LiquidUnits == 0; dy++)
                grid.SetTile(new GridPoint(x, top - dy), TileId.Cactus);
        }

        // Herbs wherever their soil lies open (surface and caves), most of them already mature.
        for (var i = N(WorldGenDensity.Herbs); i > 0; i--)
        {
            var x = random.Next(1, _width - 1);
            var y = random.Next(Math.Max(1, _surface[x] - 12), _height - 2);
            if (grid.TileAt(x, y) != TileId.Air || grid.GetCell(x, y).LiquidUnits > 0)
                continue;
            var soil = grid.TileAt(x, y + 1);
            if (grid.GetCell(x, y + 1).Shape != TileShape.Full)
                continue;
            foreach (var herb in TilePhysics.AllHerbs)
            {
                if (Array.IndexOf(herb.Soils, soil) < 0)
                    continue;
                Put(grid, x, y, herb.Tile, random.Next(3) == 0 ? (byte)0 : (byte)1);
                Features.Herbs++;
                break;
            }
        }

        // Vines hang from grassy ceilings.
        for (var i = N(WorldGenDensity.Vines); i > 0; i--)
        {
            var x = random.Next(1, _width - 1);
            var y = random.Next(Math.Max(1, _surface[x]), _height - 2);
            if (grid.TileAt(x, y) is not (TileId.Grass or TileId.JungleGrass or TileId.CanopyGrass) || grid.TileAt(x, y + 1) != TileId.Air)
                continue;
            var length = 2 + random.Next(6);
            for (var dy = 1; dy <= length && grid.TileAt(x, y + dy) == TileId.Air; dy++)
                grid.SetTile(new GridPoint(x, y + dy), TileId.Vine);
        }
    }

    // ---------------------------------------------------------------- pots

    private void Pots(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var target = N(WorldGenDensity.Pots);
        var placed = 0;
        for (var attempt = 0; attempt < target * 30 && placed < target; attempt++)
        {
            var x = random.Next(2, _width - 2);
            var y = RandomRow(random, _surface[x] + 6, _height - 3);
            if (!IsCaveFloor(grid, catalog, x, y))
                continue;
            var wall = grid.GetCell(x, y).WallId;
            var pot = wall == WallIds.Dungeon ? TileId.PotDungeon
                : y >= _moltenTop[x] - 4 ? TileId.PotMolten
                : IsFrozen(x, y) ? TileId.PotFrozen
                : IsJunk(x, y) ? TileId.PotJunk
                : Features.Canopies.Any(c => x >= c.Left && x <= c.Right) && y < _cavernRow ? TileId.PotCanopy
                : y >= _cavernRow ? TileId.PotCavern
                : TileId.PotSurface;
            grid.SetTile(new GridPoint(x, y), pot);
            placed++;
        }
        Features.Pots += placed;
    }
}
