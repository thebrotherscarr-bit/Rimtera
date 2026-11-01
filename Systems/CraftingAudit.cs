using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// `--craft-audit`: simulates progression from a fresh Classic character (docs/CRAFTING_PROGRESSION.md). It starts from
/// the starting kit and adds everything that can be gathered, then iterates recipes to a fixed point stage by stage:
/// each station built, each drill power reached, each boss beaten (the Fission Tyrant brings the Fracture), each event
/// cleared. Sources it models:
/// <list type="bullet">
/// <item>tiles of generated worlds (one radiated, one overgrowth), broken for real with <see cref="GameSimulation.ForceBreakTile"/>
/// before and after the Fracture, gated on each tile's minimum drill power (trees need a cutter, hearts a demolisher,
/// altars the Fracture and an altar breaker), plus the full loot table of every container;</item>
/// <item>creature drops (<see cref="NpcDropRegistry"/>, arsenal mob drops, Fracture essences): post-Fracture rosters wait for
/// the Fracture, event rosters for their event, boss loot for that boss;</item>
/// <item>town NPC shops (each resident's move-in condition and each stock line's flag), fishing (catch tables, crates,
/// quest rewards), the sifter, the phase pool, bug-net critters, event rewards and boss treasure caches.</item>
/// </list>
/// The audit reports the stage at which every station and item becomes obtainable, items that never do, materials no
/// recipe uses, stations nothing builds, circular recipe chains and recipes that arrive later than their tier implies.
/// </summary>
public static class CraftingAudit
{
    /// <summary>One obtained item: the stage index it arrived at and how.</summary>
    public sealed record Reach(int Stage, string Via);

    /// <summary>A recipe that is only craftable later than its output's tier implies.</summary>
    public sealed record LateRecipe(string RecipeId, ItemId Output, int Tier, string Stage, string Cause);

    public sealed class Result
    {
        public required IReadOnlyList<string> Stages { get; init; }
        public required IReadOnlyDictionary<ItemId, Reach> Items { get; init; }
        public required IReadOnlyDictionary<CraftingStationId, int> Stations { get; init; }
        /// <summary>Stage at which each boss first became fightable (its gate open and a summon in hand, or it arrives on its own).</summary>
        public required IReadOnlyDictionary<EnemyKind, int> BossReach { get; init; }
        /// <summary>Stage at which each world event first became available.</summary>
        public required IReadOnlyDictionary<WorldEventKind, int> EventReach { get; init; }
        /// <summary>Items never obtainable (deliberate exceptions removed).</summary>
        public required IReadOnlyList<ItemId> Unreachable { get; init; }
        /// <summary>Items obtainable only through a milestone whose own requirement was never met (forced to keep going).</summary>
        public required IReadOnlyList<ItemId> OnlyAfterBlocked { get; init; }
        public required IReadOnlyList<ItemId> DeadEnds { get; init; }
        public required IReadOnlyList<CraftingStationId> UnbuildableStations { get; init; }
        public required IReadOnlyList<string> BlockedMilestones { get; init; }
        public required IReadOnlyList<string> Cycles { get; init; }
        public required IReadOnlyList<LateRecipe> LateRecipes { get; init; }
        /// <summary>Tier 4+ gear (not materials) obtainable before the Fracture; must stay inside <see cref="EarlyGearWhitelist"/>.</summary>
        public required IReadOnlyList<ItemId> EarlyGear { get; init; }
        /// <summary>Stage index of the Fracture (the gate boss's defeat).</summary>
        public required int FractureStage { get; init; }
        /// <summary>Bosses this run held back until the Fracture (empty for the live run, <see cref="Run"/>).</summary>
        public required IReadOnlySet<EnemyKind> HeldForFracture { get; init; }
        public required IReadOnlyList<string> Notes { get; init; }

        /// <summary>Bosses (other than the gate boss) fightable before the Fracture, in the order they became so (PC-7).</summary>
        public IReadOnlyList<EnemyKind> PreFractureBosses => BossReach
            .Where(entry => entry.Value < FractureStage && entry.Key != GameSimulation.CoreGateBoss)
            .OrderBy(entry => entry.Value).ThenBy(entry => entry.Key).Select(entry => entry.Key).ToArray();

        /// <summary>Tier 4+ gear before the Fracture (PC-7; printed, gated by the whitelist).</summary>
        public IReadOnlyList<ItemId> PreFractureTier4Items => EarlyGear;

        /// <summary>Tier 6+ gear before the Fracture.</summary>
        public IReadOnlyList<ItemId> PreFractureTier6Items => EarlyGear.Where(id => Catalog[id].Tier >= 6).ToArray();

        /// <summary>How many tier 6+ items are obtainable before the Fracture (0 is the goal: ROADMAP_0.2.0 §1.2).</summary>
        public int PreFractureTier6Count => PreFractureTier6Items.Count;
        public required ItemCatalog Catalog { get; init; }
        public required CraftingSystem Crafting { get; init; }

        public string StageOf(ItemId item) => Items.TryGetValue(item, out var reach) ? Stages[reach.Stage] : "never";
        public bool Has(ItemId item) => Items.ContainsKey(item);
    }

    /// <summary>
    /// Items a Classic character is not meant to obtain, with the reason. Everything else must be reachable
    /// (CraftingAuditSelfTests). Keep this list short and explicit.
    /// </summary>
    public static readonly IReadOnlyDictionary<ItemId, string> DeliberateExceptions = new Dictionary<ItemId, string>
    {
    };

    /// <summary>
    /// Materials that feed no recipe on purpose: fishing junk is only sold, as Terraria's old shoe, seaweed and tin can
    /// are (no recipe in Recipe.SetupRecipes uses them).
    /// </summary>
    public static readonly IReadOnlyDictionary<ItemId, string> DeliberateDeadEnds = new Dictionary<ItemId, string>
    {
        [ItemId.RustedCan] = "fishing junk, sold for credits (Terraria: tin can)",
        [ItemId.TangledWire] = "fishing junk, sold for credits (Terraria: fishing seaweed)",
        [ItemId.SoggyDatapad] = "fishing junk, sold for credits (Terraria: old shoe)"
    };

    /// <summary>
    /// Tier 4+ gear a Classic character may obtain before the Fracture, each with its reason (X-CR-1,
    /// docs/CRAFTING_PROGRESSION.md §6). <see cref="Result.EarlyGear"/> must stay inside this list (CraftingAuditSelfTests).
    /// Entries marked "until PC-6" or "until PC-16" wait for the parity steps that re-gate them.
    /// </summary>
    public static readonly IReadOnlyDictionary<ItemId, string> EarlyGearWhitelist = BuildWhitelist(
        ("Tinker Bench combination of pre-Fracture parts; until PC-16 gives it a tier-matched input", [
            ItemId.ArcanistsCirclet, ItemId.ArchitectsFrame, ItemId.BallisticsEmblemMkII, ItemId.BallisticsModule, ItemId.BerserkerRig,
            ItemId.BioWeave, ItemId.BulwarkCore, ItemId.CloseQuartersEmblemMkII, ItemId.CommandEmblemMkII, ItemId.CommandModule,
            ItemId.DroneController, ItemId.DuelistHarness, ItemId.EnergyShieldEmitter, ItemId.EnvironmentalSuit, ItemId.ExcavationSuite,
            ItemId.ExcavatorRig, ItemId.FortuneMatrix, ItemId.GunnersHarness, ItemId.HeartPump, ItemId.InfernoTreads, ItemId.MagmaSkin,
            ItemId.MasterTackleKit, ItemId.MeleeModule, ItemId.ReinforcedArmorSuit, ItemId.RetributionMesh, ItemId.RocketPack,
            ItemId.SanguineReactor, ItemId.ServoGrip, ItemId.SpikeLattice, ItemId.TargetingLensMkII, ItemId.TechEmblemMkII,
            ItemId.TechModule, ItemId.TelescopicArm, ItemId.VitalCore, ItemId.WardensUplink]),
        ("ammunition pressed from pre-Fracture materials (viridium bars, hab-pod void essence); until PC-6 re-tiers or re-gates it", [
            ItemId.CryoRocket, ItemId.CrystalDarts, ItemId.FlechetteRounds, ItemId.IncendiaryRocket, ItemId.OverchargedCell,
            ItemId.PlasmaFuel, ItemId.PrismCell, ItemId.SeekerBolts, ItemId.ShardBolts, ItemId.ViridiumBolts, ItemId.ViridiumRounds]),
        ("tonic or food brewed from pre-Fracture ingredients (a consumable buff); until PC-6 re-tiers or re-gates it", [
            ItemId.GreaterBallisticsTonic, ItemId.GreaterCloseQuartersTonic, ItemId.GreaterCommandTonic, ItemId.GreaterTechTonic,
            ItemId.HeavyFortifyStim, ItemId.HuntersFocus, ItemId.LifeforceSerum, ItemId.WrathDraught, ItemId.AstroBurger,
            ItemId.ColonyFeast, ItemId.KoiFlambe, ItemId.PrismSorbet, ItemId.VoidNoodles]),
        ("summon for an optional boss that has no Fracture gate; fighting it early is the player's risk (gate review with the boss framework)", [
            ItemId.BountyNotice, ItemId.ClaimMarker, ItemId.DreadnoughtFlag, ItemId.MoonEaterLure, ItemId.ProtocolKey,
            ItemId.SunspotLens, ItemId.VoidLure]),
        ("summon whose boss refuses before its gate (HardmodeSummonRefusal: the Fracture, the trio or the opened vault)", [
            ItemId.MutagenBloom, ItemId.TaintedTissue]),
        ("Defense Protocol starter and drop: a pre-Fracture event (Terraria: the Old One's Army, tier 1)", [
            ItemId.SummonSignalCore, ItemId.ProtocolShard]),
        ("the viridium refinery: a pre-Fracture station (Terraria: the Hellforge); its recipes keep their own tiers", [
            ItemId.OreRefineryMk2]),
        ("fishing find, crate or quest reward (Terraria: fishing gives some hardmode-tier gear before hardmode; crates hold " +
         "hardmode ores only after the Fracture)", [
            ItemId.HiveFamiliar, ItemId.HiveStingerRapier, ItemId.MoltenCrate,
            ItemId.LavaWaderBoots, ItemId.MagmaFishingRod, ItemId.LavaLure, ItemId.RoyalNectar]),
        ("rare creature or event drop (Terraria: rare pre-hardmode drops such as the dungeon's)", [
            ItemId.AuroraRing, ItemId.CanopyLongbow, ItemId.CodexTidalSurge, ItemId.HiveLash, ItemId.SpliceNeedle, ItemId.SkyrayWings,
            ItemId.StormRepeater, ItemId.WispStaff]),
        ("rare drop of a Crystal Spires creature that also spawns before the Fracture; until PC-6 moves it to the post-Fracture roster", [
            ItemId.CodexShardStorm, ItemId.CrystalCrate, ItemId.HaloGyre, ItemId.PrismRibbon]),
        ("sold by a town NPC behind its own unlock (Demolitionist; the Mechanic after the hearts)", [
            ItemId.MiniRocket, ItemId.TurretMortar]));

