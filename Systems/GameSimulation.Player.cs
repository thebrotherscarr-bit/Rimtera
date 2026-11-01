using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>One entry of the buff bar: a status (or a timed player state) with its remaining real time.</summary>
public sealed record BuffView(string Key, string Name, string IconName, float RemainingSeconds, bool IsDebuff, StatusId? Status = null);

/// <summary>A light the renderer should add for the player (light pets, the hover-bike lamp).</summary>
public readonly record struct PlayerLightSource(Vector2 Position, float Radius, float Intensity, string Tint);

/// <summary>A cosmetic follower summoned by an item. Light pets also emit light.</summary>
public sealed class PetState
{
    internal PetState(ItemId source, string name, string spriteName, bool isLightPet, float lightRadius, bool flies, Vector2 position)
    {
        SourceItem = source;
        Name = name;
        SpriteName = spriteName;
        IsLightPet = isLightPet;
        LightRadius = lightRadius;
        Flies = flies;
        Position = position;
    }

    public ItemId SourceItem { get; }
    public string Name { get; }
    /// <summary>Sprite hint for the renderer.</summary>
    public string SpriteName { get; }
    public bool IsLightPet { get; }
    public float LightRadius { get; }
    public bool Flies { get; }
    /// <summary>Center of the pet in world tiles.</summary>
    public Vector2 Position { get; internal set; }
    public int Facing { get; internal set; } = 1;
}

/// <summary>The grappling hook projectile: flies out, latches into solid ground, then reels the player in.</summary>
public sealed class GrappleHookState
{
    public Vector2 Position { get; internal set; }
    public Vector2 Velocity { get; internal set; }
    public bool Latched { get; internal set; }
    public Vector2 Anchor { get; internal set; }
    public float RopeLength { get; internal set; }
    internal float Traveled { get; set; }
}

/// <summary>Mounts (append-only). RetiredCart was the ore cart; it has no mount spec and loads as None.</summary>
public enum MountKind : byte { None, HoverBike, RetiredCart, DuneStrider, RotorSkiff, TideRay, Burrower, SpringHopper }

/// <summary>World-local Batch B state saved with the world: ground items, signs, banners, kill counts, bestiary.</summary>
public sealed record PlayerWorldSnapshot(
    GroundItemSnapshot[]? GroundItems = null,
    SignSnapshot[]? Signs = null,
    BannerTileSnapshot[]? Banners = null,
    Dictionary<string, int>? KillCounts = null,
    BestiarySnapshot[]? Bestiary = null,
    float RespawnSecondsRemaining = 0f,
    SignSnapshot[]? ChestNames = null);

public sealed record SignSnapshot(int X, int Y, string Text);
public sealed record BannerTileSnapshot(int X, int Y, EnemyKind Kind);

/// <summary>
/// Batch B player systems: immunity frames, breath and drowning, fall damage, swimming, mobility gear
/// (double jump, rocket pack and jet boots, glider wings, climbing claws, grappling hook), quick keys with
/// potion sickness, charge crystals, tombstones and the boss respawn rule, loadouts, the Cargo Pod bank,
/// trash/favorites/sort/quick stack, signs, pets and light pets, the hover-bike, recall and phase items, and
/// kill banners. Original Rimtera tuning throughout.
/// </summary>
public sealed partial class GameSimulation
{
    // ------------------------------------------------------------------ tuning

    /// <summary>
    /// The player moves like Terraria's (a 16-pixel tile; the body is Terraria's size): a top run of about 11 tiles/s,
    /// reached in about half a second and lost in a quarter; gravity 90 tiles/s² to a 37.5 tiles/s fall; a jump of
    /// 18.8 tiles/s held for a quarter second (about 6.5 tiles high, a tap about 2). Creatures and crew keep their own.
    /// </summary>
    public static readonly PawnMovementSettings PlayerMovementSettings = new()
    {
        DoubleTapDashWindow = 0.25f,
        MaximumRunSpeed = 11.25f,
        GroundAcceleration = 18f,
        AirAcceleration = 18f,
        GroundFriction = 45f,
        AirFriction = 8f,
        Gravity = 90f,
        TerminalFallSpeed = 37.5f,
        JumpImpulse = 18.8f,
        JumpSustainSeconds = 0.25f,
        DashSpeed = 27f,
        WingLiftAcceleration = 18f * GravityScale,
        MaximumWingRiseSpeed = 8f * LiftScale,
        WingTerminalFallSpeed = 3f * LiftScale,
        ClimbSpeed = 8f
    };

    /// <summary>
    /// Old-to-new scales for the player's hand-tuned speeds: gravity is 3.75 times stronger, so a launch speed grows by
    /// its square root to reach the same height in tiles; sideways speeds grow with the run speed.
    /// </summary>
    private const float GravityScale = 90f / 24f, LiftScale = 1.9365f, RunScale = 11.25f / 5f;
    /// <summary>Terraria's air: about 23 seconds under water, refilled within a second of surfacing.</summary>
    public const float BaseBreathSeconds = 23f;
    public const float RebreatherBreathMultiplier = 6f;
    /// <summary>Hit points per second lost while drowning (Terraria's is about 17).</summary>
    public const float DrownDamagePerSecond = 17f;
    public const float SafeFallTiles = 25f;
    /// <summary>The fastest a player without fins rises under water (tiles/s).</summary>
    public const float PaddleSpeed = 2f;
    /// <summary>Terraria's fall damage: 10 per whole tile fallen beyond 25, measured from the highest point.</summary>
    public const float FallDamagePerTile = 10f;
    /// <summary>Upward speed (tiles/s) given when swimming up out of a liquid with jump held.</summary>
    public const float LiquidExitBoost = 11.5f * LiftScale;
    public const float PotionSicknessSeconds = 45f;
    public const float PhaseStrainSeconds = 6f;
    public const int LoadoutCount = 3;
    public const int BankSlots = 40;

    /// <summary>The player's inventory: Terraria's ten columns by five rows, the top row being the hotbar.</summary>
    /// <summary>The bag: the ten-slot hotbar and four more rows (Terraria's 10 × 5).</summary>
    public const int PlayerInventorySlots = 50;
    /// <summary>The coin column and the ammo column beside the bag (Terraria's slots 50–53 and 54–57).</summary>
    public const int PlayerCoinSlots = 4, PlayerAmmoSlots = 4;
    public const int PlayerTotalSlots = PlayerInventorySlots + PlayerCoinSlots + PlayerAmmoSlots;
    public const float BankStationReach = 6f;
    public const float QuickStackRadius = 10f;
    public const int KillsPerBanner = 50;
    public const float BannerRadius = 30f;
    public const float BannerDamageBonus = 1.25f;
    public const float BannerDamageTakenFactor = 0.8f;
    /// <summary>Terraria's respawn timer: 10 seconds, half as long again on Brutal and Annihilation; 3 on Explorer.</summary>
    public const float BaseRespawnSeconds = 10f;
    public const float HarshRespawnFactor = 1.5f;
    public const float ExplorerRespawnSeconds = 3f;
    public const float MaxRespawnSeconds = BaseRespawnSeconds * HarshRespawnFactor;
    public const float GrappleRange = 15f;
    public const float GrappleSpeed = 26f;
    public const float GrappleReelSpeed = 14f;
    public const float HoverBikeSpeed = 1.6f;

    // ------------------------------------------------------------------ state
    private float _breath = -1f;
    private float _drownTimer;
    private float _lavaTimer;
    private bool _uraniumWarned;
    private bool _fuelWarned;
    private bool _drownWarned;
    private bool _doubleJumpUsed;
    private float _rocketFuel;
    private bool _wasGroundedBeforeStep;
    private Vector2? _lastPlayerPosition;
    private bool _clinging;
    private bool _gliding;
    private bool _rocketActive;
    private LiquidKind _bodyLiquid;
    private GrappleHookState? _grapple;
    private readonly EquipmentSnapshot?[] _loadouts = new EquipmentSnapshot?[LoadoutCount];
    private Inventory? _bank;
    private readonly Dictionary<GridPoint, string> _signs = new();
    private readonly Dictionary<GridPoint, string> _chestNames = new();

