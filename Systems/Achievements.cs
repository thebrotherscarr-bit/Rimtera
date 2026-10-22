using Rimtera.Entities;

namespace Rimtera.Systems;

public enum AchievementCategory : byte { Exploration, Combat, Crafting, Collection, Survival, Challenge }

/// <summary>One original Rimtera achievement. <see cref="IconName"/> is a renderer sprite hint.</summary>
public sealed record AchievementDefinition(string Id, string Name, string Description, AchievementCategory Category, string IconName);

/// <summary>Achievement catalog: unlocked by event hooks (<see cref="AchievementTracker.TryUnlock"/>) or by periodic state checks.</summary>
public static class AchievementCatalog
{
    private static AchievementDefinition A(string id, string name, string description, AchievementCategory category) =>
        new(id, name, description, category, "achievement_" + id.Replace('-', '_'));

    public static IReadOnlyList<AchievementDefinition> All { get; } =
    [
        A("first-ore", "Rock and Ore", "Pick up your first chunk of ore.", AchievementCategory.Collection),
        A("first-blood", "Hostile Environment", "Defeat a creature.", AchievementCategory.Combat),
        A("kill-100", "Pest Control", "Defeat 100 creatures in one world.", AchievementCategory.Combat),
        A("first-boss", "Bigger They Come", "Defeat any boss.", AchievementCategory.Combat),
        A("gate-crasher", "Overseer Offline", "Scrap the Overseer mech.", AchievementCategory.Combat),
        A("boss-five", "Threat Assessor", "Defeat five different bosses.", AchievementCategory.Combat),
        A("astro-slayer", "Godfall", "Defeat the Astro-God.", AchievementCategory.Challenge),
        A("worm-turner", "Worm Turner", "Defeat a burrowing worm.", AchievementCategory.Combat),
        A("not-a-chest", "Trust Issues", "Defeat a mimic.", AchievementCategory.Combat),
        A("trophy-hunter", "Trophy Wall", "Hang a kill banner.", AchievementCategory.Combat),
        A("banner-earned", "Fifty of a Kind", "Earn a kill banner.", AchievementCategory.Combat),
        A("tier-2-gear", "Cobalt Age", "Own tier-2 gear.", AchievementCategory.Crafting),
        A("tier-4-gear", "Viridian Vanguard", "Own tier-4 gear.", AchievementCategory.Crafting),
        A("tier-7-gear", "Astral Ascendant", "Own tier-7 gear.", AchievementCategory.Crafting),
        A("dressed-for-it", "Dressed for It", "Wear a full armor set.", AchievementCategory.Crafting),
        A("first-catch", "Something on the Line", "Catch anything while fishing.", AchievementCategory.Collection),
        A("angler-25", "Reel Talent", "Make 25 catches.", AchievementCategory.Collection),
        A("quest-angler", "Tide-Warden's Favourite", "Complete five fishing quests.", AchievementCategory.Collection),
        A("event-cleared", "Weathered It", "Clear a world event.", AchievementCategory.Challenge),
        A("event-veteran", "Seen It All", "Clear three different world events.", AchievementCategory.Challenge),
        A("life-cores-5", "Hardened Heart", "Use five life cores.", AchievementCategory.Survival),
        A("first-crystal", "Capacitor", "Use a charge crystal.", AchievementCategory.Survival),
        A("charge-crystals-max", "Fully Charged", "Use the maximum number of charge crystals.", AchievementCategory.Survival),
        A("heart-breaker", "Heart Breaker", "Break a blight heart.", AchievementCategory.Exploration),
        A("survivor-freed", "Rescue Party", "Free a bound survivor.", AchievementCategory.Exploration),
        A("vault-opened", "Precursor Key", "Open the precursor vault.", AchievementCategory.Exploration),
        A("deep-delver", "Deep Delver", "Reach the lowest quarter of the world.", AchievementCategory.Exploration),
        A("sky-high", "Thin Air", "Climb to the top tenth of the world.", AchievementCategory.Exploration),
        A("credit-hoard", "Credit Hoard", "Carry 1,000 credits.", AchievementCategory.Collection),
        A("bank-deposit", "Cargo Secured", "Store something in your Cargo pod.", AchievementCategory.Collection),
        A("field-naturalist", "Field Naturalist", "Log 25 creatures in the bestiary.", AchievementCategory.Exploration),
        A("first-fall", "Memorial", "Die and leave a marker behind.", AchievementCategory.Survival),
        A("sleeps-with-fishes", "Sleeps with the Fishes", "Drown.", AchievementCategory.Survival),
        A("survive-long-fall", "Bad Landing", "Survive a 40-tile fall.", AchievementCategory.Survival),
        A("second-wind", "Second Wind", "Use a double jump.", AchievementCategory.Exploration),
        A("grappler", "Hooked", "Latch a grappling hook ten times.", AchievementCategory.Exploration),
        A("teleporter", "Shortcut", "Teleport with a recall capsule or phase rod.", AchievementCategory.Exploration),
        A("loyal-companion", "Loyal Companion", "Summon a pet.", AchievementCategory.Collection),
        A("easy-rider", "Easy Rider", "Ride the hover-bike.", AchievementCategory.Exploration),
        // Progression (docs/PROGRESSION_DIFF.md G14).
        A("crust-breaker", "Crust Breaker", "Cause the Fracture.", AchievementCategory.Challenge),
        A("triple-threat", "Triple Threat", "Defeat all three hard-tier bosses.", AchievementCategory.Combat),
        A("colossus-toppled", "Colossus Toppled", "Defeat the Vault Colossus.", AchievementCategory.Combat),
        A("four-pillars-fall", "Four Pillars Fall", "Bring down every obelisk.", AchievementCategory.Challenge),
        A("full-vitality", "Full Vitality", "Raise your maximum life to its cap.", AchievementCategory.Survival)
    ];

