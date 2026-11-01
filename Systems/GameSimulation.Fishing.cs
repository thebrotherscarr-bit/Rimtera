using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Fishing (FISHING_EVENTS_WEATHER.md §3.1): cast a bobber onto liquid, wait for a bite, reel inside the
/// window. The catch rolls on rod + bait + gear power scaled by pool size, time of day and rain.
/// </summary>
public sealed partial class GameSimulation
{
    public const float ReelWindowSeconds = 1.2f;
    private FishingBobber? _bobber;
    private Random? _fishingRandomField;
    private int _lastQuestTurnInDay;
    private Random FishingRandom => _fishingRandomField ??= new Random(Seed ^ 0xF15A1);

    /// <summary>The cast bobber, or null (renderer: position, Biting, ReelWindow).</summary>
    public FishingBobber? Bobber => _bobber;

    /// <summary>The best bait in the inventory (highest bait power), or null.</summary>
    public ItemId? CurrentBait => Player.Inventory.Slots
        .Where(stack => !stack.IsEmpty && Items[stack.ItemId].BaitPower > 0)
        .OrderByDescending(stack => Items[stack.ItemId].BaitPower)
        .Select(stack => (ItemId?)stack.ItemId)
        .FirstOrDefault();

    /// <summary>Fishing power before pool/time/rain factors: rod + bait + worn gear (+10 under a spelunker-style lure).</summary>
    public float GetFishingPower(ItemDefinition rod)
    {
        var power = (float)rod.FishingPower;
        if (CurrentBait is { } bait)
            power += Items[bait].BaitPower;
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            if (Player.Equipment[slot] is { } worn)
                power += Items[worn].FishingPower;
        power += GearFishingBonus;
        return power;
    }

    /// <summary>Chance a bite eats the bait: weak bait almost always goes, strong bait survives most bites (floor 25%).</summary>
    public static float BaitConsumeChance(int baitPower) => Math.Clamp(1f - baitPower / 70f, 0.25f, 1f);

    /// <summary>Pool, time-of-day and rain multipliers applied to raw power.</summary>
    public static float EffectivePower(float power, int poolSize, float clockHour, float rain)
    {
        var pool = Math.Clamp(poolSize / (float)FishingTables.FullPoolSize, 0.2f, 1f);
        var time = clockHour is >= 4.5f and < 8f or >= 17f and < 20.5f ? 1.3f : clockHour is >= 11f and < 15f ? 0.8f : 1f;
        return power * pool * time * (1f + 0.3f * Math.Clamp(rain, 0f, 1f));
    }

    private void CastOrReel(ItemDefinition rod, GridPoint target)
    {
        if (_bobber is { } bobber)
        {
            if (bobber.Biting)
                Catch(rod, bobber);
            else
                PostMessage("You reel in an empty line.");
            _bobber = null;
            SetPlayerCooldown(10);
            return;
        }
        if (CurrentBait is null)
        {
            PostMessage("You need bait: catch critters with a bug net.");
            return;
        }
        var origin = PlayerCenter + new Vector2(0f, -0.4f);
        var aim = new Vector2(target.X + 0.5f, target.Y + 0.5f) - origin;
        var distance = Math.Clamp(aim.Length(), 1f, 12f);
        var direction = aim.LengthSquared() < 0.01f ? new Vector2(Player.Motion.Facing, 0f) : Vector2.Normalize(aim);
        _bobber = new FishingBobber(origin, direction * (4f + distance) + new Vector2(0f, -4f), rod.Id);
        SetPlayerCooldown(10);
        PostMessage("You cast the line.");
    }