    /// <summary>Terraria caps a chest name at 20 characters (<c>ChestUI.RenameChest</c>).</summary>
    public const int ChestNameMaxLength = 20;

    private bool IsContainerCell(GridPoint cell) =>
        World.Contains(cell) && ContentTiles.Has(World.GetCell(cell).TileId, TileTraits.Container);

    /// <summary>The name given to the chest at <paramref name="cell"/>, or null when it has none.</summary>
    public string? GetChestName(GridPoint cell) => _chestNames.TryGetValue(cell, out var name) ? name : null;

    /// <summary>Names (or, with empty text, unnames) the chest at <paramref name="cell"/>, as Terraria's chest Rename does.</summary>
    public bool SetChestName(GridPoint cell, string? name)
    {
        if (!IsContainerCell(cell))
            return false;
        name = name?.Trim();
        if (string.IsNullOrEmpty(name))
            _chestNames.Remove(cell);
        else
            _chestNames[cell] = name.Length > ChestNameMaxLength ? name[..ChestNameMaxLength] : name;
        return true;
    }
    private readonly Dictionary<GridPoint, EnemyKind> _bannerTiles = new();
    private readonly Dictionary<EnemyKind, int> _killCounts = new();
    private readonly HashSet<EnemyKind> _activeBanners = new();
    private PetState? _pet;
    private PetState? _lightPet;
    private bool _deathHandled;
    private float _respawnSeconds;
    private int _batchBTick;

    private static readonly EquipmentSnapshot EmptyLoadout = new(null, null, null, null, null, null, null);

    // ------------------------------------------------------------------ public state for the renderer and UI

    /// <summary>Seconds of breath left (refills out of liquid).</summary>
    public float Breath => _breath < 0f ? MaximumBreath : _breath;
    /// <summary>Breath capacity: 12 s, tripled by the rebreather; infinite (shown full) with gills.</summary>
    public float MaximumBreath => HasTag("breath") ? BaseBreathSeconds * RebreatherBreathMultiplier : BaseBreathSeconds;
    /// <summary>True while the player's head is under a liquid (show the breath meter).</summary>
    public bool HeadSubmerged { get; private set; }
    /// <summary>The liquid the player's body is in (None on dry land).</summary>
    public LiquidKind SwimmingIn => _bodyLiquid;
    public bool IsSwimming => _bodyLiquid != LiquidKind.None;

