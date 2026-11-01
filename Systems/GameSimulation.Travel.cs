using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>One mount's movement profile. Original Rimtera tuning.</summary>
/// <param name="Speed">Run-speed multiplier on land.</param>
/// <param name="JumpFactor">Multiplier on the launch speed of a ground jump.</param>
/// <param name="FlightSeconds">Seconds of powered lift while jump is held in the air (refills on landing).</param>
/// <param name="SwimFactor">Run-speed multiplier while in a liquid (replaces the usual liquid drag); 0 means none.</param>
/// <param name="Digs">Chews through soft ground ahead while moving.</param>
public sealed record MountSpec(
    MountKind Kind,
    string Name,
    ItemId Item,
    string Sprite,
    float Speed,
    float JumpFactor = 1f,
    float FlightSeconds = 0f,
    float SwimFactor = 0f,
    bool Digs = false,
    float DrawWidth = 2.4f,
    float DrawHeight = 1.4f,
    string MountMessage = "",
    float FallFactor = 0.5f,
    float FallCap = 17.5f);

/// <summary>A placed pylon as the map sees it.</summary>
public readonly record struct PylonInfo(GridPoint Cell, TileId Kind, string Name, bool Active);

/// <summary>
/// Data for the travel pass: glide-walks (moving walkway floors), six biome pylons, and the mount keys.
/// Ids live in the travel bands (items 8000–8199, tiles 300–319). Names and numbers are original.
/// </summary>
public static class TravelContent
{
    /// <summary>Belt speed of a glide-walk and of an express glide-walk (tiles/s), added to whatever the rider is doing.</summary>
    public const float GlideWalkSpeed = 6f;
    public const float ExpressWalkSpeed = 11f;

    public static IReadOnlyList<TileDefinition> TileDefinitions { get; } =
    [
        new(TileId.GlideWalkLeft, "Glide-walk", true, true, 30, 0.8f, 94),
        new(TileId.GlideWalkRight, "Glide-walk", true, true, 30, 0.8f, 94),
        new(TileId.ExpressWalkLeft, "Express glide-walk", true, true, 30, 0.8f, 94),
        new(TileId.ExpressWalkRight, "Express glide-walk", true, true, 30, 0.8f, 94),
        new(TileId.ColonyPylon, "Colony pylon", false, false, 40, 1.2f, 95),
        new(TileId.FrostPylon, "Frost pylon", false, false, 40, 1.2f, 95),
        new(TileId.ScrapPylon, "Scrap pylon", false, false, 40, 1.2f, 95),
        new(TileId.CanopyPylon, "Canopy pylon", false, false, 40, 1.2f, 95),
        new(TileId.CavernPylon, "Cavern pylon", false, false, 40, 1.2f, 95),
        new(TileId.CoastPylon, "Coast pylon", false, false, 40, 1.2f, 95)
    ];

    public static bool IsGlideWalk(TileId tile) => tile is >= TileId.GlideWalkLeft and <= TileId.ExpressWalkRight;

    /// <summary>Signed belt speed of a glide-walk tile (negative runs left), or 0 for any other tile.</summary>
    public static float BeltSpeed(TileId tile) => tile switch
    {
        TileId.GlideWalkLeft => -GlideWalkSpeed,
        TileId.GlideWalkRight => GlideWalkSpeed,
        TileId.ExpressWalkLeft => -ExpressWalkSpeed,
        TileId.ExpressWalkRight => ExpressWalkSpeed,
        _ => 0f
    };

    /// <summary>The glide-walk tile an item places when the placer faces <paramref name="facing"/>.</summary>
    public static TileId GlideWalkFacing(ItemId item, int facing) => item == ItemId.ExpressWalk
        ? facing < 0 ? TileId.ExpressWalkLeft : TileId.ExpressWalkRight
        : facing < 0 ? TileId.GlideWalkLeft : TileId.GlideWalkRight;

