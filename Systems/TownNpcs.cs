using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Town residents and rescued specialists (append-only; saved as bytes).</summary>
public enum TownNpcKind : byte
{
    StationAi,
    Quartermaster,
    Medic,
    Armorer,
    ChiefEngineer,
    AstroBotanist,
    HeadResearcher,
    TradeConsul,
    TideWarden,
    Demolitionist,
    Tailor,
    Cartographer,
    Mechanic,
    Pilot,
    Cook,
    Arborist,
    Archivist,
    // ---- Hardmode arrivals (append-only): gated on later bosses ----
    /// <summary>Moves in once the Fission Tyrant has fallen.</summary>
    Isotopist,
    /// <summary>Moves in once any of the three hard-tier bosses has fallen.</summary>
    Aeronaut,
    /// <summary>Moves in once the bulb boss (Mutated Flora) has fallen.</summary>
    RelicWarden,
    /// <summary>World &amp; leisure: the greenskeeper moves in once a golf ball has been found (explicit id; GameSimulation.Golf).</summary>
    Greenskeeper = 120,
    // ---- Content fill (explicit ids 140+; Systems/Content/ContentFillTown.cs) ----
    /// <summary>Arrives some mornings with a rotating stock and leaves at dusk.</summary>
    TravellingMerchant = 140,
    /// <summary>Harmless town gels: found in their biome, then they move into free housing.</summary>
    GelAzure = 141, GelAmber = 142, GelRose = 143, GelVerdant = 144,
    // ---- Town tracks (explicit ids 160+; Systems/Content/TownContent.cs; docs/PROGRESSION_DIFF.md G13) ----
    /// <summary>Moves in once the bestiary is a tenth complete; sells critter goods.</summary>
    FieldZoologist = 160,
    /// <summary>Moves in once the player carries a dye material; sells dyes and pigments.</summary>
    PigmentTrader = 161,
    /// <summary>Moves in once ten residents live in the colony; throws colony parties.</summary>
    PartyPlanner = 162,
    // ---- One kind of character (0.3.10): the crew became residents ----
    /// <summary>
    /// A person who joins the colony rather than a trade that moves in: storyteller arrivals and the crew of older saves.
    /// Any number can live here; each works any station, by the work their skills draw them to, and sells nothing.
    /// </summary>
    Settler = 170
}

public enum Affection : sbyte
{
    Hate = -2,
    Dislike = -1,
    Like = 1,
    Love = 2
}

/// <summary>Static data for one town NPC kind: sprite, names, tastes, shop and lines.</summary>
public sealed record TownNpcDefinition(
    TownNpcKind Kind,
    string Role,
    string Sprite,
    string[] Names,
    (BiomeZone Zone, Affection Feeling)[] Biomes,
    (TownNpcKind Kind, Affection Feeling)[] Neighbours,
    (ItemId Item, string? RequiredFlag)[] Shop,
    string[] Lines,
    bool Survivor = false);

/// <summary>
/// A resident (or a bound survivor waiting to be freed). Since 0.3.10 this is the game's one kind of character: residents
/// move in like Terraria's town NPCs and run their shops, and each is a person with skills and passions (the crew's
/// RimWorld side) who works their trade by skill and guards and helps their own area.
/// </summary>
public sealed class TownNpc : IStationWorker
{
    internal TownNpc(long id, TownNpcDefinition definition, string name, Vector2 position, CharacterProfile? profile = null)
    {
        Id = id;
        Definition = definition;
        Name = name;
        Motion = new PawnMotionState(position, HumanoidBody.Size);
        WanderTarget = position;
        SetProfile(profile ?? TownPeople.ProfileFor(definition.Kind, name, unchecked((int)id * 7919 + (int)definition.Kind)));
    }

