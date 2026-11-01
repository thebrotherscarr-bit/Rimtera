using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Town NPCs on Terraria's rules (0.3.5): happiness as a product of price factors (biome, neighbours, crowding, solitude,
/// homeless), the Nurse's and Arms Dealer's move-in conditions, and the medic clearing debuffs.
/// </summary>
public static class TownParitySelfTests
{
    private static GameSimulation Town(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 400, height: 60) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static TownNpc Resident(GameSimulation game, TownNpcKind kind, int x)
    {
        var npc = game.AddTownNpc(kind, new Vector2(x, 20f), bound: false);
        npc.HomeCell = new GridPoint(x, 21);
        return npc;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- the affection factors ----
        check(GameSimulation.AffectionFactor(Affection.Love) == 0.88f && GameSimulation.AffectionFactor(Affection.Like) == 0.94f &&
              GameSimulation.AffectionFactor(Affection.Dislike) == 1.06f && GameSimulation.AffectionFactor(Affection.Hate) == 1.12f,
            "town happiness uses Terraria's price factors: love ×0.88, like ×0.94, dislike ×1.06, hate ×1.12");

        // ---- solitude, crowding, homeless, and gels not counting as neighbours ----
        var alone = Town(9801);
        var hermit = Resident(alone, TownNpcKind.Armorer, 100);
        var hermitFactor = alone.GetPriceFactor(hermit);
        var crowd = Town(9801);
        var crowded = Resident(crowd, TownNpcKind.Armorer, 100);
        foreach (var (kind, x) in new[] { (TownNpcKind.TideWarden, 104), (TownNpcKind.Isotopist, 108), (TownNpcKind.RelicWarden, 112),
                     (TownNpcKind.Aeronaut, 96), (TownNpcKind.HeadResearcher, 92) })
            Resident(crowd, kind, x);
        var crowdFactor = crowd.GetPriceFactor(crowded);
        var gels = Town(9801);
        var withGels = Resident(gels, TownNpcKind.Armorer, 100);
        foreach (var (kind, x) in new[] { (TownNpcKind.GelAzure, 104), (TownNpcKind.GelVerdant, 108), (TownNpcKind.TravellingMerchant, 96) })
            Resident(gels, kind, x);
        var gelFactor = gels.GetPriceFactor(withGels);
        var homeless = alone.AddTownNpc(TownNpcKind.Quartermaster, new Vector2(300f, 20f), bound: false);
        check(crowdFactor > hermitFactor && Math.Abs(gelFactor - hermitFactor) < 1e-4f && alone.GetPriceFactor(homeless) == 1.5f,
            $"a lone resident gets the solitude discount, a crowded one pays more, gels and travelling traders are not neighbours, " +
            $"and the homeless charge the most (alone ×{hermitFactor:0.00}, crowded ×{crowdFactor:0.00}, with gels ×{gelFactor:0.00})");

        // ---- move-in: the Nurse needs one life core and the Merchant; the Arms Dealer a gun or bullets ----
        var nurse = Town(9802);
        nurse.Player.LifeCoresUsed = 1;
        nurse.CheckTownArrivals();
        var withoutMerchant = nurse.Flags.IsSet("npc.medic.unlocked");
        Resident(nurse, TownNpcKind.Quartermaster, 100);
        nurse.CheckTownArrivals();
        var withMerchant = nurse.Flags.IsSet("npc.medic.unlocked");
        var arms = Town(9803);
        arms.Player.Inventory.Add(ItemId.CobaltRailBow, 1);
        arms.CheckTownArrivals();
        var byBow = arms.Flags.IsSet("npc.armorer.unlocked");
        arms.Player.Inventory.Add(ItemId.SlugRounds, 10);
        arms.CheckTownArrivals();
        var byBullets = arms.Flags.IsSet("npc.armorer.unlocked");
        check(!withoutMerchant && withMerchant && !byBow && byBullets,
            $"the medic moves in after one life core with the quartermaster in town, the armorer once you carry a gun or bullets " +
            $"(medic without/with quartermaster {withoutMerchant}/{withMerchant}, armorer by bow/bullets {byBow}/{byBullets})");

        // ---- the medic clears debuffs, not potion sickness ----
        var clinic = Town(9804);
        var medic = Resident(clinic, TownNpcKind.Medic, 100);
        clinic.Player.Inventory.Add(ItemId.Credits, 500);
        clinic.Player.Statuses.Apply(StatusId.Poison, 2f);
        clinic.Player.Statuses.Apply(StatusId.PotionSickness, 2f);
        var healed = clinic.HealAtMedic(medic);
        check(healed && !clinic.Player.Statuses.IsActive(StatusId.Poison) && clinic.Player.Statuses.IsActive(StatusId.PotionSickness),
            "the medic clears debuffs for a fee, as Terraria's Nurse does, but not potion sickness");
    }
}
