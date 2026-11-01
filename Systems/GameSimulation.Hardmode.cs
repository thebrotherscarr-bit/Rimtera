using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The easy-mode → hardmode progression (docs/HARDMODE_DIFF.md, plan items 1–11): the Irradiated Core's gate boss and
/// the Fracture it causes, the core breaker for altars, essences, hardmode stat scaling with a boss damage ceiling,
/// natural night arrivals of the three hard-tier bosses, upgraded event creatures, and the post-hard chain
/// (canopy bulbs → bulb boss → vault key → vault boss → dungeon ritual → four obelisks → the Astro-God).
/// Every step persists as a world flag. Names and numbers are original Rimtera design.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>The Irradiated Core's gate boss; its death causes the Fracture.</summary>
    public const EnemyKind CoreGateBoss = EnemyKind.FissionTyrant;
    /// <summary>The bulb boss that grows from canopy bulbs once the hard trio has fallen.</summary>
    public const EnemyKind BulbBoss = EnemyKind.MutatedFlora;
    /// <summary>The ritual boss waiting at the dungeon entrance after the vault boss.</summary>
    public const EnemyKind RitualBoss = EnemyKind.VoidCantor;
    /// <summary>Ceiling on any single Classic boss hit for the final tier (the per-tier table is <see cref="BossDamageCapFor"/>).</summary>
    public const float BossDamageCap = 100f;

    /// <summary>
    /// Ceiling on a single boss hit after difficulty scaling (PC-15): per tier on Classic (gate and easy 50, hard and event
    /// 80, final <see cref="BossDamageCap"/>), times the difficulty's damage scale so Brutal and Annihilation keep their ×2
    /// and ×3 (D-6). No Classic boss reaches its ceiling (the curve holds without it); it guards against a stray hit.
    /// </summary>
    public static float BossDamageCapFor(EnemyArchetype archetype, WorldDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        var classic = archetype.Tier switch
        {
            BossTier.Final => BossDamageCap,
            BossTier.Hard or BossTier.Event => 80f,
            _ => 50f
        };
        return classic * WorldSettings.EnemyDamageScale(difficulty);
    }

    /// <summary>Chance per dusk (after the Fracture) that an undefeated hard-tier boss comes on its own.</summary>
    public const float HardBossNightChance = 0.2f;
    /// <summary>Minimum days between natural hard-boss arrivals.</summary>
    public const int HardBossNightCooldownDays = 3;
    /// <summary>Distance from the dungeon entrance at which the ritual turns on the player.</summary>
    public const float RitualRadius = 14f;
    /// <summary>Percent chance an eligible creature drops an essence after the Fracture.</summary>
    public const int EssenceDropPercent = 45;

    /// <summary>The three hard-tier bosses summoned from hardmode bars and essences (the mechanical-tier analog).</summary>
    public static IReadOnlyList<EnemyKind> HardTrio { get; } = [EnemyKind.CyberSwarmLeader, EnemyKind.RenegadeOverseer, EnemyKind.WarshipCore];

    /// <summary>The four obelisk sieges and their obelisks.</summary>
    public static IReadOnlyList<(WorldEventKind Event, EnemyKind Obelisk)> Obelisks { get; } =
    [
        (WorldEventKind.EmberObelisk, EnemyKind.EmberObelisk), (WorldEventKind.RiftObelisk, EnemyKind.RiftObelisk),
        (WorldEventKind.GaleObelisk, EnemyKind.GaleObelisk), (WorldEventKind.ShardObelisk, EnemyKind.ShardObelisk)
    ];

    private float _hardmodeTimer;
    private readonly HashSet<long> _nightBossIds = new();
    private static readonly HashSet<EnemyKind> PrismKinds =
        BiomeSpawnTables.KindsFor(BiomeZone.CrystalSpires).Concat(BiomeSpawnTables.HardKindsFor(BiomeZone.CrystalSpires))
            .Where(kind => !EnemyArchetypes.Get(kind).Passive).ToHashSet();
    private static readonly HashSet<EnemyKind> BlightKinds =
    [
        EnemyKind.EventAbomination, EnemyKind.EventCorruptedSurvivor, EnemyKind.EventVoidSiphon, EnemyKind.RadLeech, EnemyKind.GlowStalker
    ];

    /// <summary>
    /// True when the Fracture rescales a creature's health and damage: regular hostiles authored on the pre-Fracture
    /// scale. Bosses, wildlife, the Dungeon Guardian and creatures authored for the Fracture keep their stats (D-18:
    /// Terraria never rescales hardmode creatures; only the spawn pools swap).
    /// </summary>
    public static bool HardmodeRescales(EnemyArchetype archetype)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        return !archetype.IsBoss && !archetype.Passive && archetype.Kind != EnemyKind.DungeonGuardian &&
               !CreatureStatTable.IsFractureAuthored(archetype.Kind) &&
               Obelisks.All(entry => entry.Obelisk != archetype.Kind); // the siege spawns its obelisks unscaled
    }

    /// <summary>
    /// Hardmode multipliers on regular pre-Fracture-authored creatures' (health, damage). The same on every difficulty
    /// (D-6): the world difficulty's own ×0.75/×1/×2/×3 (<see cref="WorldSettings.EnemyHealthScale"/>) applies on top, so
    /// a Brutal creature stays twice its Classic self after the Fracture as before it.
    /// </summary>
    public static (float Health, float Damage) HardmodeScale(WorldDifficulty difficulty) => (1.7f, 1.3f);

    /// <summary>True when the player stands in the Irradiated Core (the bottom band of liquid uranium).</summary>
    public bool InIrradiatedCore => CurrentBiome.Zone == BiomeZone.MoltenChasm || PlayerCenter.Y >= World.Height * 0.8f;

    /// <summary>All three hard-tier bosses have fallen.</summary>
    public bool HardTrioDefeated => HardTrio.All(_defeatedBosses.Contains);

    /// <summary>Tool capability carried by the core breaker and every drill or demolisher crafted after it (G4).</summary>
    public const string AltarBreakerCapability = "altar-breaker";

    /// <summary>
    /// Tool capabilities by item. Only the core breaker (the Fission Tyrant's drop) and tools whose materials come
    /// after it (lumenite, voidsteel, astral, void essence) break altars; tool power alone never does.
    /// </summary>
    private static readonly Dictionary<ItemId, string[]> ToolCapabilities = new ItemId[]
    {
        ItemId.CoreBreaker,
        ItemId.LumeniteDrill, ItemId.VoidsteelDrill, ItemId.AstralDrill,
        ItemId.LumenitePick, ItemId.VoidsteelPick, ItemId.AstralPick, ItemId.VoidAuger,
        ItemId.LumeniteDemolisher, ItemId.VoidsteelDemolisher, ItemId.AstralDemolisher,
        ItemId.GravityBreaker, ItemId.AstralSledge
    }.ToDictionary(id => id, _ => new[] { AltarBreakerCapability });

    /// <summary>True when the tool <paramref name="id"/> carries <paramref name="capability"/>.</summary>
    public static bool HasToolCapability(ItemId id, string capability) =>
        ToolCapabilities.TryGetValue(id, out var capabilities) && capabilities.Contains(capability);

    /// <summary>True for tools that can smash blight altars: those with the <see cref="AltarBreakerCapability"/>.</summary>
    public static bool CanSmashAltars(ItemDefinition? item) =>
        item is { UseKind: ItemUseKind.Pickaxe or ItemUseKind.Demolisher } && HasToolCapability(item.Id, AltarBreakerCapability);

    /// <summary>Hard-tier optional bosses that wait for the Fracture (summons refuse, world triggers give loot only).</summary>
    public static IReadOnlyList<EnemyKind> FractureGatedBosses { get; } = [EnemyKind.Abomination, EnemyKind.PrismEmpress, EnemyKind.BloomMatriarch];

    /// <summary>True when <paramref name="boss"/> may not appear yet because the Fracture has not happened.</summary>
    public bool BossAwaitsFracture(EnemyKind boss) => !FractureActive && (FractureGatedBosses.Contains(boss) || HardTrio.Contains(boss));

    /// <summary>Where the cultist ritual waits: the precursor dungeon's entrance (or a fixed column on small worlds).</summary>
    public GridPoint RitualSite
    {
        get
        {
            var x = Features?.Dungeon is { } dungeon
                ? (dungeon.Left + dungeon.Right) / 2
                : Math.Clamp(SpawnColumn + World.Width / 4, 2, World.Width - 3);
            return new GridPoint(x, FindSurface(x));
        }
    }

    /// <summary>The furthest post-hard chain step reached (for the UI and tests).</summary>
    public string HardmodeStage =>
        Flags.IsSet("boss.astro_god.down") ? "astro-god defeated"
        : Flags.IsSet("obelisks.down") ? "obelisks down"
        : Flags.IsSet("obelisks.started") ? "obelisk siege"
        : Flags.IsSet("boss.void_cantor.down") ? "cantor defeated"
        : Flags.IsSet("ritual.ready") ? "ritual waiting"
        : Flags.IsSet("vault.opened") ? "vault opened"
        : Flags.IsSet("boss.mutated_flora.down") ? "bulb boss defeated"
        : Flags.IsSet("hardmode.bulbs.grown") ? "bulbs grown"
        : FractureActive ? "fractured"
        : "easy mode";

    /// <summary>Clamps a boss hit to the ceiling (pre-difficulty values are capped so the scaled hit still fits).</summary>
    private float CapBossHit(EnemyAgent enemy, float damage, bool preDifficulty = false)
    {
        if (!enemy.IsBoss)
            return damage;
        var cap = BossDamageCapFor(enemy.Archetype, Difficulty);
        if (preDifficulty)
            cap /= WorldSettings.EnemyDamageScale(Difficulty);
        return Math.Min(damage, cap);
    }

    /// <summary>Test hook: a hit of <paramref name="damage"/> from <paramref name="enemy"/> after the boss ceiling.</summary>
    internal float ProbeCappedBossHit(EnemyAgent enemy, float damage) => CapBossHit(enemy, damage);

    /// <summary>Why a boss summon item refuses right now, or null when it may be used.</summary>
    private string? HardmodeSummonRefusal(EnemyKind boss)
    {
        if (boss == CoreGateBoss)
        {
            if (!_defeatedBosses.Contains(LastEasyBoss))
                return $"The igniter stays cold. The {EnemyArchetypes.Get(LastEasyBoss).DisplayName} still guards the surface.";
            if (!InIrradiatedCore)
                return "The igniter needs the Irradiated Core's heat. Carry it down to the liquid uranium.";
            return null;
        }
        if (HardTrio.Contains(boss) && !FractureActive)
            return "The signal finds nothing to answer it until the planet's crust fractures.";
        if (FractureGatedBosses.Contains(boss) && !FractureActive)
            return boss switch
            {
                EnemyKind.Abomination => "The tissue twitches, then lies still. Whatever it calls sleeps until the crust fractures.",
                EnemyKind.PrismEmpress => "The catalyst glints, but no light answers. The prism queen waits for the Fracture.",
                _ => "The nectar sours in the air. The hive's matriarch will not stir before the Fracture."
            };
        if (boss == EnemyKind.AstroGod && !Flags.IsSet("obelisks.down"))
            return "The sigil stays dark. Four obelisks still anchor the sky against what it would call.";
        if (boss == BulbBoss && !HardTrioDefeated)
            return "The bloom withers. Something in the canopy waits for three greater threats to fall first.";
        if (boss == EnemyKind.VaultColossus && !Flags.IsSet("vault.opened"))
            return "The vault core needs the precursor vault. Open its door with the vault key first.";
        if (boss == RitualBoss && !_defeatedBosses.Contains(EnemyKind.VaultColossus))
            return "The hymnal's pages are blank until the Vault Colossus has fallen.";
        return CoverageSummonRefusal(boss); // creature coverage bosses (GameSimulation.CreatureCoverage.cs)
    }

    // ------------------------------------------------------------------ per tick

    private void UpdateHardmode(float deltaSeconds)
    {
        ApplyHardmodeScaling();
        UpdateCreatureCoverage(deltaSeconds); // event mini-bosses and the Lumen Tempest (GameSimulation.CreatureCoverage.cs)
        _hardmodeTimer += deltaSeconds;
        if (_hardmodeTimer < 0.5f)
            return;
        _hardmodeTimer = 0f;
        UpdateObeliskShields();
        if (!Flags.IsSet("hardmode.bulbs.grown") && HardTrioDefeated)
            GrowCanopyBulbs();
        UpdateCultistRitual();
    }

    /// <summary>
    /// After the Fracture every regular creature authored on the pre-Fracture scale gets the difficulty's hardmode health
    /// and damage once; creatures authored for the Fracture keep their stats (D-18, <see cref="HardmodeRescales"/>);
    /// bosses keep their tuning but never exceed <see cref="BossDamageCap"/>.
    /// </summary>
    private void ApplyHardmodeScaling()
    {
        var (health, damage) = HardmodeScale(Difficulty);
        foreach (var enemy in _enemies)
        {
            if (enemy.StatsScaled)
                continue;
            enemy.StatsScaled = true;
            if (enemy.IsBoss)
                enemy.CapDamage(BossDamageCapFor(enemy.Archetype, WorldDifficulty.Classic)); // authored damage, before difficulty
            else if (FractureActive && HardmodeRescales(enemy.Archetype))
                enemy.ScaleStats(health, damage);
        }
    }

    // ------------------------------------------------------------------ defeats

    private void OnHardmodeDefeat(EnemyAgent enemy)
    {
        _nightBossIds.Remove(enemy.Id);
        // A hard-tier boss felled before the Fracture (debug or legacy spawns) does not count toward the trio.
        if (HardTrio.Contains(enemy.Kind) && !FractureActive)
            _defeatedBosses.Remove(enemy.Kind);
        if (enemy.Kind == CoreGateBoss && TriggerFracture())
            PostMessage("The Fission Tyrant's heart splits open, and the crack runs all the way to the surface.");
        if (!enemy.IsBoss)
            TryDropEssence(enemy);
        if (HardTrio.Contains(enemy.Kind) && HardTrioDefeated && Flags.Set("hardmode.trio.down"))
            PostMessage("All three hard-tier threats are scrap. Something swells in the canopy's roots...");
        if (enemy.Kind == EnemyKind.VaultColossus && Flags.Set("ritual.ready"))
            PostMessage("Hooded figures gather at the dungeon gate. Their chant carries on the wind.");
        if (enemy.Kind == RitualBoss)
            StartObeliskSiege();
        if (Obelisks.Any(entry => entry.Obelisk == enemy.Kind))
            OnObeliskDestroyed(enemy.Kind);
    }

    /// <summary>
    /// Essences after the Fracture: prism essence from underground prism creatures, blight essence from underground
    /// blight creatures, flight essence from flyers in the open sky above the surface.
    /// </summary>
    private void TryDropEssence(EnemyAgent enemy)
    {
        if (!FractureActive || enemy.IsPassive || enemy.EventTag is not null)
            return;
        var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
        var cell = new GridPoint(Math.Clamp((int)center.X, 0, World.Width - 1), Math.Clamp((int)center.Y, 0, World.Height - 1));
        var underground = center.Y > World.Height * 0.45f;
        ItemId? essence = null;
        if (enemy.Archetype.Flying && !underground && center.Y < BiomeLayout.SurfaceAt(cell.X) - 1.5f)
            essence = ItemId.FlightEssence;
        else if (underground)
        {
            var zone = BiomeScanner.Scan(World, cell).Zone;
            if (zone == BiomeZone.CrystalSpires || PrismKinds.Contains(enemy.Kind))
                essence = ItemId.PrismEssence;
            else if (zone == BiomeZone.Overgrowth || (zone == BiomeZone.RadiatedDeeps && Blight == WorldBlight.Radiated) ||
                     BlightKinds.Contains(enemy.Kind))
                essence = ItemId.BlightEssence;
        }
        if (essence is not { } item || _lootRandom.Next(100) >= EssenceDropPercent)
            return;
        DropLoot(enemy, item, 1);
        Flags.Increment($"essence.{WorldFlags.Key(item)}.dropped");
    }

    // ------------------------------------------------------------------ altars

    /// <summary>Every altar smash shakes loose a hostile core spirit.</summary>
    private void SpawnCoreSpirit()
    {
        var spirit = SpawnCreature(EnemyKind.FissionWisp, PlayerCenter + new Vector2(Player.Motion.Facing * -5f, -4f));
        Flags.Increment("altars.spirits");
        PostMessage($"A {spirit.DisplayName} tears free of the shattered altar!");
    }

    // ------------------------------------------------------------------ natural hard bosses and events

    private void AdvanceHardmodeClock(WorldEventMoment? boundary)
    {
        // Night arrivals leave at dawn through the per-boss leave rules (BO-6, GameSimulation.BossSpawn.cs).
        if (boundary == WorldEventMoment.Dusk)
        {
            var roll = HardmodeNightRoll(Day);
            if (!TrySpawnHardBossAtDusk(roll))
                TrySpawnOverseerPatrol(roll);
        }
    }

    /// <summary>Deterministic dusk roll in [0,1) for a natural hard-boss arrival (seed and day).</summary>
    public double HardmodeNightRoll(int day)
    {
        unchecked
        {
            var hash = (uint)Seed * 2246822519u ^ (uint)day * 3266489917u ^ 0x9E3779B9u;
            hash ^= hash >> 15;
            hash *= 668265263u;
            hash ^= hash >> 13;
            return (hash & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>
    /// After the Fracture, an undefeated hard-tier boss may arrive on its own at dusk (with a warning), at most once
    /// every <see cref="HardBossNightCooldownDays"/> days. Returns true when one came.
    /// </summary>
    public bool TrySpawnHardBossAtDusk(double roll)
    {
        if (!FractureActive || roll >= HardBossNightChance || _enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive))
            return false;
        var last = Flags.Get("hardmode.nightboss.day");
        if (last > 0 && Day - last < HardBossNightCooldownDays)
            return false;
        var pool = HardTrio.Where(kind => !_defeatedBosses.Contains(kind)).ToArray();
        if (pool.Length == 0)
            return false;
        var kind = pool[Math.Abs(Day) % pool.Length];
        PostMessage(kind switch
        {
            EnemyKind.CyberSwarmLeader => "A chittering signal sweeps the sky. Something has found your colony...",
            EnemyKind.RenegadeOverseer => "Heavy footfalls shake the ground. A rogue command code is broadcasting your position...",
            _ => "Engines drone overhead. A shadow blots out the stars..."
        });
        if (!TrySpawnBoss(kind, BossSpawnSource.NightArrival, out var boss, out _))
            return false;
        _nightBossIds.Add(boss!.Id);
        Flags.Increment("hardmode.nightboss.day", Day - last);
        Flags.Increment("hardmode.nightboss.arrivals");
        return true;
    }

    /// <summary>Chance per dusk that the Overseer mech patrols to a ready colony on its own (G6).</summary>
    public const float OverseerPatrolChance = 0.25f;
    /// <summary>Minimum days between natural Overseer patrols.</summary>
    public const int OverseerPatrolCooldownDays = 3;
    /// <summary>Maximum health the player needs before the Overseer comes on its own.</summary>
    public const float OverseerPatrolMinHealth = 140f;
    /// <summary>Total defence the player needs before the Overseer comes on its own.</summary>
    public const int OverseerPatrolMinDefense = 6;

    /// <summary>True when the player is sturdy enough that the gate boss may come looking for them.</summary>
    public bool ReadyForOverseerPatrol => Player.MaximumHitPoints >= OverseerPatrolMinHealth && TotalDefense >= OverseerPatrolMinDefense;

    /// <summary>
    /// Natural first-boss arrival: until the Overseer mech is first defeated, a dusk roll below
    /// <see cref="OverseerPatrolChance"/> brings it to a ready player (with a warning first), at most once every
    /// <see cref="OverseerPatrolCooldownDays"/> days. It withdraws at dawn. Returns true when it came.
    /// </summary>
    public bool TrySpawnOverseerPatrol(double roll)
    {
        if (_defeatedBosses.Contains(EnemyKind.StoneSentinel) || roll >= OverseerPatrolChance || !ReadyForOverseerPatrol ||
            _enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive))
            return false;
        var last = Flags.Get("overseer.patrol.day");
        if (last > 0 && Day - last < OverseerPatrolCooldownDays)
            return false;
        PostMessage("A searchlight sweeps across the hills. Something mechanical has noticed your colony...");
        if (!TrySpawnBoss(EnemyKind.StoneSentinel, BossSpawnSource.Patrol, out var boss, out _))
            return false;
        _nightBossIds.Add(boss!.Id);
        Flags.Increment("overseer.patrol.day", Day - last);
        Flags.Increment("overseer.patrol.arrivals");
        PostMessage($"The {boss.DisplayName} has arrived on patrol!");
        return true;
    }

    /// <summary>Hard tiers of the older events after the Fracture: stronger creatures replace part of their roster.</summary>
    private static readonly Dictionary<EnemyKind, EnemyKind[]> HardEventUpgrades = BuildEventUpgrades();

    private static Dictionary<EnemyKind, EnemyKind[]> BuildEventUpgrades()
    {
        var upgrades = new Dictionary<EnemyKind, EnemyKind[]>();
        void Map(EnemyKind[] easy, EnemyKind[] hard)
        {
            foreach (var kind in easy)
                upgrades[kind] = hard;
        }
        // Rust Moon (the blood-moon analog) gains heavier horrors.
        // Creature coverage: the rust jester is the Rust Moon's own hardmode unit (Terraria's blood-moon clown).
        Map([EnemyKind.EventHusk, EnemyKind.EventRustWraith, EnemyKind.EventRustHound, EnemyKind.EventEyeDrone],
            [EnemyKind.EventAbomination, EnemyKind.EventCorrodedMech, EnemyKind.EventEclipseShade, EnemyKind.VengefulWisp, EnemyKind.RustJester]);
        // Creature coverage: rainfall gains its storm cloud, the dust storm its sand-swimmer (Terraria: Angry Nimbus, Sand Shark).
        Map([EnemyKind.DrizzleGel, EnemyKind.RainSkipper, EnemyKind.SlickerHusk], [EnemyKind.Thunderhead]);
        Map([EnemyKind.EventDustDevil, EnemyKind.EventSandRay], [EnemyKind.DuneRazorfin]);
        // Creature coverage: Swarm Rain's hardmode swarmer (Terraria's rainbow slime).
        Map([EnemyKind.EventSwarmer, EnemyKind.ArmoredSwarmer], [EnemyKind.PrismSwarmer]);
        // Scavenger raids bring their veterans.
        Map([EnemyKind.EventScavengerGrunt, EnemyKind.EventScavengerSniper, EnemyKind.EventScout],
            [EnemyKind.NeoTerraReaver, EnemyKind.EventScavengerBrute, EnemyKind.EventScrapElemental]);
        // The defence protocol escalates to its heavy units.
        Map([EnemyKind.EventProtocolGrunt, EnemyKind.EventProtocolShieldbearer],
            [EnemyKind.EventProtocolOgre, EnemyKind.EventProtocolWyvern, EnemyKind.EventProtocolMage]);
        // Weather swarms turn nastier.
        Map([EnemyKind.EventAcidSlime, EnemyKind.EventDustRaider], [EnemyKind.EventCorruptedSurvivor, EnemyKind.EventScrapElemental]);
        return upgrades;
    }

    /// <summary>The event creature that actually appears: after the Fracture, 40% of upgradable picks become hard ones.</summary>
    private EnemyKind HardmodeEventCreature(EnemyKind kind, double roll) =>
        LateEventCreature(kind, roll) is { } late ? late // creature coverage: post-Mutated-Flora Dark Sun units
        : FractureActive && HardEventUpgrades.TryGetValue(kind, out var hard) && roll * 7.0 % 1.0 < 0.4
            ? hard[(int)(roll * 13.0 % 1.0 * hard.Length) % hard.Length]
            : kind;

    /// <summary>True when <paramref name="kind"/> may appear as a hardmode upgrade of an event creature.</summary>
    public static bool IsHardEventUpgrade(EnemyKind kind) => HardEventUpgrades.Values.Any(list => list.Contains(kind));

    /// <summary>Test hook: the creature an event pick becomes for a given roll.</summary>
    internal EnemyKind ProbeHardEventCreature(EnemyKind kind, double roll) => HardmodeEventCreature(kind, roll);

    // ------------------------------------------------------------------ post-hard chain

    /// <summary>Grows canopy bulbs in the underground canopy band (the jungle columns, below the surface). Returns bulbs placed.</summary>
    public int GrowCanopyBulbs()
    {
        Flags.Set("hardmode.bulbs.grown");
        var ranges = Features?.Canopies is { Count: > 0 } canopies ? canopies.ToArray() : [(2, World.Width - 3)];
        var wanted = Math.Clamp(World.Width / 250, 2, 6);
        var placed = new List<GridPoint>();
        for (var attempt = 0; attempt < wanted * 600 && placed.Count < wanted; attempt++)
        {
            var (left, right) = ranges[TriggerRandom.Next(ranges.Length)];
            var x = TriggerRandom.Next(Math.Max(1, left), Math.Max(left + 1, Math.Min(World.Width - 2, right)));
            var y = TriggerRandom.Next((int)(World.Height * 0.45), (int)(World.Height * 0.78));
            if (!World.Contains(x, y + 1) || World.TileAt(x, y) != TileId.Air || !Tiles[World.TileAt(x, y + 1)].IsSolid ||
                World.GetCell(new GridPoint(x, y)).LiquidUnits > 0 || placed.Any(point => Math.Abs(point.X - x) < 8))
                continue;
            World.SetTile(new GridPoint(x, y), TileId.CanopyBulb);
            placed.Add(new GridPoint(x, y));
        }
        if (placed.Count > 0)
            _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, placed));
        Flags.Increment("hardmode.bulbs.count", placed.Count);
        PostMessage("Pink bulbs swell among the canopy's roots. Break one, and something will answer.");
        return placed.Count;
    }

    private void BreakCanopyBulb()
    {
        Flags.Increment("canopy.bulbs.broken");
        if (!TrySpawnBoss(BulbBoss, BossSpawnSource.WorldTrigger, out var boss, out _))
        {
            PostMessage("The bulb bursts in a cloud of spores.");
            return;
        }
        PostMessage($"The bulb tears open. The {boss!.DisplayName} uncoils from the roots!");
    }

    /// <summary>After the vault boss, the ritual at the dungeon entrance turns on a player who comes close.</summary>
    private void UpdateCultistRitual()
    {
        if (!_defeatedBosses.Contains(EnemyKind.VaultColossus) || _defeatedBosses.Contains(RitualBoss) ||
            Flags.IsSet("obelisks.started") || _enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive))
            return;
        var site = RitualSite;
        if (Vector2.Distance(PlayerCenter, new Vector2(site.X + 0.5f, site.Y - 1f)) > RitualRadius)
            return;
        var size = EnemyArchetypes.Get(RitualBoss).Size;
        if (!TrySpawnBoss(RitualBoss, BossSpawnSource.Ritual, out _, out _, new Vector2(site.X - size.X / 2f, site.Y - size.Y - 3f)))
            return;
        Flags.Increment("ritual.cantor.summons");
        PostMessage("The chant breaks off. The Void Cantor turns from the dungeon gate!");
    }

    /// <summary>The Void Cantor's death raises the four obelisks and starts their sieges.</summary>
    private void StartObeliskSiege()
    {
        if (!Flags.Set("obelisks.started"))
            return;
        for (var index = 0; index < Obelisks.Count; index++)
        {
            var (kind, obelisk) = Obelisks[index];
            var x = Math.Clamp((int)(World.Width * (0.14f + 0.24f * index)), 4, World.Width - 6);
            var size = EnemyArchetypes.Get(obelisk).Size;
            var enemy = SpawnCreature(obelisk, new Vector2(x, FindSurface(x) - size.Y));
            enemy.EventTag = kind;
            enemy.Shielded = true;
            enemy.StatsScaled = true;
            _worldEvents.TryStart(kind, this, forced: true, out _);
        }
        PostMessage($"Four obelisks burst from the ground! Each is shielded until {WorldEventSystem.ObeliskShieldKills} of its creatures fall.");
    }

    private void UpdateObeliskShields()
    {
        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive || Obelisks.All(entry => entry.Obelisk != enemy.Kind))
                continue;
            var kind = Obelisks.First(entry => entry.Obelisk == enemy.Kind).Event;
            var state = _worldEvents.Active.FirstOrDefault(candidate => candidate.Kind == kind);
            // EV-1: a missing siege state never drops the shield. Unless the siege was cleared, it is restarted so its kills
            // count again (an old save whose siege timed out); only the obelisk's own quota of kills drops its shield.
            if (state is null && !Flags.IsSet(_worldEvents.Spec(kind).FlagName))
            {
                _worldEvents.TryStart(kind, this, forced: true, out _);
                state = _worldEvents.Active.FirstOrDefault(candidate => candidate.Kind == kind);
            }
            var shielded = state is null ? !Flags.IsSet(_worldEvents.Spec(kind).FlagName) : state.Kills < state.KillsNeeded;
            if (enemy.Shielded && !shielded)
                PostMessage($"The {enemy.DisplayName}'s shield collapses!");
            enemy.Shielded = shielded;
        }
    }

    private void OnObeliskDestroyed(EnemyKind obelisk)
    {
        var kind = Obelisks.First(entry => entry.Obelisk == obelisk).Event;
        if (_worldEvents.Active.FirstOrDefault(state => state.Kind == kind) is { } state)
            _worldEvents.End(state, this, completed: true);
        else
            Flags.Set(_worldEvents.Spec(kind).FlagName);
        if (!Obelisks.All(entry => Flags.IsSet(_worldEvents.Spec(entry.Event).FlagName)) || !Flags.Set("obelisks.down"))
            return;
        if (_astralCountdown <= 0f && !_enemies.Any(enemy => enemy.Kind == EnemyKind.AstroGod && enemy.IsAlive))
            _astralCountdown = AstralCountdownSeconds;
        PostMessage($"The last obelisk falls. The sky splits open: the Astro-God arrives in {AstralCountdownSeconds:0} seconds!");
    }
}
