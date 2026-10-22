using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Tool use that follows the reference (docs/TOOLS_PARITY.md): the swing and the press/hold state, per-tool reach, the
/// hit ledger, the demolisher (hearts and altars, the shape cycle that needs a fresh press, then walls), and the
/// wire, bucket, paint and shaping-hammer utilities.
/// </summary>
public sealed partial class GameSimulation
{
    private readonly HitLedger _hitLedger = new();
    /// <summary>True when the previous tick asked to use the held item (the button is being held, not freshly pressed).</summary>
    private bool _useHeldLastTick;
    /// <summary>Set when the use button is released; a demolisher pound needs it (one pound per press).</summary>
    private bool _poundRelease = true;
    private int _toolSwingTicks;
    private int _toolSwingLength;

    /// <summary>Ticks left in the current swing of the held tool (0 when it is not swinging).</summary>
    public int ToolSwingTicksRemaining => _toolSwingTicks;

    /// <summary>Length in ticks of the current swing (the tool's swing time).</summary>
    public int ToolSwingLength => _toolSwingLength;

    /// <summary>0→1 progress through the current tool swing, or null when no tool is swinging.</summary>
    public float? ToolSwingProgress => _toolSwingTicks > 0 && _toolSwingLength > 0 ? 1f - _toolSwingTicks / (float)_toolSwingLength : null;

    /// <summary>The reach box (cells, inclusive) for <paramref name="item"/>, or the held item when null.</summary>
    public (int MinX, int MinY, int MaxX, int MaxY) ToolReachBox(ItemDefinition? item = null)
    {
        var motion = Player.Motion;
        return ToolParity.ReachBox(motion.Position.X, motion.Position.Y, motion.Size.X, motion.Size.Y,
            (int)MathF.Floor(GearReachBonus), ToolParity.TileBoost(item ?? Player.Inventory.SelectedItem));
    }

    /// <summary>True when <paramref name="cell"/> is inside the held item's reach box.</summary>
    public bool IsInToolReach(GridPoint cell) => World.Contains(cell) && ToolParity.InBox(ToolReachBox(), cell.X, cell.Y);

    /// <summary>
    /// Runs once per tick before the player's action: counts the swing down, starts a new swing on a press (or while held
    /// for tools that repeat on their own), and remembers releases, which re-arm the demolisher's pound.
    /// </summary>
    private void UpdateToolUse(PlayerAction action)
    {
        if (_toolSwingTicks > 0)
            _toolSwingTicks--;
        var usingItem = action is PlayerAction.Mine or PlayerAction.UseSelected && !Player.IsDowned;
        if (!usingItem)
        {
            _useHeldLastTick = false;
            _poundRelease = true;
            return;
        }
        if (Player.Inventory.SelectedItem is { } item && _toolSwingTicks == 0)
        {
            var profile = ToolParity.Profile(item);
            if (!_useHeldLastTick || profile.AutoReuse || profile.Channel)
                _toolSwingTicks = _toolSwingLength = profile.UseAnimation;
        }
        _useHeldLastTick = true;
    }

    /// <summary>Ticks a drill waits between hits: channel drills run on their own use time, picks scale it by mining speed.</summary>
    private int DrillHitTicks(ItemDefinition item)
    {
        var ticks = ToolParity.IsChannelTool(item.Id)
            ? Math.Max(1, item.UseCooldownTicks)
            : CombatTuning.DrillUseTicks(item.UseCooldownTicks, PickSpeed);
        var skill = Math.Max(0.1f, Player.MiningSpeedMultiplier);
        return Math.Max(1, (int)MathF.Round(ticks / skill, MidpointRounding.ToEven));
    }

    /// <summary>A drill's hit time (kept under its earlier name for the test probes).</summary>
    private int DrillCooldownTicks(ItemDefinition item) => DrillHitTicks(item);

    // ------------------------------------------------------------------ hit ledger