    /// <summary>Fins (the swim tag, on the Diver's kit) or a swimming mount: full strokes under water.</summary>
    public bool CanSwim => HasTag("swim") || MountSwimsIn(_bodyLiquid);
    /// <summary>Rocket fuel left in seconds (rocket pack and jet boots).</summary>
    public float RocketFuel => _rocketFuel;
    public float MaximumRocketFuel => (HasTag("flight") ? 3f : 0f) + (HasTag("rocket-boots") ? 1.6f : 0f) + GearWingFlightSeconds;
    public bool DoubleJumpAvailable => HasTag("double-jump") && !_doubleJumpUsed;
    public bool IsWallClinging => _clinging;
    public bool IsGliding => _gliding;
    public bool IsRocketing => _rocketActive;
    /// <summary>The hook projectile (null when none is out).</summary>
    public GrappleHookState? Grapple => _grapple;
    /// <summary>A twin-hook's first hook, still latched while the second flies (null otherwise).</summary>
    public GrappleHookState? GrappleSpare => _grappleSpare;
    public bool PotionSick => Player.Statuses.IsActive(StatusId.PotionSickness);
    public int ActiveLoadout { get; private set; }
    public MountKind ActiveMount { get; private set; }
    public bool IsMounted => ActiveMount != MountKind.None;
    public PetState? ActivePet => _pet;
    public PetState? ActiveLightPet => _lightPet;
    /// <summary>The trash slot (the last discarded stack; recoverable until the next discard).</summary>
    public ItemStack TrashSlot { get; private set; }
    /// <summary>True after the bank was opened (by item or station) until <see cref="CloseBank"/>.</summary>
    public bool IsBankOpen { get; private set; }
    /// <summary>Seconds until the dead player may respawn (bosses alive make it longer).</summary>
    public float RespawnSecondsRemaining => _respawnSeconds;
    /// <summary>True while a living boss lengthens the respawn timer.</summary>
    public bool BossBlocksRespawn => _enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive);
    /// <summary>Creature kinds whose banner hangs near the player (bonus damage, less damage taken).</summary>
    public IReadOnlyCollection<EnemyKind> ActiveBannerKinds => _activeBanners;
    /// <summary>Placed kill banners by cell.</summary>
    public IReadOnlyDictionary<GridPoint, EnemyKind> BannerTiles => _bannerTiles;
    /// <summary>Kill counts per creature kind (world-local).</summary>
    public IReadOnlyDictionary<EnemyKind, int> KillCounts => _killCounts;
    /// <summary>Sign and tombstone text by cell.</summary>
    public IReadOnlyDictionary<GridPoint, string> Signs => _signs;

    /// <summary>Personal 40-slot storage that travels with the character.</summary>
    public Inventory Bank => _bank ??= new Inventory(Items, BankSlots);

    /// <summary>Equipment sets; the active one mirrors what is worn right now.</summary>
    public IReadOnlyList<EquipmentSnapshot> Loadouts => Enumerable.Range(0, LoadoutCount)
        .Select(index => index == ActiveLoadout ? CaptureEquipmentWithCalibrations() : _loadouts[index] ?? EmptyLoadout).ToArray();

    private bool HasTag(string tag) => Player.Stats.ImmunityTags.Contains(tag);

    /// <summary>Buff bar entries with remaining real seconds and icon names (statuses, potion sickness, breath, mount).</summary>
    public IReadOnlyList<BuffView> ActiveBuffs
    {
        get
        {
            var list = new List<BuffView>();
            var secondsPerHour = 1f / VitalHoursPerRealSecond;
            foreach (var status in Player.Statuses.Active)
                list.Add(new BuffView(status.Id.ToString(), StatusName(status.Id), "buff_" + SnakeCase(status.Id.ToString()),
                    status.RemainingHours * secondsPerHour, StatusEffectSystem.IsDebuff(status.Id), status.Id));
            if (ActiveMountSpec is { } mountSpec)
                list.Add(new BuffView("mount", mountSpec.Name, "buff_" + mountSpec.Sprite[6..], float.PositiveInfinity, false));
            if (_pet is { } pet)
                list.Add(new BuffView("pet", pet.Name, "buff_" + pet.SpriteName, float.PositiveInfinity, false));
            if (_lightPet is { } light)
                list.Add(new BuffView("light-pet", light.Name, "buff_" + light.SpriteName, float.PositiveInfinity, false));
            foreach (var kind in _activeBanners)
                list.Add(new BuffView("banner-" + kind, $"{EnemyArchetypes.Get(kind).DisplayName} banner", "buff_banner",
                    float.PositiveInfinity, false));
            if (HeadSubmerged && Breath <= 0f)
                list.Add(new BuffView("drowning", "Drowning", "buff_drowning", 0f, true));
            return list;
        }
    }

    /// <summary>
    /// Terraria's right-click on a buff: ends a helpful status, sends away a pet or light pet, or dismounts. Debuffs,
    /// banners and drowning cannot be cancelled. Returns true if something ended.
    /// </summary>
    public bool CancelBuff(string key)
    {
        if (ActiveBuffs.FirstOrDefault(buff => buff.Key == key) is not { } buff || buff.IsDebuff)
            return false;
        switch (key)
        {
            case "mount":
                Dismount();
                return true;
            case "pet":
                UnequipMiscTo(MiscSlot.Pet, _pet?.SourceItem);
                _pet = null;
                return true;
            case "light-pet":
                UnequipMiscTo(MiscSlot.LightPet, _lightPet?.SourceItem);
                _lightPet = null;
                return true;
        }
        return buff.Status is { } status && Player.Statuses.Remove(status);
    }

    private static string StatusName(StatusId id) => id switch
    {
        StatusId.ManaFocus => "Charge focus",
        StatusId.FrostGuard => "Frost guard",
        StatusId.ChargeSurge => "Charge surge",
        StatusId.PotionSickness => "Stim fatigue",
        StatusId.PhaseStrain => "Phase strain",
        _ => id.ToString()
    };

    private static string SnakeCase(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    /// <summary>Lights the renderer should add around the player.</summary>
    public IReadOnlyList<PlayerLightSource> PlayerLightSources
    {
        get
        {
            var lights = new List<PlayerLightSource>();
            if (_lightPet is { } pet)
                lights.Add(new PlayerLightSource(pet.Position, pet.LightRadius, 1f,
                    pet.SourceItem == ItemId.LightPetWisp ? "cyan" : "amber"));
            if (ActiveMount == MountKind.HoverBike)
                lights.Add(new PlayerLightSource(PlayerCenter + new Vector2(Player.Motion.Facing * 1.2f, 0.2f), 5f, 0.7f, "white"));
            lights.AddRange(GearLights());
            return lights;
        }
    }

    // ------------------------------------------------------------------ movement hooks

    /// <summary>Adjusts the player's movement input before the shared movement step (mobility gear, swimming, mount, hook).</summary>
    private MovementInput PreparePlayerMovement(MovementInput movement, float deltaSeconds)
    {
        var motion = Player.Motion;
        _wasGroundedBeforeStep = motion.Grounded;
        // Spawns, respawns and teleports move the player without a fall: restart fall measurement.
        if (_lastPlayerPosition is not { } last || Vector2.DistanceSquared(last, motion.Position) > 9f)
            motion.FallPeakY = motion.Position.Y;
        _bodyLiquid = LiquidAtBody();
        var speed = movement.SpeedMultiplier;
        if (IsMounted)
            speed *= MountSpeedFactor(MountSwimsIn(_bodyLiquid));
        if (!MountSwimsIn(_bodyLiquid))
        switch (_bodyLiquid)
        {
            case LiquidKind.Water: speed *= 0.6f; break;
            case LiquidKind.Fuel: speed *= 0.6f; break;
            case LiquidKind.Lava: speed *= 0.5f; break;
            case LiquidKind.Acid: speed *= 0.55f; break;
            case LiquidKind.Nectar: speed *= 0.35f; break;
        }
        movement = movement with { SpeedMultiplier = speed };
        if (Player.IsDowned)
            return movement;

        var velocity = motion.Velocity;
        var jumpUsed = false;
        _rocketActive = false;

        // Grappling hook: a latched hook pins a shrinking rope; jumping lets go.
        if ((_grapple is { Latched: true } ? _grapple : _grappleSpare) is { Latched: true } hook)
        {
            if (movement.JumpPressed)
            {
                _grapple = null;
                _grappleSpare = null;
                velocity.Y = Math.Min(velocity.Y, -7.5f * LiftScale);
                jumpUsed = true;
                _doubleJumpUsed = false;
            }
            else
            {
                movement = movement with { Grapple = new GrappleConstraint(hook.Anchor, hook.RopeLength) };
                var toAnchor = hook.Anchor - PlayerCenter;
                if (toAnchor.LengthSquared() > 0.01f)
                    velocity += Vector2.Normalize(toAnchor) * 30f * GravityScale * deltaSeconds;
                velocity.Y -= PlayerMovementSettings.Gravity * deltaSeconds * 0.6f;
                motion.FallPeakY = motion.Position.Y;
            }
        }

        // Swimming: with fins (the swim tag) jump strokes upward in any liquid; without them, under water a held jump only
        // paddles up slowly, as in Terraria before flippers.
        if (_bodyLiquid != LiquidKind.None && (movement.JumpPressed || movement.JumpHeld) && !jumpUsed)
        {
            var stroke = CanSwim || !HeadSubmerged
                ? (_bodyLiquid == LiquidKind.Nectar ? -2.5f : -4.6f) * LiftScale
                : -(PaddleSpeed + PlayerMovementSettings.Gravity * deltaSeconds); // net of this step's gravity
            velocity.Y = Math.Min(velocity.Y, stroke);
            jumpUsed = movement.JumpPressed;
        }

        GearWaterWalk(ref velocity, movement, ref jumpUsed);
        GearWaterWalk(ref velocity, movement, ref jumpUsed);
        PrepareMountMovement(ref velocity, movement, deltaSeconds);
        var airborne = !motion.Grounded && motion.AirborneSeconds > 0.1f;
        var dir = Math.Sign(movement.Horizontal);
        var wall = airborne && dir != 0 && HasTag("climb") && TouchingWall(dir);
        if (wall && movement.JumpPressed && !jumpUsed)
        {
            // Wall jump off the claws.
            velocity = new Vector2(-dir * 5.5f * RunScale, -8.6f * LiftScale);
            jumpUsed = true;
            _doubleJumpUsed = false;
        }

        if (airborne && movement.JumpPressed && !jumpUsed && _bodyLiquid == LiquidKind.None && HasTag("double-jump") && !_doubleJumpUsed)
        {
            velocity.Y = -8.6f * LiftScale;
            _doubleJumpUsed = true;
            jumpUsed = true;
            Flags.Increment("player.double_jumps");
        }

        // Rocket pack / jet boots: hold jump in the air once the launch has slowed.
        if (airborne && movement.JumpHeld && !jumpUsed && _rocketFuel > 0f && velocity.Y > -4f * LiftScale && _bodyLiquid == LiquidKind.None)
        {
            velocity.Y = Math.Max(velocity.Y - (24f + 34f) * GravityScale * deltaSeconds, -6.5f * LiftScale);
            _rocketFuel = Math.Max(0f, _rocketFuel - deltaSeconds);
            _rocketActive = true;
            motion.FallPeakY = motion.Position.Y;
        }

        // Glider wings: holding jump while falling spends wing energy for lift, then glides.
        _gliding = false;
        if (airborne && movement.JumpHeld && !jumpUsed && !_rocketActive && HasTag("glide") && velocity.Y >= 0f && _bodyLiquid == LiquidKind.None)
        {
            movement = movement with { WingsHeld = motion.WingEnergy > 0f };
            _gliding = true;
        }

        // A press spent on the hook, a wall jump or a double jump must not also queue a landing jump.
        if (jumpUsed && _bodyLiquid == LiquidKind.None)
            movement = movement with { JumpPressed = false };
        motion.Velocity = velocity;
        return movement;
    }

    /// <summary>Applies post-step physics: gliding, wall slides, swimming drag, fall damage, breath, lava.</summary>
    private void AfterPlayerMovement(MovementInput movement, float deltaSeconds)
    {
        var motion = Player.Motion;
        var velocity = motion.Velocity;
        if (motion.Grounded)
        {
            _doubleJumpUsed = false;
            _rocketFuel = MaximumRocketFuel;
        }
        else if (_rocketFuel > MaximumRocketFuel)
            _rocketFuel = MaximumRocketFuel;

        if (_gliding && motion.WingEnergy <= 0f && velocity.Y > 2.2f * LiftScale)
            velocity.Y = 2.2f * LiftScale;
        if (_gliding)
            motion.FallPeakY = Math.Max(motion.FallPeakY, motion.Position.Y - 4f);

        var dir = Math.Sign(movement.Horizontal);
        _clinging = !motion.Grounded && dir != 0 && HasTag("climb") && TouchingWall(dir) && velocity.Y > 0f;
        if (_clinging)
        {
            velocity.Y = Math.Min(velocity.Y, 1.6f * LiftScale);
            motion.FallPeakY = motion.Position.Y;
        }

        var wasInLiquid = _bodyLiquid != LiquidKind.None;
        _bodyLiquid = LiquidAtBody();
        // Breaking the surface while swimming up with jump held kicks you out, enough to clear a 1-tile lip.
        if (wasInLiquid && _bodyLiquid == LiquidKind.None && movement.JumpHeld && velocity.Y < 0f && !motion.Grounded)
            velocity.Y = Math.Min(velocity.Y, -LiquidExitBoost);
        if (_bodyLiquid != LiquidKind.None)
        {
            var sink = _bodyLiquid switch { LiquidKind.Nectar => 0.9f, LiquidKind.Lava => 1.6f, _ => 2.2f };
            velocity.Y = Math.Clamp(velocity.Y, CanSwim || !HeadSubmerged ? -6f * LiftScale : -PaddleSpeed, sink * LiftScale);
            motion.FallPeakY = motion.Position.Y;
        }
        if (ActiveMountSpec is { } mountFall && velocity.Y > mountFall.FallCap)
            velocity.Y = mountFall.FallCap;
        AfterMountMovement(movement, ref velocity);
        motion.Velocity = velocity;

        // Fall damage: 10 per tile beyond 25 (a mount takes its share); featherfall, flight gear and liquids negate it.
        var fall = motion.LastLandingFallDistance;
        var mountFactor = ActiveMountSpec?.FallFactor ?? 1f;
        if (FallDamage(fall) * mountFactor > 0f && !FallNegated())
        {
            var damage = FallDamage(fall) * mountFactor;
            var applied = Player.TakeDamage(damage, DamageKind.Blunt);
            ReportPartLoss(Player, Player.LastDamage);
            if (applied > 0f)
                PostMessage($"{Player.Name} fell {fall:0} tiles and took {applied:0} damage.");
            if (!Player.IsDead && fall >= 40f)
                Achievements.TryUnlock("survive-long-fall");
        }

        _lastPlayerPosition = motion.Position;
        UpdateBreath(deltaSeconds);
        UpdateLiquidContact(deltaSeconds);
    }

    /// <summary>Damage from a fall of <paramref name="tiles"/> (whole tiles beyond 25, ten each): 26 → 10, 30 → 50, 35 → 100.</summary>
    public static float FallDamage(float tiles) => Math.Max(0f, MathF.Floor(tiles + 0.001f) - SafeFallTiles) * FallDamagePerTile;

    private bool FallNegated() =>
        HasTag("featherfall") || HasTag("flight") || HasTag("rocket-boots") ||
        _bodyLiquid != LiquidKind.None || _grapple is { Latched: true };

    private void UpdateBreath(float deltaSeconds)
    {
        var motion = Player.Motion;
        var head = new Vector2(motion.Position.X + motion.Size.X / 2f, motion.Position.Y + 0.3f);
        var cell = ClampCell(RegionMap.CellAt(head));
        var data = World.GetCell(cell);
        HeadSubmerged = data.LiquidKind != LiquidKind.None && data.LiquidDepth >= 0.5f;
        var maximum = MaximumBreath;
        if (_breath < 0f || _breath > maximum)
            _breath = maximum;
        if (HeadSubmerged && !HasTag("gills"))
        {
            _breath = Math.Max(0f, _breath - deltaSeconds);
            if (_breath <= 0f)
            {
                _drownTimer += deltaSeconds;
                if (!_drownWarned)
                    PostMessage($"{Player.Name} is drowning!");
                _drownWarned = true;
                while (_drownTimer >= 0.25f)
                {
                    _drownTimer -= 0.25f;
                    Player.TakeDamage(DrownDamagePerSecond * 0.25f, DamageKind.Toxin);
                }
            }
        }
        else
        {
            _breath = Math.Min(maximum, _breath + deltaSeconds * BaseBreathSeconds); // full air within about a second
            _drownTimer = 0f;
            _drownWarned = false;
        }
    }

    private void UpdateLiquidContact(float deltaSeconds)
    {
        // LiquidKind.Lava is the Irradiated Core's liquid uranium: the "lava" immunity tag shields from it, and its
        // radiation burns ride on the existing burn damage and Burning status.
        if (_bodyLiquid == LiquidKind.Fuel && !_fuelWarned)
        {
            _fuelWarned = true;
            PostMessage("You wade into the fuel sea. The fumes are thick and very flammable.");
        }
        else if (_bodyLiquid == LiquidKind.None)
            _fuelWarned = false;
        if (_bodyLiquid != LiquidKind.Lava)
        {
            _lavaTimer = 0f;
            _uraniumWarned = false;
            return;
        }
        if (HasTag("lava"))
            return;
        if (!_uraniumWarned)
        {
            _uraniumWarned = true;
            PostMessage("Liquid uranium! Radiation burns sear through your suit.");
        }
        _lavaTimer += deltaSeconds;
        while (_lavaTimer >= 0.5f)
        {
            _lavaTimer -= 0.5f;
            Player.TakeDamage(HasTag("heat") ? 9f : 18f, DamageKind.Burn);
            Player.Statuses.Apply(StatusId.Burning, 0.6f, Player.Stats.ImmunityTags);
        }
    }

    private LiquidKind LiquidAtBody()
    {
        var motion = Player.Motion;
        var center = ClampCell(RegionMap.CellAt(motion.Position + motion.Size / 2f));
        var data = World.GetCell(center);
        return data.LiquidKind != LiquidKind.None && data.LiquidDepth >= 0.35f ? data.LiquidKind : LiquidKind.None;
    }

    private bool TouchingWall(int direction)
    {
        var motion = Player.Motion;
        var x = direction > 0 ? motion.Position.X + motion.Size.X + 0.08f : motion.Position.X - 0.08f;
        var top = motion.Position.Y + 0.3f;
        var bottom = motion.Position.Y + motion.Size.Y - 0.3f;
        for (var y = top; y <= bottom; y += 0.5f)
        {
            var cell = new GridPoint((int)MathF.Floor(x), (int)MathF.Floor(y));
            if (World.Contains(cell) && Tiles[World.GetCell(cell).TileId].IsSolid)
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ grappling hook

    /// <summary>Throws the grappling hook toward a world point (needs a grapple accessory). Re-throwing replaces it.</summary>
    public bool TryFireGrapple(Vector2 target)
    {
        if (!HasGrapple || Player.IsDead || Player.IsDowned)
            return false;
        var aim = target - PlayerCenter;
        if (aim.LengthSquared() < 0.01f)
            aim = new Vector2(Player.Motion.Facing, -1f);
        var spec = CurrentHook;
        // A second hook (twin lash) keeps the latched one holding until the new throw catches.
        _grappleSpare = spec.HookCount > 1 && _grapple is { Latched: true } held ? held : null;
        _grapple = new GrappleHookState { Position = PlayerCenter, Velocity = Vector2.Normalize(aim) * spec.LaunchSpeed };
        return true;
    }

    public void ReleaseGrapple()
    {
        _grapple = null;
        _grappleSpare = null;
    }

    private void UpdateGrapple(float deltaSeconds)
    {
        if (_grapple is not { } hook)
        {
            _grappleSpare = null;
            return;
        }
        if (!HasGrapple)
        {
            _grapple = null;
            _grappleSpare = null;
            return;
        }
        var spec = CurrentHook;
        if (!hook.Latched && _grappleSpare is { } spare)
        {
            // The held hook keeps pulling while the second one flies; a miss falls back onto it.
            spare.RopeLength = Math.Max(1.6f, Math.Min(spare.RopeLength, Vector2.Distance(PlayerCenter, spare.Anchor)));
        }
        if (hook.Latched)
        {
            _grappleSpare = null;
            hook.RopeLength = Math.Max(1.6f, Math.Min(hook.RopeLength, Vector2.Distance(PlayerCenter, hook.Anchor)) - spec.ReelSpeed * deltaSeconds);
            if (!World.Contains(ClampCell(RegionMap.CellAt(hook.Anchor + Vector2.Normalize(hook.Velocity) * 0.3f))) ||
                !Tiles[World.GetCell(ClampCell(RegionMap.CellAt(hook.Anchor + Vector2.Normalize(hook.Velocity) * 0.3f))).TileId].IsSolid)
                _grapple = null; // the block it held was removed
            return;
        }
        var travel = hook.Velocity * deltaSeconds;
        var steps = Math.Max(1, (int)MathF.Ceiling(travel.Length() / 0.2f));
        for (var i = 0; i < steps; i++)
        {
            var next = hook.Position + travel / steps;
            var cell = new GridPoint((int)MathF.Floor(next.X), (int)MathF.Floor(next.Y));
            if (!World.Contains(cell))
            {
                _grapple = _grappleSpare;
                _grappleSpare = null;
                return;
            }
            if (Tiles[World.GetCell(cell).TileId].IsSolid)
            {
                hook.Latched = true;
                hook.Anchor = hook.Position;
                hook.RopeLength = Vector2.Distance(PlayerCenter, hook.Anchor);
                Flags.Increment("player.grapples");
                return;
            }
            hook.Position = next;
            hook.Traveled += travel.Length() / steps;
            if (hook.Traveled > spec.Range)
            {
                _grapple = _grappleSpare;
                _grappleSpare = null;
                return;
            }
        }
    }

    // ------------------------------------------------------------------ quick keys and consumables

    /// <summary>Uses the healing item that best covers missing health; blocked by potion sickness.</summary>
    public bool QuickHeal()
    {
        if (Player.IsDead)
            return false;
        if (PotionSick)
        {
            PostMessage("Stim fatigue: you cannot use another healing item yet.");
            return false;
        }
        var missing = Player.MaximumHitPoints - Player.HitPoints;
        var candidates = Player.Inventory.Slots.Where(stack => !stack.IsEmpty && Items[stack.ItemId].HealAmount > 0f)
            .Select(stack => Items[stack.ItemId]).DistinctBy(item => item.Id).ToArray();
        if (candidates.Length == 0 || missing <= 0.5f)
        {
            PostMessage(candidates.Length == 0 ? "No healing items." : "Already at full health.");
            return false;
        }
        var pick = candidates.Where(item => item.HealAmount >= missing).OrderBy(item => item.HealAmount).FirstOrDefault()
                   ?? candidates.OrderByDescending(item => item.HealAmount).First();
        if (!Player.Inventory.TryConsume(pick.Id))
            return false;
        Player.Heal(pick.HealAmount);
        ApplyPotionSickness();
        PostMessage($"Quick heal: {pick.Name} (+{pick.HealAmount:0}).");
        return true;
    }

    /// <summary>Uses a charge item when Charge is not full.</summary>
    public bool QuickCharge()
    {
        if (Player.IsDead || Charge >= MaximumCharge - 0.5f)
            return false;
        var pick = Player.Inventory.Slots.Where(stack => !stack.IsEmpty && Items[stack.ItemId].ChargeRestore > 0f)
            .Select(stack => Items[stack.ItemId]).OrderBy(item => item.ChargeRestore).FirstOrDefault();
        if (pick is null || !Player.Inventory.TryConsume(pick.Id))
        {
            PostMessage("No charge items.");
            return false;
        }
        RestoreChargeFromItem(pick.ChargeRestore);
        PostMessage($"Quick charge: {pick.Name}.");
        return true;
    }

    /// <summary>Uses every carried buff item whose buff is not already running. Returns how many were used.</summary>
    public int QuickBuff()
    {
        if (Player.IsDead)
            return 0;
        var used = 0;
        foreach (var item in Player.Inventory.Slots.Where(stack => !stack.IsEmpty).Select(stack => Items[stack.ItemId])
                     .Where(item => item.GrantsStatus is { } status && !StatusEffectSystem.IsDebuff(status) &&
                                    item.UseKind is ItemUseKind.Consumable && item.HealAmount <= 0f)
                     .DistinctBy(item => item.GrantsStatus).ToArray())
        {
            if (Player.Statuses.IsActive(item.GrantsStatus!.Value) || !Player.Inventory.TryConsume(item.Id))
                continue;
            Player.Statuses.Apply(item.GrantsStatus.Value, item.StatusDurationHours, Player.Stats.ImmunityTags);
            used++;
        }
        PostMessage(used > 0 ? $"Quick buff: {used} item{(used == 1 ? "" : "s")} used." : "No new buffs to apply.");
        return used;
    }

    private void ApplyPotionSickness() =>
        Player.Statuses.Apply(StatusId.PotionSickness, PotionSicknessSeconds * VitalHoursPerRealSecond);

    /// <summary>Batch B consumables (charge crystals, healing items' sickness). True when fully handled.</summary>
    private bool ConsumeBatchBItem(ItemDefinition item)
    {
        if (item.Id == ItemId.ChargeCrystal)
        {
            if (Player.ChargeCrystalsUsed >= PlayerItems.MaximumChargeCrystals)
            {
                PostMessage("Your Charge capacity is already at its limit.");
                return true;
            }
            if (!Player.Inventory.TryConsume(item.Id))
                return true;
            Player.ChargeCrystalsUsed++;
            Charge += PlayerItems.ChargeCrystalBonus;
            SetPlayerCooldown(10);
            PostMessage($"Maximum Charge raised to {MaximumCharge:0}.");
            return true;
        }
        if (item.HealAmount > 0f)
        {
            if (PotionSick)
            {
                PostMessage("Stim fatigue: you cannot use another healing item yet.");
                return true;
            }
            ApplyPotionSickness();
        }
        return false;
    }

    // ------------------------------------------------------------------ item uses (utility hook)

    /// <summary>Batch B utility items: cargo pod, pets, mount, recall, phase rod and banners. True when handled.</summary>
    private bool UseBatchBUtility(ItemDefinition item, GridPoint point)
    {
        if (UseTravelItem(item, point))
            return true;
        if (PlayerItems.BannerKindOf(item.Id) is { } bannerKind)
        {
            PlaceBanner(item, bannerKind, point);
            return true;
        }
        switch (item.Id)
        {
            case ItemId.CargoPod:
                OpenBank();
                break;
            case ItemId.PetDroneChip or ItemId.PetCrabShell or ItemId.LightPetWisp or ItemId.LightPetLantern:
                TogglePet(item.Id);
                break;
            case ItemId.HoverBikeKey:
                ToggleMount(MountKind.HoverBike);
                break;
            case ItemId.RecallCapsule:
                if (!Player.Inventory.TryConsume(item.Id))
                    return true;
                RespawnPlayerAtLanding();
                PostMessage("The recall capsule folds you back to the landing site.");
                Flags.Increment("player.teleports");
                break;
            case ItemId.PhaseRod:
                TryPhaseTo(point);
                break;
            default:
                return false;
        }
        SetPlayerCooldown(Math.Max(6, item.UseCooldownTicks));
        return true;
    }

    /// <summary>Phase rod: teleport to an open spot at the target cell; repeated use while strained hurts.</summary>
    public bool TryPhaseTo(GridPoint cell)
    {
        if (!World.Contains(cell))
            return false;
        var size = Player.Motion.Size;
        var position = new Vector2(cell.X + 0.5f - size.X / 2f, cell.Y + 1f - size.Y);
        if (IsSolidActorPosition(position, size) || position.Y < 0f)
        {
            PostMessage("The phase rod cannot find room there.");
            return false;
        }
        if (Player.Statuses.IsActive(StatusId.PhaseStrain))
            Player.TakeDamage(Player.MaximumHitPoints * 0.1f, DamageKind.Toxin);
        Player.Motion.Position = position;
        Player.Motion.Velocity = Vector2.Zero;
        Player.Motion.FallPeakY = position.Y;
        _grapple = null;
        Player.Statuses.Apply(StatusId.PhaseStrain, PhaseStrainSeconds * VitalHoursPerRealSecond);
        Flags.Increment("player.teleports");
        PostMessage("You phase across the gap.");
        return true;
    }

    // ------------------------------------------------------------------ pets and mount

    /// <summary>The pet an item summons: its name, sprite, whether it is a light pet, its light radius and whether it flies.</summary>
    private static (string Name, string Sprite, bool Light, float Radius, bool Flies)? PetInfo(ItemId item) => item switch
    {
        ItemId.PetDroneChip => ("Pocket drone", "pet_pocket_drone", false, 0f, true),
        ItemId.PetCrabShell => ("Pebble crab", "pet_pebble_crab", false, 0f, false),
        ItemId.LightPetWisp => ("Lumen wisp", "pet_lumen_wisp", true, 7f, true),
        ItemId.LightPetLantern => ("Lantern bot", "pet_lantern_bot", true, 9f, true),
        _ => null
    };

    private PetState? CreatePet(ItemId item)
    {
        if (PetInfo(item) is not { } info)
            return null;
        Achievements.TryUnlock("loyal-companion");
        return new PetState(item, info.Name, info.Sprite, info.Light, info.Radius, info.Flies, PlayerCenter + new Vector2(-Player.Motion.Facing, -1f));
    }

    public bool TogglePet(ItemId item)
    {
        if (PetInfo(item) is not { } info)
            return false;
        ref var slot = ref info.Light ? ref _lightPet : ref _pet;
        if (slot?.SourceItem == item)
        {
            slot = null;
            PostMessage($"{info.Name} dismissed.");
            return true;
        }
        slot = CreatePet(item);
        PostMessage($"{info.Name} summoned.");
        return true;
    }

    /// <summary>The mount key: dismounts, or mounts the first carried mount (the hover-bike first).</summary>
    public bool ToggleMount()
    {
        if (IsMounted)
        {
            Dismount();
            return true;
        }
        return FirstCarriedMount() is { } kind && ToggleMount(kind);
    }

    private void UpdatePets(float deltaSeconds)
    {
        foreach (var pet in new[] { _pet, _lightPet })
        {
            if (pet is null)
                continue;
            var facing = Player.Motion.Facing;
            var target = pet.Flies
                ? PlayerCenter + new Vector2(-facing * 1.4f, pet.IsLightPet ? -2.2f : -1.4f + MathF.Sin(TickNumber * 0.06f) * 0.3f)
                : Player.Motion.Position + new Vector2(Player.Motion.Size.X / 2f - facing * 1.3f, Player.Motion.Size.Y - 0.25f);
            if (Vector2.Distance(pet.Position, target) > 30f)
                pet.Position = target;
            var previous = pet.Position;
            pet.Position = Vector2.Lerp(pet.Position, target, Math.Min(1f, deltaSeconds * 4f));
            if (MathF.Abs(pet.Position.X - previous.X) > 0.002f)
                pet.Facing = pet.Position.X > previous.X ? 1 : -1;
        }
    }

    // ------------------------------------------------------------------ loadouts

    /// <summary>Swaps the worn equipment for loadout <paramref name="index"/> (0–2); the current set is kept in its slot.</summary>
    public bool SwitchLoadout(int index)
    {
        if (index < 0 || index >= LoadoutCount || index == ActiveLoadout)
            return false;
        var target = _loadouts[index] ?? EmptyLoadout;
        _loadouts[ActiveLoadout] = CaptureEquipmentWithCalibrations();
        if (!Player.Equipment.Restore(target, Items))
        {
            Player.Equipment.Restore(_loadouts[ActiveLoadout]!, Items);
            RestoreAccessoryCalibrations(_loadouts[ActiveLoadout]);
            return false;
        }
        RestoreAccessoryCalibrations(target);
        _loadouts[index] = null;
        ActiveLoadout = index;
        PostMessage($"Switched to loadout {index + 1}.");
        return true;
    }

    // ------------------------------------------------------------------ bank, trash, sort and stacking

    /// <summary>True when the Cargo Pod is carried or a Cargo Pod station stands within reach.</summary>
    public bool CanAccessBank => Player.Inventory.Count(ItemId.CargoPod) > 0 || FindNearbyTile(TileId.CargoPodStation, BankStationReach) is not null;

    /// <summary>Opens personal storage when reachable; returns it (null when out of reach).</summary>
    public Inventory? OpenBank()
    {
        if (!CanAccessBank)
        {
            PostMessage("You need a Cargo pod or a nearby Cargo pod station.");
            return null;
        }
        IsBankOpen = true;
        PostMessage("Cargo pod opened.");
        return Bank;
    }

    public void CloseBank() => IsBankOpen = false;

    /// <summary>Moves an inventory stack into the trash slot (favorites refuse). The previous trash is destroyed.</summary>
    public bool TrashInventorySlot(int slot)
    {
        if ((uint)slot >= (uint)Player.Inventory.SlotCount || Player.Inventory.Slots[slot].IsEmpty || Player.Inventory.IsFavorite(slot))
            return false;
        TrashSlot = Player.Inventory.TakeFromSlot(slot) with { Favorite = false };
        return true;
    }

    /// <summary>Puts the trashed stack back into the inventory (as much as fits).</summary>
    public int RecoverTrash()
    {
        if (TrashSlot.IsEmpty)
            return 0;
        var added = Player.Inventory.Add(TrashSlot.ItemId, TrashSlot.Count, TrashSlot.Calibration);
        TrashSlot = TrashSlot.Count - added > 0 ? TrashSlot with { Count = TrashSlot.Count - added } : default;
        return added;
    }

    /// <summary>Puts a stack into the trash slot (the old trash is destroyed), as dropping a held item there does.</summary>
    public void PutInTrash(ItemStack stack) => TrashSlot = stack.IsEmpty ? default : stack with { Favorite = false };

    /// <summary>Takes the whole trashed stack back out (onto the cursor), leaving the trash empty.</summary>
    public ItemStack TakeTrash()
    {
        var taken = TrashSlot;
        TrashSlot = default;
        return taken;
    }

    public bool ToggleFavorite(int slot) => (uint)slot < (uint)Player.Inventory.SlotCount && Player.Inventory.ToggleFavorite(slot);

    /// <summary>Sorts the main inventory (hotbar and favorites stay put).</summary>
    public void SortInventory() => Player.Inventory.Sort(Inventory.HotbarSlots);

    /// <summary>Moves non-hotbar, non-favorite stacks into nearby containers (and an open bank) that already hold that item.</summary>
    public int QuickStackToNearbyChests(float radius = QuickStackRadius)
    {
        var moved = 0;
        foreach (var container in NearbyContainers(radius))
            moved += Player.Inventory.MoveAllTo(container, Inventory.HotbarSlots, onlyMatching: true);
        if (CanAccessBank)
            moved += Player.Inventory.MoveAllTo(Bank, Inventory.HotbarSlots, onlyMatching: true);
        PostMessage(moved > 0 ? $"Quick-stacked {moved} item{(moved == 1 ? "" : "s")}." : "Nothing to quick stack.");
        if (moved > 0)
            Flags.Increment("player.quick_stacks");
        return moved;
    }

    /// <summary>Takes everything from a container (or the bank when <paramref name="cell"/> is null).</summary>
    public int LootAll(GridPoint? cell = null)
    {
        var source = cell is { } point ? GetContainer(point) : CanAccessBank ? Bank : null;
        return source is null ? 0 : source.MoveAllTo(Player.Inventory, 0);
    }

    /// <summary>Quick stack into one container (or the bank): non-hotbar, non-favorite stacks it already holds.</summary>
    public int QuickStackInto(GridPoint? cell = null)
    {
        var target = cell is { } point ? GetContainer(point) : CanAccessBank ? Bank : null;
        return target is null ? 0 : Player.Inventory.MoveAllTo(target, Inventory.HotbarSlots, onlyMatching: true);
    }

    /// <summary>Restock: tops up the player's stacks from a container (or the bank) with items the player already carries.</summary>
    public int Restock(GridPoint? cell = null)
    {
        var source = cell is { } point ? GetContainer(point) : CanAccessBank ? Bank : null;
        return source is null ? 0 : source.MoveAllTo(Player.Inventory, 0, onlyMatching: true);
    }

    /// <summary>Sorts a container (or the bank) from its first slot.</summary>
    public bool SortContainer(GridPoint? cell = null)
    {
        var target = cell is { } point ? GetContainer(point) : CanAccessBank ? Bank : null;
        target?.Sort(0);
        return target is not null;
    }

    /// <summary>Puts every non-hotbar, non-favorite stack into a container (or the bank when <paramref name="cell"/> is null).</summary>
    public int DepositAll(GridPoint? cell = null)
    {
        var target = cell is { } point ? GetContainer(point) : CanAccessBank ? Bank : null;
        return target is null ? 0 : Player.Inventory.MoveAllTo(target, Inventory.HotbarSlots);
    }

    private IEnumerable<Inventory> NearbyContainers(float radius)
    {
        var center = PlayerCell();
        var r = (int)MathF.Ceiling(radius);
        for (var y = Math.Max(0, center.Y - r); y <= Math.Min(World.Height - 1, center.Y + r); y++)
        for (var x = Math.Max(0, center.X - r); x <= Math.Min(World.Width - 1, center.X + r); x++)
        {
            var cell = new GridPoint(x, y);
            var tile = World.GetCell(cell).TileId;
            if (!ContentTiles.Has(tile, TileTraits.Container) || (ContentTiles.Has(tile, TileTraits.Locked) && !_seededContainers.Contains(cell)))
                continue;
            if ((x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y) > radius * radius)
                continue;
            if (GetContainer(cell) is { } inventory)
                yield return inventory;
        }
    }

    private GridPoint? FindNearbyTile(TileId tile, float radius)
    {
        var center = PlayerCell();
        var r = (int)MathF.Ceiling(radius);
        for (var y = Math.Max(0, center.Y - r); y <= Math.Min(World.Height - 1, center.Y + r); y++)
        for (var x = Math.Max(0, center.X - r); x <= Math.Min(World.Width - 1, center.X + r); x++)
            if (World.GetCell(new GridPoint(x, y)).TileId == tile &&
                (x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y) <= radius * radius)
                return new GridPoint(x, y);
        return null;
    }

    // ------------------------------------------------------------------ signs and tombstones

    /// <summary>Writes the text of a sign or tombstone (trimmed to 240 characters). False when the cell holds neither.</summary>
    public bool SetSignText(GridPoint cell, string text)
    {
        if (!World.Contains(cell) || !PlayerTiles.HoldsText(World.GetCell(cell).TileId))
            return false;
        text = (text ?? string.Empty).Replace("\r", string.Empty);
        if (text.Length > 240)
            text = text[..240];
        if (text.Length == 0)
            _signs.Remove(cell);
        else
            _signs[cell] = text;
        return true;
    }

    /// <summary>The text on a sign or tombstone (null when the cell holds neither or it is blank).</summary>
    public string? GetSignText(GridPoint cell) =>
        World.Contains(cell) && PlayerTiles.HoldsText(World.GetCell(cell).TileId) && _signs.TryGetValue(cell, out var text) ? text : null;

    /// <summary>Interaction with Batch B tiles (signs, tombstones, the Cargo Pod station); null when not one of them.</summary>
    private InteractionResult? InteractBatchB(GridPoint target, TileId tile)
    {
        if (PlayerTiles.HoldsText(tile))
            return new InteractionResult(WorldInteractionKind.SignRead,
                GetSignText(target) is { } text ? text : tile == TileId.Tombstone ? "A weathered marker." : "The sign is blank.", Cell: target);
        if (tile == TileId.CargoPodStation)
        {
            IsBankOpen = true;
            return new InteractionResult(WorldInteractionKind.BankOpened, "Cargo pod opened.", Bank, Cell: target);
        }
        return null;
    }

    /// <summary>
    /// Seconds before a dead player may respawn, as in Terraria: 10, or 15 on Brutal and Annihilation worlds; 3 for an
    /// Explorer. A living boss does not lengthen it in single player (it leaves once the player is dead).
    /// </summary>
    public float GetRespawnDelay(CharacterDifficulty difficulty) =>
        difficulty == CharacterDifficulty.Explorer ? ExplorerRespawnSeconds
        : Difficulty >= WorldDifficulty.Brutal ? BaseRespawnSeconds * HarshRespawnFactor
        : BaseRespawnSeconds;

    /// <summary>Called every tick while the player is dead: tombstone on the first tick, then the respawn countdown.</summary>
    private void UpdateWhileDead(float deltaSeconds)
    {
        if (!_deathHandled)
        {
            _deathHandled = true;
            _grapple = null;
            ActiveMount = MountKind.None;
            _respawnSeconds = GetRespawnDelay(PlayerProfile.Difficulty);
            PlaceTombstone();
            Flags.Increment("player.deaths");
            Achievements.TryUnlock("first-fall");
            if (HeadSubmerged && Breath <= 0f)
                Achievements.TryUnlock("sleeps-with-fishes");
        }
        _respawnSeconds = Math.Max(0f, _respawnSeconds - deltaSeconds);
        BossesLeaveOnPlayerDeath(deltaSeconds);
    }

    /// <summary>Leaves a tombstone near the death site, inscribed with the character's name.</summary>
    private GridPoint? PlaceTombstone()
    {
        var start = PlayerCell();
        for (var dy = 0; dy <= 12; dy++)
        for (var dx = 0; dx <= 3; dx++)
        foreach (var sx in dx == 0 ? new[] { 0 } : new[] { dx, -dx })
        {
            var cell = new GridPoint(start.X + sx, start.Y + dy);
            var below = new GridPoint(cell.X, cell.Y + 1);
            if (!World.Contains(cell) || !World.Contains(below))
                continue;
            var data = World.GetCell(cell);
            if (data.TileId != TileId.Air || data.LiquidKind == LiquidKind.Lava || !Tiles[World.GetCell(below).TileId].IsSolid)
                continue;
            World.SetTile(cell, TileId.Tombstone);
            _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, new[] { cell }));
            _signs[cell] = $"Here lies {Player.Name}. Fell on day {Day}.";
            return cell;
        }
        return null;
    }

    // ------------------------------------------------------------------ banners and kill bookkeeping

    private void PlaceBanner(ItemDefinition item, EnemyKind kind, GridPoint point)
    {
        if (!IsInReach(point))
        {
            PostMessage("That placement is out of reach.");
            return;
        }
        var result = _placement.TryPlaceTile(World, Tiles, point,
            new PlacementDefinition(TileId.KillBanner, AnchorRule: PlacementAnchorRule.SolidAdjacent, AllowLiquid: true),
            CreatePlacementContext());
        if (!result.Success)
        {
            PostMessage($"Cannot place: {result.Failure}.");
            return;
        }
        Player.Inventory.TryConsume(item.Id);
        _bannerTiles[point] = kind;
        _framing.ReframeRegion(World, Tiles, result.CellsToReframe);
        SetPlayerCooldown(8);
        RefreshBanners();
        PostMessage($"Hung the {item.Name}.");
        Achievements.TryUnlock("trophy-hunter");
    }

    /// <summary>Damage multiplier the player deals to a kind (banner nearby → bonus).</summary>
    private float BannerDamageFactor(EnemyKind kind) => _activeBanners.Contains(kind) ? BannerDamageBonus : 1f;

    /// <summary>Damage multiplier the player takes from a kind (banner nearby → less).</summary>
    private float BannerDefenseFactor(EnemyKind kind) => _activeBanners.Contains(kind) ? BannerDamageTakenFactor : 1f;

    private void RefreshBanners()
    {
        _activeBanners.Clear();
        foreach (var (cell, kind) in _bannerTiles.ToArray())
        {
            if (!World.Contains(cell) || World.GetCell(cell).TileId != TileId.KillBanner)
            {
                // The banner was knocked down: drop it as an item where it hung.
                _bannerTiles.Remove(cell);
                SpawnGroundItem(PlayerItems.BannerFor(kind), 1, new Vector2(cell.X + 0.5f, cell.Y + 0.5f));
                continue;
            }
            if (Vector2.Distance(new Vector2(cell.X + 0.5f, cell.Y + 0.5f), PlayerCenter) <= BannerRadius)
                _activeBanners.Add(kind);
        }
        foreach (var cell in _signs.Keys.ToArray())
            if (!World.Contains(cell) || !PlayerTiles.HoldsText(World.GetCell(cell).TileId))
                _signs.Remove(cell);
        foreach (var cell in _chestNames.Keys.ToArray())
            if (!IsContainerCell(cell))
                _chestNames.Remove(cell);
    }

    /// <summary>Drops loot from a defeated creature (collected at once when it died within reach) and logs it.</summary>
    private void DropLoot(EnemyAgent enemy, ItemId item, int count)
    {
        // Boss rewards are delivered to the player (what does not fit lands at their feet).
        CollectOrDrop(item, count, enemy.IsBoss ? PlayerCenter : enemy.Motion.Position + enemy.Motion.Size / 2f);
        Bestiary.RecordDrop(enemy.Kind, item);
    }

    /// <summary>Kill counts, bestiary, banners and achievements when a creature dies.</summary>
    private void OnCreatureDefeatedBatchB(EnemyAgent enemy)
    {
        var count = _killCounts.GetValueOrDefault(enemy.Kind) + 1;
        _killCounts[enemy.Kind] = count;
        Bestiary.RecordKill(enemy.Kind, CurrentBiome.Zone);
        OnCreatureDefeatedGrowth(enemy);
        if (PlayerItems.EarnsBanner(enemy.Kind) && count % KillsPerBanner == 0)
        {
            CollectOrDrop(PlayerItems.BannerFor(enemy.Kind), 1, enemy.Motion.Position + enemy.Motion.Size / 2f);
            PostMessage($"{count} {enemy.DisplayName} kills: a trophy banner drops!");
        }
        Achievements.TryUnlock("first-blood");
        if (enemy.IsBoss)
            Achievements.TryUnlock("first-boss");
        if (enemy.Family == EnemyAiFamily.Worm)
            Achievements.TryUnlock("worm-turner");
        if (enemy.Family == EnemyAiFamily.Mimic)
            Achievements.TryUnlock("not-a-chest");
    }

    // ------------------------------------------------------------------ hit helpers used by the shared combat code

    /// <summary>A creature's contact hit on the player: banner reduction and immunity frames apply.</summary>
    private float HitPlayerFromEnemy(EnemyAgent enemy, float amount, DamageKind kind) =>
        GearHitPlayer(enemy, CapBossHit(enemy, amount * BannerDefenseFactor(enemy.Kind) * DefenseBalance.EnemyHitScale(enemy.Archetype)), kind);

    /// <summary>Distance from a point to the nearest part of a creature (worm segments count).</summary>
    private float EnemyHitDistance(EnemyAgent enemy, Vector2 point)
    {
        var best = Vector2.Distance(enemy.Motion.Position + enemy.Motion.Size / 2f, point);
        foreach (var segment in enemy.Segments)
            best = Math.Min(best, Vector2.Distance(segment, point));
        return best;
    }

    /// <summary>Projectile proximity to a creature's box or any of its segments.</summary>
    private float EnemyProjectileDistance(EnemyAgent enemy, Vector2 point)
    {
        var best = DistanceToRect(point, enemy.Motion.Position, enemy.Motion.Size);
        var half = enemy.Motion.Size.X * 0.45f;
        foreach (var segment in enemy.Segments)
            best = Math.Min(best, Math.Max(0f, Vector2.Distance(segment, point) - half));
        return best;
    }

    // ------------------------------------------------------------------ per-tick driver

    /// <summary>Per-tick Batch B update (runs after the content update).</summary>
    private void UpdateBatchB(float deltaSeconds)
    {
        _batchBTick++;
        if (_deathHandled && !Player.IsDead)
        {
            _deathHandled = false;
            _respawnSeconds = 0f;
            _breath = MaximumBreath;
        }
        if (Player.ImmunitySeconds > 0f)
            Player.ImmunitySeconds = Math.Max(0f, Player.ImmunitySeconds - deltaSeconds);
        foreach (var enemy in _enemies)
            enemy.TickHitImmunity(deltaSeconds);
        if (ActiveMountSpec is { } riding && !Carries(riding.Item))
            Dismount();
        if (_pet is { } pet && !Carries(pet.SourceItem))
            _pet = null;
        if (_lightPet is { } lightPet && !Carries(lightPet.SourceItem))
            _lightPet = null;
        if (IsBankOpen && !CanAccessBankCached())
            IsBankOpen = false;

        UpdateGrapple(deltaSeconds);
        UpdateMiscGear();
        UpdatePets(deltaSeconds);
        UpdateGroundItems(deltaSeconds);
        UpdateWorldGenContent(deltaSeconds);
        UpdateHomingProjectiles(deltaSeconds);
        UpdateFamilySpawns(deltaSeconds);
        if (_batchBTick % 30 == 0)
        {
            RefreshBanners();
            ScanBestiarySightings();
            Achievements.Evaluate(this);
        }
    }

    private bool CanAccessBankCached() => _batchBTick % 30 != 0 || CanAccessBank;

    private void ScanBestiarySightings()
    {
        foreach (var enemy in _enemies)
            if (enemy.IsAlive && Vector2.DistanceSquared(enemy.Motion.Position, Player.Motion.Position) <= 24f * 24f)
                Bestiary.RecordSeen(enemy.Kind, CurrentBiome.Zone);
    }

    // ------------------------------------------------------------------ save state

    private PlayerWorldSnapshot CapturePlayerWorld() => new(
        CaptureGroundItems(),
        _signs.Select(pair => new SignSnapshot(pair.Key.X, pair.Key.Y, pair.Value)).ToArray(),
        _bannerTiles.Select(pair => new BannerTileSnapshot(pair.Key.X, pair.Key.Y, pair.Value)).ToArray(),
        _killCounts.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
        Bestiary.Capture(),
        _respawnSeconds,
        _chestNames.Select(pair => new SignSnapshot(pair.Key.X, pair.Key.Y, pair.Value)).ToArray());

    private void RestorePlayerWorld(PlayerWorldSnapshot? state)
    {
        if (state is null)
            return;
        RestoreGroundItems(state.GroundItems);
        foreach (var sign in state.Signs ?? [])
        {
            var cell = new GridPoint(sign.X, sign.Y);
            if (World.Contains(cell) && sign.Text is { Length: > 0 } text && PlayerTiles.HoldsText(World.GetCell(cell).TileId))
                _signs[cell] = text.Length > 240 ? text[..240] : text;
        }
        foreach (var name in state.ChestNames ?? [])
            if (name.Text is { Length: > 0 } text)
                _chestNames[new GridPoint(name.X, name.Y)] = text.Length > ChestNameMaxLength ? text[..ChestNameMaxLength] : text;
        foreach (var banner in state.Banners ?? [])
        {
            var cell = new GridPoint(banner.X, banner.Y);
            if (World.Contains(cell) && Enum.IsDefined(banner.Kind) && World.GetCell(cell).TileId == TileId.KillBanner)
                _bannerTiles[cell] = banner.Kind;
        }
        foreach (var (key, value) in state.KillCounts ?? new Dictionary<string, int>())
            if (Enum.TryParse<EnemyKind>(key, out var kind) && value > 0)
                _killCounts[kind] = value;
        Bestiary.Restore(state.Bestiary);
        if (float.IsFinite(state.RespawnSecondsRemaining))
            _respawnSeconds = Math.Clamp(state.RespawnSecondsRemaining, 0f, MaxRespawnSeconds);
        RefreshBanners();
    }

    /// <summary>Restores Batch B state after a snapshot load: world-local state, then the character (file wins over the snapshot copy).</summary>
    private void RestoreBatchB(GameSnapshot snapshot)
    {
        RestorePlayerWorld(snapshot.PlayerWorld);
        if (CharacterFileStore.Active?.TryLoad(PlayerProfile.Id) is { } file)
            ApplyCharacter(file);
        else if (snapshot.Character is { } embedded)
            ApplyCharacter(embedded, inventoryAlreadyRestored: true);
        GrantCreativeKitOnce();
    }

    /// <summary>A new world picks up the character's carried state from its character file, when one exists.</summary>
    private void OnSimulationCreated(bool loadedFromSave)
    {
        if (loadedFromSave)
            return;
        if (CharacterFileStore.Active?.TryLoad(PlayerProfile.Id) is { } character)
            ApplyCharacter(character);
        GrantCreativeKitOnce();
    }
}
