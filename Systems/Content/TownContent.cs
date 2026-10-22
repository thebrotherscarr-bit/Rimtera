using System.Numerics;
using Rimtera.AI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Town tracks (docs/PROGRESSION_DIFF.md G13): the Field Zoologist (bestiary progress), the Pigment Trader (dye materials
/// carried), the Party Planner (a large colony; throws colony parties) and the Quartermaster's companion permit, which brings
/// a passive town pet. Names, lines, thresholds and stock are original Rimtera data.
/// </summary>
public static class TownContent
{
    /// <summary>Bestiary completion (0..1) at which the Field Zoologist moves in.</summary>
    public const float ZoologistBestiaryShare = 0.10f;
    /// <summary>Bestiary completion at which the Zoologist's rarer stock opens.</summary>
    public const float ZoologistRareStockShare = 0.25f;
    /// <summary>Residents (not counting gels, pets, the merchant or bound survivors) before the Party Planner comes.</summary>
    public const int PartyPlannerResidents = 10;
    /// <summary>Game hours a colony party lasts, and game hours before another can be thrown.</summary>
    public const float PartyHours = 3f, PartyCooldownHours = 24f;
    /// <summary>Joy restored to every colonist when a party starts, then topped up each half second while it runs.</summary>
    public const float PartyJoyBurst = 1f, PartyJoyPerTick = 0.01f;

    public const string ZoologistRareFlag = "bestiary.quarter";
    public const string PartyUntilFlag = "party.until_centihours";
    public const string PartyNextFlag = "party.next_centihours";

    public static void RegisterItems(Dictionary<ItemId, ItemDefinition> items)
    {
        items[ItemId.CompanionPermit] = new ItemDefinition(ItemId.CompanionPermit, "Companion permit", ItemUseKind.Utility, 1)
            { Tier = 1, Rarity = ContentItems.RarityFor(1), Value = 150, UseCooldownTicks = 12 };
    }

    /// <summary>Whether the item counts as a dye material for the Pigment Trader's arrival.</summary>
    public static bool IsDyeMaterial(ItemId item) => item == ItemId.DyePetals || PlayerSystemsContent.IsDye(item);

    public static IEnumerable<TownNpcDefinition> Definitions()
    {
        yield return new(TownNpcKind.FieldZoologist, "Field Zoologist", "npc_field_zoologist", ["Dr. Imara Fenwick", "Tobias Quell", "Nadia Sorrel"],
            [(BiomeZone.VerdantCanopy, Affection.Love), (BiomeZone.Grassland, Affection.Like), (BiomeZone.JunkWasteland, Affection.Dislike)],
            [(TownNpcKind.AstroBotanist, Affection.Love), (TownNpcKind.Arborist, Affection.Like), (TownNpcKind.Demolitionist, Affection.Hate)],
            [(ItemId.BugNet, null), (ItemId.Worm, null), (ItemId.PetCrabShell, null), (ItemId.HerbPlanter, null),
             (ItemId.GoldenCritter, ZoologistRareFlag), (ItemId.GildedVole, ZoologistRareFlag), (ItemId.LightPetWisp, ZoologistRareFlag)],
            ["Every creature you log is a page I don't have to write myself.", "Mind the critters. They were here before the colony.",
             "Something out there glitters when it runs. Catch one for me?"]);
        yield return new(TownNpcKind.PigmentTrader, "Pigment Trader", "npc_pigment_trader", ["Saffi Lorne", "Indra Vell", "Okoye Marr"],
            [(BiomeZone.CrystalSpires, Affection.Love), (BiomeZone.AcidCoast, Affection.Like), (BiomeZone.Caverns, Affection.Dislike)],
            [(TownNpcKind.Tailor, Affection.Love), (TownNpcKind.Quartermaster, Affection.Like), (TownNpcKind.ChiefEngineer, Affection.Dislike)],
            [(ItemId.DyePetals, null), (ItemId.DyeVat, null), (ItemId.DyeSolvent, null), (ItemId.CrimsonDye, null), (ItemId.AzureDye, null),
             (ItemId.VerdantDye, null), (ItemId.AmberDye, null), (ItemId.VioletDye, null), (ItemId.TealDye, null), (ItemId.Paintbrush, null),
             (ItemId.ShadowDye, "blight.fracture"), (ItemId.PrismaticDye, "hardmode.trio.down")],
            ["A grey hull is a sad hull.", "Petals, crystal dust and a little patience: that's all a colour is.",
             "Bring me pigment and I'll bring you a rainbow."]);
        yield return new(TownNpcKind.PartyPlanner, "Party Planner", "npc_party_planner", ["Jubilee Okafor", "Remy Castellan", "Pip Dunmore"],
            [(BiomeZone.Grassland, Affection.Love), (BiomeZone.FungalHollows, Affection.Like), (BiomeZone.RadiatedDeeps, Affection.Hate)],
            [(TownNpcKind.Cook, Affection.Love), (TownNpcKind.Tailor, Affection.Like), (TownNpcKind.Archivist, Affection.Dislike)],
            [(ItemId.FestivalMask, null), (ItemId.ColonyBanner, null), (ItemId.GlowSticks, null), (ItemId.NectarTea, null),
             (ItemId.AudioDeck, null)],
            ["Ten neighbours and nobody has thrown a party? Unacceptable.", "Say the word and I'll have the whole colony dancing.",
             "Good food, loud music, no raiders. That's the recipe."]);
    }
}

public sealed partial class GameSimulation
{
    /// <summary>Residents who count toward the Party Planner (free residents; no gels, pets or the travelling merchant).</summary>
    public int TownResidentCount => _townNpcs.Count(npc => !npc.IsBound && !ContentFillTown.IsGel(npc.Kind) &&
                                                           npc.Kind != TownNpcKind.TravellingMerchant);

    /// <summary>Whether a colony party is running.</summary>
    public bool IsColonyPartyActive => ColonyPartyHoursLeft > 0f;

    /// <summary>Game hours left in the current colony party (0 when none).</summary>
    public float ColonyPartyHoursLeft => Math.Max(0f, Flags.Get(TownContent.PartyUntilFlag) / 100f - ElapsedGameHours);

    /// <summary>Whether the Party Planner can throw a party now; <paramref name="reason"/> explains a refusal.</summary>
    public bool CanStartColonyParty(out string reason)
    {
        if (!_townNpcs.Any(npc => npc.Kind == TownNpcKind.PartyPlanner && !npc.IsBound))
            reason = "There is no Party Planner in the colony.";
        else if (IsColonyPartyActive)
            reason = "The party is already in full swing!";
        else if (Flags.Get(TownContent.PartyNextFlag) / 100f > ElapsedGameHours)
            reason = "The Party Planner is still cleaning up after the last one.";
        else
        {
            reason = "";
            return true;
        }
        return false;
    }

    /// <summary>
    /// Starts a colony party (Party Planner dialogue): every colonist's and the player's joy is filled, joy keeps topping up while
    /// it runs, and the player regenerates for its length. Returns false (with a message) if one cannot start now.
    /// </summary>
    public bool StartColonyParty()
    {
        if (!CanStartColonyParty(out var reason))
        {
            PostMessage(reason);
            return false;
        }
        var now = (long)MathF.Round(ElapsedGameHours * 100f);
        SetFlagValue(TownContent.PartyUntilFlag, now + (long)(TownContent.PartyHours * 100f));
        SetFlagValue(TownContent.PartyNextFlag, now + (long)(TownContent.PartyCooldownHours * 100f));
        Flags.Increment("party.thrown");
        foreach (var agent in _colonists.Where(agent => agent.IsAlive))
            agent.Needs.Restore(NeedKind.Joy, TownContent.PartyJoyBurst);
        Player.Needs.Restore(NeedKind.Joy, TownContent.PartyJoyBurst);
        Player.Statuses.Apply(StatusId.Regeneration, TownContent.PartyHours, Player.Stats.ImmunityTags);
        foreach (var npc in _townNpcs.Where(npc => !npc.IsBound))
            npc.Activity = "Partying";
        PostMessage("The Party Planner kicks off a colony party! Everyone's spirits lift.");
        return true;
    }

    /// <summary>
    /// Files a carried companion permit: a passive town pet (the first kind not yet living here) moves into a free house.
    /// Returns false when nothing is carried, every pet already lives here, or no house is free.
    /// </summary>
    public bool FileCompanionPermit()
    {
        if (Player.Inventory.Count(ItemId.CompanionPermit) <= 0)
            return false;
        var missing = Enum.GetValues<TownPetKind>().Where(kind => _pets.All(pet => pet.Kind != kind)).ToArray();
        if (missing.Length == 0 || FindPetHouse() is null)
            return false;
        var before = _pets.Count;
        UsePetPermit(ItemId.CompanionPermit, missing[0]);
        return _pets.Count > before;
    }

    /// <summary>Refreshes the G13 move-in flags now (also runs every half second; debug tools, tests).</summary>
    public void RefreshTownContentUnlocks()
    {
        void Unlock(TownNpcKind kind) => Flags.Set($"npc.{WorldFlags.Key(kind)}.unlocked");
        var completion = Bestiary.Completion;
        if (completion >= TownContent.ZoologistBestiaryShare)
            Unlock(TownNpcKind.FieldZoologist);
        if (completion >= TownContent.ZoologistRareStockShare)
            Flags.Set(TownContent.ZoologistRareFlag);
        if (Player.Inventory.Slots.Any(stack => !stack.IsEmpty && TownContent.IsDyeMaterial(stack.ItemId)))
            Unlock(TownNpcKind.PigmentTrader);
        if (TownResidentCount >= TownContent.PartyPlannerResidents)
            Unlock(TownNpcKind.PartyPlanner);
    }

    /// <summary>Half-second town update (driven from the achievement pass): unlocks, permit filing and the party.</summary>
    internal void UpdateTownContent()
    {
        RefreshTownContentUnlocks();
        if (Player.Inventory.Count(ItemId.CompanionPermit) > 0)
            FileCompanionPermit();
        if (!IsColonyPartyActive)
            return;
        foreach (var agent in _colonists.Where(agent => agent.IsAlive))
            agent.Needs.Restore(NeedKind.Joy, TownContent.PartyJoyPerTick);
        Player.Needs.Restore(NeedKind.Joy, TownContent.PartyJoyPerTick);
    }
}
