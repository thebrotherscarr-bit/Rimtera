using Rimtera.AI;
using Rimtera.Entities;

namespace Rimtera.Characters;

/// <summary>Relation labels. Append only: explicit kinds are persisted in saves.</summary>
public enum RelationKind : byte
{
    None,
    Crewmate,
    Friend,
    Rival,
    Lover,
    Partner,
    Sibling,
    OldFriend
}

/// <summary>Social interaction types. Append only.</summary>
public enum InteractionKind : byte
{
    Chitchat,
    DeepTalk,
    KindWords,
    Slight,
    Insult,
    RomanceAttempt
}

/// <summary>A pawn that takes part in colony social life: the player character or a colonist NPC.</summary>
public interface IColonyPawn
{
    /// <summary>Stable colony-local key: 0 for the player, 1.. for colonists in roster order.</summary>
    int SocialKey { get; }
    string Name { get; }
    CharacterProfile Profile { get; }
    ColonistStats ColonistStats { get; }
    SurvivalNeeds Needs { get; }
    HealthComponent Health { get; }
    ColonistMind Mind { get; }
    PawnMotionState Motion { get; }
    bool IsPlayerCharacter { get; }
    bool IsAlive { get; }
    bool IsDowned { get; }
    float TakeDamage(float amount, DamageKind kind);
}

/// <summary>An explicit relation this pawn holds toward another (stored on both sides).</summary>
public readonly record struct DirectRelation(int OtherKey, RelationKind Kind, float StartHour);

/// <summary>Persisted relation.</summary>
public readonly record struct RelationSnapshot(int OtherKey, RelationKind Kind, float StartHour);

/// <summary>UI row: how one pawn feels about another.</summary>
public readonly record struct OpinionView(
    int OtherKey,
    string OtherName,
    float Opinion,
    RelationKind Relation,
    string RelationLabel,
    float Compatibility,
    bool OtherAlive);

/// <summary>Outcome of one interaction, for messages and tests.</summary>
public sealed record InteractionResult(
    InteractionKind Kind,
    IColonyPawn Initiator,
    IColonyPawn Recipient,
    bool Success,
    bool SocialFight,
    string Message);

/// <summary>Explicit relations (crewmate, sibling, old friend, lover, partner) keyed by the other pawn.</summary>
public sealed class RelationsTracker
{
    private readonly Dictionary<int, DirectRelation> _relations = new();

    public IReadOnlyCollection<DirectRelation> All => _relations.Values;

    public RelationKind Get(int otherKey) => _relations.TryGetValue(otherKey, out var relation) ? relation.Kind : RelationKind.None;

    public DirectRelation? Find(int otherKey) => _relations.TryGetValue(otherKey, out var relation) ? relation : null;

    public void Set(int otherKey, RelationKind kind, float startHour)
    {
        if (kind == RelationKind.None)
            _relations.Remove(otherKey);
        else
            _relations[otherKey] = new DirectRelation(otherKey, kind, startHour);
    }

    public bool HasRomanticPartnerOtherThan(int otherKey) =>
        _relations.Values.Any(relation => relation.OtherKey != otherKey &&
                                          relation.Kind is RelationKind.Lover or RelationKind.Partner);

    public RelationSnapshot[] Capture() =>
        _relations.Values.OrderBy(relation => relation.OtherKey)
            .Select(relation => new RelationSnapshot(relation.OtherKey, relation.Kind, relation.StartHour)).ToArray();

    public void Restore(IEnumerable<RelationSnapshot>? relations)
    {
        _relations.Clear();
        foreach (var saved in relations ?? Array.Empty<RelationSnapshot>())
        {
            if (!Enum.IsDefined(saved.Kind) || !float.IsFinite(saved.StartHour) ||
                saved.Kind is RelationKind.Friend or RelationKind.Rival)
                throw new InvalidDataException("A saved relation is invalid.");
            if (saved.Kind != RelationKind.None)
                _relations[saved.OtherKey] = new DirectRelation(saved.OtherKey, saved.Kind, saved.StartHour);
        }
    }
}

/// <summary>Compatibility, opinion and derived relation rules. Original Rimtera tuning.</summary>
public static class SocialRules
{
    public const float FriendOpinion = 25f;
    public const float RivalOpinion = -20f;
    public const float CompatibilityOpinionScale = 12f;
    public const float InteractionRange = 7f;

