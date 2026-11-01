using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>What an interaction did (for the UI: open a container panel, a dialogue, or just show the message).</summary>
public enum WorldInteractionKind : byte
{
    None,
    DoorToggled,
    ContainerOpened,
    ContainerLocked,
    VaultUnlocked,
    SurvivorFreed,
    NpcTalk,
    // Batch B (append-only).
    SignRead,
    BankOpened
}

public sealed record InteractionResult(WorldInteractionKind Kind, string Message, Inventory? Container = null, TownNpc? Npc = null,
    GridPoint? Cell = null);

/// <summary>One saved container inventory.</summary>
public sealed record ContainerSnapshot(int X, int Y, ItemStack[] Slots, bool Seeded = true);

/// <summary>
/// Content-expansion item uses: walls, demolishers, cutters, bug nets, critters, utilities, stims,
/// summons, crates, plus interaction with doors, containers, survivors and town NPCs.
/// </summary>
public sealed partial class GameSimulation
{
    public const int ContainerSlots = 20;
    private readonly Dictionary<GridPoint, Inventory> _containers = new();
    private readonly HashSet<GridPoint> _seededContainers = new();

    /// <summary>Dispatches the content-expansion use kinds. Returns false for kinds it does not own.</summary>
    private bool UseContentItem(ItemDefinition item, GridPoint point)
    {
        switch (item.UseKind)
        {
            case ItemUseKind.Tech:
                AttackTech(item, point);
                return true;
            case ItemUseKind.Command:
                UseCommand(item, point);
                return true;
            case ItemUseKind.PlaceWall:
                PlaceWall(item, point);
                return true;
            case ItemUseKind.Demolisher:
                Demolish(item, point);
                return true;
            case ItemUseKind.Cutter:
                Cut(item, point);
                return true;
            case ItemUseKind.BugNet:
                SwingBugNet(item, point);
                return true;
            case ItemUseKind.Critter:
                ReleaseCritter(item, point);
                return true;
            case ItemUseKind.Utility:
                UseUtility(item, point);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Content consumables (stims, charge cells, summons, crates). Returns false when the item is not one.</summary>
    private bool ConsumeContentItem(ItemDefinition item)
    {
        if (ConsumeBatchBItem(item))
            return true;
        if (item.SummonsBoss is { } boss)
        {
            // D-11: only a living boss of the same kind refuses; a different boss may share the fight (BO-9).
            if (SameKindBossRefusal(boss) is { } sameKind)
            {
                PostMessage(sameKind);
                return true;
            }
            if (HardmodeSummonRefusal(boss) is { } refusal)
            {
                PostMessage(refusal);
                return true;
            }
            if (boss == EnemyKind.AstroGod)
            {
                if (_astralCountdown > 0f)
                    return true;
                Player.Inventory.TryConsume(item.Id);
                StartAstralCountdown();
                return true;
            }
            if (!TrySpawnBoss(boss, BossSpawnSource.SummonItem, out var spawned, out var refused))
            {
                PostMessage(refused!);
                return true;
            }
            Player.Inventory.TryConsume(item.Id);
            PostMessage($"The {item.Name} calls the {spawned!.DisplayName}!");
            SetPlayerCooldown(20);
            return true;
        }
        if (item.StartsEvent is { } eventKind)
        {
            if (CoverageStarterRefusal(eventKind) is { } starterRefusal) { PostMessage(starterRefusal); return true; } // creature coverage: night/day starters
            if (eventKind == WorldEventKind.DefenseProtocol && !IsNearGuide(PlayerCenter) &&
                !_townNpcs.Any(npc => npc.Kind == TownNpcKind.StationAi && !npc.IsBound &&
                                      Vector2.Distance(npc.Motion.Position, PlayerCenter) < 8f))
            {
                PostMessage("The signal core must be slotted at the Station AI.");
                return true;
            }
            if (!_worldEvents.TryStart(eventKind, this, forced: true, out var reason))
            {
                PostMessage(reason);
                return true;
            }
            Player.Inventory.TryConsume(item.Id);
            SetPlayerCooldown(20);
            return true;
        }
        if (item.IsCrate)
        {
            OpenCrate(item.Id);
            return true;
        }
        if (item.HealAmount > 0f || item.ChargeRestore > 0f)
        {
            if (!Player.Inventory.TryConsume(item.Id))
                return true;
            SoundEvents.Emit(SoundEventKind.Drink, string.Empty, PlayerCenter, (int)item.Id);
            if (item.HealAmount > 0f)
                Player.Heal(item.HealAmount);
            if (item.ChargeRestore > 0f)
                RestoreChargeFromItem(item.ChargeRestore);
            SetPlayerCooldown(10);
            PostMessage($"Used {item.Name}.");
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ walls and tools

    private void PlaceWall(ItemDefinition item, GridPoint point)
    {
        if (!IsInReach(point))
        {
            PostMessage("That placement is out of reach.");
            return;
        }
        var result = _placement.TryPlaceWall(World, point, item.PlacedWall, actorContext: new PlacementActorContext(PlayerCenter, PlacementReachRadius,
            RequireLineOfSight: false), tileCatalog: Tiles);
        if (!result.Success)
        {
            PostMessage($"Cannot place wall: {result.Failure}.");
            return;
        }
        Player.Inventory.TryConsume(item.Id);
        SoundEvents.Emit(SoundEventKind.Place, "wall", CellCenter(point), item.PlacedWall);
        SetPlayerCooldown(CombatTuning.PlacementTicks(WallPlaceTicks, GearTagValue("build")));
    }

    /// <summary>
    /// Demolisher use (ToolParityUse.cs): hearts and altars take its power; a solid block or platform is reshaped once
    /// per press; otherwise (or while held) the background wall takes (int)(power × 1.5). Crafted walls drop their item.
    /// </summary>
    private void Demolish(ItemDefinition item, GridPoint point) => UseDemolisher(item, point, item.ToolPower);

    private enum WallHammerResult { Refused, Damaged, Removed }

    /// <summary>
    /// Hammers the wall at <paramref name="point"/> if a demolisher may hit it (<see cref="ToolParity.CanSmashWall"/>): each
    /// hit adds (int)(power x 1.5) toward 100 and the wall comes down at 100 (a power of 0 knocks it out at once).
    /// </summary>
    private WallHammerResult HammerWall(GridPoint point, int toolPower = 0)
    {
        var wall = World.GetCell(point).WallId;
        if (wall == WallIds.None)
        {
            PostMessage("There is no wall there.");
            return WallHammerResult.Refused;
        }
        if (!ToolParity.CanSmashWall(World, point))
        {
            PostMessage("Work in from an exposed edge of the wall.");
            return WallHammerResult.Refused;
        }
        var hit = toolPower > 0 ? CombatTuning.WallHammerDamage(toolPower) : CombatTuning.TileBreakDamage;
        var total = _hitLedger.Add(point, HitLedger.Layer.Wall, wall, hit);
        SoundEvents.Emit(total < CombatTuning.TileBreakDamage ? SoundEventKind.Dig : SoundEventKind.Break, "wall", CellCenter(point), wall);
        AgeHitLedger();
        if (total < CombatTuning.TileBreakDamage)
        {
            PostMessage($"Hit the {WallIds.Name(wall).ToLowerInvariant()} ({total}/{CombatTuning.TileBreakDamage}).");
            return WallHammerResult.Damaged;
        }
        _hitLedger.Clear(point, HitLedger.Layer.Wall);
        World.SetWall(point, WallIds.None);
        if (ContentTiles.WallItemFor(wall) is { } refund)
            SpawnGroundItem(refund, 1, new Vector2(point.X + 0.5f, point.Y + 0.5f));
        PostMessage($"Demolished the {WallIds.Name(wall).ToLowerInvariant()}.");
        return WallHammerResult.Removed;
    }

    // ------------------------------------------------------------------ mining rules (parity B, docs/TOOLS_PARITY.md)

    /// <summary>Cells hit by a tool (kept so other systems can forget a cell when it breaks). Damage itself lives in the hit ledger.</summary>
    private readonly Dictionary<GridPoint, float> _tileHits = new();

    /// <summary>Base ticks to place one block or one wall before placement-speed bonuses (the reference's block and wall use times).</summary>
    public const int BlockPlaceTicks = ToolParity.BlockUseTime;
    public const int WallPlaceTicks = ToolParity.WallUseTime;

    /// <summary>Drill hardness of a tile (see <see cref="CombatTuning.DrillHardness"/>).</summary>
    private int DrillHardness(TileId tile) =>
        CombatTuning.DrillHardness(tile, Tiles[tile].MiningResistance, ContentTiles.Has(tile, TileTraits.Fragile));

    /// <summary>Mining-speed multiplier: 1 - "mining" bonus %, floored at 0.3.</summary>
    public float PickSpeed => CombatTuning.PickSpeed(GearTagValue("mining"));

    /// <summary>Hit damage a block has taken so far (0-99).</summary>
    public int TileHitDamage(GridPoint point)
    {
        if (!World.Contains(point))
            return 0;
        var cell = World.GetCell(point);
        if (_hitLedger.Contains(point, HitLedger.Layer.Tile))
            return _hitLedger.Damage(point, HitLedger.Layer.Tile, (ushort)cell.TileId);
        // Damage from other sources (tantrums, older saves) is kept in hit points; read it back as a share of 100.
        int hp = Tiles[cell.TileId].HitPoints;
        return hp == 0 || cell.Damage == 0 ? 0 : Math.Min(CombatTuning.TileBreakDamage - 1, (cell.Damage * CombatTuning.TileBreakDamage + hp - 1) / hp);
    }

    /// <summary>Hit damage a wall has taken so far (0-99).</summary>
    public int WallHitDamage(GridPoint point) =>
        World.Contains(point) ? _hitLedger.Damage(point, HitLedger.Layer.Wall, World.GetCell(point).WallId) : 0;

    /// <summary>
    /// Adds hit damage to a block in the hit ledger. At 100 or more it breaks (true, with the broken tile); otherwise the
    /// cell's crack damage is set to the same share of its hit points so the cracks show progress. Every damaging hit
    /// ages the other ledger entries (<see cref="HitLedger.Prune"/>).
    /// </summary>
    private bool AddTileHitDamage(GridPoint point, int damage, out TileId brokenTile)
    {
        brokenTile = TileId.Air;
        if (damage <= 0 || !World.Contains(point))
            return false;
        var cell = World.GetCell(point);
        var tile = cell.TileId;
        int hp = Tiles[tile].HitPoints;
        if (hp == 0)
            return false;
        if (!_hitLedger.Contains(point, HitLedger.Layer.Tile) && TileHitDamage(point) is var earlier and > 0)
            _hitLedger.Add(point, HitLedger.Layer.Tile, (ushort)tile, earlier); // damage from other sources carries over
        var total = _hitLedger.Add(point, HitLedger.Layer.Tile, (ushort)tile, damage);
        EmitDig(point, tile); // PE-4/PE-7: every damaging tool hit digs and kicks up dust (Systems/SoundEvents.cs)
        if (total >= CombatTuning.TileBreakDamage)
        {
            _hitLedger.Clear(point, HitLedger.Layer.Tile);
            var broke = World.TryDamageTile(point, byte.MaxValue, Tiles, out brokenTile);
            if (broke) EmitBreak(point, brokenTile);
            AgeHitLedger();
            return broke;
        }
        AgeHitLedger();
        ShowCracks(point, TileHitDamage(point));
        return false;
    }

    /// <summary>
    /// Shared pre-hit rules for drills and cutters: drills can't hit trunks, cactus or logs (cutters) or hearts
    /// (demolishers), and containers holding items can't be broken. False (with a message and the swing spent) when the
    /// hit is refused.
    /// </summary>
    private bool PrepareMiningHit(GridPoint point, TileId tile, ItemDefinition item)
    {
        if (item.UseKind == ItemUseKind.Pickaxe && ToolParity.IsCutterTile(tile))
        {
            PostMessage(TilePhysics.IsTreePart(tile)
                ? "A drill can't cut a trunk. Use a cutter to fell the tree."
                : $"A drill can't cut the {Tiles[tile].Name.ToLowerInvariant()}. Use a cutter.");
            SetPlayerCooldown(DrillHitTicks(item));
            return false;
        }
        if (item.UseKind == ItemUseKind.Pickaxe && BlightRules.IsHeart(tile))
        {
            PostMessage($"A drill glances off the {Tiles[tile].Name.ToLowerInvariant()}. Smash it with a demolisher.");
            SetPlayerCooldown(DrillHitTicks(item));
            return false;
        }
        if (ContainerHoldsItems(point, tile))
        {
            PostMessage($"Empty the {Tiles[tile].Name.ToLowerInvariant()} before breaking it.");
            SetPlayerCooldown(item.UseKind == ItemUseKind.Pickaxe ? DrillHitTicks(item) : item.UseCooldownTicks);
            return false;
        }
        _tileHits[point] = 0f;
        return true;
    }

    /// <summary>True when a container tile still holds items (or unopened loot).</summary>
    public bool ContainerHoldsItems(GridPoint point, TileId tile)
    {
        if (!ContentTiles.Has(tile, TileTraits.Container))
            return false;
        if (_containers.TryGetValue(point, out var inventory) && inventory.Slots.Any(stack => !stack.IsEmpty))
            return true;
        return !_seededContainers.Contains(point) &&
               (ContentTiles.Has(tile, TileTraits.Locked) || tile is TileId.CoastalHabPod or TileId.FungalHabRuin ||
                PhysicsContainerLoot(tile).Length > 0);
    }

    /// <summary>True while an open container at <paramref name="cell"/> is still within the player's reach (UI closes it otherwise).</summary>
    public bool ContainerInReach(GridPoint cell) =>
        World.Contains(cell) && ContentTiles.Has(World.GetCell(cell).TileId, TileTraits.Container) &&
        Vector2.Distance(PlayerCenter, new Vector2(cell.X + 0.5f, cell.Y + 0.5f)) <= PlayerReach + GearReachBonus + 1f;

    /// <summary>
    /// Partly broken tiles and walls do not mend with time: they mend as further hits age the hit ledger
    /// (<see cref="AgeHitLedger"/>, docs/TOOLS_PARITY.md). Kept as the per-tick hook the physics step calls; it only drops
    /// ledger entries whose tile or wall has gone some other way.
    /// </summary>
    private void UpdateTileDamageHealing(float deltaSeconds)
    {
        if (_tileHits.Count > 256)
            foreach (var point in _tileHits.Keys.Where(point => !World.Contains(point) || World.GetCell(point).TileId == TileId.Air).ToArray())
            {
                _tileHits.Remove(point);
                _hitLedger.Clear(point, HitLedger.Layer.Tile);
            }
    }

    // ------------------------------------------------------------------ block swapping and block placement (parity B6)

    /// <summary>
    /// Placing a block onto a different plain block swaps them when a drill in the pack could mine the old one:
    /// the old block pops out as an item. True when it handled the use (swapped or refused).
    /// </summary>
    private bool TrySwapBlock(ItemDefinition item, GridPoint point)
    {
        if (!World.Contains(point) || !Tiles.Contains(item.PlacedTile) || !Tiles[item.PlacedTile].IsSolid ||
            ContentTiles.Has(item.PlacedTile, TileTraits.Door))
            return false;
        var old = World.GetCell(point).TileId;
        if (old == TileId.Air || old == item.PlacedTile || !Tiles[old].IsSolid || old == TileId.Bedrock)
            return false;
        // Only plain blocks (their drop places the same tile back) swap; furniture, doors and special tiles do not.
        if (TileDropTable.GetDrop(old) is not { } drop || !Items.Contains(drop) || Items[drop].PlacedTile != old ||
            ContentTiles.TryGet(old, out var content) && (content.Furniture || content.Traits != TileTraits.None))
            return false;
        if (Player.ActionCooldownTicks > 0)
            return true;
        if (!IsInReach(point))
        {
            PostMessage("That placement is out of reach.");
            return true;
        }
        var drill = Items.All.Where(definition => definition.UseKind == ItemUseKind.Pickaxe && Player.Inventory.Count(definition.Id) > 0)
            .Select(definition => definition.ToolPower).DefaultIfEmpty(0).Max();
        if (drill <= 0 || drill < Tiles[old].MinimumToolPower)
        {
            PostMessage($"Cannot swap: {Tiles[old].Name} needs a stronger drill.");
            return true;
        }
        if (!Player.Inventory.TryConsume(item.Id))
            return true;
        World.SetTile(point, item.PlacedTile);
        _tileHits.Remove(point);
        _hitLedger.Clear(point, HitLedger.Layer.Tile);
        SpawnGroundItem(drop, 1, new Vector2(point.X + 0.5f, point.Y + 0.5f));
        _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, new[] { point }));
        SetPlayerCooldown(6);
        PostMessage($"Swapped {Tiles[old].Name} for {item.Name}.");
        return true;
    }

    /// <summary>Solid blocks need a neighbouring tile or a wall behind them, and can't be placed inside a creature.</summary>
    private bool BlockPlacementRefused(ItemDefinition item, GridPoint point)
    {
        if (!World.Contains(point) || !Tiles.Contains(item.PlacedTile) || !Tiles[item.PlacedTile].IsSolid ||
            World.GetCell(point).TileId != TileId.Air || ContentTiles.Has(item.PlacedTile, TileTraits.Door) || !IsInReach(point))
            return false;
        var cell = World.GetCell(point);
        bool Filled(int x, int y) => World.Contains(x, y) && World.TileAt(x, y) != TileId.Air;
        if (cell.WallId == WallIds.None && !Filled(point.X - 1, point.Y) && !Filled(point.X + 1, point.Y) &&
            !Filled(point.X, point.Y - 1) && !Filled(point.X, point.Y + 1))
        {
            PostMessage("Cannot place: blocks need a neighbouring block or a wall behind them.");
            return true;
        }
        var box = new Vector2(point.X, point.Y);
        if (_enemies.Any(enemy => enemy.IsAlive && Overlaps(enemy.Motion, box, Vector2.One)) ||
            _townNpcs.Any(npc => Overlaps(npc.Motion, box, Vector2.One)))
        {
            PostMessage("Cannot place: something is in the way.");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Cutters (axes) hit only cutter tiles: trunks and crowns, cactus and fallen logs (<see cref="ToolParity.IsCutterTile"/>).
    /// A swing at anything else does nothing and costs no time. Each cut adds (int)(power / 5 × 1.2) toward 100 (×3 on
    /// cactus); at 100 the segment (and the tree above it) falls.
    /// </summary>
    private void Cut(ItemDefinition item, GridPoint point)
    {
        if (Player.ActionCooldownTicks > 0)
            return;
        if (!IsInReach(point))
        {
            PostMessage("That is out of reach.");
            return;
        }
        var tile = World.GetCell(point).TileId;
        if (tile == TileId.Air || !ToolParity.IsCutterTile(tile))
        {
            PostMessage("Cutters only fell trees, cactus and logs.");
            return;
        }
        if (!PrepareMiningHit(point, tile, item))
            return;
        OnCutterHitFlora(point, tile, item); // flora parity: shakes the tree (once a day) and remembers the axe for fell drops
        var damage = ToolParity.CanBreakUnder(World, point) ? CombatTuning.CutterDamage(item.ToolPower, tile == TileId.Cactus) : 0;
        if (AddTileHitDamage(point, damage, out var broken) && !HandleBrokenTile(point, broken))
            PostMessage($"Cut {Tiles[broken].Name}.");
        SetPlayerCooldown(item.UseCooldownTicks); // axes and saws run on their use time (mining speed is for drills only)
    }

    private void SwingBugNet(ItemDefinition item, GridPoint point)
    {
        var target = new Vector2(point.X + 0.5f, point.Y + 0.5f);
        var critter = _enemies.FirstOrDefault(enemy => enemy.IsAlive && enemy.Archetype.Catchable &&
            Vector2.Distance(enemy.Motion.Position + enemy.Motion.Size / 2f, target) <= 1.6f &&
            Vector2.Distance(enemy.Motion.Position + enemy.Motion.Size / 2f, PlayerCenter) <= PlayerReach + 1f);
        SetPlayerCooldown(ToolParity.Profile(item).UseTime); // the net swings every 25 ticks
        if (critter is null || !Enum.TryParse<ItemId>(critter.Kind.ToString(), out var caught) || !Items.Contains(caught))
        {
            PostMessage("The net swishes through empty air.");
            return;
        }
        if (Player.Inventory.Add(caught, 1) == 0)
        {
            PostMessage("No room for the critter.");
            return;
        }
        _enemies.Remove(critter);
        RecordDeath(critter, DeathReason.Caught); // PE-5: a catch is not a kill
        SoundEvents.Emit(SoundEventKind.Catch, string.Empty, critter.Motion.Position + critter.Motion.Size / 2f, (int)critter.Kind);
        PostMessage($"Caught a {critter.DisplayName}!");
    }

    private void ReleaseCritter(ItemDefinition item, GridPoint point)
    {
        if (item.ReleasesCritter is not { } kind || !IsInReach(point) || Tiles[World.GetCell(point).TileId].IsSolid)
        {
            PostMessage("Release the critter on open ground nearby.");
            return;
        }
        Player.Inventory.TryConsume(item.Id);
        SpawnCreature(kind, new Vector2(point.X, point.Y));
        SetPlayerCooldown(8);
    }

    private void UseUtility(ItemDefinition item, GridPoint point)
    {
        if (UsePhysicsItem(item, point))
            return;
        if (item.FishingPower > 0)
        {
            CastOrReel(item, point);
            return;
        }
        switch (item.Id)
        {
            case ItemId.EmptyBucket:
            {
                var cell = World.Contains(point) ? World.GetCell(point) : default;
                if (!IsInReach(point) || cell.LiquidDepth < 0.5f || cell.LiquidKind is not (LiquidKind.Water or LiquidKind.Lava or LiquidKind.Fuel))
                {
                    PostMessage("Scoop from a pool of water, fuel or liquid uranium.");
                    return;
                }
                var full = cell.LiquidKind switch
                {
                    LiquidKind.Lava => ItemId.BucketLava,
                    LiquidKind.Fuel => ItemId.BucketFuel,
                    _ => ItemId.BucketWater
                };
                if (!Player.Inventory.TryConsume(item.Id) || Player.Inventory.Add(full, 1) == 0)
                    return;
                cell.LiquidDepth = Math.Max(0f, cell.LiquidDepth - 1f);
                if (cell.LiquidDepth <= 0f)
                    cell.LiquidKind = LiquidKind.None;
                World.SetCell(point, cell);
                PostMessage($"Filled a {Items[full].Name.ToLowerInvariant()}.");
                break;
            }
            case ItemId.BucketWater or ItemId.BucketLava or ItemId.BucketFuel:
                if (!IsInReach(point) || !World.TryAddLiquid(point, item.Id switch
                    {
                        ItemId.BucketLava => LiquidKind.Lava,
                        ItemId.BucketFuel => LiquidKind.Fuel,
                        _ => LiquidKind.Water
                    }, 1f, Tiles))
                {
                    PostMessage("The liquid has nowhere to go there.");
                    return;
                }
                Player.Inventory.TryConsume(item.Id);
                Player.Inventory.Add(ItemId.EmptyBucket, 1);
                break;
            case ItemId.PaintSprayer:
            {
                if (!IsInReach(point))
                    return;
                var tile = World.GetCell(point).TileId;
                TileId? next = tile switch
                {
                    TileId.HullPlatingGrey => TileId.HullPlatingBlue,
                    TileId.HullPlatingBlue => TileId.HullPlatingGreen,
                    TileId.HullPlatingGreen => TileId.HullPlatingRed,
                    TileId.HullPlatingRed => TileId.HullPlatingHazard,
                    TileId.HullPlatingHazard => TileId.HullPlatingGrey,
                    _ => null
                };
                if (next is not { } painted)
                {
                    PostMessage("The sprayer only recolours hull plating.");
                    return;
                }
                World.SetTile(point, painted);
                _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, new[] { point }));
                break;
            }
            case ItemId.WireTool:
                // A wire tool toggles doors from twice the normal reach.
                if (World.Contains(point) && Vector2.Distance(PlayerCenter, new Vector2(point.X + 0.5f, point.Y + 0.5f)) <= PlayerReach * 2f &&
                    ContentTiles.ToggledDoor(World.GetCell(point).TileId) is not null)
                    ToggleDoor(point);
                else
                    PostMessage("The wire tool finds no door to trigger.");
                break;
            case ItemId.GlowSticks:
                PostMessage("You crack a glow stick.");
                Player.Inventory.TryConsume(item.Id);
                break;
            case ItemId.TeleportBeacon:
                RecallHome(); // 0.7.2: home is your bed, else the base
                break;
            case ItemId.CompanionPermit:
                // Filing happens on its own while carried; using it files it now or says why it cannot.
                if (!FileCompanionPermit())
                    PostMessage("No companion can move in: every town pet already lives here, or no house is free.");
                break;
            default:
                if (UseBatchBUtility(item, point))
                    return;
                NoDirectUse();
                break;
        }
        SetPlayerCooldown(Math.Max(6, item.UseCooldownTicks));
    }

    /// <summary>Uses that reached no handler (the item audit's probe reads this; see Systems/ItemAudit.cs).</summary>
    public int UnhandledUses { get; private set; }

    private void NoDirectUse()
    {
        UnhandledUses++;
        PostMessage("That item has no direct use.");
    }

    /// <summary>
    /// 0.7.2: recall items take you home: beside your bed when you have one, else to the base (the station AI's home), else
    /// to the landing site. Returns where you arrived.
    /// </summary>
    public string RecallHome()
    {
        Player.Motion.Velocity = Vector2.Zero;
        if (BedSpawnPosition() is { } bed)
        {
            Player.Motion.Position = bed;
            PostMessage("Space folds and you step out beside your bed.");
            return "your bed";
        }
        if (_townNpcs.FirstOrDefault(npc => npc.Kind == TownNpcKind.StationAi && !npc.IsBound && npc.HomeCell is not null) is { } ai)
        {
            var column = Math.Clamp((int)ai.HomePosition.X, 2, World.Width - 3);
            Player.Motion.Position = StandingAt(new GridPoint(column, ai.HomeCell!.Value.Y));
            LiftClear(Player.Motion);
            PostMessage("Space folds and you step out at the base.");
            return "the base";
        }
        RespawnPlayerAtLanding();
        return "the landing site";
    }

    private void RespawnPlayerAtLanding()
    {
        var column = Math.Clamp(SpawnColumn == 0 ? World.Width / 2 : SpawnColumn, 2, World.Width - 3);
        Player.Motion.Position = new Vector2(column, FindSurface(column) - Player.Motion.Size.Y);
        Player.Motion.Velocity = Vector2.Zero;
        PostMessage("The beacon folds space and you step out at the landing site.");
    }

    // ------------------------------------------------------------------ interaction

    /// <summary>
    /// Interacts with whatever is at <paramref name="target"/>: toggles doors, opens containers (vault
    /// chests need the vault key), frees bound survivors, unlocks the precursor vault door, or talks to a
    /// town NPC standing there.
    /// </summary>
    public InteractionResult Interact(GridPoint target)
    {
        if (!World.Contains(target) || Vector2.Distance(PlayerCenter, new Vector2(target.X + 0.5f, target.Y + 0.5f)) > PlayerReach + 1f)
            return Report(new InteractionResult(WorldInteractionKind.None, "That is out of reach."));
        if (InteractLeisure(target) is { } leisure) return Report(leisure); // pets, audio decks (GameSimulation.Leisure.cs)

        var point = new Vector2(target.X + 0.5f, target.Y + 0.5f);
        var npc = _townNpcs.FirstOrDefault(candidate =>
            Vector2.Distance(candidate.Motion.Position + candidate.Motion.Size / 2f, point) <= 1.4f);
        if (npc is not null)
        {
            if (npc.IsBound)
            {
                FreeSurvivor(npc);
                return Report(new InteractionResult(WorldInteractionKind.SurvivorFreed,
                    $"You cut {npc.Name} free. They will move into the colony once a home is ready.", Npc: npc));
            }
            return Report(new InteractionResult(WorldInteractionKind.NpcTalk, $"{npc.Name}: \"{GetNpcDialogue(npc)}\"", Npc: npc));
        }

        var tile = World.GetCell(target).TileId;
        if (ContentTiles.ToggledDoor(tile) is not null)
        {
            return ToggleDoor(target)
                ? Report(new InteractionResult(WorldInteractionKind.DoorToggled, $"{Tiles[World.GetCell(target).TileId].Name}.", Cell: target))
                : Report(new InteractionResult(WorldInteractionKind.None, "Something is in the doorway."));
        }
        if (tile == TileId.PrecursorVaultDoor)
        {
            if (Player.Inventory.Count(ItemId.VaultKey) == 0)
                return Report(new InteractionResult(WorldInteractionKind.ContainerLocked, "The vault door is sealed. A precursor key would fit the glyph lock."));
            ClearVertical(target, TileId.PrecursorVaultDoor);
            Flags.Set("vault.opened");
            return Report(new InteractionResult(WorldInteractionKind.VaultUnlocked, "The vault key turns. The precursor door grinds open.", Cell: target));
        }
        if (ContentTiles.Has(tile, TileTraits.Container))
        {
            if (ContentTiles.Has(tile, TileTraits.Locked) && !_seededContainers.Contains(target))
            {
                if (Player.Inventory.Count(ItemId.VaultKey) == 0)
                    return Report(new InteractionResult(WorldInteractionKind.ContainerLocked, "The vault chest is locked."));
            }
            var container = GetContainer(target)!;
            SoundEvents.Emit(SoundEventKind.ChestOpen, string.Empty, CellCenter(target), (int)tile);
            return Report(new InteractionResult(WorldInteractionKind.ContainerOpened, $"Opened the {Tiles[tile].Name.ToLowerInvariant()}.",
                container, Cell: target));
        }
        if (TravelContent.IsPylon(tile)) // 0.7.6: a relay: charge it from your fuel crystals, or read its state
            return Report(new InteractionResult(WorldInteractionKind.None, ChargeRelay(target), Cell: target));
        if (TravelContent.IsGlideWalk(tile) && TryReverseGlideWalk(target))
            return Report(new InteractionResult(WorldInteractionKind.None, "The glide-walk reverses.", Cell: target));
        if (TryInteractWorldGen(target, tile, out var worldGenResult))
            return Report(worldGenResult);
        if (TryInteractPhysics(target, out var physicsResult))
            return Report(physicsResult);
        if (InteractBatchB(target, tile) is { } batchB)
            return Report(batchB);
        return Report(new InteractionResult(WorldInteractionKind.None, "Nothing to interact with."));

        InteractionResult Report(InteractionResult result)
        {
            PostMessage(result.Message);
            return result;
        }
    }

    /// <summary>The inventory of a container tile (created and, for world loot containers, filled on first access).</summary>
    public Inventory? GetContainer(GridPoint cell)
    {
        if (!World.Contains(cell) || !ContentTiles.Has(World.GetCell(cell).TileId, TileTraits.Container))
            return null;
        if (!_containers.TryGetValue(cell, out var inventory))
        {
            inventory = new Inventory(Items, ContainerSlots);
            _containers[cell] = inventory;
        }
        if (_seededContainers.Add(cell))
        {
            SeedContainerLoot(World.GetCell(cell).TileId, inventory);
            ImproveChestLoot(cell, inventory);
            GrowthChestLoot(cell, inventory); // character growth: rare salvaged thruster fins
        }
        return inventory;
    }

    /// <summary>Containers that exist in the world, for the UI and saves.</summary>
    public IReadOnlyDictionary<GridPoint, Inventory> Containers => _containers;

    private void SeedContainerLoot(TileId tile, Inventory inventory)
    {
        var table = ContainerLootTable(tile);
        foreach (var (item, min, max) in table)
        {
            var count = _lootRandom.Next(min, max + 1);
            if (count > 0)
                inventory.Add(item, count);
        }
        if (tile == TileId.LockedVaultChest && Player.Inventory.Count(ItemId.VaultKey) > 0)
            Flags.Increment("vault.chests.opened");
    }

    /// <summary>The loot table a world container is seeded from on first access (empty for player-built storage).</summary>
    internal static (ItemId Item, int Min, int Max)[] ContainerLootTable(TileId tile) =>
        tile switch
        {
            TileId.LockedVaultChest =>
            [
                (ItemId.PrecursorRelic, 1, 1), (ItemId.AncientPowerCell, 1, 2), (ItemId.LumeniteBar, 5, 12),
                (ItemId.VoidEssence, 2, 5), (ItemId.AstralHalo, 0, 1), (ItemId.Credits, 300, 900)
            ],
            TileId.CoastalHabPod =>
            [
                (ItemId.CoralShard, 2, 6), (ItemId.TideFish, 1, 4), (ItemId.HarpoonGun, 0, 1), (ItemId.ExplosiveCharge, 5, 20),
                (ItemId.CoralJellyFamiliar, 0, 1), (ItemId.Credits, 20, 80)
            ],
            TileId.FungalHabRuin =>
            [
                (ItemId.GlowSpore, 3, 8), (ItemId.MushroomMace, 0, 1), (ItemId.SporeCenser, 0, 1), (ItemId.BioGel, 2, 6),
                (ItemId.Credits, 20, 80)
            ],
            _ => PhysicsContainerLoot(tile)
        };

    /// <summary>Toggles a door and every same-family door cell stacked with it. False when an actor blocks the closing door.</summary>
    public bool ToggleDoor(GridPoint cell)
    {
        var tile = World.GetCell(cell).TileId;
        if (ContentTiles.ToggledDoor(tile) is not { } toggled)
            return false;
        var cells = new List<GridPoint> { cell };
        for (var dy = -1; World.Contains(cell.X, cell.Y + dy) && World.GetCell(cell.X, cell.Y + dy).TileId == tile && cells.Count < 4; dy--)
            cells.Add(new GridPoint(cell.X, cell.Y + dy));
        for (var dy = 1; World.Contains(cell.X, cell.Y + dy) && World.GetCell(cell.X, cell.Y + dy).TileId == tile && cells.Count < 4; dy++)
            cells.Add(new GridPoint(cell.X, cell.Y + dy));
        if (Tiles[toggled].IsSolid)
        {
            foreach (var door in cells)
            {
                var box = new Vector2(door.X, door.Y);
                if (ActorOverlaps(Player.Motion, box) ||
                    _townNpcs.Any(npc => ActorOverlaps(npc.Motion, box)))
                    return false;
            }
        }
        foreach (var door in cells)
            World.SetTile(door, toggled);
        SoundEvents.Emit(Tiles[toggled].IsSolid ? SoundEventKind.DoorClose : SoundEventKind.DoorOpen, string.Empty, CellCenter(cell), (int)toggled);
        _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, cells));
        return true;

        static bool ActorOverlaps(PawnMotionState motion, Vector2 cellOrigin) =>
            motion.Position.X < cellOrigin.X + 1f && motion.Position.X + motion.Size.X > cellOrigin.X &&
            motion.Position.Y < cellOrigin.Y + 1f && motion.Position.Y + motion.Size.Y > cellOrigin.Y;
    }

