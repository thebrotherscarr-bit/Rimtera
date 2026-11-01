using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>An item lying in the world: pops out, falls, merges with neighbours, and is pulled to the player.</summary>
public sealed class GroundItem
{
    internal GroundItem(long id, ItemId itemId, int count, Calibration calibration, Vector2 position, Vector2 velocity)
    {
        Id = id;
        ItemId = itemId;
        Count = count;
        Calibration = calibration;
        Position = position;
        Velocity = velocity;
    }

    public long Id { get; }
    public ItemId ItemId { get; }
    public int Count { get; internal set; }
    public Calibration Calibration { get; }
    /// <summary>Center of the item in world tiles.</summary>
    public Vector2 Position { get; internal set; }
    public Vector2 Velocity { get; internal set; }
    /// <summary>Seconds since it dropped (despawns after <see cref="GameSimulation.GroundItemLifetimeSeconds"/>).</summary>
    public float AgeSeconds { get; internal set; }
    public bool Grounded { get; internal set; }
    /// <summary>True while being pulled toward the player (draw a little trail).</summary>
    public bool Attracted { get; internal set; }
    /// <summary>Seconds before the player can pick it up (items the player threw away).</summary>
    public float PickupDelay { get; internal set; }
}

public sealed record GroundItemSnapshot(ItemId ItemId, int Count, float X, float Y, float Age, Calibration Calibration = Calibration.None);

public sealed partial class GameSimulation
{
    /// <summary>Tiles within which loose items fly to the player: Terraria's 42-pixel default grab range at 16-pixel tiles.</summary>
    public const float ItemGrabRadius = 2.625f;
    /// <summary>Grab radius with a magnet accessory.</summary>
    public const float ItemMagnetRadius = 10f;
    /// <summary>Tiles within which an item is collected.</summary>
    public const float ItemCollectRadius = 0.85f;
    /// <summary>Drops from the player's own actions (mining, melee kills, broken containers) within this distance go straight to the inventory.</summary>
    public const float InstantCollectRadius = PlayerReach + 1.5f;
    public const float GroundItemLifetimeSeconds = 600f;
    public const int MaximumGroundItems = 400;

    private readonly List<GroundItem> _groundItems = new();
    /// <summary>While set, drops go straight to the inventory regardless of distance (forced/debug breaks).</summary>
    private bool _directDrops;
    /// <summary>While set (a tile breaking), drops always land as ground items for the magnet to collect.</summary>
    private bool _groundDrops;
    private Random? _groundRandomField;
    private Random GroundRandom => _groundRandomField ??= new Random(Seed ^ 0x6A0D17E);

    /// <summary>Loose items in the world (render with the item sprite at <see cref="GroundItem.Position"/>).</summary>
    /// <summary>Crafting audit: empties the pack and the ground between scanned tile breaks.</summary>
    internal void ClearAuditLoot()
    {
        _groundItems.Clear();
        foreach (var stack in Player.Inventory.Slots.Where(stack => !stack.IsEmpty).ToArray())
            Player.Inventory.TryConsume(stack.ItemId, stack.Count);
    }

    public IReadOnlyList<GroundItem> GroundItems => _groundItems;

    /// <summary>Current grab radius (larger with a magnet accessory).</summary>
    public float ItemPickupRadius => HasTag("magnet") ? ItemMagnetRadius : ItemGrabRadius;

    /// <summary>
    /// Drop produced near the player: if the player is close enough (or the drop came from their own reach)
    /// it goes straight into the inventory; whatever does not fit or is out of range becomes a ground item.
    /// </summary>
    public void CollectOrDrop(ItemId item, int count, Vector2 position, Calibration calibration = Calibration.None)
    {
        if (count <= 0 || !Items.Contains(item))
            return;
        if (_groundDrops && !_directDrops || Items[item].IsInstantPickup)
        {
            // Broken blocks pop out as loose items and are picked up by walking near them; hearts and charge orbs always
            // land as pickups (applied on touch, never put in the bag).
            SpawnGroundItem(item, count, position, calibration);
            return;
        }
        var near = _directDrops ||
                   (!Player.IsDead && Vector2.Distance(position, PlayerCenter) <= Math.Max(InstantCollectRadius, ItemPickupRadius));
        if (near)
        {
            var added = Player.Inventory.Add(item, count, calibration);
            if (added > 0)
                OnItemPickedUp(item, added);
            count -= added;
        }
        if (count > 0)
            SpawnGroundItem(item, count, position, calibration);
    }