    /// <summary>
    /// Stable, symmetric compatibility in roughly [-1.5, 1.5]: a pair hash of the two profile ids (identical
    /// across sessions) plus trait interactions and an age-gap penalty.
    /// </summary>
    public static float Compatibility(CharacterProfile a, CharacterProfile b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var ha = StableHash(a.Id);
        var hb = StableHash(b.Id);
        var (low, high) = ha <= hb ? (ha, hb) : (hb, ha);
        var mixed = Mix(low * 0x9E3779B1u ^ Mix(high + 0x7F4A7C15u));
        var u1 = (mixed & 0xFFFF) / 65535f;
        var u2 = ((mixed >> 16) & 0xFFFF) / 65535f;
        var value = u1 + u2 - 1f;

        var ta = a.Traits ?? Array.Empty<ColonistTraitId>();
        var tb = b.Traits ?? Array.Empty<ColonistTraitId>();
        bool Either(ColonistTraitId id) => ta.Contains(id) || tb.Contains(id);
        bool Both(ColonistTraitId id) => ta.Contains(id) && tb.Contains(id);
        bool Split(ColonistTraitId x, ColonistTraitId y) => (ta.Contains(x) && tb.Contains(y)) || (ta.Contains(y) && tb.Contains(x));

        if (Both(ColonistTraitId.Kind)) value += 0.30f;
        else if (Either(ColonistTraitId.Kind)) value += 0.15f;
        value -= 0.25f * (ta.Count(id => id == ColonistTraitId.Abrasive) + tb.Count(id => id == ColonistTraitId.Abrasive));
        if (Either(ColonistTraitId.Psychopath)) value -= 0.15f;
        if (Both(ColonistTraitId.Optimist)) value += 0.10f;
        if (Split(ColonistTraitId.Optimist, ColonistTraitId.Pessimist)) value -= 0.15f;
        if (Split(ColonistTraitId.Neat, ColonistTraitId.Sloppy)) value -= 0.20f;
        if (Both(ColonistTraitId.NightOwl)) value += 0.10f;
        value -= Math.Min(0.3f, Math.Abs(a.Age - b.Age) / 100f);
        return Math.Clamp(value, -1.5f, 1.5f);
    }

    public static float RelationOpinionBonus(RelationKind kind) => kind switch
    {
        RelationKind.Crewmate => 5f,
        RelationKind.OldFriend => 15f,
        RelationKind.Sibling => 12f,
        RelationKind.Lover => 25f,
        RelationKind.Partner => 30f,
        _ => 0f
    };

    /// <summary>A face scar or a lost eye/jaw makes others like a pawn a little less.</summary>
    public static bool IsDisfigured(HealthComponent health)
    {
        ArgumentNullException.ThrowIfNull(health);
        return health.Injuries.Any(injury =>
            injury.Kind is ConditionKind.Scar or ConditionKind.MissingPart &&
            (injury.Part.Functions & (BodyPartFunction.SightSource | BodyPartFunction.TalkingSource)) != 0 ||
            injury.Kind == ConditionKind.Scar && string.Equals(injury.Part.Name, "head", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Opinion of <paramref name="self"/> toward <paramref name="other"/>, clamped to [-100, 100].</summary>
    public static float Opinion(IColonyPawn self, IColonyPawn other)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(self, other))
            return 0f;
        var opinion = Compatibility(self.Profile, other.Profile) * CompatibilityOpinionScale
                      + self.Mind.Thoughts.OpinionOffsetToward(other.SocialKey)
                      + RelationOpinionBonus(self.Mind.Relations.Get(other.SocialKey));
        if (IsDisfigured(other.Health))
            opinion -= 6f;
        return Math.Clamp(opinion, -100f, 100f);
    }

    /// <summary>Explicit relations win; otherwise Friend/Rival come from opinion thresholds.</summary>
    public static RelationKind DerivedRelation(IColonyPawn self, IColonyPawn other)
    {
        var explicitKind = self.Mind.Relations.Get(other.SocialKey);
        if (explicitKind is RelationKind.Partner or RelationKind.Lover or RelationKind.Sibling or RelationKind.OldFriend)
            return explicitKind;
        var opinion = Opinion(self, other);
        if (opinion >= FriendOpinion)
            return RelationKind.Friend;
        if (opinion <= RivalOpinion)
            return RelationKind.Rival;
        return explicitKind;
    }

    public static string Label(RelationKind kind) => kind switch
    {
        RelationKind.Crewmate => "Crewmate",
        RelationKind.Friend => "Friend",
        RelationKind.Rival => "Rival",
        RelationKind.Lover => "Lover",
        RelationKind.Partner => "Partner",
        RelationKind.Sibling => "Sibling",
        RelationKind.OldFriend => "Old friend",
        _ => "Acquaintance"
    };

    public static IReadOnlyList<OpinionView> GetOpinions(IColonyPawn self, IEnumerable<IColonyPawn> colony)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(colony);
        return colony.Where(other => !ReferenceEquals(other, self))
            .Select(other =>
            {
                var relation = DerivedRelation(self, other);
                return new OpinionView(other.SocialKey, other.Name, Opinion(self, other), relation, Label(relation),
                    Compatibility(self.Profile, other.Profile), other.IsAlive);
            })
            .OrderByDescending(view => view.Opinion)
            .ToArray();
    }

