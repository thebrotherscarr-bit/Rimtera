namespace Rimtera.Entities;

[Flags]
public enum BodyPartFunction
{
    None = 0,
    ConsciousnessSource = 1 << 0,
    SightSource = 1 << 1,
    MovingLimbCore = 1 << 2,
    MovingLimbSegment = 1 << 3,
    MovingLimbDigit = 1 << 4,
    ManipulationLimbCore = 1 << 5,
    ManipulationLimbSegment = 1 << 6,
    ManipulationLimbDigit = 1 << 7,
    BreathingSource = 1 << 8,
    BloodPumpingSource = 1 << 9,
    DigestionSource = 1 << 10,
    TalkingSource = 1 << 11,
    BloodFiltrationSource = 1 << 12,
    SpinalSupport = 1 << 13
}

/// <summary>A node in an entity's body tree. Parts may have several functional tags.</summary>
public sealed class BodyPart
{
    private readonly List<BodyPart> _children = new();

    public BodyPart(string name, float maxHealth, BodyPartFunction functions = BodyPartFunction.None, float coverage = 1f,
        BodyPartDepth depth = BodyPartDepth.Outside, bool vital = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A body-part name is required.", nameof(name));
        if (!float.IsFinite(maxHealth) || maxHealth <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxHealth), "Maximum health must be finite and positive.");
        if (!float.IsFinite(coverage) || coverage < 0)
            throw new ArgumentOutOfRangeException(nameof(coverage), "Coverage must be finite and non-negative.");

        Name = name;
        MaxHealth = maxHealth;
        Functions = functions;
        Coverage = coverage;
        Depth = depth;
        IsVital = vital;
    }

    public string Name { get; }
    public float MaxHealth { get; }
    public BodyPartFunction Functions { get; }
    /// <summary>Fraction of the parent's hit area this part's subtree occupies (also the capacity weight).</summary>
    public float Coverage { get; }
    public BodyPartDepth Depth { get; }
    /// <summary>Losing a vital part (brain, heart) is fatal.</summary>
    public bool IsVital { get; }
    public BodyPart? Parent { get; private set; }
    public IReadOnlyList<BodyPart> Children => _children;
    public bool IsMissing { get; internal set; }
    public bool IsRoot => Parent is null;

    public void AddChild(BodyPart child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
            throw new InvalidOperationException($"Body part '{child.Name}' already belongs to a body tree.");

        for (BodyPart? ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, child))
                throw new InvalidOperationException("A body tree cannot contain a cycle.");
        }

        child.Parent = this;
        _children.Add(child);
    }

    public IEnumerable<BodyPart> TraversePreOrder()
    {
        yield return this;
        foreach (var child in _children)
        foreach (var descendant in child.TraversePreOrder())
            yield return descendant;
    }

    public bool IsDescendantOf(BodyPart ancestor)
    {
        for (var current = this; current is not null; current = current.Parent)
            if (ReferenceEquals(current, ancestor))
                return true;
        return false;
    }
}

/// <summary>
/// An original, data-driven body condition. For part-damaging kinds severity is HP lost on the part;
/// for whole-body or illness kinds (blood loss, radiation, infection) it is a 0..1 progression.
/// </summary>
public sealed class Injury
{
    internal const float HealingEpsilon = 0.0001f;

    public Injury(
        string name,
        BodyPart part,
        float severity,
        float severityChangePerDay = 0,
        float painPerSeverity = 0.015f)
        : this(name, part, ConditionKind.Wound, severity, severityChangePerDay, painPerSeverity)
    {
    }

    internal Injury(string name, BodyPart part, ConditionKind kind, float severity, float severityChangePerDay, float painPerSeverity)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An injury name is required.", nameof(name));
        ArgumentNullException.ThrowIfNull(part);
        if (!float.IsFinite(severity) || severity < 0 || severity > part.MaxHealth)
            throw new ArgumentOutOfRangeException(nameof(severity), "Severity must be between zero and the part's maximum health.");
        if (!float.IsFinite(severityChangePerDay))
            throw new ArgumentOutOfRangeException(nameof(severityChangePerDay));
        if (!float.IsFinite(painPerSeverity) || painPerSeverity < 0)
            throw new ArgumentOutOfRangeException(nameof(painPerSeverity));