    public long Id { get; }
    public TownNpcDefinition Definition { get; }
    public TownNpcKind Kind => Definition.Kind;
    public string Name { get; }
    /// <summary>The role shown with the name: the trade, or "Settler".</summary>
    public string Role => Definition.Role;
    public PawnMotionState Motion { get; }
    /// <summary>A cell inside the NPC's room, or null while homeless.</summary>
    public GridPoint? HomeCell { get; internal set; }
    /// <summary>Survivors start tied up inside structures until the player frees them.</summary>
    public bool IsBound { get; internal set; }
    /// <summary>A settler's look (one of the crew sprites); 0 for trades, who use their definition's sprite.</summary>
    public int Look { get; internal set; }
    /// <summary>Sprite name for the renderer (npc_bound_survivor while bound).</summary>
    public string SpriteName => IsBound ? "npc_bound_survivor" : Kind == TownNpcKind.Settler ? TownPeople.SettlerSprite(Look) : Definition.Sprite;
    public float Happiness { get; internal set; }
    public string Activity { get; internal set; } = "Settling in";
    internal Vector2 WanderTarget { get; set; }
    internal float WanderTimer { get; set; }
    /// <summary>Residents can be hurt by hostiles; at zero they die and return later (see GameSimulation.Town).</summary>
    public float MaximumHitPoints { get; } = 160f;
    public float HitPoints { get; internal set; } = 160f;
    public bool IsAlive => HitPoints > 0f;
    internal float HurtCooldown { get; set; }

    // ---- 0.3.10: the person (the crew's skills, passions and work) ----
    /// <summary>Who they are: skills, passions, background and traits.</summary>
    public CharacterProfile Profile { get; private set; } = null!;
    public ColonistStats Stats { get; private set; } = null!;
    /// <summary>The work they turn to, by skill (0.3.9: no Work tab; set from their skills and passions).</summary>
    public ColonyWorkSettings Work { get; private set; } = null!;
    /// <summary>The work being done this tick; null while on the home routine.</summary>
    public WorkType? CurrentWork { get; set; }
    /// <summary>Seconds until the next guard strike.</summary>
    public float StrikeTimer { get; set; }
    /// <summary>Seeds, herbs and fish carried (Farm and Fish work).</summary>
    public Dictionary<ItemId, int> Pouch { get; } = new();
    /// <summary>The grow-zone cell, fishing spot or research bench being worked, and the seconds put into it.</summary>
    public GridPoint? FarmTarget { get; set; }
    public float FarmProgress { get; set; }

    internal void SetProfile(CharacterProfile profile)
    {
        Profile = profile;
        Stats = profile.Stats;
        Work = TownPeople.WorkFor(Kind, Stats);
    }

    /// <summary>The stations of this person's trade (none for a settler, who works any station by skill).</summary>
    public IReadOnlyList<CraftingStationId> Trade => TownTrades.TradeOf(Kind);

    // ---- 0.2.4: residents work station bills beside their shops ----
    /// <summary>Works bills at stations near home by day (the Work tab's toggle); on by default.</summary>
    public bool WorksStations { get; set; } = true;
    public Dictionary<ItemId, int> Held { get; } = new();
    public ItemStack Carried { get; set; }
    public GridPoint? OperateStation { get; set; }
    public float OperateProgress { get; set; }
    /// <summary>Home: the middle of the home cell (setting it moves the home cell there; tests and the dev console).</summary>
    public Vector2 HomePosition
    {
        get => HomeCell is { } home ? new Vector2(home.X + 0.5f, home.Y + 0.5f) : Motion.Position;
        internal set => HomeCell = new GridPoint((int)MathF.Floor(value.X), (int)MathF.Floor(value.Y));
    }
    string IStationWorker.Activity { get => Activity; set => Activity = value; }
    float IStationWorker.SpeedFor(WorkType work) => WorkCatalog.SkillSpeed(Stats, work);
    int IStationWorker.LevelFor(WorkType work) => Stats.Level(WorkCatalog.Get(work).Skill);
    bool IStationWorker.CanWorkAt(CraftingStationId station) => Kind == TownNpcKind.Settler ||
        !TownTrades.Idle(Kind) && (Trade.Contains(station) || TownTrades.BasicBenches.Contains(station));

    public void Heal(float amount) => HitPoints = Math.Min(MaximumHitPoints, HitPoints + Math.Max(0f, amount));
}

/// <summary>
/// Which crafting stations each resident works (0.3.5): every resident can turn a hand to the basic benches (the
/// Nano-Fabricator and the Field Workbench) at a generalist's skill; beyond those, each works only the stations of their
/// trade, and at a specialist's skill there. Travelling traders and town gels work nothing.
/// </summary>
public static class TownTrades
{
    /// <summary>The least skill a resident has in their trade's skills, and at most a generalist's elsewhere (0.3.10: residents' own skills set the rest).</summary>
    public const int GeneralSkill = 6, TradeSkill = 12;