    /// <summary>Spawns a loose item with a small pop, merging into a matching item close by.</summary>
    public GroundItem? SpawnGroundItem(ItemId item, int count, Vector2 position, Calibration calibration = Calibration.None,
        float pickupDelay = 0f)
    {
        if (count <= 0 || !Items.Contains(item))
            return null;
        var max = Items[item].MaxStack;
        GroundItem? last = null;
        foreach (var existing in _groundItems)
        {
            if (count <= 0)
                break;
            if (existing.ItemId != item || existing.Calibration != calibration || existing.Count >= max ||
                Vector2.DistanceSquared(existing.Position, position) > 1f)
                continue;
            var moved = Math.Min(count, max - existing.Count);
            existing.Count += moved;
            existing.AgeSeconds = 0f;
            count -= moved;
            last = existing;
        }
        while (count > 0)
        {
            var take = Math.Min(count, max);
            var pop = new Vector2(((float)GroundRandom.NextDouble() * 2f - 1f) * 1.6f, -2.5f - (float)GroundRandom.NextDouble() * 1.5f);
            last = new GroundItem(_nextEntityId++, item, take, calibration, ClampToWorld(position), pop) { PickupDelay = pickupDelay };
            _groundItems.Add(last);
            count -= take;
        }
        while (_groundItems.Count > MaximumGroundItems)
            _groundItems.RemoveAt(0);
        return last;
    }

    /// <summary>Throws the selected (or given) inventory slot onto the ground in front of the player.</summary>
    public GroundItem? DropInventorySlot(int slot, int quantity = int.MaxValue)
    {
        if ((uint)slot >= (uint)Player.Inventory.SlotCount)
            return null;
        var taken = Player.Inventory.TakeFromSlot(slot, quantity);
        if (taken.IsEmpty)
            return null;
        var item = SpawnGroundItem(taken.ItemId, taken.Count, PlayerCenter + new Vector2(Player.Motion.Facing * 0.8f, -0.3f),
            taken.Calibration, pickupDelay: 1.5f);
        if (item is not null)
            item.Velocity = new Vector2(Player.Motion.Facing * 4f, -2.5f);
        return item;
    }

    private Vector2 ClampToWorld(Vector2 position) =>
        Vector2.Clamp(position, new Vector2(0.3f, 0.3f), new Vector2(World.Width - 0.3f, World.Height - 0.3f));

    private void UpdateGroundItems(float deltaSeconds)
    {
        if (_groundItems.Count == 0)
            return;
        var center = PlayerCenter;
        var grab = ItemPickupRadius;
        var canCollect = !Player.IsDead;
        for (var i = _groundItems.Count - 1; i >= 0; i--)
        {
            var item = _groundItems[i];
            item.AgeSeconds += deltaSeconds;
            item.PickupDelay = Math.Max(0f, item.PickupDelay - deltaSeconds);
            if (item.AgeSeconds >= GroundItemLifetimeSeconds)
            {
                _groundItems.RemoveAt(i);
                continue;
            }
            var distance = Vector2.Distance(item.Position, center);
            var instant = Items[item.ItemId].IsInstantPickup;
            item.Attracted = canCollect && item.PickupDelay <= 0f && distance <= grab && (instant || Player.Inventory.CanAdd(item.ItemId, 1));
            if (item.Attracted)
            {
                if (instant && distance <= ItemCollectRadius)
                {
                    // Hearts and charge orbs work on touch and never enter the bag.
                    for (var n = 0; n < item.Count; n++)
                        ApplyInstantPickup(Items[item.ItemId]);
                    _groundItems.RemoveAt(i);
                    continue;
                }
                if (distance <= ItemCollectRadius)
                {
                    var added = Player.Inventory.Add(item.ItemId, item.Count, item.Calibration);
                    if (added > 0)
                        OnItemPickedUp(item.ItemId, added);
                    item.Count -= added;
                    if (item.Count <= 0)
                    {
                        _groundItems.RemoveAt(i);
                        continue;
                    }
                }
                var pull = Vector2.Normalize(center - item.Position) * Math.Max(6f, 14f - distance);
                item.Velocity = Vector2.Lerp(item.Velocity, pull, Math.Min(1f, deltaSeconds * 10f));
                item.Position = ClampToWorld(item.Position + item.Velocity * deltaSeconds);
                item.Grounded = false;
                continue;
            }
            StepGroundItemPhysics(item, deltaSeconds);
        }
        if (_batchBTick % 30 == 0)
            MergeGroundItems();
    }