        Name = name;
        Part = part;
        Kind = kind;
        Severity = severity;
        PeakSeverity = severity;
        SeverityChangePerDay = severityChangePerDay;
        PainPerSeverity = painPerSeverity;
    }

    internal static Injury Create(ConditionKind kind, BodyPart part, float severity, int sequence)
    {
        var definition = ConditionCatalog.Get(kind);
        return new Injury($"{definition.Label}-{sequence}", part, kind, Math.Clamp(severity, 0f, part.MaxHealth), 0f,
            definition.PainPerSeverity);
    }

    public string Name { get; }
    public BodyPart Part { get; }
    public ConditionKind Kind { get; internal set; }
    public ConditionDefinition Definition => ConditionCatalog.Get(Kind);
    public float Severity { get; private set; }
    public float SeverityChangePerDay { get; }
    public float PainPerSeverity { get; internal set; }
    public float AgeHours { get; internal set; }
    public bool IsTended { get; internal set; }
    public float TendQuality { get; internal set; }
    /// <summary>Hours a tend keeps working (infections need re-tending; wounds stay tended).</summary>
    public float TendHoursRemaining { get; internal set; }
    /// <summary>A freshly lost part bleeds until tended or it closes on its own.</summary>
    public bool IsFresh { get; internal set; }
    public float PeakSeverity { get; internal set; }
    internal bool InfectionRolled { get; set; }
    internal bool ScarRolled { get; set; }

    public bool IsPermanent => Kind is ConditionKind.Scar or ConditionKind.MissingPart;
    /// <summary>True when the condition subtracts HP from its part.</summary>
    public bool DamagesPart => Definition.DamagesPart;

    public float PainContribution => Kind switch
    {
        ConditionKind.MissingPart => IsFresh ? 0.12f : PainPerSeverity,
        ConditionKind.Wound => Severity * PainPerSeverity,
        _ when Definition.DamagesPart => Severity * PainPerSeverity * (IsTended ? 0.6f : 1f),
        _ => Severity * PainPerSeverity
    };

    public bool IsHealed => !IsPermanent && Severity <= HealingEpsilon;

    /// <summary>Bleeding per in-game hour (blood-loss severity units).</summary>
    public float BleedRate
    {
        get
        {
            if (Kind == ConditionKind.MissingPart)
                return IsFresh && !IsTended ? 0.06f * Part.MaxHealth / 25f : 0f;
            var bleed = Definition.BleedPerSeverityHour;
            if (bleed <= 0f || IsTended)
                return 0f;
            var clot = Math.Clamp(1f - AgeHours / 16f, 0f, 1f);
            return Severity * bleed * clot;
        }
    }

    /// <summary>Stage-aware label such as "gash (left arm)".</summary>
    public string Label
    {
        get
        {
            var stages = Definition.StageLabels;
            var fraction = Definition.DamagesPart ? Severity / Part.MaxHealth : Severity;
            var stage = fraction < 0.25f ? 0 : fraction < 0.6f ? 1 : 2;
            var text = stages[Math.Clamp(stage, 0, stages.Length - 1)];
            if (Kind == ConditionKind.Wound)
                text = Name;
            return Definition.IsWholeBody ? text : $"{text} ({Part.Name})";
        }
    }

    internal void SetSeverity(float value)
    {
        var max = Definition.DamagesPart || Kind == ConditionKind.Wound ? Part.MaxHealth : 1f;
        Severity = Math.Clamp(value, 0f, max);
        if (Severity > PeakSeverity)
            PeakSeverity = Severity;
    }

    internal void Advance(float elapsedHours)
    {
        var change = SeverityChangePerDay * (elapsedHours / 24f);
        Severity = Math.Clamp(Severity + change, 0, Part.MaxHealth);
    }
}

/// <summary>
/// Owns a body tree and its conditions: chooses hit parts, applies and propagates damage, destroys parts,
/// bleeds, heals (faster when tended or resting), rolls infections and scars, and derives capacities,
/// pain shock, the downed state and death. Original Rimtera tuning; deliberately simpler than any
/// inspected commercial implementation.
/// </summary>
public sealed class HealthComponent
{
    public const float DownedConsciousness = 0.3f;
    public const float DownedMoving = 0.15f;
    private const float InfectionRollAgeHours = 3f;
    private const float InfectionTendHours = 12f;
    private const float FreshStumpHours = 12f;

    private readonly List<Injury> _injuries = new();
    private readonly float _painConsciousnessScale;
    private readonly float _maximumPainPenalty;
    private readonly Random _random;
    private readonly Dictionary<BodyCapacity, float> _capacities = new();
    private int _sequence;