    public static readonly CraftingStationId[] BasicBenches = [CraftingStationId.Workbench, CraftingStationId.FieldWorkbench];

    /// <summary>Each resident's trade stations.</summary>
    public static readonly IReadOnlyDictionary<TownNpcKind, CraftingStationId[]> Stations = new Dictionary<TownNpcKind, CraftingStationId[]>
    {
        [TownNpcKind.StationAi] = [CraftingStationId.ResearchTerminal, CraftingStationId.ChargeReactor],
        [TownNpcKind.Quartermaster] = [CraftingStationId.Sawmill, CraftingStationId.Kiln],
        [TownNpcKind.Medic] = [CraftingStationId.ChemLab, CraftingStationId.BioLab],
        [TownNpcKind.Armorer] = [CraftingStationId.AmmoPress, CraftingStationId.Anvil, CraftingStationId.PowerForgeMk2],
        [TownNpcKind.ChiefEngineer] = [CraftingStationId.Furnace, CraftingStationId.OreRefineryMk2, CraftingStationId.Anvil,
            CraftingStationId.PowerForgeMk2, CraftingStationId.AlloyAnvil, CraftingStationId.Kiln],
        [TownNpcKind.AstroBotanist] = [CraftingStationId.ChemLab, CraftingStationId.BioLab],
        [TownNpcKind.HeadResearcher] = [CraftingStationId.ResearchTerminal, CraftingStationId.NanoFabricatorMk2],
        [TownNpcKind.TideWarden] = [CraftingStationId.CookingStation],
        [TownNpcKind.Demolitionist] = [CraftingStationId.AmmoPress],
        [TownNpcKind.Tailor] = [CraftingStationId.Loom, CraftingStationId.DyeVat],
        [TownNpcKind.Cartographer] = [CraftingStationId.ResearchTerminal],
        [TownNpcKind.Mechanic] = [CraftingStationId.TinkerBench, CraftingStationId.DroneBay, CraftingStationId.NanoFabricatorMk2],
        [TownNpcKind.Pilot] = [CraftingStationId.DroneBay, CraftingStationId.ChargeReactor],
        [TownNpcKind.Cook] = [CraftingStationId.CookingStation],
        [TownNpcKind.Arborist] = [CraftingStationId.Sawmill],
        [TownNpcKind.Archivist] = [CraftingStationId.ResearchTerminal],
        [TownNpcKind.Isotopist] = [CraftingStationId.ChargeReactor, CraftingStationId.AlloyAnvil, CraftingStationId.VoidForge],
        [TownNpcKind.Aeronaut] = [CraftingStationId.TinkerBench, CraftingStationId.AstralAssembler],
        [TownNpcKind.RelicWarden] = [CraftingStationId.VoidForge, CraftingStationId.AstralAssembler],
        [TownNpcKind.Greenskeeper] = [CraftingStationId.Sawmill],
        [TownNpcKind.FieldZoologist] = [CraftingStationId.BioLab],
        [TownNpcKind.PigmentTrader] = [CraftingStationId.DyeVat, CraftingStationId.Loom],
        [TownNpcKind.PartyPlanner] = [CraftingStationId.CookingStation]
    };

    /// <summary>Residents who work no station at all.</summary>
    public static bool Idle(TownNpcKind kind) => kind is TownNpcKind.TradeConsul or TownNpcKind.TravellingMerchant ||
                                                 kind is >= TownNpcKind.GelAzure and <= TownNpcKind.GelVerdant;

    public static IReadOnlyList<CraftingStationId> TradeOf(TownNpcKind kind) => Stations.TryGetValue(kind, out var stations) ? stations : [];

    public static bool CanWork(TownNpcKind kind, CraftingStationId station) =>
        !Idle(kind) && (TradeOf(kind).Contains(station) || BasicBenches.Contains(station));

    /// <summary>Work speed for a skill level (1 at skill 10, as crew).</summary>
    public static float Speed(int skill) => 0.65f + skill * 0.035f;
}