    /// <summary>The same walkway running the other way.</summary>
    public static TileId Reversed(TileId tile) => tile switch
    {
        TileId.GlideWalkLeft => TileId.GlideWalkRight,
        TileId.GlideWalkRight => TileId.GlideWalkLeft,
        TileId.ExpressWalkLeft => TileId.ExpressWalkRight,
        TileId.ExpressWalkRight => TileId.ExpressWalkLeft,
        _ => tile
    };
    public static bool IsPylon(TileId tile) => tile is >= TileId.ColonyPylon and <= TileId.CoastPylon;

    /// <summary>The biome a pylon must stand in.</summary>
    public static BiomeZone PylonZone(TileId pylon) => pylon switch
    {
        TileId.FrostPylon => BiomeZone.FrozenWasteland,
        TileId.ScrapPylon => BiomeZone.JunkWasteland,
        TileId.CanopyPylon => BiomeZone.VerdantCanopy,
        TileId.CavernPylon => BiomeZone.Caverns,
        TileId.CoastPylon => BiomeZone.AcidCoast,
        _ => BiomeZone.Grassland
    };

    public static string PylonSprite(TileId pylon) => pylon switch
    {
        TileId.FrostPylon => "deco_pylon_frost",
        TileId.ScrapPylon => "deco_pylon_scrap",
        TileId.CanopyPylon => "deco_pylon_canopy",
        TileId.CavernPylon => "deco_pylon_cavern",
        TileId.CoastPylon => "deco_pylon_coast",
        _ => "deco_pylon_colony"
    };

    public static ItemId? TileDrop(TileId tile) => tile switch
    {
        TileId.GlideWalkLeft or TileId.GlideWalkRight => ItemId.GlideWalk,
        TileId.ExpressWalkLeft or TileId.ExpressWalkRight => ItemId.ExpressWalk,
        TileId.ColonyPylon => ItemId.ColonyPylon,
        TileId.FrostPylon => ItemId.FrostPylon,
        TileId.ScrapPylon => ItemId.ScrapPylon,
        TileId.CanopyPylon => ItemId.CanopyPylon,
        TileId.CavernPylon => ItemId.CavernPylon,
        TileId.CoastPylon => ItemId.CoastPylon,
        _ => null
    };

    /// <summary>
    /// The mount table. The hover-bike keeps its original tuning. Each mount has its own fall: the share of fall damage the
    /// rider takes (none for the flyers and the spring hopper, half for the rest) and its top falling speed.
    /// </summary>
    public static IReadOnlyDictionary<MountKind, MountSpec> Mounts { get; } = new Dictionary<MountKind, MountSpec>
    {
        [MountKind.HoverBike] = new(MountKind.HoverBike, "Hover-bike", ItemId.HoverBikeKey, "mount_hover_bike",
            GameSimulation.HoverBikeSpeed, MountMessage: "The hover-bike hums to life.", FallFactor: 0f),
        [MountKind.DuneStrider] = new(MountKind.DuneStrider, "Dune strider", ItemId.StriderWhistle, "mount_dune_strider", 1.9f,
            JumpFactor: 1.3f, DrawWidth: 2.4f, DrawHeight: 1.8f, MountMessage: "A dune strider lopes over at your whistle."),
        [MountKind.RotorSkiff] = new(MountKind.RotorSkiff, "Rotor skiff", ItemId.RotorSkiffKey, "mount_rotor_skiff", 1.3f,
            FlightSeconds: 3.5f, DrawWidth: 2.2f, DrawHeight: 1.3f, MountMessage: "The rotor skiff spins up.", FallFactor: 0f),
        [MountKind.TideRay] = new(MountKind.TideRay, "Tide ray", ItemId.TideRayCharm, "mount_tide_ray", 0.85f,
            SwimFactor: 2.6f, DrawWidth: 2.4f, DrawHeight: 1.2f, MountMessage: "A tide ray glides beneath you."),
        [MountKind.Burrower] = new(MountKind.Burrower, "Burrower", ItemId.BurrowerKey, "mount_burrower", 0.7f,
            Digs: true, DrawWidth: 2.2f, DrawHeight: 1.4f, MountMessage: "The burrower's jaws start grinding."),
        [MountKind.SpringHopper] = new(MountKind.SpringHopper, "Spring hopper", ItemId.SpringHopperKey, "mount_spring_hopper", 1.1f,
            JumpFactor: 2f, DrawWidth: 1.8f, DrawHeight: 1.6f, MountMessage: "The spring hopper coils its legs.", FallFactor: 0f, FallCap: 24f)
    };