    /// <summary>Bobber flight, landing on liquid, bite timer and reel window.</summary>
    private void UpdateFishing(float deltaSeconds)
    {
        if (_bobber is not { } bobber)
            return;
        bobber.AgeSeconds += deltaSeconds;
        var selected = Player.Inventory.SelectedItem;
        if (selected?.Id != bobber.Rod || Vector2.Distance(bobber.Position, PlayerCenter) > 24f || bobber.AgeSeconds > 180f)
        {
            _bobber = null;
            return;
        }
        if (!bobber.Landed)
        {
            bobber.Velocity += new Vector2(Weather.Wind * 0.05f, 20f * deltaSeconds);
            var next = bobber.Position + bobber.Velocity * deltaSeconds;
            var cell = new GridPoint((int)MathF.Floor(next.X), (int)MathF.Floor(next.Y));
            if (!World.Contains(cell) || Tiles[World.GetCell(cell).TileId].IsSolid)
            {
                PostMessage("The line snagged.");
                _bobber = null;
                return;
            }
            bobber.Position = next;
            var data = World.GetCell(cell);
            if (data.LiquidDepth > 0.3f && data.LiquidKind != LiquidKind.None)
                LandBobber(bobber, cell, data.LiquidKind, selected);
            return;
        }

        if (bobber.Biting)
        {
            bobber.ReelWindow -= deltaSeconds;
            if (bobber.ReelWindow <= 0f)
            {
                bobber.Biting = false;
                bobber.BiteTimer = NextBiteDelay(selected);
                PostMessage("It got away.");
            }
            return;
        }
        bobber.BiteTimer -= deltaSeconds;
        if (bobber.BiteTimer > 0f)
            return;
        bobber.Biting = true;
        // A braided fishing line holds a hooked fish twice as long before it gets away.
        bobber.ReelWindow = ReelWindowSeconds * (Player.Stats.ImmunityTags.Contains("fishing-line") ? 2f : 1f);
        // Bait is used up on a bite, less often for stronger bait.
        if (CurrentBait is { } bait && FishingRandom.NextDouble() < BaitConsumeChance(Items[bait].BaitPower))
            Player.Inventory.TryConsume(bait);
        PostMessage("Something bites!");
    }

    private void LandBobber(FishingBobber bobber, GridPoint cell, LiquidKind liquid, ItemDefinition rod)
    {
        var tags = Player.Stats.ImmunityTags;
        var allowed = liquid switch
        {
            LiquidKind.Acid => tags.Contains("acid-fishing") || rod.Id == ItemId.AcidFishingRod,
            LiquidKind.Lava => tags.Contains("lava-fishing") || rod.Id == ItemId.MagmaFishingRod,
            _ => true
        };
        if (!allowed)
        {
            PostMessage(liquid == LiquidKind.Lava ? "The line dissolves in the liquid uranium." : "The acid eats through your line.");
            _bobber = null;
            return;
        }
        bobber.Landed = true;
        bobber.Velocity = Vector2.Zero;
        bobber.Liquid = liquid;
        bobber.PoolSize = MeasurePool(cell, liquid);
        bobber.BiteTimer = NextBiteDelay(rod);
    }

    private float NextBiteDelay(ItemDefinition rod)
    {
        var power = GetFishingPower(rod);
        return (4f + (float)FishingRandom.NextDouble() * 8f) / (1f + power / 40f) / (1f + Weather.Rain * 0.5f);
    }

    /// <summary>Counts connected liquid cells of one kind (capped), for the pool-size factor.</summary>
    public int MeasurePool(GridPoint start, LiquidKind liquid, int cap = 400)
    {
        var seen = new HashSet<GridPoint> { start };
        var queue = new Queue<GridPoint>();
        queue.Enqueue(start);
        while (queue.Count > 0 && seen.Count < cap)
        {
            var point = queue.Dequeue();
            foreach (var next in new[] { new GridPoint(point.X + 1, point.Y), new GridPoint(point.X - 1, point.Y),
                         new GridPoint(point.X, point.Y + 1), new GridPoint(point.X, point.Y - 1) })
            {
                if (!World.Contains(next) || seen.Contains(next))
                    continue;
                var cell = World.GetCell(next);
                if (cell.LiquidKind != liquid || cell.LiquidDepth <= 0.2f)
                    continue;
                seen.Add(next);
                queue.Enqueue(next);
            }
        }
        return seen.Count;
    }

    /// <summary>Builds the catch context at a cell (liquid, zone, depth, pool, power).</summary>
    public FishingContext GetFishingContext(GridPoint cell, LiquidKind liquid, int poolSize, float rawPower)
    {
        var zone = BiomeScanner.Scan(World, ClampCell(cell)).Zone;
        var depth = FishingTables.DepthAt(BiomeLayout, cell.X, cell.Y, World.Height);
        return new FishingContext(liquid, zone, depth, poolSize, EffectivePower(rawPower, poolSize, ClockHour, Weather.Rain),
            IsNight, Weather.Rain, Season, FractureActive);
    }

