namespace Rimtera.World;

/// <summary>
/// World-generation completion, structures and decoration: buried landers, the vault temple maze with its traps and
/// altar, canopy shrines, cave wall variety, slag foundries, campsites / rigged chests / mining caches, statues
/// (some wired), world traps, surface and sunken chests, moss caves, speleothems and crystal shrubs, exposed gems,
/// surface ore, fallen logs, water life, lilies / dyes / gourds / mushrooms, rubble and shell piles, graveyards,
/// brood pods and the final chest-content pass. Counts come from <see cref="WorldGenDensity"/>; all designs original.
/// </summary>
public sealed partial class LargeWorldGenerator
{
    /// <summary>Wires an L-shaped run from <paramref name="a"/> to <paramref name="b"/> (horizontal first) on a layer.</summary>
    private void WireRun(GridPoint a, GridPoint b, int layer = 1)
    {
        var step = Math.Sign(b.X - a.X);
        for (var x = a.X; x != b.X; x += step)
            Features.TrapWires.Add((new GridPoint(x, a.Y), layer));
        step = Math.Sign(b.Y - a.Y);
        for (var y = a.Y; y != b.Y; y += step)
            Features.TrapWires.Add((new GridPoint(b.X, y), layer));
        Features.TrapWires.Add((b, layer));
    }

    /// <summary>A cave floor at least <paramref name="half"/> cells to each side.</summary>
    private bool FlatFloor(TileGrid grid, TileCatalog catalog, int x, int y, int half)
    {
        for (var dx = -half; dx <= half; dx++)
            if (!IsCaveFloor(grid, catalog, x + dx, y))
                return false;
        return true;
    }

    /// <summary>Finds a cave floor in the column at or below a random row in [top, bottom). Returns -1 when none.</summary>
    private int FloorBelow(TileGrid grid, TileCatalog catalog, Random random, int x, int top, int bottom)
    {
        if (bottom <= top + 1)
            return -1;
        for (var y = RandomRow(random, top, bottom); y < Math.Min(bottom + 20, _height - 2); y++)
            if (IsCaveFloor(grid, catalog, x, y))
                return y;
        return -1;
    }

    // ---------------------------------------------------------------- buried landers

    private void BuriedLanders(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var wanted = N(WorldGenDensity.BuriedLanders);
        for (var attempt = 0; attempt < wanted * 30 && Features.SitesOf(FeatureNames.BuriedLanders).Count < wanted; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var half = random.Next(11, 18);
            var center = _duneSites.Count > 0 && random.Next(3) > 0
                ? _duneSites[random.Next(_duneSites.Count)].Center + random.Next(-8, 9)
                : random.Next(_width / 2, _width - half - 4);
            if (center - half < 4 || center + half >= _width - 4)
                continue;
            var ground = TopSolid(grid, catalog, center);
            var apex = ground - 3;
            var height = half;
            if (!IsJunk(center - half, ground) || !IsJunk(center + half, ground) || InCoast(center - half - 2) || InCoast(center + half + 2) ||
                apex < 4 || apex + height >= _cavernRow || InStructure(center - half, apex, center + half, apex + height, 6) ||
                AreaProtected(grid, center - half, apex, center + half, apex + height) ||
                Features.SitesOf(FeatureNames.BuriedLanders).Any(l => center - half < l.Right + 10 && center + half > l.Left - 10))
                continue;
            for (var r = 0; r <= height; r++)
            {
                var y = apex + r;
                var reach = r * half / height;
                for (var dx = -reach; dx <= reach; dx++)
                {
                    var x = center + dx;
                    var shell = Math.Abs(dx) >= reach - 1 || r == height || r >= height - 1;
                    var deck = r > 2 && r < height - 1 && r % 5 == 0 && (r / 5 % 2 == 0 ? dx < reach - 3 : dx > -reach + 3);
                    TileId tile = shell ? TileId.DungeonBrick : deck ? TileId.Wood : TileId.Air;
                    // The hatch at the apex stays open to the sky.
                    if (r <= 1 && dx == 0)
                        tile = TileId.Air;
                    grid.SetTile(new GridPoint(x, y), tile);
                    var cell = grid.GetCell(x, y);
                    if (cell.LiquidUnits > 0)
                    {
                        cell.LiquidUnits = 0;
                        cell.LiquidKind = LiquidKind.None;
                        grid.SetCell(new GridPoint(x, y), cell);
                    }
                    grid.SetWall(new GridPoint(x, y), shell ? WallIds.None : WallIds.DerelictHull);
                }
                // Ladders down through each deck gap.
                if (r > 2 && r < height - 1 && r % 5 == 0)
                {
                    var gapX = r / 5 % 2 == 0 ? center + reach - 2 : center - reach + 2;
                    for (var ly = y - 4; ly <= y + 1; ly++)
                        if (grid.TileAt(gapX, ly) == TileId.Air)
                            Put(grid, gapX, ly, TileId.Ladder);
                }
            }
            for (var ly = apex; ly <= apex + 3; ly++)
                if (grid.TileAt(center, ly) == TileId.Air)
                    Put(grid, center, ly, TileId.Ladder);
            // The loot chamber on the bottom deck.
            var floor = apex + height - 2;
            Put(grid, center, floor, TileId.LanderChest);
            Put(grid, center - 3, floor, TileId.PotJunk);
            Put(grid, center + 3, floor, TileId.SalvagePod);
            Features.SalvagePods.Add(new GridPoint(center + 3, floor));
            Put(grid, center - 5, floor - 3, TileId.WallTorch);
            _hutFloors.Add((center + 1, floor));
            Features.AddSite(FeatureNames.BuriedLanders, center - half, apex, center + half, apex + height);
        }
    }

    // ---------------------------------------------------------------- the vault temple

