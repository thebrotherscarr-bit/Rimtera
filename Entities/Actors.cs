using System.Numerics;
using Rimtera.AI;
using Rimtera.Characters;
using Rimtera.Systems;

namespace Rimtera.Entities;

/// <summary>
/// Shared original human body used by both the player and colonist NPCs: a torso core holding inside organs,
/// with neck/head (skull and brain inside, eyes and jaw outside), shoulders/arms/hands and legs/feet outside.
/// Coverage is the share of the parent's hit area a part's subtree occupies.
/// </summary>
public static class ColonistAnatomy
{
    /// <summary>Fraction of hit-point damage that also wounds the body tree.</summary>
    public const float BodyDamageScale = 0.5f;

    public static BodyPart CreateTorso()
    {
        var torso = new BodyPart("torso", 40f, BodyPartFunction.None, coverage: 1f);
        torso.AddChild(new BodyPart("heart", 15f, BodyPartFunction.BloodPumpingSource, 0.03f, BodyPartDepth.Inside, vital: true));
        torso.AddChild(new BodyPart("left lung", 15f, BodyPartFunction.BreathingSource, 0.05f, BodyPartDepth.Inside));
        torso.AddChild(new BodyPart("right lung", 15f, BodyPartFunction.BreathingSource, 0.05f, BodyPartDepth.Inside));
        torso.AddChild(new BodyPart("stomach", 20f, BodyPartFunction.DigestionSource, 0.05f, BodyPartDepth.Inside));
        torso.AddChild(new BodyPart("liver", 20f, BodyPartFunction.BloodFiltrationSource, 0.05f, BodyPartDepth.Inside));
        torso.AddChild(new BodyPart("spine", 25f, BodyPartFunction.SpinalSupport, 0.04f, BodyPartDepth.Inside));

        var neck = new BodyPart("neck", 25f, BodyPartFunction.None, 0.08f);
        var head = new BodyPart("head", 25f, BodyPartFunction.None, 0.80f);
        var skull = new BodyPart("skull", 25f, BodyPartFunction.None, 0.25f, BodyPartDepth.Inside);
        skull.AddChild(new BodyPart("brain", 10f, BodyPartFunction.ConsciousnessSource, 0.70f, BodyPartDepth.Inside, vital: true));
        head.AddChild(skull);
        head.AddChild(new BodyPart("left eye", 10f, BodyPartFunction.SightSource, 0.07f));
        head.AddChild(new BodyPart("right eye", 10f, BodyPartFunction.SightSource, 0.07f));
        head.AddChild(new BodyPart("jaw", 15f, BodyPartFunction.TalkingSource, 0.15f));
        neck.AddChild(head);
        torso.AddChild(neck);

        foreach (var side in new[] { "left", "right" })
        {
            var shoulder = new BodyPart($"{side} shoulder", 25f, BodyPartFunction.ManipulationLimbSegment, 0.12f);
            var arm = new BodyPart($"{side} arm", 25f, BodyPartFunction.ManipulationLimbCore, 0.80f);
            arm.AddChild(new BodyPart($"{side} hand", 15f, BodyPartFunction.ManipulationLimbDigit, 0.20f));
            shoulder.AddChild(arm);
            torso.AddChild(shoulder);
        }
        foreach (var side in new[] { "left", "right" })
        {
            var leg = new BodyPart($"{side} leg", 30f, BodyPartFunction.MovingLimbCore, 0.14f);
            leg.AddChild(new BodyPart($"{side} foot", 20f, BodyPartFunction.MovingLimbDigit, 0.15f));
            torso.AddChild(leg);
        }
        return torso;
    }

    /// <summary>Contact damage kind: big creatures crush, small ones bite.</summary>
    public static DamageKind ContactDamageKind(EnemyArchetype archetype) =>
        archetype.IsBoss || archetype.Size.X * archetype.Size.Y >= 1.9f ? DamageKind.Crush : DamageKind.Bite;
}

public sealed class PlayerCharacter : IColonyPawn
{
    private readonly BodyPart _torso;

    public PlayerCharacter(PawnMotionState motion, Inventory inventory, CharacterProfile colonist, int healthSeed = 0x5EED)
    {
        Motion = motion ?? throw new ArgumentNullException(nameof(motion));
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        Colonist = colonist ?? throw new ArgumentNullException(nameof(colonist));
        ColonistStats = colonist.Stats;
        Needs = new SurvivalNeeds(0.82f, 0.9f, 0.72f, 0.70f);
        _torso = ColonistAnatomy.CreateTorso();
        Health = new HealthComponent(_torso, seed: healthSeed);
        Mind = new ColonistMind(colonist, ColonistMoodCalculator.Calculate(Needs, ColonistStats));
        HitPoints = MaximumHitPoints;
    }

    public PawnMotionState Motion { get; }
    public Inventory Inventory { get; }
    public CharacterProfile Colonist { get; }
    public ColonistStats ColonistStats { get; }
    public SurvivalNeeds Needs { get; }
    public HealthComponent Health { get; }
    /// <summary>Thoughts, mood, break state and relations.</summary>
    public ColonistMind Mind { get; }
    public EquipmentLoadout Equipment { get; } = new();
    public StatusEffectController Statuses { get; } = new();
    public AgentBrain? Brain { get; set; }
    public float HitPoints { get; private set; }
    public PlayerStats Stats => PlayerStatsCalculator.Calculate(Equipment, Inventory.Catalog, Statuses, ColonistStats);
    public const int LifeCoreBonus = 10;
    public const int MaximumLifeCores = 10;
    /// <summary>Life cores consumed; each adds <see cref="LifeCoreBonus"/> maximum hit points (up to +100). Saved.</summary>
    public int LifeCoresUsed { get; internal set; }
    public float MaximumHitPoints => Stats.MaximumLife + LifeCoresUsed * LifeCoreBonus;
    /// <summary>Current mood: eases toward needs baseline + traits + Σ thoughts on the story cadence.</summary>
    public float Mood => Mind.Mood.Current;
    public string MoodLabel => ColonistMoodCalculator.Label(Mood);
    public IReadOnlyList<ThoughtView> Thoughts => Mind.CurrentThoughts;
    public float MeleeDamageMultiplier => ColonistStats.MeleeDamageMultiplier;
    public float ShootingDamageMultiplier => ColonistStats.ShootingDamageMultiplier;
    public float MiningSpeedMultiplier => ColonistStats.MiningSpeed;
    public int ActionCooldownTicks { get; internal set; }
    public bool IsDead => HitPoints <= 0f || Health.IsDead;
    /// <summary>Down from pain shock, unconsciousness or ruined legs: cannot move or act until it passes.</summary>
    public bool IsDowned => !IsDead && Health.IsDowned;
    /// <summary>The player never loses control, but below the minor break threshold they are "on the edge".</summary>
    public bool OnTheEdge => Mind.Breaker.OnTheEdge;
    /// <summary>At extreme break risk the player works noticeably slower.</summary>
    public bool AtBreakingPoint => Mind.Breaker.Risk == MentalBreakSeverity.Extreme;
    /// <summary>Multiplier on maximum run speed from the moving capacity (0 when downed).</summary>
    public float MoveCapacityFactor => IsDowned ? 0f : Math.Clamp(Health.Moving, 0f, 1f);
    /// <summary>Multiplier on action cooldowns from manipulation and the extreme-mood work penalty.</summary>
    public float ActionCooldownScale =>
        1f / Math.Max(0.3f, Health.Manipulation) * (AtBreakingPoint ? 1.35f : 1f);
    public DamageResult LastDamage { get; private set; } = DamageResult.None;