    public static bool CanSocialize(IColonyPawn pawn) =>
        pawn.IsAlive && !pawn.IsDowned && !pawn.Mind.Breaker.IsBreaking &&
        pawn.Health.GetCapacity(BodyCapacity.Talking) > 0.2f;

    internal static uint StableHash(Guid id)
    {
        var hash = 2166136261u;
        foreach (var value in id.ToByteArray())
        {
            hash ^= value;
            hash *= 16777619u;
        }
        return hash;
    }

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }
}

/// <summary>
/// Runs colony social life: starting relations, interactions between nearby pawns on an interval,
/// interaction memories, romance, social fights, lover-to-partner promotion and grief on death.
/// </summary>
public sealed class ColonySocialSystem
{
    public const float PartnerAfterHours = 96f;
    private readonly Random _random;

    public ColonySocialSystem(int seed) => _random = new Random(seed);

    /// <summary>Everyone who landed together starts as crewmates; a few pairs share a seeded history.</summary>
    public static void SeedStartingRelations(IReadOnlyList<IColonyPawn> crew, int seed, float nowHours = 0f)
    {
        ArgumentNullException.ThrowIfNull(crew);
        var random = new Random(unchecked(seed ^ 0x3C6EF372));
        for (var i = 0; i < crew.Count; i++)
        for (var j = i + 1; j < crew.Count; j++)
        {
            var roll = random.NextDouble();
            var kind = roll < 0.10 ? RelationKind.Sibling : roll < 0.25 ? RelationKind.OldFriend : RelationKind.Crewmate;
            SetBoth(crew[i], crew[j], kind, nowHours);
        }
    }

    public static void SetBoth(IColonyPawn a, IColonyPawn b, RelationKind kind, float nowHours)
    {
        a.Mind.Relations.Set(b.SocialKey, kind, nowHours);
        b.Mind.Relations.Set(a.SocialKey, kind, nowHours);
    }

    /// <summary>One social pass on the story cadence. Returns the interactions that happened.</summary>
    public IReadOnlyList<InteractionResult> Update(IReadOnlyList<IColonyPawn> pawns, float nowHours)
    {
        ArgumentNullException.ThrowIfNull(pawns);
        PromoteLovers(pawns, nowHours);
        var results = new List<InteractionResult>();
        foreach (var initiator in pawns)
        {
            if (!SocialRules.CanSocialize(initiator) || nowHours < initiator.Mind.NextInteractionHour)
                continue;
            initiator.Mind.NextInteractionHour = nowHours + 0.6f + (float)_random.NextDouble() * 0.8f;
            var center = Center(initiator);
            var candidates = pawns.Where(other => !ReferenceEquals(other, initiator) && SocialRules.CanSocialize(other) &&
                                                  Vector2Distance(center, Center(other)) <= SocialRules.InteractionRange)
                .ToArray();
            if (candidates.Length == 0)
                continue;
            var recipient = candidates[_random.Next(candidates.Length)];
            results.Add(Interact(initiator, recipient, PickInteraction(initiator, recipient), nowHours));
        }
        return results;
    }

