using System.Numerics;
using Rimtera.AI;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

public enum PlayerAction : byte
{
    None,
    Mine,
    UseSelected,
    Eat,
    ReplaceSelected,
    /// <summary>Tend the player's own wounds at reduced quality.</summary>
    TendSelf,
    /// <summary>Interact with the target cell (doors, containers, survivors, town NPCs).</summary>
    Interact,
    /// <summary>Sleep in a nearby bed at night (time runs faster).</summary>
    Sleep
}

public readonly record struct GameInput(
    MovementInput Movement,
    PlayerAction Action = PlayerAction.None,
    GridPoint? Target = null);

public readonly record struct GameTickResult(
    long Tick,
    float ClockHour,
    int Day,
    string LastMessage,
    ActivityChoice AutonomousActivity);

/// <summary>
/// Owns the fixed-step sandbox simulation and composes world, movement, inventory,
/// combat, autonomous actors, survival needs, events, and liquid updates.
/// </summary>
public sealed partial class GameSimulation : IStoryHost
{
    /// <summary>
    /// The clock: a day (06:00–18:00) takes 900 real seconds and a night 540 (with <see cref="NightClockPace"/>), so a whole
    /// cycle is Terraria's 24 real minutes. Only the time of day and what hangs on it (crops, arrivals, events, the
    /// storyteller) run on this clock.
    /// </summary>
    public const float GameHoursPerRealSecond = 1f / 75f;

    /// <summary>
    /// Vital hours: the unit statuses, regeneration, health, colonist needs and exposure are measured in. One vital hour is
    /// 12.5 real seconds, always (asleep or awake), so buffs and regen keep their real-time pace whatever the clock does.
    /// </summary>
    public const float VitalHoursPerRealSecond = 0.08f;
    /// <summary>Slow "story tick" cadence for rooms, temperature, comfort, and the biome reading.</summary>
    public const int EnvironmentIntervalTicks = 60;
    private const float PlayerReach = 5f;
    private const float ViewHalfWidth = 18f;
    private const float SafeSpawnHalfWidth = 22f;
    private const string IncursionEventId = "hostile-incursion";
    private const string EasyBossDefeatedTag = "easy-boss-defeated";

    private readonly Random _random;
    // Separate stream so boss choices never shift the ambient spawn sequence.
    private readonly Random _bossRandom;
    private readonly HashSet<EnemyKind> _defeatedBosses = new();
    private readonly PawnMovementSystem _playerMovement = new(PlayerMovementSettings);
    private readonly PawnMovementSystem _enemyMovement = new(new PawnMovementSettings
    {
        GroundAcceleration = 18f,
        AirAcceleration = 10f,
        MaximumRunSpeed = 2.7f,
        GroundFriction = 20f,
        AirFriction = 3f,
        Gravity = 24f,
        TerminalFallSpeed = 16f,
        JumpImpulse = 8.5f,
        DashSpeed = 8f,
        DashDuration = 0.1f,
        WingLiftAcceleration = 12f,
        MaximumWingRiseSpeed = 5f,
        WingTerminalFallSpeed = 4f,
        CollisionSubstep = 0.18f
    });
    private readonly PlacementSystem _placement = new();
    private readonly TileFramingSystem _framing = new();
    private readonly EventManager _eventManager;
    private readonly PawnMovementSystem _colonistMovement = new(new PawnMovementSettings
    {
        GroundAcceleration = 20f,
        AirAcceleration = 10f,
        MaximumRunSpeed = 2.1f,
        GroundFriction = 22f,
        AirFriction = 3f,
        Gravity = 24f,
        TerminalFallSpeed = 16f,
        JumpImpulse = 8.5f,
        DashSpeed = 6f,
        DashDuration = 0.1f,
        WingLiftAcceleration = 12f,
        MaximumWingRiseSpeed = 5f,
        WingTerminalFallSpeed = 4f,
        CollisionSubstep = 0.18f
    });
    private readonly List<EnemyAgent> _enemies = new();
    private readonly List<ColonistAgent> _colonists = new();
    private readonly List<ProjectileState> _projectiles = new();
    private readonly Queue<string> _recentMessages = new();
    private long _nextEntityId = 1;
    private float _spawnTimer;
    private float _liquidTimer;
    private float _elapsedGameHours;
    private ActivityChoice _autonomousActivity = new(ActivityKind.Idle, 0f, "not evaluated");
    private string? _selectedRecipeId;
    // Separate stream for mining by-products so loot never shifts spawns or combat rolls.
    private readonly Random _lootRandom;
    private readonly RegionMap _regions;
    private readonly TemperatureModel _temperature = new();
    private readonly Storyteller _storyteller;
    private readonly Dictionary<RoomInfo, BiomeReading> _roomBiomes = new();
    private int _roomBiomeBuild = -1;
    private float _environmentHours;
    private long _lastStoryHour;
    private bool _exposureWarned;
    private readonly ColonySocialSystem _social;
    // Separate stream for minds (breaks, tending, tantrums) so it never shifts spawns or combat rolls.
    private readonly Random _mindRandom;
    private readonly HashSet<int> _deathsHandled = new();
    private readonly HashSet<long> _raidEnemyIds = new();
    private bool _playerEdgeWarned;
    private bool _playerDownedWarned;

    static GameSimulation()
    {
        BiomeScanner.RegisterBiomeTile(BiomeZone.FrozenWasteland, TileId.GlacialOre);
        BiomeScanner.RegisterBiomeTile(BiomeZone.RadiatedDeeps, TileId.UraniumOre);
        BiomeScanner.RegisterBiomeTile(BiomeZone.MoltenChasm, TileId.PyrociteOre);
        BiomeScanner.RegisterBiomeTile(BiomeZone.JunkWasteland, TileId.ScrapIronOre);
        foreach (var tile in new[] { TileId.RadiatedSand, TileId.RadiatedIce, TileId.ReactorCore, TileId.RadAltar })
            BiomeScanner.RegisterBiomeTile(BiomeZone.RadiatedDeeps, tile);
        foreach (var tile in new[] { TileId.OvergrowthSoil, TileId.OvergrowthRock, TileId.OvergrowthSand, TileId.OvergrowthIce,
                     TileId.SporeHeart, TileId.RootAltar })
            BiomeScanner.RegisterBiomeTile(BiomeZone.Overgrowth, tile);
    }

    public GameSimulation(int seed = 48271, int width = 160, int height = 72, CharacterProfile? colonist = null,
        WorldHazard? hazard = null, IProgress<GenerationProgress>? progress = null, WorldBlight? blight = null)
        : this(seed, width, height, colonist, hazard, progress, blight, loaded: null)
    {
    }

    /// <summary>A world restored from saved cells and metadata (no generation).</summary>
    private sealed record LoadedWorld(WorldCell[] Cells, BiomeLayout Layout, WorldMetaSnapshot Meta);

    private GameSimulation(int seed, int width, int height, CharacterProfile? colonist, WorldHazard? hazard,
        IProgress<GenerationProgress>? progress, WorldBlight? blight, LoadedWorld? loaded)
    {
        Seed = seed;
        PlayerProfile = (colonist ?? CharacterDraft.CreateRandom(seed, "Adventurer")
            .ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow)).Normalized();
        _random = new Random(seed);
        _bossRandom = new Random(seed ^ 0x2B05E5A1);
        _lootRandom = new Random(seed ^ 0x10075EED);
        Tiles = new TileCatalog();
        Items = new ItemCatalog();
        Crafting = new CraftingSystem(Items);
        World = new TileGrid(width, height);
        _hazardChoice = hazard;
        var resolvedHazard = hazard is { } chosen ? WorldSettings.Resolve(chosen, seed) : (WorldHazard?)null;
        var generation = new WorldGenerationPipeline();
        if (loaded is not null)
        {
            // Saved worlds restore their cells and layout directly; nothing is regenerated.
            World.LoadCells(loaded.Cells);
            if (width >= DetailedGenerationMinimumWidth || blight is not null)
                Blight = WorldSettings.Resolve(blight ?? WorldBlight.Random, seed);
            BiomeLayout = loaded.Layout;
            Features = WorldMetaCodec.RestoreFeatures(loaded.Meta, Blight ?? WorldBlight.Radiated);
        }
        else if (width >= DetailedGenerationMinimumWidth || blight is not null)
        {
            // Detailed generator: large worlds and any world created with a blight.
            Blight = WorldSettings.Resolve(blight ?? WorldBlight.Random, seed);
            var detailed = new LargeWorldGenerator(width, height, Blight.Value, resolvedHazard) { Variant = SecretSeeds.Detect(seed) };
            detailed.AddPasses(generation);
            generation.Generate(World, Tiles, seed, progress);
            BiomeLayout = detailed.Layout!;
            Features = detailed.Features;
        }
        else
        {
            // Legacy small-world pipeline, unchanged so seeded worlds stay identical.
            generation.AddPass(new TerrainPass());
            generation.AddPass(new CavePass());
            generation.AddPass(new OreVeinPass());
            generation.AddPass(new DeepOrePass());
            generation.AddPass(new CrystalClusterPass());
            var biomePass = new BiomePass { Hazard = resolvedHazard };
            generation.AddPass(biomePass);
            generation.AddPass(new LiquidSettlingPass());
            generation.Generate(World, Tiles, seed, progress);
            BiomeLayout = biomePass.Layout!;
        }
        _framing.ReframeAll(World, Tiles);
        _regions = new RegionMap(World, Tiles);
        _liquids = new ActiveLiquidSimulator(World, Tiles);
        _liquids.WakeMovable();
        _ticker = new WorldTicker(World, Tiles, seed ^ 0x71C4E5) { Blight = Blight };
        InitPhysics();
        _worldRandom = new Random(seed ^ 0x0A17A125);
        World.CellChanged += OnWorldCellChanged;
        ScanBeacons();
        _storyteller = new Storyteller(seed);

        // Starting kit (StarterKits): Creative characters get their supply drop on first entry instead.
        var inventory = new Inventory(Items, PlayerInventorySlots, PlayerCoinSlots, PlayerAmmoSlots);
        var kitRandom = new Random(seed ^ 0x57A27E);
        if (PlayerProfile.Difficulty != CharacterDifficulty.Creative)
            foreach (var (item, count, calibrated) in StarterKits.Standard)
                inventory.Add(item, count, calibrated ? StarterKits.Roll(kitRandom, Items[item]) : Calibration.None);
        else
            inventory.Add(ItemId.Berry, 12);
        if (inventory.FindSlot(ItemId.FerriteStiletto) is var swordSlot and >= 0)
            inventory.Select(swordSlot);

        // The colony lands in the central Grassland, between the Frozen and Junk wastelands.
        var startX = Math.Clamp(Features?.SpawnColumn ?? width / 2, 2, width - 3);
        SpawnColumn = startX;
        var surfaceY = FindSurface(startX);
        var playerMotion = new PawnMotionState(new Vector2(startX, surfaceY - HumanoidBody.Size.Y), HumanoidBody.Size);
        Player = new PlayerCharacter(playerMotion, inventory, PlayerProfile, healthSeed: unchecked(seed ^ 0x4EA1F00D));
        AttachGrowth(); // character growth: sixth slot gate, lifebloom and upgrade stat bonuses
        Guide = new GuideNpc(new Vector2(startX + 2.5f, surfaceY - 0.8f));

        // As in Terraria, a new world starts with just you and the station AI (the Guide); crew arrive later, into housing.
        _social = new ColonySocialSystem(seed ^ 0x50C1A1E5);
        _mindRandom = new Random(seed ^ 0x6D1D5EED);
        ColonySocialSystem.SeedStartingRelations(ColonyPawns, seed);
        // The player has no brain: as in Terraria, the player character never eats, rests, digs or recovers on its own.
        // Crew colonists keep their own needs AI (UpdateColonists).
        Player.Brain = null;

        // Survivors wait, bound, inside the structures the detailed generator built.
        if (Features is { } features)
            for (var index = 0; index < features.SurvivorSpots.Count; index++)
            {
                var spot = features.SurvivorSpots[index];
                AddTownNpc(TownNpcCatalog.SurvivorKinds[index % TownNpcCatalog.SurvivorKinds.Length], StandingAt(spot), bound: true);
            }

        _eventManager = new EventManager(seed ^ 0x51ED270B);
        _eventManager.Register(new WorldEventDefinition(
            Id: "mothfall",
            Boundary: WorldEventBoundary.Dusk,
            ChancePerBoundary: 0.16f,
            DurationHours: 7f,
            SpawnAttemptMultiplier: 1.6f,
            PopulationCapMultiplier: 1.3f,
            DisplayName: "Moth-ray swarm"));
        _eventManager.Register(new WorldEventDefinition(
            Id: "deep-earth-awakening",
            Boundary: WorldEventBoundary.Dawn,
            ChancePerBoundary: 0.08f,
            DurationHours: 5f,
            SpawnAttemptMultiplier: 1.35f,
            PopulationCapMultiplier: 1.5f,
            RequiresProgression: true,
            DisplayName: "Deep-core tremors"));
        // Dusk incursions bring one event boss once an easy-tier boss has fallen.
        _eventManager.Register(new WorldEventDefinition(
            Id: IncursionEventId,
            Boundary: WorldEventBoundary.Dusk,
            ChancePerBoundary: 0.3f,
            DurationHours: 6f,
            RequiredBiomeTag: EasyBossDefeatedTag,
            DisplayName: "Hostile incursion"));