    int IColonyPawn.SocialKey => 0;
    public string Name => Colonist.Name;
    CharacterProfile IColonyPawn.Profile => Colonist;
    bool IColonyPawn.IsPlayerCharacter => true;
    bool IColonyPawn.IsAlive => !IsDead;

    public float TakeDamage(float amount) => TakeDamage(amount, DamageKind.Blunt);

    /// <summary>Seconds of post-hit invulnerability left (contact and projectile hits only).</summary>
    public float ImmunitySeconds { get; internal set; }
    public bool IsImmune => ImmunitySeconds > 0f;
    /// <summary>Invulnerability granted after a creature or projectile hit (about 40 ticks).</summary>
    public const float HitImmunitySeconds = 0.67f;

    /// <summary>Charge crystals consumed; each adds <see cref="Rimtera.Systems.PlayerItems.ChargeCrystalBonus"/> maximum Charge. Saved.</summary>
    public int ChargeCrystalsUsed { get; internal set; }

    /// <summary>
    /// A creature or projectile hit: ignored while immune, otherwise applies damage and starts the
    /// immunity window. Damage over time (statuses, liquids, exposure) uses <see cref="TakeDamage(float, DamageKind)"/>.
    /// </summary>
    public float TakeHit(float amount, DamageKind kind, float immunitySeconds = HitImmunitySeconds)
    {
        if (IsImmune || IsDead || !float.IsFinite(amount) || amount <= 0f)
            return 0f;
        var applied = TakeDamage(amount, kind);
        if (applied > 0f)
            ImmunitySeconds = Math.Max(ImmunitySeconds, immunitySeconds);
        return applied;
    }

    /// <summary>Removes hit points and wounds a coverage-chosen body part with a damage kind.</summary>
    public float TakeDamage(float amount, DamageKind kind)
    {
        if (!float.IsFinite(amount) || amount <= 0f || IsDead)
            return 0f;

        var applied = Math.Min(HitPoints, amount);
        HitPoints -= applied;
        LastDamage = Health.ApplyDamage(applied * ColonistAnatomy.BodyDamageScale, kind);
        SyncHealthDeath();
        return applied;
    }

    /// <summary>Zeroes the hit-point pool once the body itself has died (organ loss, blood loss, infection).</summary>
    public void SyncHealthDeath()
    {
        if (Health.IsDead)
            HitPoints = 0f;
    }

    /// <summary>Restores a dead player at a position: full hit points, body restored, motion reset.</summary>
    public void Respawn(Vector2 position)
    {
        Health.Reset();
        HitPoints = MaximumHitPoints;
        Motion.Position = position;
        Motion.Velocity = Vector2.Zero;
    }

    /// <summary>Environmental harm: frostbite on extremities, heat burns, or radiation sickness.</summary>
    public float TakeExposureDamage(float amount, string cause) =>
        TakeDamage(amount, ConditionCatalog.FromExposureCause(cause));

    public void RestorePersistentState(
        float hitPoints,
        float foodReserve,
        float restReserve,
        float joyReserve = 0.70f,
        float comfortReserve = 0.70f)
    {
        if (!float.IsFinite(hitPoints))
            throw new ArgumentOutOfRangeException(nameof(hitPoints));
        HitPoints = Math.Clamp(hitPoints, 0f, MaximumHitPoints);
        Needs.SetReserves(foodReserve, restReserve, joyReserve, comfortReserve);
    }

    public void Heal(float amount)
    {
        if (!float.IsFinite(amount) || amount < 0f)
            throw new ArgumentOutOfRangeException(nameof(amount));
        HitPoints = Math.Min(MaximumHitPoints, HitPoints + amount);
        Health.HealInjuries(amount * ColonistAnatomy.BodyDamageScale);
    }
}

public enum EnemyKind : ushort
{
    MossSlime,
    CaveMoth,
    CaveBat,
    RockGolem,
    Wraith,
    StoneSentinel,
    RockMite,
    SkyRay,
    SporeRat,
    AstroBeast,
    AlphaSkitter,
    DamagedMiningDrone,
    PlantOvergrowth,
    ScoutMech,
    CyberSwarmLeader,
    RenegadeOverseer,
    MutatedFlora,
    Abomination,
    WarshipCore,
    SolarFlareEntity,
    VoidStalker,
    AsteroidMinerMech,
    CyberPirateCaptain,
    AstroGod,
    SnowWolf,
    Cryopterous,
    SnowMite,
    GlacialGolem,
    RadSkitter,
    MutantRat,
    GeigerBug,
    GlowStalker,
    RadLeech,
    PyroSkitter,
    MagmaTurtle,
    LavaLeech,
    EmberRat,
    BasaltGolem,
    JunkSkitter,
    ScavengerCrab,
    SalvageDrone,
    ScrapMite,
    WastelandBeast,
    NeoTerraDeer,
    SkyHawk,