    private CatchResult Catch(ItemDefinition rod, FishingBobber bobber)
    {
        var cell = ClampCell(RegionMap.CellAt(bobber.Position));
        var context = GetFishingContext(cell, bobber.Liquid, bobber.PoolSize, GetFishingPower(rod));
        var result = RollCatch(context, FishingRandom);
        if (result.Creature is { } creature)
        {
            var spawned = creature == EnemyKind.AbyssLeviathan && TrySpawnBoss(creature, BossSpawnSource.Fishing, out var leviathan, out _) ? leviathan! : SpawnCreature(creature, bobber.Position + new Vector2(0f, -2f));
            PostMessage(result.Message);
            Flags.Increment("fishing.creatures");
            return result with { Message = $"{result.Message} ({spawned.DisplayName})" };
        }
        if (result.Item is { } item)
        {
            Player.Inventory.Add(item, 1);
            Flags.Increment("fishing.catches");
        }
        PostMessage(result.Message);
        return result;
    }

    /// <summary>Hint when abyss bait goes into acid before the Fracture (PC-3): the bait is kept.</summary>
    public const string AbyssQuietMessage = "The deep stays quiet. Something larger waits for the Fracture.";

    /// <summary>
    /// One catch roll in a context: special bait in acid hooks the Abyss Leviathan (after the Fracture; PC-3, D-3); Rust
    /// Moon water can hook a Bloodfin Ambusher; today's quest fish bites where it lives; otherwise rarity → table.
    /// </summary>
    public CatchResult RollCatch(FishingContext context, Random random)
    {
        var abyssBait = context.Liquid == LiquidKind.Acid && Player.Inventory.Count(ItemId.AbyssBait) > 0;
        if (abyssBait && !FractureActive)
            PostMessage(AbyssQuietMessage); // the bait stays in the bag and the catch rolls as usual
        if (abyssBait && FractureActive && !_enemies.Any(enemy => enemy.Kind == EnemyKind.AbyssLeviathan && enemy.IsAlive))
        {
            Player.Inventory.TryConsume(ItemId.AbyssBait);
            return new CatchResult(FishingRarity.Legendary, null, EnemyKind.AbyssLeviathan, "The abyss bait vanishes. The sea heaves upward!");
        }
        if (context.Liquid == LiquidKind.Water && _worldEvents.IsActive(WorldEventKind.RustMoon) && random.NextDouble() < 0.12)
            return new CatchResult(FishingRarity.Uncommon, null, EnemyKind.BloodfinAmbusher, "A bloodfin ambusher bursts from the water!");
        var rarity = FishingTables.RollRarity(context.Power, random);
        if (rarity >= FishingRarity.Uncommon && IsQuestFishBiting(context) && Player.Inventory.Count(TodaysQuestFish) == 0 &&
            random.NextDouble() < 0.35)
            return new CatchResult(rarity, TodaysQuestFish, null, $"You caught a {Items[TodaysQuestFish].Name}! The Tide-Warden wants this.");
        var item = FishingTables.Pick(context, rarity, random);
        return new CatchResult(rarity, item, null, $"You caught {(rarity == FishingRarity.Junk ? "some junk: " : "a ")}{Items[item].Name}.");
    }

    /// <summary>Catch-scanner preview: the possible catches per rarity where the bobber floats (requires the scanner).</summary>
    public IReadOnlyList<(FishingRarity Rarity, ItemId Item)> GetCatchPreview()
    {
        if (_bobber is not { Landed: true } bobber || !Player.Stats.ImmunityTags.Contains("catch-scanner"))
            return Array.Empty<(FishingRarity, ItemId)>();
        var context = GetFishingContext(ClampCell(RegionMap.CellAt(bobber.Position)), bobber.Liquid, bobber.PoolSize,
            GetFishingPower(Items[bobber.Rod]));
        var preview = new List<(FishingRarity, ItemId)>();
        foreach (var rarity in Enum.GetValues<FishingRarity>())
            for (var sample = 0; sample < 6; sample++)
            {
                var item = FishingTables.Pick(context, rarity, new Random(sample));
                if (!preview.Contains((rarity, item)))
                    preview.Add((rarity, item));
            }
        return preview;
    }

    // ------------------------------------------------------------------ crates and quests