    private void VaultTemple(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        if (Features.Vault is not { } vault)
            return;
        var (vaultLeft, vaultTop, vaultRight, vaultBottom) = vault;
        // The altar in the temple heart, under the vault's dividing wall.
        var heart = vaultLeft + (vaultRight - vaultLeft) / 2;
        Put(grid, heart, vaultBottom - 2, TileId.VaultAltar);
        Features.AddSite(FeatureNames.TempleAltar, new GridPoint(heart, vaultBottom - 2));

        const int cellWidth = 8, cellHeight = 6;
        var rows = Math.Clamp(_height / 450, 2, 5);
        var cols = Math.Clamp(_width / 700, 3, 10);
        var mazeRight = vaultLeft - 1;
        var mazeBottom = vaultBottom - 1;
        int mazeTop = 0, mazeLeft = 0;
        var fits = false;
        // The largest maze that fits beside the vault (it may cut through hive comb, never through protected tiles).
        for (var tryRows = rows; tryRows >= 2 && !fits; tryRows--)
        for (var tryCols = cols; tryCols >= 2 && !fits; tryCols--)
        {
            mazeTop = mazeBottom - tryRows * cellHeight;
            mazeLeft = mazeRight - tryCols * cellWidth;
            if (mazeLeft < 4 || mazeTop < _rockLine[Math.Clamp(mazeLeft, 0, _width - 1)] ||
                AreaProtected(grid, mazeLeft - 1, mazeTop - 1, mazeRight - 1, mazeBottom + 1))
                continue;
            (rows, cols, fits) = (tryRows, tryCols, true);
        }
        if (!fits)
            return;
        Fill(grid, mazeLeft, mazeTop, mazeRight, mazeBottom, TileId.PrecursorBrick, force: false);
        for (var y = mazeTop; y <= mazeBottom; y++)
        for (var x = mazeLeft; x <= mazeRight; x++)
        {
            grid.SetWall(new GridPoint(x, y), WallIds.Precursor);
            var cell = grid.GetCell(x, y);
            if (cell.LiquidUnits > 0)
            {
                cell.LiquidUnits = 0;
                cell.LiquidKind = LiquidKind.None;
                grid.SetCell(new GridPoint(x, y), cell);
            }
        }
        void Open(int left, int top, int right, int bottom)
        {
            for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
                if (grid.Contains(x, y))
                    grid.SetTile(new GridPoint(x, y), TileId.Air);
        }
        // Rooms, then a randomised depth-first spanning tree of passages between them.
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
            Open(mazeLeft + c * cellWidth + 1, mazeTop + r * cellHeight + 1, mazeLeft + (c + 1) * cellWidth - 1, mazeTop + (r + 1) * cellHeight - 1);
        var visited = new bool[cols, rows];
        var stack = new Stack<(int C, int R)>();
        stack.Push((cols - 1, rows - 1));
        visited[cols - 1, rows - 1] = true;
        while (stack.Count > 0)
        {
            var (c, r) = stack.Peek();
            var options = new List<(int C, int R)>();
            foreach (var (dc, dr) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                if (c + dc >= 0 && c + dc < cols && r + dr >= 0 && r + dr < rows && !visited[c + dc, r + dr])
                    options.Add((c + dc, r + dr));
            if (options.Count == 0)
            {
                stack.Pop();
                continue;
            }
            var (nc, nr) = options[random.Next(options.Count)];
            visited[nc, nr] = true;
            stack.Push((nc, nr));
            if (nr == r)
            {
                // A three-tall doorway in the shared side wall.
                var wallX = mazeLeft + Math.Max(c, nc) * cellWidth;
                var floorY = mazeTop + (r + 1) * cellHeight - 1;
                Open(wallX, floorY - 2, wallX, floorY);
            }
            else
            {
                // A ladder hole through the shared floor.
                var holeX = mazeLeft + c * cellWidth + 2 + random.Next(cellWidth - 4);
                var floorY = mazeTop + Math.Max(r, nr) * cellHeight;
                Open(holeX, floorY, holeX, floorY);
                for (var y = floorY - cellHeight + 1; y <= floorY + cellHeight - 1; y++)
                    if (grid.TileAt(holeX, y) == TileId.Air)
                        Put(grid, holeX, y, TileId.Ladder);
            }
        }
        // The last room opens onto the vault door; the first room onto an approach tunnel.
        Open(mazeRight, mazeBottom - 3, mazeRight, mazeBottom - 1);
        var entryFloor = mazeTop + cellHeight - 1;
        Open(mazeLeft, entryFloor - 2, mazeLeft, entryFloor);
        for (var x = mazeLeft - 1; x >= Math.Max(2, mazeLeft - 14); x--)
            for (var dy = 0; dy < 3; dy++)
                if (!IsProtected(grid.TileAt(x, entryFloor - dy)))
                    grid.SetTile(new GridPoint(x, entryFloor - dy), TileId.Air);
        // Furnish: torches and jars, and in some rooms a pressure plate wired to a dart trap or a strip of spikes.
        var rooms = 0;
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
        {
            rooms++;
            var left = mazeLeft + c * cellWidth;
            var floorY = mazeTop + (r + 1) * cellHeight - 1;
            var mid = left + cellWidth / 2;
            if (grid.TileAt(mid, floorY - 3) == TileId.Air && random.Next(2) == 0)
                Put(grid, mid, floorY - 3, TileId.WallTorch);
            if (grid.TileAt(left + 2, floorY) == TileId.Air && random.Next(3) == 0)
                Put(grid, left + 2, floorY, TileId.PotDungeon);
            if ((c, r) == (cols - 1, rows - 1) || (c, r) == (0, 0))
                continue;
            switch (random.Next(3))
            {
                case 0:
                {
                    var plateX = mid;
                    var wallX = random.Next(2) == 0 ? left : left + cellWidth;
                    if (grid.TileAt(plateX, floorY) != TileId.Air || grid.TileAt(wallX, floorY - 1) != TileId.PrecursorBrick)
                        break;
                    Put(grid, plateX, floorY, TileId.PressurePlate);
                    Put(grid, wallX, floorY - 1, TileId.DartTrap, wallX > plateX ? (byte)1 : (byte)0);
                    WireRun(new GridPoint(plateX, floorY), new GridPoint(wallX, floorY));
                    Features.TrapWires.Add((new GridPoint(wallX, floorY - 1), 1));
                    Features.AddCount(FeatureNames.DartTraps);
                    break;
                }
                case 1:
                    for (var x = mid - 1; x <= mid + 1; x++)
                        if (grid.TileAt(x, floorY) == TileId.Air)
                            Put(grid, x, floorY, TileId.SpikeStrip);
                    Features.AddCount(FeatureNames.SpikePits);
                    break;
            }
        }
        Features.AddCount(FeatureNames.TempleRooms, rooms);
        Features.AddSite("vault-temple", mazeLeft, mazeTop, mazeRight, mazeBottom);
    }

    // ---------------------------------------------------------------- canopy shrines

    private void CanopyShrines(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        if (Features.Canopies.Count == 0)
            return;
        var wanted = N(WorldGenDensity.CanopyShrines);
        for (var attempt = 0; attempt < wanted * 40 && Features.SitesOf(FeatureNames.CanopyShrines).Count < wanted; attempt++)
        {
            var (left, right) = Features.Canopies[random.Next(Features.Canopies.Count)];
            var x = random.Next(left + 6, Math.Max(left + 7, right - 6));
            var lowest = _cavernRow + (int)((_moltenTop[x] - _cavernRow) * 0.3) - 8;
            var highest = _surface[x] + 10;
            if (lowest <= highest)
                continue;
            var y = random.Next(highest, lowest);
            if (InStructure(x - 6, y - 6, x + 6, y + 1, 4) || AreaProtected(grid, x - 6, y - 6, x + 6, y + 1) ||
                Features.SitesOf(FeatureNames.CanopyShrines).Any(s => Math.Abs(s.Left + 6 - x) < 20 && Math.Abs(s.Bottom - y) < 12))
                continue;
            Room(grid, x - 6, y - 6, x + 6, y + 1, TileId.MudBrick, WallIds.MudBuilt);
            Put(grid, x, y, TileId.ShrineChest);
            Put(grid, x - 3, y, TileId.StandingTorch);
            Put(grid, x + 3, y, TileId.PotCanopy);
            var side = random.Next(2) == 0 ? -6 : 6;
            for (var dy = 0; dy < 3; dy++)
                Put(grid, x + side, y - dy, TileId.Air);
            _hutFloors.Add((x + 2, y));
            Features.AddSite(FeatureNames.CanopyShrines, x - 6, y - 6, x + 6, y + 1);
        }
    }

    // ---------------------------------------------------------------- wall variety

    /// <summary>Paints a wandering blob of walls, replacing only walls that <paramref name="replace"/> accepts.</summary>
    private void WallBlob(TileGrid grid, Random random, int x, int y, double radius, int steps, ushort wall, Func<ushort, bool> replace)
    {
        var cells = grid.RawCells;
        double cx = x, cy = y, vx = random.NextDouble() - 0.5, vy = random.NextDouble() - 0.5;
        for (var step = 0; step < steps; step++)
        {
            var r = radius * (0.8 + random.NextDouble() * 0.4);
            var reach = (int)Math.Ceiling(r);
            for (var oy = -reach; oy <= reach; oy++)
            for (var ox = -reach; ox <= reach; ox++)
            {
                var px = (int)cx + ox;
                var py = (int)cy + oy;
                if ((uint)px >= (uint)_width || py < 0 || py >= _height - 1 || ox * ox + oy * oy > r * r)
                    continue;
                ref var cell = ref cells[py * _width + px];
                if (cell.WallId != 0 && replace(cell.WallId))
                    cell.WallId = wall;
            }
            vx = Math.Clamp(vx + (random.NextDouble() - 0.5) * 0.6, -1.2, 1.2);
            vy = Math.Clamp(vy + (random.NextDouble() - 0.5) * 0.6, -1.2, 1.2);
            cx += vx * 2;
            cy += vy * 1.5;
        }
    }

    private void CaveWallVariety(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var cells = grid.RawCells;
        int grassWalls = 0, enclosed = 0;
        for (var x = 0; x < _width; x++)
        {
            var top = TopSolid(grid, catalog, x);
            var ground = grid.TileAt(x, top);
            // Root-laced walls under grassy ground.
            if (ground is TileId.Grass or TileId.JungleGrass or TileId.CanopyGrass)
                for (var y = top + 1; y < Math.Min(_height - 1, top + 9); y++)
                {
                    ref var cell = ref cells[y * _width + x];
                    if (cell.WallId == WallIds.Regolith)
                    {
                        cell.WallId = WallIds.GrassRoots;
                        grassWalls++;
                    }
                }
            // Enclosed pockets under the ground line get walls too (the wall pass starts a few rows down).
            for (var y = Math.Max(top + 1, _surface[x] - 2); y < Math.Min(_height - 1, _surface[x] + 8); y++)
            {
                var index = y * _width + x;
                if (cells[index].TileId != TileId.Air || cells[index].WallId != 0)
                    continue;
                cells[index].WallId = NaturalWallFor(grid, x, y);
                enclosed++;
            }
        }
        // Wall runs: soil and rock walls woven together, layered rock in the rock layer, rough rock in the caverns.
        var runs = N(WorldGenDensity.CaveWallRuns);
        for (var i = 0; i < runs; i++)
        {
            token.ThrowIfCancellationRequested();
            var x = random.Next(2, _width - 2);
            var y = RandomRow(random, _rockLine[x], _moltenTop[x] - 2);
            ushort wall;
            if (IsFrozen(x, y))
                wall = random.Next(2) == 0 ? WallIds.Ice : WallIds.CaveLayered;
            else if (IsJunk(x, y))
                wall = random.Next(2) == 0 ? WallIds.Scrap : WallIds.Sand;
            else if (y < _cavernRow)
                wall = random.Next(3) == 0 ? WallIds.Regolith : WallIds.CaveLayered;
            else if (y > _moltenTop[x] - 30)
                wall = WallIds.Ash;
            else
                wall = random.Next(3) == 0 ? WallIds.NaturalBasalt : WallIds.CaveRough;
            WallBlob(grid, random, x, y, 3 + random.NextDouble() * 4, random.Next(6, 18), wall,
                existing => existing is WallIds.Rock or WallIds.Regolith);
        }
        // Mud walls through the whole canopy column.
        foreach (var (left, right) in Features.Canopies)
        {
            var bottom = _cavernRow + (int)((_moltenTop[(left + right) / 2] - _cavernRow) * 0.35);
            for (var x = Math.Max(0, left - 3); x <= Math.Min(_width - 1, right + 3); x++)
            for (var y = _surface[x]; y <= bottom; y++)
            {
                ref var cell = ref cells[y * _width + x];
                if (cell.WallId is WallIds.Rock or WallIds.Regolith or WallIds.CaveLayered or WallIds.CaveRough or WallIds.GrassRoots)
                    cell.WallId = WallIds.NaturalMud;
            }
        }
        grid.MarkBulkEdit();
        Features.AddCount(FeatureNames.GrassWalls, grassWalls);
        Features.AddCount(FeatureNames.EnclosedWalls, enclosed);
        Features.AddCount(FeatureNames.CaveWallRuns, runs);
    }

    // ---------------------------------------------------------------- reactor ruins (the core's forts)

    /// <summary>
    /// Reactor ruins in the Irradiated Core (the site key keeps its old "slag-foundries" name): hazard-plated shells on
    /// vitrified floors, breached roofs, a lead-lined backing wall, the reactor forge, a reactor chest and isotope canisters.
    /// </summary>
    private void SlagFoundries(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var wanted = N(WorldGenDensity.SlagFoundries);
        for (var attempt = 0; attempt < wanted * 30 && Features.SitesOf(FeatureNames.SlagFoundries).Count < wanted; attempt++)
        {
            var width = random.Next(18, 28);
            var height = random.Next(8, 12);
            var left = random.Next(4, Math.Max(5, _width - width - 4));
            var right = left + width;
            // Ruins stand on the core cavern's floor (the highest floor cell under them), on a vitrified foundation.
            var bottom = int.MaxValue;
            var lowest = 0;
            for (var x = left; x <= right; x++)
            {
                bottom = Math.Min(bottom, _coreFloor[x]);
                lowest = Math.Max(lowest, _coreFloor[x]);
            }
            var top = bottom - height;
            if (lowest - bottom > 8 || top <= _coreCeiling[left] + 2 || top <= _coreCeiling[right] + 2 ||
                InStructure(left, top, right, bottom, 4) || AreaProtected(grid, left, top, right, bottom) ||
                Features.SitesOf(FeatureNames.SlagFoundries).Any(f => left < f.Right + 20 && right > f.Left - 20))
                continue;
            for (var x = left; x <= right; x++)
            for (var y = bottom + 1; y <= _coreFloor[x] + 1 && y < _height - 1; y++)
            {
                var cell = grid.GetCell(x, y);
                cell.TileId = TileId.VitrifiedGlass;
                cell.LiquidUnits = 0;
                cell.LiquidKind = LiquidKind.None;
                grid.SetCell(new GridPoint(x, y), cell);
            }
            Room(grid, left, top, right, bottom, TileId.HullPlatingHazard, WallIds.Lead);
            // Ruined: a breached roof, vitrified floor seams and green-plated patches where the shielding was patched.
            for (var x = left; x <= right; x++)
            {
                if (random.Next(5) == 0)
                    grid.SetTile(new GridPoint(x, top), TileId.Air);
                else if (random.Next(4) == 0)
                    grid.SetTile(new GridPoint(x, top), TileId.HullPlatingGreen);
                if (random.Next(3) == 0)
                    grid.SetTile(new GridPoint(x, bottom), TileId.VitrifiedGlass);
            }
            var floor = bottom - 1;
            for (var dy = 0; dy < 3; dy++)
            {
                Put(grid, left, floor - dy, TileId.Air);
                Put(grid, right, floor - dy, TileId.Air);
            }
            var mid = (left + right) / 2;
            Put(grid, mid, floor, TileId.SlagForge);
            Put(grid, mid + 4, floor, TileId.SlagChest);
            Put(grid, mid - 4, floor, TileId.StandingTorch);
            Put(grid, left + 2, floor, TileId.PotMolten);
            Put(grid, right - 2, floor, TileId.PotMolten);
            Features.AddSite(FeatureNames.SlagFoundries, left, top, right, bottom);
        }
    }

    // ---------------------------------------------------------------- micro-biomes

    private void MicroBiomes(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        // Campsites: a cold campfire with a seat and a heap of left-behind scrap.
        var wanted = N(WorldGenDensity.Campsites);
        for (var attempt = 0; attempt < wanted * 80 && Features.SitesOf(FeatureNames.Campsites).Count < wanted; attempt++)
        {
            var x = random.Next(4, _width - 4);
            var y = FloorBelow(grid, catalog, random, x, _rockLine[x], _moltenTop[x] - 8);
            if (y < 0 || !FlatFloor(grid, catalog, x, y, 2) || InStructure(x - 3, y - 3, x + 3, y, 2))
                continue;
            Put(grid, x, y, TileId.Campfire);
            Put(grid, x - 2, y, TileId.PlankChair);
            Put(grid, x + 2, y, TileId.LootPile);
            Features.AddSite(FeatureNames.Campsites, new GridPoint(x, y));
        }
        // Rigged chests: a plate in front of a chest is wired to an explosive under it.
        wanted = N(WorldGenDensity.RiggedChests);
        for (var attempt = 0; attempt < wanted * 80 && Features.SitesOf(FeatureNames.RiggedChests).Count < wanted; attempt++)
        {
            var x = random.Next(4, _width - 4);
            var y = FloorBelow(grid, catalog, random, x, _rockLine[x], _moltenTop[x] - 8);
            if (y < 0 || !FlatFloor(grid, catalog, x, y, 1) || !IsNaturalGround(grid.TileAt(x, y + 1)))
                continue;
            Put(grid, x, y, TileId.CabinChest);
            Put(grid, x - 1, y, TileId.PressurePlate);
            Put(grid, x, y + 1, TileId.ExplosiveTrap);
            WireRun(new GridPoint(x - 1, y), new GridPoint(x - 1, y + 1));
            Features.TrapWires.Add((new GridPoint(x, y + 1), 1));
            Features.AddSite(FeatureNames.RiggedChests, new GridPoint(x, y));
        }
        // Mining caches: a stash with a torch, rubble and exposed ore around it.
        wanted = N(WorldGenDensity.MiningCaches);
        for (var attempt = 0; attempt < wanted * 80 && Features.SitesOf(FeatureNames.MiningCaches).Count < wanted; attempt++)
        {
            var x = random.Next(4, _width - 4);
            var y = FloorBelow(grid, catalog, random, x, _rockLine[x], _moltenTop[x] - 8);
            if (y < 0 || !FlatFloor(grid, catalog, x, y, 2))
                continue;
            Put(grid, x - 2, y, TileId.StandingTorch);
            Put(grid, x, y, TileId.RubblePile);
            grid.SetTile(new GridPoint(x + 2, y), TileId.SupplyCache);
            Features.SupplyCaches.Add(new GridPoint(x + 2, y));
            var ore = y < _cavernRow ? TileId.CopperOre : TileId.CobaltOre;
            Blob(grid, random, x, y + 2, 1.6, random.Next(3, 6), random.NextDouble() - 0.5, 0.2,
                tile => tile is TileId.Rock or TileId.Soil or TileId.Slate ? ore : null);
            Features.AddSite(FeatureNames.MiningCaches, new GridPoint(x, y));
        }
    }

    // ---------------------------------------------------------------- statues

    private void Statues(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var wanted = N(WorldGenDensity.Statues);
        TileId[] kinds = [TileId.StatueHopper, TileId.StatueCrab, TileId.StatueBat];
        for (var attempt = 0; attempt < wanted * 60 && Features.SitesOf(FeatureNames.Statues).Count < wanted; attempt++)
        {
            var x = random.Next(4, _width - 4);
            var y = FloorBelow(grid, catalog, random, x, _rockLine[x], _moltenTop[x] - 4);
            if (y < 0)
                continue;
            var plateX = random.Next(2) == 0 ? x + 3 : x - 3;
            if (!IsCaveFloor(grid, catalog, plateX, y))
                plateX = 2 * x - plateX;
            var wired = Features.SitesOf(FeatureNames.Statues).Count % 3 == 0 && IsCaveFloor(grid, catalog, plateX, y);
            Put(grid, x, y, kinds[random.Next(kinds.Length)]);
            Features.AddSite(FeatureNames.Statues, new GridPoint(x, y));
            if (!wired)
                continue;
            Put(grid, plateX, y, TileId.PressurePlate);
            WireRun(new GridPoint(plateX, y), new GridPoint(x, y), layer: 2);
            Features.AddSite(FeatureNames.WiredStatues, new GridPoint(x, y));
        }
    }

    // ---------------------------------------------------------------- traps

    private void Traps(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        int Spot(out int x)
        {
            x = random.Next(4, _width - 4);
            var y = FloorBelow(grid, catalog, random, x, _rockLine[x], _moltenTop[x] - 4);
            if (y < 0 || NearSpawn(x, 0.03) || InStructure(x - 2, y - 2, x + 2, y + 2, 1))
                return -1;
            return y;
        }

        // Dart traps: a plate on the floor, the trap set into the nearest wall at body height.
        var wanted = N(WorldGenDensity.DartTraps);
        for (var attempt = 0; attempt < wanted * 40 && Features.CountOf(FeatureNames.DartTraps) < wanted; attempt++)
        {
            var y = Spot(out var x);
            if (y < 0)
                continue;
            var dir = random.Next(2) == 0 ? -1 : 1;
            var wallX = -1;
            for (var d = 2; d <= 10; d++)
            {
                var tx = x + dir * d;
                if (grid.TileAt(tx, y) != TileId.Air)
                {
                    if (IsNaturalGround(grid.TileAt(tx, y - 1)) && IsNaturalGround(grid.TileAt(tx, y)))
                        wallX = tx;
                    break;
                }
            }
            if (wallX < 0)
                continue;
            Put(grid, x, y, TileId.PressurePlate);
            Put(grid, wallX, y - 1, TileId.DartTrap, dir > 0 ? (byte)1 : (byte)0);
            WireRun(new GridPoint(x, y), new GridPoint(wallX, y));
            Features.TrapWires.Add((new GridPoint(wallX, y - 1), 1));
            Features.AddCount(FeatureNames.DartTraps);
        }
        // Explosive traps under a plate.
        wanted = N(WorldGenDensity.ExplosiveTraps);
        for (var attempt = 0; attempt < wanted * 40 && Features.CountOf(FeatureNames.ExplosiveTraps) < wanted; attempt++)
        {
            var y = Spot(out var x);
            if (y < 0 || !IsNaturalGround(grid.TileAt(x, y + 1)))
                continue;
            Put(grid, x, y, TileId.PressurePlate);
            Put(grid, x, y + 1, TileId.ExplosiveTrap);
            WireRun(new GridPoint(x, y), new GridPoint(x, y + 1));
            Features.AddCount(FeatureNames.ExplosiveTraps);
        }
        // Boulder traps: a boulder held above the ceiling by an actuated block wired to a plate below.
        wanted = N(WorldGenDensity.BoulderTraps);
        for (var attempt = 0; attempt < wanted * 40 && Features.CountOf(FeatureNames.BoulderTraps) < wanted; attempt++)
        {
            var y = Spot(out var x);
            if (y < 0)
                continue;
            var ceiling = -1;
            for (var cy = y - 1; cy > y - 14 && cy > 2; cy--)
                if (grid.TileAt(x, cy) != TileId.Air)
                {
                    ceiling = cy;
                    break;
                }
            if (ceiling < 0 || ceiling > y - 3 || !IsNaturalGround(grid.TileAt(x, ceiling)) || !IsNaturalGround(grid.TileAt(x, ceiling - 1)))
                continue;
            grid.SetTile(new GridPoint(x, ceiling - 1), TileId.Boulder);
            var hold = grid.GetCell(x, ceiling);
            hold.HasActuator = true;
            hold.Shape = TileShape.Full;
            grid.SetCell(new GridPoint(x, ceiling), hold);
            Put(grid, x, y, TileId.PressurePlate);
            WireRun(new GridPoint(x, y), new GridPoint(x, ceiling));
            Features.AddCount(FeatureNames.BoulderTraps);
        }
        // Spike pits: a short trench floored with spikes.
        wanted = N(WorldGenDensity.SpikePits);
        for (var attempt = 0; attempt < wanted * 40 && Features.CountOf(FeatureNames.SpikePits) < wanted; attempt++)
        {
            var y = Spot(out var x);
            if (y < 0 || !FlatFloor(grid, catalog, x, y, 2))
                continue;
            var ok = true;
            for (var dx = -2; dx <= 2 && ok; dx++)
            for (var dy = 1; dy <= 3 && ok; dy++)
                ok = IsNaturalGround(grid.TileAt(x + dx, y + dy));
            if (!ok)
                continue;
            for (var dx = -2; dx <= 2; dx++)
            {
                grid.SetTile(new GridPoint(x + dx, y + 1), TileId.Air);
                Put(grid, x + dx, y + 2, TileId.SpikeStrip);
            }
            Features.AddCount(FeatureNames.SpikePits);
        }
    }

    // ---------------------------------------------------------------- surface and sunken chests

    private void SurfaceAndSunkenChests(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var wanted = N(WorldGenDensity.SurfaceCrates);
        var crates = 0;
        for (var attempt = 0; attempt < wanted * 60 && crates < wanted; attempt++)
        {
            // The first crates land near the landing site; the rest anywhere on open ground.
            var x = crates < 2 ? _width / 2 + random.Next(-_width / 12, _width / 12 + 1) : random.Next(_width / 20, _width - _width / 20);
            if (Math.Abs(x - _width / 2) < 12 || InBlightZone(x, 4) || InCoast(x))
                continue;
            var top = TopSolid(grid, catalog, x);
            if (!IsNaturalGround(grid.TileAt(x, top)) && grid.TileAt(x, top) is not (TileId.DesertSand or TileId.Sand) ||
                grid.GetCell(x, top).Shape != TileShape.Full || grid.TileAt(x, top - 1) != TileId.Air || grid.GetCell(x, top - 1).LiquidUnits > 0)
                continue;
            Put(grid, x, top - 1, TileId.SurfaceCrate);
            crates++;
        }
        Features.AddCount(FeatureNames.SurfaceCrates, crates);

        var sunken = 0;
        bool Sink(int x)
        {
            var top = TopSolid(grid, catalog, x);
            var above = grid.GetCell(x, top - 1);
            if (above.TileId != TileId.Air || above.LiquidUnits < WorldCell.LiquidUnitsMax / 2 || IsProtected(grid.TileAt(x, top)))
                return false;
            Put(grid, x, top - 1, TileId.SunkenChest);
            sunken++;
            return true;
        }
        foreach (var lake in Features.SitesOf("surface-lakes"))
            for (var attempt = 0; attempt < 6 && !Sink(random.Next(lake.Left + 1, Math.Max(lake.Left + 2, lake.Right))); attempt++)
            {
            }
        foreach (var (coastLeft, coastRight) in Features.AcidCoasts)
        {
            var perCoast = N(WorldGenDensity.CoastChests);
            for (var attempt = 0; attempt < perCoast * 20 && perCoast > 0; attempt++)
                if (Sink(random.Next(coastLeft + 2, Math.Max(coastLeft + 3, coastRight - 2))))
                    perCoast--;
        }
        Features.AddCount(FeatureNames.SunkenChests, sunken);
    }

    // ---------------------------------------------------------------- moss caves, speleothems and gems

    private void MossCaves(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        TileId[] mosses = [TileId.MossBlue, TileId.MossGold, TileId.MossRed, TileId.MossViolet];
        var wanted = N(WorldGenDensity.MossCaves);
        var strands = 0;
        for (var attempt = 0; attempt < wanted * 30 && Features.SitesOf(FeatureNames.MossCaves).Count < wanted; attempt++)
        {
            var cx = random.Next(8, _width - 8);
            var cy = RandomRow(random, _cavernRow, _moltenTop[cx] - 8);
            if (grid.TileAt(cx, cy) != TileId.Air || IsFrozen(cx, cy) || IsJunk(cx, cy))
                continue;
            var moss = mosses[random.Next(mosses.Length)];
            var radius = random.Next(9, 17);
            var converted = 0;
            for (var y = cy - radius; y <= cy + radius; y++)
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if (!grid.Contains(x, y) || y >= _height - 2 || (x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius)
                    continue;
                var tile = grid.TileAt(x, y);
                if (tile == TileId.Air)
                {
                    var cell = grid.GetCell(x, y);
                    if (cell.WallId is WallIds.Rock or WallIds.CaveRough or WallIds.CaveLayered or WallIds.NaturalBasalt)
                        grid.SetWall(new GridPoint(x, y), WallIds.MossyRock);
                    continue;
                }
                if (tile is not (TileId.Rock or TileId.Slate) ||
                    (grid.TileAt(x - 1, y) != TileId.Air && grid.TileAt(x + 1, y) != TileId.Air &&
                     grid.TileAt(x, y - 1) != TileId.Air && grid.TileAt(x, y + 1) != TileId.Air))
                    continue;
                grid.SetTile(new GridPoint(x, y), moss);
                converted++;
                // Long moss hangs from mossy ceilings.
                if (grid.TileAt(x, y + 1) == TileId.Air && grid.GetCell(x, y + 1).LiquidUnits == 0 && random.Next(3) == 0)
                {
                    var length = 1 + random.Next(4);
                    for (var dy = 1; dy <= length && grid.TileAt(x, y + dy) == TileId.Air && grid.GetCell(x, y + dy).LiquidUnits == 0; dy++)
                    {
                        Put(grid, x, y + dy, TileId.LongMoss);
                        strands++;
                    }
                }
            }
            if (converted < 6)
                continue;
            Features.AddSite(FeatureNames.MossCaves, cx - radius, cy - radius, cx + radius, cy + radius);
        }
        Features.AddCount(FeatureNames.LongMoss, strands);
    }

    private static TileId? SpeleothemFor(TileId host, bool hanging)
    {
        int? family = host switch
        {
            TileId.Rock or TileId.Slate or TileId.Soil or TileId.Mud or TileId.Clay or TileId.Alabaster or TileId.MyceliumStone or
                TileId.OvergrowthRock or TileId.CorruptRock or TileId.MossBlue or TileId.MossGold or TileId.MossRed or TileId.MossViolet => 0,
            TileId.Snow or TileId.Slush or TileId.RadiatedIce or TileId.OvergrowthIce => 1,
            TileId.ScrapSandstone or TileId.SaltCrust => 2,
            TileId.IrradiatedRock or TileId.CoreCrust or TileId.VitrifiedGlass => 3,
            TileId.HiveComb => 4,
            _ => null
        };
        return family is { } f ? (TileId)((int)TileId.StalactiteStone + f * 2 + (hanging ? 0 : 1)) : null;
    }

    private void Speleothems(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var placed = 0;
        bool TryAt(int x, int y)
        {
            if (!grid.Contains(x, y) || y < 2 || y >= _height - 2 || grid.TileAt(x, y) != TileId.Air || grid.GetCell(x, y).LiquidUnits > 0)
                return false;
            var aboveCell = grid.GetCell(x, y - 1);
            if (aboveCell.Shape == TileShape.Full && SpeleothemFor(aboveCell.TileId, hanging: true) is { } hanging &&
                grid.TileAt(x, y + 1) == TileId.Air)
            {
                Put(grid, x, y, hanging);
                placed++;
                return true;
            }
            var belowCell = grid.GetCell(x, y + 1);
            if (belowCell.Shape == TileShape.Full && SpeleothemFor(belowCell.TileId, hanging: false) is { } standing &&
                grid.TileAt(x, y - 1) == TileId.Air)
            {
                Put(grid, x, y, standing);
                placed++;
                return true;
            }
            return false;
        }
        for (var i = N(WorldGenDensity.Speleothems); i > 0; i--)
        {
            var x = random.Next(2, _width - 2);
            // Walk the column from a random row to the next cave edge.
            for (var y = RandomRow(random, _rockLine[x], _height - 3); y < _height - 3; y++)
            {
                if (grid.TileAt(x, y) != TileId.Air)
                    continue;
                TryAt(x, y);
                break;
            }
        }
        // Drips and mounds of comb inside every bloom hive.
        foreach (var (left, top, right, bottom) in Features.BloomHives)
            for (var i = 0; i < 24; i++)
                TryAt(random.Next(left, right + 1), random.Next(top, bottom + 1));
        Features.AddCount(FeatureNames.Speleothems, placed);

        // Crystal shrubs on deep cavern floors.
        var shrubs = 0;
        for (var attempt = 0; attempt < N(WorldGenDensity.CrystalShrubs) * 40 && shrubs < N(WorldGenDensity.CrystalShrubs); attempt++)
        {
            var x = random.Next(2, _width - 2);
            var y = FloorBelow(grid, catalog, random, x, _cavernRow + (_moltenTop[x] - _cavernRow) / 3, _moltenTop[x] - 4);
            if (y < 0)
                continue;
            Put(grid, x, y, TileId.CrystalShrub);
            shrubs++;
        }
        Features.AddCount(FeatureNames.CrystalShrubs, shrubs);
    }

    private void ExposedGems(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var placed = 0;
        var attempts = N(WorldGenDensity.ExposedGems) * 6;
        var wanted = N(WorldGenDensity.ExposedGems);
        for (var attempt = 0; attempt < attempts && placed < wanted; attempt++)
        {
            var y = RandomRow(random, _rockLine[0], _moltenTop[0] - 4);
            // Every other attempt searches the frozen biome so ice caves glitter too.
            var x = attempt % 2 == 0 && _frozenEdge[y] > 4 ? random.Next(1, _frozenEdge[y]) : random.Next(2, _width - 2);
            var tile = grid.TileAt(x, y);
            if (tile is not (TileId.Rock or TileId.Snow or TileId.Slate) || grid.GetCell(x, y).Shape != TileShape.Full)
                continue;
            if (grid.TileAt(x - 1, y) != TileId.Air && grid.TileAt(x + 1, y) != TileId.Air &&
                grid.TileAt(x, y - 1) != TileId.Air && grid.TileAt(x, y + 1) != TileId.Air)
                continue;
            grid.SetTile(new GridPoint(x, y), GemForDepth(random, x, y));
            placed++;
        }
        Features.AddCount(FeatureNames.ExposedGems, placed);
    }

    // ---------------------------------------------------------------- surface details

    private void SurfaceOre(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var placed = 0;
        for (var i = N(WorldGenDensity.SurfaceOrePatches); i > 0; i--)
        {
            var x = random.Next(_width / 20, _width - _width / 20);
            if (Math.Abs(x - _width / 2) < 20 || InBlightZone(x, 2) || InCoast(x))
                continue;
            var top = TopSolid(grid, catalog, x);
            var ore = random.Next(5) < 2 ? TileId.Rock : IsJunk(x, top) ? TileId.ScrapIronOre : TileId.CopperOre;
            var span = random.Next(2, 6);
            for (var px = x - span / 2; px <= x + span / 2; px++)
            {
                var ptop = TopSolid(grid, catalog, px);
                for (var y = ptop; y < ptop + 1 + random.Next(3); y++)
                    if (grid.TileAt(px, y) is TileId.Grass or TileId.Soil or TileId.Snow or TileId.DesertSand or TileId.JungleGrass &&
                        grid.TileAt(px, y - 1) is TileId.Air or TileId.Grass or TileId.Soil or TileId.Snow or TileId.DesertSand or TileId.Rock or
                            TileId.CopperOre or TileId.ScrapIronOre)
                        grid.SetTile(new GridPoint(px, y), ore);
            }
            placed++;
        }
        Features.AddCount(FeatureNames.SurfaceOre, placed);
    }

    private void FallenLogs(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var placed = 0;
        for (var i = N(WorldGenDensity.FallenLogs) * 3; i > 0 && placed < N(WorldGenDensity.FallenLogs); i--)
        {
            var x = random.Next(3, _width - 3);
            var top = TopSolid(grid, catalog, x);
            if (grid.TileAt(x, top) is not (TileId.Grass or TileId.JungleGrass or TileId.CanopyGrass or TileId.Snow) ||
                grid.GetCell(x, top).Shape != TileShape.Full || grid.TileAt(x, top - 1) != TileId.Air || grid.GetCell(x, top - 1).LiquidUnits > 0 ||
                Math.Abs(x - _width / 2) < 10)
                continue;
            Put(grid, x, top - 1, TileId.FallenLog);
            placed++;
        }
        Features.AddCount(FeatureNames.FallenLogs, placed);
    }

    /// <summary>Lilypads on still water, reeds in the shallows, seaweed on deep beds, coral fans on the ocean floor and in acid pools.</summary>
    private void WaterLife(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var cells = grid.RawCells;
        int plants = 0, fans = 0;
        for (var x = 1; x < _width - 1; x++)
        {
            if ((x & 255) == 0)
                token.ThrowIfCancellationRequested();
            var run = 0;
            var bottomRow = Math.Min(_height - 2, _moltenTop[x] - 2);
            for (var y = 1; y < bottomRow; y++)
            {
                var cell = cells[y * _width + x];
                var wet = cell.TileId == TileId.Air && cell.LiquidUnits > WorldCell.LiquidUnitsMax / 2 &&
                          cell.LiquidKind is LiquidKind.Water or LiquidKind.Acid or LiquidKind.Fuel;
                if (!wet)
                {
                    run = 0;
                    continue;
                }
                var kind = cell.LiquidKind;
                if (run == 0 && kind == LiquidKind.Water && cells[(y - 1) * _width + x].TileId == TileId.Air &&
                    cells[(y - 1) * _width + x].LiquidUnits == 0 && !IsFrozen(x, y) && random.Next(8) == 0)
                {
                    Put(grid, x, y - 1, TileId.Lilypad);
                    plants++;
                }
                run++;
                var below = cells[(y + 1) * _width + x];
                if (!catalog[below.TileId].IsSolid || below.Shape != TileShape.Full)
                    continue;
                // On the bed.
                if ((kind == LiquidKind.Acid || InCoast(x) && run >= 3) && random.Next(5) == 0)
                {
                    Put(grid, x, y, TileId.CoralFan);
                    fans++;
                }
                else if (run >= 4 && random.Next(6) == 0)
                {
                    var height = 1 + random.Next(Math.Min(4, run - 2));
                    for (var h = 0; h < height; h++)
                        Put(grid, x, y - h, TileId.Seaweed);
                    plants++;
                }
                else if (run <= 2 && kind == LiquidKind.Water && random.Next(4) == 0)
                {
                    Put(grid, x, y, TileId.Reed);
                    plants++;
                }
                run = 0;
            }
        }
        Features.AddCount(FeatureNames.WaterPlants, plants);
        Features.AddCount(FeatureNames.CoralFans, fans);
    }

    private void SurfacePlants(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        // Column tops are read many times here; props never change them, so remember each one.
        var tops = new int[_width];
        Array.Fill(tops, -1);
        int Top(int x) => tops[x] >= 0 ? tops[x] : tops[x] = TopSolid(grid, catalog, x);
        bool OpenOn(int x, int y, params TileId[] soils) =>
            grid.Contains(x, y + 1) && Array.IndexOf(soils, grid.TileAt(x, y + 1)) >= 0 && grid.GetCell(x, y + 1).Shape == TileShape.Full &&
            grid.TileAt(x, y) == TileId.Air && grid.GetCell(x, y).LiquidUnits == 0;

        // Solar lilies in sunny grassland patches.
        var lilies = 0;
        var patches = N(WorldGenDensity.SolarLilyPatches);
        for (var attempt = 0; attempt < patches * 30 && patches > 0; attempt++)
        {
            var x = random.Next(_width / 10, _width - _width / 10);
            if (InBlightZone(x, 4) || Math.Abs(x - _width / 2) < 8 || !OpenOn(x, Top(x) - 1, TileId.Grass, TileId.JungleGrass))
                continue;
            for (var dx = -4; dx <= 4; dx++)
            {
                var px = x + dx;
                var top = Top(px);
                if ((dx == 0 || random.Next(2) == 0) && OpenOn(px, top - 1, TileId.Grass, TileId.JungleGrass))
                {
                    Put(grid, px, top - 1, TileId.SolarLily);
                    lilies++;
                }
            }
            patches--;
        }
        Features.AddCount(FeatureNames.SolarLilies, lilies);

        // Dye blossoms: rare, anywhere their soil is open.
        var dyes = 0;
        for (var attempt = 0; attempt < N(WorldGenDensity.DyeBlossoms) * 30 && dyes < N(WorldGenDensity.DyeBlossoms); attempt++)
        {
            var x = random.Next(2, _width - 2);
            var y = random.Next(3) == 0 ? Top(x) - 1 : FloorBelow(grid, catalog, random, x, _surface[x], _moltenTop[x] - 2);
            if (y < 1 || grid.TileAt(x, y) != TileId.Air)
                continue;
            var soil = grid.TileAt(x, y + 1);
            TileId? dye = soil switch
            {
                TileId.Grass or TileId.JungleGrass => TileId.DyeBlossomGold,
                TileId.CanopyGrass or TileId.Mud or TileId.GlowshroomGrass => TileId.DyeBlossomIndigo,
                TileId.Snow or TileId.Rock or TileId.DesertSand => TileId.DyeBlossomCrimson,
                _ => null
            };
            if (dye is not { } blossom || !OpenOn(x, y, soil))
                continue;
            Put(grid, x, y, blossom);
            dyes++;
        }
        Features.AddCount(FeatureNames.DyeBlossoms, dyes);

        // Wild gourds on the grassland and mushrooms on soil, mud and glowshroom floors.
        var gourds = 0;
        for (var i = N(WorldGenDensity.WildGourds) * 30; i > 0 && gourds < N(WorldGenDensity.WildGourds); i--)
        {
            var x = random.Next(3, _width - 3);
            var top = Top(x);
            if (!OpenOn(x, top - 1, TileId.Grass, TileId.JungleGrass))
                continue;
            Put(grid, x, top - 1, TileId.WildGourd);
            gourds++;
        }
        Features.AddCount(FeatureNames.WildGourds, gourds);
        var mushrooms = 0;
        for (var attempt = 0; attempt < N(WorldGenDensity.WildMushrooms) * 20 && mushrooms < N(WorldGenDensity.WildMushrooms); attempt++)
        {
            var x = random.Next(2, _width - 2);
            // Mostly a cave find; the few on the surface keep their distance so the open grassland is not carpeted.
            var onSurface = random.Next(10) == 0;
            var y = onSurface ? Top(x) - 1 : FloorBelow(grid, catalog, random, x, _surface[x], _moltenTop[x] - 2);
            if (y < 1 || !OpenOn(x, y, TileId.Grass, TileId.Soil, TileId.Mud, TileId.GlowshroomGrass, TileId.FungalSoil, TileId.CanopyGrass))
                continue;
            if (onSurface && Enumerable.Range(x - 6, 13).Any(cx => cx > 1 && cx < _width - 1 && grid.TileAt(cx, Top(cx) - 1) == TileId.WildMushroom))
                continue;
            Put(grid, x, y, TileId.WildMushroom);
            mushrooms++;
        }
        Features.AddCount(FeatureNames.WildMushrooms, mushrooms);
    }

    // ---------------------------------------------------------------- piles, graves and pods

    private void Piles(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var placed = 0;
        var wanted = N(WorldGenDensity.RubblePiles);
        for (var attempt = 0; attempt < wanted * 20 && placed < wanted; attempt++)
        {
            var x = random.Next(2, _width - 2);
            var y = FloorBelow(grid, catalog, random, x, _surface[x] + 4, _height - 4);
            if (y < 0)
                continue;
            Put(grid, x, y, IsJunk(x, y) && random.Next(2) == 0 || random.Next(4) == 0 ? TileId.LootPile : TileId.RubblePile);
            placed++;
        }
        // Halls of white stone are strewn with rubble.
        foreach (var (left, top, right, bottom) in Features.AlabasterHalls)
            for (var i = 0; i < 10; i++)
            {
                var x = random.Next(left, right + 1);
                for (var y = top; y <= bottom + 2; y++)
                    if (IsCaveFloor(grid, catalog, x, y))
                    {
                        Put(grid, x, y, TileId.RubblePile);
                        placed++;
                        break;
                    }
            }
        Features.AddCount(FeatureNames.RubblePiles, placed);

        var shells = 0;
        foreach (var (coastLeft, coastRight) in Features.AcidCoasts)
            for (var i = N(WorldGenDensity.ShellPilesPerCoast); i > 0; i--)
            {
                var x = random.Next(coastLeft + 1, Math.Max(coastLeft + 2, coastRight));
                var top = TopSolid(grid, catalog, x);
                if (grid.TileAt(x, top) is TileId.ShellBeach or TileId.SaltCrust or TileId.AcidSand or TileId.ShellSand &&
                    grid.GetCell(x, top).Shape == TileShape.Full && grid.TileAt(x, top - 1) == TileId.Air)
                {
                    Put(grid, x, top - 1, TileId.ShellPile);
                    shells++;
                }
            }
        Features.AddCount(FeatureNames.ShellPiles, shells);
    }

    private void Graveyards(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var wanted = N(WorldGenDensity.Graveyards);
        for (var attempt = 0; attempt < wanted * 250 && Features.SitesOf(FeatureNames.Graveyards).Count < wanted; attempt++)
        {
            var x = random.Next(_width / 10, _width - _width / 10);
            if (NearSpawn(x, 0.05) || InBlightZone(x, 6) || InCoast(x))
                continue;
            var ground = TopSolid(grid, catalog, x);
            var flat = true;
            for (var dx = 0; dx <= 12 && flat; dx += 2)
                flat = Math.Abs(TopSolid(grid, catalog, x + dx) - ground) <= 2;
            if (!flat || grid.TileAt(x, ground) is not (TileId.Grass or TileId.Soil or TileId.Snow or TileId.JungleGrass))
                continue;
            var graves = 0;
            for (var dx = 0; dx <= 12; dx += 2 + random.Next(2))
            {
                var top = TopSolid(grid, catalog, x + dx);
                if (grid.TileAt(x + dx, top - 1) == TileId.Air && grid.GetCell(x + dx, top - 1).LiquidUnits == 0)
                {
                    Put(grid, x + dx, top - 1, TileId.GraveMarker);
                    graves++;
                }
            }
            if (graves >= 3)
                Features.AddSite(FeatureNames.Graveyards, x, ground - 2, x + 14, ground);
        }
    }

    private void BroodPods(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var pods = 0;
        foreach (var (left, top, right, bottom) in Features.BloomHives)
        {
            var here = 0;
            for (var attempt = 0; attempt < 60 && here < 3; attempt++)
            {
                var x = random.Next(left, right + 1);
                var y = random.Next(top, bottom + 1);
                if (grid.TileAt(x, y) != TileId.Air || grid.GetCell(x, y).LiquidUnits > 0 || grid.TileAt(x, y + 1) != TileId.HiveComb)
                    continue;
                Put(grid, x, y, TileId.BroodPod);
                here++;
            }
            if (here == 0)
            {
                // Beside the hive heart on its comb pedestal.
                var cx = (left + right) / 2;
                var cy = (top + bottom) / 2;
                Put(grid, cx - 1, cy - 1, TileId.BroodPod);
                here++;
            }
            pods += here;
        }
        Features.AddCount(FeatureNames.BroodPods, pods);
    }

    /// <summary>
    /// Catalogues every chest with its depth tier; the simulation tops up any chest whose rolled loot lacks a useful
    /// item for its depth when it is first opened.
    /// </summary>
    private void ImproveChests(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var maxId = Enum.GetValues<TileId>().Max(id => (int)id);
        var container = new bool[maxId + 1];
        foreach (var id in Enum.GetValues<TileId>())
            container[(int)id] = ContentTiles.Has(id, TileTraits.Container);
        var cells = grid.RawCells;
        for (var index = 0; index < cells.Length; index++)
        {
            var id = (int)cells[index].TileId;
            if (id == 0 || id > maxId || !container[id])
                continue;
            var x = index % _width;
            var y = index / _width;
            Features.Chests.Add((new GridPoint(x, y), Tier(x, y)));
        }
        Features.AddCount(FeatureNames.ImprovedChests, Features.Chests.Count);
    }
}