/// <summary>A saved resident. <see cref="Hp"/> was appended in 0.2.2 (NC-20); older saves leave it null (full health).</summary>
public sealed record TownNpcSnapshot(TownNpcKind Kind, string Name, float X, float Y, int? HomeX, int? HomeY, bool Bound, float? Hp = null,
    bool? WorksStations = null, ItemStack[]? Held = null, ItemStack? Carried = null,
    // 0.3.10: the person (skills, passions, background), a settler's look, and what Farm and Fish work carry.
    Rimtera.Characters.CharacterProfile? Person = null, int? Look = null, ItemStack[]? Pouch = null);

public readonly record struct ShopOffer(ItemId Item, string Name, int Price);

/// <summary>Why a room is or is not valid housing.</summary>
public sealed record HousingResult(bool Valid, string Reason, RoomInfo? Room = null)
{
    public static HousingResult Fail(string reason) => new(false, reason);
}

/// <summary>
/// Quarters validation over <see cref="RegionMap"/> rooms (CONTENT_EXPANSION.md §2.6): enclosed, backing
/// walls on nearly every cell, at least <see cref="MinimumCells"/> cells, a light source, a door in the
/// boundary, and table-like plus seat-like furniture.
/// </summary>
public static class HousingValidator
{
    public const int MinimumCells = 60;
    public const float MinimumWalledFraction = 0.9f;

    public static HousingResult Check(TileGrid world, RegionMap regions, GridPoint cell)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(regions);
        var region = regions.GetRegion(cell);
        if (region is null)
            return HousingResult.Fail("There is no open room here.");
        if (!region.IsRoom)
            return HousingResult.Fail(region.TouchesMapEdge || region.IsLarge
                ? "This space is not enclosed."
                : region.CellCount > RegionMap.MaxRoomCells
                    ? $"This room is too large ({region.CellCount}/{RegionMap.MaxRoomCells} cells)."
                    : "This room is not sheltered by walls or a roof.");
        if (region.CellCount < MinimumCells)
            return HousingResult.Fail($"This room is too small ({region.CellCount}/{MinimumCells} cells).");

        int walled = 0;
        bool light = false, table = false, seat = false, door = false;
        var cells = region.CellIndices;
        foreach (var index in cells)
        {
            var x = index % world.Width;
            var y = index / world.Width;
            var data = world.GetCell(x, y);
            if (data.WallId != WallIds.None)
                walled++;
            var traits = ContentTiles.TraitsOf(data.TileId);
            light |= (traits & TileTraits.Light) != 0;
            table |= (traits & TileTraits.Table) != 0;
            seat |= (traits & TileTraits.Seat) != 0;
            door |= (traits & TileTraits.OpenDoor) != 0;
            if (!door)
            {
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    // Entrances frame the room: a door (shut or open) or a platform in the wall, floor or ceiling.
                    if (world.Contains(x + dx, y + dy) &&
                        ContentTiles.Has(world.GetCell(x + dx, y + dy).TileId, TileTraits.Door | TileTraits.OpenDoor | TileTraits.Platform))
                    {
                        door = true;
                        break;
                    }
                }
            }
        }
        if (walled < MinimumWalledFraction * cells.Count)
            return HousingResult.Fail("The room needs background walls.");
        if (!light)
            return HousingResult.Fail("The room needs a light source.");
        if (!door)
            return HousingResult.Fail("The room needs a door or platform entrance.");
        if (!table)
            return HousingResult.Fail("The room needs a table.");
        if (!seat)
            return HousingResult.Fail("The room needs a chair.");
        return new HousingResult(true, "This room is suitable housing.", region);
    }
}

/// <summary>Town NPC data: who they are, what they like and what they sell.</summary>
public static class TownNpcCatalog
{
    private static readonly Dictionary<TownNpcKind, TownNpcDefinition> Table = Build().ToDictionary(definition => definition.Kind);

    public static TownNpcDefinition Get(TownNpcKind kind) => Table[kind];
    public static IEnumerable<TownNpcDefinition> All => Table.Values;

    /// <summary>Kinds rescued from structures (they arrive as bound survivors).</summary>
    public static readonly TownNpcKind[] SurvivorKinds =
    [
        TownNpcKind.Demolitionist, TownNpcKind.Tailor, TownNpcKind.Cartographer, TownNpcKind.Mechanic,
        TownNpcKind.Pilot, TownNpcKind.Cook, TownNpcKind.Arborist, TownNpcKind.Archivist
    ];

