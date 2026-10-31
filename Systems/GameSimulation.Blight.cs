using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Engine services and the blight: random tile updates, active liquids, purifier beacons,
/// blight hearts and altars, the Fracture, world loot containers and life cores.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>Worlds at least this wide (or created with a blight) use the detailed generator.</summary>
    public const int DetailedGenerationMinimumWidth = 400;
    /// <summary>Seconds between active-liquid steps.</summary>
    public const float LiquidStepSeconds = 0.1f;
    /// <summary>Rooms farther than this from the player skip the temperature model.</summary>
    public const float RoomSimulationRadius = 160f;
    /// <summary>Every this-many broken hearts summons the blight's boss.</summary>
    public const int HeartsPerBoss = 3;

    /// <summary>
    /// The last easy-tier boss in the progression. Defeating it no longer fractures the world by itself: it is the
    /// requirement for summoning the Irradiated Core's gate boss (<see cref="CoreGateBoss"/>), whose death causes the Fracture.
    /// </summary>
    public static readonly EnemyKind LastEasyBoss =
        EnemyArchetypes.BossProgression.Last(kind => EnemyArchetypes.Get(kind).Tier == BossTier.Easy);

    private readonly WorldTicker _ticker;
    private readonly ActiveLiquidSimulator _liquids;
    // Separate stream for world-changing rolls (altar ore) so it never shifts spawns or loot.
    private readonly Random _worldRandom;
    private readonly WorldHazard? _hazardChoice;
    private int _heartsBroken;
    private int _altarsSmashed;
    private readonly List<GridPoint> _lastAltarOre = new();

    /// <summary>The world's blight, or null for legacy worlds generated without one (no spread).</summary>
    public WorldBlight? Blight { get; private set; }
    /// <summary>What the detailed generator placed (zones, hearts, altars, loot), or null for legacy worlds.</summary>
    public WorldFeatures? Features { get; }
    /// <summary>True once the last easy-tier boss has fallen: altars unlock and blight spreads into everything, faster.</summary>
    public bool FractureActive { get; private set; }
    /// <summary>Tiles converted by the Fracture's blight scar.</summary>
    public int FractureCellsConverted { get; private set; }
    /// <summary>Tiles converted by the Fracture's mirrored prism scar.</summary>
    public int FracturePrismCells { get; private set; }
    public int HeartsBroken => _heartsBroken;
    public int AltarsSmashed => _altarsSmashed;
    /// <summary>Cells where the most recent altar smash seeded ore veins.</summary>
    public IReadOnlyList<GridPoint> LastAltarOreSeeds => _lastAltarOre;
    /// <summary>Placed purifier beacons (blight cannot spread within <see cref="WorldTicker.BeaconRadius"/> tiles).</summary>
    public IReadOnlyCollection<GridPoint> BeaconPositions => _ticker.Beacons;
    /// <summary>Random tile updates (grass, blight spread, crystals).</summary>
    public WorldTicker Ticker => _ticker;
    /// <summary>Event-driven liquid processing (active set, buffer, fallback statistics).</summary>
    public ActiveLiquidSimulator Liquids => _liquids;

    private void OnWorldCellChanged(int x, int y, in WorldCell before, in WorldCell after)
    {
        if (before.TileId == after.TileId)
            return;
        if (after.TileId == TileId.PurifierBeacon)
            _ticker.Beacons.Add(new GridPoint(x, y));
        else if (before.TileId == TileId.PurifierBeacon)
            _ticker.Beacons.Remove(new GridPoint(x, y));
    }

    private void ScanBeacons()
    {
        _ticker.Beacons.Clear();
        var cells = World.Cells;
        for (var index = 0; index < cells.Length; index++)
            if (cells[index].TileId == TileId.PurifierBeacon)
                _ticker.Beacons.Add(new GridPoint(index % World.Width, index / World.Width));
    }

    /// <summary>After a bulk cell load: rescan beacons and wake liquids that can still move.</summary>
    private void AfterBulkLoad()
    {
        ScanBeacons();
        _liquids.WakeMovable();
    }

    private static string AltarLockedMessage(string name) =>
        $"The {name} hums behind a ward no drill can break, not until the planet's crust fractures.";

    private static string AltarNeedsBreakerMessage(string name) =>
        $"The {name}'s ward shrugs off the drill. Only a core breaker (or something stronger) can crack it.";

    /// <summary>The three hardmode ores an altar smash seeds, in order (the cycle repeats).</summary>
    public static readonly TileId[] AltarOreCycle = [TileId.ViridiumOre, TileId.LumeniteOre, TileId.VoidsteelOre];

    /// <summary>Fraction of the first smash's veins seeded by smash number <paramref name="smash"/> (1-based): each full cycle yields less.</summary>
    public static double AltarYieldFactor(int smash) => Math.Max(0.35, 1.0 - 0.2 * ((smash - 1) / AltarOreCycle.Length));

    /// <summary>
    /// Breaks a tile as if mined (drops, loot, hearts, altars), ignoring reach and tools. Debug and
    /// test entry point; altars still refuse before the Fracture. Returns true when the tile broke.
    /// </summary>
    public bool ForceBreakTile(GridPoint point)
    {
        if (!World.Contains(point))
            return false;
        var tile = World.GetCell(point).TileId;
        if (tile is TileId.Air or TileId.Bedrock)
            return false;
        if (BlightRules.IsAltar(tile) && !FractureActive)
        {
            PostMessage(AltarLockedMessage(Tiles[tile].Name));
            return false;
        }
        World.SetTile(point, TileId.Air);
        _directDrops = true; // debug/test breaks deliver straight to the inventory
        try
        {
            if (!HandleBrokenTile(point, tile))
                PostMessage($"Mined {Tiles[tile].Name}.");
        }
        finally
        {
            _directDrops = false;
        }
        return true;
    }

    /// <summary>Drops and special effects for a tile that just broke at <paramref name="point"/>. True when it posted its own message.</summary>
    private bool HandleBrokenTile(GridPoint point, TileId brokenTile)
    {
        var outer = _groundDrops;
        _groundDrops = true; // broken blocks drop as ground items (magnet pickup), not straight into the pack
        _tileHits.Remove(point);
        try
        {
            return HandleBrokenTileCore(point, brokenTile);
        }
        finally
        {
            _groundDrops = outer;
        }
    }

    private bool HandleBrokenTileCore(GridPoint point, TileId brokenTile)
    {
        var cleared = new List<GridPoint> { point };
        var special = true;
        if (BlightRules.IsHeart(brokenTile))
        {
            ClearConnected(point, brokenTile, cleared);
            BreakHeart(brokenTile);
        }
        else if (BlightRules.IsAltar(brokenTile))
        {
            ClearConnected(point, brokenTile, cleared);
            SmashAltar(brokenTile);
        }
        else if (brokenTile == TileId.SupplyCache)
        {
            GrantLoot("supply cache", CacheLoot, rolls: 3);
        }
        else if (brokenTile == TileId.SalvagePod)
        {
            GrantLoot("salvage pod", PodLoot, rolls: 1 + _lootRandom.Next(2));
        }
        else if (HandleBrokenPhysicsTile(point, brokenTile))
        {
            special = true;
        }
        else if (HandleBrokenContentTile(point, brokenTile))
        {
            special = false;
        }
        else
        {
            special = false;
            if (TileDropTable.GetDrop(brokenTile) is { } drop)
                CollectOrDrop(drop, 1, new System.Numerics.Vector2(point.X + 0.5f, point.Y + 0.5f));
            if (TileDropTable.GetByproduct(brokenTile) is { } shard && _lootRandom.Next(3) == 0)
                CollectOrDrop(shard, 1, new System.Numerics.Vector2(point.X + 0.5f, point.Y + 0.5f));
            if (brokenTile == TileId.LifeCore)
            {
                special = true;
                PostMessage("You pry a pulsing life core from the rock. Use it to strengthen your body.");
            }
        }
        _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, cleared));
        return special;
    }

    /// <summary>Clears the rest of a multi-cell object (hearts and altars are 2×2).</summary>
    private void ClearConnected(GridPoint start, TileId tile, List<GridPoint> cleared)
    {
        var frontier = new Stack<GridPoint>();
        frontier.Push(start);
        while (frontier.Count > 0 && cleared.Count < 8)
        {
            var point = frontier.Pop();
            foreach (var next in new[]
                     {
                         new GridPoint(point.X + 1, point.Y), new GridPoint(point.X - 1, point.Y),
                         new GridPoint(point.X, point.Y + 1), new GridPoint(point.X, point.Y - 1)
                     })
            {
                if (!World.Contains(next) || World.GetCell(next).TileId != tile ||
                    Math.Abs(next.X - start.X) > 1 || Math.Abs(next.Y - start.Y) > 1)
                    continue;
                World.SetTile(next, TileId.Air);
                cleared.Add(next);
                frontier.Push(next);
            }
        }
    }

    private void BreakHeart(TileId heart)
    {
        _heartsBroken++;
        var radiated = heart == TileId.ReactorCore;
        var name = Tiles[heart].Name;
        var loot = new List<(ItemId Item, int Count)>();
        if (radiated)
        {
            loot.Add((ItemId.CausticShard, 3 + _lootRandom.Next(3)));
            loot.Add((ItemId.UraniumOre, 5 + _lootRandom.Next(6)));
            loot.Add((ItemId.Arrow, 40));
            loot.Add((((_heartsBroken - 1) % 3) switch
            {
                0 => ItemId.PlasmaPistol,
                1 => ItemId.RadFilterMask,
                _ => ItemId.SniperRifle
            }, 1));
        }
        else
        {
            loot.Add((ItemId.Berry, 6));
            loot.Add((ItemId.RestorativeTonic, 2));
            // Viridium only after the Fracture; before it the heart gives the easy-tier cobalt instead (G1).
            loot.Add((FractureActive ? ItemId.ViridiumOre : ItemId.CobaltOre, 4 + _lootRandom.Next(5)));
            loot.Add((((_heartsBroken - 1) % 3) switch
            {
                0 => ItemId.GrenadeLauncher,
                1 => ItemId.HearthCharm,
                _ => ItemId.Trailcoat
            }, 1));
        }
        foreach (var (item, count) in loot)
            Player.Inventory.Add(item, count);
        PostMessage($"The {name} cracks open: {DescribeLoot(loot)}. ({_heartsBroken} broken)");
        OnHeartBrokenContent();

        if (_heartsBroken % HeartsPerBoss == 0)
        {
            var boss = SpawnBoss(radiated ? RadiatedHeartAvenger : EnemyKind.PlantOvergrowth);
            PostMessage($"The blight screams. The {boss.DisplayName} rises to avenge its hearts!");
        }
        else
        {
            PostMessage($"The blight shudders. {HeartsPerBoss - _heartsBroken % HeartsPerBoss} more and something will answer.");
        }
    }

    /// <summary>
    /// The boss that avenges radiated hearts: the easy-tier Fallout Hive-mind before the Fracture, the hard-tier
    /// Cyber-Swarm Leader after it (G1).
    /// </summary>
    public EnemyKind RadiatedHeartAvenger => FractureActive ? EnemyKind.CyberSwarmLeader : EnemyKind.FalloutHivemind;

    private void SmashAltar(TileId altar)
    {
        _altarsSmashed++;
        var ore = AltarOreCycle[(_altarsSmashed - 1) % AltarOreCycle.Length];
        _lastAltarOre.Clear();
        var changed = new List<GridPoint>();
        var veins = Math.Max(1, (int)Math.Round(Math.Clamp(World.Count / 40000, 3, 24) * AltarYieldFactor(_altarsSmashed)));
        for (var attempt = 0; attempt < veins * 200 && _lastAltarOre.Count < veins; attempt++)
        {
            var x = _worldRandom.Next(1, World.Width - 1);
            var y = _worldRandom.Next((int)(World.Height * 0.35), World.Height - 2);
            if (World.TileAt(x, y) is not (TileId.Rock or TileId.CorruptRock or TileId.OvergrowthRock or TileId.EmberRock or TileId.IrradiatedRock))
                continue;
            _lastAltarOre.Add(new GridPoint(x, y));
            var steps = _worldRandom.Next(6, 12);
            for (var step = 0; step < steps; step++)
            {
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) > 1 && _worldRandom.Next(2) == 0)
                        continue;
                    var px = x + dx;
                    var py = y + dy;
                    if (py >= World.Height - 1 ||
                        World.TileAt(px, py) is not (TileId.Rock or TileId.CorruptRock or TileId.OvergrowthRock or TileId.EmberRock or TileId.IrradiatedRock))
                        continue;
                    var point = new GridPoint(px, py);
                    World.SetTile(point, ore);
                    changed.Add(point);
                }
                x = Math.Clamp(x + _worldRandom.Next(-1, 2), 1, World.Width - 2);
                y = Math.Clamp(y + _worldRandom.Next(-1, 2), 1, World.Height - 2);
            }
        }
        _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, changed));
        if (ore == TileId.VoidsteelOre && changed.Count > 0)
            Flags.Set("ore.voidsteel");
        OnAltarSmashedContent();
        PostMessage($"The {Tiles[altar].Name} shatters! Fresh {Tiles[ore].Name.ToLowerInvariant()} veins surge through the deep rock.");
        SpawnCoreSpirit();
    }

    private static readonly (ItemId Item, int Min, int Max, int Weight)[] CacheLoot =
    [
        (ItemId.Berry, 4, 8, 5), (ItemId.Arrow, 30, 80, 5), (ItemId.CopperBar, 3, 8, 4), (ItemId.EnergyCrystal, 1, 3, 3),
        (ItemId.RestorativeTonic, 1, 3, 3), (ItemId.ManaTonic, 1, 2, 2), (ItemId.PurifierGel, 10, 30, 2),
        (ItemId.CobaltOre, 3, 8, 2), (ItemId.LifeCore, 1, 1, 1), (ItemId.PlasmaPistol, 1, 1, 1), (ItemId.Trailboots, 1, 1, 1),
        // Content expansion: credits, ammo, class gear and accessories.
        (ItemId.Credits, 20, 80, 4), (ItemId.FerriteBar, 3, 8, 3), (ItemId.SlugRounds, 30, 80, 3), (ItemId.MedStim, 1, 3, 2),
        (ItemId.ChargeCellSmall, 2, 5, 2), (ItemId.ScrapPistol, 1, 1, 1), (ItemId.SparkEmitter, 1, 1, 1), (ItemId.ScoutDroneRemote, 1, 1, 1),
        (ItemId.BoneClub, 1, 1, 1), (ItemId.GrappleHook, 1, 1, 1), (ItemId.LuckyCharm, 1, 1, 1), (ItemId.DepthMeter, 1, 1, 1),
        (ItemId.RubyShard, 1, 2, 1), (ItemId.SapphireShard, 1, 2, 1), (ItemId.EmeraldShard, 1, 2, 1), (ItemId.TopazShard, 1, 2, 1),
        (ItemId.ScrapCrate, 1, 1, 1),
        // Arsenal pass: cache-only finds.
        (ItemId.HuntersRecurve, 1, 1, 1), (ItemId.ProspectorPick, 1, 1, 1)
    ];

    private static readonly (ItemId Item, int Min, int Max, int Weight)[] PodLoot =
    [
        (ItemId.Berry, 1, 3, 5), (ItemId.Arrow, 10, 30, 5), (ItemId.CopperOre, 2, 6, 4), (ItemId.ScrapIronOre, 2, 6, 3),
        (ItemId.CopperBar, 1, 3, 2), (ItemId.EnergyCrystal, 1, 1, 2), (ItemId.RestorativeTonic, 1, 1, 1),
        (ItemId.Credits, 5, 30, 3), (ItemId.SlugRounds, 10, 30, 2), (ItemId.BioGel, 1, 3, 2), (ItemId.CircuitBoard, 1, 2, 1),
        (ItemId.PowerCell, 1, 1, 1), (ItemId.Worm, 1, 3, 1), (ItemId.RopeCoil, 1, 2, 1)
    ];

    /// <summary>Every item a supply cache or salvage pod can hold (crafting audit).</summary>
    internal static IEnumerable<ItemId> SupplyLootItems => CacheLoot.Concat(PodLoot).Select(entry => entry.Item);

    private void GrantLoot(string container, (ItemId Item, int Min, int Max, int Weight)[] table, int rolls)
    {
        var total = table.Sum(entry => entry.Weight);
        var granted = new List<(ItemId Item, int Count)>();
        for (var roll = 0; roll < rolls; roll++)
        {
            var pick = _lootRandom.Next(total);
            foreach (var entry in table)
            {
                pick -= entry.Weight;
                if (pick >= 0)
                    continue;
                var count = _lootRandom.Next(entry.Min, entry.Max + 1);
                Player.Inventory.Add(entry.Item, count);
                granted.Add((entry.Item, count));
                break;
            }
        }
        PostMessage($"The {container} held {DescribeLoot(granted)}.");
    }

    private string DescribeLoot(IEnumerable<(ItemId Item, int Count)> loot) =>
        string.Join(", ", loot.Select(entry => entry.Count == 1 ? Items[entry.Item].Name : $"{entry.Count} {Items[entry.Item].Name}"));

    /// <summary>Turns blight within <paramref name="radius"/> of <paramref name="center"/> back into natural ground. Returns tiles converted.</summary>
    public int PurifyArea(GridPoint center, float radius)
    {
        var changed = new List<GridPoint>();
        var reach = (int)MathF.Ceiling(radius);
        for (var y = center.Y - reach; y <= center.Y + reach; y++)
        for (var x = center.X - reach; x <= center.X + reach; x++)
        {
            if (!World.Contains(x, y) || (x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y) > radius * radius)
                continue;
            var exposed = !Tiles[World.TileAt(x, y - 1, TileId.Air)].IsSolid;
            if (BlightRules.Purify(World.TileAt(x, y), exposed) is not { } natural)
                continue;
            var point = new GridPoint(x, y);
            World.SetTile(point, natural);
            changed.Add(point);
        }
        if (changed.Count > 0)
            _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, changed));
        return changed.Count;
    }

    /// <summary>Consumes a carried life core for +10 maximum hit points (up to ten cores). Returns true when used.</summary>
    public bool UseLifeCore()
    {
        if (Player.LifeCoresUsed >= PlayerCharacter.MaximumLifeCores)
        {
            PostMessage("Your body cannot hold any more life cores.");
            return false;
        }
        if (!Player.Inventory.TryConsume(ItemId.LifeCore))
        {
            PostMessage("No life core to use.");
            return false;
        }
        Player.LifeCoresUsed++;
        Player.Heal(PlayerCharacter.LifeCoreBonus);
        PostMessage($"The life core fuses with you. Maximum health is now {Player.MaximumHitPoints:0}.");
        return true;
    }

    /// <summary>
    /// The Fracture: a blight scar and a mirrored prism scar tear through the world in a V, altars unlock (for the
    /// core breaker), and both blight and prism spread into every convertible tile, faster, underground too.
    /// Returns false if it already happened.
    /// </summary>
    public bool TriggerFracture()
    {
        if (FractureActive)
            return false;
        FractureActive = true;
        _ticker.Fractured = true;
        PostMessage("The planet's crust fractures! The blight tears across the land and the altars' wards fail.");
        Record("The Fracture tore the surface open.");
        OnFractureContent();
        var blight = Blight;
        var changed = new List<GridPoint>();
        var prismChanged = 0;
        var halfWidth = Math.Clamp(World.Width / 140, 2, 9);
        // Right arm: the world's blight. Left arm: its mirror image in prism. Together they cut a V.
        foreach (var (startFraction, direction) in new[] { (0.22, 1), (0.78, -1) })
        {
            var prismArm = direction > 0;
            if (!prismArm && blight is null)
                continue;
            var startX = (int)(World.Width * startFraction);
            var startY = FindSurface(Math.Clamp(startX, 0, World.Width - 1)) - 2;
            for (var y = Math.Max(0, startY); y < World.Height - 1; y++)
            {
                var centerX = startX + (int)Math.Round((y - startY) * 0.9 * direction);
                if (centerX < -halfWidth || centerX >= World.Width + halfWidth)
                    break;
                for (var x = centerX - halfWidth; x <= centerX + halfWidth; x++)
                {
                    // Purifier beacons shelter their reach from both scars.
                    if (!World.Contains(x, y) || _ticker.IsNearBeacon(x, y))
                        continue;
                    if (prismArm)
                    {
                        var exposed = !Tiles[World.TileAt(x, y - 1, TileId.Air)].IsSolid;
                        if (CrystalRules.Convert(World.TileAt(x, y), exposed) is not { } prism)
                            continue;
                        World.SetTile(new GridPoint(x, y), prism);
                        changed.Add(new GridPoint(x, y));
                        prismChanged++;
                        continue;
                    }
                    if (BlightRules.Convert(World.TileAt(x, y), blight!.Value) is not { } blighted)
                        continue;
                    var point = new GridPoint(x, y);
                    World.SetTile(point, blighted);
                    changed.Add(point);
                }
            }
        }
        FractureCellsConverted = changed.Count - prismChanged;
        FracturePrismCells = prismChanged;
        // Reframe only around the converted cells (never the whole world).
        if (changed.Count > 0)
            _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, changed));
        return true;
    }
}
