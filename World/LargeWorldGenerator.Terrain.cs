namespace Rimtera.World;

/// <summary>
/// World-generation completion, terrain and water: mountain caves and their openings, small surface pockets, slush
/// and silt pockets, scrap dunes over the Junk Wasteland and the hardened scrap hive below it, tide grottos under the
/// acid coasts, gem veins, the underground canopy band, underground lakes, the phase pool, seeps, surface ponds and
/// canopy pools, coolant springs (after draining stray dune water), loose-sand settling, sky-exposed wall cleanup,
/// surface-lava cooling and terrain smoothing. Counts come from <see cref="WorldGenDensity"/>; all shapes original.
/// </summary>
public sealed partial class LargeWorldGenerator
{
    private readonly List<(int Center, int Half)> _duneSites = new();

    /// <summary>Plain natural ground that smoothing may slope and speleothems may hang from.</summary>
    private static bool IsNaturalGround(TileId tile) => tile is TileId.Soil or TileId.Grass or TileId.Rock or TileId.Snow or
        TileId.JungleGrass or TileId.FungalSoil or TileId.IrradiatedRock or TileId.CorruptRock or TileId.CrimsonSoil or TileId.CoreCrust or
        TileId.Mud or TileId.Clay or TileId.Slate or TileId.CanopyGrass or TileId.GlowshroomGrass or TileId.MyceliumStone or
        TileId.OvergrowthSoil or TileId.OvergrowthRock or TileId.ScrapSandstone or TileId.VitrifiedGlass or TileId.Alabaster or
        TileId.MossBlue or TileId.MossGold or TileId.MossRed or TileId.MossViolet or TileId.SaltCrust or TileId.CoralReef or
        TileId.HiveComb or TileId.RadiatedIce or TileId.OvergrowthIce;

    private int Tier(int x, int y)
    {
        x = Math.Clamp(x, 0, _width - 1);
        return y < _surface[x] + 4 ? 0 : y < _cavernRow ? 1 : y < _moltenTop[x] - 2 ? 2 : 3;
    }

    /// <summary>Digs a shallow bowl at the surface and fills it with liquid (lakes, ponds, springs and pools).</summary>
    private bool DigBowl(TileGrid grid, TileCatalog catalog, int center, int half, int depth, LiquidKind kind, TileId? lining)
    {
        if (center - half < 2 || center + half >= _width - 2)
            return false;
        var level = int.MaxValue;
        for (var x = center - half; x <= center + half; x++)
            level = Math.Min(level, TopSolid(grid, catalog, x));
        if (level >= _height - 4)
            return false;
        for (var x = center - half; x <= center + half; x++)
        {
            var t = Math.Abs(x - center) / (double)(half + 1);
            var bowl = level + (int)Math.Round(depth * Math.Sqrt(Math.Max(0, 1 - t * t)));
            var top = TopSolid(grid, catalog, x);
            for (var y = top; y <= bowl; y++)
            {
                if (IsProtected(grid.TileAt(x, y)))
                    continue;
                grid.SetTile(new GridPoint(x, y), TileId.Air);
                if (y > level)
                    grid.TryAddLiquid(new GridPoint(x, y), kind, 1f, catalog);
            }
            if (lining is { } floor && grid.Contains(x, bowl + 1) && !IsProtected(grid.TileAt(x, bowl + 1)) &&
                catalog[grid.TileAt(x, bowl + 1)].IsSolid)
                grid.SetTile(new GridPoint(x, bowl + 1), floor);
        }
        return true;
    }

    // ---------------------------------------------------------------- mountain caves and surface pockets