    /// <summary>
    /// The intended pre-Fracture bosses (Terraria's pre-hardmode bosses; docs/roadmap/0.1.7.md release acceptance): the
    /// progression bosses before the gate boss and the optional biome bosses. Any other boss the audit finds fightable before
    /// the Fracture must be on <see cref="PreFractureBossReview"/> (CraftingAuditSelfTests).
    /// </summary>
    public static readonly IReadOnlySet<EnemyKind> IntendedPreFractureBosses = new HashSet<EnemyKind>
    {
        EnemyKind.StoneSentinel, EnemyKind.AlphaSkitter, EnemyKind.DamagedMiningDrone, EnemyKind.PlantOvergrowth, EnemyKind.ScoutMech,
        EnemyKind.FalloutHivemind, EnemyKind.SporeMonarch, EnemyKind.HoarfrostBehemoth
    };

    /// <summary>
    /// Bosses the live game still lets a Classic character fight before the Fracture although the plan gates them (or asks
    /// the owner to decide), each with the reason and the step that closes it. Their loot is reported apart
    /// (<see cref="RunGated"/> holds them back), not whitelisted. Remove an entry when its gate lands and is modelled in
    /// <see cref="BossGateOpen"/>: CraftingAuditSelfTests fails on an entry that is no longer early.
    /// </summary>
    public static readonly IReadOnlyDictionary<EnemyKind, string> PreFractureBossReview = new Dictionary<EnemyKind, string>
    {
        [EnemyKind.ScavengerWarlord] = "Scavenger Raid finale, dusk incursion (PC-4: stream C) and the Warlord banner; the 0.1.7 acceptance " +
                                       "counts it as pre-hardmode, so once PC-4 gates its nanite stim it can move to the intended list",
        [EnemyKind.VoidStalker] = "dusk incursion boss (D-3 option A, PC-4: stream C; its nanite marrow is tier 6) and the Void lure (owner's call)",
        [EnemyKind.CyberPirateCaptain] = "dusk incursion boss (D-3 option A, PC-4: stream C) and the Bounty notice (owner's call)",
        [EnemyKind.SolarFlareEntity] = "dusk incursion boss (D-3 option A, PC-4: stream C) and the Sunspot lens (owner's call)",
        [EnemyKind.AsteroidMinerMech] = "dusk incursion boss (D-3 option A, PC-4: stream C) and the Claim marker (owner's call); " +
                                        "it drops a drill-power-107 drill",
        [EnemyKind.PirateDreadnought] = "the Dreadnought flag summons it at any time; its event (Pirate Boarding) already waits for the Fracture (owner's call)"
    };

    private static Dictionary<ItemId, string> BuildWhitelist(params (string Reason, ItemId[] Items)[] groups)
    {
        var map = new Dictionary<ItemId, string>();
        foreach (var (reason, ids) in groups)
            foreach (var id in ids)
                map.Add(id, reason);
        return map;
    }

    /// <summary>Why a whole family of items is excluded (difficulty-only boss rewards, debug items).</summary>
    public static string? ExceptionFor(ItemId item, ItemCatalog catalog)
    {
        if (DeliberateExceptions.TryGetValue(item, out var reason))
            return reason;
        // Brutal / Annihilation boss salvage caches, their exclusive core-chips and Annihilation relics (Terraria: Expert
        // treasure bags and their expert items, Master relics) are not part of a Classic world.
        foreach (var boss in EnemyArchetypes.All.Where(archetype => archetype.IsBoss).Select(archetype => archetype.Kind))
        {
            if (PlayerSystemsContent.BagFor(boss) == item)
                return "salvage cache: Brutal and Annihilation worlds only (Terraria: Expert treasure bag)";
            if (PlayerSystemsContent.ExclusiveFor(boss) == item)
                return "core-chip from a salvage cache: Brutal and Annihilation only (Terraria: Expert-only item)";
            if (PlayerSystemsContent.RelicFor(boss) == item)
                return "boss relic: Annihilation only (Terraria: Master-mode relic)";
        }
        if (item == ItemId.AuxiliarySocket)
            return "Fission Tyrant drop on Brutal and Annihilation only (Terraria: the Expert-only Demon Heart)";
        return null;
    }

    // ================================================================== the opening

    /// <summary>What a fresh character holds after a few minutes: the kit plus one felled tree, then plus surface digging.</summary>
    public sealed record Opening(IReadOnlySet<ItemId> FromTree, IReadOnlySet<CraftingStationId> StationsFromTree,
        IReadOnlySet<ItemId> FromSurface, IReadOnlySet<CraftingStationId> StationsFromSurface);

    /// <summary>A felled tree: timber and saplings (the starting hatchet is a cutter).</summary>
    public static readonly ItemId[] TreeYield = [ItemId.Timber, ItemId.Sapling];

    /// <summary>Surface digging with the starting drill: regolith, basalt, ferrite ore, sand, energy crystals and berries.</summary>
    public static readonly ItemId[] SurfaceYield =
        [ItemId.SoilBlock, ItemId.RockBlock, ItemId.CopperOre, ItemId.SandBlock, ItemId.EnergyCrystal, ItemId.Berry, ItemId.Credits];

    /// <summary>
    /// The opening check: from the Classic kit and a felled tree (then also surface digging), craft by hand and at every
    /// station whose placeable is in hand, to a fixed point. Independent of the world scan.
    /// </summary>
    public static Opening RunOpening(CraftingSystem? crafting = null, ItemCatalog? items = null)
    {
        items ??= new ItemCatalog();
        crafting ??= new CraftingSystem(items);
        var tiles = new TileCatalog();
        (HashSet<ItemId>, HashSet<CraftingStationId>) Close(IEnumerable<ItemId> start)
        {
            var have = start.ToHashSet();
            var stations = new HashSet<CraftingStationId>();
            bool changed;
            do
            {
                changed = false;
                foreach (var id in have.ToArray())
                {
                    var item = items[id];
                    if (item.UseKind == ItemUseKind.PlaceTile && tiles.Contains(item.PlacedTile) &&
                        tiles[item.PlacedTile].Station is var station and not CraftingStationId.None && stations.Add(station))
                    {
                        stations.UnionWith(station.ImpliedClosure());
                        changed = true;
                    }
                }
                foreach (var recipe in crafting.Recipes)
                {
                    if (have.Contains(recipe.Output) || recipe.RequiredLiquids is { Count: > 0 } || recipe.RequiredBiomeTags is { Count: > 0 })
                        continue;
                    if ((recipe.RequiredStations ?? new HashSet<CraftingStationId>()).All(stations.Contains) &&
                        recipe.Ingredients.All(group => group.Alternatives.Any(have.Contains)))
                        changed |= have.Add(recipe.Output);
                }
            } while (changed);
            return (have, stations);
        }
        var kit = StarterKits.Standard.Select(entry => entry.Item).ToArray();
        var tree = items.All.Any(item => kit.Contains(item.Id) && item.UseKind == ItemUseKind.Cutter) ? TreeYield : [];
        var (fromTree, treeStations) = Close(kit.Concat(tree));
        var (fromSurface, surfaceStations) = Close(kit.Concat(tree).Concat(SurfaceYield));
        return new Opening(fromTree, treeStations, fromSurface, surfaceStations);
    }

    // ================================================================== state

    private sealed class State
    {
        public required ItemCatalog Items { get; init; }
        public required TileCatalog Tiles { get; init; }
        public required CraftingSystem Crafting { get; init; }
        public readonly Dictionary<ItemId, Reach> Have = new();
        public readonly HashSet<CraftingStationId> Stations = new();
        public readonly Dictionary<CraftingStationId, int> StationStage = new();
        public readonly HashSet<EnemyKind> Defeated = new();
        public readonly HashSet<WorldEventKind> Events = new();
        public readonly WorldFlags Flags = new();
        public readonly List<string> Stages = ["Start"];
        public bool Fracture;
        /// <summary>Bosses this run holds back until the Fracture (the gated run: <see cref="PreFractureBossReview"/>).</summary>
        public IReadOnlySet<EnemyKind> HeldForFracture { get; init; } = new HashSet<EnemyKind>();
        public int MinePower;
        public bool Forced;
        public readonly HashSet<ItemId> ForcedItems = new();
        /// <summary>Ground tiles in reach: natural tiles (and post-Fracture ones once fractured) plus every placeable obtained.</summary>
        public readonly HashSet<TileId> Grounds = new();
        public int Stage => Stages.Count - 1;

        public bool Got(ItemId item) => Have.ContainsKey(item);

        public bool Grant(ItemId item, string via)
        {
            if (Have.ContainsKey(item) || !Items.Contains(item))
                return false;
            Have[item] = new Reach(Stage, via);
            if (Forced)
                ForcedItems.Add(item);
            if (Items[item].PlacedTile != TileId.Air)
                Grounds.Add(Items[item].PlacedTile);
            return true;
        }

        public bool Any(Func<ItemDefinition, bool> predicate) => Have.Keys.Any(id => predicate(Items[id]));
        public int DrillPower => Have.Keys.Select(id => Items[id]).Where(item => item.UseKind == ItemUseKind.Pickaxe)
            .Select(item => item.ToolPower).DefaultIfEmpty(0).Max();
    }

    /// <summary>A gathering rule: when <see cref="Ready"/> holds, every output is obtainable.</summary>
    private sealed record Rule(string Via, Func<State, bool> Ready, IReadOnlyCollection<ItemId> Outputs);

    /// <summary>A progression step: once ready (and earlier ones have had their chance), it changes the world state.</summary>
    private sealed record Milestone(string Name, int Order, Func<State, bool> Ready, Action<State> Apply)
    {
        /// <summary>The boss a "Defeated" milestone stands for (null for every other milestone).</summary>
        public EnemyKind? Boss { get; init; }
    }

    /// <summary>What breaking a natural tile gave in the scan: before and after the Fracture.</summary>
    private sealed record TileYield(TileId Tile, HashSet<ItemId> Before, HashSet<ItemId> After)
    {
        /// <summary>The tile only turned up after the Fracture.</summary>
        public bool OnlyAfter { get; set; } = true;
    }

