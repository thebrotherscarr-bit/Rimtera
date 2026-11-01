using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Pylons are relays (0.7.6, the power grid). Each relay holds its own charge, kept in the world's flags so it is saved,
/// and its own tier (the best fuel crystal it has been charged with). A relay is online while it is charged and staffed
/// (<see cref="PylonResidentsNeeded"/> people living within <see cref="PylonTownRadius"/> tiles). Online, it powers the
/// machines within that radius at its tier and lets you travel; each trip costs <see cref="RelayTravelCost"/> at both ends,
/// and an online relay burns <see cref="RelayUpkeepPerHour"/> an hour. A relay that runs dry goes offline, with a letter,
/// and stays dark until you go back and charge it in person (interact with it carrying fuel crystals). The station AI is the
/// base's relay: within <see cref="OutpostDistance"/> tiles of its home, machines draw on its reserve.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>A relay's most charge, what a new (or older) relay starts with, its upkeep an hour, and a trip's cost per end.</summary>
    public const int RelayCapacity = 300, RelayStartCharge = 60, RelayUpkeepPerHour = 1, RelayTravelCost = 10;

    private static string RelayKey(GridPoint cell) => $"relay.{cell.X}.{cell.Y}";

    /// <summary>A relay's charge (0 to <see cref="RelayCapacity"/>); a relay seen for the first time starts with <see cref="RelayStartCharge"/>.</summary>
    public int RelayCharge(GridPoint cell)
    {
        EnsureRelay(cell);
        return (int)Flags.Get(RelayKey(cell) + ".charge") - 1;
    }

    /// <summary>A relay's tier: the best fuel crystal it has been charged with (0 for crude or none).</summary>
    public int RelayTier(GridPoint cell) => (int)Flags.Get(RelayKey(cell) + ".tier");

    /// <summary>Whether enough people live around a relay to staff it.</summary>
    public bool RelayStaffed(GridPoint cell) => CountResidentsNear(cell) >= PylonResidentsNeeded;

    /// <summary>Whether a relay is online: staffed and charged.</summary>
    public bool RelayOnline(GridPoint cell) => RelayStaffed(cell) && RelayCharge(cell) > 0;

    private void EnsureRelay(GridPoint cell)
    {
        if (!Flags.IsSet(RelayKey(cell) + ".charge"))
            SetFlagValue(RelayKey(cell) + ".charge", RelayStartCharge + 1); // stored one up, so an empty relay still reads as known
    }

    private void SetRelayCharge(GridPoint cell, int charge) =>
        SetFlagValue(RelayKey(cell) + ".charge", Math.Clamp(charge, 0, RelayCapacity) + 1);

    internal void SetRelayChargeForConsole(GridPoint cell, int charge) => SetRelayCharge(cell, charge);

    internal void SetRelayTierForConsole(GridPoint cell, int tier) => SetFlagValue(RelayKey(cell) + ".tier", Math.Clamp(tier, 0, 5));

    /// <summary>
    /// Charges a relay from the player's fuel crystals (best first) until it is full, raising its tier to the best crystal
    /// used. Returns what happened, for the message line.
    /// </summary>
    public string ChargeRelay(GridPoint cell)
    {
        if (!World.Contains(cell) || !TravelContent.IsPylon(World.GetCell(cell).TileId))
            return "There is no relay there.";
        var fed = new List<string>();
        var tierBefore = RelayTier(cell);
        foreach (var (fuel, energy, tier) in PowerFuels.OrderByDescending(entry => entry.Tier))
        {
            var room = (RelayCapacity - RelayCharge(cell)) / energy;
            var take = Math.Min(room, Player.Inventory.Count(fuel));
            if (take == 0 && tier > RelayTier(cell) && Player.Inventory.Count(fuel) > 0)
                take = 1; // the best crystal raises the tier even into a full relay
            if (take <= 0 || !Player.Inventory.TryConsume(fuel, take))
                continue;
            SetRelayCharge(cell, RelayCharge(cell) + take * energy);
            if (tier > RelayTier(cell))
                SetFlagValue(RelayKey(cell) + ".tier", tier);
            fed.Add($"{take} {Items[fuel].Name.ToLowerInvariant()}");
        }
        if (fed.Count == 0)
            return RelayStatus(cell) + (RelayCharge(cell) >= RelayCapacity ? "" : " Bring fuel crystals to charge it.");
        var raised = RelayTier(cell) > tierBefore ? $" It now powers tier {RelayTier(cell)} machines." : "";
        Record($"A relay was charged ({RelayCharge(cell)}/{RelayCapacity}).");
        return $"Charged the relay with {string.Join(", ", fed)}: {RelayCharge(cell)}/{RelayCapacity}.{raised}";
    }

    /// <summary>A relay's state in words.</summary>
    public string RelayStatus(GridPoint cell)
    {
        var name = Tiles[World.GetCell(cell).TileId].Name;
        var state = RelayOnline(cell) ? "online"
            : !RelayStaffed(cell) ? $"offline: it needs {PylonResidentsNeeded} people living around it"
            : "offline: out of charge";
        return $"{name}: {state}, charge {RelayCharge(cell)}/{RelayCapacity}, tier {RelayTier(cell)}.";
    }

    /// <summary>Hourly: online relays burn their upkeep; one that runs dry goes offline, with a letter.</summary>
    private void UpdateRelays()
    {
        foreach (var pylon in Pylons)
        {
            if (!RelayOnline(pylon.Cell))
                continue;
            SetRelayCharge(pylon.Cell, RelayCharge(pylon.Cell) - RelayUpkeepPerHour);
            if (RelayCharge(pylon.Cell) > 0)
                continue;
            var place = PylonPlace(pylon);
            SendLetter(LetterKind.BadNews, $"Relay offline: {place}",
                $"The {pylon.Name.ToLowerInvariant()} at {place} has run dry. Its machines run unpowered and travel there is cut off until you go back and charge it.",
                tag: RelayKey(pylon.Cell));
            Record($"The relay at {place} ran dry.");
        }
    }

    /// <summary>
    /// Where a machine of tier <paramref name="need"/> at <paramref name="at"/> draws power, or null when it runs unpowered:
    /// the station AI's reserve at the base (or anywhere, until the AI has a home), else the nearest online relay that
    /// reaches it at that tier with the charge to spare.
    /// </summary>
    private (bool Ai, GridPoint Relay)? PowerSourceAt(int need, Vector2 at)
    {
        if (need <= 0)
            return null;
        var aiHome = _townNpcs.Any(npc => npc.Kind == TownNpcKind.StationAi && !npc.IsBound && npc.HomeCell is not null);
        if (!aiHome || Vector2.Distance(at, BaseCenter) <= OutpostDistance)
        {
            if (StationAiOnline && StationPowerTier >= need && StationPower >= need)
                return (true, default);
            if (!aiHome)
                return null;
        }
        foreach (var pylon in Pylons.OrderBy(pylon => Vector2.Distance(at, new Vector2(pylon.Cell.X + 0.5f, pylon.Cell.Y))))
        {
            if (Vector2.Distance(at, new Vector2(pylon.Cell.X + 0.5f, pylon.Cell.Y)) > PylonTownRadius)
                break;
            if (RelayOnline(pylon.Cell) && RelayTier(pylon.Cell) >= need && RelayCharge(pylon.Cell) >= need)
                return (false, pylon.Cell);
        }
        return null;
    }

    /// <summary>Whether a station at <paramref name="at"/> runs powered now: from the base's AI, or from a relay near it.</summary>
    public bool StationPoweredAt(CraftingStationId station, Vector2 at) => PowerSourceAt(PowerTierOf(station), at) is not null;

    /// <summary>Draws a powered craft's cost from whatever powers it at <paramref name="at"/>.</summary>
    private void SpendCraftPowerAt(RecipeDefinition recipe, Vector2 at)
    {
        var cost = PowerCost(recipe);
        if (PowerSourceAt(cost, at) is not { } source)
            return;
        if (source.Ai)
        {
            SpendCraftPower(recipe);
            return;
        }
        SetRelayCharge(source.Relay, RelayCharge(source.Relay) - cost);
    }
}
