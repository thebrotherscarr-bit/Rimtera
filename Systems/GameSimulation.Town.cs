using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Town NPCs: flag-gated unlocks, hourly arrivals into valid housing, home assignment, wandering and
/// sheltering, happiness-driven shop prices, buy/sell with credits, survivors, and dialogue.
/// </summary>
public sealed partial class GameSimulation
{
    public const float NeighbourRadius = 25f;
    /// <summary>Game hours before a resident who died comes back (into any free valid house, by day).</summary>
    public const float TownRespawnHours = 10f;
    /// <summary>Hit points a resident recovers per real second while not being hurt.</summary>
    public const float TownNpcRegenPerSecond = 2f;
    private readonly List<TownNpc> _townNpcs = new();

    /// <summary>Residents and bound survivors.</summary>
    public IReadOnlyList<TownNpc> TownNpcs => _townNpcs;

    /// <summary>Validates the room containing <paramref name="cell"/> as housing (for the UI's housing query).</summary>
    public HousingResult CheckHousing(GridPoint cell)
    {
        _regions.EnsureCurrent();
        var result = HousingValidator.Check(World, _regions, cell);
        if (result.Valid && result.Room is { } room && _townNpcs.Any(npc => npc.HomeCell is { } home && _regions.GetRegion(home) == room))
            return HousingResult.Fail("This room is already claimed.");
        return result;
    }

    /// <summary>Runs the hourly arrival check now (debug tools, tests).</summary>
    public void CheckTownArrivals() => UpdateTownHourly();

    /// <summary>Hourly: refresh unlock flags, move unlocked NPCs into free housing, consul comes and goes.</summary>
    private void UpdateTownHourly()
    {
        RefreshUnlockFlags();
        UpdateContentFillTown(); // content fill: travelling merchant and town gels (Systems/Content/ContentFillTown.cs)
        // The trade consul leaves at dusk (until D-53). Unhappy residents stay (NC-9): unhappiness only raises their prices.
        foreach (var npc in _townNpcs.ToArray())
        {
            if (npc.IsBound)
                continue;
            if (npc.Kind == TownNpcKind.TradeConsul && IsNight)
            {
                DropColonistLoad(npc);
                _townNpcs.Remove(npc);
                PostMessage($"{npc.Name} the {npc.Definition.Role} has left the colony.");
            }
        }
        // Settlers only look for homes by day, and at most one moves in per check.
        if (IsNight)
            return;
        // Homeless residents (freed survivors) take the first free house.
        foreach (var npc in _townNpcs.Where(npc => !npc.IsBound && npc.HomeCell is null))
            if (FindFreeHouse() is { } home)
            {
                npc.HomeCell = home;
                PostMessage($"{npc.Name} the {npc.Definition.Role} has moved into a home.");
                return;
            }

        foreach (var definition in TownNpcCatalog.All)
        {
            if (_townNpcs.Any(npc => npc.Kind == definition.Kind) || !Flags.IsSet($"npc.{WorldFlags.Key(definition.Kind)}.unlocked"))
                continue;
            if (Flags.Get(RespawnFlag(definition.Kind)) > (long)MathF.Floor(ElapsedGameHours) || Flags.IsSet(LostFlag(definition.Kind)))
                continue;
            if (definition.Kind == TownNpcKind.TradeConsul && (IsNight || TriggerRandom.Next(6) != 0))
                continue;
            if (FindFreeHouse() is not { } house)
                return;
            var npc = AddTownNpc(definition.Kind, StandingAt(house), bound: false);
            npc.HomeCell = house;
            PostMessage($"{npc.Name} the {definition.Role} has arrived and moved in!");
            return;
        }
        // Crew members, and residents replacing the dead, arrive through the storyteller's arrival stream.
    }