    private static int MeteoritePower => new TileCatalog()[TileId.Meteorite].MinimumToolPower;

    private static Result? _cached, _gated;
    private static (IReadOnlyList<TileYield> Yields, List<string> Notes)? _scan;
    private static readonly object CacheLock = new();

    /// <summary>The audit for the current content (cached: the world scan is the slow part).</summary>
    public static Result Run()
    {
        lock (CacheLock)
            return _cached ??= Compute(worldWidth: 4200, worldHeight: 1200, heldForFracture: new HashSet<EnemyKind>());
    }

    /// <summary>
    /// The same run with every boss on <see cref="PreFractureBossReview"/> held back until the Fracture: the progression as
    /// it stands once those bosses are gated. The tier 4+ whitelist and the tier 6+ count are checked against this run,
    /// so what only those bosses leak (their loot, and anything it unlocks) is reported apart instead of whitelisted.
    /// </summary>
    public static Result RunGated()
    {
        lock (CacheLock)
            return _gated ??= Compute(worldWidth: 4200, worldHeight: 1200, heldForFracture: PreFractureBossReview.Keys.ToHashSet());
    }

    // ================================================================== the run

    private static Result Compute(int worldWidth, int worldHeight, IReadOnlySet<EnemyKind> heldForFracture)
    {
        var items = new ItemCatalog();
        var tiles = new TileCatalog();
        var crafting = new CraftingSystem(items);
        var state = new State { Items = items, Tiles = tiles, Crafting = crafting, HeldForFracture = heldForFracture };

        if (_scan is null)
        {
            var scanNotes = new List<string>();
            _scan = (ScanWorlds(worldWidth, worldHeight, scanNotes), scanNotes);
        }
        var yields = _scan.Value.Yields;
        var notes = new List<string>(_scan.Value.Notes);
        var naturalBefore = yields.Where(yield => !yield.OnlyAfter).Select(yield => yield.Tile).ToHashSet();
        var naturalAfter = yields.Select(yield => yield.Tile).ToHashSet();
        state.Grounds.UnionWith(naturalBefore);
        state.Flags.Set("season.starfall"); // seasons come round on their own
        var rules = new List<Rule>();
        AddStartRules(rules);
        AddTileRules(rules, yields, tiles, items);
        AddCreatureRules(rules, items);
        AddShopRules(rules, items);
        AddMiscRules(rules, items);
        var milestones = BuildMilestones(items, tiles, yields);
        var eventHost = new AuditEventHost(state.Flags);
        var events = new WorldEventSystem(1).Specs.ToArray();
        AddEventRules(rules, events, items);

        var recipeStage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var eventReach = new Dictionary<WorldEventKind, int>();

        void Close()
        {
            if (state.Fracture)
                state.Grounds.UnionWith(naturalAfter);
            bool changed;
            do
            {
                changed = false;
                // Events become available as their conditions are met (flags change with milestones).
                foreach (var spec in events)
                {
                    if (state.Events.Contains(spec.Kind) || !EventAvailable(spec, state, eventHost, items))
                        continue;
                    state.Events.Add(spec.Kind);
                    eventReach.TryAdd(spec.Kind, state.Stage);
                    changed = true;
                }
                foreach (var rule in rules)
                {
                    if (!rule.Ready(state))
                        continue;
                    foreach (var output in rule.Outputs)
                        changed |= state.Grant(output, rule.Via);
                }
                foreach (var recipe in crafting.Recipes)
                {
                    if (recipeStage.ContainsKey(recipe.Id) || !Craftable(recipe, state))
                        continue;
                    recipeStage[recipe.Id] = state.Stage;
                    changed |= state.Grant(recipe.Output, "craft " + recipe.Id + Where(recipe));
                    changed = true;
                }
            } while (changed);
        }

        Close();
        var done = new HashSet<string>();
        var blocked = new List<string>();
        var bossReach = new Dictionary<EnemyKind, int>();
        void NoteReachableBosses()
        {
            foreach (var milestone in milestones)
                if (milestone.Boss is { } boss && !bossReach.ContainsKey(boss) && !done.Contains(milestone.Name) && milestone.Ready(state))
                    bossReach[boss] = state.Stage;
        }
        NoteReachableBosses();
        while (true)
        {
            // PC-7: the Fracture is held back until every other ready milestone has been taken, so a boss (or anything
            // else) that needs no Fracture is dated before it, however late its order.
            var next = milestones.Where(milestone => !done.Contains(milestone.Name) && milestone.Ready(state))
                .OrderBy(milestone => milestone.Boss == GameSimulation.CoreGateBoss && !state.Fracture ? 1 : 0)
                .ThenBy(milestone => milestone.Order).FirstOrDefault();
            if (next is null)
                break;
            done.Add(next.Name);
            state.Stages.Add(next.Name);
            next.Apply(state);
            Close();
            NoteReachableBosses();
        }
        // Milestones that never became ready: force them in order so what lies behind them is still reported.
        foreach (var milestone in milestones.Where(milestone => !done.Contains(milestone.Name)).OrderBy(milestone => milestone.Order).ToArray())
        {
            if (done.Contains(milestone.Name))
                continue;
            blocked.Add(milestone.Name);
            state.Forced = true;
            done.Add(milestone.Name);
            state.Stages.Add("(blocked) " + milestone.Name);
            milestone.Apply(state);
            Close();
        }

        var unreachable = items.All.Select(item => item.Id)
            .Where(id => (!state.Have.ContainsKey(id) || state.ForcedItems.Contains(id)) && ExceptionFor(id, items) is null)
            .OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray();
        var ingredients = crafting.Recipes.SelectMany(recipe => recipe.Ingredients).SelectMany(group => group.Alternatives).ToHashSet();
        var otherUses = OtherMaterialUses(items);
        var deadEnds = items.All.Where(item => item.IsMaterial && item.UseKind == ItemUseKind.Material && item.Id != ItemId.Credits &&
                                               !ingredients.Contains(item.Id) && !DeliberateDeadEnds.ContainsKey(item.Id) &&
                                               !otherUses.Contains(item.Id))
            .Select(item => item.Id).OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray();
        var stationIds = Enum.GetValues<CraftingStationId>().Where(station => station != CraftingStationId.None).ToArray();
        var unbuildable = stationIds.Where(station => !state.StationStage.ContainsKey(station) ||
                                                      state.Stages[state.StationStage[station]].StartsWith("(blocked)", StringComparison.Ordinal))
            .ToArray();

        var fractureStage = state.Stages.IndexOf("Defeated " + EnemyArchetypes.Get(GameSimulation.CoreGateBoss).DisplayName);
        if (fractureStage < 0)
            fractureStage = state.Stages.Count;
        return new Result
        {
            FractureStage = fractureStage,
            HeldForFracture = heldForFracture,
            Stages = state.Stages.ToArray(),
            Items = new Dictionary<ItemId, Reach>(state.Have),
            Stations = new Dictionary<CraftingStationId, int>(state.StationStage),
            BossReach = bossReach,
            EventReach = eventReach,
            Unreachable = unreachable.Where(id => !state.ForcedItems.Contains(id)).ToArray(),
            OnlyAfterBlocked = unreachable.Where(id => state.ForcedItems.Contains(id)).ToArray(),
            DeadEnds = deadEnds,
            UnbuildableStations = unbuildable,
            BlockedMilestones = blocked,
            Cycles = FindCycles(crafting, state),
            LateRecipes = FindLateRecipes(crafting, items, state, recipeStage),
            EarlyGear = state.Have.Where(entry => StageLevel(state.Stages, entry.Value.Stage) < 4 && items[entry.Key].Tier >= 4 &&
                                                  !items[entry.Key].IsMaterial && items[entry.Key].UseKind != ItemUseKind.Material)
                .Select(entry => entry.Key).OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray(),
            Notes = notes,
            Catalog = items,
            Crafting = crafting
        };
    }

    private static string Where(RecipeDefinition recipe) =>
        recipe.RequiredStations is { Count: > 0 } stations ? " @ " + string.Join("+", stations.Select(station => station.DisplayName())) : " (by hand)";

    private static bool Craftable(RecipeDefinition recipe, State state)
    {
        foreach (var station in recipe.RequiredStations ?? new HashSet<CraftingStationId>())
            if (!state.Stations.Contains(station))
                return false;
        foreach (var group in recipe.Ingredients)
            if (!group.Alternatives.Any(state.Got))
                return false;
        // Guide-taught recipes: the reveal material is always one of the ingredients, so carrying it is implied.
        return true;
    }

    // ================================================================== sources

    private static void AddStartRules(List<Rule> rules)
    {
        rules.Add(new Rule("starting kit", _ => true, StarterKits.Standard.Select(entry => entry.Item).ToArray()));
    }

    private static void AddTileRules(List<Rule> rules, IReadOnlyList<TileYield> yields, TileCatalog tiles, ItemCatalog items)
    {
        foreach (var yield in yields)
        {
            var tile = yield.Tile;
            var definition = tiles[tile];
            var power = definition.MinimumToolPower;
            var cutter = ToolParity.IsCutterTile(tile);
            var heart = BlightRules.IsHeart(tile);
            var altar = BlightRules.IsAltar(tile);
            bool CanBreak(State state)
            {
                // Structures behind a door or a guardian: the precursor vault's chests need its key, the dungeon's its gate boss.
                if (tile == TileId.LockedVaultChest && !state.Flags.IsSet("vault.opened"))
                    return false;
                if (tile == TileId.DungeonChest && !state.Defeated.Contains(PhysicsContent.DungeonGateBoss))
                    return false;
                if (cutter)
                    return state.Any(item => item.UseKind == ItemUseKind.Cutter);
                if (heart)
                    return state.Any(item => item.UseKind == ItemUseKind.Demolisher);
                if (altar)
                    return state.Fracture && state.Any(GameSimulation.CanSmashAltars);
                return state.MinePower >= power && state.Any(item => item.UseKind == ItemUseKind.Pickaxe);
            }
            var tool = cutter ? "cut" : heart || altar ? "smash" : power > 0 ? $"mine (power {power})" : "mine";
            if (yield.Before.Count > 0)
                rules.Add(new Rule($"{tool} {definition.Name}", CanBreak, yield.Before.ToArray()));
            var after = yield.After.Except(yield.Before).ToArray();
            if (after.Length > 0)
                rules.Add(new Rule($"{tool} {definition.Name} after the Fracture", state => state.Fracture && CanBreak(state), after));
            // Containers: their whole loot table (the scan only rolls a few times).
            if (ContentTiles.Has(tile, TileTraits.Container))
            {
                var loot = GameSimulation.ContainerLootTable(tile).Select(entry => entry.Item)
                    .Concat(GameSimulation.ChestExtraFinds).Where(items.Contains).ToArray();
                if (loot.Length > 0)
                    rules.Add(new Rule($"open {definition.Name}", CanBreak, loot));
            }
        }
        // Biome materials every generated chest gains (the scan does not open every chest in every biome).
        var biomeMaterials = Enum.GetValues<BiomeZone>().Select(GameSimulation.BiomeChestMaterial).OfType<(ItemId Item, int Min, int Max)>()
            .Select(entry => entry.Item).ToArray();
        rules.Add(new Rule("biome chests", _ => true, biomeMaterials));
    }