    /// <summary>
    /// After a hit that did damage, every other partly broken tile and wall ages one hit and mends a little once it is old
    /// enough; the crack drawing follows.
    /// </summary>
    private void AgeHitLedger()
    {
        foreach (var (point, layer, damage) in _hitLedger.Prune(StillThere))
            if (layer == HitLedger.Layer.Tile)
                ShowCracks(point, damage);
    }

    private bool StillThere(GridPoint point, HitLedger.Layer layer, ushort kind) =>
        World.Contains(point) && (layer == HitLedger.Layer.Tile
            ? (ushort)World.GetCell(point).TileId == kind && World.GetCell(point).TileId != TileId.Air
            : World.GetCell(point).WallId == kind);

    /// <summary>Sets a cell's crack damage to the same share of its hit points as <paramref name="damage"/> is of 100.</summary>
    private void ShowCracks(GridPoint point, int damage)
    {
        if (!World.Contains(point))
            return;
        var cell = World.GetCell(point);
        var hp = Tiles.Contains(cell.TileId) ? Tiles[cell.TileId].HitPoints : 0;
        if (hp == 0)
            return;
        var shown = damage <= 0 ? 0 : Math.Clamp(damage * hp / CombatTuning.TileBreakDamage, 1, Math.Max(1, hp - 1));
        if (shown == cell.Damage)
            return;
        cell.Damage = (byte)Math.Min(byte.MaxValue, shown);
        World.SetCell(point, cell);
    }

    // ------------------------------------------------------------------ demolisher

    /// <summary>
    /// One demolisher use at <paramref name="point"/> (the reference's order): a heart or altar takes the hammer's power;
    /// otherwise a solid block or platform is reshaped once per press (the press is spent even when the block can't be
    /// shaped); otherwise, or while the button is held, the wall there is hit for (int)(power × 1.5) at half the use time.
    /// A power of 0 (the shaping hammer) knocks a wall out in one hit and can't touch hearts or altars.
    /// </summary>
    private void UseDemolisher(ItemDefinition item, GridPoint point, int power)
    {
        if (Player.ActionCooldownTicks > 0)
            return;
        if (!IsInReach(point))
        {
            PostMessage("That is out of reach.");
            return;
        }
        var useTime = Math.Max(1, item.UseCooldownTicks);
        var cell = World.GetCell(point);
        var tile = cell.TileId;
        var active = tile != TileId.Air;
        if (!active || !(ToolParity.IsDemolisherTile(tile) || ToolParity.IsPoundable(tile, Tiles)))
            _poundRelease = false; // not a demolisher target: this press can no longer pound

        if (active && ToolParity.IsDemolisherTile(tile))
        {
            if (power <= 0)
            {
                PostMessage($"The {item.Name.ToLowerInvariant()} is too light to crack the {Tiles[tile].Name.ToLowerInvariant()}.");
                return;
            }
            HitDemolisherTile(item, point, tile, power);
            SetPlayerCooldown(useTime);
            return;
        }

        if (active && ToolParity.IsPoundable(tile, Tiles) && _poundRelease)
        {
            _poundRelease = false;
            SetPlayerCooldown(useTime);
            if (ToolParity.NextPoundShape(World, Tiles, point) is not { } shape)
            {
                PostMessage($"The {Tiles[tile].Name.ToLowerInvariant()} won't take a new shape.");
                return;
            }
            SetShape(point, shape);
            _hitLedger.Clear(point, HitLedger.Layer.Tile);
            ShowCracks(point, 0);
            PostMessage(shape == TileShape.Full ? "Hammered the block flat." : $"Hammered the block into a {ShapeName(shape)}.");
            return;
        }

        if (cell.WallId == WallIds.None)
        {
            if (!active)
                PostMessage("There is no wall there.");
            return;
        }
        if (!ToolParity.CanSmashWall(World, point))
        {
            PostMessage("Work in from an exposed edge of the wall.");
            return;
        }
        HammerWall(point, power);
        SetPlayerCooldown(Math.Max(1, useTime / 2)); // wall hits come twice as fast
    }

