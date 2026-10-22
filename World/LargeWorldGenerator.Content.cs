namespace Rimtera.World;

/// <summary>
/// Content-expansion generation passes (CONTENT_EXPANSION.md §2.3): soft deposits, the oceans at both
/// edges, Fungal Hollows, the Verdant Canopy over its Bloom Hives, the Precursor Vault, mini-biomes
/// (geode chambers, basalt columns, alabaster halls, mite warrens, mosses), bound survivors and frozen
/// ponds. Placement is expressed as fractions of the world size and every count comes from
/// <see cref="WorldGenDensity"/> (area or width scaled), so every pass stays linear in area.
/// </summary>
public sealed partial class LargeWorldGenerator
{
    private readonly List<(int X, int Y)> _hutFloors = new();

    private static ushort? ContentWallFor(TileId tile) => tile switch
    {
        TileId.AcidSand or TileId.ShellSand or TileId.ShellBeach or TileId.SaltCrust or TileId.CoralReef => WallIds.NaturalCoral,
        TileId.Mud or TileId.CanopyGrass or TileId.Clay => WallIds.NaturalMud,
        TileId.HiveComb or TileId.Nectar => WallIds.NaturalHive,
        TileId.GlowshroomGrass or TileId.MyceliumStone or TileId.SporeBlock or TileId.MushroomCap => WallIds.Glowshroom,
        TileId.BasaltColumn or TileId.Slate => WallIds.NaturalBasalt,
        TileId.Alabaster => WallIds.NaturalAlabaster,
        TileId.PrecursorBrick or TileId.PrecursorGlyph => WallIds.Precursor,
        TileId.GeodeCyan or TileId.GeodeMagenta => WallIds.Crystal,
        TileId.PearlStone or TileId.PearlSand => WallIds.NaturalPearl,
        TileId.ScrapSandstone => WallIds.Sand,
        TileId.Slush => WallIds.Ice,
        TileId.MudBrick => WallIds.MudBuilt,
        _ => null
    };

    private bool InCoast(int x) => Features.AcidCoasts.Any(coast => x >= coast.Left && x <= coast.Right);

    private bool Overlaps(IEnumerable<(int Left, int Top, int Right, int Bottom)> boxes, int left, int top, int right, int bottom, int margin) =>
        boxes.Any(box => left < box.Right + margin && right > box.Left - margin && top < box.Bottom + margin && bottom > box.Top - margin);

    // ---------------------------------------------------------------- soft deposits

    private void ClayGravelSlate(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        for (var i = N(WorldGenDensity.ClayGravelSlate); i > 0; i--)
        {
            token.ThrowIfCancellationRequested();
            var x = random.Next(_width);
            var kind = random.Next(3);
            var (top, bottom) = kind switch
            {
                0 => (_surface[x] + 2, _rockLine[x] + 2),
                1 => (_surface[x] + 3, _cavernRow),
                _ => (_cavernRow, _moltenTop[x] - 6)
            };
            if (bottom <= top + 1)
                continue;
            var y = RandomRow(random, top, bottom);
            var paint = kind switch
            {
                0 => (Func<TileId, TileId?>)(tile => tile == TileId.Soil ? (random.Next(3) == 0 ? TileId.Silt : TileId.Clay) : null),
                1 => tile => tile is TileId.Soil or TileId.Rock ? TileId.Gravel : null,
                _ => tile => tile == TileId.Rock ? TileId.Slate : null
            };
            Blob(grid, random, x, y, 1.5 + random.NextDouble() * 2.5, random.Next(4, 11), random.NextDouble() - 0.5,
                random.NextDouble() - 0.5, paint);
        }
    }

    // ---------------------------------------------------------------- oceans