    /// <summary>Kinds that only spawn after the Fracture (hard rosters that are not also in a regular roster).</summary>
    private static HashSet<EnemyKind> HardKinds()
    {
        var regular = new HashSet<EnemyKind>();
        foreach (var zone in Enum.GetValues<BiomeZone>())
            regular.UnionWith(BiomeSpawnTables.KindsFor(zone));
        foreach (var slot in new[] { "grass", "under", "cave" })
            regular.UnionWith(BiomeSpawnTables.FillKinds(slot));
        regular.UnionWith(BiomeSpawnTables.DungeonKinds);
        var hard = new HashSet<EnemyKind>();
        foreach (var zone in BiomeSpawnTables.HardZones)
            hard.UnionWith(BiomeSpawnTables.HardKindsFor(zone));
        foreach (var archetype in EnemyArchetypes.All)
            if (CreatureCoverage.Creature(archetype.Kind) is { Hard: true })
                hard.Add(archetype.Kind);
        hard.ExceptWith(regular.Where(kind => CreatureCoverage.Creature(kind) is not { Hard: true }));
        return hard;
    }

    /// <summary>Kinds whose only home is an event roster (they wait for that event).</summary>
    private static Dictionary<EnemyKind, List<WorldEventKind>> EventOnlyKinds(IEnumerable<WorldEventSpec> events)
    {
        var map = new Dictionary<EnemyKind, List<WorldEventKind>>();
        foreach (var spec in events)
        {
            var kinds = spec.Roster.Select(entry => entry.Kind).Concat(CreatureCoverage.EventNatives(spec.Kind))
                .Concat(CreatureCoverage.EventMiniBosses.Where(entry => entry.Event == spec.Kind).Select(entry => entry.MiniBoss));
            foreach (var kind in kinds)
            {
                if (!map.TryGetValue(kind, out var list))
                    map[kind] = list = new List<WorldEventKind>();
                list.Add(spec.Kind);
            }
        }
        return map;
    }

    private static void AddCreatureRules(List<Rule> rules, ItemCatalog items)
    {
        var hard = HardKinds();
        var events = new WorldEventSystem(1).Specs.ToArray();
        var eventOnly = EventOnlyKinds(events);
        var ambient = new HashSet<EnemyKind>();
        foreach (var zone in Enum.GetValues<BiomeZone>())
            ambient.UnionWith(BiomeSpawnTables.KindsFor(zone));
        foreach (var zone in BiomeSpawnTables.HardZones)
            ambient.UnionWith(BiomeSpawnTables.HardKindsFor(zone));
        foreach (var slot in new[] { "grass", "under", "cave" })
            ambient.UnionWith(BiomeSpawnTables.FillKinds(slot));
        ambient.UnionWith(BiomeSpawnTables.DungeonKinds);
        foreach (var archetype in EnemyArchetypes.All)
            if (CreatureCoverage.Creature(archetype.Kind) is { } row && !row.Slot.StartsWith("event:", StringComparison.Ordinal))
                ambient.Add(archetype.Kind);
        // The dungeon opens after its gate boss, the precursor vault with its key.
        var elsewhere = new HashSet<EnemyKind>();
        foreach (var zone in Enum.GetValues<BiomeZone>().Where(zone => zone != BiomeZone.PrecursorVault))
            elsewhere.UnionWith(BiomeSpawnTables.KindsFor(zone));
        foreach (var slot in new[] { "grass", "under", "cave" })
            elsewhere.UnionWith(BiomeSpawnTables.FillKinds(slot));
        var vault = BiomeSpawnTables.KindsFor(BiomeZone.PrecursorVault).Where(kind => !elsewhere.Contains(kind)).ToHashSet();
        var dungeon = BiomeSpawnTables.DungeonKinds.Where(kind => !elsewhere.Contains(kind)).ToHashSet();

        foreach (var archetype in EnemyArchetypes.All)
        {
            var kind = archetype.Kind;
            // Drop rules (0.1.9 C4): chances never block an item; conditions do. Unconditional rules join the kind's own rule,
            // conditional ones get a rule of their own (after the Fracture, during their event); Brutal-only rules are skipped.
            var allRules = NpcDropRegistry.GetDrops(kind).Where(drop => drop.MinimumDifficulty <= WorldDifficulty.Classic).ToArray();
            var drops = allRules.Where(drop => !DropConditionGates(drop)).Select(drop => drop.Item).ToList();
            var conditional = allRules.Where(DropConditionGates).ToArray();
            if (archetype.CreditDrop > 0)
                drops.Add(ItemId.Credits);
            if (archetype.IsBoss)
            {
                if (ArsenalItems.BossPools.TryGetValue(kind, out var pool))
                    drops.AddRange(pool);
                if (PlayerSystemsContent.TrophyFor(kind) is { } trophy)
                    drops.Add(trophy);
                // Late-boss upgrades drop a third of the time on Classic (GrowthItems.UpgradeBosses).
                if (GrowthItems.UpgradeBosses.TryGetValue(kind, out var upgrade))
                    drops.Add(upgrade);
                // OnCreatureDefeatedForEvents extras.
                drops.AddRange(kind switch
                {
                    EnemyKind.StoneSentinel => [ItemId.OverseerCircuit],
                    EnemyKind.AstroGod => [ItemId.AstralOre],
                    EnemyKind.VoidStalker => [ItemId.VoidEssence],
                    _ => Array.Empty<ItemId>()
                });
                // Every boss leaves healing stims of its tier (GameSimulation.DropSustainLoot, outside the salvage cache).
                drops.Add(GameSimulation.BossPotions(archetype.Tier).Stim);
                rules.Add(new Rule($"defeat {archetype.DisplayName}", state => state.Defeated.Contains(kind), drops.Where(items.Contains).ToArray()));
                AddConditionalDropRules(rules, items, $"defeat {archetype.DisplayName}", state => state.Defeated.Contains(kind), conditional);
                continue;
            }
            var afterFracture = new List<ItemId>();
            if (ArsenalItems.MobDrops.TryGetValue(kind, out var mob))
                foreach (var (item, _) in mob)
                    (ArsenalItems.MobDropWaitsForFracture(item) ? afterFracture : drops).Add(item);
            if (archetype.Catchable && Enum.TryParse<ItemId>(kind.ToString(), out var critter) && items.Contains(critter))
                rules.Add(new Rule($"net a {archetype.DisplayName}", state => state.Any(item => item.UseKind == ItemUseKind.BugNet) &&
                                                                          (!hard.Contains(kind) || state.Fracture), [critter]));
            if (PlayerItems.EarnsBanner(kind) && items.Contains(PlayerItems.BannerFor(kind)))
                drops.Add(PlayerItems.BannerFor(kind));
            if (drops.Count == 0 && conditional.Length == 0 && afterFracture.Count == 0)
                continue;
            Func<State, bool> ready;
            string where;
            if (GameSimulation.Obelisks.Any(entry => entry.Obelisk == kind))
            {
                ready = state => state.Defeated.Contains(GameSimulation.RitualBoss);
                where = "in the obelisk sieges";
            }
            else if (CreatureCoverage.Creature(kind) is { Slot: "dungeon-hard" })
            {
                ready = state => state.Defeated.Contains(GameSimulation.BulbBoss);
                where = "in the late dungeon";
            }
            else if (vault.Contains(kind))
            {
                ready = state => state.Flags.IsSet("vault.opened");
                where = "in the precursor vault";
            }
            else if (dungeon.Contains(kind))
            {
                ready = state => state.Defeated.Contains(PhysicsContent.DungeonGateBoss) && (!hard.Contains(kind) || state.Fracture);
                where = "in the dungeon";
            }
            else if (!ambient.Contains(kind) && eventOnly.TryGetValue(kind, out var during))
            {
                var hardEvent = hard.Contains(kind);
                ready = state => during.Any(state.Events.Contains) && (!hardEvent || state.Fracture);
                where = "during " + string.Join("/", during);
            }
            else if (hard.Contains(kind))
            {
                ready = state => state.Fracture;
                where = "after the Fracture";
            }
            else
            {
                ready = _ => true;
                where = "in the wild";
            }
            rules.Add(new Rule($"kill {archetype.DisplayName} ({where})", ready, drops.Where(items.Contains).Distinct().ToArray()));
            if (afterFracture.Count > 0)
                rules.Add(new Rule($"kill {archetype.DisplayName} ({where}) after the Fracture", state => ready(state) && state.Fracture,
                    afterFracture.Where(items.Contains).ToArray()));
            AddConditionalDropRules(rules, items, $"kill {archetype.DisplayName} ({where})", ready, conditional);
        }
        // Essences: any creature after the Fracture in the right place.
        rules.Add(new Rule("Fracture essences", state => state.Fracture, [ItemId.FlightEssence, ItemId.PrismEssence, ItemId.BlightEssence]));
        // Hearts and charge orbs: any hostile kill may leave one (instant pickups; GameSimulation.DropSustainLoot).
        rules.Add(new Rule("hearts and charge orbs from kills", _ => true, [ItemId.HeartPickup, ItemId.ChargeOrb]));
    }

    /// <summary>Conditions that delay a drop in a Classic run: the Fracture and a running event (night and day come round).</summary>
    private static bool DropConditionGates(NpcDropDefinition drop) =>
        (drop.Conditions & (DropCondition.Fracture | DropCondition.DuringEvent)) != 0;

    private static void AddConditionalDropRules(List<Rule> rules, ItemCatalog items, string source, Func<State, bool> ready,
        IEnumerable<NpcDropDefinition> conditional)
    {
        foreach (var drop in conditional.Where(drop => items.Contains(drop.Item)))
        {
            var fracture = drop.Conditions.HasFlag(DropCondition.Fracture);
            var during = drop.Conditions.HasFlag(DropCondition.DuringEvent) ? drop.Event : null;
            rules.Add(new Rule($"{source}{(fracture ? " after the Fracture" : "")}{(during is { } kind ? $" during {kind}" : "")}",
                state => ready(state) && (!fracture || state.Fracture) && (during is not { } e || state.Events.Contains(e)), [drop.Item]));
        }
    }