    // ---- Content expansion (append-only; archetypes in Entities/ContentCreatures.cs) ----
    // Critters (passive; most can be caught with the bug net).
    GlowGrub, MegaFauna, SandSkitterer, ScavengerBeetle, SkyFlier, TerraRat,
    SnowHare, ScrapRat, EmberNewt, GlowFirefly, PebbleBeetle, GoldenCritter,
    ShellCrab, GlowJelly, BeachSkitter, SeaBird, GlowSnail, SporeMoth, CaveFrog, TruffleMite,
    PrismButterfly, LightWispBird, CrystalBunny, HaloSnail, CanopyParrot, LeafFrog, PollenBee, VineSnake,
    HopBunny, GrassSnake, PenguinDrone, DuneLizard, Worm, ButterflyBlue, ButterflyGold, Dragonfly,
    SquirrelBot, Owl, Duck, FishSilver, FishGlow, CrabSmall, Snail, LadybugBot, Frog,
    // Biome creatures.
    AcidSkitterer, NeoTerraReaver, TerraformerDrone,
    AcidShark, TideLeech, ReefCrawler, BrineGolem, SpittingBarnacle, StormRay, DrownedDiver, CoralSentinel,
    SporeZombie, FungalCrawler, PuffDrifter, MycelidShaman, ShroomStalker, CapTurtle, LanternBat, SporeGolem,
    PrismKnight, ShardUnicorn, LightMimic, CrystalWraith, RefractorTurret, HaloBat, PearlGolem, SpireDragonfly,
    PollenDroneSwarm, ThornLurker, CanopyApe, SnapjawPlant, HiveGuard, MudCrawler, SpittingOrchid, JungleStalker,
    VaultSentinel, GlyphDrone, AncientGuardian, GeodeCrab, AlabasterHoplite, MiteBroodmother, SilkSpiderMite,
    // Event creatures.
    EventGrunt, EventScout, EventMarksman, EventBrute, EventCommander, EventDrone, EventSwarmer,
    EventEnergyVortex, EventRogueTerraformer, EventVoidSiphon, EventCorruptedSurvivor, EventAbomination,
    EventHusk, EventRustWraith, EventRustHound, EventEyeDrone, EventCorrodedMech, EventSwarmQueen,
    EventDustDevil, EventSandRay, EventScrapElemental, EventDustRaider, EventAcidSlime, EventAcidBird,
    EventToxicCloud, EventAuroraWisp, EventFestivalBalloonDrone, EventMeteorHead, EventMeteorCrawler,
    EventEclipseShade, EventEclipseReaper, EventEclipseMothMech,
    EventScavengerGrunt, EventScavengerBrute, EventScavengerSniper, EventScavengerRigger,
    EventPirateDeckhand, EventPirateGunner, EventPirateParrotDrone, EventPirateCannon,
    EventProtocolGrunt, EventProtocolShieldbearer, EventProtocolWyvern, EventProtocolOgre, EventProtocolMage,
    EventPrecursorProbe, EventProbeGunner, EventProbeTeslaWalker, EventProbeBrainDrone,
    // Biome and event bosses.
    SporeMonarch, PrismEmpress, BloomMatriarch, VaultColossus,
    MoonEater, EclipseLord, ScavengerWarlord, PirateDreadnought, ProtocolDragon, MothershipCore,
    // Fishing catches and trigger spawns.
    BloodfinAmbusher, AbyssLeviathan, VengefulWisp,
    // ---- Batch B AI families (append-only; archetypes in Entities/ContentCreatures.cs) ----
    TunnelWyrm, DuneBorer, RiftCaster, DiveKite, CacheMimic, GelHopper, BoulderHopper, FleaHopper,
    // ---- Batch A (append-only; archetype in Systems/GameSimulation.Physics.cs) ----
    DungeonGuardian,
    // ---- Neo-Terra theme pass (append-only; archetypes in Entities/ContentCreatures.cs): Irradiated Core natives ----
    CoreHusk, FissionWisp,
    // ---- Hardmode progression (append-only; archetypes in Entities/ContentCreatures.cs) ----
    /// <summary>The Irradiated Core's gate boss: defeating it causes the Fracture.</summary>
    FissionTyrant,
    /// <summary>The ritual boss at the dungeon entrance after the Vault Colossus.</summary>
    VoidCantor,
    /// <summary>The four shielded obelisks of the endgame siege.</summary>
    EmberObelisk, RiftObelisk, GaleObelisk, ShardObelisk,
    // ---- Content fill (append-only; explicit band 1000-1299; data in Systems/Content/ContentFillData.cs) ----
    // Critters and gold variants.
    RegolithVole = 1000, SunbeamMoth = 1001, ThistleFinch = 1002, MeadowCricket = 1003, PuffballTortoise = 1004, DuskGlider = 1005,
    LampWorm = 1006, MoonhopToad = 1007, StarMoth = 1008, PondMinnow = 1009, BubbleTadpole = 1010, BurrowMole = 1011,
    RootGrub = 1012, GravelSnail = 1013, GemTick = 1014, EchoFlitter = 1015, BlindCavefish = 1016, CaveSalamander = 1017,
    GlowSlug = 1018, FrostVole = 1019, IcicleBeetle = 1020, SnowOwlet = 1021, RimeFish = 1022, RustScarab = 1023,
    WireMouse = 1024, SprocketFly = 1025, TideFlea = 1026, HermitBot = 1027, FuelSeahorse = 1028, ReefStar = 1029,
    CanopyGecko = 1030, LeafbackBeetle = 1031, OrchidMoth = 1032, MireSkipper = 1033, PuffcapHopper = 1034, MyceliumWorm = 1035,
    SporeFirefly = 1036, ShardMoth = 1037, LumenFish = 1038, MutantRoach = 1039, GlowToad = 1040, Rootworm = 1041,
    IsotopeBeetle = 1042, FissionMoth = 1043, GildedVole = 1044, GildedCricket = 1045, GildedFinch = 1046, GildedMinnow = 1047,
    GildedGecko = 1048, GildedScarab = 1049, GildedHopBunny = 1050, GildedSquirrelBot = 1051, GildedFrog = 1052,
    // Biome hostiles and their hardmode versions.
    ThistleBoar = 1100, MeadowGel = 1101, HornetDrone = 1102, BoreBeetle = 1103, DuskProwler = 1104,
    UmbralGel = 1105, GloomKite = 1106, GraveHusk = 1107, DriftingEye = 1108, ThornBehemoth = 1109,
    StormHarrier = 1110, NightRevenant = 1111, ShockGel = 1112, SkyLancer = 1113, TunnelRat = 1114,
    BurrowSpider = 1115, ClayGolem = 1116, GrubWorm = 1117, MudGel = 1118, DrillMite = 1119,
    BedrockCrusher = 1120, DelveWyrm = 1121, ShadeCaster = 1122, GemCrawler = 1123, CaveLurker = 1124,
    SlateGel = 1125, EchoCaster = 1126, CrystalBorer = 1127, HollowBat = 1128, UrnMimic = 1129,
    GeodeTitan = 1130, VoidMantis = 1131, RunicCaster = 1132, ArmoredDelver = 1133, StrongboxMimic = 1134,
    RimeGel = 1135, FrostFlitter = 1136, TundraBrute = 1137, DriftLeaper = 1138, CryoCaster = 1139,
    PermafrostBorer = 1140, GlacierTitan = 1141, BlizzardWraith = 1142, FrozenMimic = 1143, RimeDiver = 1144,
    RustGel = 1145, ScrapHound = 1146, BoltDrone = 1147, DunePhantom = 1148, JunkTinker = 1149,
    SlagBorer = 1150, JunkPileMimic = 1151, ScrapColossus = 1152, RustReaper = 1153, DuneDevourer = 1154,
    FuelGel = 1155, PincerCrab = 1156, GullDiver = 1157, FuelEel = 1158, BarbRay = 1159,
    InkSquid = 1160, DeepMaw = 1161, ReefHorror = 1162, TideCaller = 1163, VineGel = 1164,
    HornBeetle = 1165, CanopyBat = 1166, SpearFrog = 1167, RootLasher = 1168, DartLizard = 1169,
    ThornChanter = 1170, MossHornet = 1171, CanopyWyrm = 1172, StranglerBloom = 1173, SilverbackBrute = 1174,
    CombGel = 1175, BroodLarva = 1176, HiveWarden = 1177, SporeGel = 1178, ToadstoolLeaper = 1179,
    MyceliumBorer = 1180, GlowcapMite = 1181, CapSentinel = 1182, MycelidHulk = 1183, SporePhantom = 1184,
    PuffBomber = 1185, PrismGel = 1186, ShardLeaper = 1187, LumenCaster = 1188, PearlWyrm = 1189,
    GlassKite = 1190, PrismColossus = 1191, HaloSeraph = 1192, ChromaSerpent = 1193, IsotopeGel = 1194,
    FalloutHound = 1195, RadMoth = 1196, FalloutSeer = 1197, SludgeWorm = 1198, RadBehemoth = 1199,
    IsotopeWraith = 1200, LeadBoxMimic = 1201, RootGel = 1202, BlightCrawler = 1203, ThornBat = 1204,
    RootWyrm = 1205, BlightHulk = 1206, RotChanter = 1207, VineReaper = 1208, SlagGel = 1209,
    FissionHound = 1210, CoreBorer = 1211, IsotopeImp = 1212, ReactorBat = 1213, MeltdownGolem = 1214,
    CriticalWisp = 1215, WebLurker = 1216, BroodTyrant = 1217, GlyphCaster = 1218, VaultLancer = 1219,
    CryptHoplite = 1220, VoidScribe = 1221, CryptFlitter = 1222,
    // Progression chain (docs/PROGRESSION_DIFF.md G1): the pre-Fracture avenger of radiated blight hearts.
    FalloutHivemind = 1300
}