    /// <summary>
    /// The world is capped at both edges by a sea of fuel (the feature list keeps its old AcidCoasts name). Inland, the
    /// coastal shelf has already eased the land down to the shore; here a shell beach runs down to the shoreline, then a
    /// sloped sea floor of shell sand, sand and coral reef mounds deepens toward the world edge. Width and depth scale
    /// with world width (Medium ≈ 350 columns, ~55 deep).
    /// </summary>
    private void AcidCoasts(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var oceanWidth = OceanWidth;
        var seaDepth = Math.Clamp(oceanWidth / 6, 14, Math.Max(14, (int)(_height * 0.06)));
        var beachLength = Math.Max(6, (int)(oceanWidth * 0.16));
        var floorNoise = Noise(random, _width, 9);
        foreach (var leftSide in new[] { true, false })
        {
            token.ThrowIfCancellationRequested();
            var inner = leftSide ? oceanWidth : _width - 1 - oceanWidth;
            var landY = _surface[inner];
            var seaLevel = landY + Math.Max(3, oceanWidth / 45);
            for (var i = 0; i <= oceanWidth; i++)
            {
                var x = leftSide ? oceanWidth - i : _width - 1 - oceanWidth + i;
                int ground;
                if (i < beachLength)
                {
                    // The beach: eases from the land height down to just under the shoreline.
                    var t = i / (double)beachLength;
                    var eased = t * t * (3 - 2 * t);
                    ground = (int)Math.Round(landY + (seaLevel + 1 - landY) * eased);
                }
                else
                {
                    // The sea floor: drops quickly past the shore, then flattens toward the abyss at the edge.
                    var u = (i - beachLength) / (double)Math.Max(1, oceanWidth - beachLength);
                    var shelf = 1 - (1 - u) * (1 - u);
                    ground = seaLevel + 1 + (int)Math.Round(shelf * seaDepth + floorNoise[x] * 1.5 * u);
                }
                ground = Math.Clamp(ground, 4, _moltenTop[x] - 8);
                var underwater = ground > seaLevel;
                for (var y = 0; y < ground; y++)
                {
                    var tile = grid.TileAt(x, y);
                    if (tile != TileId.Air && !IsProtected(tile))
                        grid.SetTile(new GridPoint(x, y), TileId.Air);
                }
                // Sand bed: a few rows thick, and filling any old valley or cave mouth under the new ground.
                var sandDepth = 4 + random.Next(3);
                for (var y = ground; y < Math.Min(_height - 2, ground + 40); y++)
                {
                    var tile = grid.TileAt(x, y);
                    if (y >= ground + sandDepth && tile != TileId.Air)
                        break;
                    if (IsProtected(tile))
                        continue;
                    var depth = y - ground;
                    var bed = underwater
                        ? (depth == 0 ? (random.Next(5) == 0 ? TileId.Sand : TileId.ShellSand) : depth < 3 ? TileId.ShellSand : TileId.Sand)
                        : (depth < 2 ? (depth == 0 && random.Next(9) == 0 ? TileId.SaltCrust : TileId.ShellBeach) : TileId.Sand);
                    grid.SetTile(new GridPoint(x, y), bed);
                }
                for (var y = seaLevel; y < ground; y++)
                    grid.TryAddLiquid(new GridPoint(x, y), LiquidKind.Fuel, 1f, catalog);
                _surface[x] = Math.Min(ground, seaLevel);
                if (underwater && ground - seaLevel > 6 && random.Next(9) == 0)
                {
                    // Coral reef mounds on the sea floor.
                    var height = random.Next(1, 4);
                    for (var h = 1; h <= height && ground - h > seaLevel + 2; h++)
                        grid.SetTile(new GridPoint(x, ground - h), TileId.CoralReef);
                }
            }
            Features.AcidCoasts.Add(leftSide ? (0, oceanWidth) : (_width - 1 - oceanWidth, _width - 1));
            Features.AddSite(FeatureNames.Oceans, leftSide ? 0 : _width - 1 - oceanWidth, seaLevel,
                leftSide ? oceanWidth : _width - 1, seaLevel + seaDepth + 1);

            // A coastal hab pod and a beacon tower on the dry beach.
            var podX = leftSide ? oceanWidth - 3 : _width - 1 - oceanWidth + 3;
            var podY = TopSolid(grid, catalog, podX) - 1;
            if (grid.Contains(podX, podY) && grid.TileAt(podX, podY) == TileId.Air && grid.GetCell(podX, podY).LiquidDepth <= 0f)
                grid.SetTile(new GridPoint(podX, podY), TileId.CoastalHabPod);
            var towerX = podX + (leftSide ? -3 : 3);
            var towerY = TopSolid(grid, catalog, towerX) - 1;
            if (grid.Contains(towerX, towerY) && grid.TileAt(towerX, towerY) == TileId.Air && grid.GetCell(towerX, towerY).LiquidDepth <= 0f)
                grid.SetTile(new GridPoint(towerX, towerY), TileId.BeaconTower);
        }
    }