    /// <summary>When each resident can move in (GameSimulation.Town RefreshUnlockFlags and friends).</summary>
    private static Func<State, bool> ResidentReady(TownNpcDefinition npc, ItemCatalog items)
    {
        bool Boss(State state, EnemyKind kind) => state.Defeated.Contains(kind);
        return npc.Kind switch
        {
            TownNpcKind.StationAi => _ => true,
            TownNpcKind.Quartermaster => state => state.Got(ItemId.Credits),
            TownNpcKind.Medic => state => state.Got(ItemId.LifeCore),
            TownNpcKind.Armorer => state => state.Any(item => item.EffectiveClass == DamageClass.Ranged && item.UseKind == ItemUseKind.Ranged &&
                                                              item.Id != ItemId.CopperBow),
            TownNpcKind.ChiefEngineer => state => Boss(state, EnemyKind.StoneSentinel),
            TownNpcKind.AstroBotanist => state => state.Got(ItemId.HydroponicsTray),
            TownNpcKind.HeadResearcher => state => state.Flags.Get("hearts.broken") > 0,
            TownNpcKind.TradeConsul => _ => true,
            TownNpcKind.TideWarden => state => state.Any(item => item.FishingPower > 0),
            TownNpcKind.Isotopist => state => Boss(state, GameSimulation.CoreGateBoss),
            TownNpcKind.Aeronaut => state => GameSimulation.HardTrio.Any(kind => Boss(state, kind)),
            TownNpcKind.RelicWarden => state => Boss(state, GameSimulation.BulbBoss),
            TownNpcKind.Greenskeeper => state => state.Got(ItemId.GolfBall),
            TownNpcKind.PigmentTrader => state => state.Have.Keys.Any(TownContent.IsDyeMaterial),
            // Rescued survivors (bound in world structures), the merchant, town gels, the zoologist and the party planner
            // need only exploration and a growing colony.
            _ => _ => true
        };
    }

    private static void AddShopRules(List<Rule> rules, ItemCatalog items)
    {
        foreach (var npc in TownNpcCatalog.All)
        {
            var resident = ResidentReady(npc, items);
            foreach (var group in npc.Shop.Where(entry => items.Contains(entry.Item)).GroupBy(entry => entry.RequiredFlag))
            {
                var flag = group.Key;
                rules.Add(new Rule($"buy from the {npc.Role}" + (flag is null ? string.Empty : $" ({flag})"),
                    state => state.Got(ItemId.Credits) && resident(state) && (flag is null || state.Flags.IsSet(flag)),
                    group.Select(entry => entry.Item).ToArray()));
            }
        }
    }

    private static void AddMiscRules(List<Rule> rules, ItemCatalog items)
    {
        // Fishing: every catch table, with the right rod for acid and liquid uranium.
        var water = new HashSet<ItemId>();
        var acid = new HashSet<ItemId>();
        var lava = new HashSet<ItemId>();
        var afterFracture = new HashSet<ItemId>();
        var random = new Random(7);
        foreach (var liquid in new[] { LiquidKind.Water, LiquidKind.Acid, LiquidKind.Lava, LiquidKind.Nectar, LiquidKind.Fuel })
        foreach (var zone in Enum.GetValues<BiomeZone>())
        foreach (var depth in Enum.GetValues<FishingDepth>())
        foreach (var night in new[] { false, true })
        foreach (var rarity in Enum.GetValues<FishingRarity>())
        foreach (var power in new[] { 20f, 80f })
        foreach (var fractured in new[] { false, true })
        {
            var context = new FishingContext(liquid, zone, depth, FishingTables.FullPoolSize, power, night, 1f, Fractured: fractured);
            var set = fractured ? afterFracture : liquid == LiquidKind.Acid ? acid : liquid == LiquidKind.Lava ? lava : water;
            for (var i = 0; i < 6; i++)
                set.Add(FishingTables.Pick(context, rarity, random));
        }
        // Catches that only bite after the Fracture (the Astral crate) in any liquid with a fitting rod.
        afterFracture.ExceptWith(water.Concat(acid).Concat(lava));
        rules.Add(new Rule("fish after the Fracture", state => state.Fracture && state.Any(item => item.FishingPower > 0),
            afterFracture.Where(items.Contains).ToArray()));
        rules.Add(new Rule("fish", state => state.Any(item => item.FishingPower > 0), water.Where(items.Contains).ToArray()));
        rules.Add(new Rule("fish in acid", state => state.Got(ItemId.AcidFishingRod) || state.Any(item => item.ImmunityTags.Contains("acid-fishing")),
            acid.Where(items.Contains).ToArray()));
        rules.Add(new Rule("fish in liquid uranium", state => state.Got(ItemId.MagmaFishingRod) || state.Any(item => item.ImmunityTags.Contains("lava-fishing")),
            lava.Where(items.Contains).ToArray()));
        foreach (var (fish, _, unlock) in FishingTables.QuestFish)
            rules.Add(new Rule("quest fish", state => state.Any(item => item.FishingPower > 0) && (unlock is null || state.Flags.IsSet(unlock)), [fish]));
        rules.Add(new Rule("Tide-Warden quests", state => state.Any(item => item.FishingPower > 0),
            [ItemId.FerriteFishingRod, ItemId.FishingLine, ItemId.FishingLure, ItemId.AcidWader, ItemId.CatchScanner, ItemId.AlloyCrate]));
        // The 30th quest's Astral fishing rod is paid once the Fracture has happened (GameSimulation.TurnInQuestFish).
        rules.Add(new Rule("Tide-Warden quests after the Fracture", state => state.Fracture && state.Any(item => item.FishingPower > 0),
            [ItemId.AstralFishingRod]));
        // Crates (fished, dropped or rewarded) open into their loot.
        foreach (var crate in items.All.Where(item => item.IsCrate))
        {
            var before = FishingTables.CrateLoot(crate.Id, fractured: false, obelisksDown: false).Select(entry => entry.Item).Where(items.Contains).ToArray();
            var after = FishingTables.CrateLoot(crate.Id, fractured: true, obelisksDown: false).Select(entry => entry.Item).Where(items.Contains)
                .Except(before).ToArray();
            var astral = FishingTables.CrateLoot(crate.Id, fractured: true).Select(entry => entry.Item).Where(items.Contains)
                .Except(before).Except(after).ToArray();
            if (before.Length > 0)
                rules.Add(new Rule($"open {crate.Name}", state => state.Got(crate.Id), before));
            if (after.Length > 0)
                rules.Add(new Rule($"open {crate.Name} after the Fracture", state => state.Fracture && state.Got(crate.Id), after));
            if (astral.Length > 0)
                rules.Add(new Rule($"open {crate.Name} after the obelisks", state => state.Flags.IsSet(FishingTables.AstralCrateFlag) && state.Got(crate.Id),
                    astral));
        }
        // Boss salvage caches (Brutal+) open into the boss loot; the core-chip is the cache's own.
        foreach (var archetype in EnemyArchetypes.All.Where(archetype => archetype.IsBoss))
            if (PlayerSystemsContent.BagFor(archetype.Kind) is { } bag && PlayerSystemsContent.ExclusiveFor(archetype.Kind) is { } exclusive)
                rules.Add(new Rule($"open {items[bag].Name}", state => state.Got(bag), [exclusive]));
        // The sediment sifter.
        foreach (var (sediment, tier) in PlayerSystemsContent.Sediments)
            rules.Add(new Rule("sift " + sediment, state => state.Got(sediment) && state.Got(ItemId.Sifter),
                PlayerSystemsContent.SiftTable.Where(entry => entry.Tier <= tier && !entry.Fractured).Select(entry => entry.Item).ToArray()));
        foreach (var (sediment, tier) in PlayerSystemsContent.Sediments)
            rules.Add(new Rule("sift " + sediment + " after the Fracture", state => state.Fracture && state.Got(sediment) && state.Got(ItemId.Sifter),
                PlayerSystemsContent.SiftTable.Where(entry => entry.Tier <= tier).Select(entry => entry.Item).ToArray()));
        // The phase pool transmutes what is dropped into it.
        foreach (var item in items.All)
            if (GameSimulation.PhaseTransmute(item.Id) is { } shifted)
                rules.Add(new Rule($"phase pool ({item.Name})", state => state.Got(item.Id), [shifted]));
        // Supply caches and salvage pods (natural tiles) roll from their whole tables.
        rules.Add(new Rule("supply caches and salvage pods", state => state.Any(item => item.UseKind == ItemUseKind.Pickaxe),
            GameSimulation.SupplyLootItems.Where(items.Contains).Distinct().ToArray()));
        // Hearts give a fragment each; smashed altars give void essence.
        rules.Add(new Rule("break a heart", state => state.Any(item => item.UseKind == ItemUseKind.Demolisher), [ItemId.HeartFragment]));
        rules.Add(new Rule("smash an altar", state => state.Fracture && state.Any(GameSimulation.CanSmashAltars), [ItemId.VoidEssence]));
        // A blank audio deck records whatever theme is playing.
        rules.Add(new Rule("record an audio deck", state => state.Got(ItemId.AudioDeck), LeisureContent.Decks.Select(deck => deck.Item).ToArray()));
        // Herbs: wild herbs grow on their soils and drop seeds when harvested in bloom.
        foreach (var herb in TilePhysics.AllHerbs)
            rules.Add(new Rule($"harvest {herb.Herb}", state => state.Grounds.Overlaps(herb.Soils) || state.Grounds.Contains(herb.Tile) || state.Got(herb.Seeds),
                [herb.Herb, herb.Seeds]));
        // Saplings grow each biome's tree on its ground; felling it gives that timber.
        foreach (var species in ContentFillFlora.Species)
            rules.Add(new Rule($"grow and fell a {species.TrunkName}", state => state.Got(ItemId.Sapling) && state.Grounds.Overlaps(species.Grounds) &&
                                                                              state.Any(item => item.UseKind == ItemUseKind.Cutter), [species.Timber]));
        // Wild plants (fibre tufts, prism blooms...) sprout on their grounds.
        foreach (var (_, name, drop, grounds, _) in ContentFillFlora.WildPlants)
            rules.Add(new Rule($"pick a wild {name}", state => state.Grounds.Overlaps(grounds), [drop]));
        // After the Fracture, underground canopy ground sprouts lifebloom pods (Terraria: life fruit).
        rules.Add(new Rule("find a lifebloom pod", state => state.Fracture &&
                                                           state.Grounds.Overlaps([TileId.Mud, TileId.CanopyGrass, TileId.JungleGrass]), [ItemId.LifebloomPod]));
        // A meteor fall leaves meteorite in the world.
        rules.Add(new Rule("mine a fallen meteorite", state => state.Events.Contains(WorldEventKind.MeteorFall) &&
                                                               state.MinePower >= MeteoritePower, [ItemId.MeteoriteOre]));
        // Buckets fill from pools; wiring tools return what they remove.
        rules.Add(new Rule("fill a bucket", state => state.Got(ItemId.EmptyBucket), [ItemId.BucketWater, ItemId.BucketLava, ItemId.BucketFuel]));
    }