    public InteractionKind PickInteraction(IColonyPawn initiator, IColonyPawn recipient)
    {
        var opinion = SocialRules.Opinion(initiator, recipient);
        var traits = initiator.Profile.Traits ?? Array.Empty<ColonistTraitId>();
        var kind = traits.Contains(ColonistTraitId.Kind);
        var abrasive = traits.Contains(ColonistTraitId.Abrasive);
        var psychopath = traits.Contains(ColonistTraitId.Psychopath);
        var weights = new (InteractionKind Kind, float Weight)[]
        {
            (InteractionKind.Chitchat, 1f),
            (InteractionKind.DeepTalk, 0.2f * (1f + Math.Max(0f, opinion) / 40f)),
            (InteractionKind.KindWords, kind ? 0.45f : 0f),
            (InteractionKind.Slight, 0.12f * (opinion < 0f ? 1.8f : 1f) * (abrasive ? 4f : 1f) * (kind ? 0.1f : 1f)),
            (InteractionKind.Insult, kind ? 0f : 0.025f * (opinion < -10f ? 3f : 1f) * (abrasive ? 6f : 1f) * (psychopath ? 2f : 1f)),
            (InteractionKind.RomanceAttempt, CanRomance(initiator, recipient) && opinion >= 15f ? 0.05f * opinion / 40f : 0f)
        };
        var total = weights.Sum(entry => entry.Weight);
        var roll = (float)_random.NextDouble() * total;
        foreach (var (candidate, weight) in weights)
        {
            roll -= weight;
            if (weight > 0f && roll <= 0f)
                return candidate;
        }
        return InteractionKind.Chitchat;
    }

    public static bool CanRomance(IColonyPawn a, IColonyPawn b) =>
        !ReferenceEquals(a, b) && a.Profile.Age >= 18 && b.Profile.Age >= 18 &&
        a.Mind.Relations.Get(b.SocialKey) is not (RelationKind.Sibling or RelationKind.Lover or RelationKind.Partner) &&
        !a.Mind.Relations.HasRomanticPartnerOtherThan(b.SocialKey) &&
        !b.Mind.Relations.HasRomanticPartnerOtherThan(a.SocialKey);

    public static float RomanceSuccessChance(IColonyPawn initiator, IColonyPawn recipient)
    {
        var opinion = SocialRules.Opinion(recipient, initiator);
        var chance = 0.15f + opinion / 100f * 0.6f + SocialRules.Compatibility(initiator.Profile, recipient.Profile) * 0.15f;
        if (Math.Abs(initiator.Profile.Age - recipient.Profile.Age) > 15)
            chance *= 0.4f;
        return Math.Clamp(chance, 0.02f, 0.9f);
    }