/// <summary>
/// Movement/attack family a creature uses (append-only). Default keeps the legacy ground/flying/boss logic.
/// </summary>
public enum EnemyAiFamily : byte
{
    Default,
    /// <summary>Segmented body that follows its head and burrows through solid ground.</summary>
    Worm,
    /// <summary>Teleports near the target, then fires slow homing bolts.</summary>
    Caster,
    /// <summary>Hovers above the target and periodically dives at it.</summary>
    DivingFlyer,
    /// <summary>Sits disguised as a container until approached or struck, then leaps after the target.</summary>
    Mimic,
    /// <summary>Regular hops toward the target.</summary>
    Hopper,
    /// <summary>Slow, long, high leaps.</summary>
    LeapingHopper,
    /// <summary>Rapid, low skitter-hops.</summary>
    SkitteringHopper
}

/// <summary>Boss grouping used for summon order and event eligibility.</summary>
public enum BossTier : byte
{
    None,
    Gate,
    Easy,
    Hard,
    Event,
    Final
}

/// <summary>
/// Data row for one creature. Bosses add ranged-volley tuning read by the shared boss
/// controller; at or below <see cref="PhaseThreshold"/> of its HP a boss enters phase 2.
/// </summary>
public sealed record EnemyArchetype(
    EnemyKind Kind,
    string DisplayName,
    Vector2 Size,
    float HitPoints,
    float AttackDamage,
    bool Flying,
    BossTier Tier = BossTier.None,
    float ProjectileSpeed = 0f,
    int VolleyCount = 0,
    float PhaseThreshold = 0.5f)
{
    public bool IsBoss => Tier != BossTier.None;
    public float ContactCooldown { get; init; } = 1.5f;
    public float MoveSpeedMultiplier { get; init; } = 1f;
    public float AttackRange { get; init; } = 9f;
    public float AttackCooldown { get; init; } = 2.1f;
    public float TelegraphSeconds { get; init; } = 0.55f;
    /// <summary>Wildlife: wanders, flees when hurt, and never attacks.</summary>
    public bool Passive { get; init; }
    /// <summary>Small creatures that close distance in hops rather than walking.</summary>
    public bool Hops { get; init; }
    /// <summary>Passive critter that the bug net turns into a same-named item.</summary>
    public bool Catchable { get; init; }
    /// <summary>Credits dropped on death (in addition to item drops).</summary>
    public int CreditDrop { get; init; }
    /// <summary>Stationary creatures (barnacles, turrets, snapjaw plants) never walk.</summary>
    public bool Stationary { get; init; }
    /// <summary>Behaviour family (worms, casters, divers, mimics, hoppers); Default = legacy logic.</summary>
    public EnemyAiFamily Family { get; init; }
    /// <summary>Body segments trailing a worm's head.</summary>
    public int SegmentCount { get; init; }
    /// <summary>Spawns only below the surface (worms, casters).</summary>
    public bool Underground { get; init; }
    /// <summary>
    /// Armor: each player hit loses half this many points (never below 1), less what armor penetration gives back.
    /// 0 for every creature until the roster is tuned (CombatTuning.CreatureDamageTaken).
    /// </summary>
    public int Defense { get; init; }
}