    private static readonly Dictionary<string, AchievementDefinition> ById = All.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

    public static AchievementDefinition? Get(string id) => ById.GetValueOrDefault(id);
}

/// <summary>Unlocked achievements (carried by the character file).</summary>
public sealed class AchievementTracker
{
    private readonly Dictionary<string, DateTimeOffset> _unlocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AchievementDefinition> _recent = new();

    /// <summary>Raised once per newly unlocked achievement.</summary>
    public event Action<AchievementDefinition>? Unlocked;

    public bool IsUnlocked(string id) => _unlocked.ContainsKey(id);
    public IReadOnlyCollection<string> UnlockedIds => _unlocked.Keys.ToArray();
    public int UnlockedCount => _unlocked.Count;
    public int TotalCount => AchievementCatalog.All.Count;
    /// <summary>Unlock time per achievement id.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> UnlockTimes => _unlocked;
    /// <summary>Latest unlocks, newest last (up to eight), for toast pop-ups.</summary>
    public IReadOnlyList<AchievementDefinition> RecentUnlocks => _recent;

    public bool TryUnlock(string id)
    {
        if (AchievementCatalog.Get(id) is not { } definition || _unlocked.ContainsKey(definition.Id))
            return false;
        _unlocked[definition.Id] = DateTimeOffset.UtcNow;
        _recent.Add(definition);
        if (_recent.Count > 8)
            _recent.RemoveAt(0);
        Unlocked?.Invoke(definition);
        return true;
    }

    internal void Load(IEnumerable<string>? ids)
    {
        foreach (var id in ids ?? [])
            if (AchievementCatalog.Get(id) is { } definition)
                _unlocked.TryAdd(definition.Id, DateTimeOffset.UtcNow);
    }