    private void StepGroundItemPhysics(GroundItem item, float deltaSeconds)
    {
        var velocity = item.Velocity;
        var cell = ClampCell(RegionMap.CellAt(item.Position));
        var inLiquid = World.GetCell(cell).LiquidKind != LiquidKind.None;
        velocity.Y = Math.Min(velocity.Y + (inLiquid ? 6f : 20f) * deltaSeconds, inLiquid ? 1.5f : 10f);
        velocity.X *= MathF.Pow(item.Grounded ? 0.02f : 0.6f, deltaSeconds);
        var next = item.Position + new Vector2(velocity.X * deltaSeconds, 0f);
        if (SolidAt(next + new Vector2(MathF.Sign(velocity.X) * 0.2f, 0f)))
            velocity.X = 0f;
        else
            item.Position = next;
        next = item.Position + new Vector2(0f, velocity.Y * deltaSeconds);
        if (velocity.Y > 0f && SolidAt(next + new Vector2(0f, 0.22f)))
        {
            // Rest on top of the tile below.
            var floor = MathF.Floor(next.Y + 0.22f);
            item.Position = item.Position with { Y = Math.Min(item.Position.Y, floor - 0.22f) };
            velocity.Y = 0f;
            item.Grounded = true;
        }
        else if (velocity.Y < 0f && SolidAt(next - new Vector2(0f, 0.22f)))
            velocity.Y = 0f;
        else
        {
            item.Position = next;
            item.Grounded = false;
        }
        // Buried (a tile was placed on it): nudge up.
        if (SolidAt(item.Position))
            item.Position -= new Vector2(0f, 1f);
        item.Position = ClampToWorld(item.Position);
        item.Velocity = velocity;
    }

    private bool SolidAt(Vector2 point)
    {
        var cell = new GridPoint((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y));
        return !World.Contains(cell) || Tiles[World.GetCell(cell).TileId].IsSolid;
    }

    private void MergeGroundItems()
    {
        for (var i = 0; i < _groundItems.Count; i++)
        {
            var a = _groundItems[i];
            var max = Items[a.ItemId].MaxStack;
            for (var j = _groundItems.Count - 1; j > i; j--)
            {
                var b = _groundItems[j];
                if (b.ItemId != a.ItemId || b.Calibration != a.Calibration || a.Count >= max ||
                    Vector2.DistanceSquared(a.Position, b.Position) > 1.5f * 1.5f)
                    continue;
                var moved = Math.Min(b.Count, max - a.Count);
                a.Count += moved;
                b.Count -= moved;
                a.AgeSeconds = Math.Min(a.AgeSeconds, b.AgeSeconds);
                if (b.Count <= 0)
                    _groundItems.RemoveAt(j);
            }
        }
    }

    // ------------------------------------------------------------------ hearts, charge orbs and boss potions (0.1.9 C2/C3)

    /// <summary>Hearts and charge orbs the player has picked up (for checks and the stats screen).</summary>
    public int HeartsPickedUp { get; private set; }
    public int ChargeOrbsPickedUp { get; private set; }

    private Random? _dropRandomField;
    /// <summary>Rolls drop chances, hearts and charge orbs, and boss potion counts.</summary>
    private Random DropRandom => _dropRandomField ??= new Random(Seed ^ 0x1D80F5);

    /// <summary>Base one-in-12 chance of a heart (or orb), rising to one in four for an empty bar.</summary>
    public const float PickupBaseChance = 1f / 12f, PickupMissingChance = 1f / 6f;

    /// <summary>
    /// Chance a kill drops a heart: none at full health, otherwise a base chance plus a share weighted by how hurt the player is
    /// (Terraria's NPCLoot_DropHeals favours a hurt player). At half health it is one in six.
    /// </summary>
    public float HeartDropChance => Player.IsDead || Player.HitPoints >= Player.MaximumHitPoints - 0.5f ? 0f
        : PickupBaseChance + PickupMissingChance * (1f - Player.HitPoints / Math.Max(1f, Player.MaximumHitPoints));