/// <summary>Original Neo-Terra creature roster. Pre-existing kinds keep their legacy tuning.</summary>
public static class EnemyArchetypes
{
    private static readonly Dictionary<EnemyKind, EnemyArchetype> _table = new[]
    {
        // Regular creatures.
        new EnemyArchetype(EnemyKind.MossSlime, "Swarm bug", new(0.9f, 0.65f), 24f, 8f, false) { ContactCooldown = 1.2f },
        new EnemyArchetype(EnemyKind.CaveMoth, "Moth-ray", new(0.8f, 0.7f), 18f, 6f, true),
        new EnemyArchetype(EnemyKind.CaveBat, "Spore bat", new(0.8f, 0.6f), 16f, 5f, true),
        new EnemyArchetype(EnemyKind.RockGolem, "Security droid", new(1.2f, 1.6f), 70f, 14f, false),
        new EnemyArchetype(EnemyKind.Wraith, "Void wisp", new(0.9f, 1.4f), 42f, 11f, true),
        new EnemyArchetype(EnemyKind.RockMite, "Rock mite", new(0.7f, 0.5f), 30f, 9f, false) { ContactCooldown = 1.1f },
        new EnemyArchetype(EnemyKind.SkyRay, "Sky-ray", new(1.1f, 0.6f), 26f, 8f, true),
        new EnemyArchetype(EnemyKind.SporeRat, "Spore rat", new(0.8f, 0.55f), 22f, 7f, false) { ContactCooldown = 1.2f },
        new EnemyArchetype(EnemyKind.AstroBeast, "Astro-beast", new(1.6f, 1.3f), 110f, 18f, false) { ContactCooldown = 1.8f },

        // Gate boss: the original two-phase sentinel, unchanged.
        new EnemyArchetype(EnemyKind.StoneSentinel, "Overseer mech", new(1.3f, 1.8f), 520f, 20f, false,
            BossTier.Gate, ProjectileSpeed: 8f, VolleyCount: 1, PhaseThreshold: 0.5f),

        // Easy tier.
        new EnemyArchetype(EnemyKind.AlphaSkitter, "Alpha Skitter", new(2.6f, 1.8f), 650f, 18f, false,
            BossTier.Easy, ProjectileSpeed: 9f, VolleyCount: 3, PhaseThreshold: 0.5f) { MoveSpeedMultiplier = 1.2f },
        new EnemyArchetype(EnemyKind.DamagedMiningDrone, "Damaged Mining Drone", new(2.2f, 2f), 600f, 16f, true,
            BossTier.Easy, ProjectileSpeed: 9f, VolleyCount: 2, PhaseThreshold: 0.45f),
        new EnemyArchetype(EnemyKind.PlantOvergrowth, "Plant Overgrowth", new(2.8f, 3f), 820f, 20f, false,
            BossTier.Easy, ProjectileSpeed: 7f, VolleyCount: 5, PhaseThreshold: 0.4f) { MoveSpeedMultiplier = 0.6f, AttackRange = 12f },
        new EnemyArchetype(EnemyKind.ScoutMech, "Scout Mech", new(2.2f, 2.8f), 900f, 22f, false,
            BossTier.Easy, ProjectileSpeed: 12f, VolleyCount: 2, PhaseThreshold: 0.5f) { AttackRange = 12f },
        // Radiated hearts' avenger before the Fracture: a buzzing cloud of fused fallout drones (not in the summon order).
        new EnemyArchetype(EnemyKind.FalloutHivemind, "Fallout Hive-mind", new(2.4f, 2f), 800f, 22f, true,
            BossTier.Easy, ProjectileSpeed: 9f, VolleyCount: 4, PhaseThreshold: 0.5f)
            { MoveSpeedMultiplier = 1.15f, ContactCooldown = 1.2f, Family = EnemyAiFamily.DivingFlyer },

        // Hard tier.
        new EnemyArchetype(EnemyKind.CyberSwarmLeader, "Cyber-Swarm Leader", new(2.6f, 2.2f), 1400f, 26f, true,
            BossTier.Hard, ProjectileSpeed: 11f, VolleyCount: 5, PhaseThreshold: 0.5f) { MoveSpeedMultiplier = 1.25f },
        new EnemyArchetype(EnemyKind.RenegadeOverseer, "Renegade Overseer", new(2.6f, 3.4f), 1700f, 30f, false,
            BossTier.Hard, ProjectileSpeed: 13f, VolleyCount: 3, PhaseThreshold: 0.5f) { AttackRange = 12f },
        new EnemyArchetype(EnemyKind.MutatedFlora, "Mutated Flora", new(3.2f, 3.2f), 1900f, 30f, false,
            BossTier.Hard, ProjectileSpeed: 9f, VolleyCount: 7, PhaseThreshold: 0.4f) { MoveSpeedMultiplier = 0.55f, AttackRange = 13f },
        new EnemyArchetype(EnemyKind.Abomination, "Abomination", new(3.6f, 3f), 2300f, 34f, false,
            BossTier.Hard, ProjectileSpeed: 10f, VolleyCount: 4, PhaseThreshold: 0.6f) { MoveSpeedMultiplier = 1.1f },
        new EnemyArchetype(EnemyKind.WarshipCore, "Warship Core", new(4.2f, 2.2f), 2600f, 34f, true,
            BossTier.Hard, ProjectileSpeed: 14f, VolleyCount: 5, PhaseThreshold: 0.5f) { AttackRange = 15f, AttackCooldown = 2.4f },

        // Event bosses arrive at dusk once an easy-tier boss has fallen.
        new EnemyArchetype(EnemyKind.SolarFlareEntity, "Solar Flare Entity", new(2.8f, 2.8f), 1300f, 26f, true,
            BossTier.Event, ProjectileSpeed: 12f, VolleyCount: 6, PhaseThreshold: 0.5f),
        new EnemyArchetype(EnemyKind.VoidStalker, "Void Stalker", new(2.2f, 3.2f), 1200f, 30f, true,
            BossTier.Event, ProjectileSpeed: 13f, VolleyCount: 2, PhaseThreshold: 0.5f) { MoveSpeedMultiplier = 1.3f },
        new EnemyArchetype(EnemyKind.AsteroidMinerMech, "Asteroid Miner Mech", new(3f, 3f), 1500f, 28f, false,
            BossTier.Event, ProjectileSpeed: 10f, VolleyCount: 3, PhaseThreshold: 0.5f),
        new EnemyArchetype(EnemyKind.CyberPirateCaptain, "Cyber-Pirate Captain", new(2f, 2.8f), 1250f, 26f, false,
            BossTier.Event, ProjectileSpeed: 14f, VolleyCount: 3, PhaseThreshold: 0.5f) { MoveSpeedMultiplier = 1.2f },

        // Frozen Wasteland.
        new EnemyArchetype(EnemyKind.SnowWolf, "Snow-wolf", new(1.3f, 0.9f), 46f, 11f, false) { MoveSpeedMultiplier = 1.35f, ContactCooldown = 1.3f },
        new EnemyArchetype(EnemyKind.Cryopterous, "Cryopterous", new(0.9f, 0.6f), 28f, 7f, false) { Hops = true, ContactCooldown = 1.2f },
        new EnemyArchetype(EnemyKind.SnowMite, "Snow-mite", new(0.7f, 0.5f), 26f, 8f, false) { Hops = true, ContactCooldown = 1.1f },
        new EnemyArchetype(EnemyKind.GlacialGolem, "Glacial golem", new(1.5f, 1.9f), 160f, 19f, false) { MoveSpeedMultiplier = 0.7f, ContactCooldown = 1.9f },

        // Radiated Deeps.
        new EnemyArchetype(EnemyKind.RadSkitter, "Rad-skitter", new(1f, 0.6f), 34f, 9f, false) { MoveSpeedMultiplier = 1.25f },
        new EnemyArchetype(EnemyKind.MutantRat, "Mutant rat", new(0.9f, 0.6f), 30f, 8f, false) { ContactCooldown = 1.2f },
        new EnemyArchetype(EnemyKind.GeigerBug, "Geiger bug", new(0.7f, 0.6f), 20f, 6f, true),
        new EnemyArchetype(EnemyKind.GlowStalker, "Glow-stalker", new(1f, 1.6f), 58f, 13f, false) { MoveSpeedMultiplier = 1.15f },
        new EnemyArchetype(EnemyKind.RadLeech, "Rad-leech", new(1.4f, 0.8f), 140f, 17f, false) { MoveSpeedMultiplier = 0.85f, ContactCooldown = 1.6f },

        // Molten Chasm.
        new EnemyArchetype(EnemyKind.PyroSkitter, "Pyro-skitter", new(1f, 0.6f), 38f, 10f, false) { MoveSpeedMultiplier = 1.3f },
        new EnemyArchetype(EnemyKind.MagmaTurtle, "Magma turtle", new(1.3f, 0.9f), 90f, 12f, false) { MoveSpeedMultiplier = 0.55f, ContactCooldown = 1.7f },
        new EnemyArchetype(EnemyKind.LavaLeech, "Lava-leech", new(1.1f, 0.6f), 44f, 11f, false),
        new EnemyArchetype(EnemyKind.EmberRat, "Ember rat", new(0.8f, 0.55f), 30f, 9f, false) { ContactCooldown = 1.2f },
        new EnemyArchetype(EnemyKind.BasaltGolem, "Basalt golem", new(1.6f, 2f), 190f, 22f, false) { MoveSpeedMultiplier = 0.65f, ContactCooldown = 2f },

        // Junk Wasteland.
        new EnemyArchetype(EnemyKind.JunkSkitter, "Junk-skitter", new(1f, 0.6f), 32f, 8f, false) { MoveSpeedMultiplier = 1.25f },
        new EnemyArchetype(EnemyKind.ScavengerCrab, "Scavenger crab", new(1.1f, 0.7f), 48f, 10f, false) { MoveSpeedMultiplier = 0.9f },
        new EnemyArchetype(EnemyKind.SalvageDrone, "Salvage drone", new(0.9f, 0.7f), 30f, 8f, true),
        new EnemyArchetype(EnemyKind.ScrapMite, "Scrap-mite", new(0.7f, 0.5f), 24f, 8f, false) { Hops = true, ContactCooldown = 1.1f },
        new EnemyArchetype(EnemyKind.WastelandBeast, "Wasteland beast", new(1.7f, 1.4f), 150f, 20f, false) { ContactCooldown = 1.8f },

        // Grassland wildlife.
        new EnemyArchetype(EnemyKind.NeoTerraDeer, "Neo-Terra deer", new(1.2f, 1.2f), 30f, 0f, false) { Passive = true, MoveSpeedMultiplier = 1.2f },
        new EnemyArchetype(EnemyKind.SkyHawk, "Sky-hawk", new(0.9f, 0.6f), 18f, 0f, true) { Passive = true },

        // End-world boss.
        new EnemyArchetype(EnemyKind.AstroGod, "Astro-God", new(5f, 5f), 6000f, 45f, true,
            BossTier.Final, ProjectileSpeed: 15f, VolleyCount: 8, PhaseThreshold: 0.5f) { AttackRange = 18f, AttackCooldown = 2.6f }
    }.Concat(ContentCreatures.Archetypes()).Append(Rimtera.Systems.PhysicsContent.DungeonGuardian).ToDictionary(archetype => archetype.Kind);

