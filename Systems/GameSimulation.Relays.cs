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

    // ---- 0.7.8: raiders hit relays ----
    /// <summary>A relay's most integrity; how near a hostile must be to hit it (tiles), and how often it hits (seconds).</summary>
    public const int RelayMaxIntegrity = 200;
    public const float RelayHitReach = 2.5f, RelayHitInterval = 1f;
    private float _relayHitTimer;

    /// <summary>A relay's integrity (0 to <see cref="RelayMaxIntegrity"/>); full until something hits it.</summary>
    public int RelayIntegrity(GridPoint cell) =>
        Flags.IsSet(RelayKey(cell) + ".hp") ? (int)Flags.Get(RelayKey(cell) + ".hp") - 1 : RelayMaxIntegrity;

    private void SetRelayIntegrity(GridPoint cell, int value) =>
        SetFlagValue(RelayKey(cell) + ".hp", Math.Clamp(value, 0, RelayMaxIntegrity) + 1);

    /// <summary>
    /// Each tick (0.7.8): every hostile within <see cref="RelayHitReach"/> tiles of a relay hits it once a second for its
    /// attack damage. A relay worn to nothing is destroyed: it falls, with a letter, and whatever it powered goes dark.
    /// </summary>
    private void UpdateRelayDamage(float deltaSeconds)
    {
        _relayHitTimer -= deltaSeconds;
        if (_relayHitTimer > 0f)
            return;
        _relayHitTimer = RelayHitInterval;
        if (!_enemies.Any(enemy => enemy.IsAlive && !enemy.IsPassive))
            return;
        foreach (var pylon in Pylons)
        {
            var at = new System.Numerics.Vector2(pylon.Cell.X + 0.5f, pylon.Cell.Y);
            var hits = _enemies.Where(enemy => enemy.IsAlive && !enemy.IsPassive &&
                    System.Numerics.Vector2.Distance(enemy.Motion.Position + enemy.Motion.Size / 2f, at) <= RelayHitReach)
                .Sum(enemy => Math.Max(1f, enemy.AttackDamage));
            if (hits <= 0f)
                continue;
            var left = RelayIntegrity(pylon.Cell) - (int)MathF.Ceiling(hits);
            SetRelayIntegrity(pylon.Cell, left);
            if (left > 0)
                continue;
            DestroyRelay(pylon);
        }
    }

    private void DestroyRelay(PylonInfo pylon)
    {
        var place = PylonPlace(pylon);
        World.SetTile(pylon.Cell, TileId.Air);
        _pylons.Remove(pylon.Cell);
        foreach (var part in new[] { ".charge", ".tier", ".hp" })
            Flags.Clear(RelayKey(pylon.Cell) + part);
        Flags.Increment("history.relays_lost");
        SendLetter(LetterKind.Threat, $"Relay destroyed: {place}",
            $"Raiders tore down the {pylon.Name.ToLowerInvariant()} at {place}. Its machines run unpowered and travel there is cut off. Build a new pylon to restore it.",
            tag: RelayKey(pylon.Cell));
        Record($"The relay at {place} was destroyed.");
        PostMessage($"The relay at {place} was destroyed!");
    }

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
            var room = (RelayCapacity - RelayCharge(cell) + RelayMaxIntegrity - RelayIntegrity(cell)) / energy;
            var take = Math.Min(room, Player.Inventory.Count(fuel));
            if (take == 0 && tier > RelayTier(cell) && Player.Inventory.Count(fuel) > 0)
                take = 1; // the best crystal raises the tier even into a full relay
            if (take <= 0 || !Player.Inventory.TryConsume(fuel, take))
                continue;
            // 0.7.8: a damaged relay is repaired first, one point of integrity for each point of charge.
            var energyIn = take * energy;
            var repair = Math.Min(energyIn, RelayMaxIntegrity - RelayIntegrity(cell));
            if (repair > 0)
                SetRelayIntegrity(cell, RelayIntegrity(cell) + repair);
            SetRelayCharge(cell, RelayCharge(cell) + energyIn - repair);
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
        var damage = RelayIntegrity(cell) < RelayMaxIntegrity ? $", integrity {RelayIntegrity(cell)}/{RelayMaxIntegrity}" : "";
        return $"{name}: {state}, charge {RelayCharge(cell)}/{RelayCapacity}, tier {RelayTier(cell)}{damage}.";
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