    public static MountKind? MountForItem(ItemId item) =>
        Mounts.Values.FirstOrDefault(spec => spec.Item == item) is { } spec ? spec.Kind : null;

    public static void RegisterItems(Dictionary<ItemId, ItemDefinition> items)
    {
        void Add(ItemDefinition definition) => items[definition.Id] = definition;

        Add(new ItemDefinition(ItemId.GlideWalk, "Glide-walk", ItemUseKind.PlaceTile, 999)
            { PlacedTile = TileId.GlideWalkRight, Tier = 1, Value = 4 });
        Add(new ItemDefinition(ItemId.ExpressWalk, "Express glide-walk", ItemUseKind.PlaceTile, 999)
            { PlacedTile = TileId.ExpressWalkRight, Tier = 2, Value = 18 });
        foreach (var (item, tile, name) in new[]
                 {
                     (ItemId.ColonyPylon, TileId.ColonyPylon, "Colony pylon"), (ItemId.FrostPylon, TileId.FrostPylon, "Frost pylon"),
                     (ItemId.ScrapPylon, TileId.ScrapPylon, "Scrap pylon"), (ItemId.CanopyPylon, TileId.CanopyPylon, "Canopy pylon"),
                     (ItemId.CavernPylon, TileId.CavernPylon, "Cavern pylon"), (ItemId.CoastPylon, TileId.CoastPylon, "Coast pylon")
                 })
            Add(new ItemDefinition(item, name, ItemUseKind.PlaceTile, 99)
                { PlacedTile = tile, Tier = 2, Rarity = ItemRarity.Uncommon, Value = 250 });
        foreach (var spec in Mounts.Values.Where(spec => spec.Kind != MountKind.HoverBike))
            Add(new ItemDefinition(spec.Item, spec.Kind switch
                {
                    MountKind.DuneStrider => "Strider whistle",
                    MountKind.RotorSkiff => "Rotor-skiff key",
                    MountKind.TideRay => "Tide-ray charm",
                    MountKind.Burrower => "Burrower key",
                    _ => "Spring-hopper key"
                }, ItemUseKind.Utility, 1)
                { Tier = 2, Rarity = ItemRarity.Rare, Value = 300, UseCooldownTicks = 20 });
    }