    /// <summary>Chance a kill drops a charge orb: none with a full charge bar, more the emptier it is (Terraria's mana stars).</summary>
    public float ChargeOrbDropChance => Player.IsDead || Charge >= MaximumCharge - 0.5f ? 0f
        : PickupBaseChance + PickupMissingChance * (1f - Charge / Math.Max(1f, MaximumCharge));

    /// <summary>
    /// After the regular loot: a boss leaves 5-15 healing stims of its tier (delivered like its loot, outside any salvage cache,
    /// on every difficulty) and 3-5 hearts; any other hostile kill may leave a heart or a charge orb.
    /// </summary>
    private void DropSustainLoot(EnemyAgent enemy)
    {
        var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
        if (enemy.IsBoss)
        {
            var (stim, low, high) = BossPotions(enemy.Archetype.Tier);
            DropLoot(enemy, stim, DropRandom.Next(low, high + 1));
            var hearts = DropRandom.Next(3, 6);
            for (var n = 0; n < hearts; n++)
                SpawnGroundItem(ItemId.HeartPickup, 1, center + new Vector2((n - hearts / 2f) * 0.5f, 0f), pickupDelay: 0.5f);
            return;
        }
        if (enemy.Archetype.Passive || enemy.Archetype.AttackDamage <= 0f || enemy.Archetype.HitPoints <= 1f)
            return;
        if (DropRandom.NextDouble() < HeartDropChance)
            SpawnGroundItem(ItemId.HeartPickup, 1, center);
        if (DropRandom.NextDouble() < ChargeOrbDropChance)
            SpawnGroundItem(ItemId.ChargeOrb, 1, center);
    }

    /// <summary>The healing stim a boss of <paramref name="tier"/> drops and how many (5-15, more for later bosses).</summary>
    public static (ItemId Stim, int Low, int High) BossPotions(BossTier tier) => tier switch
    {
        BossTier.Gate => (ItemId.MedStim, 5, 8),
        BossTier.Easy => (ItemId.MedStim, 6, 10),
        BossTier.Hard => (ItemId.TraumaStim, 8, 12),
        BossTier.Event => (ItemId.NaniteStim, 10, 14),
        _ => (ItemId.PhoenixStim, 11, 15)
    };

    /// <summary>A heart heals, a charge orb refills charge; neither brings on sickness.</summary>
    private void ApplyInstantPickup(ItemDefinition item)
    {
        if (item.PickupHeal > 0f)
        {
            Player.Heal(item.PickupHeal);
            HeartsPickedUp++;
        }
        if (item.PickupCharge > 0f)
        {
            Charge += item.PickupCharge;
            ChargeOrbsPickedUp++;
        }
    }

    private void OnItemPickedUp(ItemId item, int count)
    {
        SoundEvents.Emit(SoundEventKind.Pickup, item == ItemId.Credits ? "coin" : string.Empty, PlayerCenter, (int)item);
        if (item is ItemId.CopperOre or ItemId.CobaltOre or ItemId.ViridiumOre or ItemId.ScrapIronOre or ItemId.GlacialOre
            or ItemId.UraniumOre or ItemId.PyrociteOre or ItemId.LumeniteOre or ItemId.VoidsteelOre or ItemId.MeteoriteOre or ItemId.AstralOre)
            Achievements.TryUnlock("first-ore");
    }

    private GroundItemSnapshot[] CaptureGroundItems() => _groundItems
        .Select(item => new GroundItemSnapshot(item.ItemId, item.Count, item.Position.X, item.Position.Y, item.AgeSeconds, item.Calibration))
        .ToArray();

    private void RestoreGroundItems(IEnumerable<GroundItemSnapshot>? items)
    {
        foreach (var saved in items ?? [])
        {
            if (!Items.Contains(saved.ItemId) || saved.Count <= 0 || !float.IsFinite(saved.X) || !float.IsFinite(saved.Y) ||
                !Enum.IsDefined(saved.Calibration))
                continue;
            var count = Math.Min(saved.Count, Items[saved.ItemId].MaxStack);
            _groundItems.Add(new GroundItem(_nextEntityId++, saved.ItemId, count, saved.Calibration,
                ClampToWorld(new Vector2(saved.X, saved.Y)), Vector2.Zero)
            { AgeSeconds = float.IsFinite(saved.Age) ? Math.Clamp(saved.Age, 0f, GroundItemLifetimeSeconds) : 0f });
        }
    }
}
