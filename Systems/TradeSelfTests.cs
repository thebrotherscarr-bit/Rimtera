using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Trade (0.2.9): the best Social on Trade work bargains at shops (0.3.10: a housed resident, such as a settler or the Trade Consul).</summary>
public static class TradeSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var game = new GameSimulation(seed: 9991, width: 120, height: 56) { FamilySpawnsEnabled = false };
        game.AddStartingCrewForChecks();
        foreach (var agent in game.Settlers)
        {
            agent.Work.Set(WorkType.Trade, WorkCatalog.Off);
            agent.HomeCell = new GridPoint((int)agent.Motion.Position.X, (int)agent.Motion.Position.Y); // the negotiator lives here
        }
        var npc = game.AddTownNpc(TownNpcKind.Mechanic, game.Player.Motion.Position, bound: false);
        var stack = new ItemStack(ItemId.CopperBar, 1);
        var plainMultiplier = game.GetPriceMultiplier(npc);
        var plainSell = game.GetSellPrice(npc, stack);
        var noNegotiator = game.Negotiator is null && game.TradeDiscount == 0f;

        var best = game.Settlers.MaxBy(agent => agent.Stats.Level(ColonistSkill.Social))!;
        var social = best.Stats.Level(ColonistSkill.Social);
        foreach (var agent in game.Settlers)
            agent.Work.Set(WorkType.Trade, 3);
        var expected = Math.Min(GameSimulation.MaxTradeDiscount, social * GameSimulation.TradeDiscountPerLevel);
        var tradedMultiplier = game.GetPriceMultiplier(npc);
        var tradedSell = game.GetSellPrice(npc, stack);
        check(noNegotiator && game.Negotiator == best && Math.Abs(game.TradeDiscount - expected) < 1e-5f &&
              (social == 0 || (tradedMultiplier < plainMultiplier && tradedSell >= plainSell)),
            $"Trade: the best Social on Trade work ({best.Name}, Social {social}) takes {expected:P1} off shop prices and gets more for sales " +
            $"(price ×{plainMultiplier:0.000} → ×{tradedMultiplier:0.000}, a copper bar sells for {plainSell} → {tradedSell})");

        game.HurtTownNpc(best, 1e6f);
        check(game.Negotiator != best && WorkCatalog.Get(WorkType.Trade).HasJobs,
            "a dead negotiator bargains no more; the next best on Trade takes over");
    }
}