    private void MountainCaves(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var tall = _mountains.Where(m => m.Height >= 8).ToList();
        var wanted = tall.Count == 0 ? 0 : N(WorldGenDensity.MountainCaves);
        for (var i = 0; i < wanted; i++)
        {
            token.ThrowIfCancellationRequested();
            var (center, halfWidth, lift) = tall[random.Next(tall.Count)];
            double x = center + (random.NextDouble() - 0.5) * halfWidth;
            if (x < 4 || x >= _width - 4)
                continue;
            double y = _surface[(int)x] + 4 + random.NextDouble() * Math.Max(1, lift * 0.5);
            var heading = random.Next(2) == 0 ? -1 : 1;
            var length = (int)(halfWidth * (1.0 + random.NextDouble()));
            var opened = false;
            for (var step = 0; step < length; step++)
            {
                x += heading * (0.8 + random.NextDouble() * 0.4);
                y += (random.NextDouble() - 0.5) * 0.9;
                if (x < 3 || x >= _width - 3)
                    break;
                y = Math.Clamp(y, _surface[(int)x] + 3, _surface[(int)x] + Math.Max(4, lift));
                Stamp(grid, (int)x, (int)y, 1.6 + random.NextDouble() * 1.1, CarveAir);
                // An opening: a short shaft angling up and out through the mountain's flank.
                if (!opened && step > length / 3 && random.Next(6) == 0)
                {
                    double ox = x, oy = y;
                    for (var s = 0; s < 40 && oy > _surface[Math.Clamp((int)ox, 0, _width - 1)] - 1; s++)
                    {
                        ox += heading * 0.7;
                        oy -= 0.8;
                        if (ox < 3 || ox >= _width - 3)
                            break;
                        Stamp(grid, (int)ox, (int)oy, 1.4, CarveAir);
                    }
                    opened = true;
                }
            }
            Features.AddSite(FeatureNames.MountainCaves, (int)Math.Min(x, center), (int)y - 3, (int)Math.Max(x, center), (int)y + 3);
        }

        // Many small surface pockets just under the ground, some breaking the surface.
        for (var i = N(WorldGenDensity.SurfacePockets); i > 0; i--)
        {
            var x = random.Next(4, _width - 4);
            if (NearSpawn(x, 0.04))
                continue;
            var y = _surface[x] + random.Next(1, 12);
            Blob(grid, random, x, y, 1.3 + random.NextDouble(), random.Next(3, 8), random.NextDouble() - 0.5,
                random.NextDouble() * 0.4 - 0.2, CarveAir, minY: _surface[x] - 1);
            Features.AddCount(FeatureNames.SurfacePockets);
        }
    }

    // ---------------------------------------------------------------- slush and silt pockets

    private void SlushPockets(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var wanted = N(WorldGenDensity.SlushPockets);
        for (var attempt = 0; attempt < wanted * 6 && Features.CountOf(FeatureNames.SlushPockets) < wanted; attempt++)
        {
            var y = RandomRow(random, _rockLine[0] + 2, _moltenTop[0] - 8);
            var edge = _frozenEdge[y];
            if (edge < 4)
                continue;
            var x = random.Next(1, edge);
            Blob(grid, random, x, y, 1.5 + random.NextDouble() * 2, random.Next(4, 10), random.NextDouble() - 0.5,
                random.NextDouble() - 0.5, tile => tile is TileId.Snow or TileId.Rock or TileId.Soil or TileId.Silt or TileId.Mud ? TileId.Slush : null);
            Features.AddCount(FeatureNames.SlushPockets);
        }
    }

    private void SiltPockets(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        for (var i = N(WorldGenDensity.SiltPockets); i > 0; i--)
        {
            var x = random.Next(2, _width - 2);
            var y = RandomRow(random, _cavernRow, _moltenTop[x] - 6);
            Blob(grid, random, x, y, 1.5 + random.NextDouble() * 2, random.Next(4, 10), random.NextDouble() - 0.5,
                random.NextDouble() - 0.5, tile => tile is TileId.Rock or TileId.Soil or TileId.Slate ? TileId.Silt : null);
            Features.AddCount(FeatureNames.SiltPockets);
        }
    }

    // ---------------------------------------------------------------- scrap dunes and the scrap hive

    private void ScrapDunes(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var junkLeft = _junkStart[Math.Clamp(_surface[_width - 1], 0, _height - 1)];
        var junkWidth = _width - junkLeft;
        if (junkWidth < 12)
            return;
        var wanted = N(WorldGenDensity.ScrapDunes);
        // Dunes stop short of the coastal shelf so the land eases down to the fuel sea.
        var coastLimit = _width - CoastReach;
        for (var i = 0; i < wanted; i++)
        {
            var half = random.Next(10, Math.Max(11, Math.Min(42, junkWidth / 3)));
            var center = random.Next(junkLeft + half / 2, Math.Max(junkLeft + half / 2 + 1, _width - half / 2));
            var height = 3 + random.NextDouble() * Math.Max(5, _height * 0.007);
            var crest = int.MaxValue;
            for (var x = center - half; x <= center + half; x++)
            {
                if (x < 1 || x >= coastLimit || !IsJunk(x, _surface[x]))
                    continue;
                var t = (x - center) / (double)half;
                // Long windward slope on the west, short steep lee on the east.
                var lift = height * (t < 0 ? Math.Pow(1 + t, 1.6) : Math.Pow(Math.Max(0, 1 - t), 0.55));
                var top = Math.Max(4, _surface[x] - (int)Math.Round(lift));
                for (var y = top; y < _surface[x]; y++)
                    if (grid.TileAt(x, y) == TileId.Air)
                        grid.SetTile(new GridPoint(x, y), TileId.DesertSand);
                _surface[x] = top;
                crest = Math.Min(crest, top);
            }
            if (crest == int.MaxValue)
                continue;
            _duneSites.Add((center, half));
            Features.AddSite(FeatureNames.ScrapDunes, center - half, crest, center + half, crest + (int)height);
        }
    }

