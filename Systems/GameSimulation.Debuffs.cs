using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The hit pipeline on the player and the debuffs creatures inflict (0.1.9 combat core, stream B):
/// <list type="bullet">
/// <item>IC-3: creature debuffs on contact and shots (<see cref="EnemyArchetype.Inflicts"/>, filled by
/// <see cref="CreatureInflictions"/>) and what each one does: bleeding stops regeneration, chilled and slow cut speed
/// (the strongest wins), confusion swaps left and right, darkness dims the light, weak cuts melee damage and defense,
/// broken armour halves defense, silence stops Tech use, venom, frostburn and cursed fire burn in Terraria units.</item>
/// <item>EN-17: creature afflictions from weapons and ammo: venom, corroded (defense shred), the salvage mark (credits
/// on the kill) and wet (puts burning out).</item>
/// <item>EN-12: contact by box overlap with a 0.1-tile inset; worm segments test against the player's box.</item>
/// <item>IC-18: ±15% variance on creature and shot hits (luck counts against the player), and hostile shots pass through
/// an immune player.</item>
/// </list>
/// Rolls use their own streams so the legacy combat and spawn sequences never shift.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>Chilled leaves this share of movement speed (Terraria: 0.75).</summary>
    public const float ChilledMoveFactor = 0.75f;
    /// <summary>Slow leaves this share of movement speed (Terraria: 0.5). The stronger of chilled and slow applies.</summary>
    public const float SlowMoveFactor = 0.5f;
    /// <summary>Weak multiplies close-quarters damage by this (its −4 defense rides on the status definition).</summary>
    public const float WeakMeleeFactor = 0.8f;
    /// <summary>Share of the light darkness takes away from what the player sees.</summary>
    public const float DarknessDimmingShare = 0.5f;
    /// <summary>Defense corroded strips from a creature (Terraria's Ichor analog).</summary>
    public const int CorrodedDefenseShred = 15;
    /// <summary>Extra share of a creature's credits the salvage mark adds on the kill (at least 1 credit).</summary>
    public const float SalvageMarkCreditShare = 0.5f;
    /// <summary>Damage-over-time per second venom deals to a creature (four times poison's).</summary>
    public const float CreatureVenomDps = 16f;
    /// <summary>Contact boxes are shrunk by this on every side before testing overlap (EN-12).</summary>
    public const float ContactInset = 0.1f;

    private Random? _hurtRandomStream;
    private Random? _inflictRandomStream;
    private Random HurtRandom => _hurtRandomStream ??= new Random(Seed ^ 0x4B17E5);
    private Random InflictRandom => _inflictRandomStream ??= new Random(Seed ^ 0xDEB0FF);

    // ================================================================== debuff effects on the player

    /// <summary>Movement multiplier from chilled and slow: the strongest applies (×0.5 for both, never ×0.375).</summary>
    public float DebuffMoveFactor
    {
        get
        {
            var factor = 1f;
            if (Player.Statuses.IsActive(StatusId.Chilled))
                factor = Math.Min(factor, ChilledMoveFactor);
            if (Player.Statuses.IsActive(StatusId.Slow))
                factor = Math.Min(factor, SlowMoveFactor);
            return factor;
        }
    }

    /// <summary>Confusion: left and right are swapped in the simulation (not the input layer).</summary>
    public bool PlayerConfused => Player.Statuses.IsActive(StatusId.Confused);

    /// <summary>Bleeding: no natural, gear or status regeneration.</summary>
    public bool PlayerBleeding => Player.Statuses.IsActive(StatusId.Bleeding);

    /// <summary>Silence: Tech items cannot be used.</summary>
    public bool PlayerSilenced => Player.Statuses.IsActive(StatusId.Silenced);

    /// <summary>Weak: close-quarters damage ×<see cref="WeakMeleeFactor"/>.</summary>
    public bool PlayerWeak => Player.Statuses.IsActive(StatusId.Weak);

    /// <summary>Share of the light the renderer takes away (darkness); 0 when clear.</summary>
    public float DarknessDimming => Player.Statuses.IsActive(StatusId.Darkness) ? DarknessDimmingShare : 0f;

    /// <summary>Swaps the horizontal input while confused.</summary>
    private MovementInput ApplyConfusion(MovementInput movement) =>
        PlayerConfused ? movement with { Horizontal = -movement.Horizontal } : movement;

    /// <summary>Silence blocks Tech items; posts why and returns true when the use is refused.</summary>
    private bool SilenceBlocksUse()
    {
        if (!PlayerSilenced || Player.Inventory.SelectedItem is not { } item || item.EffectiveClass != DamageClass.Tech)
            return false;
        PostMessage("Silenced: Tech items will not answer.");
        return true;
    }

    /// <summary>Applies broken armour (halves) to a defense total.</summary>
    private int DebuffDefense(int defense) =>
        Player.Statuses.IsActive(StatusId.BrokenArmor) && defense > 0 ? defense / 2 : defense;

    /// <summary>Weak's close-quarters damage cut, applied on top of every other melee bonus.</summary>
    private CombatStats DebuffCombatStats(CombatStats stats) =>
        PlayerWeak ? stats with { MeleeMultiplier = stats.MeleeMultiplier * WeakMeleeFactor } : stats;

    /// <summary>Rolls a creature's debuffs onto the player after a hit that landed.</summary>
    private void InflictOnPlayer(EnemyArchetype archetype)
    {
        foreach (var (status, chance, seconds) in archetype.Inflicts)
        {
            if (chance < 1f && InflictRandom.NextDouble() >= chance)
                continue;
            Player.Statuses.Apply(status, seconds * VitalHoursPerRealSecond, Player.Stats.ImmunityTags);
        }
    }

    // ================================================================== the hit pipeline on the player

    /// <summary>
    /// One hit's ±15% spread on the player (whole points). Luck counts against the hit: a lucky player's second roll keeps
    /// the lower damage, an unlucky one's the higher.
    /// </summary>
    private float RollHurtVariance(float amount) =>
        float.IsFinite(amount) && amount > 0f ? CombatTuning.RollVariance(amount, HurtRandom, -Math.Clamp(Luck, -1f, 1f)) : amount;

    /// <summary>A hostile shot reaching the player: variance, the gear pipeline, then the shooter's debuffs.</summary>
    private float HitPlayerFromProjectile(ProjectileState projectile)
    {
        var applied = GearHitPlayer(null, RollHurtVariance(ScaleIncoming(projectile.Damage)), projectile.Kind);
        if (applied > 0f && projectile.SourceKind is { } kind && EnemyArchetypes.Contains(kind))
            InflictOnPlayer(EnemyArchetypes.Get(kind));
        return applied;
    }

    /// <summary>
    /// True when a creature's box, shrunk by <see cref="ContactInset"/> on every side, overlaps the player's box: a body
    /// has to sink 0.1 tile into the player before it hurts, so grazing corners do not.
    /// </summary>
    internal static bool ContactBoxesOverlap(Vector2 creaturePosition, Vector2 creatureSize, Vector2 playerPosition, Vector2 playerSize)
    {
        var inset = new Vector2(Math.Min(ContactInset, creatureSize.X * 0.25f), Math.Min(ContactInset, creatureSize.Y * 0.25f));
        var (min, max) = (creaturePosition + inset, creaturePosition + creatureSize - inset);
        var (playerMin, playerMax) = (playerPosition, playerPosition + playerSize);
        return min.X < playerMax.X && playerMin.X < max.X && min.Y < playerMax.Y && playerMin.Y < max.Y;
    }

    /// <summary>A non-boss creature's body touches the player's box (EN-12).</summary>
    private bool ContactTouchesPlayer(EnemyAgent enemy) =>
        ContactBoxesOverlap(enemy.Motion.Position, enemy.Motion.Size, Player.Motion.Position, Player.Motion.Size);

    /// <summary>A worm segment (a square as wide as the head) touches the player's box.</summary>
    private bool SegmentTouchesPlayer(EnemyAgent worm, Vector2 segment)
    {
        var side = Math.Max(0.3f, worm.Motion.Size.X * 0.9f);
        var size = new Vector2(side, side);
        return ContactBoxesOverlap(segment - size / 2f, size, Player.Motion.Position, Player.Motion.Size);
    }

    // ================================================================== creature afflictions (EN-17)

    /// <summary>Defense a creature has left against player hits once corroded.</summary>
    internal static int CorrodedDefense(EnemyAgent enemy, int defense) =>
        enemy.HasAffliction(StatusId.Corroded) ? Math.Max(0, defense - CorrodedDefenseShred) : defense;

    /// <summary>Credits a creature drops: the salvage mark adds half again (at least one).</summary>
    internal static int SalvageMarkCredits(EnemyAgent enemy, int credits) =>
        enemy.HasAffliction(StatusId.SalvageMark)
            ? credits + Math.Max(1, (int)MathF.Ceiling(credits * SalvageMarkCreditShare))
            : credits;

    /// <summary>
    /// Wet rules on a fresh affliction: a wet creature cannot be set burning, and getting wet puts burning out.
    /// Returns false when the affliction is refused.
    /// </summary>
    private static bool WetAllowsAffliction(EnemyAgent enemy, StatusId id)
    {
        if (id == StatusId.Burning && enemy.HasAffliction(StatusId.Wet))
            return false;
        if (id == StatusId.Wet)
            enemy.ClearAffliction(StatusId.Burning);
        return true;
    }

    /// <summary>A creature in water is wet (and so stops burning).</summary>
    private void SoakEnemyInWater(EnemyAgent enemy)
    {
        var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
        var cell = new GridPoint((int)MathF.Floor(center.X), (int)MathF.Floor(center.Y));
        if (!World.Contains(cell))
            return;
        var data = World.GetCell(cell);
        if (data.LiquidKind == LiquidKind.Water && data.LiquidDepth > 0.4f)
        {
            enemy.Afflict(StatusId.Wet, AfflictionSeconds(StatusId.Wet));
            enemy.ClearAffliction(StatusId.Burning);
        }
    }

    // ================================================================== auto-swing (IC-15)

    /// <summary>
    /// Whether holding the use button repeats the held item right now: the item's own rule, or any melee weapon or whip
    /// while an auto-swing accessory is worn (<see cref="ItemUseRules.AutoReuse(ItemDefinition?, bool)"/>).
    /// </summary>
    public bool AutoReuseSelected => ItemUseRules.AutoReuse(Player.Inventory.SelectedItem, AutoSwingActive);

    // ================================================================== self-test probes

    /// <summary>Self-test probe: one creature contact hit on the player (immunity cleared first). Returns the damage applied.</summary>
    internal float ProbeContactHit(EnemyAgent enemy, float amount)
    {
        Player.ImmunitySeconds = 0f;
        return HitPlayerFromEnemy(enemy, amount, ColonistAnatomy.ContactDamageKind(enemy.Archetype));
    }

    /// <summary>Self-test probe: does the creature's body touch the player's box?</summary>
    internal bool ProbeContactTouches(EnemyAgent enemy) => ContactTouchesPlayer(enemy);

    /// <summary>Self-test probe: does a worm segment at <paramref name="segment"/> touch the player's box?</summary>
    internal bool ProbeSegmentTouches(EnemyAgent worm, Vector2 segment) => SegmentTouchesPlayer(worm, segment);

    /// <summary>Self-test probe: fires one hostile bolt (optionally from a creature kind, whose debuffs it carries).</summary>
    internal ProjectileState ProbeFireHostileBolt(Vector2 origin, Vector2 velocity, float damage, EnemyKind? source = null)
    {
        var bolt = new ProjectileState(_nextEntityId++, origin, velocity, damage, 2f, 3f, ProjectileTeam.Enemy,
            source is { } kind ? EnemyArchetypes.Get(kind).DisplayName : null) { SourceKind = source };
        _projectiles.Add(bolt);
        return bolt;
    }

    /// <summary>Self-test probe: one ranged attack with the given weapon (ammo must be carried); returns the shots fired.</summary>
    internal IReadOnlyList<ProjectileState> ProbeAttackRanged(ItemDefinition item, GridPoint target)
    {
        var before = _projectiles.Count;
        Player.ActionCooldownTicks = 0;
        AttackRanged(item, target);
        var fired = _projectiles.Skip(before).ToArray();
        _projectiles.RemoveRange(before, _projectiles.Count - before);
        return fired;
    }

    /// <summary>Self-test probe: a creature's defense against player hits right now.</summary>
    internal static int ProbeCreatureDefense(EnemyAgent enemy) => CreatureDefense(enemy);

    /// <summary>Self-test probe: credits a creature would drop now.</summary>
    internal static int ProbeCreditDrop(EnemyAgent enemy) => SalvageMarkCredits(enemy, enemy.Archetype.CreditDrop);
}