    // ---------------------------------------------------------------- fungal hollows

    private void FungalHollows(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var wanted = N(WorldGenDensity.FungalHollows);
        var rxBase = Math.Max(9, _height / 40);
        for (var attempt = 0; attempt < wanted * 30 && Features.FungalHollows.Count < wanted; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var cx = random.Next(_width / 10, _width - _width / 10);
            var rx = rxBase + random.Next(0, rxBase / 2 + 1);
            var ry = Math.Max(5, rx / 2);
            if (_moltenTop[cx] - 8 - ry <= _cavernRow + ry)
                continue;
            var cy = random.Next(_cavernRow + ry, _moltenTop[cx] - 8 - ry);
            if (IsFrozen(cx, cy) || IsJunk(cx, cy) || NearSpawn(cx, 0.05) || InCoast(cx) ||
                Overlaps(Features.FungalHollows, cx - rx, cy - ry, cx + rx, cy + ry, 8))
                continue;
            for (var y = cy - ry - 2; y <= cy + ry + 2; y++)
            for (var x = cx - rx - 2; x <= cx + rx + 2; x++)
            {
                if (!grid.Contains(x, y) || y >= _height - 1 || IsProtected(grid.TileAt(x, y)))
                    continue;
                var dx = (x - cx) / (double)rx;
                var dy = (y - cy) / (double)ry;
                var d = dx * dx + dy * dy;
                if (d <= 1.0)
                    grid.SetTile(new GridPoint(x, y), TileId.Air);
                else if (d <= 1.7 && grid.TileAt(x, y) != TileId.Air)
                    grid.SetTile(new GridPoint(x, y), random.Next(5) == 0 ? TileId.SporeBlock : TileId.MyceliumStone);
            }
            // Glowshroom floors and giant mushrooms (spore-block stems with caps).
            for (var x = cx - rx; x <= cx + rx; x++)
            {
                var floor = -1;
                for (var y = cy; y <= cy + ry + 3 && y < _height - 1; y++)
                    if (grid.TileAt(x, y) != TileId.Air) { floor = y; break; }
                if (floor < 0 || IsProtected(grid.TileAt(x, floor)))
                    continue;
                grid.SetTile(new GridPoint(x, floor), TileId.GlowshroomGrass);
                if (random.Next(9) == 0 && x > cx - rx + 2 && x < cx + rx - 2)
                {
                    var stem = random.Next(3, Math.Max(4, ry));
                    for (var h = 1; h <= stem && grid.TileAt(x, floor - h) == TileId.Air; h++)
                        grid.SetTile(new GridPoint(x, floor - h), h == stem ? TileId.MushroomCap : TileId.SporeBlock);
                    for (var side = -1; side <= 1; side += 2)
                        if (grid.TileAt(x + side, floor - stem) == TileId.Air)
                            grid.SetTile(new GridPoint(x + side, floor - stem), TileId.MushroomCap);
                }
            }
            var ruinX = cx + random.Next(-rx / 2, rx / 2 + 1);
            for (var y = cy; y <= cy + ry + 2; y++)
            {
                if (grid.TileAt(ruinX, y + 1) != TileId.Air && grid.TileAt(ruinX, y) == TileId.Air)
                {
                    grid.SetTile(new GridPoint(ruinX, y), TileId.FungalHabRuin);
                    _hutFloors.Add((ruinX + 1, y));
                    break;
                }
            }
            Features.FungalHollows.Add((cx - rx, cy - ry, cx + rx, cy + ry));
        }
    }

    // ---------------------------------------------------------------- canopy, hives and vault