    /// <summary>Clears a vertical run of one tile (doors, vault doors) through <paramref name="cell"/>.</summary>
    private List<GridPoint> ClearVertical(GridPoint cell, TileId tile)
    {
        var cleared = new List<GridPoint>();
        for (var y = cell.Y; World.Contains(cell.X, y) && World.GetCell(cell.X, y).TileId == tile; y--)
            cleared.Add(new GridPoint(cell.X, y));
        for (var y = cell.Y + 1; World.Contains(cell.X, y) && World.GetCell(cell.X, y).TileId == tile; y++)
            cleared.Add(new GridPoint(cell.X, y));
        foreach (var point in cleared)
            World.SetTile(point, TileId.Air);
        _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, cleared));
        return cleared;
    }

    /// <summary>Content tile breakage: door frames, container contents. True when it handled drops itself.</summary>
    private bool HandleBrokenContentTile(GridPoint point, TileId broken)
    {
        if (ContentTiles.ToggledDoor(broken) is { } other)
        {
            // Clear the rest of the frame (either state) and drop a single door item.
            foreach (var family in new[] { broken, other })
            {
                for (var dy = -3; dy <= 3; dy++)
                {
                    var cell = new GridPoint(point.X, point.Y + dy);
                    if (World.Contains(cell) && World.GetCell(cell).TileId == family)
                        World.SetTile(cell, TileId.Air);
                }
            }
            if (TileDropTable.GetDrop(broken) is { } door)
                BreakDrop(point, door, 1);
            return true;
        }
        if (ContentTiles.Has(broken, TileTraits.Container))
        {
            var inventory = GetContainerForBreak(point, broken);
            if (inventory is not null)
                foreach (var stack in inventory.Slots.Where(stack => !stack.IsEmpty))
                    CollectOrDrop(stack.ItemId, stack.Count, new Vector2(point.X + 0.5f, point.Y + 0.5f), stack.Calibration);
            _containers.Remove(point);
            _seededContainers.Remove(point);
            if (TileDropTable.GetDrop(broken) is { } box)
                BreakDrop(point, box, 1);
            return true;
        }
        if (broken == TileId.BloomHive)
        {
            Flags.Increment("hive.broken");
            if (BossAwaitsFracture(EnemyKind.BloomMatriarch))
                PostMessage("The hive splits open, but its matriarch sleeps on. She will not wake before the Fracture.");
            else if (TrySpawnBoss(EnemyKind.BloomMatriarch, BossSpawnSource.WorldTrigger, out var boss, out _))
                PostMessage($"The hive splits open. The {boss!.DisplayName} emerges, furious!");
            return true;
        }
        if (broken == TileId.CrystalAltar)
        {
            BreakDrop(point, ItemId.PrismShard, 3);
            if (BossAwaitsFracture(EnemyKind.PrismEmpress))
                PostMessage("The altar's light gutters out. Whatever it served will not answer before the Fracture.");
            else
                TrySpawnBoss(EnemyKind.PrismEmpress, BossSpawnSource.WorldTrigger, out _, out _);
            return true;
        }
        return false;
    }

    private Inventory? GetContainerForBreak(GridPoint point, TileId broken)
    {
        if (_containers.TryGetValue(point, out var existing))
            return existing;
        if (ContentTiles.Has(broken, TileTraits.Locked) || broken is TileId.CoastalHabPod or TileId.FungalHabRuin)
        {
            var inventory = new Inventory(Items, ContainerSlots);
            SeedContainerLoot(broken, inventory);
            return inventory;
        }
        return null;
    }

    private ContainerSnapshot[] CaptureContainers() => _containers
        .Where(pair => pair.Value.Slots.Any(stack => !stack.IsEmpty) || _seededContainers.Contains(pair.Key))
        .Select(pair => new ContainerSnapshot(pair.Key.X, pair.Key.Y, pair.Value.Slots.ToArray(), _seededContainers.Contains(pair.Key)))
        .ToArray();

    private void RestoreContainers(IEnumerable<ContainerSnapshot>? containers)
    {
        foreach (var saved in containers ?? Array.Empty<ContainerSnapshot>())
        {
            var cell = new GridPoint(saved.X, saved.Y);
            if (!World.Contains(cell) || saved.Slots is null || saved.Slots.Length != ContainerSlots)
                continue;
            var inventory = new Inventory(Items, ContainerSlots);
            if (!inventory.RestoreState(saved.Slots, 0))
                throw new InvalidDataException("A saved container is invalid.");
            _containers[cell] = inventory;
            if (saved.Seeded)
                _seededContainers.Add(cell);
        }
    }
}