    /// <summary>Unlocks are set by flags; conditions that need a scan are checked once and recorded as flags.</summary>
    private void RefreshUnlockFlags()
    {
        void Unlock(TownNpcKind kind) => Flags.Set($"npc.{WorldFlags.Key(kind)}.unlocked");
        Unlock(TownNpcKind.StationAi);
        if (Player.Inventory.Count(ItemId.Credits) >= 50)
            Unlock(TownNpcKind.Quartermaster);
        // Terraria's Nurse: the player's own progress (more than the starting health), not the crew's injuries.
        // 0.3.5: as Terraria's Nurse, more than the starting 100 health (one life core) and the Quartermaster (Merchant) in town.
        if (Player.MaximumHitPoints > 100f && _townNpcs.Any(npc => npc.Kind == TownNpcKind.Quartermaster && !npc.IsBound))
            Unlock(TownNpcKind.Medic);
        // 0.3.5: as Terraria's Arms Dealer, a gun or bullets carried (bows no longer count).
        if (Player.Inventory.Slots.Any(stack => !stack.IsEmpty && (Items[stack.ItemId].Style == WeaponStyle.Gun ||
                                                                   Items[stack.ItemId].UsesAmmo == AmmoClass.Bullet ||
                                                                   Items[stack.ItemId].AmmoKind == AmmoClass.Bullet)))
            Unlock(TownNpcKind.Armorer);
        if (Flags.IsSet("boss.stone_sentinel.down"))
            Unlock(TownNpcKind.ChiefEngineer);
        if (Flags.IsSet("placed.hydroponics"))
            Unlock(TownNpcKind.AstroBotanist);
        if (Flags.Get("hearts.broken") > 0)
            Unlock(TownNpcKind.HeadResearcher);
        if (_townNpcs.Count(npc => !npc.IsBound) >= 2)
            Unlock(TownNpcKind.TradeConsul);
        if (Player.Inventory.Slots.Any(stack => !stack.IsEmpty && Items[stack.ItemId].FishingPower > 0))
            Unlock(TownNpcKind.TideWarden);
        // Hardmode arrivals follow the later bosses.
        if (Flags.IsSet($"boss.{WorldFlags.Key(CoreGateBoss)}.down"))
            Unlock(TownNpcKind.Isotopist);
        if (HardTrio.Any(kind => Flags.IsSet($"boss.{WorldFlags.Key(kind)}.down")))
            Unlock(TownNpcKind.Aeronaut);
        if (Flags.IsSet($"boss.{WorldFlags.Key(BulbBoss)}.down"))
            Unlock(TownNpcKind.RelicWarden);
    }

