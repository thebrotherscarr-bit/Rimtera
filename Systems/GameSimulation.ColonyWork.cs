using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Colony work (0.2.1, docs/DESIGN_COLONY_ESCAPE.md §3.3, the think-tree pattern in docs/RESEARCH.md): each colonist asks
/// the job giver of every enabled work type, in priority order, for a job, and does the first one offered. Guard, Tend,
/// Haul and Operate (station bills, 0.2.3: GameSimulation.Bills.cs) have job givers now; the other work types arrive with
/// their systems (zones, research).
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>How far from home (tiles) each kind of work reaches.</summary>
    public const float GuardRadius = 16f, TendRadius = 30f, HaulRadius = 24f;
    /// <summary>Loose items this close to the player are left for the player to pick up.</summary>
    public const float HaulPlayerClearance = 6f;
    /// <summary>A tended patient is "hurt" below this share of their health.</summary>
    public const float TendThreshold = 0.7f;

    /// <summary>The work the colonist chose this tick (the Work tab shows it); null while on the camp routine.</summary>
    public static WorkType? CurrentWork(ColonistAgent agent) => agent.CurrentWork;

    /// <summary>Asks each enabled job giver in priority order; true when one gave a job and the colonist worked it.</summary>
    private bool TryColonyWork(ColonistAgent agent, Vector2 center, float deltaSeconds, float elapsedHours)
    {
        if (agent.Work.Priority(WorkType.Farm) == WorkCatalog.Off && (agent.Pouch.Count > 0 || agent.FarmTarget is not null))
            DropPouch(agent, center); // taken off Farm: seeds and herbs are set down for the haulers
        if (agent.Work.Priority(WorkType.Operate) == WorkCatalog.Off && agent.Work.Priority(WorkType.Brew) == WorkCatalog.Off &&
            (agent.Held.Count > 0 || agent.OperateStation is not null))
        {
            agent.OperateStation = null; // taken off Operate: set fetched ingredients down for the haulers
            DropHeld(agent, center);
        }
        foreach (var work in agent.Work.Order())
        {
            var worked = work switch
            {
                WorkType.Guard => TryGuard(agent, center, deltaSeconds),
                WorkType.Tend => TryTend(agent, center, deltaSeconds, elapsedHours),
                WorkType.Haul => TryHaul(agent, center, deltaSeconds),
                WorkType.Operate or WorkType.Brew => TryOperate(agent, center, deltaSeconds, target => StepColonistToward(agent, center, target, deltaSeconds, 1f), work),
                WorkType.Farm => TryFarm(agent, center, deltaSeconds),
                _ => false // no job giver yet
            };
            if (worked)
            {
                agent.CurrentWork = work;
                if (work is not (WorkType.Operate or WorkType.Brew))
                    agent.OperateStation = null; // free the station for another operator meanwhile
                return true;
            }
        }
        agent.CurrentWork = null;
        agent.OperateStation = null;
        return false;
    }

    // ------------------------------------------------------------------ guard

    private bool TryGuard(ColonistAgent agent, Vector2 center, float deltaSeconds)
    {
        agent.StrikeTimer = Math.Max(0f, agent.StrikeTimer - deltaSeconds);
        var threat = _enemies
            .Where(enemy => enemy.IsAlive && !enemy.IsPassive && !enemy.IsBoss &&
                            Vector2.Distance(enemy.Motion.Position + enemy.Motion.Size / 2f, agent.HomePosition) <= GuardRadius)
            .OrderBy(enemy => Vector2.DistanceSquared(enemy.Motion.Position + enemy.Motion.Size / 2f, center))
            .FirstOrDefault();
        if (threat is null)
            return false;
        agent.Activity = $"Guarding against the {threat.DisplayName}";
        var target = threat.Motion.Position + threat.Motion.Size / 2f;
        if (DistanceToRect(center, threat.Motion.Position, threat.Motion.Size) < 1.2f)
        {
            if (agent.StrikeTimer <= 0f)
            {
                threat.TakeDamage(GuardStrikeDamage(agent.ColonistStats));
                agent.StrikeTimer = 1f;
            }
            StepColonistToward(agent, center, center, deltaSeconds, 1f);
        }
        else
            StepColonistToward(agent, center, target, deltaSeconds, 1.1f);
        return true;
    }

    /// <summary>A guard's blow: a little base damage plus Melee skill, scaled by the colonist's melee damage.</summary>
    public static float GuardStrikeDamage(ColonistStats stats) =>
        (6f + stats.Level(ColonistSkill.Melee) * 0.8f) * stats.MeleeDamageMultiplier;

    // ------------------------------------------------------------------ tend

    private bool TryTend(ColonistAgent agent, Vector2 center, float deltaSeconds, float elapsedHours)
    {
        Vector2? patientAt = null;
        string? patientName = null;
        Action<float>? heal = null;
        var best = float.MaxValue;
        foreach (var other in _colonists)
        {
            if (ReferenceEquals(other, agent) || !other.IsAlive || !(other.IsDowned || other.HitPoints < other.MaximumHitPoints * TendThreshold))
                continue;
            var at = other.Motion.Position + other.Motion.Size / 2f;
            if (Vector2.Distance(at, agent.HomePosition) > TendRadius || Vector2.DistanceSquared(at, center) >= best)
                continue;
            best = Vector2.DistanceSquared(at, center);
            patientAt = at;
            patientName = other.Name;
            heal = other.Heal;
        }
        foreach (var npc in _townNpcs)
        {
            if (!npc.IsAlive || npc.IsBound || npc.HitPoints >= npc.MaximumHitPoints * TendThreshold)
                continue;
            var at = npc.Motion.Position + npc.Motion.Size / 2f;
            if (Vector2.Distance(at, agent.HomePosition) > TendRadius || Vector2.DistanceSquared(at, center) >= best)
                continue;
            best = Vector2.DistanceSquared(at, center);
            patientAt = at;
            patientName = npc.Name;
            heal = amount => npc.HitPoints = Math.Min(npc.MaximumHitPoints, npc.HitPoints + amount);
        }
        if (patientAt is not { } spot || heal is null)
            return false;
        agent.Activity = $"Tending {patientName}";
        if (Vector2.Distance(spot, center) < 1.6f)
        {
            heal(TendHealPerVitalHour(agent.ColonistStats) * elapsedHours);
            StepColonistToward(agent, center, center, deltaSeconds, 1f);
        }
        else
            StepColonistToward(agent, center, spot, deltaSeconds, 1f);
        return true;
    }

    /// <summary>Health a tender restores per vital hour (12.5 real seconds): faster with Medical skill.</summary>
    public static float TendHealPerVitalHour(ColonistStats stats) => 20f + stats.Level(ColonistSkill.Medical) * 3f;

    // ------------------------------------------------------------------ haul

    private bool TryHaul(ColonistAgent agent, Vector2 center, float deltaSeconds)
    {
        if (!agent.Carried.IsEmpty)
            return DeliverCarried(agent, center, target => StepColonistToward(agent, center, target, deltaSeconds, 1f)); // nowhere to put it: keep carrying until a chest has room

        GroundItem? pick = null;
        var nearest = float.MaxValue;
        foreach (var item in _groundItems)
        {
            if (item.PickupDelay > 0f || !item.Grounded || !Items.Contains(item.ItemId) || Items[item.ItemId].IsInstantPickup ||
                Vector2.Distance(item.Position, agent.HomePosition) > HaulRadius ||
                Vector2.Distance(item.Position, PlayerCenter) < HaulPlayerClearance ||
                _colonists.Any(other => !ReferenceEquals(other, agent) && other.HaulTarget == item.Id))
                continue;
            var distance = Vector2.DistanceSquared(item.Position, center);
            if (distance < nearest && StorageFor(agent, item.ItemId) is not null)
            {
                nearest = distance;
                pick = item;
            }
        }
        if (pick is null)
        {
            agent.HaulTarget = null;
            return false;
        }
        agent.HaulTarget = pick.Id;
        agent.Activity = $"Hauling {Items[pick.ItemId].Name}";
        if (Vector2.Distance(pick.Position, center) < 1.4f)
        {
            agent.Carried = new ItemStack(pick.ItemId, pick.Count, pick.Calibration);
            _groundItems.Remove(pick);
            agent.HaulTarget = null;
        }
        else
            StepColonistToward(agent, center, pick.Position, deltaSeconds, 1f);
        return true;
    }

    /// <summary>Walks the carried stack to the nearest chest with room and puts it in; false when no chest near home has room.</summary>
    private bool DeliverCarried(IStationWorker agent, Vector2 center, Action<Vector2> move, GridPoint? nearStation = null)
    {
        // An operator's product goes to the chest nearest its station (where a "keep N" bill counts it), else near home.
        var target = (nearStation is { } station ? NearestWithRoom(BillStorage(station), agent.Carried.ItemId, agent.Motion.Position) : null)
                     ?? StorageFor(agent, agent.Carried.ItemId);
        if (target is not { } chest)
            return false;
        agent.Activity = $"Hauling {Items[agent.Carried.ItemId].Name} to storage";
        var chestCenter = new Vector2(chest.X + 0.5f, chest.Y + 0.5f);
        if (Vector2.Distance(chestCenter, center) < 1.8f)
        {
            var carried = agent.Carried;
            var stored = GetContainer(chest)?.Add(carried.ItemId, carried.Count, carried.Calibration) ?? 0;
            agent.Carried = carried.Count - stored > 0 ? carried with { Count = carried.Count - stored } : default;
            Flags.Increment("colony.hauled", stored);
        }
        else
            move(chestCenter);
        return true;
    }

    private readonly Dictionary<(GridPoint, int), (long Tick, GridPoint[] Cells)> _storageNearHome = new();

    /// <summary>Chest tiles within haul range of a home, rescanned every two seconds (placed chests count before they are opened).</summary>
    private IReadOnlyList<GridPoint> StorageCellsNear(Vector2 home, float radius = HaulRadius)
    {
        var key = (new GridPoint((int)MathF.Floor(home.X), (int)MathF.Floor(home.Y)), (int)MathF.Ceiling(radius));
        if (_storageNearHome.TryGetValue(key, out var cached) && TickNumber - cached.Tick < 120)
            return cached.Cells;
        var cells = new List<GridPoint>();
        var reach = key.Item2;
        var (keyX, keyY) = (key.Item1.X, key.Item1.Y);
        for (var y = keyY - reach; y <= keyY + reach; y++)
        for (var x = keyX - reach; x <= keyX + reach; x++)
            if (World.Contains(x, y) && ContentTiles.Has(World.TileAt(x, y), TileTraits.Container) &&
                Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), home) <= radius)
                cells.Add(new GridPoint(x, y));
        _storageNearHome[key] = (TickNumber, cells.ToArray());
        return cells;
    }

    /// <summary>The nearest chest within haul range of the colonist's home with room for <paramref name="item"/>.</summary>
    private GridPoint? StorageFor(IStationWorker agent, ItemId item) =>
        NearestWithRoom(StorageCellsNear(agent.HomePosition), item, agent.Motion.Position);

}