    /// <summary>Summon order for the challenge key: gate boss, easy tier, the core gate boss, hard tier, then the Astro-God.</summary>
    public static IReadOnlyList<EnemyKind> BossProgression { get; } = new[]
    {
        EnemyKind.StoneSentinel,
        EnemyKind.AlphaSkitter, EnemyKind.DamagedMiningDrone, EnemyKind.PlantOvergrowth, EnemyKind.ScoutMech,
        EnemyKind.FissionTyrant,
        EnemyKind.CyberSwarmLeader, EnemyKind.RenegadeOverseer, EnemyKind.MutatedFlora, EnemyKind.Abomination,
        EnemyKind.WarshipCore,
        EnemyKind.AstroGod
    };

    public static IReadOnlyList<EnemyKind> EventBosses { get; } = new[]
    {
        EnemyKind.SolarFlareEntity, EnemyKind.VoidStalker, EnemyKind.AsteroidMinerMech, EnemyKind.CyberPirateCaptain
    };

    public static IEnumerable<EnemyArchetype> All => _table.Values.OrderBy(archetype => archetype.Kind);

    public static bool Contains(EnemyKind kind) => _table.ContainsKey(kind);

    public static EnemyArchetype Get(EnemyKind kind) => _table.TryGetValue(kind, out var archetype)
        ? archetype
        : throw new ArgumentOutOfRangeException(nameof(kind), $"No archetype is registered for {kind}.");
}

public enum EnemyState : byte
{
    Roaming,
    Pursuing,
    Telegraphing,
    Recovering,
    Defeated
}

public sealed class EnemyAgent
{
    public EnemyAgent(long id, EnemyKind kind, Vector2 position)
    {
        Id = id;
        Kind = kind;
        Archetype = EnemyArchetypes.Get(kind);
        Motion = new PawnMotionState(position, Archetype.Size);
        MaximumHitPoints = Archetype.HitPoints;
        HitPoints = MaximumHitPoints;
        AttackDamage = Archetype.AttackDamage;
    }

    public long Id { get; }
    public EnemyKind Kind { get; }
    public EnemyArchetype Archetype { get; }
    public string DisplayName => Archetype.DisplayName;
    public PawnMotionState Motion { get; }
    public float MaximumHitPoints { get; private set; }
    public float HitPoints { get; private set; }
    public float AttackDamage { get; private set; }
    /// <summary>True once hardmode stat scaling has been applied (or ruled out) for this creature.</summary>
    internal bool StatsScaled { get; set; }
    /// <summary>Shielded creatures (obelisks before their quota) ignore every hit.</summary>
    public bool Shielded { get; internal set; }

    /// <summary>Multiplies maximum hit points (keeping the current fraction) and attack damage.</summary>
    internal void ScaleStats(float hitPointScale, float damageScale)
    {
        var fraction = MaximumHitPoints > 0f ? HitPoints / MaximumHitPoints : 1f;
        MaximumHitPoints *= hitPointScale;
        HitPoints = MaximumHitPoints * fraction;
        AttackDamage *= damageScale;
    }

    /// <summary>Caps attack damage (boss damage ceiling).</summary>
    internal void CapDamage(float cap) => AttackDamage = Math.Min(AttackDamage, cap);
    public EnemyState State { get; internal set; } = EnemyState.Roaming;
    public float StateTime { get; internal set; }
    public float AttackCooldown { get; internal set; }
    public int Phase { get; internal set; }
    /// <summary>Seconds a passive creature keeps fleeing after being hurt.</summary>
    public float FleeSeconds { get; internal set; }
    /// <summary>The world event that spawned this creature (kill quotas, wave progress), or null.</summary>
    public WorldEventKind? EventTag { get; internal set; }
    public bool IsPassive => Archetype.Passive;
    public bool IsBoss => Archetype.IsBoss;
    public bool IsAlive => HitPoints > 0f;

    // ---- Batch B: per-attacker immunity and AI-family state ----
    private Dictionary<long, float>? _hitImmunity;
    private List<Vector2>? _segments;

    /// <summary>The creature's behaviour family.</summary>
    public EnemyAiFamily Family => Archetype.Family;
    /// <summary>World-space centers of a worm's body segments, head first excluded (empty for other kinds).</summary>
    public IReadOnlyList<Vector2> Segments => _segments ?? (IReadOnlyList<Vector2>)Array.Empty<Vector2>();
    internal List<Vector2> SegmentList => _segments ??= new List<Vector2>();
    /// <summary>Mimics: true while still posing as a container (draw it as one; it deals no contact damage).</summary>
    public bool Disguised { get; internal set; }
    /// <summary>Divers: true while plunging at the target. Casters: true just after a teleport.</summary>
    public bool Lunging { get; internal set; }
    internal float AiTimer { get; set; }
    internal int AiStep { get; set; }

    /// <summary>True when <paramref name="attacker"/> may hit this creature again (its per-attacker cooldown ran out).</summary>
    public bool CanBeHitBy(long attacker) => _hitImmunity is null || !_hitImmunity.TryGetValue(attacker, out var left) || left <= 0f;

    /// <summary>Starts a per-attacker hit cooldown.</summary>
    public void MarkHitBy(long attacker, float seconds)
    {
        if (seconds <= 0f)
            return;
        (_hitImmunity ??= new Dictionary<long, float>())[attacker] = seconds;
    }

    internal void TickHitImmunity(float deltaSeconds)
    {
        if (_hitImmunity is null || _hitImmunity.Count == 0)
            return;
        foreach (var key in _hitImmunity.Keys.ToArray())
        {
            var left = _hitImmunity[key] - deltaSeconds;
            if (left <= 0f)
                _hitImmunity.Remove(key);
            else
                _hitImmunity[key] = left;
        }
    }