    private static void AddEventRules(List<Rule> rules, IReadOnlyList<WorldEventSpec> events, ItemCatalog items)
    {
        foreach (var spec in events)
            if (spec.Rewards is { Length: > 0 } rewards)
                rules.Add(new Rule($"clear {spec.Name}", state => state.Events.Contains(spec.Kind),
                    rewards.Select(reward => reward.Item).Where(items.Contains).ToArray()));
    }

    private static bool EventAvailable(WorldEventSpec spec, State state, AuditEventHost host, ItemCatalog items)
    {
        // Eligible under some weather and time of day.
        var eligible = false;
        foreach (var variant in AuditEventHost.Variants)
        {
            host.Use(variant);
            try
            {
                eligible |= spec.Eligible(host);
            }
            catch (Exception)
            {
                // A condition reading host state the audit does not model counts as not met.
            }
        }
        if (!eligible)
            return false;
        var natural = spec.Category == WorldEventCategory.Weather || spec.Moment != WorldEventMoment.Never && spec.Chance > 0f;
        // Started by the world rather than a roll: the light hoard, the meteor after a heart, the obelisk sieges.
        var worldTriggered = spec.Kind switch
        {
            WorldEventKind.LumenTempest => true,
            WorldEventKind.MeteorFall => state.Flags.Get("hearts.broken") > 0,
            _ => GameSimulation.Obelisks.Any(entry => entry.Event == spec.Kind)
        };
        return natural || worldTriggered || state.Have.Keys.Any(id => items[id].StartsEvent == spec.Kind);
    }

    /// <summary>Material ids with a use besides recipes: sifter sediment, bait, ammo, a boss or event summon, a vault key...</summary>
    private static HashSet<ItemId> OtherMaterialUses(ItemCatalog items)
    {
        var uses = new HashSet<ItemId>(PlayerSystemsContent.Sediments.Keys);
        foreach (var item in items.All)
        {
            if (item.BaitPower > 0 || item.SummonsBoss is not null || item.StartsEvent is not null || item.UseKind == ItemUseKind.Ammunition ||
                item.PlacedTile != TileId.Air || item.PlacedWall != 0 || GameSimulation.PhaseTransmute(item.Id) is not null)
                uses.Add(item.Id);
        }
        uses.UnionWith(items.All.Select(item => item.Id).Where(TownContent.IsDyeMaterial));
        uses.UnionWith([ItemId.VaultKey, ItemId.AncientPowerCell, ItemId.HeartFragment, ItemId.AudioDeck]);
        uses.UnionWith(FishingTables.QuestFish.Select(entry => entry.Fish)); // turned in to the Tide-Warden
        uses.UnionWith(LeisureContent.Paints); // the paintbrush and roller spend them
        return uses;
    }

    // ================================================================== milestones