    private void VerdantCanopy(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var wanted = N(WorldGenDensity.Canopies);
        var span = Math.Max(14, (int)(_width * 0.06));
        for (var attempt = 0; attempt < wanted * 40 && Features.Canopies.Count < wanted; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var left = random.Next(_width / 10, Math.Max(_width / 10 + 1, _width - _width / 10 - span));
            var right = left + span;
            var middle = (left + right) / 2;
            if (NearSpawn(middle, 0.12) || InBlightZone(left, 8) || InBlightZone(right, 8) || InCoast(left) || InCoast(right) ||
                IsFrozen(left, _surface[left]) || IsJunk(right, _surface[right]) ||
                Features.Canopies.Any(canopy => left < canopy.Right + span && right > canopy.Left - span) ||
                (Features.Derelict is { } ship && left < ship.Right + 6 && right > ship.Left - 6))
                continue;
            for (var x = left; x <= right; x++)
            {
                var top = TopSolid(grid, catalog, x);
                for (var y = top; y < Math.Min(_height - 1, _rockLine[x] + 4); y++)
                {
                    var tile = grid.TileAt(x, y);
                    if (y == top && tile is TileId.Grass or TileId.JungleGrass or TileId.Soil)
                        grid.SetTile(new GridPoint(x, y), TileId.CanopyGrass);
                    else if (tile is TileId.Soil or TileId.Grass or TileId.JungleGrass)
                        grid.SetTile(new GridPoint(x, y), TileId.Mud);
                }
            }
            // Mud reaches into the rock below the canopy.
            for (var i = Math.Max(3, span / 4); i > 0; i--)
            {
                var x = random.Next(left, right + 1);
                Blob(grid, random, x, RandomRow(random, _rockLine[x], _cavernRow + 8), 2 + random.NextDouble() * 2, random.Next(5, 12),
                    random.NextDouble() - 0.5, random.NextDouble() - 0.5, tile => tile is TileId.Rock or TileId.Soil ? TileId.Mud : null);
            }
            Features.Canopies.Add((left, right));
            BloomHive(grid, catalog, random, left, right);
            // More hives in a deep, wide canopy column (one per few million cells of it).
            var column = (right - left) * (double)Math.Max(1, _moltenTop[middle] - _rockLine[middle]);
            var extra = (int)Math.Round(WorldGenDensity.HivesPerCanopyArea.Rate * column);
            for (var hive = 0; hive < extra * 4 && extra > 0; hive++)
            {
                var before = Features.BloomHives.Count;
                BloomHive(grid, catalog, random, left, right, spread: true);
                if (Features.BloomHives.Count > before && --extra == 0)
                    break;
            }
        }
    }

    private void BloomHive(TileGrid grid, TileCatalog catalog, Random random, int left, int right, bool spread = false)
    {
        // Hive size grows with the canopy but is capped: a big canopy gets more hives, not one enormous one.
        var rx = Math.Clamp((right - left) / 3, 8, 26);
        var cx = spread
            ? left + random.Next(0, Math.Max(1, right - left))
            : (left + right) / 2 + random.Next(-(right - left) / 6, (right - left) / 6 + 1);
        var ry = Math.Max(5, rx * 3 / 5);
        var top = Math.Max(_rockLine[cx] + ry + 2, _cavernRow - ry);
        var bottom = _moltenTop[cx] - ry - 10;
        if (bottom <= top)
            return;
        var cy = random.Next(top, bottom);
        if (Overlaps(Features.BloomHives, cx - rx, cy - ry, cx + rx, cy + ry, 6) ||
            (Features.Vault is { } vaultBox && Overlaps([vaultBox], cx - rx, cy - ry, cx + rx, cy + ry, 6)))
            return;
        for (var y = cy - ry - 2; y <= cy + ry + 2; y++)
        for (var x = cx - rx - 2; x <= cx + rx + 2; x++)
        {
            if (!grid.Contains(x, y) || y >= _height - 1 || IsProtected(grid.TileAt(x, y)))
                continue;
            var dx = (x - cx) / (double)rx;
            var dy = (y - cy) / (double)ry;
            var d = dx * dx + dy * dy;
            if (d <= 1.0)
                grid.SetTile(new GridPoint(x, y), dy > 0.55 && random.Next(4) == 0 ? TileId.Nectar : TileId.Air);
            else if (d <= 1.8)
                grid.SetTile(new GridPoint(x, y), TileId.HiveComb);
            if (d <= 1.8)
                grid.SetWall(new GridPoint(x, y), WallIds.NaturalHive);
        }
        // Nectar pools in the bottom of the chamber.
        for (var x = cx - rx + 1; x < cx + rx; x++)
            for (var y = cy + ry / 3; y <= cy + ry; y++)
                if (grid.TileAt(x, y) == TileId.Air)
                    grid.TryAddLiquid(new GridPoint(x, y), LiquidKind.Nectar, 1f, catalog);
        // The hive heart sits on a comb pedestal.
        for (var dx = -1; dx <= 1; dx++)
            grid.SetTile(new GridPoint(cx + dx, cy), TileId.HiveComb);
        grid.SetTile(new GridPoint(cx, cy - 1), TileId.BloomHive);
        Features.BloomHives.Add((cx - rx, cy - ry, cx + rx, cy + ry));
    }