    /// <summary>A demolisher hit on a heart or altar: altars keep Rimtera's altar-breaker rule; damage is the hammer's power.</summary>
    private void HitDemolisherTile(ItemDefinition item, GridPoint point, TileId tile, int power)
    {
        var definition = Tiles[tile];
        if (BlightRules.IsAltar(tile) && (!FractureActive || !CanSmashAltars(item)))
        {
            PostMessage(FractureActive ? AltarNeedsBreakerMessage(definition.Name) : AltarLockedMessage(definition.Name));
            return;
        }
        var damage = ToolParity.CanBreakUnder(World, point) ? power : 0;
        if (AddTileHitDamage(point, damage, out var broken))
        {
            if (!HandleBrokenTile(point, broken))
                PostMessage($"Smashed the {definition.Name.ToLowerInvariant()}.");
        }
        else
            PostMessage($"Hit {definition.Name} ({TileHitDamage(point)}/{CombatTuning.TileBreakDamage}).");
    }

    private static string ShapeName(TileShape shape) => shape switch
    {
        TileShape.Half => "half block",
        TileShape.SlopeUpRight or TileShape.SlopeUpLeft => "slope",
        TileShape.CeilingRight or TileShape.CeilingLeft => "ceiling slope",
        _ => "block"
    };

    // ------------------------------------------------------------------ utility tools

    /// <summary>
    /// The utility tools whose rules follow the reference: the shaping hammer (a light demolisher), wire tool (lays wire,
    /// never removes it), wire cutter (one piece per use), buckets and paint tools. False for anything else, and for the
    /// wire tool without a spool (it keeps its remote door trigger).
    /// </summary>
    private bool UseToolParityUtility(ItemDefinition item, GridPoint point)
    {
        switch (item.Id)
        {
            case ItemId.ShapingHammer:
                UseDemolisher(item, point, item.ToolPower);
                return true;
            case ItemId.WireTool:
                return UseWireTool(item, point);
            case ItemId.WireCutter:
                UseWireCutter(item, point);
                return true;
            case ItemId.Actuator:
                // Fitting rules stay with the wiring code; a fitted actuator costs the reference's 10-tick use time.
                if (Player.ActionCooldownTicks > 0)
                    return true;
                if (UsePhysicsItem(item, point) && Player.ActionCooldownTicks > 0)
                    SetPlayerCooldown(ToolParity.Profile(item).UseTime);
                return true;
            case ItemId.EmptyBucket:
                ScoopLiquid(item, point);
                return true;
            case ItemId.BucketWater or ItemId.BucketLava or ItemId.BucketFuel:
                PourLiquid(item, point);
                return true;
            case ItemId.Paintbrush or ItemId.PaintRoller or ItemId.PaintScraper:
                UsePaintParity(item, point);
                return true;
        }
        return false;
    }

    private bool UseWireTool(ItemDefinition item, GridPoint point)
    {
        if (Player.Inventory.Count(ItemId.WireSpool) == 0 || !World.Contains(point) || !IsInReach(point))
            return false; // no spool or out of reach: the remote door trigger may still apply
        if (Player.ActionCooldownTicks > 0)
            return true;
        var layer = ActiveWireLayer;
        if (_wires.Has(point, layer))
            return true; // already wired on this layer: the tool does nothing and costs no time
        Player.Inventory.TryConsume(ItemId.WireSpool);
        _wires.Place(point, layer);
        TrackTimer(point);
        PostMessage($"Laid layer-{layer} wire.");
        SetPlayerCooldown(ToolParity.Profile(item).UseTime);
        return true;
    }

