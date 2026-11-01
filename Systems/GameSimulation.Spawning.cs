using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Natural creature spawning and leaving (MECHANICS_PARITY.md C9, C10, C12): spawns land just off-screen and never
/// inside the safe radius; rate and cap rise at night, underground, in the blight and during events, and fall near
/// a populated town or while a boss lives; far creatures wander off (bosses and event units stay); bosses leave
/// when the player dies, and by their own leave rules (BO-6, GameSimulation.BossSpawn.cs). Tuning lives in <see cref="CombatTuning"/>.
/// </summary>
public sealed partial class GameSimulation
{
    private const float SpawnIntervalSeconds = 0.65f;
    private readonly Dictionary<long, float> _farSeconds = new();

    /// <summary>Live town NPCs (not bound survivors) close enough to calm the player's surroundings.</summary>
    public int TownNpcsCalmingSpawns => _townNpcs.Count(npc => !npc.IsBound &&
        Vector2.Distance(npc.Motion.Position, Player.Motion.Position) <= CombatTuning.TownCalmRadius);

    private bool PlayerInBlight(BiomeZone zone) =>
        zone == BiomeZone.Overgrowth || zone == BiomeZone.RadiatedDeeps && Blight == WorldBlight.Radiated;

    /// <summary>
    /// The running events' spawn modifiers (EV-2, D-28): the product of every active <see cref="WorldEventSpec"/>'s
    /// multipliers, from <see cref="WorldEventSystem"/> alone (the legacy event manager is gone).
    /// </summary>
    public (float Spawn, float Population, bool NoHostiles, bool Shelter) EventSpawnFactors => _worldEvents.Modifiers;