    /// <summary>Opens a crate from the inventory and rolls its loot. Returns the items granted.</summary>
    public IReadOnlyList<(ItemId Item, int Count)> OpenCrate(ItemId crate)
    {
        var granted = new List<(ItemId, int)>();
        if (!Items[crate].IsCrate || !Player.Inventory.TryConsume(crate))
            return granted;
        foreach (var (item, min, max) in FishingTables.CrateLoot(crate, FractureActive, Flags.IsSet(FishingTables.AstralCrateFlag)))
        {
            var count = _lootRandom.Next(min, max + 1);
            if (count <= 0)
                continue;
            Player.Inventory.Add(item, count);
            granted.Add((item, count));
        }
        PostMessage($"The {Items[crate].Name} held {DescribeLoot(granted)}.");
        return granted;
    }

    /// <summary>Quest fish whose unlock flags are set (the starfall minnow needs the Starfall season).</summary>
    public IReadOnlyList<ItemId> UnlockedQuestFish => FishingTables.QuestFish
        .Where(entry => entry.Unlock is null || Flags.IsSet(entry.Unlock) ||
                        (entry.Unlock == "season.starfall" && Season == SeasonWindow.Starfall))
        .Select(entry => entry.Fish).ToArray();

    /// <summary>Today's quest fish: a deterministic daily rotation over the unlocked quest fish.</summary>
    public ItemId TodaysQuestFish => QuestFishForDay(Day);

    /// <summary>The quest fish the Tide-Warden asks for on a given day.</summary>
    public ItemId QuestFishForDay(int day)
    {
        var pool = UnlockedQuestFish;
        var hash = unchecked((uint)(Seed * 92821) ^ (uint)day * 2654435761u);
        hash ^= hash >> 13;
        hash *= 0x5bd1e995;
        hash ^= hash >> 15;
        return pool[(int)(hash % (uint)pool.Count)];
    }

    private bool IsQuestFishBiting(FishingContext context) =>
        FishingTables.QuestFish.FirstOrDefault(entry => entry.Fish == TodaysQuestFish).Where?.Invoke(context) == true;

    /// <summary>Hands today's quest fish to the Tide-Warden: credits scale with completed quests, milestones add gear.</summary>
    public bool TurnInQuestFish(TownNpc npc)
    {
        if (npc.Kind != TownNpcKind.TideWarden || npc.IsBound)
            return false;
        if (_lastQuestTurnInDay == Day)
        {
            PostMessage($"{npc.Name}: \"Come back tomorrow for a new fish.\"");
            return false;
        }
        var fish = TodaysQuestFish;
        if (!Player.Inventory.TryConsume(fish))
        {
            PostMessage($"{npc.Name}: \"Bring me a {Items[fish].Name}.\"");
            return false;
        }
        _lastQuestTurnInDay = Day;
        var completed = Flags.Increment("quests.fishing");
        var credits = 40 + (int)completed * 10;
        Player.Inventory.Add(ItemId.Credits, credits);
        ItemId? milestone = completed switch
        {
            1 => ItemId.FerriteFishingRod,
            5 => ItemId.FishingLine,
            10 => ItemId.FishingLure,
            15 => ItemId.AcidWader,
            20 => ItemId.CatchScanner,
            _ => completed % 4 == 0 ? ItemId.AlloyCrate : null
        };
        // The Astral fishing rod (tier 7) is the 30th-quest reward, held until the Fracture: the first quest turned in at or
        // past 30 once the crust has fractured pays it (Terraria's golden rod is pre-hardmode, but tier 7 is not).
        if (completed >= AstralRodQuests && FractureActive && !Flags.IsSet(AstralRodFlag))
        {
            Flags.Set(AstralRodFlag);
            milestone = ItemId.AstralFishingRod;
        }
        if (milestone is { } reward)
            Player.Inventory.Add(reward, 1);
        PostMessage($"{npc.Name} pays {credits} credits{(milestone is { } m ? $" and a {Items[m].Name}" : string.Empty)}. ({completed} quests)");
        return true;
    }

    /// <summary>Quests before the Tide-Warden pays the Astral fishing rod (after the Fracture).</summary>
    public const int AstralRodQuests = 30;
    /// <summary>World flag set once the Astral fishing rod has been paid.</summary>
    public const string AstralRodFlag = "quests.fishing.astral_rod";

    private FishingSaveState CaptureFishing() => new(_lastQuestTurnInDay);

    private void RestoreFishing(FishingSaveState? state) => _lastQuestTurnInDay = state?.LastQuestTurnInDay ?? 0;
}
