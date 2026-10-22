namespace Rimtera.Entities;

/// <summary>Whether a body part sits on the surface (hit directly) or inside another part (reached by inward damage).</summary>
public enum BodyPartDepth : byte
{
    Outside,
    Inside
}

/// <summary>What caused incoming damage; decides the condition kind, bleeding and inward propagation.</summary>
public enum DamageKind : byte
{
    Blunt,
    Cut,
    Bite,
    Crush,
    Shot,
    Burn,
    Frostbite,
    Radiation,
    /// <summary>Poison-style harm that drains the hit-point pool without wounding a body part.</summary>
    Toxin
}

/// <summary>Every condition a body can carry. <see cref="Wound"/> is the legacy generic injury.</summary>
public enum ConditionKind : byte
{
    Wound,
    Cut,
    Bruise,
    Burn,
    Frostbite,
    Bite,
    Shot,
    Crush,
    RadiationSickness,
    Infection,
    Scar,
    BloodLoss,
    MissingPart
}

/// <summary>Derived body capacities that gameplay reads.</summary>
public enum BodyCapacity : byte
{
    Consciousness,
    Moving,
    Manipulation,
    Sight,
    Breathing,
    BloodPumping,
    Digestion,
    Talking
}

/// <summary>
/// Static tuning for one condition kind. All numbers are original Rimtera tuning.
/// Pain and bleeding scale with severity (part HP lost) unless noted.
/// </summary>
public sealed record ConditionDefinition(
    ConditionKind Kind,
    string Label,
    bool DamagesPart,
    float PainPerSeverity,
    float BleedPerSeverityHour,
    float HealPerDay,
    float InfectionChance,
    float InwardChance,
    string[] StageLabels)
{
    public bool IsOpenWound => BleedPerSeverityHour > 0f;
    public bool IsWholeBody => Kind is ConditionKind.BloodLoss or ConditionKind.RadiationSickness;
}

public static class ConditionCatalog
{
    private static readonly Dictionary<ConditionKind, ConditionDefinition> _table = new[]
    {
        new ConditionDefinition(ConditionKind.Wound, "wound", true, 0.015f, 0f, 0f, 0f, 0f, ["graze", "wound", "deep wound"]),
        new ConditionDefinition(ConditionKind.Cut, "cut", true, 0.0125f, 0.0045f, 6f, 0.10f, 0.20f, ["nick", "cut", "gash"]),
        new ConditionDefinition(ConditionKind.Bruise, "bruise", true, 0.0125f, 0f, 9f, 0f, 0f, ["welt", "bruise", "heavy bruise"]),
        new ConditionDefinition(ConditionKind.Burn, "burn", true, 0.019f, 0f, 4f, 0.12f, 0f, ["scorch", "burn", "charring"]),
        new ConditionDefinition(ConditionKind.Frostbite, "frostbite", true, 0.015f, 0f, 4f, 0.05f, 0f, ["frostnip", "frostbite", "deep frostbite"]),
        new ConditionDefinition(ConditionKind.Bite, "bite", true, 0.0125f, 0.0055f, 5f, 0.25f, 0.25f, ["nip", "bite", "mauling"]),
        new ConditionDefinition(ConditionKind.Shot, "gunshot", true, 0.0125f, 0.0065f, 5f, 0.15f, 0.40f, ["graze", "bolt wound", "through-wound"]),
        new ConditionDefinition(ConditionKind.Crush, "crush", true, 0.0125f, 0.0025f, 5f, 0.08f, 0.45f, ["dent", "crush", "shattering"]),
        new ConditionDefinition(ConditionKind.RadiationSickness, "radiation sickness", false, 0.15f, 0f, 6f, 0f, 0f, ["mild radiation sickness", "radiation sickness", "severe radiation sickness"]),
        new ConditionDefinition(ConditionKind.Infection, "infection", false, 0.25f, 0f, 0f, 0f, 0f, ["minor infection", "infection", "spreading infection"]),
        new ConditionDefinition(ConditionKind.Scar, "scar", true, 0.005f, 0f, 0f, 0f, 0f, ["scar", "scar", "heavy scarring"]),
        new ConditionDefinition(ConditionKind.BloodLoss, "blood loss", false, 0f, 0f, 7.2f, 0f, 0f, ["minor blood loss", "moderate blood loss", "severe blood loss"]),
        new ConditionDefinition(ConditionKind.MissingPart, "missing", false, 0.03f, 0f, 0f, 0f, 0f, ["missing", "missing", "missing"])
    }.ToDictionary(definition => definition.Kind);

    public static ConditionDefinition Get(ConditionKind kind) =>
        _table.TryGetValue(kind, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(kind));

    public static ConditionKind FromDamage(DamageKind kind) => kind switch
    {
        DamageKind.Blunt => ConditionKind.Bruise,
        DamageKind.Cut => ConditionKind.Cut,
        DamageKind.Bite => ConditionKind.Bite,
        DamageKind.Crush => ConditionKind.Crush,
        DamageKind.Shot => ConditionKind.Shot,
        DamageKind.Burn => ConditionKind.Burn,
        DamageKind.Frostbite => ConditionKind.Frostbite,
        DamageKind.Radiation => ConditionKind.RadiationSickness,
        _ => ConditionKind.Bruise
    };

    /// <summary>Maps the exposure cause strings used by the environment to a damage kind.</summary>
    public static DamageKind FromExposureCause(string? cause) => cause?.ToLowerInvariant() switch
    {
        "frostbite" => DamageKind.Frostbite,
        "heatstroke" => DamageKind.Burn,
        "burn" => DamageKind.Burn,
        "radiation-sickness" or "radiation" => DamageKind.Radiation,
        _ => DamageKind.Blunt
    };
}

/// <summary>Outcome of one damage application to a body.</summary>
public sealed record DamageResult(
    BodyPart? HitPart,
    IReadOnlyList<BodyPart> DamagedParts,
    IReadOnlyList<BodyPart> DestroyedParts,
    float BodyDamage)
{
    public static DamageResult None { get; } = new(null, Array.Empty<BodyPart>(), Array.Empty<BodyPart>(), 0f);
}

/// <summary>Read-only view of one condition for the UI.</summary>
public readonly record struct ConditionView(
    ConditionKind Kind,
    string Label,
    string PartName,
    float Severity,
    float BleedRate,
    float Pain,
    bool Tended,
    float TendQuality,
    bool Permanent);

/// <summary>Read-only view of one body part for the UI.</summary>
public readonly record struct BodyPartStatus(
    string Name,
    string? ParentName,
    BodyPartDepth Depth,
    float Health,
    float MaxHealth,
    bool Missing,
    BodyPartFunction Functions,
    IReadOnlyList<ConditionView> Conditions);

/// <summary>Persisted form of one condition; parts are identified by their unique name.</summary>
public readonly record struct ConditionSnapshot(
    ConditionKind Kind,
    string Part,
    float Severity,
    float AgeHours,
    bool Tended,
    float TendQuality,
    float TendHoursRemaining,
    bool Fresh,
    float PeakSeverity,
    bool InfectionRolled,
    bool ScarRolled);

/// <summary>Body-wide persisted health state: conditions plus the parts that are gone.</summary>
public sealed record HealthSnapshot(
    ConditionSnapshot[]? Conditions,
    string[]? MissingParts,
    bool Dead = false,
    string? DeathCause = null);