    public static void RegisterRecipes(CraftingSystem crafting)
    {
        void Add(string id, ItemId output, int count, CraftingStationId? station, params (ItemId Item, int Count)[] ingredients) =>
            crafting.Register(new RecipeDefinition("tv-" + id, output, count,
                ingredients.Select(entry => IngredientRequirement.Exact(entry.Item, entry.Count)).ToArray(),
                station is { } required ? new HashSet<CraftingStationId> { required } : null));

        Add("glide-walk", ItemId.GlideWalk, 20, CraftingStationId.Anvil, (ItemId.FerriteBar, 1), (ItemId.CopperBar, 1));
        Add("express-walk", ItemId.ExpressWalk, 5, CraftingStationId.Anvil, (ItemId.GlideWalk, 5), (ItemId.PowerCell, 1));
        Add("pylon-colony", ItemId.ColonyPylon, 1, CraftingStationId.Anvil, (ItemId.EnergyCrystal, 10), (ItemId.PowerCell, 2), (ItemId.WoodBlock, 20));
        Add("pylon-frost", ItemId.FrostPylon, 1, CraftingStationId.Anvil, (ItemId.EnergyCrystal, 10), (ItemId.PowerCell, 2), (ItemId.FrostCrystal, 8));
        Add("pylon-scrap", ItemId.ScrapPylon, 1, CraftingStationId.Anvil, (ItemId.EnergyCrystal, 10), (ItemId.PowerCell, 2), (ItemId.OxidizedShard, 8));
        Add("pylon-canopy", ItemId.CanopyPylon, 1, CraftingStationId.Anvil, (ItemId.EnergyCrystal, 10), (ItemId.PowerCell, 2), (ItemId.JungleRootBlock, 20));
        Add("pylon-cavern", ItemId.CavernPylon, 1, CraftingStationId.Anvil, (ItemId.EnergyCrystal, 16), (ItemId.PowerCell, 2), (ItemId.RockBlock, 30));
        Add("pylon-coast", ItemId.CoastPylon, 1, CraftingStationId.Anvil, (ItemId.EnergyCrystal, 10), (ItemId.PowerCell, 2), (ItemId.CoralShard, 8));
        Add("strider", ItemId.StriderWhistle, 1, CraftingStationId.Anvil, (ItemId.CopperBar, 12), (ItemId.PowerCell, 3), (ItemId.DesertGlassBlock, 20));
        Add("rotor-skiff", ItemId.RotorSkiffKey, 1, CraftingStationId.Anvil, (ItemId.CobaltBar, 12), (ItemId.PowerCell, 6), (ItemId.CircuitBoard, 4));
        Add("tide-ray", ItemId.TideRayCharm, 1, CraftingStationId.Workbench, (ItemId.CoralShard, 15), (ItemId.GlowSpore, 10), (ItemId.PowerCell, 2));
        Add("burrower", ItemId.BurrowerKey, 1, CraftingStationId.Anvil, (ItemId.FerriteBar, 15), (ItemId.PowerCell, 4), (ItemId.RockBlock, 40));
        Add("spring-hopper", ItemId.SpringHopperKey, 1, CraftingStationId.Workbench, (ItemId.CopperBar, 8), (ItemId.GlowSpore, 6), (ItemId.PowerCell, 2));
    }
}

/// <summary>
/// Travel: glide-walks (moving walkway floors that carry whoever stands on them), biome pylons for fast travel
/// from the fullscreen map (active only inside a settled town), and the movement of the mount types.
/// </summary>
public sealed partial class GameSimulation
{
    // ------------------------------------------------------------------ tuning
    public const float PylonTownRadius = 40f;
    public const int PylonResidentsNeeded = 2;
    public const float PylonUseRadius = 6f;
    public const float MountFlightLift = 62f * GravityScale;
    public const float BurrowSeconds = 0.12f;

    private readonly HashSet<GridPoint> _pylons = new();
    private bool _pylonsScanned;
    private float _mountFlight;
    private float _burrowTimer;

    /// <summary>Belt speed under the player's feet this tick (0 when not on a glide-walk).</summary>
    public float GlideWalkCarry { get; private set; }
    public MountSpec? ActiveMountSpec => TravelContent.Mounts.GetValueOrDefault(ActiveMount);
    /// <summary>Flight seconds left on a flying mount.</summary>
    public float MountFlightRemaining => _mountFlight;

    // ------------------------------------------------------------------ mounts

    /// <summary>Mounts <paramref name="kind"/> (needs its item carried), or dismounts when it is already active.</summary>
    public bool ToggleMount(MountKind kind)
    {
        if (ActiveMount == kind)
        {
            Dismount();
            return true;
        }
        if (!TravelContent.Mounts.TryGetValue(kind, out var spec) || Player.Inventory.Count(spec.Item) == 0)
            return false;
        ActiveMount = kind;
        _mountFlight = spec.FlightSeconds;
        PostMessage(spec.MountMessage);
        Achievements.TryUnlock("easy-rider");
        return true;
    }