    /// <summary>Performs an interaction now (no range or timer checks) and applies its memories and effects.</summary>
    public InteractionResult Interact(IColonyPawn initiator, IColonyPawn recipient, InteractionKind kind, float nowHours)
    {
        ArgumentNullException.ThrowIfNull(initiator);
        ArgumentNullException.ThrowIfNull(recipient);
        var a = initiator.Name;
        var b = recipient.Name;
        switch (kind)
        {
            case InteractionKind.Chitchat:
                Remember(initiator, recipient, ThoughtId.Chitchat);
                Remember(recipient, initiator, ThoughtId.Chitchat);
                return new InteractionResult(kind, initiator, recipient, true, false, $"{a} chatted with {b}.");
            case InteractionKind.DeepTalk:
                Remember(initiator, recipient, ThoughtId.DeepTalk);
                Remember(recipient, initiator, ThoughtId.DeepTalk);
                return new InteractionResult(kind, initiator, recipient, true, false, $"{a} and {b} had a deep talk.");
            case InteractionKind.KindWords:
                Remember(recipient, initiator, ThoughtId.KindWords);
                return new InteractionResult(kind, initiator, recipient, true, false, $"{a} offered {b} some kind words.");
            case InteractionKind.Slight:
                Remember(recipient, initiator, ThoughtId.Slighted);
                return new InteractionResult(kind, initiator, recipient, true, false, $"{a} slighted {b}.");
            case InteractionKind.Insult:
            {
                Remember(recipient, initiator, ThoughtId.Insulted);
                var fight = TrySocialFight(initiator, recipient);
                return new InteractionResult(kind, initiator, recipient, true, fight,
                    fight ? $"{a} insulted {b}, and it came to blows!" : $"{a} insulted {b}.");
            }
            case InteractionKind.RomanceAttempt:
            {
                if (!CanRomance(initiator, recipient))
                    return Interact(initiator, recipient, InteractionKind.Chitchat, nowHours);
                var success = _random.NextDouble() < RomanceSuccessChance(initiator, recipient);
                if (success)
                {
                    SetBoth(initiator, recipient, RelationKind.Lover, nowHours);
                    Remember(initiator, recipient, ThoughtId.NewLovers);
                    Remember(recipient, initiator, ThoughtId.NewLovers);
                    return new InteractionResult(kind, initiator, recipient, true, false, $"{a} and {b} are now lovers.");
                }
                Remember(initiator, recipient, ThoughtId.Rebuffed);
                return new InteractionResult(kind, initiator, recipient, false, false, $"{b} turned down {a}'s advances.");
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>Chance that an insult at low opinion turns into a fight.</summary>
    public static float SocialFightChance(IColonyPawn initiator, IColonyPawn recipient)
    {
        if (!recipient.IsAlive || recipient.IsDowned || !initiator.IsAlive || initiator.IsDowned)
            return 0f;
        var opinion = SocialRules.Opinion(recipient, initiator);
        if (opinion > -15f)
            return 0f;
        var chance = 0.25f + (-15f - opinion) / 80f;
        if (recipient.Profile.Traits?.Contains(ColonistTraitId.Kind) == true)
            chance *= 0.3f;
        if (recipient.Profile.Traits?.Contains(ColonistTraitId.Brawler) == true)
            chance *= 1.4f;
        return Math.Clamp(chance, 0f, 0.85f);
    }

    private bool TrySocialFight(IColonyPawn initiator, IColonyPawn recipient)
    {
        if (_random.NextDouble() >= SocialFightChance(initiator, recipient))
            return false;
        // A short scuffle: each side lands a couple of blows on the other.
        for (var blow = 0; blow < 2; blow++)
        {
            initiator.TakeDamage(3f + (float)_random.NextDouble() * 4f, DamageKind.Blunt);
            recipient.TakeDamage(3f + (float)_random.NextDouble() * 4f, DamageKind.Blunt);
        }
        Remember(initiator, recipient, ThoughtId.SocialFight);
        Remember(recipient, initiator, ThoughtId.SocialFight);
        return true;
    }

    private static void Remember(IColonyPawn self, IColonyPawn about, ThoughtId id, float scale = 1f) =>
        self.Mind.Thoughts.AddMemory(id, about.SocialKey, about.Name, scale);

    /// <summary>Lovers who stay together long enough become partners.</summary>
    public static void PromoteLovers(IReadOnlyList<IColonyPawn> pawns, float nowHours)
    {
        foreach (var pawn in pawns)
        foreach (var relation in pawn.Mind.Relations.All.ToArray())
        {
            if (relation.Kind != RelationKind.Lover || nowHours - relation.StartHour < PartnerAfterHours)
                continue;
            var other = pawns.FirstOrDefault(candidate => candidate.SocialKey == relation.OtherKey);
            if (other is null || !other.IsAlive)
                continue;
            SetBoth(pawn, other, RelationKind.Partner, nowHours);
        }
    }

    /// <summary>Adds relation-scaled grief (or relief) to every surviving pawn.</summary>
    public static void NotifyDeath(IColonyPawn dead, IEnumerable<IColonyPawn> survivors)
    {
        ArgumentNullException.ThrowIfNull(dead);
        foreach (var other in survivors)
        {
            if (ReferenceEquals(other, dead) || !other.IsAlive)
                continue;
            var opinion = SocialRules.Opinion(other, dead);
            var relation = other.Mind.Relations.Get(dead.SocialKey);
            var (thought, scale) = relation switch
            {
                RelationKind.Lover or RelationKind.Partner => (ThoughtId.LoverDied, 1f),
                RelationKind.Sibling => (ThoughtId.SiblingDied, 1f),
                _ when opinion >= SocialRules.FriendOpinion => (ThoughtId.FriendDied, Math.Clamp(opinion / 50f, 0.6f, 1.6f)),
                _ when opinion <= SocialRules.RivalOpinion => (ThoughtId.RivalDied, 1f),
                _ => (ThoughtId.CrewmateDied, 1f)
            };
            other.Mind.Thoughts.AddMemory(thought, dead.SocialKey, dead.Name, scale);
        }
    }

    private static System.Numerics.Vector2 Center(IColonyPawn pawn) => pawn.Motion.Position + pawn.Motion.Size / 2f;

    private static float Vector2Distance(System.Numerics.Vector2 a, System.Numerics.Vector2 b) => System.Numerics.Vector2.Distance(a, b);
}