    /// <summary>Spawn rate and cap multipliers for where the player stands right now.</summary>
    public (float Rate, float Cap) CurrentSpawnFactors()
    {
        var reading = BiomeScanner.Scan(World, PlayerCell());
        return CombatTuning.SpawnFactors(IsNight, reading.Underground, PlayerInBlight(reading.Zone), TownNpcsCalmingSpawns,
            _enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive), FractureActive, Player.Statuses.IsActive(StatusId.Battle),
            Player.Statuses.IsActive(StatusId.Calm));
    }

    /// <summary>
    /// The chance one spawn attempt (every <see cref="SpawnIntervalSeconds"/>) succeeds: Terraria's per-tick roll over the
    /// interval, at <see cref="CombatTuning.SpawnsPerSecond"/> times the rate multiplier (0.3.4, EN-8).
    /// </summary>
    public static float SpawnAttemptChance(float rateMultiplier) =>
        1f - MathF.Exp(-CombatTuning.SpawnsPerSecond * Math.Max(0f, rateMultiplier) * SpawnIntervalSeconds);

    private void UpdateSpawning(float deltaSeconds)
    {
        _playerDeadSeconds = 0f;
        UpdateDespawning(deltaSeconds);
        UpdateBossLeaving(deltaSeconds); // BO-6: lost targets and left biomes (dawn and dusk run on the clock)
        _spawnTimer += deltaSeconds;
        if (_spawnTimer < SpawnIntervalSeconds)
            return;
        _spawnTimer %= SpawnIntervalSeconds;

        var content = EventSpawnFactors; // EV-2: the one event system's multipliers
        // One biome scan per spawn attempt decides which roster this attempt draws from.
        var reading = BiomeScanner.Scan(World, PlayerCell());
        var (rateFactor, capFactor) = CombatTuning.SpawnFactors(IsNight, reading.Underground, PlayerInBlight(reading.Zone),
            TownNpcsCalmingSpawns, _enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive), FractureActive,
            Player.Statuses.IsActive(StatusId.Battle), Player.Statuses.IsActive(StatusId.Calm));
        var spawnMultiplier = content.Spawn * rateFactor *
                              (Weather.Dust > 0.4f && CurrentBiome.Zone == BiomeZone.JunkWasteland ? 1.3f : 1f);
        var limit = Math.Clamp((int)MathF.Ceiling(CombatTuning.BasePopulation * content.Population * capFactor), 1, 24);
        var population = _enemies.Count(enemy => !enemy.IsBoss && enemy.IsAlive);
        if (population >= limit ||
            _random.NextDouble() >= SpawnAttemptChance(spawnMultiplier * CombatTuning.SpawnRamp(population, limit)))
            return;

        for (var attempt = 0; attempt < 14; attempt++)
        {
            var side = _random.Next(2) == 0 ? -1 : 1;
            var offset = _random.Next((int)SafeSpawnHalfWidth, (int)SafeSpawnHalfWidth + 18);
            var x = (int)MathF.Floor(Player.Motion.Position.X) + side * offset;
            if (x < 2 || x >= World.Width - 2 || Math.Abs(x - Player.Motion.Position.X) < ViewHalfWidth)
                continue;
            if (!TryFindSpawnGround(x, reading.Underground, out var groundY))
                continue;
            // Cave spawns need a roofed (dark) pocket rather than open sky.
            if (reading.Underground && !HasRoof(x, groundY - 1))
                continue;

            var isNight = ClockHour >= 18f || ClockHour < 6f;
            var roll = _random.NextDouble();
            var underground = Player.Motion.Position.Y > World.Height * 0.45f;
            var groundTile = World.GetCell(new GridPoint(x, groundY)).TileId;
            var sporeGround = groundTile is TileId.FungalSoil or TileId.JungleGrass;
            // Context bands: night fliers, spore rats on fungal/vine ground or at night,
            // mites and droids below, sky-rays by day, and a rare astro-beast on the surface.
            var grassland = reading.Zone == BiomeZone.Grassland;
            // Inside the precursor dungeon (in front of its wall) the dungeon's own roster replaces the biome's.
            var inDungeon = World.GetCell(PlayerCell()).WallId == WallIds.Dungeon;
            var kind = inDungeon ? BiomeSpawnTables.PickDungeon(roll, isNight)
                : BiomeSpawnTables.UsesTable(reading.Zone) ? BiomeSpawnTables.Pick(reading.Zone, roll, isNight, FractureActive)
                : FractureActive && roll < BiomeSpawnTables.HardShare
                    ? BiomeSpawnTables.Pick(underground ? BiomeZone.Caverns : BiomeZone.Grassland, roll, isNight, hardmode: true) : roll switch
            {
                // Daytime grassland wildlife takes the top of the roll.
                >= 0.93 when grassland && !isNight && !underground => roll < 0.97 ? EnemyKind.NeoTerraDeer : EnemyKind.SkyHawk,
                < 0.18 when isNight => EnemyKind.CaveBat,
                < 0.30 when isNight => EnemyKind.Wraith,
                < 0.40 when isNight || sporeGround => EnemyKind.SporeRat,
                < 0.48 when underground => EnemyKind.RockGolem,
                < 0.60 when underground => EnemyKind.RockMite,
                < 0.70 when !isNight && !underground => EnemyKind.SkyRay,
                < 0.75 when !underground => EnemyKind.AstroBeast,
                < 0.84 => EnemyKind.CaveMoth,
                _ => EnemyKind.MossSlime
            };
            kind = BiomeSpawnTables.ContentFill(reading, inDungeon, underground, isNight, FractureActive, roll, kind); // content fill
            kind = CoverageSpawn(reading, inDungeon, isNight, roll, kind); // creature coverage: sky, junk/frost underground, grave field, late dungeon
            WorldEventKind? eventTag = null;
            if (_worldEvents.PickSpawn(roll) is { } eventKind)
            {
                kind = HardmodeEventCreature(eventKind, roll);
                eventTag = _worldEvents.Active.FirstOrDefault(state =>
                    _worldEvents.Spec(state.Kind).Roster.Any(entry => entry.Kind == eventKind))?.Kind;
            }
            else if (content.NoHostiles && !EnemyArchetypes.Get(kind).Passive)
                return;
            var archetype = EnemyArchetypes.Get(kind);
            var lift = kind is EnemyKind.SkyRay or EnemyKind.GeigerBug or EnemyKind.SalvageDrone or EnemyKind.SkyHawk || (archetype.Flying && kind > EnemyKind.SkyHawk) ? 3f : 0f;
            var position = new Vector2(x, groundY - archetype.Size.Y - lift);
            if (eventTag is null && reading.Zone == BiomeZone.AcidCoast && !reading.Underground)
            {
                // Ocean rule: swimmers need open water to appear in; shore life keeps to the dry beach; fliers skim the waves.
                if (BiomeSpawnTables.IsSwimmer(kind))
                {
                    if (!TryFindSwimSpot(x, archetype.Size.Y, out var swimTop))
                        continue;
                    position = new Vector2(x, swimTop);
                }
                else if (World.GetCell(new GridPoint(x, groundY - 1)).LiquidUnits > 0)
                {
                    if (!archetype.Flying)
                        continue;
                    position = new Vector2(x, WaterSurfaceRow(x) - archetype.Size.Y - lift);
                }
            }
            else if (BiomeSpawnTables.IsSwimmer(kind) && eventTag is null)
            {
                // A swimmer rolled outside the ocean (another table or roster) still needs water, or it does not appear.
                if (!TryFindSwimSpot(x, archetype.Size.Y, out var swimTop))
                    continue;
                position = new Vector2(x, swimTop);
            }
            // Every spawn needs open room for its body (flyers need clear air where they appear).
            if (IsSolidActorPosition(position, archetype.Size))
                continue;
            var enemy = new EnemyAgent(_nextEntityId++, kind, position) { EventTag = eventTag };
            _enemies.Add(enemy);
            PostMessage($"{WithArticle(archetype.DisplayName)} emerged beyond the safe distance.");
            return;
        }
    }

    /// <summary>
    /// Finds a body of water or sea fuel in column <paramref name="x"/> at least three cells deep and returns a top row for a swimmer
    /// of <paramref name="height"/> placed inside it (below the surface, above the bed).
    /// </summary>
    private bool TryFindSwimSpot(int x, float height, out float top)
    {
        top = 0f;
        var run = 0;
        for (var y = 1; y < World.Height - 1; y++)
        {
            var cell = World.GetCell(new GridPoint(x, y));
            var water = cell.LiquidKind is LiquidKind.Water or LiquidKind.Fuel && cell.LiquidDepth >= 0.9f && !Tiles[cell.TileId].IsSolid;
            if (water)
            {
                run++;
                continue;
            }
            if (run >= Math.Max(3, (int)MathF.Ceiling(height) + 2))
            {
                var bed = y;
                var surface = bed - run;
                var room = run - (int)MathF.Ceiling(height) - 1;
                top = surface + 1 + _random.Next(Math.Max(1, room));
                return true;
            }
            run = 0;
        }
        return false;
    }

    /// <summary>The first row from the sky in column <paramref name="x"/> holding liquid or solid ground.</summary>
    private int WaterSurfaceRow(int x)
    {
        for (var y = 1; y < World.Height - 1; y++)
        {
            var cell = World.GetCell(new GridPoint(x, y));
            if (cell.LiquidUnits > 0 || Tiles[cell.TileId].IsSolid)
                return y;
        }
        return World.Height - 1;
    }

    /// <summary>True when some solid tile covers the column above <paramref name="y"/> (a cave, not open sky).</summary>
    private bool HasRoof(int x, int y)
    {
        for (var above = y - 1; above >= 0; above--)
            if (Tiles[World.GetCell(new GridPoint(x, above)).TileId].IsSolid)
                return true;
        return false;
    }

    /// <summary>Whether a creature may wander off when far away: not bosses, event or raid units, or world-placed ambushers.</summary>
    private bool CanDespawn(EnemyAgent enemy) =>
        !enemy.IsBoss && enemy.EventTag is null && !_raidEnemyIds.Contains(enemy.Id) && !enemy.Archetype.Stationary &&
        !enemy.Disguised && enemy.Archetype.Family != EnemyAiFamily.Mimic;

    /// <summary>Creatures that stay far from the player for a few seconds leave the world.</summary>
    private void UpdateDespawning(float deltaSeconds)
    {
        if (_enemies.Count == 0)
        {
            _farSeconds.Clear();
            return;
        }
        var center = PlayerCenter;
        List<EnemyAgent>? leaving = null;
        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive || !CanDespawn(enemy) ||
                Vector2.Distance(enemy.Motion.Position + enemy.Motion.Size / 2f, center) < CombatTuning.DespawnDistance)
            {
                _farSeconds.Remove(enemy.Id);
                continue;
            }
            var far = _farSeconds.GetValueOrDefault(enemy.Id) + deltaSeconds;
            _farSeconds[enemy.Id] = far;
            if (far >= CombatTuning.DespawnSeconds)
                (leaving ??= new List<EnemyAgent>()).Add(enemy);
        }
        if (leaving is null)
            return;
        foreach (var enemy in leaving)
        {
            RecordDeath(enemy, DeathReason.Despawned); // PE-5: leaving is silent (Systems/SoundEvents.cs)
            _enemies.Remove(enemy);
            _farSeconds.Remove(enemy.Id);
        }
    }

    /// <summary>Seconds the player must stay dead before living bosses give up and leave.</summary>
    public const float BossLeaveAfterDeathSeconds = 3f;
    private float _playerDeadSeconds;

    /// <summary>Called every dead tick: once the player has been down a moment, every boss leaves.</summary>
    private void BossesLeaveOnPlayerDeath(float deltaSeconds)
    {
        _playerDeadSeconds += deltaSeconds;
        if (_playerDeadSeconds < BossLeaveAfterDeathSeconds)
            return;
        foreach (var boss in _enemies.Where(enemy => enemy.IsBoss && enemy.IsAlive).ToArray())
            BossLeaves(boss, "it loses interest");
    }

    // ---- test probes ----
    internal void ProbeAdvanceHours(float hours) => AdvanceClock(hours);
    internal void ProbeSpawnTick(float deltaSeconds) => UpdateSpawning(deltaSeconds);
}