    private void PrecursorVault(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        if (Features.Canopies.Count == 0)
            return;
        var canopy = Features.Canopies[0];
        var vaultWidth = Math.Clamp(_width / 50, 24, 48);
        var vaultHeight = Math.Clamp(vaultWidth / 2, 12, 20);
        var left = Math.Clamp((canopy.Left + canopy.Right) / 2 - vaultWidth / 2, 2, _width - vaultWidth - 3);
        var bottomLimit = _moltenTop.Skip(left).Take(vaultWidth).Min() - 6;
        var top = Math.Max(_cavernRow + 4, bottomLimit - vaultHeight - 4);
        if (top + vaultHeight >= bottomLimit)
            return;
        var right = left + vaultWidth;
        var bottom = top + vaultHeight;
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            var shell = y <= top + 1 || y >= bottom - 1 || x <= left + 1 || x >= right - 1;
            var divider = x == left + vaultWidth / 2 && y > top + 1 && y < bottom - 4;
            TileId tile = shell || divider
                ? (random.Next(6) == 0 ? TileId.PrecursorGlyph : TileId.PrecursorBrick)
                : TileId.Air;
            grid.SetTile(new GridPoint(x, y), tile);
            grid.SetWall(new GridPoint(x, y), WallIds.Precursor);
        }
        // A three-tall sealed door in the left shell, with an approach tunnel carved to it.
        var doorBottom = bottom - 2;
        for (var dy = 0; dy < 3; dy++)
        {
            grid.SetTile(new GridPoint(left, doorBottom - dy), TileId.PrecursorVaultDoor);
            grid.SetTile(new GridPoint(left + 1, doorBottom - dy), TileId.Air);
        }
        for (var x = left - 1; x >= Math.Max(1, left - 10); x--)
            for (var dy = 0; dy < 3; dy++)
                if (!IsProtected(grid.TileAt(x, doorBottom - dy)))
                    grid.SetTile(new GridPoint(x, doorBottom - dy), TileId.Air);
        var floor = bottom - 2;
        grid.SetTile(new GridPoint(left + 4, floor), TileId.PrecursorStatue);
        grid.SetTile(new GridPoint(left + 6, floor), TileId.EnergyPylon);
        grid.SetTile(new GridPoint(left + vaultWidth / 2 - 3, floor), TileId.GlyphTablet);
        grid.SetTile(new GridPoint(left + vaultWidth / 2 + 3, floor), TileId.LockedVaultChest);
        grid.SetTile(new GridPoint(right - 4, floor), TileId.LockedVaultChest);
        grid.SetTile(new GridPoint(right - 6, floor), TileId.EnergyPylon);
        _hutFloors.Add((right - 8, floor));
        Features.Vault = (left, top, right, bottom);
    }

    // ---------------------------------------------------------------- mini-biomes

    private void MiniBiomes(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        // Geode chambers: crystal-shelled pockets in the rock layer.
        var geodes = N(WorldGenDensity.GeodeChambers);
        for (var attempt = 0; attempt < geodes * 10 && Features.GeodeChambers.Count < geodes; attempt++)
        {
            var x = random.Next(6, _width - 6);
            var y = RandomRow(random, _rockLine[x] + 4, _moltenTop[x] - 8);
            var r = 3 + random.Next(0, 3);
            if (InCoast(x) || Overlaps(Features.GeodeChambers, x - r, y - r, x + r, y + r, 6))
                continue;
            var colour = random.Next(2) == 0 ? TileId.GeodeCyan : TileId.GeodeMagenta;
            for (var dy = -r - 1; dy <= r + 1; dy++)
            for (var dx = -r - 1; dx <= r + 1; dx++)
            {
                var px = x + dx;
                var py = y + dy;
                if (!grid.Contains(px, py) || py >= _height - 1 || IsProtected(grid.TileAt(px, py)))
                    continue;
                var d = dx * dx + dy * dy;
                if (d <= (r - 1) * (r - 1))
                    grid.SetTile(new GridPoint(px, py), TileId.Air);
                else if (d <= (r + 1) * (r + 1))
                    grid.SetTile(new GridPoint(px, py), colour);
                if (d <= (r + 1) * (r + 1))
                    grid.SetWall(new GridPoint(px, py), WallIds.Crystal);
            }
            if (grid.TileAt(x, y + r - 2) == TileId.Air)
                grid.SetTile(new GridPoint(x, y + r - 2), TileId.GeodeCluster);
            Features.GeodeChambers.Add((x - r, y - r, x + r, y + r));
        }

        // Basalt columns and alabaster halls: wide cavern chambers with their own stone.
        void Chamber(List<(int, int, int, int)> list, int count, TileId lining, TileId? pillar, ushort wall)
        {
            for (var attempt = 0; attempt < count * 12 && list.Count < count; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var x = random.Next(_width / 12, _width - _width / 12);
                var rx = 10 + random.Next(0, 6);
                var ry = 4 + random.Next(0, 3);
                if (_moltenTop[x] - 6 - ry <= _cavernRow + ry)
                    continue;
                var y = random.Next(_cavernRow + ry, _moltenTop[x] - 6 - ry);
                if (InCoast(x) || Overlaps(list, x - rx, y - ry, x + rx, y + ry, 8) ||
                    Overlaps(Features.FungalHollows, x - rx, y - ry, x + rx, y + ry, 4))
                    continue;
                for (var py = y - ry - 2; py <= y + ry + 2; py++)
                for (var px = x - rx - 2; px <= x + rx + 2; px++)
                {
                    if (!grid.Contains(px, py) || py >= _height - 1 || IsProtected(grid.TileAt(px, py)))
                        continue;
                    var dx = (px - x) / (double)rx;
                    var dy = (py - y) / (double)ry;
                    var d = dx * dx + dy * dy;
                    if (d <= 1.0)
                        grid.SetTile(new GridPoint(px, py), pillar is { } column && px % 3 == 0 && random.Next(3) > 0 ? column : TileId.Air);
                    else if (d <= 1.8)
                        grid.SetTile(new GridPoint(px, py), lining);
                    if (d <= 1.8)
                        grid.SetWall(new GridPoint(px, py), wall);
                }
                list.Add((x - rx, y - ry, x + rx, y + ry));
            }
        }
        Chamber(Features.BasaltColumns, N(WorldGenDensity.BasaltColumns), TileId.Slate, TileId.BasaltColumn, WallIds.NaturalBasalt);
        Chamber(Features.AlabasterHalls, N(WorldGenDensity.AlabasterHalls), TileId.Alabaster, null, WallIds.NaturalAlabaster);

        // Mite warrens: caves strung with silk.
        var warrens = N(WorldGenDensity.MiteWarrens);
        for (var attempt = 0; attempt < warrens * 12 && Features.MiteWarrens.Count < warrens; attempt++)
        {
            var x = random.Next(_width / 12, _width - _width / 12);
            var rx = 7 + random.Next(0, 4);
            var ry = 4 + random.Next(0, 2);
            var y = RandomRow(random, _rockLine[x] + ry, _moltenTop[x] - 8 - ry);
            if (InCoast(x) || NearSpawn(x, 0.04) || Overlaps(Features.MiteWarrens, x - rx, y - ry, x + rx, y + ry, 10))
                continue;
            for (var py = y - ry; py <= y + ry; py++)
            for (var px = x - rx; px <= x + rx; px++)
            {
                if (!grid.Contains(px, py) || py >= _height - 1 || IsProtected(grid.TileAt(px, py)))
                    continue;
                var dx = (px - x) / (double)rx;
                var dy = (py - y) / (double)ry;
                if (dx * dx + dy * dy > 1.0)
                    continue;
                grid.SetTile(new GridPoint(px, py), random.Next(10) < 3 ? TileId.MiteSilk : TileId.Air);
            }
            if (grid.TileAt(x, y + ry - 1) is TileId.Air or TileId.MiteSilk)
                grid.SetTile(new GridPoint(x, y + ry - 1), TileId.MiteSilkCocoon);
            Features.MiteWarrens.Add((x - rx, y - ry, x + rx, y + ry));
        }

        // Mosses on exposed cavern rock, coloured by broad regions.
        var mossNoise = Noise(random, _width, Math.Max(20, _width / 30.0));
        for (var i = N(WorldGenDensity.MossSpecks); i > 0; i--)
        {
            var x = random.Next(1, _width - 1);
            var y = RandomRow(random, _cavernRow, _moltenTop[x] - 2);
            if (grid.TileAt(x, y) != TileId.Rock || (grid.TileAt(x, y - 1) != TileId.Air && grid.TileAt(x, y + 1) != TileId.Air))
                continue;
            var band = (int)((mossNoise[x] + 1) * 2) % 4;
            grid.SetTile(new GridPoint(x, y), band switch
            {
                0 => TileId.MossBlue,
                1 => TileId.MossGold,
                2 => TileId.MossRed,
                _ => TileId.MossViolet
            });
        }
    }

    // ---------------------------------------------------------------- survivors and ponds

    private void PlaceSurvivors(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        // Sky-island huts and the derelict hold survivors too.
        foreach (var island in Features.Islands)
        {
            var hutX = (island.Left + island.Right) / 2 - 1;
            _hutFloors.Add((hutX, island.Top - 1));
        }
        if (Features.Derelict is { } ship)
        {
            for (var x = ship.Left + 3; x < ship.Right - 3; x++)
            {
                for (var y = ship.Top + 1; y < ship.Bottom - 1; y++)
                {
                    if (grid.TileAt(x, y) == TileId.Air && grid.TileAt(x, y - 1) == TileId.Air && catalog[grid.TileAt(x, y + 1)].IsSolid)
                    {
                        _hutFloors.Add((x, y));
                        x = ship.Right;
                        break;
                    }
                }
            }
        }
        var wanted = N(WorldGenDensity.Survivors);
        foreach (var (x, y) in _hutFloors.OrderBy(_ => random.Next()))
        {
            if (Features.SurvivorSpots.Count >= wanted)
                break;
            if (grid.Contains(x, y) && grid.TileAt(x, y) == TileId.Air && grid.Contains(x, y - 1) && grid.TileAt(x, y - 1) == TileId.Air)
                Features.SurvivorSpots.Add(new GridPoint(x, y));
        }
    }

    /// <summary>Frozen-surface ponds grow a skin of thin ice.</summary>
    private void IceFrozenPonds(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        for (var x = 0; x < _width; x++)
        {
            if (InCoast(x))
                continue; // the open ocean never ices over
            for (var y = 1; y < Math.Min(_height - 1, _rockLine[x]); y++)
            {
                var cell = grid.GetCell(x, y);
                if (cell.LiquidKind != LiquidKind.Water || cell.LiquidDepth < 0.5f)
                    continue;
                if (IsFrozen(x, y) && grid.TileAt(x, y - 1) == TileId.Air && grid.GetCell(x, y - 1).LiquidDepth <= 0f)
                {
                    cell.LiquidDepth = 0f;
                    cell.LiquidKind = LiquidKind.None;
                    cell.TileId = TileId.ThinIce;
                    grid.SetCell(new GridPoint(x, y), cell);
                }
                break;
            }
        }
    }
}
