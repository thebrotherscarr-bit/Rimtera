using Rimtera.Characters;
using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// Trade (0.2.9, docs/DESIGN_COLONY_ESCAPE.md §3.3 "Trade"): the crew member on Trade work with the best Social skill
/// bargains for the colony. Shops sell cheaper and pay more, 0.75% a Social level (15% at most, at Social 20), on top of
/// the shopkeeper's happiness. Trade has no job to walk to: the negotiator only has to be alive and up.
/// </summary>
public sealed partial class GameSimulation
{
    public const float TradeDiscountPerLevel = 0.0075f, MaxTradeDiscount = 0.15f;

    /// <summary>The crew member who bargains: on Trade work, alive and not downed, with the best Social.</summary>
    public ColonistAgent? Negotiator => _colonists
        .Where(agent => agent.IsAlive && !agent.IsDowned && agent.Work.Priority(WorkType.Trade) != WorkCatalog.Off)
        .MaxBy(agent => agent.ColonistStats.Level(ColonistSkill.Social));

    /// <summary>The share off shop prices (and onto what shops pay) the negotiator wins.</summary>
    public float TradeDiscount => Negotiator is { } negotiator
        ? Math.Min(MaxTradeDiscount, negotiator.ColonistStats.Level(ColonistSkill.Social) * TradeDiscountPerLevel)
        : 0f;
}