    public HealthComponent(
        BodyPart root,
        float painConsciousnessScale = 0.25f,
        float maximumPainPenalty = 0.25f,
        int seed = 0x5EED)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Parent is not null)
            throw new ArgumentException("HealthComponent requires the root of a body tree.", nameof(root));
        if (!float.IsFinite(painConsciousnessScale) || painConsciousnessScale < 0)
            throw new ArgumentOutOfRangeException(nameof(painConsciousnessScale));
        if (!float.IsFinite(maximumPainPenalty) || maximumPainPenalty is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(maximumPainPenalty));

        Root = root;
        _painConsciousnessScale = painConsciousnessScale;
        _maximumPainPenalty = maximumPainPenalty;
        _random = new Random(seed);
        Recalculate();
    }

    public BodyPart Root { get; }
    public IReadOnlyList<Injury> Injuries => _injuries;
    public float PainLevel { get; private set; }
    public float Consciousness { get; private set; }
    /// <summary>Pain at or above this level knocks the body down (pain shock).</summary>
    public float PainShockThreshold { get; set; } = 0.8f;
    /// <summary>Resting bodies heal half again as fast.</summary>
    public bool IsResting { get; set; }
    public float BloodLoss => FindWholeBody(ConditionKind.BloodLoss)?.Severity ?? 0f;
    public float RadiationSickness => FindWholeBody(ConditionKind.RadiationSickness)?.Severity ?? 0f;
    public float BleedRate { get; private set; }
    public bool IsBleeding => BleedRate > 0.0001f;
    public bool InPainShock => PainLevel >= PainShockThreshold;
    public bool IsDead { get; private set; }
    public string? DeathCause { get; private set; }
    public bool IsDowned { get; private set; }
    public string? DownedReason { get; private set; }
    public float Moving => GetCapacity(BodyCapacity.Moving);
    public float Manipulation => GetCapacity(BodyCapacity.Manipulation);
    public float Sight => GetCapacity(BodyCapacity.Sight);
    public float Breathing => GetCapacity(BodyCapacity.Breathing);
    public float BloodPumping => GetCapacity(BodyCapacity.BloodPumping);
    public bool NeedsTending => _injuries.Any(IsTendable);
    public bool HasInfection => _injuries.Any(injury => injury.Kind == ConditionKind.Infection);

    /// <summary>All parts (pre-order) including missing ones.</summary>
    public IEnumerable<BodyPart> Parts => Root.TraversePreOrder();

    public BodyPart? FindPart(string name) =>
        Root.TraversePreOrder().FirstOrDefault(part => string.Equals(part.Name, name, StringComparison.OrdinalIgnoreCase));

    public float GetCapacity(BodyCapacity capacity) => _capacities.TryGetValue(capacity, out var value) ? value : 0f;

    public IReadOnlyDictionary<BodyCapacity, float> Capacities => _capacities;

    public void AddInjury(Injury injury)
    {
        ArgumentNullException.ThrowIfNull(injury);
        if (!ContainsPart(injury.Part))
            throw new ArgumentException("The injury's body part is not in this component's body tree.", nameof(injury));

        _injuries.Add(injury);
        Recalculate();
    }

    public bool RemoveInjury(Injury injury)
    {
        var removed = _injuries.Remove(injury);
        if (removed)
            Recalculate();
        return removed;
    }

    public void SetPartMissing(BodyPart part, bool missing = true)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (!ContainsPart(part))
            throw new ArgumentException("The body part is not in this component's body tree.", nameof(part));

        part.IsMissing = missing;
        Recalculate();
    }

    /// <summary>Clears every condition and restores all parts (respawn).</summary>
    public void Reset()
    {
        _injuries.Clear();
        foreach (var part in Root.TraversePreOrder())
            part.IsMissing = false;
        IsDead = false;
        DeathCause = null;
        Recalculate();
    }

    /// <summary>Current HP of one part: zero when it or an ancestor is missing.</summary>
    public float GetPartHealth(BodyPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (IsEffectivelyMissing(part))
            return 0f;
        var damage = 0f;
        foreach (var injury in _injuries)
            if (ReferenceEquals(injury.Part, part) && (injury.DamagesPart || injury.Kind == ConditionKind.Wound))
                damage += injury.Severity;
        return Math.Max(0f, part.MaxHealth - damage);
    }

    public IReadOnlyList<BodyPartStatus> GetPartStatuses() => Root.TraversePreOrder()
        .Select(part => new BodyPartStatus(
            part.Name, part.Parent?.Name, part.Depth, GetPartHealth(part), part.MaxHealth, IsEffectivelyMissing(part),
            part.Functions,
            _injuries.Where(injury => ReferenceEquals(injury.Part, part) && !injury.Definition.IsWholeBody)
                .Select(View).ToArray()))
        .ToArray();

    /// <summary>Every condition as a UI row (whole-body conditions included).</summary>
    public IReadOnlyList<ConditionView> GetConditionViews() => _injuries.Select(View).ToArray();

    private static ConditionView View(Injury injury) => new(
        injury.Kind, injury.Label, injury.Part.Name, injury.Severity, injury.BleedRate, injury.PainContribution,
        injury.IsTended, injury.TendQuality, injury.IsPermanent);

    // ---------------------------------------------------------------- damage

    /// <summary>
    /// Probability that a random outside hit lands on each part: a part keeps the share of its subtree's
    /// coverage that its outside children do not claim. Inside parts are only reached by inward damage.
    /// </summary>
    public IReadOnlyDictionary<BodyPart, float> GetHitWeights()
    {
        var weights = new Dictionary<BodyPart, float>();
        Accumulate(Root, 1f);
        var total = weights.Values.Sum();
        if (total <= 0f)
            return weights;
        foreach (var key in weights.Keys.ToArray())
            weights[key] /= total;
        return weights;

        void Accumulate(BodyPart part, float area)
        {
            if (IsEffectivelyMissing(part))
                return;
            var claimed = 0f;
            foreach (var child in part.Children)
            {
                if (child.Depth != BodyPartDepth.Outside)
                    continue;
                claimed += child.Coverage;
                Accumulate(child, area * child.Coverage);
            }
            var own = area * Math.Max(0f, 1f - claimed);
            if (own > 0f)
                weights[part] = own;
        }
    }

    public BodyPart ChooseHitPart() => ChooseHitPart(_random);

    public BodyPart ChooseHitPart(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var weights = GetHitWeights();
        var roll = (float)random.NextDouble();
        BodyPart? last = null;
        foreach (var (part, weight) in weights)
        {
            last = part;
            roll -= weight;
            if (roll <= 0f)
                return part;
        }
        return last ?? Root;
    }

    /// <summary>Applies damage of a kind to a coverage-weighted random part, propagating inward where it can.</summary>
    public DamageResult ApplyDamage(float amount, DamageKind kind)
    {
        if (!float.IsFinite(amount) || amount <= 0f || IsDead || kind == DamageKind.Toxin)
            return DamageResult.None;
        if (kind == DamageKind.Radiation)
        {
            AddWholeBody(ConditionKind.RadiationSickness, amount * 0.05f);
            Recalculate();
            return new DamageResult(Root, Array.Empty<BodyPart>(), Array.Empty<BodyPart>(), amount);
        }

        var part = kind == DamageKind.Frostbite ? ChooseExtremity() : ChooseHitPart(_random);
        return ApplyDamageToPart(part, amount, kind, allowInward: true);
    }

    /// <summary>Applies damage directly to a named part (tests, scripted harm, surgery-style effects).</summary>
    public DamageResult ApplyDamageToPart(BodyPart part, float amount, DamageKind kind, bool allowInward = false)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (!ContainsPart(part))
            throw new ArgumentException("The body part is not in this component's body tree.", nameof(part));
        if (!float.IsFinite(amount) || amount <= 0f || IsDead || kind == DamageKind.Toxin || IsEffectivelyMissing(part))
            return DamageResult.None;

        var damaged = new List<BodyPart>();
        var destroyed = new List<BodyPart>();
        var conditionKind = ConditionCatalog.FromDamage(kind);
        var definition = ConditionCatalog.Get(conditionKind);
        var current = part;
        var remaining = amount;
        while (true)
        {
            var inside = current.Children.Where(child => child.Depth == BodyPartDepth.Inside && !child.IsMissing).ToArray();
            if (!allowInward || inside.Length == 0 || definition.InwardChance <= 0f ||
                _random.NextDouble() >= definition.InwardChance)
            {
                ApplyToSinglePart(current, remaining, conditionKind, damaged, destroyed);
                break;
            }
            // The outer layer absorbs part of the blow; the rest reaches a coverage-weighted organ.
            var outerShare = remaining * 0.4f;
            ApplyToSinglePart(current, outerShare, conditionKind, damaged, destroyed);
            remaining -= outerShare;
            var totalCoverage = inside.Sum(child => Math.Max(0.0001f, child.Coverage));
            var pick = (float)_random.NextDouble() * totalCoverage;
            var next = inside[^1];
            foreach (var child in inside)
            {
                pick -= Math.Max(0.0001f, child.Coverage);
                if (pick <= 0f)
                {
                    next = child;
                    break;
                }
            }
            if (IsEffectivelyMissing(next))
                break;
            current = next;
        }

        Recalculate();
        return new DamageResult(part, damaged, destroyed, amount);
    }

    private void ApplyToSinglePart(BodyPart part, float amount, ConditionKind kind, List<BodyPart> damaged, List<BodyPart> destroyed)
    {
        if (amount <= 0f || IsEffectivelyMissing(part))
            return;
        var health = GetPartHealth(part);
        if (amount >= health && !part.IsRoot)
        {
            DestroyPart(part);
            destroyed.Add(part);
            return;
        }
        if (part.IsRoot)
            amount = Math.Min(amount, health - 1f);
        if (amount <= 0f)
            return;

        damaged.Add(part);
        var merge = _injuries.FirstOrDefault(injury => injury.Kind == kind && ReferenceEquals(injury.Part, part) &&
                                                        !injury.IsTended && injury.AgeHours < 1f);
        if (merge is not null)
            merge.SetSeverity(merge.Severity + amount);
        else
            _injuries.Add(Injury.Create(kind, part, amount, ++_sequence));
    }

    /// <summary>Removes a part and everything attached to it; the stump bleeds while fresh.</summary>
    public void DestroyPart(BodyPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (part.IsRoot || IsEffectivelyMissing(part))
            return;
        _injuries.RemoveAll(injury => injury.Part.IsDescendantOf(part));
        foreach (var lost in part.TraversePreOrder())
            lost.IsMissing = true;
        var stump = Injury.Create(ConditionKind.MissingPart, part, 0f, ++_sequence);
        stump.IsFresh = true;
        _injuries.Add(stump);
        var vital = part.TraversePreOrder().FirstOrDefault(lost => lost.IsVital);
        if (vital is not null)
            Kill($"{vital.Name} destroyed");
        Recalculate();
    }

    /// <summary>Adds (or deepens) a whole-body condition such as blood loss or radiation sickness.</summary>
    public void AddWholeBody(ConditionKind kind, float amount)
    {
        if (!float.IsFinite(amount) || amount == 0f || IsDead)
            return;
        var existing = FindWholeBody(kind);
        if (existing is null)
        {
            if (amount < 0f)
                return;
            existing = Injury.Create(kind, Root, 0f, ++_sequence);
            _injuries.Add(existing);
        }
        existing.SetSeverity(existing.Severity + amount);
        if (existing.Severity <= Injury.HealingEpsilon)
            _injuries.Remove(existing);
        Recalculate();
    }

    // ---------------------------------------------------------------- care

    private static bool IsTendable(Injury injury) => injury.Kind switch
    {
        ConditionKind.Infection => injury.TendHoursRemaining <= 0f,
        ConditionKind.MissingPart => injury.IsFresh && !injury.IsTended,
        ConditionKind.Wound or ConditionKind.Scar or ConditionKind.BloodLoss or ConditionKind.RadiationSickness => false,
        _ => !injury.IsTended
    };

    /// <summary>Tends every condition that needs it at a 0..1 quality. Returns how many were tended.</summary>
    public int Tend(float quality)
    {
        if (!float.IsFinite(quality))
            throw new ArgumentOutOfRangeException(nameof(quality));
        quality = Math.Clamp(quality, 0.05f, 1f);
        var count = 0;
        foreach (var injury in _injuries)
        {
            if (!IsTendable(injury))
                continue;
            injury.IsTended = true;
            injury.TendQuality = quality;
            injury.TendHoursRemaining = injury.Kind == ConditionKind.Infection ? InfectionTendHours : float.PositiveInfinity;
            if (injury.Kind == ConditionKind.MissingPart)
                injury.IsFresh = false;
            count++;
        }
        if (count > 0)
            Recalculate();
        return count;
    }

    /// <summary>
    /// Mends part damage directly (healing items, life regeneration): spreads <paramref name="amount"/> HP of
    /// repair over open wounds, worst first. Missing parts, scars, blood loss and illness are untouched.
    /// </summary>
    public float HealInjuries(float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0f || IsDead)
            return 0f;
        var healed = 0f;
        foreach (var injury in _injuries.Where(injury => injury.DamagesPart && !injury.IsPermanent)
                     .OrderByDescending(injury => injury.Severity).ToArray())
        {
            if (amount <= 0f)
                break;
            var step = Math.Min(amount, injury.Severity);
            injury.SetSeverity(injury.Severity - step);
            amount -= step;
            healed += step;
        }
        if (healed > 0f)
        {
            _injuries.RemoveAll(injury => injury.IsHealed && injury.DamagesPart);
            Recalculate();
        }
        return healed;
    }

    // ---------------------------------------------------------------- time

    /// <summary>Advance conditions: bleeding, healing, infection and scarring; then refresh capacities and states.</summary>
    public void Tick(float elapsedHours)
    {
        if (!float.IsFinite(elapsedHours) || elapsedHours < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedHours));
        if (IsDead || elapsedHours == 0f)
        {
            Recalculate();
            return;
        }

        var days = elapsedHours / 24f;
        var restFactor = IsResting ? 1.5f : 1f;
        var bleed = _injuries.Sum(injury => injury.BleedRate);
        var newInfections = new List<Injury>();
        var newScars = new List<Injury>();
        foreach (var injury in _injuries.ToArray())
        {
            var previousAge = injury.AgeHours;
            injury.AgeHours += elapsedHours;
            if (injury.TendHoursRemaining > 0f && !float.IsPositiveInfinity(injury.TendHoursRemaining))
            {
                injury.TendHoursRemaining = Math.Max(0f, injury.TendHoursRemaining - elapsedHours);
                if (injury.TendHoursRemaining <= 0f)
                    injury.IsTended = false;
            }

            switch (injury.Kind)
            {
                case ConditionKind.Wound:
                    injury.Advance(elapsedHours);
                    break;
                case ConditionKind.MissingPart:
                    if (injury.IsFresh && injury.AgeHours >= FreshStumpHours)
                        injury.IsFresh = false;
                    break;
                case ConditionKind.Scar:
                    break;
                case ConditionKind.BloodLoss:
                    break;
                case ConditionKind.RadiationSickness:
                    injury.SetSeverity(injury.Severity - injury.Definition.HealPerDay * 0.04f * days * restFactor);
                    break;
                case ConditionKind.Infection:
                    var infectionRate = injury.IsTended ? -(0.3f + 0.6f * injury.TendQuality) : 0.35f;
                    injury.SetSeverity(injury.Severity + infectionRate * days);
                    if (injury.Severity >= 1f)
                        Kill($"infection in the {injury.Part.Name}");
                    break;
                default:
                    var definition = injury.Definition;
                    var heal = definition.HealPerDay * days * restFactor * (injury.IsTended ? 1f + injury.TendQuality * 2f : 1f);
                    if (_injuries.Any(other => other.Kind == ConditionKind.Infection && ReferenceEquals(other.Part, injury.Part) && !other.IsTended))
                        heal *= 0.25f;
                    injury.SetSeverity(injury.Severity - heal);
                    if (!injury.InfectionRolled && definition.InfectionChance > 0f &&
                        previousAge < InfectionRollAgeHours && injury.AgeHours >= InfectionRollAgeHours)
                    {
                        injury.InfectionRolled = true;
                        var chance = definition.InfectionChance * (0.5f + injury.PeakSeverity / injury.Part.MaxHealth);
                        if (injury.IsTended)
                            chance *= 0.3f * (1f - injury.TendQuality);
                        if (_random.NextDouble() < chance &&
                            !_injuries.Any(other => other.Kind == ConditionKind.Infection && ReferenceEquals(other.Part, injury.Part)))
                        {
                            var infection = Injury.Create(ConditionKind.Infection, injury.Part, 0.05f, ++_sequence);
                            newInfections.Add(infection);
                        }
                    }
                    if (!injury.ScarRolled && injury.PeakSeverity >= injury.Part.MaxHealth * 0.35f &&
                        injury.Severity <= injury.PeakSeverity * 0.2f)
                    {
                        injury.ScarRolled = true;
                        if (_random.NextDouble() < (injury.IsTended ? 0.25f + 0.2f * (1f - injury.TendQuality) : 0.45f))
                        {
                            var scar = Injury.Create(ConditionKind.Scar, injury.Part, injury.PeakSeverity * 0.15f, ++_sequence);
                            newScars.Add(scar);
                            injury.SetSeverity(0f);
                        }
                    }
                    break;
            }
        }
        _injuries.AddRange(newInfections);
        _injuries.AddRange(newScars);
        _injuries.RemoveAll(injury => injury.IsHealed && injury.Kind != ConditionKind.BloodLoss);

        // Blood loss rises with bleeding and recovers slowly on its own.
        var recovery = ConditionCatalog.Get(ConditionKind.BloodLoss).HealPerDay * 0.05f * days * restFactor;
        var bloodChange = bleed * elapsedHours - recovery;
        var blood = FindWholeBody(ConditionKind.BloodLoss);
        if (bloodChange > 0f || blood is not null)
        {
            if (blood is null)
            {
                blood = Injury.Create(ConditionKind.BloodLoss, Root, 0f, ++_sequence);
                _injuries.Add(blood);
            }
            blood.SetSeverity(blood.Severity + bloodChange);
            if (blood.Severity >= 1f)
                Kill("blood loss");
            else if (blood.Severity <= Injury.HealingEpsilon)
                _injuries.Remove(blood);
        }

        Recalculate();
    }

    private void Kill(string cause)
    {
        if (IsDead)
            return;
        IsDead = true;
        DeathCause = cause;
    }

    // ---------------------------------------------------------------- capacities

    /// <summary>Coverage-weighted integrity for parts carrying a selected functional tag.</summary>
    public float GetTaggedCapacity(BodyPartFunction function)
    {
        var functionBits = (int)function;
        if (function == BodyPartFunction.None || (functionBits & (functionBits - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(function), "Select exactly one body-part function tag.");

        var weightedIntegrity = 0f;
        var totalWeight = 0f;
        foreach (var part in Root.TraversePreOrder())
        {
            if ((part.Functions & function) == 0 || part.Coverage <= 0)
                continue;

            weightedIntegrity += PartIntegrity(part) * part.Coverage;
            totalWeight += part.Coverage;
        }

        return totalWeight > 0 ? Math.Clamp(weightedIntegrity / totalWeight, 0, 1) : 0;
    }

    private float AverageIntegrity(BodyPartFunction function, float fallback = 1f)
    {
        var parts = Root.TraversePreOrder().Where(part => (part.Functions & function) != 0).ToArray();
        return parts.Length == 0 ? fallback : parts.Average(PartIntegrity);
    }

    /// <summary>Limb efficiency: each core limb, scaled by its attached segments/digits, averaged over limbs.</summary>
    private float LimbEfficiency(BodyPartFunction core, BodyPartFunction segment, BodyPartFunction digit, float digitWeight)
    {
        var limbs = Root.TraversePreOrder().Where(part => (part.Functions & core) != 0).ToArray();
        if (limbs.Length == 0)
            return 1f;
        var total = 0f;
        foreach (var limb in limbs)
        {
            var efficiency = PartIntegrity(limb);
            if (efficiency > 0f)
            {
                for (var ancestor = limb.Parent; ancestor is not null; ancestor = ancestor.Parent)
                    if ((ancestor.Functions & segment) != 0)
                        efficiency *= 0.75f + 0.25f * PartIntegrity(ancestor);
                var digits = limb.TraversePreOrder().Where(part => (part.Functions & digit) != 0).ToArray();
                if (digits.Length > 0)
                    efficiency *= 1f - digitWeight + digitWeight * digits.Average(PartIntegrity);
            }
            total += efficiency;
        }
        return total / limbs.Length;
    }

    private void Recalculate()
    {
        var pain = _injuries.Sum(injury => injury.PainContribution);
        PainLevel = Math.Clamp(pain, 0, 1);
        BleedRate = _injuries.Sum(injury => injury.BleedRate);

        var painPenalty = Math.Min(_maximumPainPenalty, PainLevel * _painConsciousnessScale);
        var brain = GetTaggedCapacity(BodyPartFunction.ConsciousnessSource);
        var pumping = AverageIntegrity(BodyPartFunction.BloodPumpingSource);
        var breathing = AverageIntegrity(BodyPartFunction.BreathingSource);
        var filtration = AverageIntegrity(BodyPartFunction.BloodFiltrationSource);
        var infection = _injuries.Where(injury => injury.Kind == ConditionKind.Infection).Select(injury => injury.Severity)
            .DefaultIfEmpty(0f).Max();
        var consciousness = brain * (1 - painPenalty)
                            * (0.3f + 0.7f * pumping)
                            * (0.5f + 0.5f * breathing)
                            * (0.85f + 0.15f * filtration)
                            * (1f - 0.6f * BloodLoss)
                            * (1f - 0.35f * RadiationSickness)
                            * (1f - 0.3f * infection);
        Consciousness = Math.Clamp(consciousness, 0f, 1f);
        var awareness = Math.Clamp(Consciousness * 1.25f, 0f, 1f);
        var spine = AverageIntegrity(BodyPartFunction.SpinalSupport);

        _capacities[BodyCapacity.Consciousness] = Consciousness;
        _capacities[BodyCapacity.Moving] = Math.Clamp(
            LimbEfficiency(BodyPartFunction.MovingLimbCore, BodyPartFunction.MovingLimbSegment, BodyPartFunction.MovingLimbDigit, 0.4f)
            * (0.4f + 0.6f * spine) * awareness, 0f, 1f);
        _capacities[BodyCapacity.Manipulation] = Math.Clamp(
            LimbEfficiency(BodyPartFunction.ManipulationLimbCore, BodyPartFunction.ManipulationLimbSegment,
                BodyPartFunction.ManipulationLimbDigit, 0.5f) * awareness, 0f, 1f);
        _capacities[BodyCapacity.Sight] = Math.Clamp(AverageIntegrity(BodyPartFunction.SightSource) * Math.Min(1f, awareness * 1.5f), 0f, 1f);
        _capacities[BodyCapacity.Breathing] = breathing;
        _capacities[BodyCapacity.BloodPumping] = pumping;
        _capacities[BodyCapacity.Digestion] = AverageIntegrity(BodyPartFunction.DigestionSource) * (0.7f + 0.3f * filtration);
        _capacities[BodyCapacity.Talking] = AverageIntegrity(BodyPartFunction.TalkingSource) * awareness;

        if (!IsDead && pumping <= 0f && Root.TraversePreOrder().Any(part => (part.Functions & BodyPartFunction.BloodPumpingSource) != 0))
            Kill("heart failure");
        if (!IsDead && breathing <= 0f && Root.TraversePreOrder().Any(part => (part.Functions & BodyPartFunction.BreathingSource) != 0))
            Kill("suffocation");
        if (!IsDead && BloodLoss >= 1f)
            Kill("blood loss");

        if (IsDead)
        {
            IsDowned = false;
            DownedReason = null;
        }
        else if (InPainShock)
        {
            IsDowned = true;
            DownedReason = "pain shock";
        }
        else if (Consciousness <= DownedConsciousness)
        {
            IsDowned = true;
            DownedReason = "unconscious";
        }
        else if (_capacities[BodyCapacity.Moving] <= DownedMoving)
        {
            IsDowned = true;
            DownedReason = "cannot stand";
        }
        else
        {
            IsDowned = false;
            DownedReason = null;
        }
    }

    private float PartIntegrity(BodyPart part)
    {
        if (IsEffectivelyMissing(part))
            return 0;
        return Math.Clamp(GetPartHealth(part) / part.MaxHealth, 0, 1);
    }

    private static bool IsEffectivelyMissing(BodyPart part)
    {
        for (BodyPart? current = part; current is not null; current = current.Parent)
            if (current.IsMissing)
                return true;
        return false;
    }

    private BodyPart ChooseExtremity()
    {
        var extremities = Root.TraversePreOrder()
            .Where(part => !IsEffectivelyMissing(part) &&
                           (part.Functions & (BodyPartFunction.MovingLimbDigit | BodyPartFunction.ManipulationLimbDigit |
                                              BodyPartFunction.TalkingSource)) != 0)
            .ToArray();
        return extremities.Length == 0 ? ChooseHitPart(_random) : extremities[_random.Next(extremities.Length)];
    }

    private Injury? FindWholeBody(ConditionKind kind) =>
        _injuries.FirstOrDefault(injury => injury.Kind == kind && ReferenceEquals(injury.Part, Root));

    private bool ContainsPart(BodyPart target)
    {
        foreach (var part in Root.TraversePreOrder())
        {
            if (ReferenceEquals(part, target))
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- persistence

    public HealthSnapshot Capture() => new(
        _injuries.Select(injury => new ConditionSnapshot(
            injury.Kind, injury.Part.Name, injury.Severity, injury.AgeHours, injury.IsTended, injury.TendQuality,
            float.IsPositiveInfinity(injury.TendHoursRemaining) ? -1f : injury.TendHoursRemaining,
            injury.IsFresh, injury.PeakSeverity, injury.InfectionRolled, injury.ScarRolled)).ToArray(),
        Root.TraversePreOrder().Where(part => part.IsMissing && (part.Parent is null || !part.Parent.IsMissing))
            .Select(part => part.Name).ToArray(),
        IsDead,
        DeathCause);

    /// <summary>Replaces this body's state with a saved one. Unknown part names are rejected.</summary>
    public void Restore(HealthSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var restored = new List<Injury>();
        foreach (var name in snapshot.MissingParts ?? Array.Empty<string>())
            if (FindPart(name) is null)
                throw new InvalidDataException($"Unknown saved body part '{name}'.");
        foreach (var saved in snapshot.Conditions ?? Array.Empty<ConditionSnapshot>())
        {
            var part = FindPart(saved.Part) ?? throw new InvalidDataException($"Unknown saved body part '{saved.Part}'.");
            if (!Enum.IsDefined(saved.Kind) || !float.IsFinite(saved.Severity) || !float.IsFinite(saved.AgeHours) ||
                saved.Severity < 0f || saved.Severity > part.MaxHealth)
                throw new InvalidDataException("A saved body condition is invalid.");
            var injury = saved.Kind == ConditionKind.Wound
                ? new Injury($"wound-{++_sequence}", part, saved.Severity, -0.2f)
                : Injury.Create(saved.Kind, part, saved.Severity, ++_sequence);
            injury.AgeHours = Math.Max(0f, saved.AgeHours);
            injury.IsTended = saved.Tended;
            injury.TendQuality = Math.Clamp(saved.TendQuality, 0f, 1f);
            injury.TendHoursRemaining = saved.TendHoursRemaining < 0f ? float.PositiveInfinity : saved.TendHoursRemaining;
            injury.IsFresh = saved.Fresh;
            injury.PeakSeverity = Math.Max(saved.Severity, float.IsFinite(saved.PeakSeverity) ? saved.PeakSeverity : 0f);
            injury.InfectionRolled = saved.InfectionRolled;
            injury.ScarRolled = saved.ScarRolled;
            restored.Add(injury);
        }

        foreach (var part in Root.TraversePreOrder())
            part.IsMissing = false;
        foreach (var name in snapshot.MissingParts ?? Array.Empty<string>())
            foreach (var lost in FindPart(name)!.TraversePreOrder())
                lost.IsMissing = true;
        _injuries.Clear();
        _injuries.AddRange(restored);
        IsDead = snapshot.Dead;
        DeathCause = snapshot.DeathCause;
        Recalculate();
    }
}