    private void ScrapHive(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var left = Math.Max(_width / 2, _junkStart[_cavernRow]);
        var right = _width - N(WorldGenDensity.CoastWidth) - 30; // well clear of the sea floor
        if (right - left < 24)
            return;
        var cx = (left + right) / 2;
        var rx = Math.Max(10, (right - left) * 2 / 5);
        var top = _rockLine[cx] + 4;
        var bottom = _cavernRow + (int)((_moltenTop[cx] - _cavernRow) * 0.25);
        if (bottom - top < 14)
            return;
        var ry = (bottom - top) / 2;
        var cy = top + ry;
        var edge = Noise(random, 360, 9);
        for (var y = cy - ry; y <= cy + ry; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = cx - rx; x <= cx + rx; x++)
            {
                if (!grid.Contains(x, y) || y >= _height - 2)
                    continue;
                var dx = (x - cx) / (double)rx;
                var dy = (y - cy) / (double)ry;
                var angle = (int)((Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI) * 359);
                if (dx * dx + dy * dy > 1.0 + edge[angle] * 0.18)
                    continue;
                var tile = grid.TileAt(x, y);
                if (tile is TileId.Air or TileId.ScrapIronOre || IsProtected(tile))
                    continue;
                grid.SetTile(new GridPoint(x, y), TileId.ScrapSandstone);
            }
        }
        // Burrower galleries: wandering caverns that stay inside the hardened sand.
        var galleries = Math.Max(4, (int)(Math.PI * rx * ry / 420));
        for (var i = 0; i < galleries; i++)
        {
            var gx = cx + random.Next(-rx * 3 / 4, rx * 3 / 4 + 1);
            var gy = cy + random.Next(-ry * 3 / 4, ry * 3 / 4 + 1);
            Blob(grid, random, gx, gy, 2.5 + random.NextDouble() * 2.5, random.Next(10, 26), random.NextDouble() * 2 - 1,
                random.NextDouble() * 0.6 - 0.3, tile => tile == TileId.ScrapSandstone ? TileId.Air : null, wander: 0.6);
        }
        var nests = 0;
        for (var attempt = 0; attempt < galleries * 20 && nests < galleries; attempt++)
        {
            var x = cx + random.Next(-rx, rx + 1);
            var y = cy + random.Next(-ry, ry + 1);
            if (!IsCaveFloor(grid, catalog, x, y) || grid.TileAt(x, y + 1) != TileId.ScrapSandstone)
                continue;
            Put(grid, x, y, TileId.BurrowerNest);
            nests++;
        }
        Features.AddCount(FeatureNames.BurrowerNests, nests);
        Features.AddSite(FeatureNames.ScrapHive, cx - rx, cy - ry, cx + rx, cy + ry);
    }

    // ---------------------------------------------------------------- tide grottos

    private void TideGrottos(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        // A solid lining cell (any old cave or liquid there is closed off so the flooded grotto cannot drain).
        void Seal(int x, int y, TileId lining)
        {
            var cell = grid.GetCell(x, y);
            cell.TileId = lining;
            cell.LiquidUnits = 0;
            cell.LiquidKind = LiquidKind.None;
            grid.SetCell(new GridPoint(x, y), cell);
        }
        foreach (var (coastLeft, coastRight) in Features.AcidCoasts.ToList())
        {
            var wanted = N(WorldGenDensity.TideGrottosPerCoast);
            var made = 0;
            for (var attempt = 0; attempt < wanted * 12 && made < wanted; attempt++)
            {
                if (coastRight - coastLeft < 16)
                    break;
                var x = random.Next(coastLeft + 6, coastRight - 6);
                var seabed = TopSolid(grid, catalog, x);
                var rx = random.Next(6, Math.Max(7, Math.Min(13, (coastRight - coastLeft) / 3)));
                var ry = random.Next(3, 6);
                var cy = seabed + ry + random.Next(5, 11);
                if (cy + ry + 3 >= _moltenTop[x] - 4 || Features.SitesOf(FeatureNames.TideGrottos)
                        .Any(g => x - rx < g.Right + 4 && x + rx > g.Left - 4))
                    continue;
                for (var y = cy - ry - 2; y <= cy + ry + 2; y++)
                for (var px = x - rx - 2; px <= x + rx + 2; px++)
                {
                    if (!grid.Contains(px, y) || y >= _height - 2 || IsProtected(grid.TileAt(px, y)))
                        continue;
                    var dx = (px - x) / (double)rx;
                    var dy = (y - cy) / (double)ry;
                    var d = dx * dx + dy * dy;
                    if (d <= 1.0)
                    {
                        grid.SetTile(new GridPoint(px, y), TileId.Air);
                        grid.TryAddLiquid(new GridPoint(px, y), LiquidKind.Fuel, 1f, catalog);
                    }
                    else if (d <= 2.2)
                        Seal(px, y, random.Next(3) == 0 ? TileId.CoralReef : TileId.SaltCrust);
                }
                // The mouth: a two-wide flooded shaft up to the sea floor, walled with salt crust.
                for (var y = seabed - 1; y < cy - ry + 1; y++)
                for (var dx = -2; dx <= 3; dx++)
                {
                    var px = x + dx;
                    if (!grid.Contains(px, y) || IsProtected(grid.TileAt(px, y)))
                        continue;
                    if (dx is 0 or 1)
                    {
                        grid.SetTile(new GridPoint(px, y), TileId.Air);
                        grid.TryAddLiquid(new GridPoint(px, y), LiquidKind.Fuel, 1f, catalog);
                    }
                    else if (y >= seabed)
                        Seal(px, y, TileId.SaltCrust);
                }
                // Treasure on the grotto floor, and coral fans.
                var chestX = x + random.Next(-rx / 2, rx / 2 + 1);
                for (var y = cy; y <= cy + ry + 1; y++)
                {
                    if (grid.TileAt(chestX, y) == TileId.Air && catalog[grid.TileAt(chestX, y + 1)].IsSolid)
                    {
                        Put(grid, chestX, y, TileId.SunkenChest);
                        break;
                    }
                }
                for (var px = x - rx + 1; px < x + rx; px++)
                for (var y = cy; y <= cy + ry + 1; y++)
                {
                    if (grid.TileAt(px, y) != TileId.Air || !catalog[grid.TileAt(px, y + 1)].IsSolid)
                        continue;
                    if (random.Next(3) == 0)
                    {
                        Put(grid, px, y, TileId.CoralFan);
                        Features.AddCount(FeatureNames.CoralFans);
                    }
                    break;
                }
                Features.AddSite(FeatureNames.TideGrottos, x - rx, cy - ry, x + rx, cy + ry);
                made++;
            }
        }
    }

    /// <summary>
    /// Seals dry caves and loose substrate just under each sea floor (and its shore) so the sea neither drains nor
    /// collapses into them; flooded grottos are left open. Runs after all carving, before sand settles.
    /// </summary>
    private void SealSeaFloor(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var sealedCells = 0;
        foreach (var (coastLeft, coastRight) in Features.AcidCoasts)
            for (var x = Math.Max(0, coastLeft - 10); x <= Math.Min(_width - 1, coastRight + 10); x++)
            {
                var seabed = TopSolid(grid, catalog, x);
                for (var y = seabed + 1; y < Math.Min(_height - 2, seabed + 18); y++)
                {
                    var cell = grid.GetCell(x, y);
                    var open = cell.TileId == TileId.Air && cell.LiquidUnits == 0;
                    var loose = y >= seabed + 4 && TilePhysics.Falls(cell.TileId);
                    if (!open && !loose)
                        continue;
                    cell.TileId = TileId.Rock;
                    cell.Flags = 0;
                    grid.SetCell(new GridPoint(x, y), cell);
                    sealedCells++;
                }
            }
        Features.AddCount("sealed-sea-floor", sealedCells);
    }

    // ---------------------------------------------------------------- gems

    private void GemVeins(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        for (var i = N(WorldGenDensity.GemVeins); i > 0; i--)
        {
            var x = random.Next(2, _width - 2);
            var y = RandomRow(random, _rockLine[x], _moltenTop[x] - 4);
            var gem = GemForDepth(random, x, y);
            Blob(grid, random, x, y, 0.9 + random.NextDouble() * 0.6, random.Next(2, 5), random.NextDouble() - 0.5,
                random.NextDouble() - 0.5, tile => tile is TileId.Rock or TileId.Slate ? gem : null);
            Features.AddCount(FeatureNames.GemVeins);
        }
    }

    /// <summary>Shallow rock holds amethyst and topaz, the middle sapphire and emerald, the deep ruby and diamond.</summary>
    private TileId GemForDepth(Random random, int x, int y)
    {
        var span = Math.Max(1, _moltenTop[x] - _rockLine[x]);
        var band = (int)((y - _rockLine[x]) / (double)span * 3) + random.Next(-1, 2);
        band = Math.Clamp(band, 0, 2);
        return ContentTiles.GemTiles[band * 2 + random.Next(2)].Tile;
    }

    // ---------------------------------------------------------------- the underground canopy band

    private void UndergroundCanopy(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        foreach (var (left, right) in Features.Canopies)
        {
            token.ThrowIfCancellationRequested();
            var middle = (left + right) / 2;
            var bottom = _cavernRow + (int)((_moltenTop[middle] - _cavernRow) * 0.35);
            var top = _rockLine[middle];
            var sway = Noise(random, _height, 23);
            var bulge = Noise(random, _height, 11);
            var halfSpan = (right - left) / 2.0;
            for (var y = top; y <= bottom; y++)
            {
                // An organic column: it sways, bulges and rounds off toward its foot.
                var depth = (y - top) / (double)Math.Max(1, bottom - top);
                var half = halfSpan * (1.0 + bulge[y] * 0.22) * Math.Sqrt(Math.Max(0.05, 1 - Math.Pow(depth, 4))) + 4;
                var centre = middle + sway[y] * halfSpan * 0.25;
                for (var x = (int)(centre - half); x <= (int)(centre + half); x++)
                {
                    if (!grid.Contains(x, y) || y < _rockLine[Math.Clamp(x, 0, _width - 1)] || y >= _height - 2)
                        continue;
                    if (grid.TileAt(x, y) is TileId.Rock or TileId.Soil or TileId.Clay or TileId.Gravel or TileId.Slate or TileId.Silt or TileId.Sand)
                        grid.SetTile(new GridPoint(x, y), TileId.Mud);
                }
            }
            // Mud caverns, carved only through mud so hives and ores stay whole.
            var caverns = Math.Max(2, (right - left) * (bottom - _rockLine[middle]) / 1800);
            for (var i = 0; i < caverns; i++)
            {
                var x = random.Next(left, right + 1);
                var y = RandomRow(random, _rockLine[x] + 4, bottom - 2);
                Blob(grid, random, x, y, 2.5 + random.NextDouble() * 2.2, random.Next(10, 24), random.NextDouble() * 2 - 1,
                    random.NextDouble() * 0.6 - 0.3, tile => tile == TileId.Mud ? TileId.Air : null, wander: 0.6);
            }
            // Jungle grass on every exposed mud floor, and stunted canopy trees where there is headroom.
            var trees = 0;
            for (var x = left - 4; x <= right + 4; x++)
            {
                if (x < 1 || x >= _width - 1)
                    continue;
                for (var y = _rockLine[x]; y <= bottom; y++)
                {
                    if (grid.TileAt(x, y) != TileId.Mud || grid.TileAt(x, y - 1) != TileId.Air)
                        continue;
                    grid.SetTile(new GridPoint(x, y), TileId.CanopyGrass);
                    if (random.Next(9) == 0 && TilePhysics.TryGrowTree(grid, catalog, x, y - 1, random, minHeight: 3, maxHeight: 6))
                        trees++;
                }
            }
            Features.AddCount(FeatureNames.UndergroundCanopyTrees, trees);
            Features.AddSite(FeatureNames.UndergroundCanopy, left, _rockLine[middle], right, bottom);
        }
    }

    // ---------------------------------------------------------------- underground lakes and the phase pool

    private void UndergroundLakes(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var wanted = N(WorldGenDensity.UndergroundLakes);
        for (var attempt = 0; attempt < wanted * 8 && Features.SitesOf(FeatureNames.UndergroundLakes).Count < wanted; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var cx = random.Next(_width / 16, _width - _width / 16);
            var rx = random.Next(6, 17);
            var ry = random.Next(3, 7);
            if (_moltenTop[cx] - 10 - ry <= _cavernRow + 4 + ry)
                continue;
            var cy = random.Next(_cavernRow + 4 + ry, _moltenTop[cx] - 10 - ry);
            if (InCoast(cx) || AreaProtected(grid, cx - rx - 1, cy - ry - 1, cx + rx + 1, cy + ry + 1) ||
                InStructure(cx - rx, cy - ry, cx + rx, cy + ry, 4))
                continue;
            // Close off any old caves around the lower bowl first, so the lake holds its water.
            for (var y = cy - ry / 4; y <= cy + ry + 2; y++)
            for (var x = cx - rx - 3; x <= cx + rx + 3; x++)
            {
                if (!grid.Contains(x, y) || y >= _height - 2)
                    continue;
                var dx = (x - cx) / (double)(rx + 3);
                var dy = (y - cy) / (double)(ry + 2);
                var cell = grid.GetCell(x, y);
                if (dx * dx + dy * dy <= 1.0 && cell.TileId == TileId.Air && cell.LiquidUnits == 0)
                    grid.SetTile(new GridPoint(x, y), TileId.Rock);
            }
            var inCanopy = Features.Canopies.Any(c => cx >= c.Left && cx <= c.Right);
            var kind = cy > _height * 0.72 && random.Next(3) == 0 ? LiquidKind.Lava
                : inCanopy && random.Next(3) == 0 ? LiquidKind.Nectar : LiquidKind.Water;
            for (var y = cy - ry; y <= cy + ry; y++)
            {
                // A flat-topped bowl: the upper half is squashed into a low ceiling.
                var dy = (y - cy) / (double)(y < cy ? ry * 0.7 : ry);
                var reach = rx * Math.Sqrt(Math.Max(0, 1 - dy * dy)) + random.Next(-1, 2);
                for (var x = cx - (int)reach; x <= cx + (int)reach; x++)
                {
                    if (!grid.Contains(x, y) || IsProtected(grid.TileAt(x, y)))
                        continue;
                    grid.SetTile(new GridPoint(x, y), TileId.Air);
                    if (y >= cy - ry / 4)
                        grid.TryAddLiquid(new GridPoint(x, y), kind, 1f, catalog);
                }
            }
            Features.AddSite(FeatureNames.UndergroundLakes, cx - rx, cy - ry, cx + rx, cy + ry);
        }
    }

    private void PhasePool(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var rx = Math.Clamp(_width / 220, 8, 34);
        var ry = Math.Max(4, rx * 2 / 5);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var cx = random.Next(Math.Max(rx + 4, (int)(_width * 0.12)), Math.Min(_width - rx - 4, (int)(_width * 0.88)));
            var top = _cavernRow + (int)((_moltenTop[cx] - _cavernRow) * 0.45) + ry;
            var bottom = _moltenTop[cx] - 12 - ry;
            if (attempt > 120)
                top = _cavernRow + ry + 2;
            if (bottom <= top)
                continue;
            var cy = random.Next(top, bottom);
            if (InCoast(cx) || NearSpawn(cx, 0.05) || AreaProtected(grid, cx - rx - 2, cy - ry - 2, cx + rx + 2, cy + ry + 2) ||
                (attempt < 150 && InStructure(cx - rx, cy - ry, cx + rx, cy + ry, 6)))
                continue;
            for (var y = cy - ry - 3; y <= cy + ry + 3; y++)
            for (var x = cx - rx - 3; x <= cx + rx + 3; x++)
            {
                if (!grid.Contains(x, y) || y >= _height - 2)
                    continue;
                var dx = (x - cx) / (double)rx;
                var dy = (y - cy) / (double)ry;
                var d = dx * dx + dy * dy;
                if (d <= 1.0)
                {
                    grid.SetTile(new GridPoint(x, y), TileId.Air);
                    if (y >= cy - ry / 5)
                        grid.TryAddLiquid(new GridPoint(x, y), LiquidKind.Phase, 1f, catalog);
                }
                else if (d <= 1.9)
                    grid.SetTile(new GridPoint(x, y), random.Next(4) == 0 ? TileId.GeodeMagenta : TileId.Alabaster);
                if (d <= 1.9)
                    grid.SetWall(new GridPoint(x, y), WallIds.Crystal);
            }
            // A crooked approach from above so cave explorers stumble onto it.
            double ax = cx, ay = cy - ry;
            var dir = random.Next(2) == 0 ? -1 : 1;
            for (var step = 0; step < ry * 4 + 12; step++)
            {
                ax += dir * (0.4 + random.NextDouble() * 0.6);
                ay -= 0.7;
                if (ax < 3 || ax >= _width - 3 || ay < _cavernRow)
                    break;
                Stamp(grid, (int)ax, (int)ay, 1.5, CarveAir);
            }
            Features.AddSite(FeatureNames.PhasePool, cx - rx, cy - ry, cx + rx, cy + ry);
            return;
        }
    }

    // ---------------------------------------------------------------- surface water

    private void SurfacePonds(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var wanted = N(WorldGenDensity.SurfacePonds);
        for (var attempt = 0; attempt < wanted * 10 && Features.SitesOf(FeatureNames.SurfacePonds).Count < wanted; attempt++)
        {
            var center = random.Next(_width / 12, _width - _width / 12);
            var half = random.Next(3, 7);
            var top = TopSolid(grid, catalog, center);
            if (NearSpawn(center, 0.05) || InBlightZone(center, half + 4) || InCoast(center) || IsJunk(center, top) ||
                InStructure(center - half, top - 8, center + half, top + 4, 4) ||
                grid.TileAt(center, top) is not (TileId.Grass or TileId.JungleGrass or TileId.Snow or TileId.Soil))
                continue;
            if (DigBowl(grid, catalog, center, half, random.Next(1, 4), LiquidKind.Water, IsFrozen(center, top) ? TileId.Snow : TileId.Clay))
                Features.AddSite(FeatureNames.SurfacePonds, center - half, top, center + half, top + 3);
        }
    }

    private void CanopyPools(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        foreach (var (left, right) in Features.Canopies)
        {
            var pools = Math.Max(1, (right - left) / 45);
            for (var attempt = 0; attempt < pools * 8 && pools > 0; attempt++)
            {
                var center = random.Next(left + 6, Math.Max(left + 7, right - 6));
                var half = random.Next(2, 6);
                var top = TopSolid(grid, catalog, center);
                if (grid.TileAt(center, top) != TileId.CanopyGrass || InStructure(center - half, top - 8, center + half, top + 4, 3))
                    continue;
                if (DigBowl(grid, catalog, center, half, random.Next(1, 3), LiquidKind.Water, TileId.Mud))
                {
                    Features.AddSite(FeatureNames.CanopyPools, center - half, top, center + half, top + 2);
                    pools--;
                }
            }
        }
    }

    /// <summary>Stray surface water sitting on dune sand drains away (springs are dug afterwards).</summary>
    private void DrainDunes(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var drained = 0;
        for (var x = 0; x < _width; x++)
        {
            var top = TopSolid(grid, catalog, x);
            if (!IsJunk(x, top) || grid.TileAt(x, top) is not (TileId.DesertSand or TileId.ScrapSandstone or TileId.Sand))
                continue;
            for (var y = Math.Max(0, top - 24); y < top; y++)
            {
                var cell = grid.GetCell(x, y);
                if (cell.LiquidKind != LiquidKind.Water || cell.LiquidUnits == 0)
                    continue;
                cell.LiquidUnits = 0;
                cell.LiquidKind = LiquidKind.None;
                grid.SetCell(new GridPoint(x, y), cell);
                drained++;
            }
        }
        Features.AddCount(FeatureNames.DrainedDuneWater, drained);
    }

    private void CoolantSprings(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var wanted = N(WorldGenDensity.CoolantSprings);
        for (var attempt = 0; attempt < wanted * 20 && Features.SitesOf(FeatureNames.CoolantSprings).Count < wanted; attempt++)
        {
            var center = random.Next(_width / 2, _width - 4);
            var top = TopSolid(grid, catalog, center);
            var half = random.Next(4, 9);
            if (!IsJunk(center - half, top) || InCoast(center - half - 3) || InCoast(center + half + 3) ||
                InStructure(center - half - 3, top - 10, center + half + 3, top + 6, 3) ||
                Features.SitesOf(FeatureNames.BuriedLanders).Any(l => center + half + 3 > l.Left && center - half - 3 < l.Right) ||
                Features.SitesOf(FeatureNames.CoolantSprings).Any(s => center + half + 8 > s.Left && center - half - 8 < s.Right) ||
                grid.TileAt(center, top) is not (TileId.DesertSand or TileId.Sand))
                continue;
            if (!DigBowl(grid, catalog, center, half, random.Next(2, 5), LiquidKind.Water, TileId.ScrapSandstone))
                continue;
            // Rust palms on both banks.
            foreach (var side in new[] { -1, 1 })
            {
                var x = center + side * (half + 2);
                var ground = TopSolid(grid, catalog, x);
                if (grid.Contains(x, ground - 1) && grid.TileAt(x, ground - 1) == TileId.Air)
                    TilePhysics.TryGrowTree(grid, catalog, x, ground - 1, random, minHeight: 5, maxHeight: 8);
            }
            Features.AddSite(FeatureNames.CoolantSprings, center - half, top, center + half, top + 4);
        }
    }

    /// <summary>Seeps: small reservoirs behind cave walls that pour water (or, deep down, lava) into the caves.</summary>
    private void Seeps(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        void Make(LiquidKind kind, int wanted, string feature, double minDepthFraction)
        {
            var made = 0;
            for (var attempt = 0; attempt < wanted * 40 && made < wanted; attempt++)
            {
                var x = random.Next(5, _width - 5);
                var top = kind == LiquidKind.Lava ? (int)(_height * minDepthFraction) : _rockLine[x];
                var y = RandomRow(random, top, _moltenTop[x] - 4);
                var dir = random.Next(2) == 0 ? -1 : 1;
                if (grid.TileAt(x, y) != TileId.Air || grid.GetCell(x, y).LiquidUnits > 0 ||
                    grid.TileAt(x, y + 1) != TileId.Air || grid.TileAt(x, y + 2) != TileId.Air)
                    continue;
                var ok = true;
                for (var d = 1; d <= 4 && ok; d++)
                for (var dy = -1; dy <= 1 && ok; dy++)
                    ok = IsNaturalGround(grid.TileAt(x + dir * d, y + dy));
                if (!ok)
                    continue;
                for (var d = 2; d <= 3; d++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    grid.SetTile(new GridPoint(x + dir * d, y + dy), TileId.Air);
                    grid.TryAddLiquid(new GridPoint(x + dir * d, y + dy), kind, 1f, catalog);
                }
                grid.SetTile(new GridPoint(x + dir, y), TileId.Air);
                grid.TryAddLiquid(new GridPoint(x + dir, y), kind, 1f, catalog);
                made++;
            }
            Features.AddCount(feature, made);
        }
        Make(LiquidKind.Water, N(WorldGenDensity.WaterSeeps), FeatureNames.WaterSeeps, 0);
        Make(LiquidKind.Lava, N(WorldGenDensity.LavaSeeps), FeatureNames.LavaSeeps, 0.7);
    }

    // ---------------------------------------------------------------- cleanup passes

    /// <summary>Drops every unsupported falling block (sand, silt, slush, boulders) onto whatever lies below it.</summary>
    private void SettleLooseSand(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var cells = grid.RawCells;
        var moved = 0;
        for (var x = 0; x < _width; x++)
        {
            if ((x & 255) == 0)
                token.ThrowIfCancellationRequested();
            for (var y = _height - 3; y >= 0; y--)
            {
                var index = y * _width + x;
                if (!TilePhysics.Falls(cells[index].TileId) || cells[index + _width].TileId != TileId.Air)
                    continue;
                var landing = y + 1;
                while (landing + 1 < _height - 1 && cells[(landing + 1) * _width + x].TileId == TileId.Air)
                    landing++;
                var target = landing * _width + x;
                var block = cells[index].TileId;
                cells[target].TileId = block;
                cells[target].Flags = 0;
                cells[target].LiquidUnits = 0;
                cells[target].LiquidKind = LiquidKind.None;
                cells[target].Damage = 0;
                cells[target].FrameMask = 0;
                cells[index].TileId = TileId.Air;
                cells[index].Flags = 0;
                cells[index].Damage = 0;
                cells[index].FrameMask = 0;
                moved++;
            }
        }
        if (moved > 0)
            grid.MarkBulkEdit();
        Features.AddCount(FeatureNames.SettledSand, moved);
    }

    private static bool IsTerrainWall(ushort wall) => wall is WallIds.Regolith or WallIds.Rock or WallIds.Ice or WallIds.Scrap or
        WallIds.Spore or WallIds.Radiated or WallIds.Overgrowth or WallIds.Ash or WallIds.Sand or WallIds.NaturalMud or
        WallIds.NaturalCoral or WallIds.GrassRoots or WallIds.CaveLayered or WallIds.CaveRough or WallIds.MossyRock;

    /// <summary>Natural walls left hanging in open sky (over cave mouths, pockets and lake bowls) are removed.</summary>
    private void ClearSkyWalls(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var cells = grid.RawCells;
        var cleared = 0;
        for (var x = 0; x < _width; x++)
        {
            var top = TopSolid(grid, catalog, x);
            for (var y = 0; y < top; y++)
            {
                ref var cell = ref cells[y * _width + x];
                if (cell.WallId == 0 || !IsTerrainWall(cell.WallId))
                    continue;
                cell.WallId = 0;
                cleared++;
            }
        }
        if (cleared > 0)
            grid.MarkBulkEdit();
        Features.AddCount(FeatureNames.ClearedSkyWalls, cleared);
    }

    /// <summary>Lava that ended up near the surface (seeps, lakes) cools away.</summary>
    private void CoolSurfaceLava(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var cooled = 0;
        var cells = grid.RawCells;
        var deepest = _rockLine.Max() + 4;
        for (var y = 0; y < Math.Min(_height - 1, deepest); y++)
        for (var x = 0; x < _width; x++)
        {
            ref var cell = ref cells[y * _width + x];
            if (cell.LiquidUnits == 0 || cell.LiquidKind != LiquidKind.Lava || y >= _rockLine[x] + 4)
                continue;
            cell.LiquidUnits = 0;
            cell.LiquidKind = LiquidKind.None;
            cooled++;
        }
        Features.AddCount(FeatureNames.CooledSurfaceLava, cooled);
    }

    /// <summary>
    /// Slopes and half-blocks on exposed natural edges: a ledge open on one side becomes a ramp, a lone bump a half
    /// block, and overhangs get ceiling slopes. Shapes only plain natural ground (see <see cref="TilePhysics.IsShapeable"/>).
    /// </summary>
    private void SmoothTerrain(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var cells = grid.RawCells;
        int sloped = 0, halves = 0;
        var seed = random.Next();
        bool Open(int index) => cells[index].TileId == TileId.Air;
        for (var y = 2; y < _height - 2; y++)
        {
            if ((y & 63) == 0)
                token.ThrowIfCancellationRequested();
            var row = y * _width;
            for (var x = 2; x < _width - 2; x++)
            {
                var index = row + x;
                ref var cell = ref cells[index];
                if (cell.TileId == TileId.Air || !IsNaturalGround(cell.TileId) || cell.Shape != TileShape.Full || cell.HasActuator)
                    continue;
                var above = Open(index - _width);
                var below = Open(index + _width);
                if (above == below)
                    continue;
                var left = Open(index - 1);
                var right = Open(index + 1);
                if (!left && !right)
                    continue;
                // Deterministic per cell: about three in four candidates are shaped.
                var hash = (uint)(x * 73856093 ^ y * 19349663 ^ seed);
                if ((hash >> 7) % 4 == 0)
                    continue;
                TileShape shape;
                if (above)
                {
                    // Keep anything resting on this block (a falling block above would sit oddly on a ramp).
                    if (left && right)
                        shape = TileShape.Half;
                    else
                        shape = left ? TileShape.SlopeUpRight : TileShape.SlopeUpLeft;
                }
                else
                {
                    if (left && right)
                        continue;
                    shape = left ? TileShape.CeilingRight : TileShape.CeilingLeft;
                }
                cell.Shape = shape;
                if (shape == TileShape.Half)
                    halves++;
                else
                    sloped++;
            }
        }
        grid.MarkBulkEdit();
        Features.AddCount(FeatureNames.SlopedEdges, sloped);
        Features.AddCount(FeatureNames.HalfBlocks, halves);
    }
}