    public float TakeDamage(float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0f || !IsAlive || Shielded)
            return 0f;
        var applied = Math.Min(HitPoints, amount);
        HitPoints -= applied;
        if (Archetype.Passive)
            FleeSeconds = 4f;
        if (HitPoints <= 0f)
            State = EnemyState.Defeated;
        return applied;
    }

    // ---- Player & combat systems: afflictions carried by the creature (GameSimulation.PlayerSystems.cs) ----
    private Dictionary<StatusId, float>? _afflictions;
    private static readonly Dictionary<StatusId, float> NoAfflictions = new();

    /// <summary>Active afflictions and the seconds each has left.</summary>
    public IReadOnlyDictionary<StatusId, float> Afflictions => _afflictions ?? NoAfflictions;
    public bool HasAffliction(StatusId id) => _afflictions?.ContainsKey(id) == true;
    /// <summary>Seconds until the next damage-over-time pulse.</summary>
    internal float AfflictionPulse { get; set; }

    /// <summary>Starts or refreshes an affliction (the longer of the two durations wins).</summary>
    internal void Afflict(StatusId id, float seconds)
    {
        if (seconds <= 0f || !IsAlive)
            return;
        _afflictions ??= new Dictionary<StatusId, float>();
        _afflictions[id] = Math.Max(seconds, _afflictions.GetValueOrDefault(id));
    }

    internal void TickAfflictions(float deltaSeconds)
    {
        if (_afflictions is null || _afflictions.Count == 0)
            return;
        foreach (var key in _afflictions.Keys.ToArray())
        {
            var left = _afflictions[key] - deltaSeconds;
            if (left <= 0f)
                _afflictions.Remove(key);
            else
                _afflictions[key] = left;
        }
    }

    /// <summary>Copies the live creature state into a save snapshot.</summary>
    internal EnemySnapshot CaptureState() => new(
        Id,
        Kind,
        Motion.Position.X,
        Motion.Position.Y,
        Motion.Velocity.X,
        Motion.Velocity.Y,
        Motion.Facing,
        Motion.Grounded,
        MaximumHitPoints,
        HitPoints,
        AttackDamage,
        StatsScaled,
        Shielded,
        State,
        StateTime,
        AttackCooldown,
        Phase,
        FleeSeconds,
        EventTag,
        Disguised,
        Lunging,
        AiTimer,
        AiStep,
        _segments is { Count: > 0 } ? _segments.SelectMany(point => new[] { point.X, point.Y }).ToArray() : null,
        _afflictions is { Count: > 0 }
            ? _afflictions.Select(pair => new AfflictionSnapshot(pair.Key, pair.Value)).ToArray()
            : null,
        _hitImmunity is { Count: > 0 }
            ? _hitImmunity.Select(pair => new HitImmunitySnapshot(pair.Key, pair.Value)).ToArray()
            : null);

    /// <summary>Applies a snapshot captured by <see cref="CaptureState"/> to this creature.</summary>
    internal void RestoreState(EnemySnapshot state)
    {
        Motion.Position = new Vector2(state.X, state.Y);
        Motion.Velocity = new Vector2(state.VelocityX, state.VelocityY);
        Motion.Facing = state.Facing < 0 ? -1 : 1;
        Motion.Grounded = state.Grounded;
        MaximumHitPoints = state.MaximumHitPoints;
        HitPoints = state.HitPoints;
        AttackDamage = state.AttackDamage;
        StatsScaled = state.StatsScaled;
        Shielded = state.Shielded;
        State = state.State;
        StateTime = state.StateTime;
        AttackCooldown = state.AttackCooldown;
        Phase = state.Phase;
        FleeSeconds = state.FleeSeconds;
        EventTag = state.EventTag;
        Disguised = state.Disguised;
        Lunging = state.Lunging;
        AiTimer = state.AiTimer;
        AiStep = state.AiStep;

        if (state.Segments is { Length: >= 2 } segments)
        {
            _segments = new List<Vector2>(segments.Length / 2);
            for (var index = 0; index + 1 < segments.Length; index += 2)
                _segments.Add(new Vector2(segments[index], segments[index + 1]));
        }
        else
        {
            _segments = null;
        }

        _afflictions = state.Afflictions is { Length: > 0 }
            ? state.Afflictions.Where(entry => entry.Seconds > 0f)
                .GroupBy(entry => entry.Status)
                .ToDictionary(group => group.Key, group => group.Max(entry => entry.Seconds))
            : null;
        _hitImmunity = state.HitImmunity is { Length: > 0 }
            ? state.HitImmunity.Where(entry => entry.Seconds > 0f)
                .GroupBy(entry => entry.Attacker)
                .ToDictionary(group => group.Key, group => group.Max(entry => entry.Seconds))
            : null;
    }
}

public enum ProjectileTeam : byte
{
    Player,
    Enemy
}

public sealed class ColonistAgent : IColonyPawn
{
    private readonly BodyPart _torso;

    public ColonistAgent(long id, CharacterProfile profile, Vector2 position, int healthSeed = 0)
    {
        Id = id;
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        ColonistStats = profile.Stats;
        Motion = new PawnMotionState(position, new Vector2(0.68f, 1.8f));
        Needs = new SurvivalNeeds(0.80f, 0.86f, 0.70f, 0.66f);
        _torso = ColonistAnatomy.CreateTorso();
        Health = new HealthComponent(_torso, seed: healthSeed != 0 ? healthSeed : unchecked((int)id * 7919 + 17));
        Mind = new ColonistMind(profile, ColonistMoodCalculator.Calculate(Needs, ColonistStats));
        HitPoints = MaximumHitPoints;
        HomePosition = position;
        WanderTarget = position;
    }

    public long Id { get; }
    public CharacterProfile Profile { get; }
    public ColonistStats ColonistStats { get; }
    public PawnMotionState Motion { get; }
    public SurvivalNeeds Needs { get; }
    public HealthComponent Health { get; }
    /// <summary>Thoughts, mood, break state and relations.</summary>
    public ColonistMind Mind { get; }
    public float HitPoints { get; private set; }
    public float MaximumHitPoints => ColonistStats.MaximumHealth(Profile.MaximumHealth);
    /// <summary>Current mood: eases toward needs baseline + traits + Σ thoughts on the story cadence.</summary>
    public float Mood => Mind.Mood.Current;
    public string MoodLabel => ColonistMoodCalculator.Label(Mood);
    public IReadOnlyList<ThoughtView> Thoughts => Mind.CurrentThoughts;
    public Vector2 HomePosition { get; set; }
    public Vector2 WanderTarget { get; set; }
    public float WanderTimer { get; set; }
    public string Activity { get; internal set; } = "Making camp";
    public bool IsAlive => HitPoints > 0f && !Health.IsDead;
    public bool IsDowned => IsAlive && Health.IsDowned;
    /// <summary>Active mental break, or None.</summary>
    public MentalBreakKind MentalBreak => Mind.Breaker.ActiveKind;
    /// <summary>Colony-local social key (1.. in roster order); assigned by the simulation.</summary>
    public int SocialKey { get; internal set; } = 1;
    public string Name => Profile.Name;
    bool IColonyPawn.IsPlayerCharacter => false;
    /// <summary>Multiplier on maximum run speed from the moving capacity (0 when downed).</summary>
    public float MoveCapacityFactor => IsDowned ? 0f : Math.Clamp(Health.Moving, 0f, 1f);
    public DamageResult LastDamage { get; private set; } = DamageResult.None;
    /// <summary>Hours until the next break action (binge bite, tantrum blow).</summary>
    internal float BreakTimer { get; set; }

    public float TakeDamage(float amount) => TakeDamage(amount, DamageKind.Blunt);