    /// <summary>State-based checks (run every half second by the simulation).</summary>
    internal void Evaluate(GameSimulation game)
    {
        game.UpdateInfoGear(); // compass, depth meter, ore scanner, life monitor (Systems/GameSimulation.InfoGear.cs)
        game.UpdateTownContent(); // G13 town tracks share this half-second pass (Systems/Content/TownContent.cs)
        var flags = game.Flags;
        var player = game.Player;
        void When(bool condition, string id)
        {
            if (condition)
                TryUnlock(id);
        }
        When(flags.Get("kills.total") >= 100, "kill-100");
        When(game.DefeatedBosses.Contains(EnemyKind.StoneSentinel), "gate-crasher");
        When(game.DefeatedBosses.Count >= 5, "boss-five");
        When(game.DefeatedBosses.Contains(EnemyKind.AstroGod), "astro-slayer");
        When(game.KillCounts.Values.Any(count => count >= GameSimulation.KillsPerBanner), "banner-earned");
        var owned = player.Inventory.Slots.Where(stack => !stack.IsEmpty).Select(stack => stack.ItemId)
            .Concat(Enum.GetValues<EquipmentSlot>()
                .Select(slot => player.Equipment[slot]).Where(id => id.HasValue).Select(id => id!.Value));
        var gearTier = owned.Select(id => game.Items[id]).Where(item => item.UseKind is ItemUseKind.Melee or ItemUseKind.Ranged
                or ItemUseKind.Tech or ItemUseKind.Command or ItemUseKind.Equipment or ItemUseKind.Pickaxe)
            .Select(item => item.Tier).DefaultIfEmpty(0).Max();
        When(gearTier >= 2, "tier-2-gear");
        When(gearTier >= 4, "tier-4-gear");
        When(gearTier >= 7, "tier-7-gear");
        var armor = new[] { player.Equipment[EquipmentSlot.Head], player.Equipment[EquipmentSlot.Body], player.Equipment[EquipmentSlot.Legs] };
        When(armor.All(id => id.HasValue) && armor.Select(id => game.Items[id!.Value].ArmorSetId).Distinct().Count() == 1 &&
             game.Items[armor[0]!.Value].ArmorSetId is not null, "dressed-for-it");
        When(flags.Get("fishing.catches") >= 1, "first-catch");
        When(flags.Get("fishing.catches") >= 25, "angler-25");
        When(flags.Get("quests.fishing") >= 5, "quest-angler");
        var cleared = flags.All.Count(pair => pair.Value != 0 && pair.Key.StartsWith("event.", StringComparison.Ordinal) &&
                                              pair.Key.EndsWith(".cleared", StringComparison.Ordinal));
        When(cleared >= 1, "event-cleared");
        When(cleared >= 3, "event-veteran");
        When(player.LifeCoresUsed >= 5, "life-cores-5");
        When(player.ChargeCrystalsUsed >= 1, "first-crystal");
        When(player.ChargeCrystalsUsed >= PlayerItems.MaximumChargeCrystals, "charge-crystals-max");
        When(flags.Get("hearts.broken") >= 1, "heart-breaker");
        When(flags.Get("survivors.freed") >= 1, "survivor-freed");
        When(flags.IsSet("vault.opened"), "vault-opened");
        When(player.Motion.Position.Y >= game.World.Height * 0.75f, "deep-delver");
        When(player.Motion.Position.Y <= game.World.Height * 0.1f, "sky-high");
        When(player.Inventory.Count(ItemId.Credits) >= 1000, "credit-hoard");
        When(game.Bank.Slots.Any(stack => !stack.IsEmpty), "bank-deposit");
        When(flags.Get("player.double_jumps") >= 1, "second-wind");
        When(flags.Get("player.grapples") >= 10, "grappler");
        When(flags.Get("player.teleports") >= 1, "teleporter");
        When(flags.IsSet("blight.fracture"), "crust-breaker");
        When(flags.IsSet("hardmode.trio.down"), "triple-threat");
        When(flags.IsSet("boss.vault_colossus.down") || game.DefeatedBosses.Contains(EnemyKind.VaultColossus), "colossus-toppled");
        When(flags.IsSet("obelisks.down"), "four-pillars-fall");
        When(AtLifeCap(game), "full-vitality");
    }

    /// <summary>Maximum life is at its ceiling: every life core and every lifebloom pod used (300 life).</summary>
    internal static bool AtLifeCap(GameSimulation game) =>
        game.Player.LifeCoresUsed >= PlayerCharacter.MaximumLifeCores && game.LifebloomPodsUsed >= GrowthItems.MaximumLifeblooms;
}

public sealed partial class GameSimulation
{
    private AchievementTracker? _achievements;

    /// <summary>Achievements carried by the character; unlocks post a message.</summary>
    public AchievementTracker Achievements
    {
        get
        {
            if (_achievements is null)
            {
                _achievements = new AchievementTracker();
                _achievements.Unlocked += definition => PostSideMessage($"Achievement unlocked: {definition.Name}!");
            }
            return _achievements;
        }
    }

    /// <summary>Adds a log line without replacing <see cref="LastMessage"/> (achievement toasts, side notes).</summary>
    private void PostSideMessage(string message)
    {
        _recentMessages.Enqueue(message);
        while (_recentMessages.Count > 5)
            _recentMessages.Dequeue();
    }
}