    /// <summary>
    /// Terraria's housing assignment: moves a resident into the valid room at <paramref name="cell"/>, unless another
    /// resident already lives there. Returns the player-facing result.
    /// </summary>
    public string AssignHome(TownNpc npc, GridPoint cell)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (npc.IsBound || !_townNpcs.Contains(npc))
            return $"{npc.Name} cannot move right now.";
        _regions.EnsureCurrent();
        var result = HousingValidator.Check(World, _regions, cell);
        if (!result.Valid || result.Room is not { } room)
            return $"Not valid housing: {result.Reason}";
        if (_townNpcs.Any(other => !ReferenceEquals(other, npc) && other.HomeCell is { } home && _regions.GetRegion(home) == room))
            return "Someone else already lives in this room.";
        npc.HomeCell = FloorCellOf(room) ?? cell;
        return $"{npc.Name} the {npc.Definition.Role} will live here.";
    }

    private GridPoint? FindFreeHouse()
    {
        _regions.EnsureCurrent();
        foreach (var room in _regions.Rooms.Where(room => room.CellCount >= HousingValidator.MinimumCells)
                     .OrderBy(room => Vector2.Distance(room.Centroid, PlayerCenter)))
        {
            var cell = room.CellIndices.Count > 0
                ? new GridPoint(room.CellIndices[0] % World.Width, room.CellIndices[0] / World.Width)
                : RegionMap.CellAt(room.Centroid);
            if (CheckHousing(cell).Valid)
                return FloorCellOf(room) ?? cell;
        }
        return null;
    }

    /// <summary>A standing spot in a room: an open cell with solid ground below.</summary>
    private GridPoint? FloorCellOf(RoomInfo room)
    {
        foreach (var index in room.CellIndices)
        {
            var x = index % World.Width;
            var y = index / World.Width;
            if (World.Contains(x, y + 1) && Tiles[World.TileAt(x, y + 1)].IsSolid && World.Contains(x, y - 1) &&
                !Tiles[World.TileAt(x, y - 1)].IsSolid)
                return new GridPoint(x, y);
        }
        return null;
    }

    /// <summary>
    /// Where a person stands on an open cell over a floor: feet on the floor, centred on the cell, but nudged off a wall on
    /// either side, since a body (1.25 tiles) is wider than the cell.
    /// </summary>
    internal Vector2 StandingAt(GridPoint cell)
    {
        bool WallBeside(int dx)
        {
            for (var dy = 0; dy < (int)MathF.Ceiling(HumanoidBody.Size.Y); dy++)
                if (World.Contains(cell.X + dx, cell.Y - dy) && Tiles[World.TileAt(cell.X + dx, cell.Y - dy)].IsSolid)
                    return true;
            return false;
        }
        var x = cell.X + 0.5f - HumanoidBody.Size.X / 2f;
        var left = WallBeside(-1);
        var right = WallBeside(1);
        if (left && !right)
            x = cell.X + 0.01f;
        else if (right && !left)
            x = cell.X + 1f - HumanoidBody.Size.X - 0.01f;
        return new Vector2(x, cell.Y + 1f - HumanoidBody.Size.Y - 0.01f);
    }

    /// <summary>The station AI's resident self once it has moved into a house (Terraria's Guide finding a home).</summary>
    private TownNpc? StationAiResident => _townNpcs.FirstOrDefault(npc => npc.Kind == TownNpcKind.StationAi && !npc.IsBound && npc.HomeCell is not null);

    /// <summary>True once the station AI lives in a house; until then it waits as a hologram at the landing site.</summary>
    public bool GuideMovedIn => StationAiResident is not null;

    /// <summary>Where to find the station AI: its hologram at the landing site, then (once housed) wherever it lives.</summary>
    public Vector2 GuidePosition => StationAiResident is { } ai ? ai.Motion.Position + ai.Motion.Size / 2f : Guide.Position;

    /// <summary>Within talking distance of the station AI, wherever it is.</summary>
    public bool IsNearGuide(Vector2 point) => Vector2.Distance(point, GuidePosition) <= Guide.InteractionRadius;

    /// <summary>
    /// How many crew the colony can attract: one for every two housed residents, plus one per boss defeated, up to five.
    /// Crew arrive like Terraria's town NPCs, by day into a free valid house, rather than walking into camp at random.
    /// </summary>
    public int CrewCapacity => Math.Min(WandererJoinsIncident.MaximumCrew,
        _townNpcs.Count(npc => !npc.IsBound && npc.HomeCell is not null) / 2 + _defeatedBosses.Count);

    /// <summary>Adds a town NPC directly (debug tools, tests and survivor placement).</summary>
    public TownNpc AddTownNpc(TownNpcKind kind, Vector2 position, bool bound)
    {
        var definition = TownNpcCatalog.Get(kind);
        var taken = _townNpcs.Select(npc => npc.Name).ToHashSet();
        var offset = (int)((uint)(Seed * 31 + (int)kind * 7) % (uint)definition.Names.Length);
        var name = Enumerable.Range(0, definition.Names.Length).Select(i => definition.Names[(offset + i) % definition.Names.Length])
            .FirstOrDefault(candidate => !taken.Contains(candidate)) ?? definition.Names[0];
        var npc = new TownNpc(_nextEntityId++, definition, name, position) { IsBound = bound };
        _townNpcs.Add(npc);
        return npc;
    }

    private void FreeSurvivor(TownNpc npc)
    {
        npc.IsBound = false;
        Flags.Set($"npc.{WorldFlags.Key(npc.Kind)}.unlocked");
        Flags.Increment("survivors.freed");
        if (FindFreeHouse() is { } house)
            npc.HomeCell = house;
    }

    private static string RespawnFlag(TownNpcKind kind) => $"npc.{WorldFlags.Key(kind)}.respawn_hour";

    /// <summary>Game hour (elapsed) before which a dead resident of this kind will not return; 0 when free to come.</summary>
    public long TownNpcRespawnHour(TownNpcKind kind) => Flags.Get(RespawnFlag(kind));

    /// <summary>Hurts a resident (hostile contact, hazards, tests). Returns true if it died.</summary>
    public bool HurtTownNpc(TownNpc npc, float amount)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (amount <= 0f || npc.IsBound || !npc.IsAlive || !_townNpcs.Contains(npc))
            return false;
        npc.HitPoints = Math.Max(0f, npc.HitPoints - amount);
        npc.HurtCooldown = 0.6f;
        if (npc.IsAlive)
        {
            WarnStationAiHurt(npc);
            return false;
        }
        DropColonistLoad(npc);
        _townNpcs.Remove(npc);
        Record($"{npc.Name} the {npc.Definition.Role} was slain.");
        if (StationAiDefeat(npc))
            return true;
        if (PeopleDieForGood)
        {
            // Permadeath: the role stays empty until the arrival stream brings someone new to take it up.
            Flags.Set(LostFlag(npc.Kind));
            PostMessage($"{npc.Name} the {npc.Definition.Role} was slain. They are gone for good.");
        }
        else
        {
            var respawnAt = (long)MathF.Ceiling(ElapsedGameHours + TownRespawnHours);
            Flags.Clear(RespawnFlag(npc.Kind));
            Flags.Increment(RespawnFlag(npc.Kind), respawnAt);
            PostMessage($"{npc.Name} the {npc.Definition.Role} was slain!");
        }
        return true;
    }

    /// <summary>
    /// People die for good on every setting except the no-stakes ones (Explorer and Creative), where slain residents
    /// still come back after a while.
    /// </summary>
    public bool PeopleDieForGood => PlayerProfile.Difficulty is not (CharacterDifficulty.Explorer or CharacterDifficulty.Creative);

    private static string LostFlag(TownNpcKind kind) => $"npc.{WorldFlags.Key(kind)}.lost";

    /// <summary>Unlocked town roles emptied by a death and not yet filled again.</summary>
    public IEnumerable<TownNpcKind> LostResidentKinds() => TownNpcCatalog.All
        .Select(definition => definition.Kind)
        .Where(kind => Flags.IsSet(LostFlag(kind)) && !_townNpcs.Any(npc => npc.Kind == kind));

    /// <summary>Hostile creatures touching a resident hurt it; residents heal slowly otherwise.</summary>
    private void UpdateTownNpcHealth(float deltaSeconds)
    {
        foreach (var npc in _townNpcs.ToArray())
        {
            if (npc.IsBound)
                continue;
            if (npc.HurtCooldown > 0f)
            {
                npc.HurtCooldown -= deltaSeconds;
                continue;
            }
            var box = (Min: npc.Motion.Position, Max: npc.Motion.Position + npc.Motion.Size);
            EnemyAgent? attacker = null;
            foreach (var enemy in _enemies)
            {
                if (!enemy.IsAlive || enemy.IsPassive || enemy.AttackDamage <= 0f)
                    continue;
                var min = enemy.Motion.Position;
                var max = min + enemy.Motion.Size;
                if (min.X < box.Max.X && max.X > box.Min.X && min.Y < box.Max.Y && max.Y > box.Min.Y)
                {
                    attacker = enemy;
                    break;
                }
            }
            if (attacker is not null)
                HurtTownNpc(npc, attacker.AttackDamage);
            else
                npc.HitPoints = Math.Min(npc.MaximumHitPoints, npc.HitPoints + TownNpcRegenPerSecond * deltaSeconds);
        }
    }

    /// <summary>NPCs wander near home by day and go indoors at night or while a dangerous event runs.</summary>
    private void UpdateTownNpcs(float deltaSeconds)
    {
        if (_townNpcs.Count == 0)
            return;
        UpdateTownNpcHealth(deltaSeconds);
        var shelter = IsNight || _worldEvents.Modifiers.Shelter;
        foreach (var npc in _townNpcs)
        {
            if (npc.IsBound)
            {
                npc.Activity = "Bound";
                _colonistMovement.Step(npc.Motion, new MovementInput(0f), deltaSeconds, World, Tiles);
                continue;
            }
            var center = npc.Motion.Position + npc.Motion.Size / 2f;
            // 0.2.4: a housed resident works station bills near home by day (beside the shop), like an Operate colonist.
            if (npc.HomeCell is not null && !shelter && npc.WorksStations)
            {
                Vector2? step = null;
                if (TryOperate(npc, center, deltaSeconds, to => step = to, null))
                {
                    StepTownNpcToward(npc, center, step ?? center, deltaSeconds);
                    continue;
                }
            }
            else
            {
                npc.OperateStation = null;
                npc.OperateProgress = 0f;
                if (!npc.WorksStations)
                    DropHeld(npc, center);
            }
            Vector2 target;
            if (npc.HomeCell is { } home)
            {
                var homePoint = new Vector2(home.X + 0.5f, home.Y + 0.5f);
                if (shelter)
                {
                    target = homePoint;
                    npc.Activity = "Indoors";
                }
                else
                {
                    npc.WanderTimer -= deltaSeconds;
                    if (npc.WanderTimer <= 0f)
                    {
                        npc.WanderTimer = 3f + (npc.Id % 5);
                        var offset = ((npc.Id * 37 + TickNumber / 60) % 13 - 6) * 0.9f;
                        npc.WanderTarget = new Vector2(Math.Clamp(homePoint.X + offset, 1f, World.Width - 2f), homePoint.Y);
                    }
                    target = npc.WanderTarget;
                    npc.Activity = "Wandering";
                }
            }
            else
            {
                target = PlayerCenter;
                npc.Activity = "Looking for a home";
            }
            StepTownNpcToward(npc, center, target, deltaSeconds);
        }
    }

    /// <summary>A resident's walk: toward the target at 70% pace, hopping a one-tile step.</summary>
    private void StepTownNpcToward(TownNpc npc, Vector2 center, Vector2 target, float deltaSeconds)
    {
        var dx = target.X - center.X;
        var horizontal = MathF.Abs(dx) > 0.6f ? MathF.Sign(dx) : 0f;
        var jump = horizontal != 0f && npc.Motion.Grounded && IsGroundBlockedAhead(npc.Motion, horizontal);
        _colonistMovement.Step(npc.Motion, new MovementInput(horizontal, JumpPressed: jump, SpeedMultiplier: 0.7f), deltaSeconds, World, Tiles);
    }

    // ------------------------------------------------------------------ happiness and shops

    /// <summary>Terraria's price factor for each affection (1.4 happiness): love ×0.88, like ×0.94, dislike ×1.06, hate ×1.12.</summary>
    public static float AffectionFactor(Affection feeling) => feeling switch
    {
        Affection.Hate => 1.12f,
        Affection.Dislike => 1.06f,
        > 0 when (int)feeling >= 2 => 0.88f,
        > 0 => 0.94f,
        _ => 1f
    };

    /// <summary>Residents that do not count as neighbours, as Terraria's <c>IsNotReallyTownNPC</c>: town gels and the travelling traders.</summary>
    private static bool CountsAsNeighbour(TownNpc npc) =>
        !npc.IsBound && !ContentFillTown.IsGel(npc.Kind) && npc.Kind is not (TownNpcKind.TravellingMerchant or TownNpcKind.TradeConsul);

    /// <summary>
    /// Terraria 1.4's shop-price factor (0.3.5, P1–P6), a product of: the home biome's affection; each neighbour's within
    /// <see cref="NeighbourRadius"/> tiles of home; ×1.05 for each neighbour beyond three (crowded) or ×0.95 with two or
    /// fewer nearby and four or fewer within 120 tiles (solitude); and the full 1.5 when homeless or more than 120 tiles
    /// from home. Clamped to 0.75–1.5. Gels and travelling traders are not counted as neighbours.
    /// </summary>
    public float GetPriceFactor(TownNpc npc)
    {
        if (npc.HomeCell is not { } home || Vector2.Distance(npc.Motion.Position, new Vector2(home.X + 0.5f, home.Y + 0.5f)) > FarFromHome)
            return 1.5f;
        var position = new Vector2(home.X + 0.5f, home.Y + 0.5f);
        var zone = BiomeScanner.Scan(World, ClampCell(home)).Zone;
        var factor = 1f;
        foreach (var (biome, feeling) in npc.Definition.Biomes)
            if (biome == zone)
                factor *= AffectionFactor(feeling);
        Vector2 Where(TownNpc other) => other.HomeCell is { } otherHome ? new Vector2(otherHome.X + 0.5f, otherHome.Y + 0.5f) : other.Motion.Position;
        var others = _townNpcs.Where(other => other != npc && CountsAsNeighbour(other)).ToArray();
        var neighbours = others.Where(other => Vector2.Distance(Where(other), position) <= NeighbourRadius).ToArray();
        foreach (var other in neighbours)
            foreach (var (kind, feeling) in npc.Definition.Neighbours)
                if (kind == other.Kind)
                    factor *= AffectionFactor(feeling);
        if (neighbours.Length > 3)
            factor *= MathF.Pow(1.05f, neighbours.Length - 3);
        else if (neighbours.Length <= 2 && others.Count(other => Vector2.Distance(Where(other), position) <= FarFromHome) <= 4)
            factor *= 0.95f;
        return Math.Clamp(factor, 0.75f, 1.5f);
    }

    /// <summary>Tiles from home beyond which a resident counts as far from home (Terraria's 120).</summary>
    public const float FarFromHome = 120f;

    /// <summary>
    /// Happiness as a score for the UI and moods: how far the price factor sits below 1, in steps of 6% (Terraria's "like"
    /// step), so +2 is about ×0.88 and −2 about ×1.12.
    /// </summary>
    public float GetHappiness(TownNpc npc)
    {
        var score = (1f - GetPriceFactor(npc)) / 0.06f;
        npc.Happiness = score;
        return score;
    }

    /// <summary>Shop price multiplier: Terraria's happiness factor (0.75–1.5), then the 0.2.9 Trade discount.</summary>
    public float GetPriceMultiplier(TownNpc npc) => GetPriceFactor(npc) * (1f - TradeDiscount);

    /// <summary>What an NPC sells right now (flag-gated stock) at happiness-adjusted prices.</summary>
    public IReadOnlyList<ShopOffer> GetShop(TownNpc npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (npc.IsBound)
            return Array.Empty<ShopOffer>();
        var multiplier = GetPriceMultiplier(npc);
        return npc.Definition.Shop
            .Where(entry => entry.RequiredFlag is null || Flags.IsSet(entry.RequiredFlag))
            .Where(entry => Items.Contains(entry.Item))
            .Where(entry => ContentFillTown.InStock(npc.Kind, entry.Item, Seed, Day))
            .Select(entry => new ShopOffer(entry.Item, Items[entry.Item].Name,
                Math.Max(1, (int)MathF.Ceiling(Items[entry.Item].CreditValue * multiplier))))
            .ToArray();
    }

    /// <summary>Buys <paramref name="quantity"/> of an offered item with credits. Returns false if unaffordable or not stocked.</summary>
    public bool Buy(TownNpc npc, ItemId item, int quantity = 1)
    {
        if (quantity <= 0 || GetShop(npc).FirstOrDefault(offer => offer.Item == item) is not { Price: > 0 } offer)
            return false;
        var total = offer.Price * quantity;
        if (Player.Inventory.Count(ItemId.Credits) < total || !Player.Inventory.CanAdd(item, quantity))
        {
            PostMessage($"You need {total} credits for that.");
            return false;
        }
        Player.Inventory.TryConsume(ItemId.Credits, total);
        SoundEvents.Emit(SoundEventKind.Coin, "buy", PlayerCenter, (int)item);
        Player.Inventory.Add(item, quantity);
        PostMessage($"Bought {quantity} {Items[item].Name} for {total} credits.");
        return true;
    }

    /// <summary>Credits an NPC pays for one unit of a stack (a fifth of value, better when they are happy).</summary>
    public int GetSellPrice(TownNpc npc, ItemStack stack) => stack.IsEmpty || stack.ItemId == ItemId.Credits
        ? 0
        : Math.Max(1, (int)(Items[stack.ItemId].CreditValue * CalibrationCatalog.Get(stack.Calibration).ValueMultiplier * 0.2f /
                            GetPriceMultiplier(npc)));

    /// <summary>Sells from an inventory slot to an NPC. Returns credits earned.</summary>
    public int Sell(TownNpc npc, int slot, int quantity = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (npc.IsBound || (uint)slot >= (uint)Player.Inventory.SlotCount)
            return 0;
        var stack = Player.Inventory.Slots[slot];
        var unit = GetSellPrice(npc, stack);
        if (unit <= 0)
            return 0;
        var taken = Player.Inventory.TakeFromSlot(slot, quantity);
        var earned = unit * taken.Count;
        Player.Inventory.Add(ItemId.Credits, earned);
        SoundEvents.Emit(SoundEventKind.Coin, "sell", PlayerCenter, (int)taken.ItemId);
        _buyback.Insert(0, new BuybackEntry(taken, earned));
        if (_buyback.Count > BuybackCapacity)
            _buyback.RemoveAt(_buyback.Count - 1);
        PostMessage($"Sold {taken.Count} {Items[taken.ItemId].Name} for {earned} credits.");
        return earned;
    }

    /// <summary>How many recent sales can be bought back (Terraria keeps a short list, newest first).</summary>
    public const int BuybackCapacity = 10;
    private readonly List<BuybackEntry> _buyback = new();

    /// <summary>Recent sales, newest first, each at the price it was sold for.</summary>
    public IReadOnlyList<BuybackEntry> Buyback => _buyback;

    /// <summary>Buys back a recent sale for the credits it earned. False if unaffordable or there is no room.</summary>
    public bool BuyBack(int index)
    {
        if ((uint)index >= (uint)_buyback.Count)
            return false;
        var entry = _buyback[index];
        if (Player.Inventory.Count(ItemId.Credits) < entry.Price || !Player.Inventory.CanAdd(entry.Stack.ItemId, entry.Stack.Count))
        {
            PostMessage($"You need {entry.Price} credits and room in the bag for that.");
            return false;
        }
        Player.Inventory.TryConsume(ItemId.Credits, entry.Price);
        SoundEvents.Emit(SoundEventKind.Coin, "buy", PlayerCenter, (int)entry.Stack.ItemId);
        Player.Inventory.Add(entry.Stack.ItemId, entry.Stack.Count, entry.Stack.Calibration);
        _buyback.RemoveAt(index);
        PostMessage($"Bought back {entry.Stack.Count} {Items[entry.Stack.ItemId].Name} for {entry.Price} credits.");
        return true;
    }

    /// <summary>The medic heals the player fully for credits (scaled by missing health and happiness).</summary>
    public bool HealAtMedic(TownNpc npc)
    {
        if (npc.Kind != TownNpcKind.Medic || npc.IsBound)
            return false;
        var missing = Player.MaximumHitPoints - Player.HitPoints;
        // 0.3.5 (S1): like Terraria's Nurse, the medic also clears debuffs (not potion sickness), each for a small fee.
        var debuffs = Player.Statuses.Active.Select(status => status.Id)
            .Where(id => StatusEffectSystem.IsDebuff(id) && id != StatusId.PotionSickness).ToArray();
        var cost = Math.Max(1, (int)MathF.Ceiling((missing * 0.5f + debuffs.Length * 5f) * GetPriceMultiplier(npc)));
        if (missing <= 0f && debuffs.Length == 0 || !Player.Inventory.TryConsume(ItemId.Credits, cost))
            return false;
        Player.Heal(missing);
        foreach (var id in debuffs)
            Player.Statuses.Remove(id);
        PostMessage($"{npc.Name} patches you up for {cost} credits.");
        return true;
    }

    /// <summary>One line of dialogue (rotates by day, plus a mood remark and the fishing quest for the Tide-Warden).</summary>
    public string GetNpcDialogue(TownNpc npc)
    {
        if (npc.IsBound)
            return "Please... cut me loose!";
        var lines = npc.Definition.Lines;
        var line = lines[(int)((uint)(Day + npc.Id) % (uint)lines.Length)];
        var happiness = GetHappiness(npc);
        var mood = happiness >= 2f ? " I love it here." : happiness <= -2f ? " I can't stand this place." : string.Empty;
        if (npc.Kind == TownNpcKind.TideWarden)
            line += $" Today I want a {Items[TodaysQuestFish].Name}.";
        return line + mood;
    }

    private TownNpcSnapshot[] CaptureTownNpcs() => _townNpcs.Select(npc => new TownNpcSnapshot(npc.Kind, npc.Name,
        npc.Motion.Position.X, npc.Motion.Position.Y, npc.HomeCell?.X, npc.HomeCell?.Y, npc.IsBound, npc.HitPoints,
        npc.WorksStations ? null : false,
        npc.Held.Count == 0 ? null : npc.Held.OrderBy(pair => pair.Key).Select(pair => new ItemStack(pair.Key, pair.Value)).ToArray(),
        npc.Carried.IsEmpty ? null : npc.Carried)).ToArray();

    private void RestoreTownNpcs(TownNpcSnapshot[]? npcs)
    {
        if (npcs is null)
            return;
        _townNpcs.Clear();
        foreach (var saved in npcs)
        {
            if (saved is null || !Enum.IsDefined(saved.Kind) || !float.IsFinite(saved.X) || !float.IsFinite(saved.Y))
                throw new InvalidDataException("A saved town NPC is invalid.");
            var npc = new TownNpc(_nextEntityId++, TownNpcCatalog.Get(saved.Kind), saved.Name ?? TownNpcCatalog.Get(saved.Kind).Names[0],
                new Vector2(Math.Clamp(saved.X, 0f, World.Width - 1f), Math.Clamp(saved.Y, 0f, World.Height - 2f)))
            {
                IsBound = saved.Bound,
                HomeCell = saved.HomeX is { } hx && saved.HomeY is { } hy && World.Contains(hx, hy) ? new GridPoint(hx, hy) : null
            };
            // NC-20: a hurt resident loads as hurt. Saves from before the field (or a bad value) load at full health; a
            // living resident never loads dead.
            if (saved.Hp is { } hp && float.IsFinite(hp))
                npc.HitPoints = Math.Clamp(hp, 1f, npc.MaximumHitPoints);
            npc.WorksStations = saved.WorksStations ?? true;
            foreach (var held in saved.Held ?? [])
                if (!held.IsEmpty && Items.Contains(held.ItemId))
                    npc.Held[held.ItemId] = npc.Held.GetValueOrDefault(held.ItemId) + held.Count;
            if (saved.Carried is { IsEmpty: false } carried && Items.Contains(carried.ItemId))
                npc.Carried = carried;
            _townNpcs.Add(npc);
        }
    }
}

/// <summary>A recent sale that can be bought back, at the price it fetched.</summary>
public sealed record BuybackEntry(ItemStack Stack, int Price);