    /// <summary>Removes hit points and wounds a coverage-chosen body part with a damage kind.</summary>
    public float TakeDamage(float amount, DamageKind kind)
    {
        if (!float.IsFinite(amount) || amount <= 0f || !IsAlive)
            return 0f;
        var applied = Math.Min(HitPoints, amount);
        HitPoints -= applied;
        LastDamage = Health.ApplyDamage(applied * ColonistAnatomy.BodyDamageScale, kind);
        SyncHealthDeath();
        return applied;
    }

    /// <summary>Zeroes the hit-point pool once the body itself has died (organ loss, blood loss, infection).</summary>
    public void SyncHealthDeath()
    {
        if (Health.IsDead)
            HitPoints = 0f;
    }

    /// <summary>Environmental harm: frostbite on extremities, heat burns, or radiation sickness.</summary>
    public float TakeExposureDamage(float amount, string cause) =>
        TakeDamage(amount, ConditionCatalog.FromExposureCause(cause));

    public void RestorePersistentState(
        Vector2 position,
        float hitPoints,
        float foodReserve,
        float restReserve,
        float joyReserve,
        float comfortReserve)
    {
        Motion.Position = position;
        HitPoints = Math.Clamp(hitPoints, 0f, MaximumHitPoints);
        Needs.SetReserves(foodReserve, restReserve, joyReserve, comfortReserve);
    }

    /// <summary>Restores hit points (e.g. while resting) without touching body conditions.</summary>
    public void Heal(float amount)
    {
        if (!float.IsFinite(amount) || amount < 0f || !IsAlive)
            return;
        HitPoints = Math.Min(MaximumHitPoints, HitPoints + amount);
    }
}

public sealed record GuideNpc(Vector2 Position, float InteractionRadius = 6f)
{
    public bool IsInRange(Vector2 playerPosition) =>
        Vector2.DistanceSquared(Position, playerPosition) <= InteractionRadius * InteractionRadius;
}

public sealed class ProjectileState
{
    public ProjectileState(long id, Vector2 position, Vector2 velocity, float damage, float knockback, float lifetime, ProjectileTeam team,
        string? sourceName = null, DamageKind kind = DamageKind.Shot, float purifyRadius = 0f)
    {
        PurifyRadius = purifyRadius;
        SourceName = sourceName;
        Kind = kind;
        Id = id;
        Position = position;
        Velocity = velocity;
        Damage = damage;
        Knockback = knockback;
        RemainingSeconds = lifetime;
        Team = team;
    }

    public long Id { get; }
    public Vector2 Position { get; internal set; }
    public Vector2 Velocity { get; internal set; }
    /// <summary>Steering rate (radians per second) toward the target for homing bolts; 0 flies straight.</summary>
    public float Homing { get; internal set; }
    public float Damage { get; }
    public float Knockback { get; }
    public float RemainingSeconds { get; internal set; }
    public ProjectileTeam Team { get; }
    /// <summary>Display name of the shooter, used in hit messages.</summary>
    public string? SourceName { get; }
    /// <summary>How the bolt wounds a body: energy bolts are shots, flare plasma burns.</summary>
    public DamageKind Kind { get; }
    /// <summary>Purifier gel: blight within this radius of the impact turns back to natural ground.</summary>
    public float PurifyRadius { get; }
    /// <summary>Combat class of the shot (thrown melee, ballistics, tech, drones).</summary>
    public DamageClass Class { get; internal set; }
    /// <summary>Enemies the projectile can still pass through.</summary>
    public int PierceRemaining { get; internal set; }
    /// <summary>Critical-hit chance in percent.</summary>
    public int CritChance { get; internal set; }
    /// <summary>The use that launched this shot rolled a crit: doubled after the target's defense on every hit.</summary>
    public bool Critical { get; internal set; }
    /// <summary>Sprite hint for the renderer (null = default bolt).</summary>
    public string? SpriteName { get; internal set; }
    internal HashSet<long>? HitEnemies { get; set; }
    /// <summary>Affliction this shot applies to the creature it hits (status ammo and elemental weapons), or null.</summary>
    public StatusId? InflictStatus { get; internal set; }
    /// <summary>Percent chance the affliction takes hold on each hit.</summary>
    public int InflictChance { get; internal set; } = 100;

    // ---- Weapon styles (GameSimulation.WeaponStyles.cs) ----
    /// <summary>Weapon style that launched this projectile (None for legacy shots, minions and enemies).</summary>
    public WeaponStyle Style { get; internal set; }
    /// <summary>Item that launched it (yo-yo, boomerang, flail, gun...).</summary>
    public ItemId? SourceItem { get; internal set; }
    /// <summary>Yo-yos and flails: draw a string/chain from the player to the projectile.</summary>
    public bool Tethered { get; internal set; }
    /// <summary>Boomerangs, flails and yo-yos flying back to the player.</summary>
    public bool Returning { get; internal set; }
    internal float Gravity { get; set; }
    internal float ExplosionRadius { get; set; }
    internal float MaxRange { get; set; }
    internal float Traveled { get; set; }
    internal Dictionary<long, float>? HitCooldowns { get; set; }

    /// <summary>Copies the live projectile state into a save snapshot.</summary>
    internal ProjectileSnapshot CaptureState() => new(
        Id,
        Position.X,
        Position.Y,
        Velocity.X,
        Velocity.Y,
        Damage,
        Knockback,
        RemainingSeconds,
        Team,
        SourceName,
        Kind,
        PurifyRadius,
        Homing,
        Class,
        PierceRemaining,
        CritChance,
        Critical,
        SpriteName,
        InflictStatus,
        InflictChance,
        Style,
        SourceItem,
        Tethered,
        Returning,
        Gravity,
        ExplosionRadius,
        MaxRange,
        Traveled,
        HitEnemies is { Count: > 0 } ? HitEnemies.ToArray() : null,
        HitCooldowns is { Count: > 0 }
            ? HitCooldowns.Select(pair => new HitCooldownSnapshot(pair.Key, pair.Value)).ToArray()
            : null);

    /// <summary>Applies the mutable part of a snapshot to this projectile.</summary>
    internal void RestoreState(ProjectileSnapshot state)
    {
        Position = new Vector2(state.X, state.Y);
        Velocity = new Vector2(state.VelocityX, state.VelocityY);
        RemainingSeconds = state.RemainingSeconds;
        Homing = state.Homing;
        Class = state.Class;
        PierceRemaining = state.PierceRemaining;
        CritChance = state.CritChance;
        Critical = state.Critical;
        SpriteName = state.SpriteName;
        InflictStatus = state.InflictStatus;
        InflictChance = state.InflictChance;
        Style = state.Style;
        SourceItem = state.SourceItem;
        Tethered = state.Tethered;
        Returning = state.Returning;
        Gravity = state.Gravity;
        ExplosionRadius = state.ExplosionRadius;
        MaxRange = state.MaxRange;
        Traveled = state.Traveled;
        HitEnemies = state.HitEnemies is { Length: > 0 } ? state.HitEnemies.ToHashSet() : null;
        HitCooldowns = state.HitCooldowns is { Length: > 0 }
            ? state.HitCooldowns.Where(entry => entry.Seconds > 0f)
                .GroupBy(entry => entry.EnemyId)
                .ToDictionary(group => group.Key, group => group.Max(entry => entry.Seconds))
            : null;
    }
}