    /// <summary>One piece per use: the actuator first, then the highest wire layer down to layer 1.</summary>
    private void UseWireCutter(ItemDefinition item, GridPoint point)
    {
        if (Player.ActionCooldownTicks > 0)
            return;
        if (!World.Contains(point) || !IsInReach(point))
        {
            PostMessage("That is out of reach.");
            return;
        }
        var cell = World.GetCell(point);
        if (cell.HasActuator)
        {
            cell.HasActuator = false;
            cell.IsInactive = false;
            World.SetCell(point, cell);
            Player.Inventory.Add(ItemId.Actuator, 1);
            _physicsDirty.Add(point);
            PostMessage("Pried out the actuator.");
        }
        else if (Enumerable.Range(1, WireMap.LayerCount).Reverse().FirstOrDefault(layer => _wires.Has(point, layer)) is var layer and > 0)
        {
            _wires.Remove(point, layer);
            Player.Inventory.Add(ItemId.WireSpool, 1);
            PostMessage($"Snipped the layer-{layer} wire.");
        }
        else
        {
            PostMessage("No wiring there.");
            return;
        }
        SetPlayerCooldown(ToolParity.Profile(item).UseTime);
    }

    // ------------------------------------------------------------------ buckets

    /// <summary>An empty bucket needs more than 100/255 of a cell of one liquid in the 3×3 around the target (liquid units).</summary>
    public static readonly int ScoopThresholdUnits = 100 * WorldCell.LiquidUnitsMax / 255;

