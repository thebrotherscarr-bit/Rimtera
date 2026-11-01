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

    /// <summary>The resident who bargains for you: housed, on Trade work, with the best Social (0.3.10: residents, not crew).</summary>
    public TownNpc? Negotiator => _townNpcs
        .Where(npc => npc.IsAlive && !npc.IsBound && npc.HomeCell is not null && npc.Work.Priority(WorkType.Trade) != WorkCatalog.Off)
        .MaxBy(npc => npc.Stats.Level(ColonistSkill.Social));

    /// <summary>The share off shop prices (and onto what shops pay) the negotiator wins.</summary>
    public float TradeDiscount => Negotiator is { } negotiator
        ? Math.Min(MaxTradeDiscount, negotiator.Stats.Level(ColonistSkill.Social) * TradeDiscountPerLevel)
        : 0f;
}