    private void Dismount()
    {
        if (ActiveMountSpec is { } spec)
            PostMessage($"You step off the {spec.Name.ToLowerInvariant()}.");
        ActiveMount = MountKind.None;
    }

    /// <summary>The first mount whose item the player carries (hover-bike first), for the mount key.</summary>
    private MountKind? FirstCarriedMount() =>
        (MiscEquipped(MiscSlot.Mount) is { } equipped ? TravelContent.MountForItem(equipped) : null)
        ?? TravelContent.Mounts.Values.FirstOrDefault(spec => Player.Inventory.Count(spec.Item) > 0)?.Kind;

    /// <summary>Travel item uses (mount keys). True when handled.</summary>
    private bool UseTravelItem(ItemDefinition item, GridPoint point)
    {
        if (TravelContent.MountForItem(item.Id) is not { } kind || kind == MountKind.HoverBike)
            return false;
        ToggleMount(kind);
        SetPlayerCooldown(Math.Max(6, item.UseCooldownTicks));
        return true;
    }

    /// <summary>Speed multiplier the active mount applies (liquids handled by the caller).</summary>
    private float MountSpeedFactor(bool inLiquid) => ActiveMountSpec is not { } spec ? 1f
        : inLiquid && spec.SwimFactor > 0f ? spec.SwimFactor : spec.Speed;

    /// <summary>True when the active mount ignores liquid drag (it swims).</summary>
    private bool MountSwimsIn(LiquidKind liquid) =>
        ActiveMountSpec is { SwimFactor: > 0f } && liquid is LiquidKind.Water or LiquidKind.Acid or LiquidKind.Fuel or LiquidKind.Phase;

    /// <summary>Pre-step mount movement: flight lift, swimming strokes and burrowing.</summary>
    private void PrepareMountMovement(ref Vector2 velocity, MovementInput movement, float deltaSeconds)
    {
        if (ActiveMountSpec is not { } spec)
            return;
        var motion = Player.Motion;
        if (spec.FlightSeconds > 0f)
        {
            if (motion.Grounded)
                _mountFlight = spec.FlightSeconds;
            else if (movement.JumpHeld && _mountFlight > 0f && _bodyLiquid == LiquidKind.None)
            {
                velocity.Y = Math.Max(velocity.Y - MountFlightLift * deltaSeconds, -7.5f * LiftScale);
                _mountFlight = Math.Max(0f, _mountFlight - deltaSeconds);
                motion.FallPeakY = motion.Position.Y;
            }
        }
        if (spec.SwimFactor > 0f && MountSwimsIn(_bodyLiquid))
        {
            if (movement.JumpHeld)
                velocity.Y = Math.Min(velocity.Y, -6f * LiftScale);
            else if (movement.Vertical > 0.5f)
                velocity.Y = 5f * LiftScale;
            else
                velocity.Y *= 0.8f;
        }
        if (spec.Digs)
            Burrow(movement, deltaSeconds);
    }

    /// <summary>Post-step mount movement: boosted jumps and breath under water for the swimmer.</summary>
    private void AfterMountMovement(MovementInput movement, ref Vector2 velocity)
    {
        if (ActiveMountSpec is not { } spec)
            return;
        // Boosted jumps: while the jump's sustain window runs, hold the launch speed at the mount's scale.
        if (spec.JumpFactor > 1f && velocity.Y < -1f && (movement.JumpHeld || movement.JumpPressed) && Player.Motion.JumpSustainRemaining > 0f)
            velocity.Y = Math.Min(velocity.Y, -PlayerMovementSettings.JumpImpulse * spec.JumpFactor);
        if (spec.SwimFactor > 0f && _bodyLiquid != LiquidKind.None)
            _breath = MaximumBreath;
    }