    private static IEnumerable<TownNpcDefinition> Build()
    {
        yield return new(TownNpcKind.StationAi, "Station AI", "station_ai", ["ORACLE-7", "HALCYON", "VESPER-2"],
            [(BiomeZone.Grassland, Affection.Like), (BiomeZone.PrecursorVault, Affection.Love), (BiomeZone.AcidCoast, Affection.Dislike)],
            [(TownNpcKind.HeadResearcher, Affection.Like)],
            [(ItemId.FieldWorkbench, null), (ItemId.WallTorch, null), (ItemId.RopeCoil, null)],
            ["Every recipe begins with something you already carry.", "Bring me materials and I will tell you what they make.",
             "Heart fragments refine viridium. Overseer circuits forge the trio."]);
        yield return new(TownNpcKind.Quartermaster, "Quartermaster", "npc_store_clerk", ["Dorrin Vask", "Imke Oyelaran", "Pell Hatchway"],
            [(BiomeZone.Grassland, Affection.Love), (BiomeZone.JunkWasteland, Affection.Dislike)],
            [(TownNpcKind.Cook, Affection.Like), (TownNpcKind.Demolitionist, Affection.Dislike)],
            [(ItemId.Berry, null), (ItemId.WallTorch, null), (ItemId.RopeCoil, null), (ItemId.WoodBlock, null), (ItemId.FerriteBar, null),
             (ItemId.PlankChair, null), (ItemId.PlankTable, null), (ItemId.PlankDoor, null), (ItemId.BugNet, null), (ItemId.FishingRod, null),
             (ItemId.EmptyBucket, null), (ItemId.GlowSticks, null), (ItemId.CompanionPermit, null),
             // The charge ladder (0.1.9 C1), each rung once its gate has passed.
             (ItemId.ChargeCellMedium, "boss.stone_sentinel.down"), (ItemId.ChargeCellLarge, "ore.voidsteel"),
             (ItemId.ChargeCellPeak, "boss.void_cantor.down")],
            ["Torches, rope, rations. The basics never go out of stock.", "Credits are credits. I don't ask where they came from."]);
        yield return new(TownNpcKind.Medic, "Medic", "npc_medical_doctor", ["Dr. Senna Abara", "Dr. Ilo Marchetti"],
            [(BiomeZone.FrozenWasteland, Affection.Like), (BiomeZone.RadiatedDeeps, Affection.Hate)],
            [(TownNpcKind.AstroBotanist, Affection.Love), (TownNpcKind.Demolitionist, Affection.Dislike)],
            [(ItemId.MedStim, null), (ItemId.RestorativeTonic, null), (ItemId.RegenPatch, null), (ItemId.LaserScalpel, null),
             (ItemId.LifeMonitor, null), (ItemId.ChargeStim, "boss.stone_sentinel.down"),
             // The healing ladder (0.1.9 C1), each rung once its gate has passed.
             (ItemId.TraumaStim, "boss.stone_sentinel.down"), (ItemId.RestorationStim, "boss.stone_sentinel.down"),
             (ItemId.NaniteStim, "ore.voidsteel"), (ItemId.PhoenixStim, "boss.void_cantor.down")],
            ["Hold still. This stings less than it looks.", "I can patch you up — for a fee. Nothing is free out here."]);
        yield return new(TownNpcKind.Armorer, "Armorer", "npc_security_guard", ["Captain Rhee", "Sergeant Okafor", "Lieutenant Brask"],
            [(BiomeZone.JunkWasteland, Affection.Like), (BiomeZone.VerdantCanopy, Affection.Dislike)],
            [(TownNpcKind.Mechanic, Affection.Like), (TownNpcKind.Medic, Affection.Like), (TownNpcKind.Pilot, Affection.Dislike)],
            [(ItemId.SlugRounds, null), (ItemId.Arrow, null), (ItemId.ShotgunShells, null), (ItemId.ScrapPistol, null),
             (ItemId.FerriteRevolver, null), (ItemId.CobaltCarbine, "boss.stone_sentinel.down"), (ItemId.ExplosiveCharge, "hearts.broken"),
             (ItemId.BountyNotice, "blight.fracture")],
            ["Keep your sights clean and your rounds dry.", "Raiders hit at dusk. Be ready."]);
        yield return new(TownNpcKind.ChiefEngineer, "Chief Engineer", "npc_base_engineer", ["Tamsin Kroll", "Udo Ferreira"],
            [(BiomeZone.Caverns, Affection.Like), (BiomeZone.MoltenChasm, Affection.Love), (BiomeZone.AcidCoast, Affection.Hate)],
            [(TownNpcKind.Mechanic, Affection.Love), (TownNpcKind.Tailor, Affection.Dislike)],
            [(ItemId.WireTool, null), (ItemId.CircuitBoard, null), (ItemId.PowerCell, null), (ItemId.ScoutDroneRemote, null),
             (ItemId.TurretAutogun, null), (ItemId.ConveyorBlock, null), (ItemId.DroneSpareParts, null), (ItemId.CommandVisor, "hearts.broken")],
            ["Drones fly better when you stop kicking them.", "A conveyor under the forge saves a lot of walking."]);
        yield return new(TownNpcKind.AstroBotanist, "Astro-Botanist", "npc_xeno_biologist", ["Wren Aalto", "Juno Kassab"],
            [(BiomeZone.VerdantCanopy, Affection.Love), (BiomeZone.FungalHollows, Affection.Like), (BiomeZone.JunkWasteland, Affection.Hate)],
            [(TownNpcKind.Medic, Affection.Like), (TownNpcKind.Arborist, Affection.Love), (TownNpcKind.Demolitionist, Affection.Hate)],
            [(ItemId.Berry, null), (ItemId.HydroponicsTray, null), (ItemId.PurifierGel, null), (ItemId.PlantPot, null),
             (ItemId.HerbPlanter, null), (ItemId.BioGel, null), (ItemId.PurifierSprayer, "hearts.broken"),
             (ItemId.PrismGrassBlock, "blight.fracture")],
            ["The canopy breathes. Listen and you can hear it.", "Purifier gel turns blight back to honest soil."]);
        yield return new(TownNpcKind.HeadResearcher, "Head Researcher", "npc_colony_worker", ["Prof. Adaeze Lin", "Prof. Marek Holm"],
            [(BiomeZone.CrystalSpires, Affection.Love), (BiomeZone.Overgrowth, Affection.Dislike)],
            [(TownNpcKind.Archivist, Affection.Love), (TownNpcKind.StationAi, Affection.Like), (TownNpcKind.Quartermaster, Affection.Dislike)],
            [(ItemId.DatapadFlare, null), (ItemId.DatapadSonic, null), (ItemId.ChargeCellSmall, null), (ItemId.ChargeStim, null),
             (ItemId.SparkEmitter, null), (ItemId.FocusLensCrystal, "blight.fracture"), (ItemId.OverchargeCapacitor, "blight.fracture")],
            ["Charge is just patience stored in a crystal.", "The blight hearts are not random. They are a pattern."]);
        yield return new(TownNpcKind.TradeConsul, "Trade Consul", "npc_store_clerk", ["Consul Ysolde", "Consul Barth"],
            [(BiomeZone.AcidCoast, Affection.Love), (BiomeZone.Grassland, Affection.Like)],
            [(TownNpcKind.Pilot, Affection.Love), (TownNpcKind.Quartermaster, Affection.Dislike)],
            [(ItemId.JetBoots, null), (ItemId.LuckyCharm, null), (ItemId.MagnetRing, null), (ItemId.NightVisionGoggles, null),
             (ItemId.RubyShard, null), (ItemId.TopazShard, null), (ItemId.GliderWings, "blight.fracture"), (ItemId.RocketPack, "ore.voidsteel")],
            ["Rare goods, rare prices. I leave at dusk.", "My ship does not wait for haggling."]);
        yield return new(TownNpcKind.TideWarden, "Tide-Warden", "npc_colony_worker", ["Old Maro", "Saltie Venn"],
            [(BiomeZone.AcidCoast, Affection.Love), (BiomeZone.FrozenWasteland, Affection.Dislike)],
            [(TownNpcKind.Cook, Affection.Love), (TownNpcKind.Pilot, Affection.Like)],
            [(ItemId.FishingRod, null), (ItemId.FerriteFishingRod, null), (ItemId.Worm, null), (ItemId.FishingLine, "quests.fishing"),
             (ItemId.CatchScanner, "quests.fishing")],
            ["Bring me today's fish and I will make it worth your while.", "Rain makes them bite. Dawn too."]);
        yield return new(TownNpcKind.Demolitionist, "Demolitionist", "npc_demolitionist", ["Boom Kessler", "Rook Adeyemi"],
            [(BiomeZone.Caverns, Affection.Love), (BiomeZone.CrystalSpires, Affection.Dislike)],
            [(TownNpcKind.Mechanic, Affection.Like), (TownNpcKind.Medic, Affection.Dislike)],
            [(ItemId.ExplosiveCharge, null), (ItemId.MiniRocket, null), (ItemId.FerriteDemolisher, null), (ItemId.CobaltDemolisher, null),
             (ItemId.StickyMine, null), (ItemId.DemolitionTube, "blight.fracture")],
            ["Stand back. No, further.", "Every wall is a door if you use enough charge."], Survivor: true);
        yield return new(TownNpcKind.Tailor, "Tailor", "npc_tailor", ["Ines Varga", "Theo Quill"],
            [(BiomeZone.MiteWarrens, Affection.Like), (BiomeZone.MoltenChasm, Affection.Dislike)],
            [(TownNpcKind.Cook, Affection.Like), (TownNpcKind.ChiefEngineer, Affection.Dislike)],
            [(ItemId.ClothRoll, null), (ItemId.FestivalMask, null), (ItemId.ColonyBanner, null), (ItemId.GliderWings, "blight.fracture")],
            ["Mite silk makes the finest thread, if you can stand the smell.", "Hold still while I measure."], Survivor: true);
        yield return new(TownNpcKind.Cartographer, "Cartographer", "npc_cartographer", ["Lio Ferran", "Anouk Belle"],
            [(BiomeZone.Grassland, Affection.Like), (BiomeZone.PrecursorVault, Affection.Love)],
            [(TownNpcKind.Archivist, Affection.Like), (TownNpcKind.Pilot, Affection.Like)],
            [(ItemId.CompassChip, null), (ItemId.DepthMeter, null), (ItemId.OreScanner, null), (ItemId.TeleportBeacon, "vault.opened")],
            ["The coasts are acid all the way down. Mind your boots.", "There is a vault under the canopy. I'd stake my maps on it."],
            Survivor: true);
        yield return new(TownNpcKind.Mechanic, "Mechanic", "npc_mechanic", ["Gus Okonkwo", "Rita Sandoval"],
            [(BiomeZone.JunkWasteland, Affection.Love), (BiomeZone.VerdantCanopy, Affection.Dislike)],
            [(TownNpcKind.ChiefEngineer, Affection.Love), (TownNpcKind.Tailor, Affection.Dislike)],
            [(ItemId.DroneSpareParts, null), (ItemId.DroneBattery, null), (ItemId.TurretFlame, "boss.stone_sentinel.down"),
             (ItemId.TurretMortar, "hearts.broken")],
            ["If it rattles, it works. If it stops rattling, run.", "Your drones need oil. And love."], Survivor: true);
        yield return new(TownNpcKind.Pilot, "Pilot", "npc_pilot", ["Skye Morrow", "Dax Ilunga"],
            [(BiomeZone.Grassland, Affection.Love), (BiomeZone.Caverns, Affection.Hate)],
            [(TownNpcKind.Aeronaut, Affection.Like), (TownNpcKind.Armorer, Affection.Dislike)],
            [(ItemId.RocketPack, "ore.voidsteel"), (ItemId.GliderWings, null), (ItemId.DoubleJumpCanister, null), (ItemId.GrappleHook, null)],
            ["Nothing beats open sky.", "Pirates fly old hulls. Shoot the engines."], Survivor: true);
        yield return new(TownNpcKind.Cook, "Cook", "npc_cook", ["Mama Teodora", "Chef Kwame"],
            [(BiomeZone.VerdantCanopy, Affection.Like), (BiomeZone.RadiatedDeeps, Affection.Hate)],
            [(TownNpcKind.Quartermaster, Affection.Like), (TownNpcKind.TideWarden, Affection.Love)],
            [(ItemId.RationBar, null), (ItemId.MushroomStew, null), (ItemId.NectarTea, null), (ItemId.CoralSushi, null),
             (ItemId.CookingStation, null)],
            ["Eat something. You look like a ration bar.", "Fresh fish makes the best stew."], Survivor: true);
        yield return new(TownNpcKind.Arborist, "Arborist", "npc_arborist", ["Elm Vasquez", "Fern Adair"],
            [(BiomeZone.VerdantCanopy, Affection.Love), (BiomeZone.MoltenChasm, Affection.Hate)],
            [(TownNpcKind.AstroBotanist, Affection.Love), (TownNpcKind.Demolitionist, Affection.Dislike)],
            [(ItemId.FerriteCutter, null), (ItemId.CobaltCutter, null), (ItemId.AstroTimberPlanksBlock, null), (ItemId.Sawmill, null)],
            ["Cut what you need and plant what you cut.", "The hives under the canopy sing at night."], Survivor: true);
        yield return new(TownNpcKind.Archivist, "Archivist", "npc_archivist", ["Sister Odalys", "Keeper Brann"],
            [(BiomeZone.PrecursorVault, Affection.Love), (BiomeZone.AcidCoast, Affection.Dislike)],
            [(TownNpcKind.HeadResearcher, Affection.Love), (TownNpcKind.Demolitionist, Affection.Hate)],
            [(ItemId.PrecursorGlyphBlock, null), (ItemId.ResearchTerminal, null), (ItemId.VaultCore, "vault.opened")],
            ["The precursors wrote in light and stone.", "Every glyph is a question. Few are answered."], Survivor: true);
        yield return new(TownNpcKind.Isotopist, "Isotopist", "npc_base_engineer", ["Dr. Vela Okonkwo", "Rask Hollin"],
            [(BiomeZone.MoltenChasm, Affection.Love), (BiomeZone.FrozenWasteland, Affection.Dislike)],
            [(TownNpcKind.ChiefEngineer, Affection.Like), (TownNpcKind.Medic, Affection.Dislike)],
            [(ItemId.UraniumRod, null), (ItemId.CausticShard, null), (ItemId.CoreIgniter, null), (ItemId.PurifierBeacon, null),
             (ItemId.ViridiumBar, "altars.smashed")],
            ["The core still hums where the Tyrant fell.", "Every altar you crack pushes new metal up from the deep."]);
        yield return new(TownNpcKind.Aeronaut, "Aeronaut", "npc_colony_worker", ["Kite Marlowe", "Ansa Brightwater"],
            [(BiomeZone.Grassland, Affection.Love), (BiomeZone.Caverns, Affection.Hate)],
            [(TownNpcKind.Pilot, Affection.Love), (TownNpcKind.Demolitionist, Affection.Dislike)],
            [(ItemId.GliderWings, null), (ItemId.RocketPack, null), (ItemId.DoubleJumpCanister, null), (ItemId.ClothRoll, null),
             (ItemId.WarshipTransponder, "hardmode.trio.down")],
            ["Flight essence only drifts off things that live in the open sky.", "Wings are just patience and a good updraft."]);
        yield return new(TownNpcKind.RelicWarden, "Relic Warden", "npc_archivist", ["Warden Iolas", "Warden Pethe"],
            [(BiomeZone.PrecursorVault, Affection.Love), (BiomeZone.VerdantCanopy, Affection.Like)],
            [(TownNpcKind.Archivist, Affection.Love), (TownNpcKind.Tailor, Affection.Dislike)],
            [(ItemId.AncientPowerCell, null), (ItemId.PrecursorRelic, "vault.opened"), (ItemId.VoidHymnal, "boss.vault_colossus.down")],
            ["The vault's guardian was only the door. Something chants behind it.", "Hooded ones gather at the dungeon gate."]);
        yield return LeisureContent.Greenskeeper(); // Systems/Golf.cs
        foreach (var resident in ContentFillTown.Definitions()) yield return resident; // Systems/Content/ContentFillTown.cs
        foreach (var resident in TownContent.Definitions()) yield return resident; // Systems/Content/TownContent.cs
        // 0.3.10: settlers (the crew's successors) have no shop and no move-in rule: they join through the storyteller.
        yield return new(TownNpcKind.Settler, "Settler", TownPeople.SettlerSprites[0], ["Settler"],
            [(BiomeZone.Grassland, Affection.Like)], [], [],
            ["I came a long way to find a place like this.", "Point me at the work. I'll get it done.",
             "Somebody has to keep the benches running."]);
    }
}
