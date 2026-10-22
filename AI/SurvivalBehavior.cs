namespace Rimtera.AI;

public enum NeedKind
{
    Food,
    Rest,
    Joy,
    Comfort
}

public enum ActivityKind
{
    Eat,
    Rest,
    Mine,
    Idle
}

/// <summary>Reserves are normalized: 1 is full, 0 is depleted.</summary>
public sealed class SurvivalNeeds
{
    public SurvivalNeeds(float foodReserve, float restReserve, float joyReserve = 0.70f, float comfortReserve = 0.70f)
    {
        FoodReserve = Clamp01(foodReserve);
        RestReserve = Clamp01(restReserve);
        JoyReserve = Clamp01(joyReserve);
        ComfortReserve = Clamp01(comfortReserve);
    }

    public float FoodReserve { get; private set; }
    public float RestReserve { get; private set; }
    public float JoyReserve { get; private set; }
    public float ComfortReserve { get; private set; }

    public void Advance(float elapsedHours, float foodConsumption = 1f, float restConsumption = 1f)
    {
        if (!float.IsFinite(elapsedHours) || elapsedHours < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedHours));
        if (!float.IsFinite(foodConsumption) || foodConsumption < 0 ||
            !float.IsFinite(restConsumption) || restConsumption < 0)
            throw new ArgumentOutOfRangeException(nameof(foodConsumption));

        FoodReserve = Clamp01(FoodReserve - 0.045f * foodConsumption * elapsedHours);
        RestReserve = Clamp01(RestReserve - 0.035f * restConsumption * elapsedHours);
        JoyReserve = Clamp01(JoyReserve - 0.020f * elapsedHours);
        ComfortReserve = Clamp01(ComfortReserve - 0.015f * elapsedHours);
    }

    public void Restore(NeedKind need, float amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        switch (need)
        {
            case NeedKind.Food: FoodReserve = Clamp01(FoodReserve + amount); break;
            case NeedKind.Rest: RestReserve = Clamp01(RestReserve + amount); break;
            case NeedKind.Joy: JoyReserve = Clamp01(JoyReserve + amount); break;
            case NeedKind.Comfort: ComfortReserve = Clamp01(ComfortReserve + amount); break;
            default: throw new ArgumentOutOfRangeException(nameof(need));
        }
    }

    /// <summary>Lowers one reserve, e.g. comfort lost to cold or heat exposure.</summary>
    public void Drain(NeedKind need, float amount)
    {
        if (!float.IsFinite(amount) || amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        switch (need)
        {
            case NeedKind.Food: FoodReserve = Clamp01(FoodReserve - amount); break;
            case NeedKind.Rest: RestReserve = Clamp01(RestReserve - amount); break;
            case NeedKind.Joy: JoyReserve = Clamp01(JoyReserve - amount); break;
            case NeedKind.Comfort: ComfortReserve = Clamp01(ComfortReserve - amount); break;
            default: throw new ArgumentOutOfRangeException(nameof(need));
        }
    }

    public void SetReserves(float foodReserve, float restReserve, float joyReserve = 0.70f, float comfortReserve = 0.70f)
    {
        if (!float.IsFinite(foodReserve) || !float.IsFinite(restReserve) ||
            !float.IsFinite(joyReserve) || !float.IsFinite(comfortReserve))
            throw new ArgumentOutOfRangeException(nameof(foodReserve), "Need reserves must be finite.");
        FoodReserve = Clamp01(foodReserve);
        RestReserve = Clamp01(restReserve);
        JoyReserve = Clamp01(joyReserve);
        ComfortReserve = Clamp01(comfortReserve);
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0, 1);
}

public readonly record struct AgentContext(
    SurvivalNeeds Needs,
    int FoodInInventory,
    bool IsSafeToRest,
    bool HasWorkTarget);

public readonly record struct ActivityChoice(ActivityKind Activity, float Priority, string Reason);

public interface IBehaviorNode
{
    bool TryEvaluate(in AgentContext context, out ActivityChoice choice);
}

/// <summary>Creates an activity candidate when a need crosses its configured reserve threshold.</summary>
public sealed class NeedActivityNode : IBehaviorNode
{
    private readonly NeedKind _need;
    private readonly float _triggerReserve;
    private readonly float _basePriority;
    private readonly float _urgencyGain;

    public NeedActivityNode(NeedKind need, float triggerReserve, float basePriority, float urgencyGain)
    {
        _need = need;
        _triggerReserve = Math.Clamp(triggerReserve, 0.01f, 1f);
        _basePriority = basePriority;
        _urgencyGain = urgencyGain;
    }

    public bool TryEvaluate(in AgentContext context, out ActivityChoice choice)
    {
        var reserve = _need == NeedKind.Food
            ? context.Needs.FoodReserve
            : context.Needs.RestReserve;

        if (reserve >= _triggerReserve ||
            (_need == NeedKind.Food && context.FoodInInventory <= 0) ||
            (_need == NeedKind.Rest && !context.IsSafeToRest))
        {
            choice = default;
            return false;
        }

        var urgency = (_triggerReserve - reserve) / _triggerReserve;
        var activity = _need == NeedKind.Food ? ActivityKind.Eat : ActivityKind.Rest;
        choice = new ActivityChoice(
            activity,
            _basePriority + urgency * _urgencyGain,
            $"{_need} reserve below configured threshold");
        return true;
    }
}

public sealed class WorkActivityNode : IBehaviorNode
{
    private readonly float _priority;

    public WorkActivityNode(float priority = 0.2f) => _priority = priority;

    public bool TryEvaluate(in AgentContext context, out ActivityChoice choice)
    {
        if (!context.HasWorkTarget)
        {
            choice = default;
            return false;
        }

        choice = new ActivityChoice(ActivityKind.Mine, _priority, "work target available");
        return true;
    }
}

/// <summary>Evaluates every child and picks the highest valid priority, preserving child order on ties.</summary>
public sealed class PrioritySelectorNode
{
    private readonly IReadOnlyList<IBehaviorNode> _children;

    public PrioritySelectorNode(params IBehaviorNode[] children) => _children = children;

    public ActivityChoice Select(in AgentContext context)
    {
        var found = false;
        var best = new ActivityChoice(ActivityKind.Idle, 0, "no eligible activity");
        foreach (var child in _children)
        {
            if (child.TryEvaluate(context, out var candidate) &&
                candidate.Priority > 0 &&
                (!found || candidate.Priority > best.Priority))
            {
                best = candidate;
                found = true;
            }
        }

        return best;
    }
}

public sealed class AgentBrain
{
    private readonly PrioritySelectorNode _root;

    public AgentBrain(PrioritySelectorNode root) => _root = root;

    public ActivityChoice Current { get; private set; } = new(ActivityKind.Idle, 0, "not evaluated");

    /// <summary>Re-evaluates the tree each update so changing needs can change the selected activity.</summary>
    public ActivityChoice Think(in AgentContext context) => Current = _root.Select(context);
}