    /// <summary>Units of the target's liquid in the 3×3 block around it (0 when the target holds none).</summary>
    public static int ScoopableUnits(TileGrid world, GridPoint point)
    {
        if (!world.Contains(point))
            return 0;
        var target = world.GetCell(point);
        if (target.LiquidUnits == 0)
            return 0;
        var total = 0;
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (!world.Contains(point.X + dx, point.Y + dy))
                continue;
            var near = world.GetCell(point.X + dx, point.Y + dy);
            if (near.LiquidUnits > 0 && near.LiquidKind == target.LiquidKind)
                total += near.LiquidUnits;
        }
        return total;
    }

    /// <summary>
    /// Scoops one cell's worth: the whole target cell, topped up from its neighbours of the same liquid, into a full bucket.
    /// </summary>
    private void ScoopLiquid(ItemDefinition item, GridPoint point)
    {
        if (Player.ActionCooldownTicks > 0)
            return;
        var cell = World.Contains(point) ? World.GetCell(point) : default;
        ItemId? full = cell.LiquidKind switch
        {
            LiquidKind.Water => ItemId.BucketWater,
            LiquidKind.Lava => ItemId.BucketLava,
            LiquidKind.Fuel => ItemId.BucketFuel,
            _ => null
        };
        if (!IsInReach(point) || full is null || ScoopableUnits(World, point) <= ScoopThresholdUnits)
        {
            PostMessage("Scoop from a pool of water, fuel or liquid uranium.");
            return;
        }
        if (!Player.Inventory.TryConsume(item.Id))
            return;
        Player.Inventory.Add(full.Value, 1);
        var kind = cell.LiquidKind;
        int taken = cell.LiquidUnits;
        cell.LiquidUnits = 0;
        cell.LiquidKind = LiquidKind.None;
        World.SetCell(point, cell);
        for (var dx = -1; dx <= 1 && taken < WorldCell.LiquidUnitsMax; dx++)
        for (var dy = -1; dy <= 1 && taken < WorldCell.LiquidUnitsMax; dy++)
        {
            var near = new GridPoint(point.X + dx, point.Y + dy);
            if (near == point || !World.Contains(near))
                continue;
            var other = World.GetCell(near);
            if (other.LiquidUnits == 0 || other.LiquidKind != kind)
                continue;
            var take = Math.Min(other.LiquidUnits, WorldCell.LiquidUnitsMax - taken);
            taken += take;
            other.LiquidUnits = (byte)(other.LiquidUnits - take);
            if (other.LiquidUnits == 0)
                other.LiquidKind = LiquidKind.None;
            World.SetCell(near, other);
        }
        PostMessage($"Filled a {Items[full.Value].Name.ToLowerInvariant()}.");
        SetPlayerCooldown(ToolParity.Profile(item).UseTime);
    }

    /// <summary>Pours a full cell into a target holding less than 200/255 of a cell of the same liquid (or none), not inside a solid block.</summary>
    private void PourLiquid(ItemDefinition item, GridPoint point)
    {
        if (Player.ActionCooldownTicks > 0)
            return;
        var kind = item.Id switch
        {
            ItemId.BucketLava => LiquidKind.Lava,
            ItemId.BucketFuel => LiquidKind.Fuel,
            _ => LiquidKind.Water
        };
        var cell = World.Contains(point) ? World.GetCell(point) : default;
        var blocked = !World.Contains(point) || !IsInReach(point) ||
                      cell.LiquidUnits >= 200 * WorldCell.LiquidUnitsMax / 255 ||
                      cell.LiquidUnits > 0 && cell.LiquidKind != kind ||
                      cell.TileId != TileId.Air && !cell.IsInactive && Tiles[cell.TileId].IsSolid && !TilePhysics.IsPlatform(cell.TileId) ||
                      Tiles[cell.TileId].BlocksLiquid && !cell.IsInactive;
        if (blocked)
        {
            PostMessage("The liquid has nowhere to go there.");
            return;
        }
        cell.LiquidUnits = WorldCell.LiquidUnitsMax;
        cell.LiquidKind = kind;
        World.SetCell(point, cell);
        Player.Inventory.TryConsume(item.Id);
        Player.Inventory.Add(ItemId.EmptyBucket, 1);
        SetPlayerCooldown(ToolParity.Profile(item).UseTime);
    }

    // ------------------------------------------------------------------ paint

    /// <summary>
    /// Brush paints the block, roller the wall (one paint per use, nothing when the colour is already there); the scraper
    /// takes the block's paint and coating off first and only then the wall's. Every use that changes something costs
    /// the tool's use time.
    /// </summary>
    private void UsePaintParity(ItemDefinition item, GridPoint point)
    {
        if (Player.ActionCooldownTicks > 0)
            return;
        if (!World.Contains(point) || !IsInReach(point))
        {
            PostMessage("That is out of reach.");
            return;
        }
        var cell = World.GetCell(point);
        PaintMap.TryGetValue(point, out var value);
        if (item.Id == ItemId.PaintScraper)
        {
            if ((value & 0x10F) != 0 && cell.TileId != TileId.Air)
            {
                SetPaint(point, value & ~0x10F);
                PostMessage("Scraped the paint off the block.");
            }
            else if ((value & 0xF0) != 0 && cell.WallId != WallIds.None)
            {
                SetPaint(point, value & ~0xF0);
                PostMessage("Scraped the paint off the wall.");
            }
            else
                return;
            SetPlayerCooldown(ToolParity.Profile(item).UseTime);
            return;
        }
        var roller = item.Id == ItemId.PaintRoller;
        if (roller ? cell.WallId == WallIds.None : cell.TileId == TileId.Air)
        {
            PostMessage(roller ? "The roller needs a background wall." : "The brush needs a block to paint.");
            return;
        }
        var paintIndex = Array.FindIndex(LeisureContent.Paints, paint => Player.Inventory.Count(paint) > 0);
        if (paintIndex < 0)
        {
            PostMessage("You have no paint.");
            return;
        }
        var color = paintIndex + 1;
        var current = roller ? (value >> 4) & 0xF : value & 0xF;
        if (current == color || !Player.Inventory.TryConsume(LeisureContent.Paints[paintIndex]))
            return;
        SetPaint(point, roller ? (value & ~0xF0) | color << 4 : (value & ~0xF) | color);
        SetPlayerCooldown(ToolParity.Profile(item).UseTime);
    }
}