        ClockHour = 8f;
        Day = 1;
        CurrentBiome = BiomeScanner.Scan(World, PlayerCell());
        PlayerAmbientTemperature = AmbientAt(PlayerCenter, CurrentBiome);
        PlayerComfort = TemperatureModel.Evaluate(PlayerAmbientTemperature, Player.Stats.ImmunityTags);
        PostMessage($"{PlayerProfile.Name} touches down; the station AI flickers awake beside the landing site. Seed {seed}.");
        Record($"{PlayerProfile.Name} touched down with the station AI.");
        OnSimulationCreated(loaded is not null);
    }

    public TileCatalog Tiles { get; }
    public int Seed { get; }
    public ItemCatalog Items { get; }
    public CraftingSystem Crafting { get; }
    public TileGrid World { get; }
    public CharacterProfile PlayerProfile { get; }
    public PlayerCharacter Player { get; }
    public GuideNpc Guide { get; }
    public IReadOnlyList<ColonistAgent> Colonists => _colonists;
    public IReadOnlyList<EnemyAgent> Enemies => _enemies;
    public IReadOnlyList<ProjectileState> Projectiles => _projectiles;
    public IReadOnlyList<ActiveWorldEvent> ActiveEvents => _eventManager.ActiveEvents;
    public IReadOnlyList<string> RecentMessages => _recentMessages.ToArray();
    public long TickNumber { get; private set; }
    public int Day { get; private set; }
    public float ClockHour { get; private set; }
    public string LastMessage { get; private set; } = string.Empty;
    public string? SelectedRecipeId => _selectedRecipeId;
    public RecipeDefinition? SelectedRecipe => _selectedRecipeId is null
        ? null
        : Crafting.Recipes.FirstOrDefault(recipe => string.Equals(recipe.Id, _selectedRecipeId, StringComparison.OrdinalIgnoreCase));
    public ActivityChoice AutonomousActivity => _autonomousActivity;
    public bool IsOver => Player.IsDead;
    public IReadOnlyCollection<EnemyKind> DefeatedBosses => _defeatedBosses;

    /// <summary>The player character followed by every colonist (alive or dead), in social-key order.</summary>
    public IReadOnlyList<IColonyPawn> ColonyPawns => new IColonyPawn[] { Player }.Concat(_colonists).ToArray();
    /// <summary>Interaction driver (forced interactions for tools and tests).</summary>
    public ColonySocialSystem Social => _social;
    /// <summary>In-game hours since landing; the clock relations and social memories use.</summary>
    public float ElapsedGameHours => _elapsedGameHours;
    /// <summary>How one pawn feels about every other colony member (opinion, relation label, compatibility).</summary>
    public IReadOnlyList<OpinionView> GetOpinions(IColonyPawn pawn) => SocialRules.GetOpinions(pawn, ColonyPawns);
    public IColonyPawn? FindPawn(int socialKey) => ColonyPawns.FirstOrDefault(pawn => pawn.SocialKey == socialKey);

    /// <summary>Seeded biome geography of this world (borders, chasm depth, radiated pockets).</summary>
    public BiomeLayout BiomeLayout { get; }
    /// <summary>The player's biome, refreshed every <see cref="EnvironmentIntervalTicks"/> ticks.</summary>
    public BiomeReading CurrentBiome { get; private set; }
    /// <summary>Region/room map over open cells; rebuilds lazily after tile edits.</summary>
    public RegionMap Regions => _regions;
    public Storyteller Storyteller => _storyteller;
    /// <summary>Most recent incidents (oldest first, up to five) for the HUD.</summary>
    public IReadOnlyList<StoryIncidentRecord> IncidentLog => _storyteller.RecentIncidents;
    /// <summary>Current storyteller threat budget.</summary>
    public float ThreatPoints => _storyteller.LastThreatPoints;
    public bool SolarFlareActive => _storyteller.SolarFlareActive;
    public bool ToxicFalloutActive => _storyteller.FalloutActive;
    /// <summary>Active cold-snap (negative) or heat-wave (positive) offset in °C.</summary>
    public float OutdoorTemperatureOffset => _storyteller.TemperatureOffset;
    /// <summary>Outdoor air temperature in °C where the player stands.</summary>
    public float OutdoorTemperature => _temperature.OutdoorAt(CurrentBiome.Zone, CurrentBiome.Underground, ClockHour);
    /// <summary>Temperature the player actually feels (room air indoors), refreshed on the story tick.</summary>
    public float PlayerAmbientTemperature { get; private set; }
    public ThermalComfort PlayerComfort { get; private set; }
    /// <summary>The enclosed room the player stands in, or null outdoors.</summary>
    public RoomInfo? PlayerRoom => _regions.GetRoomAt(PlayerCenter);

    /// <summary>Temperature in °C at any world position: room air if enclosed, else outdoor air.</summary>
    public float GetTemperatureAt(Vector2 worldPosition) =>
        AmbientAt(worldPosition, BiomeScanner.Scan(World, ClampCell(RegionMap.CellAt(worldPosition))));

    /// <summary>
    /// Places a creature directly (debug tools, tests, scripted encounters), lifted clear of
    /// any terrain it would overlap.
    /// </summary>
    public EnemyAgent SpawnCreature(EnemyKind kind, Vector2 position)
    {
        var enemy = new EnemyAgent(_nextEntityId++, kind, FindClearSpawn(position, EnemyArchetypes.Get(kind).Size));
        _enemies.Add(enemy);
        return enemy;
    }

    /// <summary>Fires a named storyteller incident now if its gate allows (debug console).</summary>
    public StoryIncidentRecord? ForceIncident(string incidentId) => _storyteller.ForceIncident(incidentId, this);

    /// <summary>The boss the challenge key summons next; the Astro-God repeats once all are beaten.</summary>
    public EnemyKind NextBoss => EnemyArchetypes.BossProgression
        .FirstOrDefault(kind => !_defeatedBosses.Contains(kind), EnemyKind.AstroGod);

    /// <param name="binaryCells">
    /// Put the deflated cells in <see cref="GameSnapshot.CellData"/> (raw bytes for a sidecar file) instead of the
    /// base64 <see cref="GameSnapshot.CompressedCells"/> string. Disk saves use this so large worlds stay out of JSON.
    /// </param>
    public GameSnapshot CaptureSnapshot(bool binaryCells = false)
    {
        // Cells are saved run-length encoded and deflated; the JSON cell array stays null.
        SyncWiringToFlags();
        var compressed = binaryCells ? null : CellCodec.Encode(World.Cells);
        var cellData = binaryCells ? CellCodec.EncodeBytes(World.Cells) : null;

        var motion = Player.Motion;
        var colonistSnapshots = _colonists.Select((agent, index) => new ColonistSnapshot(
            index,
            agent.Motion.Position.X,
            agent.Motion.Position.Y,
            agent.HitPoints,
            agent.Needs.FoodReserve,
            agent.Needs.RestReserve,
            agent.Needs.JoyReserve,
            agent.Needs.ComfortReserve)).ToArray();
        return new GameSnapshot(
            GameSnapshot.CurrentFormatVersion, Seed, World.Width, World.Height, null,
            TickNumber, Day, ClockHour, motion.Position.X, motion.Position.Y,
            motion.Velocity.X, motion.Velocity.Y, motion.Facing,
            Player.HitPoints, Player.Needs.FoodReserve, Player.Needs.RestReserve,
            Player.Inventory.SlotsWithCursorReturned(), Player.Inventory.SelectedSlot,
            CaptureEquipmentWithCalibrations(), Player.Statuses.Active.ToArray(),
            Player.Needs.JoyReserve, Player.Needs.ComfortReserve, colonistSnapshots,
            _defeatedBosses.OrderBy(kind => kind).ToArray(),
            _storyteller.Capture(),
            CapturePawn(Player, -1),
            _colonists.Select((agent, index) => CapturePawn(agent, index)).ToArray(),
            CompressedCells: compressed,
            WorldMeta: WorldMetaCodec.Capture(BiomeLayout, Features),
            Blight: Blight,
            Hazard: _hazardChoice,
            FractureActive: FractureActive,
            HeartsBroken: _heartsBroken,
            AltarsSmashed: _altarsSmashed,
            LifeCoresUsed: Player.LifeCoresUsed,
            Content: CaptureContent(),
            PlayerWorld: CapturePlayerWorld(),
            Character: CaptureCharacterForSnapshot(),
            Enemies: _enemies.Select(enemy => enemy.CaptureState()).ToArray(),
            Projectiles: _projectiles.Select(projectile => projectile.CaptureState()).ToArray(),
            RaidEnemyIds: _raidEnemyIds.ToArray(),
            NightBossIds: _nightBossIds.ToArray(),
            WhipMarkedEnemy: _whipMarkedEnemy,
            WhipBuffSeconds: _whipBuffSeconds,
            Run: CaptureRun(),
            MiscEquipment: CaptureMisc(), BedSpawnX: _bedSpawn?.X, BedSpawnY: _bedSpawn?.Y) { CellData = cellData };
    }

    private static PawnStateSnapshot CapturePawn(IColonyPawn pawn, int index) => new(
        index,
        pawn.Health.Capture(),
        pawn.Mind.Thoughts.Capture(),
        pawn.Mind.Relations.Capture(),
        pawn.Mind.Breaker.Capture(),
        pawn.Mind.Mood.Current,
        pawn.Mind.NextInteractionHour);

    private static void RestorePawn(IColonyPawn pawn, PawnStateSnapshot state)
    {
        if (state.Health is not null)
            pawn.Health.Restore(state.Health);
        if (state.Memories is not null)
            pawn.Mind.Thoughts.Restore(state.Memories);
        if (state.Relations is not null)
            pawn.Mind.Relations.Restore(state.Relations);
        if (state.Break is { } breakState)
            pawn.Mind.Breaker.Restore(breakState);
        if (state.Mood is { } mood)
        {
            if (!float.IsFinite(mood))
                throw new InvalidDataException("The saved mood is invalid.");
            pawn.Mind.Mood.Force(mood);
        }
        if (state.NextInteractionHour is { } next && float.IsFinite(next))
            pawn.Mind.NextInteractionHour = next;
    }

    public static GameSimulation Restore(GameSnapshot snapshot, CharacterProfile? colonist = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.FormatVersion != GameSnapshot.CurrentFormatVersion ||
            snapshot.WorldWidth is < 16 or > MaximumWorldWidth || snapshot.WorldHeight is < 16 or > MaximumWorldHeight ||
            (snapshot.Cells is null && snapshot.CompressedCells is null && snapshot.CellData is null) ||
            (snapshot.Cells is not null && snapshot.Cells.Length != (long)snapshot.WorldWidth * snapshot.WorldHeight) ||
            snapshot.HeartsBroken is < 0 || snapshot.AltarsSmashed is < 0 ||
            snapshot.LifeCoresUsed is < 0 or > PlayerCharacter.MaximumLifeCores ||
            (snapshot.Blight is { } savedBlight && !Enum.IsDefined(savedBlight)) ||
            (snapshot.Hazard is { } savedHazard && !Enum.IsDefined(savedHazard)) ||
            snapshot.Inventory is null || snapshot.TickNumber < 0 || snapshot.Day < 1 ||
            !float.IsFinite(snapshot.ClockHour) || snapshot.ClockHour is < 0f or >= 24f ||
            !float.IsFinite(snapshot.PlayerX) || !float.IsFinite(snapshot.PlayerY) ||
            !float.IsFinite(snapshot.VelocityX) || !float.IsFinite(snapshot.VelocityY) ||
            (snapshot.JoyReserve is { } joy && !float.IsFinite(joy)) ||
            (snapshot.ComfortReserve is { } comfort && !float.IsFinite(comfort)))
            throw new InvalidDataException("The Rimtera save snapshot is invalid or uses an unsupported version.");

        var cellCount = snapshot.WorldWidth * snapshot.WorldHeight;
        WorldCell[] cells;
        if (snapshot.CellData is { } bytes)
            cells = CellCodec.DecodeBytes(bytes, cellCount);
        else if (snapshot.CompressedCells is { } packed)
            cells = CellCodec.Decode(packed, cellCount);
        else
        {
            // JSON cell arrays (oldest saves) carried float depths; round them onto liquid units consistently.
            cells = (WorldCell[])snapshot.Cells!.Clone();
            for (var index = 0; index < cells.Length; index++)
                CellCodec.NormalizeLiquid(ref cells[index]);
        }
        ValidateCells(cells, snapshot.WorldWidth);

        // New saves carry the generation metadata: restore directly. Older saves regenerate once for it.
        var layout = snapshot.WorldMeta is { } meta ? WorldMetaCodec.RestoreLayout(meta, snapshot.WorldWidth, snapshot.WorldHeight) : null;
        GameSimulation game;
        if (layout is not null)
        {
            game = new GameSimulation(snapshot.Seed, snapshot.WorldWidth, snapshot.WorldHeight, colonist,
                snapshot.Hazard, progress: null, blight: snapshot.Blight, new LoadedWorld(cells, layout, snapshot.WorldMeta!));
        }
        else
        {
            game = new GameSimulation(snapshot.Seed, snapshot.WorldWidth, snapshot.WorldHeight, colonist,
                snapshot.Hazard, progress: null, blight: snapshot.Blight);
            game.World.LoadCells(cells);
            game.AfterBulkLoad();
            game._framing.ReframeAll(game.World, game.Tiles);
        }
        game.Player.LifeCoresUsed = snapshot.LifeCoresUsed ?? 0;
        game._heartsBroken = snapshot.HeartsBroken ?? 0;
        game._altarsSmashed = snapshot.AltarsSmashed ?? 0;
        if (snapshot.FractureActive == true)
        {
            game.FractureActive = true;
            game._ticker.Fractured = true;
        }

        // Saves from before the 50-slot (Terraria 10 × 5) inventory carry 24 slots, and saves from before 0.1.10 have no
        // coin or ammo slots; the new ones start empty.
        var savedSlots = snapshot.Inventory.Length < PlayerTotalSlots
            ? snapshot.Inventory.Concat(Enumerable.Repeat(default(ItemStack), PlayerTotalSlots - snapshot.Inventory.Length)).ToArray()
            : snapshot.Inventory;
        if (!game.Player.Inventory.RestoreState(savedSlots, snapshot.SelectedInventorySlot))
            throw new InvalidDataException("The saved inventory layout is invalid.");

        if (snapshot.Equipment is not null && !game.Player.Equipment.Restore(snapshot.Equipment, game.Items))
            throw new InvalidDataException("The saved equipment layout is invalid.");
        game.RestoreAccessoryCalibrations(snapshot.Equipment);
        if (snapshot.Statuses is not null && !game.Player.Statuses.Restore(snapshot.Statuses))
            throw new InvalidDataException("The saved status layout is invalid.");
        game.RestoreContent(snapshot.Content);
        game.RestoreEntities(snapshot);
        game.RestoreRun(snapshot.Run);
        game.RestoreMisc(snapshot.MiscEquipment);
        if (snapshot is { BedSpawnX: { } bedX, BedSpawnY: { } bedY } && game.World.Contains(bedX, bedY))
            game._bedSpawn = new GridPoint(bedX, bedY);
        game.LoadWiringFromFlags();

        var motion = game.Player.Motion;
        motion.Position = new Vector2(
            Math.Clamp(snapshot.PlayerX, 0f, game.World.Width - motion.Size.X),
            Math.Clamp(snapshot.PlayerY, 0f, game.World.Height - motion.Size.Y));
        motion.Velocity = new Vector2(snapshot.VelocityX, snapshot.VelocityY);
        motion.Facing = snapshot.Facing < 0 ? -1 : 1;
        game.Player.RestorePersistentState(
            snapshot.HitPoints,
            snapshot.FoodReserve,
            snapshot.RestReserve,
            snapshot.JoyReserve ?? 0.70f,
            snapshot.ComfortReserve ?? 0.70f);
        // Saves from before crew arrived through housing carry the two crew members a world used to start with.
        var savedCrew = snapshot.Colonists?.Length ?? 0;
        var joined = snapshot.Storyteller?.WanderersJoined ?? 0;
        if (savedCrew > joined)
            game.AddLegacyStartingCrew(savedCrew - joined);
        if (snapshot.Storyteller is { } story)
        {
            // Crew arrivals are no longer capped over a run (the dead are replaced), only by what housing supports at once.
            if (story.WanderersJoined is < 0 or > 1024)
                throw new InvalidDataException("The saved storyteller state is invalid.");
            for (var wanderer = 0; wanderer < story.WanderersJoined; wanderer++)
                game.AddWanderer(wanderer, game.PlayerCenter);
            game._storyteller.Restore(story);
        }
        if (snapshot.Colonists is not null)
        {
            var seen = new HashSet<int>();
            foreach (var colonistSnapshot in snapshot.Colonists)
            {
                if (colonistSnapshot.Index < 0 || colonistSnapshot.Index >= game._colonists.Count ||
                    !seen.Add(colonistSnapshot.Index) ||
                    !float.IsFinite(colonistSnapshot.X) || !float.IsFinite(colonistSnapshot.Y) ||
                    !float.IsFinite(colonistSnapshot.HitPoints) ||
                    !float.IsFinite(colonistSnapshot.FoodReserve) || !float.IsFinite(colonistSnapshot.RestReserve) ||
                    !float.IsFinite(colonistSnapshot.JoyReserve) || !float.IsFinite(colonistSnapshot.ComfortReserve))
                    throw new InvalidDataException("The saved colonist roster is invalid.");
                var agent = game._colonists[colonistSnapshot.Index];
                var position = new Vector2(
                    Math.Clamp(colonistSnapshot.X, 0f, game.World.Width - agent.Motion.Size.X),
                    Math.Clamp(colonistSnapshot.Y, 0f, game.World.Height - agent.Motion.Size.Y));
                agent.RestorePersistentState(
                    position,
                    colonistSnapshot.HitPoints,
                    colonistSnapshot.FoodReserve,
                    colonistSnapshot.RestReserve,
                    colonistSnapshot.JoyReserve,
                    colonistSnapshot.ComfortReserve);
            }
        }
        if (snapshot.PlayerState is { } playerState)
            RestorePawn(game.Player, playerState);
        if (snapshot.ColonistStates is not null)
        {
            var seenStates = new HashSet<int>();
            foreach (var state in snapshot.ColonistStates)
            {
                // States for crew the snapshot no longer rebuilds (e.g. wanderers without storyteller data) are skipped.
                if (state is null || state.Index < 0 || state.Index >= game._colonists.Count)
                    continue;
                if (!seenStates.Add(state.Index))
                    throw new InvalidDataException("The saved colonist state is invalid.");
                RestorePawn(game._colonists[state.Index], state);
            }
        }
        foreach (var agent in game._colonists)
        {
            agent.SyncHealthDeath();
            if (!agent.IsAlive)
                game._deathsHandled.Add(agent.SocialKey);
        }
        game.Player.SyncHealthDeath();
        if (snapshot.DefeatedBosses is not null)
        {
            foreach (var kind in snapshot.DefeatedBosses)
            {
                if (!EnemyArchetypes.Contains(kind) || !EnemyArchetypes.Get(kind).IsBoss)
                    throw new InvalidDataException("The saved boss record is invalid.");
                game._defeatedBosses.Add(kind);
            }
        }
        game.TickNumber = snapshot.TickNumber;
        game.Day = snapshot.Day;
        game.ClockHour = snapshot.ClockHour;
        game._elapsedGameHours = Math.Max(0f, (snapshot.Day - 1) * 24f + snapshot.ClockHour - 8f);
        game._lastStoryHour = (long)MathF.Floor(game._elapsedGameHours);
        game.CurrentBiome = BiomeScanner.Scan(game.World, game.PlayerCell());
        game._temperature.OutdoorOffset = game._storyteller.TemperatureOffset;
        game.PlayerAmbientTemperature = game.AmbientAt(game.PlayerCenter, game.CurrentBiome);
        game.PlayerComfort = TemperatureModel.Evaluate(game.PlayerAmbientTemperature, game.Player.Stats.ImmunityTags);
        game.RestoreBatchB(snapshot);
        // People grew to Terraria's body size (HumanoidBody); an older save can leave feet in the ground, so lift them clear.
        foreach (var body in game.TownNpcs.Select(npc => npc.Motion).Concat(game._colonists.Select(agent => agent.Motion)).Prepend(game.Player.Motion))
            game.LiftClear(body);
        return game;
    }

    /// <summary>Largest supported world (the Large preset).</summary>
    public const int MaximumWorldWidth = 8400;
    public const int MaximumWorldHeight = 2400;

    /// <summary>Rejects unknown tiles, liquid kinds and inconsistent liquid in loaded cells.</summary>
    private static void ValidateCells(WorldCell[] cells, int width)
    {
        var catalog = new TileCatalog();
        var known = new bool[ushort.MaxValue + 1];
        for (var id = 0; id < known.Length; id++)
            known[id] = catalog.Contains((TileId)id);
        for (var index = 0; index < cells.Length; index++)
        {
            var cell = cells[index];
            if (!known[(int)cell.TileId] || !Enum.IsDefined(cell.LiquidKind) ||
                cell.LiquidUnits > WorldCell.LiquidUnitsMax ||
                (cell.LiquidUnits == 0 && cell.LiquidKind != LiquidKind.None) ||
                (cell.LiquidUnits > 0 && cell.LiquidKind == LiquidKind.None))
                throw new InvalidDataException(
                    $"The saved tile at ({index % width},{index / width}) contains an invalid value.");
        }
    }

    public GameTickResult Tick(in GameInput input, float deltaSeconds = 1f / 60f)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f || deltaSeconds > 0.25f)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Simulation steps must be in (0, 0.25] seconds.");
        Player.SyncHealthDeath();
        if (IsOver)
        {
            UpdateWhileDead(deltaSeconds);
            return CurrentResult();
        }

        TickNumber++;
        if (Player.ActionCooldownTicks > 0)
            Player.ActionCooldownTicks--;

        if (_sleeping && (input.Action is not (PlayerAction.None or PlayerAction.Sleep) || MathF.Abs(input.Movement.Horizontal) > 0.01f))
            WakeUp();
        var elapsedHours = deltaSeconds * ClockHoursPerRealSecond * TimeRate;
        // Statuses, regen, health and needs run on real time (vital hours), never on the clock: sleeping speeds the clock,
        // not the buffs, as in Terraria.
        var vitalHours = deltaSeconds * VitalHoursPerRealSecond;
        AdvanceClock(elapsedHours * ClockPace); // nights pass faster than days (GameSimulation.World.cs)
        Player.Needs.Advance(
            vitalHours,
            Player.ColonistStats.FoodConsumptionMultiplier,
            Player.ColonistStats.RestRateMultiplier);
        Player.Health.IsResting = Player.IsDowned;
        Player.Health.Tick(vitalHours);
        var statusDamage = Player.Statuses.Advance(vitalHours);
        if (statusDamage > 0f)
            Player.TakeDamage(statusDamage, DamageKind.Toxin);
        if (Player.Stats.LifeRegenerationPerHour > 0f)
            Player.Heal(Player.Stats.LifeRegenerationPerHour * vitalHours);
        Player.SyncHealthDeath();
        if (Player.IsDead)
        {
            if (Player.Health.DeathCause is { } cause)
                PostMessage($"{Player.Name} has died ({cause}).");
            return CurrentResult();
        }
        UpdatePlayerDowned();

        // Downed pawns cannot move or act; a hurt body runs slower and works clumsier.
        var movement = Player.IsDowned
            ? new MovementInput(0f)
            : input.Movement with
            {
                SpeedMultiplier = input.Movement.SpeedMultiplier * Player.Stats.MoveSpeedMultiplier * Player.MoveCapacityFactor *
                                  ContentMoveFactor
            };
        movement = PreparePlayerMovement(movement, deltaSeconds);
        _playerMovement.Step(Player.Motion, movement, deltaSeconds, World, Tiles);
        CarryOnGlideWalk(deltaSeconds);
        AfterPlayerMovement(movement, deltaSeconds);
        UpdateToolUse(input.Action); // swing, press/hold and release tracking (ToolParityUse.cs)
        ApplyBufferedHotbarSlot(); // a hotbar change made mid-swing lands once the use ends (GameSimulation.Controls.cs)
        // As in Terraria, using an item turns the player toward what it is used on, even while walking the other way.
        if (input.Action is PlayerAction.Mine or PlayerAction.UseSelected or PlayerAction.ReplaceSelected && input.Target is { } aimed &&
            !Player.IsDowned && MathF.Abs(aimed.X + 0.5f - PlayerCenter.X) > 0.3f)
            Player.Motion.Facing = aimed.X + 0.5f < PlayerCenter.X ? -1 : 1;
        if (input.Action != PlayerAction.None && !Player.IsDowned)
            PerformAction(input.Action, input.Target);

        UpdateColonists(deltaSeconds, vitalHours);
        UpdateTownNpcs(deltaSeconds);
        UpdateEnemies(deltaSeconds);
        UpdatePlayerSystems(deltaSeconds);
        UpdateContentPerTick(deltaSeconds);
        UpdateHardmode(deltaSeconds);
        UpdateBatchB(deltaSeconds);
        UpdateFishing(deltaSeconds);
        UpdateWorldRealtime(deltaSeconds);
        UpdatePhysics(deltaSeconds);
        UpdateProjectiles(deltaSeconds);
        UpdateAutonomousBehavior(elapsedHours);
        UpdateLeisure(deltaSeconds); // wind, kites, decks, pets, golf, wave moons, creative (GameSimulation.Leisure.cs)
        UpdateSpawning(deltaSeconds);
        UpdateEnvironment(vitalHours);
        UpdateStory();

        _liquidTimer += deltaSeconds;
        if (_liquidTimer >= LiquidStepSeconds)
        {
            _liquidTimer %= LiquidStepSeconds;
            _liquids.Step();
        }
        var grown = _ticker.Tick();
        if (grown.Count > 0)
            _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, grown));

        return CurrentResult();
    }

    /// <summary>Legacy entry point for the challenge key; summons the next boss in the progression.</summary>
    public bool TrySummonSentinel() => TrySummonNextBoss();

    /// <summary>Summons the next undefeated boss (gate, easy, hard, then Astro-God) if no boss is active.</summary>
    public bool TrySummonNextBoss()
    {
        if (_enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive))
            return false;
        var kind = NextBoss;
        SpawnBoss(kind);
        PostMessage($"The {EnemyArchetypes.Get(kind).DisplayName} has answered the challenge.");
        return true;
    }

    private EnemyAgent SpawnBoss(EnemyKind kind)
    {
        var archetype = EnemyArchetypes.Get(kind);
        var playerX = (int)MathF.Floor(Player.Motion.Position.X);
        var direction = Player.Motion.Facing >= 0 ? 1 : -1;
        var x = Math.Clamp(playerX + direction * 12, 1, Math.Max(1, World.Width - 2 - (int)MathF.Ceiling(archetype.Size.X)));
        var surface = FindSurface(x);
        var lift = archetype.Flying ? 4f : 0f;
        var position = FindClearSpawn(new Vector2(x, surface - archetype.Size.Y - lift), archetype.Size);
        var enemy = new EnemyAgent(_nextEntityId++, kind, position);
        _enemies.Add(enemy);
        return enemy;
    }

    /// <summary>Raises a spawn box until it no longer overlaps solid terrain (large bosses on rough ground).</summary>
    private Vector2 FindClearSpawn(Vector2 position, Vector2 size)
    {
        var candidate = position;
        for (var step = 0; step < 24 && candidate.Y > 0f && IsSolidActorPosition(candidate, size); step++)
            candidate.Y -= 1f;
        return candidate with { Y = Math.Max(0f, candidate.Y) };
    }

    public bool TrySelectInventorySlot(int slot)
    {
        if (!Player.Inventory.Select(slot))
            return false;
        var item = Player.Inventory.SelectedItem;
        PostMessage(item is null ? "Selected an empty slot." : $"Selected {item.Name}.");
        return true;
    }

    public bool SelectNextInventoryItem(int direction = 1)
    {
        if (direction == 0)
            return false;
        var slots = Player.Inventory.BagSlots;
        var step = Math.Sign(direction);
        for (var offset = 1; offset <= slots; offset++)
        {
            var slot = (Player.Inventory.SelectedSlot + step * offset) % slots;
            if (slot < 0)
                slot += slots;
            if (!Player.Inventory.Slots[slot].IsEmpty)
                return TrySelectInventorySlot(slot);
        }
        return false;
    }

    public IReadOnlyList<RecipeDefinition> GetCraftableRecipes() =>
        Crafting.GetCraftableRecipes(Player.Inventory, CreateCraftingEnvironment());

    public CraftingResult TryCraft(string recipeId)
    {
        var result = Crafting.TryCraft(recipeId, Player.Inventory, CreateCraftingEnvironment(), RollCalibration);
        PostMessage(result.Message);
        return result;
    }

    public CraftingResult TryCraftNext()
    {
        var craftable = GetCraftableRecipes();
        var recipe = craftable.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, _selectedRecipeId, StringComparison.OrdinalIgnoreCase));
        recipe ??= craftable.FirstOrDefault();
        if (recipe is null)
        {
            var result = new CraftingResult(false, "No recipe is currently craftable; check materials and nearby stations.");
            PostMessage(result.Message);
            return result;
        }
        _selectedRecipeId = recipe.Id;
        return TryCraft(recipe.Id);
    }

    public RecipeDefinition? CycleCraftableRecipe(int direction = 1)
    {
        if (direction == 0)
            return SelectedRecipe;
        var craftable = GetCraftableRecipes();
        if (craftable.Count == 0)
        {
            _selectedRecipeId = null;
            PostMessage("No recipe is currently craftable; check materials and nearby stations.");
            return null;
        }

        var current = -1;
        for (var i = 0; i < craftable.Count; i++)
        {
            if (string.Equals(craftable[i].Id, _selectedRecipeId, StringComparison.OrdinalIgnoreCase))
            {
                current = i;
                break;
            }
        }
        var step = Math.Sign(direction);
        var next = current < 0
            ? (step > 0 ? 0 : craftable.Count - 1)
            : (current + step + craftable.Count) % craftable.Count;
        var selected = craftable[next];
        _selectedRecipeId = selected.Id;
        PostMessage($"Selected recipe: {Items[selected.Output].Name}.");
        return selected;
    }

    public IReadOnlyList<string> ConsultGuide()
    {
        var playerCenter = Player.Motion.Position + Player.Motion.Size / 2f;
        if (!IsNearGuide(playerCenter))
        {
            PostMessage("The guide is too far away to consult.");
            return Array.Empty<string>();
        }
        var learned = Crafting.ConsultGuide(Player.Inventory);
        PostMessage(learned.Count == 0
            ? "The guide has no new recipes to show from your carried materials."
            : $"The guide revealed: {string.Join(", ", learned.Select(DescribeRecipe))}.");
        return learned;
    }

    private string DescribeRecipe(string recipeId) =>
        Crafting.Recipes.FirstOrDefault(recipe => string.Equals(recipe.Id, recipeId, StringComparison.OrdinalIgnoreCase)) is { } recipe
            ? Items[recipe.Output].Name
            : recipeId;

    private void PerformAction(PlayerAction action, GridPoint? target)
    {
        switch (action)
        {
            case PlayerAction.Mine:
                Mine(target ?? GetFacingTile());
                break;
            case PlayerAction.UseSelected:
                UseSelected(target ?? GetFacingTile());
                break;
            case PlayerAction.Eat:
                EatSelectedOrBerry();
                break;
            case PlayerAction.ReplaceSelected:
                ReplaceSelected(target ?? GetFacingTile());
                break;
            case PlayerAction.TendSelf:
                TendPlayerSelf();
                break;
            case PlayerAction.Interact:
                if (!TryInteractPlayerSystems(target ?? GetFacingTile()))
                    Interact(target ?? GetFacingTile());
                break;
            case PlayerAction.Sleep:
                TrySleep();
                break;
        }
    }

    /// <summary>Applies an action cooldown scaled by manipulation and the extreme-mood work penalty.</summary>
    private void SetPlayerCooldown(int ticks)
    {
        var scaled = (int)MathF.Ceiling(ticks * Player.ActionCooldownScale - 0.001f);
        Player.ActionCooldownTicks = Math.Max(0, scaled);
    }

    /// <summary>The player dresses their own wounds at reduced quality. Returns the number of conditions tended.</summary>
    public int TendPlayerSelf()
    {
        if (Player.IsDead || Player.IsDowned || Player.ActionCooldownTicks > 0)
            return 0;
        if (!Player.Health.NeedsTending)
        {
            PostMessage("No wounds need tending.");
            return 0;
        }
        var quality = Math.Clamp(Player.ColonistStats.MedicalTendQuality * 0.7f * Math.Max(0.3f, Player.Health.Manipulation), 0.05f, 1f);
        var count = Player.Health.Tend(quality);
        SetPlayerCooldown(45);
        PostMessage($"{Player.Name} dressed their own wounds ({quality:P0} quality).");
        return count;
    }

    private void UpdatePlayerDowned()
    {
        if (Player.IsDowned && !_playerDownedWarned)
        {
            PostMessage($"{Player.Name} is down ({Player.Health.DownedReason})!");
        }
        else if (!Player.IsDowned && _playerDownedWarned)
            PostMessage($"{Player.Name} is back on their feet.");
        _playerDownedWarned = Player.IsDowned;
    }

    /// <summary>Reports newly destroyed body parts and adds the shock memory.</summary>
    private void ReportPartLoss(IColonyPawn pawn, DamageResult result)
    {
        foreach (var part in result.DestroyedParts)
        {
            PostMessage($"{pawn.Name} lost the {part.Name}!");
            pawn.Mind.Thoughts.AddMemory(ThoughtId.LostLimb);
        }
    }

    private void Mine(GridPoint point)
    {
        if (Player.ActionCooldownTicks > 0)
            return;
        if (!IsInReach(point))
        {
            PostMessage("That tile is out of reach.");
            return;
        }

        var item = Player.Inventory.SelectedItem;
        if (item?.UseKind != ItemUseKind.Pickaxe)
        {
            PostMessage("Select a drill to mine.");
            return;
        }

        var cell = World.GetCell(point);
        var definition = Tiles[cell.TileId];
        if (cell.TileId == TileId.Air || cell.TileId == TileId.Bedrock)
        {
            PostMessage(cell.TileId == TileId.Bedrock ? "The bedrock holds." : "Nothing to mine there.");
            return;
        }

        // Every drill swing at a tile costs the drill's hit time, even when the tile refuses it (docs/TOOLS_PARITY.md).
        var hitTicks = DrillHitTicks(item);
        if (item.ToolPower < definition.MinimumToolPower)
        {
            PostMessage($"{definition.Name} needs a stronger drill.");
            SetPlayerCooldown(hitTicks);
            return;
        }
        if (BlightRules.IsAltar(cell.TileId) && (!FractureActive || !CanSmashAltars(item)))
        {
            PostMessage(FractureActive ? AltarNeedsBreakerMessage(definition.Name) : AltarLockedMessage(definition.Name));
            SetPlayerCooldown(hitTicks);
            return;
        }
        if (!PrepareMiningHit(point, cell.TileId, item)) return; // trunks and cactus need cutters, hearts demolishers, full containers refuse

        // Each hit adds the drill's power (×2 soft ground, ÷N on the toughest blocks) toward 100; at 100 the block breaks.
        // A block propping up a trunk, container, altar or heart takes no damage until that goes.
        var hit = !ToolParity.CanBreakUnder(World, point) ? 0
            : ToolParity.IsOneHitTile(cell.TileId) ? CombatTuning.TileBreakDamage // plants, rope, torches: always one hit
            : CombatTuning.DrillDamage(item.ToolPower, DrillHardness(cell.TileId), definition.MinimumToolPower);
        if (hit == 0 && item.ToolPower >= definition.MinimumToolPower && !ToolParity.CanBreakUnder(World, point))
            PostMessage($"Clear what stands on the {definition.Name.ToLowerInvariant()} first.");
        else if (AddTileHitDamage(point, hit, out var brokenTile))
        {
            if (!HandleBrokenTile(point, brokenTile))
                PostMessage($"Mined {definition.Name}.");
        }
        else
        {
            PostMessage($"Hit {definition.Name} ({TileHitDamage(point)}/{CombatTuning.TileBreakDamage}).");
        }
        SetPlayerCooldown(hitTicks);
    }

    private void UseSelected(GridPoint point)
    {
        if (TryUseWeaponStyle(point)) return;
        if (Player.ActionCooldownTicks > 0)
            return;
        var item = Player.Inventory.SelectedItem;
        if (item is null)
        {
            PostMessage("Selected slot is empty.");
            return;
        }
        if (ToolCombat.IsTool(item))
            StrikeWithToolSwing(item, point); // tools are melee weapons too: the swing hits creatures in its arc (GameSimulation.ToolCombat.cs)
        if (UseToolParityUtility(item, point)) return; // shaping hammer, wire tools, buckets, paint tools (ToolParityUse.cs)
        if (TryUseLeisureItem(item, point)) return; // kites, paint, golf, permits, decks (GameSimulation.Leisure.cs)

        switch (item.UseKind)
        {
            case ItemUseKind.PlaceTile:
                Place(item, point);
                break;
            case ItemUseKind.Melee:
                AttackMelee(item, point);
                break;
            case ItemUseKind.Ranged:
                AttackRanged(item, point);
                break;
            case ItemUseKind.Food:
                EatSelectedOrBerry();
                break;
            case ItemUseKind.Equipment:
            case ItemUseKind.Vanity:
                EquipSelected(item.UseKind == ItemUseKind.Vanity);
                break;
            case ItemUseKind.Consumable:
                ConsumeSelected(item);
                break;
            case ItemUseKind.Pickaxe:
                Mine(point);
                break;
            default:
                if (!UsePlayerSystemsItem(item, point) && !UseContentItem(item, point))
                    NoDirectUse();
                break;
        }
    }

    private void Place(ItemDefinition item, GridPoint point)
    {
        if (TryPlacePhysicsTile(item, point))
            return;
        if (TryPlaceTravelTile(item, point)) return;
        if (!IsInReach(point))
        {
            PostMessage("That placement is out of reach.");
            return;
        }

        // Doors stand three tiles tall with the clicked cell as their bottom; trapdoors are single cells.
        var tallDoor = ContentTiles.Has(item.PlacedTile, TileTraits.Door) && item.PlacedTile != TileId.Trapdoor;
        var origin = tallDoor ? new GridPoint(point.X, point.Y - 2) : point;
        var anchor = tallDoor ? PlacementAnchorRule.SolidBelow
            : item.PlacedTile is TileId.Trapdoor or TileId.WallTorch or TileId.CeilingLight or TileId.PaintingPlanet or TileId.ColonyBanner or TileId.BossTrophy
                ? PlacementAnchorRule.SolidAdjacent
                // Non-solid colony furniture stands on the ground; blocks may float.
                : Tiles[item.PlacedTile].IsSolid ? PlacementAnchorRule.None : PlacementAnchorRule.SolidBelow;
        var result = _placement.TryPlaceTile(
            World,
            Tiles,
            origin,
            new PlacementDefinition(
                item.PlacedTile,
                Height: tallDoor ? 3 : 1,
                AnchorRule: anchor,
                AllowLiquid: true),
            CreatePlacementContext());
        if (!result.Success)
        {
            PostMessage($"Cannot place: {result.Failure}.");
            return;
        }

        Player.Inventory.TryConsume(item.Id);
        RememberPlacedDecor(item, origin);
        _framing.ReframeRegion(World, Tiles, result.CellsToReframe);
        SetPlayerCooldown(CombatTuning.PlacementTicks(BlockPlaceTicks, GearTagValue("build")));
        if (item.PlacedTile == TileId.HydroponicsTray)
            Flags.Set("placed.hydroponics");
        PostMessage($"Placed {item.Name}.");
    }

    private void ReplaceSelected(GridPoint point)
    {
        if (Player.ActionCooldownTicks > 0)
            return;
        var item = Player.Inventory.SelectedItem;
        if (item?.UseKind != ItemUseKind.PlaceTile)
        {
            PostMessage("Select a block item to replace a tile.");
            return;
        }
        if (!World.Contains(point))
        {
            PostMessage("That tile is outside the world.");
            return;
        }

        var oldTile = World.GetCell(point).TileId;
        var oldDrop = TileDropTable.GetDrop(oldTile);
        if (oldDrop is { } drop && !Player.Inventory.CanAdd(drop, 1))
        {
            PostMessage("Inventory has no room for the replaced tile's drop.");
            return;
        }

        var strongestToolPower = Items.All
            .Where(definition => definition.UseKind == ItemUseKind.Pickaxe && Player.Inventory.Count(definition.Id) > 0)
            .Select(definition => definition.ToolPower)
            .DefaultIfEmpty(0)
            .Max();
        var oldDefinition = Tiles[oldTile];
        var requiredPower = (int)MathF.Ceiling(oldDefinition.HitPoints * oldDefinition.MiningResistance / 2f);
        var replaceable = new HashSet<TileId>
        {
            TileId.Soil, TileId.Grass, TileId.Rock, TileId.CopperOre, TileId.Sand, TileId.Wood,
            TileId.Workbench, TileId.Furnace, TileId.Anvil,
            TileId.CobaltOre, TileId.ViridiumOre, TileId.EnergyCrystal,
            TileId.Snow, TileId.DesertSand, TileId.JungleGrass, TileId.EmberRock, TileId.IrradiatedRock, TileId.CoreCrust, TileId.CorruptRock, TileId.CrimsonSoil,
            TileId.GlacialOre, TileId.UraniumOre, TileId.PyrociteOre, TileId.ScrapIronOre
        };
        var result = _placement.TryReplaceTile(
            World,
            Tiles,
            point,
            item.PlacedTile,
            new TileReplacementRules(replaceable, requiredPower, AllowLiquid: false,
                DropPolicy: ReplacementDropPolicy.ReturnPreviousTile),
            strongestToolPower,
            CreatePlacementContext());
        if (!result.Success)
        {
            PostMessage($"Cannot replace: {result.Failure}.");
            return;
        }

        Player.Inventory.TryConsume(item.Id);
        if (result.PreviousTile is { } previous && TileDropTable.GetDrop(previous) is { } previousDrop)
            Player.Inventory.Add(previousDrop, 1);
        _framing.ReframeRegion(World, Tiles, result.CellsToReframe);
        SetPlayerCooldown(10);
        PostMessage($"Replaced {Tiles[oldTile].Name} with {item.Name}.");
    }

    private void AttackMelee(ItemDefinition item, GridPoint target)
    {
        if (TryThrowMelee(item, target))
            return;
        var reach = (item.Reach > PlayerReach ? item.Reach : PlayerReach) + GearReachBonus;
        if (!World.Contains(target) || Vector2.Distance(PlayerCenter, new Vector2(target.X + 0.5f, target.Y + 0.5f)) > reach)
        {
            PostMessage("That target is out of reach.");
            return;
        }
        CutPlantsAround(target); // flora parity: swings cut grass plants, vines, herbs and pots

        if (_random.NextDouble() > Player.ColonistStats.MeleeHitChance)
        {
            PostMessage($"The swing goes wide ({Player.ColonistStats.MeleeHitChance:P0} hit chance).");
            SetPlayerCooldown(item.UseCooldownTicks);
            return;
        }

        var targetPosition = new Vector2(target.X + 0.5f, target.Y + 0.5f);
        var enemy = _enemies
            .Where(candidate => candidate.IsAlive)
            .Where(candidate => EnemyHitDistance(candidate, targetPosition) <= 1.25f)
            .OrderBy(candidate => EnemyHitDistance(candidate, targetPosition))
            .FirstOrDefault();
        if (enemy is null)
        {
            PostMessage("The swing missed.");
        }
        else
        {
            var calibration = SelectedCalibration;
            var damage = 0f;
            LastHitCritical = false;
            if (enemy.CanBeHitBy(-1))
            {
                var (rolled, critical) = RollUse(ComputeWeaponDamage(item, calibration), CritChanceFor(item, calibration));
                damage = StrikeEnemy(enemy, rolled, critical, item.EffectiveClass);
            }
            enemy.MarkHitBy(-1, 0.05f);
            OnMeleeHit(item, enemy, damage);
            var knockback = item.Knockback * CalibrationCatalog.Get(calibration).KnockbackMultiplier;
            var away = enemy.Motion.Position.X >= Player.Motion.Position.X ? 1f : -1f;
            KnockEnemy(enemy, new Vector2(away * knockback, -knockback * 0.2f), LastHitCritical);
            PostMessage(enemy.IsAlive ? $"Hit {enemy.DisplayName} for {damage:0} damage." : $"Defeated {enemy.DisplayName}.");
        }
        SetPlayerCooldown(ScaledCooldown(item, SelectedCalibration));
    }

    private void AttackRanged(ItemDefinition item, GridPoint target)
    {
        if (_storyteller.SolarFlareActive)
        {
            PostMessage($"The solar flare has knocked the {item.Name} offline.");
            SetPlayerCooldown(item.UseCooldownTicks);
            return;
        }
        if (item.AmmoType is not { } ammo || Player.Inventory.Count(ammo) == 0)
        {
            PostMessage(item.AmmoType is { } needed ? $"Out of {Items[needed].Name}s." : "No compatible ammunition.");
            return;
        }
        // Ballistics set bonuses may save the round; legacy guns (no set) always spend it without a roll.
        var ammoSave = CombatStats.AmmoSaveChance;
        if (ammoSave <= 0f || CombatRandom.NextDouble() >= ammoSave)
            Player.Inventory.TryConsume(ammo);
        if (item.Class == DamageClass.Ranged && item.Tier > 0)
        {
            // Content guns: multi-pellet, pierce, crits, calibrations and ammo damage.
            var calibration = SelectedCalibration;
            var contentDamage = ComputeWeaponDamage(item, calibration, Items[ammo].AmmoDamage);
            FireProjectiles(item, target, contentDamage, item.Knockback * CalibrationCatalog.Get(calibration).KnockbackMultiplier,
                item.ProjectileSpeed, item.ProjectileCount, item.ProjectileSpread, item.ProjectileLifetime, item.Pierce,
                CritChanceFor(item, calibration), DamageClass.Ranged, item.PurifyRadius);
            TagLastProjectiles(Math.Max(1, item.ProjectileCount), ammo);
            SetPlayerCooldown(ScaledCooldown(item, calibration));
            PostMessage($"Fired the {DescribeStack(Player.Inventory.SelectedStack)}.");
            return;
        }

        var origin = Player.Motion.Position + new Vector2(Player.Motion.Size.X / 2f, Player.Motion.Size.Y * 0.35f);
        var aim = new Vector2(target.X + 0.5f, target.Y + 0.5f) - origin;
        if (aim.LengthSquared() < 0.001f)
            aim = new Vector2(Player.Motion.Facing, 0f);
        // Damaged eyes widen the spread on top of the shooter's own accuracy.
        var sight = Player.Health.Sight;
        var spread = (1f - Player.ColonistStats.ShootingAccuracy * sight) * 0.16f + (1f - sight) * 0.2f;
        var angle = ((float)_random.NextDouble() * 2f - 1f) * spread;
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        aim = new Vector2(aim.X * cos - aim.Y * sin, aim.X * sin + aim.Y * cos);
        var velocity = Vector2.Normalize(aim) * item.ProjectileSpeed;
        var damage = item.Damage * Player.ShootingDamageMultiplier;
        _projectiles.Add(new ProjectileState(_nextEntityId++, origin, velocity, damage, item.Knockback, 3f, ProjectileTeam.Player,
            purifyRadius: item.PurifyRadius) { Class = DamageClass.Ranged, CritChance = CritChanceFor(item, Calibration.None) });
        SetPlayerCooldown(item.UseCooldownTicks);
        PostMessage($"Fired {item.Name} ({Player.ColonistStats.ShootingAccuracy:P0} accuracy).");
    }

    private void EatSelectedOrBerry()
    {
        var selected = Player.Inventory.SelectedItem;
        var food = selected?.UseKind == ItemUseKind.Food ? selected : Items[ItemId.Berry];
        if (!Player.Inventory.TryConsume(food.Id))
        {
            PostMessage("No food available.");
            return;
        }
        Player.Needs.Restore(NeedKind.Food, food.FoodRestore);
        GearOnAte(food);
        if (food.Id == ItemId.Berry)
            Player.Mind.Thoughts.AddMemory(ThoughtId.AteNanoBanana);
        SetPlayerCooldown(Math.Max(Player.ActionCooldownTicks, 8));
        PostMessage($"Ate {food.Name}.");
    }

    private void EquipSelected(bool vanity)
    {
        var item = Player.Inventory.SelectedItem;
        if (item?.EquipmentSlot is not { } slot)
        {
            PostMessage("That item cannot be equipped.");
            return;
        }

        if (!GearTryEquip(slot, vanity))
        {
            PostMessage($"Cannot equip {item.Name}.");
            return;
        }

        SetPlayerCooldown(6);
        PostMessage($"Equipped {item.Name}.");
    }

    private void ConsumeSelected(ItemDefinition item)
    {
        if (TryUseGrowthItem(item)) return; // lifebloom pods, auxiliary socket, permanent upgrades
        if (item.Id == ItemId.LifeCore)
        {
            if (UseLifeCore())
                SetPlayerCooldown(6);
            return;
        }
        if (ConsumeContentItem(item))
            return;
        if (item.GrantsStatus is not { } status)
        {
            NoDirectUse();
            return;
        }
        if (!Player.Inventory.TryConsume(item.Id))
        {
            PostMessage("No item available.");
            return;
        }
        Player.Statuses.Apply(status, item.StatusDurationHours, Player.Stats.ImmunityTags);
        SetPlayerCooldown(6);
        PostMessage($"Used {item.Name}.");
    }

    public bool TryUnequipEquipment(EquipmentSlot slot, bool vanity = false)
    {
        var item = GearUnequip(slot, vanity);
        if (item is null)
        {
            PostMessage("Nothing to unequip there.");
            return false;
        }
        SetPlayerCooldown(6);
        PostMessage($"Unequipped {Items[item.Value].Name}.");
        return true;
    }

    private void UpdateColonists(float deltaSeconds, float elapsedHours)
    {
        var playerCenter = Player.Motion.Position + Player.Motion.Size / 2f;
        foreach (var agent in _colonists)
        {
            agent.SyncHealthDeath();
            if (!agent.IsAlive)
            {
                HandleColonistDeath(agent);
                continue;
            }

            agent.Needs.Advance(
                elapsedHours,
                agent.ColonistStats.FoodConsumptionMultiplier,
                agent.ColonistStats.RestRateMultiplier);
            agent.Health.IsResting = agent.IsDowned || agent.Activity == "Resting at camp";
            agent.Health.Tick(elapsedHours);
            agent.SyncHealthDeath();
            if (!agent.IsAlive)
            {
                HandleColonistDeath(agent);
                continue;
            }

            var center = agent.Motion.Position + agent.Motion.Size / 2f;
            if (agent.IsDowned)
            {
                // Downed: lies where they fell until wounds ease (or they bleed out).
                agent.Mind.Breaker.End();
                agent.Activity = $"Downed ({agent.Health.DownedReason})";
                _colonistMovement.Step(agent.Motion, new MovementInput(0f), deltaSeconds, World, Tiles);
                continue;
            }
            if (agent.Mind.Breaker.IsBreaking)
            {
                UpdateBreakBehavior(agent, center, deltaSeconds, elapsedHours);
                continue;
            }

            var toPlayer = playerCenter - center;
            var distance = toPlayer.Length();
            var safe = !_enemies.Any(enemy => enemy.IsAlive && !enemy.IsPassive &&
                Vector2.Distance(enemy.Motion.Position, agent.Motion.Position) < 8f);
            var atCamp = Vector2.Distance(agent.Motion.Position, agent.HomePosition) < 5.5f;
            agent.WanderTimer -= deltaSeconds;

            if (distance > 9f)
            {
                agent.Activity = "Following";
            }
            else if (!safe)
            {
                agent.Activity = "Alert";
            }
            else if (atCamp)
            {
                if (agent.Needs.RestReserve < 0.55f)
                {
                    agent.Activity = "Resting at camp";
                    var restBefore = agent.Needs.RestReserve;
                    agent.Needs.Restore(NeedKind.Rest, elapsedHours * 0.16f);
                    agent.Needs.Restore(NeedKind.Comfort, elapsedHours * 0.05f);
                    agent.Heal(elapsedHours * 2f);
                    if (restBefore < 0.54f && agent.Needs.RestReserve >= 0.54f)
                        agent.Mind.Thoughts.AddMemory(ThoughtId.RestedWell);
                }
                else if (agent.Needs.FoodReserve < 0.60f)
                {
                    agent.Activity = "Foraging";
                    agent.Needs.Restore(
                        NeedKind.Food,
                        elapsedHours * (0.075f + agent.ColonistStats.Level(ColonistSkill.Plants) * 0.005f));
                }
                else
                {
                    agent.Activity = "Working around camp";
                    agent.Needs.Restore(NeedKind.Joy, elapsedHours * 0.06f);
                    agent.Needs.Restore(NeedKind.Comfort, elapsedHours * 0.05f);
                }
            }
            else
            {
                agent.Activity = "Wandering";
            }

            Vector2 target;
            if (distance > 9f)
            {
                target = playerCenter;
            }
            else if (agent.Activity == "Resting at camp")
            {
                target = center;
            }
            else
            {
                if (distance < 4f)
                    agent.HomePosition = agent.Motion.Position;
                if (agent.WanderTimer <= 0f)
                {
                    agent.WanderTimer = 2.5f + (float)_random.NextDouble() * 3.5f;
                    var offset = ((float)_random.NextDouble() * 2f - 1f) * 4.5f;
                    agent.WanderTarget = new Vector2(
                        Math.Clamp(agent.HomePosition.X + offset, 1f, World.Width - 2f),
                        agent.HomePosition.Y);
                }
                target = agent.WanderTarget;
            }

            StepColonistToward(agent, center, target, deltaSeconds, 1f);
        }
    }

    private void StepColonistToward(ColonistAgent agent, Vector2 center, Vector2 target, float deltaSeconds, float speed)
    {
        var dx = target.X - center.X;
        var horizontal = MathF.Abs(dx) > 0.75f ? MathF.Sign(dx) : 0f;
        var jump = horizontal != 0f && agent.Motion.Grounded && IsBlockedAhead(agent, horizontal);
        _colonistMovement.Step(
            agent.Motion,
            new MovementInput(horizontal, JumpPressed: jump,
                SpeedMultiplier: agent.ColonistStats.MoveSpeedMultiplier * agent.MoveCapacityFactor * speed),
            deltaSeconds,
            World,
            Tiles);
    }

    private static readonly TileId[] FurnitureTiles =
    [
        TileId.Workbench, TileId.Furnace, TileId.Anvil, TileId.MiniTurret, TileId.HydroponicsTray,
        TileId.SolarPanel, TileId.OxygenUnit
    ];

    /// <summary>What a breaking colonist does instead of their normal routine.</summary>
    private void UpdateBreakBehavior(ColonistAgent agent, Vector2 center, float deltaSeconds, float elapsedHours)
    {
        var target = center;
        var speed = 0.8f;
        agent.BreakTimer -= elapsedHours;
        switch (agent.Mind.Breaker.ActiveKind)
        {
            case MentalBreakKind.SadWander:
                agent.Activity = "Wandering sadly";
                agent.WanderTimer -= deltaSeconds;
                if (agent.WanderTimer <= 0f)
                {
                    agent.WanderTimer = 3f + (float)_mindRandom.NextDouble() * 3f;
                    var offset = ((float)_mindRandom.NextDouble() * 2f - 1f) * 10f;
                    agent.WanderTarget = new Vector2(
                        Math.Clamp(agent.HomePosition.X + offset, 1f, World.Width - 2f), agent.HomePosition.Y);
                }
                target = agent.WanderTarget;
                speed = 0.6f;
                break;
            case MentalBreakKind.HideInRoom:
                var room = NearestRoom(center);
                agent.Activity = room is null ? "Hiding (no room nearby)" : "Hiding in a room";
                target = room?.Centroid ?? agent.HomePosition;
                break;
            case MentalBreakKind.FoodBinge:
                // The colony's food travels in the player's pack.
                agent.Activity = "Food binge";
                target = PlayerCenter;
                if (agent.BreakTimer <= 0f)
                {
                    agent.BreakTimer = 0.4f;
                    if (Player.Inventory.TryConsume(ItemId.Berry))
                    {
                        agent.Needs.Restore(NeedKind.Food, Items[ItemId.Berry].FoodRestore);
                        agent.Mind.Thoughts.AddMemory(ThoughtId.AteNanoBanana);
                    }
                    else
                    {
                        agent.Activity = "Food binge (nothing left)";
                    }
                }
                break;
            case MentalBreakKind.Tantrum:
                agent.Activity = "Tantrum";
                if (FindNearbyFurniture(center, 8) is { } spot)
                {
                    target = new Vector2(spot.X + 0.5f, spot.Y + 0.5f);
                    if (agent.BreakTimer <= 0f && Vector2.Distance(center, target) <= 2.5f)
                    {
                        agent.BreakTimer = 0.35f;
                        var name = Tiles[World.GetCell(spot).TileId].Name;
                        if (World.TryDamageTile(spot, 12, Tiles, out _))
                        {
                            _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, new[] { spot }));
                            PostMessage($"{agent.Name} smashed the {name} in a tantrum!");
                        }
                        else
                        {
                            PostMessage($"{agent.Name} is kicking the {name}.");
                        }
                    }
                }
                break;
            default:
                agent.Activity = "Dazed";
                speed = 0f;
                break;
        }

        if (speed <= 0f)
            _colonistMovement.Step(agent.Motion, new MovementInput(0f), deltaSeconds, World, Tiles);
        else
            StepColonistToward(agent, center, target, deltaSeconds, speed);
    }

    private RoomInfo? NearestRoom(Vector2 position)
    {
        RoomInfo? best = null;
        var bestDistance = 40f;
        foreach (var room in _regions.Rooms)
        {
            var distance = Vector2.Distance(position, room.Centroid);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = room;
            }
        }
        return best;
    }

    private GridPoint? FindNearbyFurniture(Vector2 center, int radius)
    {
        GridPoint? best = null;
        var bestDistance = float.MaxValue;
        var cx = (int)MathF.Floor(center.X);
        var cy = (int)MathF.Floor(center.Y);
        for (var y = Math.Max(0, cy - radius); y <= Math.Min(World.Height - 1, cy + radius); y++)
        for (var x = Math.Max(0, cx - radius); x <= Math.Min(World.Width - 1, cx + radius); x++)
        {
            var point = new GridPoint(x, y);
            if (Array.IndexOf(FurnitureTiles, World.GetCell(point).TileId) < 0)
                continue;
            var distance = Vector2.Distance(center, new Vector2(x + 0.5f, y + 0.5f));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = point;
            }
        }
        return best;
    }

    private void HandleColonistDeath(ColonistAgent agent)
    {
        if (!_deathsHandled.Add(agent.SocialKey))
            return;
        agent.Mind.Breaker.End();
        agent.Activity = "Dead";
        PostMessage(agent.Health.DeathCause is { } cause
            ? $"{agent.Name} has died ({cause})."
            : $"{agent.Name} has died.");
        Record(agent.Health.DeathCause is { } died ? $"{agent.Name} died ({died})." : $"{agent.Name} died.");
        ColonySocialSystem.NotifyDeath(agent, ColonyPawns);
    }

    /// <summary>Raises a body a quarter tile at a time (up to two tiles) until it no longer overlaps solid ground.</summary>
    private void LiftClear(PawnMotionState motion)
    {
        for (var step = 0; step < 8 && PawnMovementSystem.OverlapsSolid(motion.Position, motion.Size, World, Tiles); step++)
            motion.Position -= new Vector2(0f, 0.25f);
    }

    private bool IsBlockedAhead(ColonistAgent agent, float horizontal)
    {
        var motion = agent.Motion;
        var x = (int)MathF.Floor(motion.Position.X + motion.Size.X / 2f + MathF.Sign(horizontal) * motion.Size.X);
        var y = (int)MathF.Floor(motion.Position.Y + motion.Size.Y - 0.30f);
        if (x < 0 || y < 0 || x >= World.Width || y >= World.Height)
            return true;
        return Tiles[World.GetCell(new GridPoint(x, y)).TileId].IsSolid;
    }

    /// <summary>True when a step of terrain sits at foot level just ahead of a ground actor's box.</summary>
    private bool IsGroundBlockedAhead(PawnMotionState motion, float horizontal)
    {
        var x = (int)MathF.Floor(motion.Position.X + motion.Size.X / 2f + MathF.Sign(horizontal) * (motion.Size.X / 2f + 0.38f));
        var y = (int)MathF.Floor(motion.Position.Y + motion.Size.Y - 0.30f);
        if (x < 0 || y < 0 || x >= World.Width || y >= World.Height)
            return false;
        return Tiles[World.GetCell(new GridPoint(x, y)).TileId].IsSolid;
    }

    private void UpdateEnemies(float deltaSeconds)
    {
        var playerCenter = Player.Motion.Position + Player.Motion.Size / 2f;
        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive)
                continue;

            var archetype = enemy.Archetype;
            TickCreatureLava(enemy, deltaSeconds); // lava burns creatures that are not lava-immune (EN-5)
            if (!enemy.IsAlive)
                continue;
            enemy.AttackCooldown = Math.Max(0f, enemy.AttackCooldown - deltaSeconds);
            enemy.StateTime += deltaSeconds;
            if (archetype.Passive)
            {
                // Wildlife never reaches the contact-damage code below.
                UpdatePassiveCreature(enemy, playerCenter, deltaSeconds);
                continue;
            }
            var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
            var goalCenter = AggroGoal(enemy, playerCenter); // aggro: the player, or a colonist/resident drawing it off
            var delta = goalCenter - center;
            var distance = delta.Length();
            var alertRadius = enemy.IsBoss ? 48f : 22f;
            var horizontal = distance <= alertRadius && !archetype.Stationary ? Math.Sign(delta.X) : 0;
            var speed = archetype.MoveSpeedMultiplier;

            if (enemy.IsBoss)
                speed *= UpdateBossController(enemy, center, delta, distance, alertRadius);
            else
            {
                enemy.State = distance <= alertRadius ? EnemyState.Pursuing : EnemyState.Roaming;
            }

            var holding = enemy.State == EnemyState.Telegraphing;
            if (!enemy.IsBoss && archetype.Family != EnemyAiFamily.Default &&
                StepAiFamily(enemy, goalCenter, distance, alertRadius, deltaSeconds))
            {
            }
            else if (archetype.Flying)
            {
                // Flying bosses hover above the player instead of ramming into them.
                var goal = enemy.IsBoss ? playerCenter - new Vector2(0f, enemy.Motion.Size.Y * 0.5f + 2.5f) : goalCenter;
                MoveFlyingEnemy(enemy, goal, deltaSeconds, distance <= alertRadius && !holding, speed);
            }
            else
            {
                var movement = holding ? default : new MovementInput(horizontal, SpeedMultiplier: speed);
                if (!enemy.IsBoss && (enemy.Kind is EnemyKind.MossSlime or EnemyKind.RockMite || archetype.Hops) &&
                    enemy.Motion.Grounded && distance < alertRadius && enemy.StateTime > 1.1f)
                {
                    movement = movement with { JumpPressed = true };
                    enemy.StateTime = 0f;
                }
                else if (horizontal != 0 && enemy.Motion.Grounded && IsGroundBlockedAhead(enemy.Motion, horizontal))
                {
                    movement = movement with { JumpPressed = true };
                }
                _enemyMovement.Step(enemy.Motion, movement, deltaSeconds, World, Tiles);
            }

            center = enemy.Motion.Position + enemy.Motion.Size / 2f;
            distance = Vector2.Distance(center, playerCenter);
            if (!enemy.IsBoss && distance < 1.15f && enemy.AttackCooldown <= 0f)
            {
                var damage = HitPlayerFromEnemy(enemy, ScaleIncoming(enemy.AttackDamage), ColonistAnatomy.ContactDamageKind(archetype));
                ReportPartLoss(Player, Player.LastDamage);
                enemy.AttackCooldown = archetype.ContactCooldown;
                var away = Math.Sign(Player.Motion.Position.X - enemy.Motion.Position.X);
                Player.Motion.Velocity += GearKnockback(new Vector2(away * 3f, -2f));
                if (damage > 0f)
                    PostMessage($"{enemy.DisplayName} hit you for {damage:0}.");
                if (damage > 0f && CombatStats.Thorns > 0f)
                    enemy.TakeDamage(damage * CombatStats.Thorns);
            }
            else if (enemy.IsBoss && enemy.AttackCooldown <= 0f && !holding &&
                     DistanceToRect(playerCenter, enemy.Motion.Position, enemy.Motion.Size) < 0.6f)
            {
                HitPlayerFromEnemy(enemy, ScaleIncoming(enemy.AttackDamage), DamageKind.Crush);
                ReportPartLoss(Player, Player.LastDamage);
                enemy.AttackCooldown = 1.4f;
                PostMessage($"The {enemy.DisplayName} struck you.");
            }
        }

        var defeated = _enemies.Where(enemy => !enemy.IsAlive).ToArray();
        foreach (var enemy in defeated)
        {
            foreach (var drop in BossDropsOrBag(enemy))
                DropLoot(enemy, drop.Item, drop.Quantity);
            OnCreatureDefeated(enemy);

            if (enemy.IsBoss)
            {
                if (!_defeatedBosses.Contains(enemy.Kind))
                    Record($"The {enemy.DisplayName} was defeated.");
                _defeatedBosses.Add(enemy.Kind);
                AddColonyMemory(ThoughtId.BossDefeated);
                PostMessage(enemy.Kind == EnemyKind.StoneSentinel
                    ? $"The {enemy.DisplayName} fell. The deep-core is no longer quiet."
                    : $"The {enemy.DisplayName} has been defeated!");
            }
            else
            {
                PostMessage($"Defeated {enemy.DisplayName}.");
                Player.Mind.Thoughts.AddMemory(ThoughtId.KilledCreature);
            }
            OnHardmodeDefeat(enemy);
            _enemies.Remove(enemy);
        }
    }

    /// <summary>
    /// Wildlife: drifts about on its own, bolts away from the player for a few seconds after
    /// being hurt, and never attacks.
    /// </summary>
    private void UpdatePassiveCreature(EnemyAgent enemy, Vector2 playerCenter, float deltaSeconds)
    {
        enemy.FleeSeconds = Math.Max(0f, enemy.FleeSeconds - deltaSeconds);
        var fleeing = enemy.FleeSeconds > 0f;
        enemy.State = EnemyState.Roaming;
        var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
        var away = center.X >= playerCenter.X ? 1f : -1f;
        if (enemy.Archetype.Flying)
        {
            var goal = center + new Vector2(away * 6f, -2.5f);
            MoveFlyingEnemy(enemy, goal, deltaSeconds, pursuing: fleeing, enemy.Archetype.MoveSpeedMultiplier * 1.4f);
            return;
        }

        // Wander in three-second legs: left, pause, or right, chosen per creature.
        var leg = (enemy.Id * 7 + TickNumber / 180) % 3;
        var horizontal = fleeing ? away : leg - 1;
        var speed = enemy.Archetype.MoveSpeedMultiplier * (fleeing ? 1.6f : 0.45f);
        var movement = new MovementInput(horizontal, SpeedMultiplier: speed);
        if (horizontal != 0f && enemy.Motion.Grounded && IsGroundBlockedAhead(enemy.Motion, horizontal))
            movement = movement with { JumpPressed = true };
        _enemyMovement.Step(enemy.Motion, movement, deltaSeconds, World, Tiles);
    }

    /// <summary>
    /// Shared boss controller: approach, telegraph, fire a fanned volley, recover. Below the
    /// archetype's HP threshold the boss enters phase 2 with faster shots, cooldowns, and movement.
    /// Returns the movement speed factor for this tick.
    /// </summary>
    private float UpdateBossController(EnemyAgent enemy, Vector2 center, Vector2 delta, float distance, float alertRadius)
    {
        var archetype = enemy.Archetype;
        var previousPhase = enemy.Phase;
        enemy.Phase = enemy.HitPoints / enemy.MaximumHitPoints <= archetype.PhaseThreshold ? 1 : 0;
        var enraged = enemy.Phase > 0;
        if (enraged && previousPhase == 0)
            PostMessage($"The {enemy.DisplayName} overloads its systems!");

        // The legacy sentinel keeps its exact 2.1s/1.45s cadence; other bosses scale the same way.
        var attackRange = archetype.AttackRange + MathF.Max(0f, MathF.Max(enemy.Motion.Size.X, enemy.Motion.Size.Y) - 1.8f) * 0.5f;
        if (enemy.State == EnemyState.Telegraphing)
        {
            if (enemy.StateTime >= archetype.TelegraphSeconds)
            {
                FireBossVolley(enemy, center, delta, enraged);
                enemy.State = EnemyState.Recovering;
                enemy.StateTime = 0f;
            }
        }
        else if (distance < attackRange && enemy.AttackCooldown <= 0f)
        {
            enemy.State = EnemyState.Telegraphing;
            enemy.StateTime = 0f;
            enemy.AttackCooldown = enraged ? archetype.AttackCooldown * (1.45f / 2.1f) : archetype.AttackCooldown;
            PostMessage($"The {enemy.DisplayName} charges an attack...");
        }
        else if (enemy.State == EnemyState.Recovering && enemy.StateTime > 0.35f)
        {
            enemy.State = EnemyState.Pursuing;
        }
        else if (enemy.State != EnemyState.Recovering && distance <= alertRadius)
        {
            enemy.State = EnemyState.Pursuing;
        }
        return enraged ? 1.3f : 1f;
    }

    private void FireBossVolley(EnemyAgent enemy, Vector2 center, Vector2 delta, bool enraged)
    {
        var archetype = enemy.Archetype;
        var aim = delta.LengthSquared() < 0.001f ? Vector2.UnitX : Vector2.Normalize(delta);
        var speed = archetype.ProjectileSpeed * (enraged ? 1.25f : 1f);
        var count = Math.Max(1, archetype.VolleyCount + (enraged && archetype.VolleyCount > 1 ? 1 : 0));
        const float fanStep = 0.17f;
        var baseAngle = MathF.Atan2(aim.Y, aim.X) - fanStep * (count - 1) / 2f;
        for (var shot = 0; shot < count; shot++)
        {
            var angle = baseAngle + fanStep * shot;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            _projectiles.Add(new ProjectileState(
                _nextEntityId++, center, direction * speed,
                CapBossHit(enemy, enemy.AttackDamage * DefenseBalance.EnemyHitScale(enemy.Archetype), preDifficulty: true), 3f, 4f,
                ProjectileTeam.Enemy, enemy.DisplayName,
                enemy.Kind == EnemyKind.SolarFlareEntity ? DamageKind.Burn : DamageKind.Shot));
        }
        PostMessage(count == 1
            ? $"The {enemy.DisplayName} fires a bolt!"
            : $"The {enemy.DisplayName} fires {(count is 8 or 11 or 18 ? "an" : "a")} {count}-shot volley!");
    }

    private void MoveFlyingEnemy(EnemyAgent enemy, Vector2 target, float deltaSeconds, bool pursuing, float speedMultiplier = 1f)
    {
        var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
        var desired = pursuing ? target - center : new Vector2(MathF.Sin((TickNumber + enemy.Id * 31) * 0.035f), 0.3f);
        if (enemy.IsBoss && !pursuing)
            desired = Vector2.Zero;
        if (desired.LengthSquared() > 0.001f)
            desired = Vector2.Normalize(desired) * (pursuing ? 2.8f : 1.1f) * speedMultiplier;
        enemy.Motion.Velocity = Vector2.Lerp(enemy.Motion.Velocity, desired, Math.Min(1f, deltaSeconds * 2.4f));

        var displacement = enemy.Motion.Velocity * deltaSeconds;
        var steps = Math.Max(1, (int)MathF.Ceiling(displacement.Length() / 0.12f));
        var increment = displacement / steps;
        for (var i = 0; i < steps; i++)
        {
            var candidate = enemy.Motion.Position + increment;
            if (enemy.IsBoss)
            {
                // Huge flying bosses drift through terrain but stay inside the world.
                enemy.Motion.Position = Vector2.Clamp(candidate, Vector2.Zero,
                    new Vector2(World.Width, World.Height) - enemy.Motion.Size);
                continue;
            }
            if (IsSolidActorPosition(candidate, enemy.Motion.Size))
            {
                enemy.Motion.Velocity = -enemy.Motion.Velocity * 0.35f;
                break;
            }
            enemy.Motion.Position = candidate;
        }
    }

    private void UpdateProjectiles(float deltaSeconds)
    {
        for (var i = _projectiles.Count - 1; i >= 0; i--)
        {
            var projectile = _projectiles[i];
            if (projectile.Style != WeaponStyle.None && StepStyledProjectile(i, deltaSeconds)) continue;
            projectile.RemainingSeconds -= deltaSeconds;
            var travel = projectile.Velocity * deltaSeconds;
            var steps = Math.Max(1, (int)MathF.Ceiling(travel.Length() / 0.2f));
            var increment = travel / steps;
            var remove = projectile.RemainingSeconds <= 0f;
            Vector2? impact = null;
            for (var step = 0; step < steps && !remove; step++)
            {
                var next = projectile.Position + increment;
                var tile = new GridPoint((int)MathF.Floor(next.X), (int)MathF.Floor(next.Y));
                if (!World.Contains(tile) || Tiles[World.GetCell(tile).TileId].IsSolid)
                {
                    remove = true;
                    impact = next;
                    break;
                }

                projectile.Position = next;
                if (projectile.Team == ProjectileTeam.Player) CutPlantAt(tile, FloraCut.Projectile); // flora parity: shots cut plants
                if (projectile.Team == ProjectileTeam.Player)
                {
                    var target = _enemies.FirstOrDefault(enemy => enemy.IsAlive &&
                        EnemyProjectileDistance(enemy, projectile.Position) <= 0.35f);
                    if (target is not null && projectile.HitEnemies?.Contains(target.Id) != true)
                    {
                        var dealt = StrikeEnemy(target, projectile.Damage, projectile.Critical, projectile.Class);
                        ApplyProjectileAffliction(projectile, target);
                        var direction = Math.Sign(projectile.Velocity.X);
                        KnockEnemy(target, new Vector2(direction * projectile.Knockback, -projectile.Knockback * 0.15f), LastHitCritical);
                        if (projectile.Class == DamageClass.Melee && CombatStats.Lifesteal > 0f)
                            Player.Heal(dealt * CombatStats.Lifesteal);
                        GearOnHitEnemy(target, dealt, projectile.Class);
                        if (projectile.PierceRemaining > 0)
                        {
                            projectile.PierceRemaining--;
                            (projectile.HitEnemies ??= new HashSet<long>()).Add(target.Id);
                        }
                        else
                            remove = true;
                    }
                }
                else if (DistanceToRect(projectile.Position, Player.Motion.Position, Player.Motion.Size) <= 0.4f)
                {
                    GearHitPlayer(null, ScaleIncoming(projectile.Damage), projectile.Kind);
                    ReportPartLoss(Player, Player.LastDamage);
                    Player.Motion.Velocity += GearKnockback(Vector2.Normalize(projectile.Velocity) * projectile.Knockback);
                    remove = true;
                    PostMessage(projectile.SourceName is { } source
                        ? $"The {source}'s bolt hit you for {projectile.Damage:0}."
                        : $"An energy bolt hit you for {projectile.Damage:0}.");
                }
            }

            if (remove)
            {
                if (projectile.PurifyRadius > 0f)
                    PurifyArea(ClampCell(RegionMap.CellAt(impact ?? projectile.Position)), projectile.PurifyRadius);
                _projectiles.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Formerly the player's RimWorld-style brain (auto-eating berries, auto-resting for faster healing, and joy and comfort
    /// restored for free near camp). Terraria's player never acts or recovers on its own, so the player now has no
    /// autonomous behaviour at all: hunger is met by eating food yourself, health comes back only by Terraria's natural
    /// regeneration, gear, buffs and the items you use.
    /// </summary>
    private void UpdateAutonomousBehavior(float elapsedHours) =>
        _autonomousActivity = new ActivityChoice(ActivityKind.Idle, 0f, "the player acts only on input");

    // UpdateSpawning lives in GameSimulation.Spawning.cs.

    /// <summary>
    /// Slow story tick: every <see cref="EnvironmentIntervalTicks"/> ticks refresh the biome
    /// reading, rebuild rooms if tiles changed, move room temperatures, and apply comfort,
    /// exposure, and fallout to the player and crew.
    /// </summary>
    private void UpdateEnvironment(float elapsedHours)
    {
        _environmentHours += elapsedHours;
        if (TickNumber % EnvironmentIntervalTicks != 0)
            return;
        var hours = _environmentHours;
        _environmentHours = 0f;

        CurrentBiome = BiomeScanner.Scan(World, PlayerCell());
        // Rain and snow chill the air a little on top of the storyteller's cold snaps and heat waves.
        _temperature.OutdoorOffset = _storyteller.TemperatureOffset - 3f * Weather.Rain - 5f * Weather.Snow;
        ApplyWeatherEffects(hours);
        _regions.EnsureCurrent();
        if (_roomBiomeBuild != _regions.FullBuildCount)
        {
            _roomBiomes.Clear();
            _roomBiomeBuild = _regions.FullBuildCount;
        }
        else if (_roomBiomes.Count > 512)
        {
            foreach (var stale in _roomBiomes.Keys.Where(room => !room.IsLive).ToArray())
                _roomBiomes.Remove(stale);
        }
        var playerCenter = PlayerCenter;
        foreach (var room in _regions.Rooms)
        {
            // Only rooms near the colony matter; far caves on huge worlds keep their last temperature.
            if (Vector2.DistanceSquared(room.Centroid, playerCenter) > RoomSimulationRadius * RoomSimulationRadius)
                continue;
            if (!_roomBiomes.TryGetValue(room, out var roomBiome))
            {
                roomBiome = BiomeScanner.Scan(World, ClampCell(RegionMap.CellAt(room.Centroid)));
                _roomBiomes[room] = roomBiome;
            }
            var outdoor = _temperature.OutdoorAt(roomBiome.Zone, roomBiome.Underground, ClockHour);
            var sources = TemperatureModel.CountSources(room, World, Tiles);
            room.Temperature = TemperatureModel.StepRoom(room.Temperature, outdoor, room, sources, roomBiome.Underground, hours);
        }

        PlayerAmbientTemperature = AmbientAt(PlayerCenter, CurrentBiome);
        PlayerComfort = TemperatureModel.Evaluate(PlayerAmbientTemperature, Player.Stats.ImmunityTags);
        ApplyThermalComfort(PlayerComfort, Player.Needs, hours,
            (amount, cause) => Player.TakeExposureDamage(amount, cause));
        if (PlayerComfort.Dangerous && !_exposureWarned)
            PostMessage(PlayerComfort.TooCold
                ? $"Frostbite risk at {PlayerAmbientTemperature:0} °C: get indoors or wear insulation."
                : $"Heatstroke risk at {PlayerAmbientTemperature:0} °C: get indoors or wear a heat ward.");
        _exposureWarned = PlayerComfort.Dangerous;

        var playerIndoors = PlayerRoom is not null;
        if (_storyteller.FalloutActive && !playerIndoors)
        {
            Player.Statuses.Apply(StatusId.Poison, 1f, Player.Stats.ImmunityTags);
            // Filter masks and suits ("toxic" gear) keep the fallout out of the lungs.
            if (!Player.Stats.ImmunityTags.Contains("toxic"))
                Player.Health.AddWholeBody(ConditionKind.RadiationSickness, hours * 0.03f);
        }

        foreach (var agent in _colonists)
        {
            if (!agent.IsAlive)
                continue;
            var center = agent.Motion.Position + agent.Motion.Size / 2f;
            var comfort = TemperatureModel.Evaluate(AmbientAt(center, CurrentBiome), null);
            ApplyThermalComfort(comfort, agent.Needs, hours, (amount, cause) => agent.TakeExposureDamage(amount, cause));
            if (_storyteller.FalloutActive && _regions.GetRoomAt(center) is null)
                agent.TakeExposureDamage(hours * 1.2f, "radiation-sickness");
        }

        UpdateMinds(hours);
    }

    /// <summary>
    /// Story-cadence pass over every mind: age memories, recompute situational thoughts, ease mood,
    /// track/roll mental breaks, let medics tend the wounded, run social interactions, and notice
    /// raids that have been beaten off.
    /// </summary>
    private void UpdateMinds(float hours)
    {
        if (hours <= 0f)
            return;
        var night = ClockHour >= 18f || ClockHour < 6f;
        var living = ColonyPawns.Where(pawn => pawn.IsAlive).ToArray();
        foreach (var pawn in living)
        {
            var center = pawn.Motion.Position + pawn.Motion.Size / 2f;
            var indoors = _regions.GetRoomAt(center) is not null;
            var comfort = pawn.IsPlayerCharacter
                ? PlayerComfort
                : TemperatureModel.Evaluate(AmbientAt(center, CurrentBiome), null);
            var mind = pawn.Mind;
            mind.Thoughts.Advance(hours);
            mind.Thoughts.SetSituational(ComputeSituationalThoughts(pawn, comfort, indoors, night, living));
            mind.UpdateMood(pawn.Needs, pawn.ColonistStats, hours);
            mind.Breaker.Track(mind.Mood.Current, hours);

            if (pawn.IsPlayerCharacter)
            {
                // The player never loses control: they get a visible warning and, at the extreme, slower work.
                if (Player.OnTheEdge && !_playerEdgeWarned)
                    PostMessage($"{Player.Name} is on the edge ({mind.Breaker.StateLabel}).");
                else if (!Player.OnTheEdge && _playerEdgeWarned)
                    PostMessage($"{Player.Name} has steadied.");
                _playerEdgeWarned = Player.OnTheEdge;
                continue;
            }

            if (mind.Breaker.IsBreaking)
            {
                if (mind.Breaker.Advance(hours))
                {
                    mind.Thoughts.AddMemory(ThoughtId.Catharsis);
                    PostMessage($"{pawn.Name} has pulled themselves together.");
                }
            }
            else
            {
                var started = mind.Breaker.TryStartBreak(_mindRandom, hours, canBreak: !pawn.IsDowned,
                    hasFood: Player.Inventory.Count(ItemId.Berry) > 0);
                if (started != MentalBreakKind.None)
                    PostMessage($"{pawn.Name} is having a {mind.Breaker.ActiveSeverity.ToString().ToLowerInvariant()} " +
                                $"mental break: {MentalBreaker.Describe(started)}.");
            }
        }

        UpdateTending(living);

        foreach (var result in _social.Update(living, _elapsedGameHours))
        {
            if (result.SocialFight || result.Kind is InteractionKind.Insult or InteractionKind.RomanceAttempt)
                PostMessage(result.Message);
        }

        if (_raidEnemyIds.Count > 0 && !_enemies.Any(enemy => _raidEnemyIds.Contains(enemy.Id)) && !Player.IsDead)
        {
            _raidEnemyIds.Clear();
            AddColonyMemory(ThoughtId.RaidSurvived);
            PostMessage("The raid has been beaten off.");
        }
    }

    private IEnumerable<(ThoughtId Id, float Scale, string? OtherName)> ComputeSituationalThoughts(
        IColonyPawn pawn, ThermalComfort comfort, bool indoors, bool night, IReadOnlyList<IColonyPawn> living)
    {
        var health = pawn.Health;
        var pain = health.PainLevel;
        if (pain >= 0.8f) yield return (ThoughtId.MindShatteringPain, 1f, null);
        else if (pain >= 0.55f) yield return (ThoughtId.IntensePain, 1f, null);
        else if (pain >= 0.3f) yield return (ThoughtId.SeriousPain, 1f, null);
        else if (pain >= 0.1f) yield return (ThoughtId.MinorPain, 1f, null);
        if (health.IsBleeding)
            yield return (ThoughtId.Bleeding, 1f, null);
        foreach (var missing in health.Injuries.Where(injury => injury.Kind == ConditionKind.MissingPart))
            yield return (ThoughtId.MissingBodyPart, 1f, missing.Part.Name);
        if (SocialRules.IsDisfigured(health))
            yield return (ThoughtId.Disfigured, 1f, null);
        if (health.HasInfection)
            yield return (ThoughtId.Infected, 1f, null);
        if (health.RadiationSickness >= 0.15f)
            yield return (ThoughtId.RadiationSick, 1f, null);
        if (pawn.IsDowned)
            yield return (ThoughtId.Downed, 1f, null);

        var needs = pawn.Needs;
        if (needs.FoodReserve < 0.08f) yield return (ThoughtId.Starving, 1f, null);
        else if (needs.FoodReserve < 0.25f) yield return (ThoughtId.Hungry, 1f, null);
        if (needs.RestReserve < 0.10f) yield return (ThoughtId.Exhausted, 1f, null);
        else if (needs.RestReserve < 0.25f) yield return (ThoughtId.Tired, 1f, null);

        if (!comfort.Comfortable)
        {
            yield return comfort.TooCold
                ? (comfort.Dangerous ? ThoughtId.Freezing : ThoughtId.Chilly, 1f, null)
                : (comfort.Dangerous ? ThoughtId.Overheating : ThoughtId.Sweltering, 1f, null);
        }
        if (_storyteller.FalloutActive && !indoors)
            yield return (ThoughtId.ToxicFalloutOutdoors, 1f, null);
        if (night)
            yield return (indoors ? ThoughtId.ShelteredAtNight : ThoughtId.ExposedAtNight, 1f, null);

        var loverNearby = false;
        foreach (var other in living)
        {
            if (ReferenceEquals(other, pawn))
                continue;
            var relation = SocialRules.DerivedRelation(pawn, other);
            if (relation is RelationKind.Lover or RelationKind.Partner)
                loverNearby = true;
            else if (relation == RelationKind.Rival)
                yield return (ThoughtId.RivalInColony, 1f, other.Name);
            else if (relation == RelationKind.Friend || SocialRules.Opinion(pawn, other) >= SocialRules.FriendOpinion)
                yield return (ThoughtId.FriendInColony, 1f, other.Name);
        }
        if (loverNearby)
            yield return (ThoughtId.LoverNearby, 1f, null);
    }

    /// <summary>
    /// Colonists with medical skill tend the nearest wounded crew member. Never the player: Terraria's player gets no free
    /// healing, so the player's wounds are tended only by the player (TendSelf) or healed by what the player uses.
    /// </summary>
    private void UpdateTending(IReadOnlyList<IColonyPawn> living)
    {
        foreach (var medic in _colonists)
        {
            if (!medic.IsAlive || medic.IsDowned || medic.Mind.Breaker.IsBreaking ||
                medic.ColonistStats.Level(ColonistSkill.Medical) < 1 || medic.Health.Manipulation < 0.2f)
                continue;
            var center = medic.Motion.Position + medic.Motion.Size / 2f;
            var patient = living
                .Where(pawn => !ReferenceEquals(pawn, Player) && pawn.Health.NeedsTending &&
                               Vector2.Distance(center, pawn.Motion.Position + pawn.Motion.Size / 2f) <= 6f)
                .OrderBy(pawn => ReferenceEquals(pawn, medic) ? 1 : 0)
                .ThenByDescending(pawn => pawn.IsDowned)
                .ThenByDescending(pawn => pawn.Health.BleedRate)
                .FirstOrDefault();
            if (patient is null)
                continue;
            var self = ReferenceEquals(patient, medic);
            var quality = medic.ColonistStats.MedicalTendQuality * (0.85f + 0.3f * (float)_mindRandom.NextDouble())
                          * Math.Min(1f, medic.Health.Manipulation) * (self ? 0.7f : 1f);
            quality = Math.Clamp(quality, 0.05f, 1f);
            if (patient.Health.Tend(quality) <= 0)
                continue;
            medic.Activity = $"Tending {patient.Name}";
            PostMessage(self
                ? $"{medic.Name} dressed their own wounds ({quality:P0} quality)."
                : $"{medic.Name} tended {patient.Name}'s wounds ({quality:P0} quality).");
        }
    }

    /// <summary>Gives every living colony member (player included) the same memory.</summary>
    private void AddColonyMemory(ThoughtId id)
    {
        foreach (var pawn in ColonyPawns)
            if (pawn.IsAlive)
                pawn.Mind.Thoughts.AddMemory(id);
    }

    private static void ApplyThermalComfort(ThermalComfort comfort, SurvivalNeeds needs, float hours, Action<float, string> injure)
    {
        if (hours <= 0f)
            return;
        if (!comfort.Comfortable)
            needs.Drain(NeedKind.Comfort, hours * 0.04f * Math.Min(3f, comfort.DegreesOutsideComfort / 8f + 0.25f));
        if (comfort.Dangerous)
            injure(hours * 1.5f * Math.Min(3f, comfort.DegreesOutsideSafe / 10f + 0.5f), comfort.TooCold ? "frostbite" : "heatstroke");
    }

    /// <summary>Room air when enclosed; otherwise outdoor air plus warmth from nearby lava.</summary>
    private float AmbientAt(Vector2 position, BiomeReading reading)
    {
        var room = _regions.GetRoomAt(position);
        if (room is not null && !float.IsNaN(room.Temperature))
            return room.Temperature;
        var outdoor = _temperature.OutdoorAt(reading.Zone, reading.Underground, ClockHour);
        return outdoor + TemperatureModel.LavaWarmth(World, ClampCell(RegionMap.CellAt(position)));
    }

    /// <summary>Story tick: the storyteller gets one check per in-game hour.</summary>
    private void UpdateStory()
    {
        var hour = (long)MathF.Floor(_elapsedGameHours);
        if (hour <= _lastStoryHour)
            return;
        _lastStoryHour = hour;
        _storyteller.OnHour(this);
        UpdateTownHourly();
    }

    private Vector2 PlayerCenter => Player.Motion.Position + Player.Motion.Size / 2f;

    private GridPoint PlayerCell() => ClampCell(RegionMap.CellAt(PlayerCenter));

    private GridPoint ClampCell(GridPoint cell) =>
        new(Math.Clamp(cell.X, 0, World.Width - 1), Math.Clamp(cell.Y, 0, World.Height - 1));

    /// <summary>Surface ground normally; when the player is underground, ground near their depth.</summary>
    private bool TryFindSpawnGround(int x, bool nearPlayerDepth, out int groundY)
    {
        if (nearPlayerDepth)
        {
            var playerY = (int)MathF.Floor(PlayerCenter.Y);
            for (var distance = 0; distance <= 10; distance++)
            for (var sign = -1; sign <= 1; sign += 2)
            {
                var y = playerY + sign * distance;
                if (y < 3 || y >= World.Height)
                    continue;
                if (Tiles[World.GetCell(new GridPoint(x, y)).TileId].IsSolid &&
                    !Tiles[World.GetCell(new GridPoint(x, y - 1)).TileId].IsSolid &&
                    !Tiles[World.GetCell(new GridPoint(x, y - 2)).TileId].IsSolid &&
                    World.GetCell(new GridPoint(x, y - 1)).LiquidKind != LiquidKind.Lava)
                {
                    groundY = y;
                    return true;
                }
            }
        }
        return TryFindGround(x, out groundY);
    }

    private static float ItemValue(ItemDefinition item) => item switch
    {
        { ShopPrice: > 0 } => item.ShopPrice,
        { UseKind: ItemUseKind.Ranged or ItemUseKind.Melee or ItemUseKind.Pickaxe } => 40f,
        { UseKind: ItemUseKind.PlaceTile, IsMaterial: true } => 0.2f,
        { UseKind: ItemUseKind.PlaceTile } => 15f,
        { Id: ItemId.CopperBar } => 6f,
        { IsMaterial: true } => 3f,
        _ => 1f
    };

    /// <summary>Wealth proxy for threat scaling: carried inventory and worn gear at rough values.</summary>
    public float ColonyWealth
    {
        get
        {
            var total = 0f;
            foreach (var stack in Player.Inventory.Slots)
                if (!stack.IsEmpty)
                    total += ItemValue(Items[stack.ItemId]) * stack.Count;
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
                if (Player.Equipment[slot] is { } worn)
                    total += ItemValue(Items[worn]);
            return total;
        }
    }

    private bool RadiatedTerrainNearby()
    {
        var center = PlayerCell().X;
        var found = 0;
        for (var x = Math.Max(0, center - 40); x <= Math.Min(World.Width - 1, center + 40); x++)
        for (var y = 0; y < World.Height; y++)
        {
            if (World.GetCell(new GridPoint(x, y)).TileId is TileId.CrimsonSoil or TileId.CorruptRock or TileId.UraniumOre &&
                ++found >= 20)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The two crew members a world started with before crew arrived through housing. Only loading a save from then
    /// recreates them (same seed, same people), so their saved state has someone to land on.
    /// </summary>
    private void AddLegacyStartingCrew(int count)
    {
        var crew = ColonistCatalog.GenerateCrew(Seed, 2, PlayerProfile.Name);
        for (var index = 0; index < Math.Min(count, crew.Count); index++)
        {
            var x = Math.Clamp(SpawnColumn + (index == 0 ? 5 : -4), 2, World.Width - 3);
            var agent = new ColonistAgent(_nextEntityId++, crew[index], new Vector2(x, FindSurface(x) - HumanoidBody.Size.Y),
                healthSeed: unchecked(Seed * 31 + 7 * (index + 1)));
            agent.HomePosition = agent.Motion.Position;
            agent.WanderTarget = agent.Motion.Position;
            agent.SocialKey = _colonists.Count + 1;
            _colonists.Add(agent);
        }
    }

    /// <summary>
    /// Gives a world the two crew members (and their seeded relations) every world used to start with, for checks that
    /// exercise crew systems on a fresh world. A save of such a world reloads them the way older saves do.
    /// </summary>
    internal void AddStartingCrewForChecks()
    {
        AddLegacyStartingCrew(2);
        ColonySocialSystem.SeedStartingRelations(ColonyPawns, Seed);
    }

    private ColonistAgent AddWanderer(int wandererIndex, Vector2 near)
    {
        // Deterministic per world and arrival order; avoid repeating a crew member's name.
        var taken = new HashSet<string>(_colonists.Select(agent => agent.Profile.Name), StringComparer.OrdinalIgnoreCase)
            { PlayerProfile.Name };
        var crew = ColonistCatalog.GenerateCrew(unchecked(Seed + 7919 * (wandererIndex + 1)), 4, PlayerProfile.Name);
        var profile = crew.FirstOrDefault(candidate => !taken.Contains(candidate.Name)) ?? crew[0];
        var x = Math.Clamp((int)MathF.Floor(near.X) + (wandererIndex % 2 == 0 ? 3 : -3), 2, World.Width - 3);
        var agent = new ColonistAgent(_nextEntityId++, profile, new Vector2(x, FindSurface(x) - HumanoidBody.Size.Y),
            healthSeed: unchecked(Seed * 31 + 7919 * (wandererIndex + 1)));
        agent.SocialKey = _colonists.Count + 1;
        _colonists.Add(agent);
        return agent;
    }

    StoryContext IStoryHost.CaptureStoryContext() => new(
        Day, ClockHour, CurrentBiome.Zone,
        _colonists.Count(agent => agent.IsAlive),
        _defeatedBosses.Count,
        ColonyWealth,
        _enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive),
        RadiatedTerrainNearby(),
        Player.MaximumHitPoints <= 0f ? 0f : Player.HitPoints / Player.MaximumHitPoints,
        CrewShortage: Math.Max(0, CrewCapacity - _colonists.Count(agent => agent.IsAlive)),
        LostResidents: LostResidentKinds().Count(),
        FreeHouse: FindFreeHouse() is not null);

    int IStoryHost.SpawnRaidWave(BiomeZone biome, float threatPoints, out string description)
    {
        var count = Math.Clamp((int)MathF.Round(threatPoints / 45f), 2, 8);
        var side = _storyteller.NextUnit() < 0.5f ? -1 : 1;
        var night = ClockHour >= 18f || ClockHour < 6f;
        var arrivals = new List<string>();
        var playerX = (int)MathF.Floor(PlayerCenter.X);
        for (var index = 0; index < count; index++)
        {
            var kind = BiomeSpawnTables.Pick(biome, _storyteller.NextUnit(), night);
            var archetype = EnemyArchetypes.Get(kind);
            var x = playerX + side * ((int)ViewHalfWidth + 3 + index * 2);
            if (x < 2 || x >= World.Width - 2)
            {
                side = -side;
                x = playerX + side * ((int)ViewHalfWidth + 3 + index * 2);
                if (x < 2 || x >= World.Width - 2)
                    continue;
            }
            if (!TryFindSpawnGround(x, CurrentBiome.Underground, out var groundY))
                continue;
            var lift = archetype.Flying ? 3f : 0f;
            _raidEnemyIds.Add(SpawnCreature(kind, new Vector2(x, groundY - archetype.Size.Y - lift)).Id);
            arrivals.Add(archetype.DisplayName);
        }
        description = arrivals.Count == 0
            ? string.Empty
            : $"{arrivals.Count} hostiles ({string.Join(", ", arrivals.Distinct())}) are";
        return arrivals.Count;
    }

    string IStoryHost.DeliverCargo(BiomeZone biome, float threatPoints)
    {
        var cells = 20 + (int)(threatPoints / 10f);
        // Before the first boss a pod carries supplies only, so smelting your own bars stays part of the climb.
        var alloy = _defeatedBosses.Count == 0 ? 0 : 2 + (int)(threatPoints / 150f);
        Player.Inventory.Add(ItemId.Berry, 6);
        Player.Inventory.Add(ItemId.Arrow, cells);
        if (alloy > 0)
            Player.Inventory.Add(ItemId.CopperBar, alloy);
        var bonus = biome switch
        {
            BiomeZone.FrozenWasteland => ItemId.FrostCrystal,
            BiomeZone.RadiatedDeeps => ItemId.CausticShard,
            BiomeZone.MoltenChasm => ItemId.ObsidianShard,
            BiomeZone.JunkWasteland => ItemId.OxidizedShard,
            BiomeZone.Overgrowth => ItemId.ViridiumOre,
            _ => ItemId.EnergyCrystal
        };
        Player.Inventory.Add(bonus, 2);
        AddColonyMemory(ThoughtId.CargoDropArrived);
        return alloy > 0
            ? $"6 {Items[ItemId.Berry].Name}s, {cells} {Items[ItemId.Arrow].Name}s, {alloy} {Items[ItemId.CopperBar].Name} and 2 {Items[bonus].Name}s"
            : $"6 {Items[ItemId.Berry].Name}s, {cells} {Items[ItemId.Arrow].Name}s and 2 {Items[bonus].Name}s";
    }

    bool IStoryHost.TryAddWanderer(int wandererIndex, out string name)
    {
        var agent = AddWanderer(wandererIndex, PlayerCenter);
        name = agent.Profile.Name;
        return true;
    }

    bool IStoryHost.TryWelcomeArrival(out string summary, out bool joinedCrew)
    {
        summary = string.Empty;
        joinedCrew = false;
        if (FindFreeHouse() is not { } house)
            return false;
        // A role lost to a death is filled first: someone new takes up the dead resident's trade.
        if (LostResidentKinds().Take(1).ToList() is [var lost])
        {
            Flags.Clear(LostFlag(lost));
            var resident = AddTownNpc(lost, StandingAt(house), bound: false);
            resident.HomeCell = house;
            summary = $"{resident.Name} the {resident.Definition.Role} arrives looking for a home and moves in.";
            Record($"{resident.Name} arrived to take up the {resident.Definition.Role}'s trade.");
            return true;
        }
        // Crew are limited by what the colony can support (CrewCapacity), not by how many came before: the dead are replaced.
        if (_colonists.Count(agent => agent.IsAlive) >= CrewCapacity)
            return false;
        var agent = AddWanderer(_storyteller.WanderersJoined, StandingAt(house));
        agent.Motion.Position = StandingAt(house);
        agent.HomePosition = agent.Motion.Position;
        agent.WanderTarget = agent.Motion.Position;
        joinedCrew = true;
        summary = $"{agent.Profile.Name} arrives, asks to join the crew, and moves in.";
        Record($"{agent.Profile.Name} joined the crew.");
        return true;
    }

    void IStoryHost.Announce(string message) => PostMessage(message);

    private void AdvanceClock(float elapsedHours)
    {
        var previous = ClockHour;
        _elapsedGameHours += elapsedHours;
        var absoluteHour = 8f + _elapsedGameHours;
        Day = 1 + (int)MathF.Floor(absoluteHour / 24f);
        ClockHour = absoluteHour % 24f;

        WorldEventBoundary? boundary = null;
        if (CrossedBoundary(previous, ClockHour, 6f, elapsedHours))
            boundary = WorldEventBoundary.Dawn;
        else if (CrossedBoundary(previous, ClockHour, 18f, elapsedHours))
            boundary = WorldEventBoundary.Dusk;

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ClockHour >= 18f || ClockHour < 6f ? "night" : "surface" };
        if (_defeatedBosses.Any(kind => EnemyArchetypes.Get(kind).Tier == BossTier.Easy))
            tags.Add(EasyBossDefeatedTag);
        var context = new WorldEventContext(
            ProgressionUnlocked: Day >= 2 || _defeatedBosses.Count > 0,
            BiomeTags: tags);
        var result = _eventManager.Advance(elapsedHours, boundary, context);
        foreach (var id in result.Ended)
            PostMessage($"Event ended: {_eventManager.GetDisplayName(id)}.");
        foreach (var id in result.Started)
        {
            PostMessage($"Event started: {_eventManager.GetDisplayName(id)}.");
            if (string.Equals(id, IncursionEventId, StringComparison.OrdinalIgnoreCase))
                TrySpawnEventBoss();
        }
        AdvanceContentClock(previous, elapsedHours, boundary);
        AdvanceHardmodeClock(boundary);
    }

    /// <summary>
    /// Spawns one dusk event boss, preferring ones not yet beaten. The choice uses the
    /// dedicated boss RNG so it is deterministic per seed.
    /// </summary>
    private bool TrySpawnEventBoss()
    {
        if (_enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive))
            return false;
        var eligible = EventBossPool();
        if (eligible.Length == 0)
        {
            PostMessage("Distant engines circle the colony, then turn away. Something stronger must fall before they dare land.");
            return false;
        }
        var pool = eligible.Where(kind => !_defeatedBosses.Contains(kind)).ToArray();
        if (pool.Length == 0)
            pool = eligible;
        var boss = SpawnBoss(pool[_bossRandom.Next(pool.Length)]);
        PostMessage($"Warning: the {boss.DisplayName} descends on the colony at dusk!");
        return true;
    }

    /// <summary>Pre-Fracture dusk incursions only bring event bosses under this many hit points (G2).</summary>
    public const float PreFractureEventBossMaxHitPoints = 1000f;

    /// <summary>
    /// Event bosses a dusk incursion may bring. After the Fracture: all of them. Before it: only those under
    /// <see cref="PreFractureEventBossMaxHitPoints"/>, or, when none qualify, all of them once the Scout Mech has fallen.
    /// </summary>
    public EnemyKind[] EventBossPool()
    {
        if (FractureActive)
            return EnemyArchetypes.EventBosses.ToArray();
        var light = EnemyArchetypes.EventBosses.Where(kind => EnemyArchetypes.Get(kind).HitPoints < PreFractureEventBossMaxHitPoints).ToArray();
        if (light.Length > 0)
            return light;
        return _defeatedBosses.Contains(EnemyKind.ScoutMech) ? EnemyArchetypes.EventBosses.ToArray() : [];
    }

    /// <summary>Test hook: runs the dusk incursion's boss pick.</summary>
    internal bool ProbeSpawnEventBoss() => TrySpawnEventBoss();

    private static string WithArticle(string name) =>
        ("AEIOUaeiou".Contains(name[0]) ? "An " : "A ") + name;

    private bool CrossedBoundary(float previousHour, float currentHour, float boundary, float elapsedHours)
    {
        if (elapsedHours >= 24f)
            return true;
        return currentHour >= previousHour
            ? previousHour < boundary && currentHour >= boundary
            : previousHour < boundary || currentHour >= boundary;
    }

    /// <summary>
    /// Tile reach is a box around the body: <see cref="CombatTuning.TileReachX"/> cells sideways and
    /// <see cref="CombatTuning.TileReachY"/> cells up or down past the cells the body touches, each plus the reach bonus
    /// (capped at 20) and the held item's own reach boost (<see cref="ToolParity.TileBoost"/>).
    /// </summary>
    private bool IsInReach(GridPoint point) =>
        World.Contains(point) && ToolParity.InBox(ToolReachBox(), point.X, point.Y);

    /// <summary>
    /// Radius handed to the placement system: it circumscribes the reach box, so the box check in
    /// <see cref="IsInReach"/> decides and the placement system only adds its own rules.
    /// </summary>
    private float PlacementReachRadius
    {
        get
        {
            var box = ToolReachBox();
            var center = PlayerCenter;
            var x = MathF.Max(center.X - box.MinX, box.MaxX + 1 - center.X) + 1.5f;
            var y = MathF.Max(center.Y - box.MinY, box.MaxY + 1 - center.Y) + 1.5f;
            return MathF.Sqrt(x * x + y * y);
        }
    }

    private PlacementActorContext CreatePlacementContext() =>
        new(Player.Motion.Position + Player.Motion.Size / 2f, PlacementReachRadius,
            RequireLineOfSight: true, ActorSize: Player.Motion.Size);

    private CraftingEnvironment CreateCraftingEnvironment()
    {
        var center = Player.Motion.Position + Player.Motion.Size / 2f;
        var radius = 6f + CraftingStationReachBonus; // fabricator manual
        var nearbyLiquids = new HashSet<LiquidKind>();
        var minX = Math.Max(0, (int)MathF.Floor(center.X - radius));
        var maxX = Math.Min(World.Width - 1, (int)MathF.Ceiling(center.X + radius));
        var minY = Math.Max(0, (int)MathF.Floor(center.Y - radius));
        var maxY = Math.Min(World.Height - 1, (int)MathF.Ceiling(center.Y + radius));
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var cell = World.GetCell(new GridPoint(x, y));
            if (cell.LiquidDepth > 0f && cell.LiquidKind != LiquidKind.None)
                nearbyLiquids.Add(cell.LiquidKind);
        }

        var biomeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            center.Y < World.Height * 0.45f ? "surface" : "underground",
            ClockHour >= 18f || ClockHour < 6f ? "night" : "day"
        };
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var tile = World.GetCell(new GridPoint(x, y)).TileId;
            if (tile == TileId.Snow) biomeTags.Add("snow");
            if (tile == TileId.DesertSand || tile == TileId.Sand) biomeTags.Add("desert");
            if (tile == TileId.JungleGrass) biomeTags.Add("jungle");
        }
        return new CraftingEnvironment(center, World, Tiles, radius, biomeTags, nearbyLiquids);
    }

    private GridPoint GetFacingTile()
    {
        var center = Player.Motion.Position + Player.Motion.Size / 2f;
        var x = (int)MathF.Floor(center.X + Player.Motion.Facing * 1.3f);
        var y = (int)MathF.Floor(center.Y);
        return new GridPoint(Math.Clamp(x, 0, World.Width - 1), Math.Clamp(y, 0, World.Height - 1));
    }

    /// <summary>World difficulty from the world profile; scales creature damage and toughness.</summary>
    public WorldDifficulty Difficulty { get; set; } = WorldDifficulty.Classic;

    /// <summary>Column the colony landed on; respawns return here.</summary>
    public int SpawnColumn { get; private set; }

    private float ScaleIncoming(float damage) => damage * WorldSettings.EnemyScale(Difficulty);

    private float ScaleOutgoing(float damage) => damage / WorldSettings.EnemyScale(Difficulty);

    /// <summary>
    /// Brings a dead player back at the landing site. What is left where they fell depends on the difficulty: Easy drops
    /// part of the money, Medium the money and every carried item, Hard the money and everything but tools; Explorer and
    /// Creative keep everything. Ironman never respawns (the host ends the run instead). Returns the player-facing message.
    /// </summary>
    public string RespawnPlayer(CharacterDifficulty difficulty)
    {
        var creditsDropped = DropDeathCredits(difficulty);

        string message;
        if (BedSpawnPosition() is { } beside)
        {
            Player.Respawn(beside);
            message = $"{Player.Colonist.Name} wakes in bed.";
            Record($"{Player.Colonist.Name} fell, and woke in bed.");
        }
        else
        {
            var lostBed = _bedSpawn is not null;
            _bedSpawn = null;
            var column = Math.Clamp(SpawnColumn == 0 ? World.Width / 2 : SpawnColumn, 2, World.Width - 3);
            Player.Respawn(new Vector2(column, FindSurface(column) - Player.Motion.Size.Y));
            message = lostBed
                ? $"Your bed is missing or obstructed. {Player.Colonist.Name} wakes at the landing site."
                : $"{Player.Colonist.Name} wakes at the landing site.";
            Record($"{Player.Colonist.Name} fell, and woke at the landing site.");
        }
        if (creditsDropped > 0)
            message += $" {creditsDropped} credits lie where you fell.";
        PostMessage(message);
        return message;
    }

    private int FindSurface(int x)
    {
        for (var y = 1; y < World.Height; y++)
        {
            // Open above means not solid: a plant, mushroom or sapling on the ground does not hide the surface.
            if (Tiles[World.GetCell(new GridPoint(x, y)).TileId].IsSolid &&
                !Tiles[World.GetCell(new GridPoint(x, y - 1)).TileId].IsSolid)
                return y;
        }
        return World.Height / 2;
    }

    private bool TryFindGround(int x, out int groundY)
    {
        for (var y = 1; y < World.Height - 1; y++)
        {
            var tile = World.GetCell(new GridPoint(x, y)).TileId;
            var above = World.GetCell(new GridPoint(x, y - 1)).TileId;
            if (tile != TileId.Air && above == TileId.Air && Tiles[tile].IsSolid)
            {
                groundY = y;
                return true;
            }
        }
        groundY = 0;
        return false;
    }

    private bool IsSolidActorPosition(Vector2 position, Vector2 size)
    {
        var left = (int)MathF.Floor(position.X);
        var right = (int)MathF.Floor(position.X + size.X - 0.0001f);
        var top = (int)MathF.Floor(position.Y);
        var bottom = (int)MathF.Floor(position.Y + size.Y - 0.0001f);
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            var point = new GridPoint(x, y);
            if (!World.Contains(point) || Tiles[World.GetCell(point).TileId].IsSolid)
                return true;
        }
        return false;
    }

    private static float DistanceToRect(Vector2 point, Vector2 origin, Vector2 size)
    {
        var closest = Vector2.Clamp(point, origin, origin + size);
        return Vector2.Distance(point, closest);
    }

    private GameTickResult CurrentResult() => new(TickNumber, ClockHour, Day, LastMessage, _autonomousActivity);

    private void PostMessage(string message)
    {
        LastMessage = message;
        _recentMessages.Enqueue(message);
        while (_recentMessages.Count > 5)
            _recentMessages.Dequeue();
    }
}