    private static bool IsSoftGround(TileId tile) => tile is TileId.Soil or TileId.Grass or TileId.Sand or TileId.DesertSand or
        TileId.Snow or TileId.JungleGrass or TileId.FungalSoil or TileId.Ash or TileId.CrimsonSoil or TileId.HallowGrass or
        TileId.RadiatedSand or TileId.OvergrowthSoil or TileId.OvergrowthSand;

    /// <summary>The burrower chews the soft column just ahead of the rider.</summary>
    private void Burrow(MovementInput movement, float deltaSeconds)
    {
        var dir = Math.Sign(movement.Horizontal);
        if (dir == 0)
        {
            _burrowTimer = 0f;
            return;
        }
        var motion = Player.Motion;
        var column = (int)MathF.Floor(dir > 0 ? motion.Position.X + motion.Size.X + 0.1f : motion.Position.X - 0.1f);
        var top = (int)MathF.Floor(motion.Position.Y + 0.05f);
        var bottom = (int)MathF.Floor(motion.Position.Y + motion.Size.Y - 0.05f);
        var soft = new List<GridPoint>();
        for (var y = top; y <= bottom; y++)
        {
            var cell = new GridPoint(column, y);
            if (World.Contains(cell) && IsSoftGround(World.GetCell(cell).TileId))
                soft.Add(cell);
        }
        if (soft.Count == 0)
        {
            _burrowTimer = 0f;
            return;
        }
        _burrowTimer += deltaSeconds;
        if (_burrowTimer < BurrowSeconds)
            return;
        _burrowTimer = 0f;
        foreach (var cell in soft)
        {
            if (TileDropTable.GetDrop(World.GetCell(cell).TileId) is { } drop && Items.Contains(drop) && Player.Inventory.CanAdd(drop, 1))
                Player.Inventory.Add(drop, 1);
            World.SetTile(cell, TileId.Air);
        }
        _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, soft));
        Flags.Increment("travel.burrowed", soft.Count);
    }

    // ------------------------------------------------------------------ glide-walks

    /// <summary>
    /// A glide-walk carries whoever stands on it: its belt speed is added on top of the rider's own walking,
    /// so walking with it is fast, standing still drifts along, and walking against it barely makes headway.
    /// The belt under the middle of the feet wins; at the end of the line the rider simply steps off.
    /// </summary>
    private void CarryOnGlideWalk(float deltaSeconds)
    {
        GlideWalkCarry = 0f;
        var motion = Player.Motion;
        if (!motion.Grounded || Player.IsDead)
            return;
        var row = (int)MathF.Floor(motion.Position.Y + motion.Size.Y + 0.05f);
        var belt = 0f;
        foreach (var footX in new[] { motion.Position.X + motion.Size.X / 2f, motion.Position.X + 0.05f, motion.Position.X + motion.Size.X - 0.05f })
        {
            var cell = new GridPoint((int)MathF.Floor(footX), row);
            if (World.Contains(cell) && TravelContent.BeltSpeed(World.GetCell(cell).TileId) is var speed && speed != 0f)
            {
                belt = speed;
                break;
            }
        }
        if (belt == 0f)
            return;
        var moved = motion.Position with { X = motion.Position.X + belt * deltaSeconds };
        if (IsSolidActorPosition(moved, motion.Size))
            return;
        motion.Position = moved;
        GlideWalkCarry = belt;
        Flags.Increment("travel.glide_ticks");
    }

    /// <summary>Flips the glide-walk at <paramref name="point"/> to run the other way. True when it was a glide-walk.</summary>
    public bool TryReverseGlideWalk(GridPoint point)
    {
        if (!World.Contains(point) || !TravelContent.IsGlideWalk(World.GetCell(point).TileId))
            return false;
        World.SetTile(point, TravelContent.Reversed(World.GetCell(point).TileId));
        return true;
    }

    // ------------------------------------------------------------------ placement

    /// <summary>Places glide-walks (running the way the placer faces) and pylons (town and biome rules). True when handled.</summary>
    private bool TryPlaceTravelTile(ItemDefinition item, GridPoint point)
    {
        var tile = item.PlacedTile;
        if (!TravelContent.IsGlideWalk(tile) && !TravelContent.IsPylon(tile))
            return false;
        if (!World.Contains(point) || !IsInReach(point))
        {
            PostMessage("That placement is out of reach.");
            return true;
        }
        if (World.GetCell(point).TileId != TileId.Air)
        {
            PostMessage("Cannot place: the cell is occupied.");
            return true;
        }
        if (TravelContent.IsPylon(tile))
        {
            if (PylonPlacementProblem(tile, point, checkBiome: true) is { } problem)
            {
                PostMessage(problem);
                return true;
            }
        }
        else
            tile = TravelContent.GlideWalkFacing(item.Id, Player.Motion.Facing);
        PlaceTravelTile(point, tile);
        Player.Inventory.TryConsume(item.Id);
        SetPlayerCooldown(5);
        PostMessage($"Placed {item.Name}.");
        return true;
    }

    private void PlaceTravelTile(GridPoint point, TileId tile)
    {
        World.SetTile(point, tile);
        _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, new[] { point }));
        if (TravelContent.IsPylon(tile))
            _pylons.Add(point);
    }

    /// <summary>Why a pylon may not go at <paramref name="point"/>, or null when it may.</summary>
    internal string? PylonPlacementProblem(TileId pylon, GridPoint point, bool checkBiome)
    {
        if (!World.Contains(point.X, point.Y + 1) || !Tiles[World.GetCell(new GridPoint(point.X, point.Y + 1)).TileId].IsSolid)
            return "A pylon needs solid ground under it.";
        if (Pylons.Any(other => other.Kind == pylon))
            return $"A {Tiles[pylon].Name.ToLowerInvariant()} already stands somewhere in this world.";
        if (CountResidentsNear(point) < PylonResidentsNeeded)
            return $"A pylon needs a staffed home: at least {PylonResidentsNeeded} people living within {PylonTownRadius:0} tiles (the base, or an outpost).";
        if (checkBiome && TravelContent.PylonZone(pylon) is var zone && pylon != TileId.ColonyPylon &&
            BiomeScanner.Scan(World, point).Zone != zone)
            return $"The {Tiles[pylon].Name.ToLowerInvariant()} only works in the {BiomeScanner.DisplayName(zone)}.";
        return null;
    }

    // 0.7.2: a pylon is staffed by the people who live around it (prisoners do not count).
    private int CountResidentsNear(GridPoint cell) => _townNpcs.Count(npc => !npc.IsBound && !npc.IsPrisoner && npc.IsAlive && npc.HomeCell is { } home &&
        Vector2.Distance(new Vector2(home.X, home.Y), new Vector2(cell.X, cell.Y)) <= PylonTownRadius);

    // ------------------------------------------------------------------ pylons and fast travel

    /// <summary>Every standing pylon (scanned from the world once, then tracked).</summary>
    public IReadOnlyList<PylonInfo> Pylons
    {
        get
        {
            if (!_pylonsScanned)
            {
                _pylonsScanned = true;
                for (var y = 0; y < World.Height; y++)
                for (var x = 0; x < World.Width; x++)
                    if (TravelContent.IsPylon(World.GetCell(new GridPoint(x, y)).TileId))
                        _pylons.Add(new GridPoint(x, y));
            }
            _pylons.RemoveWhere(cell => !World.Contains(cell) || !TravelContent.IsPylon(World.GetCell(cell).TileId));
            return _pylons.OrderBy(cell => cell.X).Select(cell =>
            {
                var kind = World.GetCell(cell).TileId;
                return new PylonInfo(cell, kind, Tiles[kind].Name, RelayOnline(cell)); // 0.7.6: staffed and charged
            }).ToArray();
        }
    }

    /// <summary>0.7.2: where a pylon stands: "the base", an outpost, or "the wilds" (nobody lives there).</summary>
    public string PylonPlace(PylonInfo pylon)
    {
        var at = new Vector2(pylon.Cell.X + 0.5f, pylon.Cell.Y);
        if (Vector2.Distance(at, BaseCenter) <= OutpostDistance)
            return "the base";
        return Outposts.Where(outpost => Vector2.Distance(outpost.Center, at) <= PylonTownRadius + OutpostSpread)
            .OrderBy(outpost => Vector2.Distance(outpost.Center, at)).FirstOrDefault()?.Label ?? "the wilds";
    }

    /// <summary>The active pylon within reach of the player, if any (the teleport source).</summary>
    public PylonInfo? PylonInRange => Pylons.Where(pylon => pylon.Active &&
            Vector2.Distance(PlayerCenter, new Vector2(pylon.Cell.X + 0.5f, pylon.Cell.Y)) <= PylonUseRadius)
        .Select(pylon => (PylonInfo?)pylon).FirstOrDefault();

    /// <summary>Teleports from a nearby active pylon to the active pylon at <paramref name="destination"/>.</summary>
    public bool TryPylonTeleport(GridPoint destination)
    {
        if (Player.IsDead || Player.IsDowned)
            return false;
        if (PylonInRange is not { } source)
        {
            PostMessage("Stand by an active pylon to travel.");
            return false;
        }
        if (Pylons.FirstOrDefault(pylon => pylon.Cell == destination) is not { Kind: not TileId.Air } target)
            return false;
        if (!target.Active)
        {
            PostMessage(RelayStatus(target.Cell)); // 0.7.6: unstaffed or out of charge
            return false;
        }
        if (target.Cell == source.Cell)
            return false;
        // 0.7.6: a trip costs charge at both ends.
        if (RelayCharge(source.Cell) < RelayTravelCost || RelayCharge(target.Cell) < RelayTravelCost)
        {
            PostMessage($"Not enough charge to travel: each relay needs {RelayTravelCost}.");
            return false;
        }
        var size = Player.Motion.Size;
        foreach (var offset in new[] { 0f, 1f, -1f, 2f, -2f })
        {
            var position = new Vector2(destination.X + 0.5f + offset - size.X / 2f, destination.Y + 1f - size.Y - 0.01f);
            if (IsSolidActorPosition(position, size))
                continue;
            _grapple = null;
            Player.Motion.Position = position;
            Player.Motion.Velocity = Vector2.Zero;
            Player.Motion.FallPeakY = position.Y;
            Flags.Increment("player.teleports");
            Flags.Increment("travel.pylon_jumps");
            SetRelayCharge(source.Cell, RelayCharge(source.Cell) - RelayTravelCost);
            SetRelayCharge(target.Cell, RelayCharge(target.Cell) - RelayTravelCost);
            PostMessage($"The pylons fold space: you arrive at the {target.Name.ToLowerInvariant()}.");
            return true;
        }
        PostMessage("The destination pylon is boxed in.");
        return false;
    }

    // ------------------------------------------------------------------ test helpers

    /// <summary>Puts a housed resident in the town (tests and debug tools).</summary>
    internal void AddResidentForTests(TownNpcKind kind, GridPoint home)
    {
        var definition = TownNpcCatalog.Get(kind);
        var npc = new TownNpc(9000 + _townNpcs.Count, definition, definition.Names[0], new Vector2(home.X, home.Y - 2f)) { HomeCell = home };
        _townNpcs.Add(npc);
    }

    /// <summary>Places a travel tile directly, skipping reach and pylon rules (tests and debug tools).</summary>
    internal void PlaceTravelTileForTests(GridPoint point, TileId tile) => PlaceTravelTile(point, tile);
}