    private static List<Milestone> BuildMilestones(ItemCatalog items, TileCatalog tiles, IReadOnlyList<TileYield> yields)
    {
        var list = new List<Milestone>();
        // 1. Stations: built as soon as their placeable is in hand.
        var stationItems = items.All.Where(item => item.UseKind == ItemUseKind.PlaceTile && tiles.Contains(item.PlacedTile) &&
                                                   tiles[item.PlacedTile].Station != CraftingStationId.None)
            .GroupBy(item => tiles[item.PlacedTile].Station).ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToArray());
        foreach (var (station, placeables) in stationItems.OrderBy(entry => entry.Key))
            list.Add(new Milestone("Built " + station.DisplayName(), 100 + (int)station,
                state => placeables.Any(state.Got) || state.Stations.Contains(station),
                state =>
                {
                    state.Stations.Add(station);
                    state.StationStage.TryAdd(station, state.Stage);
                    foreach (var implied in station.ImpliedClosure())
                    {
                        state.Stations.Add(implied);
                        state.StationStage.TryAdd(implied, state.Stage);
                    }
                }));
        // 2. Drill power gates of the tiles actually in the world.
        foreach (var power in yields.Select(yield => tiles[yield.Tile].MinimumToolPower).Append(MeteoritePower).Where(power => power > 0).Distinct().Order())
            list.Add(new Milestone($"Drill power {power}", power, state => state.DrillPower >= power, state => state.MinePower = power));
        // 3. Flags the world sets as it is explored.
        list.Add(new Milestone("Hearts broken", 2000, state => state.Any(item => item.UseKind == ItemUseKind.Demolisher),
            state => state.Flags.Set("hearts.broken")));
        list.Add(new Milestone("Vault opened", 2100, state => state.Got(ItemId.VaultKey), state => state.Flags.Set("vault.opened")));
        list.Add(new Milestone("Altars smashed", 2200, state => state.Fracture && state.Any(GameSimulation.CanSmashAltars),
            state => state.Flags.Set("altars.smashed")));
        list.Add(new Milestone("Voidsteel found", 2300, state => state.Got(ItemId.VoidsteelOre) || state.Got(ItemId.VoidsteelBar),
            state => state.Flags.Set("ore.voidsteel")));
        list.Add(new Milestone("Fishing quests", 2400, state => state.Any(item => item.FishingPower > 0), state => state.Flags.Set("quests.fishing")));
        list.Add(new Milestone("Colony party", 2500, _ => true, state => state.Flags.Set("party.thrown")));
        list.Add(new Milestone("Obelisks down", 2550, state => state.Defeated.Contains(GameSimulation.RitualBoss), state => state.Flags.Set("obelisks.down")));
        // 4. Bosses, in progression order, then the rest by tier.
        var order = EnemyArchetypes.BossProgression.ToList();
        foreach (var archetype in EnemyArchetypes.All.Where(archetype => archetype.IsBoss && !order.Contains(archetype.Kind))
                     .OrderBy(archetype => archetype.Tier).ThenBy(archetype => archetype.HitPoints))
            order.Add(archetype.Kind);
        var summons = items.All.Where(item => item.SummonsBoss is not null).GroupBy(item => item.SummonsBoss!.Value)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToArray());
        for (var index = 0; index < order.Count; index++)
        {
            var kind = order[index];
            if (kind == EnemyKind.DungeonGuardian)
                continue;
            var archetype = EnemyArchetypes.Get(kind);
            var called = summons.GetValueOrDefault(kind) ?? [];
            list.Add(new Milestone("Defeated " + archetype.DisplayName, 3000 + index,
                state => !state.Defeated.Contains(kind) && BossGateOpen(kind, state) && (called.Any(state.Got) || NaturalBoss(kind, state)),
                state => DefeatBoss(kind, state)) { Boss = kind });
        }
        // 5. Events clear (rewards, unlock flags).
        foreach (var spec in new WorldEventSystem(1).Specs)
            list.Add(new Milestone("Cleared " + spec.Name, 2600 + (int)spec.Kind, state => state.Events.Contains(spec.Kind) && !state.Flags.IsSet(spec.FlagName),
                state => state.Flags.Set(spec.FlagName)));
        return list;
    }

    /// <summary>HardmodeSummonRefusal and the coverage refusals, as world-state conditions.</summary>
    private static bool BossGateOpen(EnemyKind boss, State state)
    {
        if (state.HeldForFracture.Contains(boss) && !state.Fracture)
            return false;
        if (boss == GameSimulation.CoreGateBoss)
            return state.Defeated.Contains(GameSimulation.LastEasyBoss);
        if ((GameSimulation.HardTrio.Contains(boss) || GameSimulation.FractureGatedBosses.Contains(boss) ||
             boss is EnemyKind.GelSovereign or EnemyKind.HollowRegent or EnemyKind.RimeSeraph) && !state.Fracture)
            return false;
        if (boss == EnemyKind.AstroGod && !state.Flags.IsSet("obelisks.down"))
            return false;
        // 0.2.2 lifecycle gates (PC-1, PC-2): the Moon Eater (lure and Rust Moon finale) waits for the Fracture, and the
        // Protocol Dragon (key and Defense Protocol finale) for the Vault Colossus.
        if (boss == EnemyKind.MoonEater && !state.Fracture)
            return false;
        if (boss == EnemyKind.ProtocolDragon && !state.Defeated.Contains(EnemyKind.VaultColossus))
            return false;
        if (boss == GameSimulation.BulbBoss && !GameSimulation.HardTrio.All(state.Defeated.Contains))
            return false;
        if (boss == EnemyKind.VaultColossus && !state.Flags.IsSet("vault.opened"))
            return false;
        if (boss == GameSimulation.RitualBoss && !state.Defeated.Contains(EnemyKind.VaultColossus))
            return false;
        if (GameSimulation.Obelisks.Any(entry => entry.Obelisk == boss) && !state.Defeated.Contains(GameSimulation.RitualBoss))
            return false;
        return true;
    }

    /// <summary>Bosses that come without a summon item: patrols, hearts, altars, dusk incursions, events and the vault altar.</summary>
    private static bool NaturalBoss(EnemyKind boss, State state)
    {
        if (boss == EnemyKind.StoneSentinel)
            return state.Got(ItemId.LifeCore); // the Overseer patrols to a colony with 140+ max HP and some armour
        if (boss == EnemyKind.FalloutHivemind)
            return !state.Fracture && state.Any(item => item.UseKind == ItemUseKind.Demolisher);
        if (boss == EnemyKind.CyberSwarmLeader)
            return state.Fracture && state.Any(item => item.UseKind == ItemUseKind.Demolisher);
        if (GameSimulation.HardTrio.Contains(boss))
            return state.Fracture; // undefeated hard-tier bosses come on their own at dusk
        if (EnemyArchetypes.EventBosses.Contains(boss))
            return state.Fracture && state.Defeated.Contains(EnemyKind.ScoutMech); // the dusk incursion waits for the Fracture (PC-4)
        if (boss == EnemyKind.PrismEmpress)
            return state.Fracture && state.Any(GameSimulation.CanSmashAltars);
        if (boss == EnemyKind.BloomMatriarch)
            return state.Fracture; // brood pods in the bloom hives
        if (boss == EnemyKind.VaultColossus)
            return state.Got(ItemId.AncientPowerCell);
        if (boss == GameSimulation.RitualBoss)
            return true; // the cultists gather once the Vault Colossus has fallen
        if (GameSimulation.Obelisks.Any(entry => entry.Obelisk == boss))
            return true;
        if (boss == EnemyKind.AbyssLeviathan) // PC-3: the deep stays quiet until the Fracture (GameSimulation.RollCatch)
            return state.Fracture && state.Got(ItemId.AbyssBait) && (state.Got(ItemId.AcidFishingRod) || state.Any(item => item.ImmunityTags.Contains("acid-fishing")));
        foreach (var spec in new WorldEventSystem(1).Specs)
            if (spec.FinalBoss == boss && state.Events.Contains(spec.Kind))
                return true;
        foreach (var (kind, miniBoss, _) in CreatureCoverage.EventMiniBosses)
            if (miniBoss == boss && state.Events.Contains(kind))
                return true;
        return false;
    }

    private static void DefeatBoss(EnemyKind boss, State state)
    {
        state.Defeated.Add(boss);
        state.Flags.Set($"boss.{WorldFlags.Key(boss)}.down");
        if (boss == GameSimulation.CoreGateBoss)
        {
            state.Fracture = true;
            state.Flags.Set("blight.fracture");
        }
        if (GameSimulation.HardTrio.All(state.Defeated.Contains))
            state.Flags.Set("hardmode.trio.down");
        if (boss == EnemyKind.VaultColossus)
            state.Flags.Set("ritual.ready");
        if (GameSimulation.Obelisks.All(entry => state.Defeated.Contains(entry.Obelisk)))
            state.Flags.Set("obelisks.down");
    }

    // ================================================================== the world scan

    private static IReadOnlyList<TileYield> ScanWorlds(int width, int height, List<string> notes)
    {
        var byTile = new Dictionary<TileId, TileYield>();
        foreach (var blight in new[] { WorldBlight.Radiated, WorldBlight.Overgrowth })
        {
            var game = new GameSimulation(seed: 20261028 + (int)blight, width: width, height: height, blight: blight) { FamilySpawnsEnabled = false };
            ScanPass(game, byTile, after: false);
            game.TriggerFracture();
            // Smashed altars seed the hardmode ores (as in Terraria), so smash a few before the second pass.
            var altars = Enumerable.Range(0, game.World.Width * game.World.Height)
                .Where(index => BlightRules.IsAltar(game.World.Cells[index].TileId))
                .Select(index => new GridPoint(index % game.World.Width, index / game.World.Width)).Take(12).ToArray();
            foreach (var altar in altars)
                if (BlightRules.IsAltar(game.World.GetCell(altar).TileId))
                    game.ForceBreakTile(altar);
            ScanPass(game, byTile, after: true);
        }
        notes.Add($"World scan: {byTile.Count} natural tile kinds in two {width}x{height} worlds (radiated and overgrowth).");
        return byTile.Values.OrderBy(yield => yield.Tile).ToArray();
    }

    private static void ScanPass(GameSimulation game, Dictionary<TileId, TileYield> byTile, bool after)
    {
        const int SamplesPerTile = 6;
        var world = game.World;
        var seen = new Dictionary<TileId, List<GridPoint>>();
        var counts = new Dictionary<TileId, int>();
        foreach (var index in Enumerable.Range(0, world.Width * world.Height))
        {
            var tile = world.Cells[index].TileId;
            if (tile is TileId.Air or TileId.Bedrock)
                continue;
            counts[tile] = counts.GetValueOrDefault(tile) + 1;
        }
        // Spread the samples over each tile's occurrences.
        var stride = counts.ToDictionary(entry => entry.Key, entry => Math.Max(1, entry.Value / SamplesPerTile));
        var running = new Dictionary<TileId, int>();
        for (var index = 0; index < world.Width * world.Height; index++)
        {
            var tile = world.Cells[index].TileId;
            if (tile is TileId.Air or TileId.Bedrock)
                continue;
            var n = running[tile] = running.GetValueOrDefault(tile) + 1;
            if (n % stride[tile] != 0)
                continue;
            if (!seen.TryGetValue(tile, out var list))
                seen[tile] = list = new List<GridPoint>();
            if (list.Count < SamplesPerTile)
                list.Add(new GridPoint(index % world.Width, index / world.Width));
        }
        foreach (var (tile, points) in seen)
        {
            if (!byTile.TryGetValue(tile, out var yield))
                byTile[tile] = yield = new TileYield(tile, new HashSet<ItemId>(), new HashSet<ItemId>());
            if (!after)
                yield.OnlyAfter = false;
            var target = after ? yield.After : yield.Before;
            foreach (var point in points)
            {
                if (world.GetCell(point).TileId != tile)
                    continue;
                game.ClearAuditLoot();
                game.ForceBreakTile(point);
                foreach (var stack in game.Player.Inventory.Slots.Where(stack => !stack.IsEmpty))
                    target.Add(stack.ItemId);
                foreach (var ground in game.GroundItems)
                    target.Add(ground.ItemId);
            }
        }
        game.ClearAuditLoot();
    }

    // ================================================================== findings

    private static IReadOnlyList<string> FindCycles(CraftingSystem crafting, State state)
    {
        // Strongly connected components of the "ingredient -> output" graph among items the run never reached:
        // a chain that only feeds itself.
        var unreached = crafting.Recipes.Select(recipe => recipe.Output).Where(id => !state.Have.ContainsKey(id) || state.ForcedItems.Contains(id))
            .ToHashSet();
        var edges = new Dictionary<ItemId, HashSet<ItemId>>();
        foreach (var recipe in crafting.Recipes.Where(recipe => unreached.Contains(recipe.Output)))
        foreach (var item in recipe.Ingredients.SelectMany(group => group.Alternatives).Where(unreached.Contains))
        {
            if (!edges.TryGetValue(item, out var set))
                edges[item] = set = new HashSet<ItemId>();
            set.Add(recipe.Output);
        }
        var cycles = new List<string>();
        var index = 0;
        var indices = new Dictionary<ItemId, int>();
        var low = new Dictionary<ItemId, int>();
        var stack = new Stack<ItemId>();
        var onStack = new HashSet<ItemId>();
        void Connect(ItemId node)
        {
            indices[node] = low[node] = index++;
            stack.Push(node);
            onStack.Add(node);
            foreach (var next in edges.GetValueOrDefault(node) ?? [])
            {
                if (!indices.ContainsKey(next))
                {
                    Connect(next);
                    low[node] = Math.Min(low[node], low[next]);
                }
                else if (onStack.Contains(next))
                    low[node] = Math.Min(low[node], indices[next]);
            }
            if (low[node] != indices[node])
                return;
            var component = new List<ItemId>();
            ItemId member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                component.Add(member);
            } while (member != node);
            if (component.Count > 1 || edges.GetValueOrDefault(node)?.Contains(node) == true)
                cycles.Add(string.Join(" <-> ", component.OrderBy(id => id.ToString(), StringComparer.Ordinal)));
        }
        foreach (var node in edges.Keys.ToArray())
            if (!indices.ContainsKey(node))
                Connect(node);
        return cycles;
    }

    /// <summary>Progress level a stage stands for: 3 before the Fracture, 4 after it, 5 / 6 at the lumenite / voidsteel drill gates, 7 after the final boss.</summary>
    private static int StageLevel(IReadOnlyList<string> stages, int stage)
    {
        bool fractured = false, lumenite = false, voidsteel = false, final = false;
        for (var i = 0; i <= stage && i < stages.Count; i++)
        {
            var name = stages[i].Replace("(blocked) ", string.Empty, StringComparison.Ordinal);
            fractured |= name == "Defeated " + EnemyArchetypes.Get(GameSimulation.CoreGateBoss).DisplayName;
            lumenite |= fractured && name == "Drill power 85";
            voidsteel |= fractured && name == "Drill power 100";
            final |= name == "Defeated " + EnemyArchetypes.Get(EnemyKind.AstroGod).DisplayName;
        }
        // The drill gates only count when passed after the Fracture: a drill a pre-Fracture boss drops neither makes its
        // stage post-Fracture nor makes every recipe at the Fracture look late (PC-7).
        return !fractured ? 3 : final ? 7 : voidsteel ? 6 : lumenite ? 5 : 4;
    }

    private static IReadOnlyList<LateRecipe> FindLateRecipes(CraftingSystem crafting, ItemCatalog items, State state,
        IReadOnlyDictionary<string, int> recipeStage)
    {
        var late = new List<LateRecipe>();
        foreach (var recipe in crafting.Recipes)
        {
            if (!recipeStage.TryGetValue(recipe.Id, out var stage) || !state.Have.TryGetValue(recipe.Output, out var reach) ||
                reach.Stage != stage || !reach.Via.StartsWith("craft " + recipe.Id + " ", StringComparison.Ordinal))
                continue;
            var tier = items[recipe.Output].Tier;
            var level = StageLevel(state.Stages, stage);
            if (level <= Math.Max(3, tier))
                continue;
            // Name what holds it back: the latest station or ingredient.
            var causes = new List<(int Stage, string What)>();
            foreach (var station in recipe.RequiredStations ?? new HashSet<CraftingStationId>())
                if (state.StationStage.TryGetValue(station, out var s))
                    causes.Add((s, station.DisplayName()));
            foreach (var group in recipe.Ingredients)
            {
                var best = group.Alternatives.Where(state.Have.ContainsKey).Select(id => (state.Have[id].Stage, items[id].Name))
                    .OrderBy(entry => entry.Stage).FirstOrDefault();
                causes.Add(best);
            }
            var worst = causes.OrderByDescending(entry => entry.Stage).First();
            late.Add(new LateRecipe(recipe.Id, recipe.Output, tier, state.Stages[stage], worst.What));
        }
        return late;
    }

    // ================================================================== report

    /// <summary>`--craft-audit [--items]`: prints the report. Exit code 1 when anything is unreachable.</summary>
    public static int Report(string[] args)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = Run();
        var items = result.Catalog;
        Console.WriteLine($"Crafting audit ({watch.Elapsed.TotalSeconds:0.0}s): {items.All.Count()} items, {result.Crafting.Recipes.Count} recipes " +
                          $"({result.Crafting.Recipes.Count(recipe => recipe.RequiredStations is not { Count: > 0 })} by hand).");
        foreach (var note in result.Notes)
            Console.WriteLine("  " + note);
        Console.WriteLine();
        Console.WriteLine("Stages (in the order the run reached them):");
        for (var i = 0; i < result.Stages.Count; i++)
        {
            var arrived = result.Items.Values.Count(reach => reach.Stage == i);
            Console.WriteLine($"  {i,3}. {result.Stages[i]}  (+{arrived} items)");
        }
        Console.WriteLine();
        Console.WriteLine("Stations:");
        foreach (var station in Enum.GetValues<CraftingStationId>().Where(station => station != CraftingStationId.None))
        {
            var stage = result.Stations.TryGetValue(station, out var s) ? $"{s,3}. {result.Stages[s]}" : "     NEVER";
            var placeable = items.All.FirstOrDefault(item => item.UseKind == ItemUseKind.PlaceTile && new TileCatalog().Contains(item.PlacedTile) &&
                                                             new TileCatalog()[item.PlacedTile].Station == station);
            var via = placeable is not null && result.Items.TryGetValue(placeable.Id, out var reach) ? reach.Via : "-";
            Console.WriteLine($"  {station.DisplayName(),-22} {stage,-44} via {via}");
        }
        Console.WriteLine();
        Console.WriteLine("Station recipes:");
        var tileCatalog = new TileCatalog();
        foreach (var item in items.All.Where(item => item.UseKind == ItemUseKind.PlaceTile && tileCatalog.Contains(item.PlacedTile) &&
                                                     tileCatalog[item.PlacedTile].Station != CraftingStationId.None))
        {
            var recipes = result.Crafting.FindByOutput(item.Id);
            Console.WriteLine($"  {item.Name} ({tileCatalog[item.PlacedTile].Station.DisplayName()}): " + (recipes.Count == 0 ? "no recipe" :
                string.Join(" | ", recipes.Select(recipe => string.Join(" + ", recipe.Ingredients.Select(group =>
                    $"{group.Quantity} {string.Join("/", group.Alternatives.Select(id => items[id].Name))}")) + Where(recipe)))));
            Console.WriteLine($"      used by {result.Crafting.FindByStation(tileCatalog[item.PlacedTile].Station).Count} recipes");
        }
        Console.WriteLine();
        var hand = result.Crafting.Recipes.Where(recipe => recipe.RequiredStations is not { Count: > 0 }).ToArray();
        Console.WriteLine($"Hand recipes ({hand.Length}): " + string.Join(", ", hand.Select(recipe => $"{items[recipe.Output].Name} x{recipe.OutputCount} <= " +
            string.Join(" + ", recipe.Ingredients.Select(group => $"{group.Quantity} {string.Join("/", group.Alternatives.Select(id => items[id].Name))}")) +
            (recipe.RequiredLiquids is { Count: > 0 } liquids ? $" (near {string.Join("/", liquids)})" : string.Empty))));
        Console.WriteLine($"Hull panels feed {result.Crafting.FindByIngredient(ItemId.WoodBlock).Count} recipes; timber feeds " +
                          $"{result.Crafting.FindByIngredient(ItemId.Timber).Count}.");
        Console.WriteLine();
        var opening = RunOpening(result.Crafting, items);
        string Stations(IEnumerable<CraftingStationId> stations) => string.Join(", ", stations.Select(station => station.DisplayName()));
        Console.WriteLine($"Opening, kit + one felled tree: stations [{Stations(opening.StationsFromTree)}], {opening.FromTree.Count} items");
        Console.WriteLine("  " + string.Join(", ", opening.FromTree.Select(id => items[id].Name).Order()));
        Console.WriteLine($"Opening, + surface digging: stations [{Stations(opening.StationsFromSurface)}], {opening.FromSurface.Count} items");
        Console.WriteLine();
        Print("Unreachable items", result.Unreachable.Select(id => $"{id} ({items[id].Name}, T{items[id].Tier})"));
        Print("Only behind a blocked milestone", result.OnlyAfterBlocked.Select(id => $"{id} ({result.StageOf(id)})"));
        Print("Blocked milestones", result.BlockedMilestones);
        Print("Stations nothing can build", result.UnbuildableStations.Select(station => station.DisplayName()));
        Print("Dead-end materials (no recipe or other use)", result.DeadEnds.Select(id => $"{id} ({items[id].Name})"));
        Print("Circular chains among unreached items", result.Cycles);
        Print("Recipes later than their tier implies", result.LateRecipes.Select(late =>
            $"{late.RecipeId} -> {late.Output} (T{late.Tier}) at '{late.Stage}', held back by {late.Cause}"));
        // PC-7: the bosses a Classic character can fight before the Fracture, and what only the ones under review leak.
        var gate = EnemyArchetypes.Get(GameSimulation.CoreGateBoss);
        Print($"Bosses reachable before the Fracture (gate: {gate.DisplayName}, {gate.HitPoints:0} life)", result.PreFractureBosses.Select(kind =>
        {
            var boss = EnemyArchetypes.Get(kind);
            var flags = string.Concat(boss.HitPoints > gate.HitPoints ? " ABOVE THE GATE BOSS" : "",
                PreFractureBossReview.TryGetValue(kind, out var why) ? $" [under review: {why}]"
                : IntendedPreFractureBosses.Contains(kind) ? "" : " [NOT INTENDED]");
            return $"{boss.DisplayName}: {boss.HitPoints:0} life, {boss.Tier}, from '{result.Stages[result.BossReach[kind]]}'{flags}";
        }));
        var gated = RunGated();
        Print("Pre-Fracture only through a boss under review", result.EarlyGear.Except(gated.EarlyGear).Select(id =>
            $"{id} (T{items[id].Tier}) at '{result.StageOf(id)}' via {result.Items[id].Via}"));
        Print("Tier 6+ gear before the Fracture (live)", result.PreFractureTier6Items.Select(id => $"{id} (T{items[id].Tier}) via {result.Items[id].Via}"));
        Console.WriteLine($"With the bosses under review held for the Fracture ({gated.HeldForFracture.Count}):");
        Console.WriteLine();
        Print("Tier 4+ gear before the Fracture, outside the whitelist", gated.EarlyGear.Where(id => !EarlyGearWhitelist.ContainsKey(id)).Select(id =>
            $"{id} (T{items[id].Tier}) at '{gated.StageOf(id)}' via {gated.Items[id].Via}"));
        Print("Tier 4+ gear before the Fracture, whitelisted", gated.EarlyGear.Where(EarlyGearWhitelist.ContainsKey).Select(id =>
            $"{id} (T{items[id].Tier}) at '{gated.StageOf(id)}' via {gated.Items[id].Via}: {EarlyGearWhitelist[id]}"));
        Print("Whitelist entries no longer early (remove them)", EarlyGearWhitelist.Keys.Where(id => !gated.EarlyGear.Contains(id)).Select(id => id.ToString()));
        Print("Tier 6+ gear before the Fracture", gated.PreFractureTier6Items.Select(id => $"{id} (T{items[id].Tier})"));
        Print("Deliberate dead ends", DeliberateDeadEnds.Select(entry => $"{entry.Key}: {entry.Value}"));
        var excepted = items.All.Where(item => ExceptionFor(item.Id, items) is not null).ToArray();
        Console.WriteLine($"Deliberate exceptions: {excepted.Length} items " +
                          $"({string.Join("; ", excepted.GroupBy(item => ExceptionFor(item.Id, items)).Select(group => $"{group.Count()} x {group.Key}"))})");
        if (args.Contains("--items", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine();
            Console.WriteLine("Every item:");
            foreach (var (id, reach) in result.Items.OrderBy(entry => entry.Value.Stage).ThenBy(entry => entry.Key.ToString(), StringComparer.Ordinal))
                Console.WriteLine($"  {reach.Stage,3} {id,-28} {reach.Via}");
        }
        Console.WriteLine();
        Console.WriteLine($"Summary: {result.Unreachable.Count} unreachable, {result.OnlyAfterBlocked.Count} behind blocked milestones, " +
                          $"{result.DeadEnds.Count} dead-end materials, {result.UnbuildableStations.Count} unbuildable stations, " +
                          $"{result.Cycles.Count} cycles, {result.LateRecipes.Count} late recipes; before the Fracture: " +
                          $"{result.PreFractureBosses.Count} bosses ({result.PreFractureBosses.Count(PreFractureBossReview.ContainsKey)} under review), " +
                          $"tier 6+ {gated.PreFractureTier6Count} ({result.PreFractureTier6Count} live), tier 4+ outside the whitelist " +
                          $"{gated.EarlyGear.Count(id => !EarlyGearWhitelist.ContainsKey(id))}.");
        return result.Unreachable.Count == 0 && result.UnbuildableStations.Count == 0 ? 0 : 1;
    }

    private static void Print(string title, IEnumerable<string> lines)
    {
        var all = lines.ToArray();
        Console.WriteLine($"{title}: {all.Length}");
        foreach (var line in all)
            Console.WriteLine("  " + line);
        Console.WriteLine();
    }

    /// <summary>Just enough of an event host to evaluate event eligibility against the audit's flags.</summary>
    private sealed class AuditEventHost(WorldFlags flags) : IWorldEventHost
    {
        /// <summary>Clear, rainy and snowy weather, each by day and by night.</summary>
        public static readonly (bool Night, WeatherState Weather)[] Variants =
        [
            (false, new WeatherState(CloudCover: 0.1f)), (true, new WeatherState(CloudCover: 0.1f)),
            (false, new WeatherState(CloudCover: 1f, Rain: 1f, Wind: 1f, Storm: true)), (true, new WeatherState(CloudCover: 1f, Rain: 1f, Wind: 1f, Storm: true)),
            (false, new WeatherState(CloudCover: 0.8f, Snow: 1f, Dust: 1f)), (true, new WeatherState(CloudCover: 0.8f, Snow: 1f, Dust: 1f))
        ];

        public void Use((bool Night, WeatherState Weather) variant) => (IsNight, Weather) = variant;
        public WorldFlags Flags { get; } = flags;
        public int Day => 30;
        public bool IsNight { get; private set; }
        public BiomeZone CurrentZone => BiomeZone.Grassland;
        public WeatherState Weather { get; private set; } = Variants[0].Weather;
        public int TownNpcCount => 20;
        public int SpawnEventCreatures(WorldEventKind kind, IReadOnlyList<(EnemyKind Kind, float Weight)> roster, int count) => 0;
        public bool SpawnEventBoss(WorldEventKind kind, EnemyKind boss) => false;
        public void GrantRewards(IReadOnlyList<(ItemId Item, int Count)> rewards) { }
        public void Announce(string message) { }
        public void MeteorStrike() { }
        public void ApplyWeatherEvent(WorldEventKind kind, bool active) { }
    }
}
