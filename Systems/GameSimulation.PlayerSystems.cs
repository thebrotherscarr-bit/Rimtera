using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Emote bubbles shown over the player, colonists and town NPCs.</summary>
public enum EmoteKind : byte { Wave, Happy, Laugh, Love, Sad, Angry, Question, Alert, Hungry, Scared, Sleepy, Music }

/// <summary>One live emote bubble: who shows it, where (world point above the head) and seconds left.</summary>
public readonly record struct EmoteBubble(long ActorId, EmoteKind Kind, Vector2 Anchor, float Remaining);

public enum AggroTargetKind : byte { Player, Colonist, TownNpc }

/// <summary>Whom a hostile creature is going after.</summary>
public readonly record struct AggroTarget(AggroTargetKind Kind, long Id, Vector2 Center);

/// <summary>
/// Player and combat systems (docs/TERRARIA_GAPS.md plan item 2): creature afflictions, luck, aggro, salvage caches
/// with trophies and relics, emotes, sifting and dyes. Terraria is behavioural reference only; every rule and number
/// here is Rimtera's own. Random rolls use private streams so the legacy combat and loot sequences never shift.
/// </summary>
public sealed partial class GameSimulation
{
    public const long PlayerActorId = -1;
    public const float EmoteSeconds = 4f;
    public const float SifterReach = 5f;
    /// <summary>Aggro is worth this many tiles of distance per 100 points.</summary>
    public const float AggroTilesPer100 = 2.5f;
    /// <summary>Colonists and residents count as this much farther away than they are (hostiles prefer the player).</summary>
    public const float ColonistAggroPenalty = 8f, TownNpcAggroPenalty = 10f;
    /// <summary>Crew standing this close to the player share the player's threat (the creature keeps hunting the player).</summary>
    public const float SharedTargetRadius = 6f;

    public static readonly StatusId[] CreatureAfflictions =
        [StatusId.Burning, StatusId.Poison, StatusId.Frostbite, StatusId.ArmorShred, StatusId.Confused, StatusId.Marked];

    private Random? _playerSystemsRandomStream;
    private Random? _luckRandomStream;
    private Random PlayerSystemsRandom => _playerSystemsRandomStream ??= new Random(Seed ^ 0x51F7E5);
    private Random LuckRandom => _luckRandomStream ??= new Random(Seed ^ 0x10CC7);
    private readonly Dictionary<long, (EmoteKind Kind, float Remaining)> _emotes = new();
    private readonly Dictionary<long, AggroTarget> _aggroTargets = new();
    private float _emoteThinkTimer;
    private float _decorScanTimer;
    private List<(string Key, GridPoint Cell, ItemId Item)>? _decorCache;

    /// <summary>Per tick, after creatures move: afflictions, colonist contact hits, emotes and placed decor.</summary>
    private void UpdatePlayerSystems(float deltaSeconds)
    {
        UpdateAfflictions(deltaSeconds);
        UpdateColonistContact();
        UpdateEmotes(deltaSeconds);
        _decorScanTimer += deltaSeconds;
        if (_decorScanTimer >= 0.5f)
        {
            _decorScanTimer = 0f;
            ScanPlacedDecor();
        }
    }

    // ================================================================== afflictions on creatures

    /// <summary>Default affliction length in seconds.</summary>
    public static float AfflictionSeconds(StatusId id) => id switch
    {
        StatusId.Burning => 4f, StatusId.Poison => 6f, StatusId.Frostbite => 4f,
        StatusId.ArmorShred => 6f, StatusId.Confused => 3f, StatusId.Marked => 8f, _ => 0f
    };

    /// <summary>Damage per second an affliction deals.</summary>
    public static float AfflictionDamagePerSecond(StatusId id) => id switch
    {
        StatusId.Burning => 6f, StatusId.Poison => 4f, StatusId.Frostbite => 2f, _ => 0f
    };

    /// <summary>True when the creature shrugs the affliction off (bosses ignore confusion; machines, fire- and ice-born resist their own kind).</summary>
    public static bool ResistsAffliction(EnemyAgent enemy, StatusId id)
    {
        if (Array.IndexOf(CreatureAfflictions, id) < 0)
            return true;
        if (enemy.IsBoss && id == StatusId.Confused)
            return true;
        var name = enemy.DisplayName + " " + enemy.Kind;
        bool Has(params string[] words) => words.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));
        return id switch
        {
            StatusId.Poison => Has("droid", "drone", "mech", "golem", "sentinel", "obelisk", "turret", "construct", "warship"),
            StatusId.Burning => Has("ember", "magma", "pyro", "flare", "solar", "fire", "lava", "molten"),
            StatusId.Frostbite => Has("frost", "glacial", "ice", "snow", "cryo", "rime"),
            StatusId.Confused => Has("obelisk", "turret"),
            _ => false
        };
    }

    /// <summary>Afflicts a creature (bosses keep afflictions half as long). False when it resists.</summary>
    public bool AfflictEnemy(EnemyAgent enemy, StatusId id, float? seconds = null)
    {
        ArgumentNullException.ThrowIfNull(enemy);
        if (!enemy.IsAlive || ResistsAffliction(enemy, id))
            return false;
        var duration = seconds ?? AfflictionSeconds(id);
        enemy.Afflict(id, enemy.IsBoss ? duration * 0.5f : duration);
        return true;
    }

    /// <summary>Damage multiplier from afflictions: armour shred opens the creature up, a mark a little more.</summary>
    public static float EnemyDamageTakenFactor(EnemyAgent enemy)
    {
        var factor = 1f;
        if (enemy.HasAffliction(StatusId.ArmorShred))
            factor *= enemy.IsBoss ? 1.12f : 1.25f;
        if (enemy.HasAffliction(StatusId.Marked))
            factor *= 1.1f;
        return factor;
    }

    /// <summary>The affliction a weapon or ammo applies on hit and its percent chance, or null.</summary>
    public static (StatusId Status, int Chance)? WeaponAffliction(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.GrantsStatus is { } granted && Array.IndexOf(CreatureAfflictions, granted) >= 0 &&
            item.UseKind is not (ItemUseKind.Consumable or ItemUseKind.Food))
            return (granted, 100);
        if (!item.Calibratable && item.UseKind != ItemUseKind.Ammunition)
            return null;
        var name = item.Id.ToString();
        bool Has(params string[] words) => words.Any(word => name.Contains(word, StringComparison.Ordinal));
        if (Has("Flame", "Ember", "Pyro", "Inferno", "Magma", "Blaze", "Incendiary", "Thermite", "Solar"))
            return (StatusId.Burning, 33);
        if (Has("Venom", "Toxic", "Caustic", "Blight", "Acid", "Spore"))
            return (StatusId.Poison, 33);
        if (Has("Frost", "Cryo", "Glacial", "Rime"))
            return (StatusId.Frostbite, 33);
        if (Has("Serrated", "Shred", "Ripper", "Chainsaw", "Breaker"))
            return (StatusId.ArmorShred, 33);
        if (Has("Psi", "Hypno", "Rift"))
            return (StatusId.Confused, 20);
        if (Has("Tracer", "Spotter", "Marker"))
            return (StatusId.Marked, 100);
        return null;
    }

    private void RollAffliction(EnemyAgent enemy, StatusId status, int chance)
    {
        if (chance >= 100 || PlayerSystemsRandom.Next(100) < chance)
            AfflictEnemy(enemy, status);
    }

    /// <summary>Melee, style and blast hits: the weapon's affliction may take hold.</summary>
    private void ApplyWeaponAffliction(ItemDefinition? item, EnemyAgent enemy)
    {
        if (item is not null && enemy.IsAlive && WeaponAffliction(item) is { } affliction)
            RollAffliction(enemy, affliction.Status, affliction.Chance);
    }

    /// <summary>A shot carrying an affliction hits a creature.</summary>
    private void ApplyProjectileAffliction(ProjectileState projectile, EnemyAgent enemy)
    {
        if (projectile.InflictStatus is { } status && enemy.IsAlive)
            RollAffliction(enemy, status, projectile.InflictChance);
    }

    /// <summary>Stamps a weapon's affliction on a freshly fired projectile.</summary>
    private void StampAffliction(ProjectileState projectile, ItemDefinition item)
    {
        if (projectile.InflictStatus is null && WeaponAffliction(item) is { } affliction)
        {
            projectile.InflictStatus = affliction.Status;
            projectile.InflictChance = affliction.Chance;
        }
    }

    /// <summary>Status ammo: the last <paramref name="count"/> player shots carry the ammo's affliction.</summary>
    private void TagLastProjectiles(int count, ItemId ammo)
    {
        if (!Items.Contains(ammo) || WeaponAffliction(Items[ammo]) is not { } affliction)
            return;
        for (var i = _projectiles.Count - 1; i >= 0 && count > 0; i--, count--)
            if (_projectiles[i].Team == ProjectileTeam.Player)
            {
                _projectiles[i].InflictStatus = affliction.Status;
                _projectiles[i].InflictChance = affliction.Chance;
            }
    }

    /// <summary>Test probe: fires one player bolt that carries an affliction.</summary>
    internal ProjectileState ProbeFireAfflictedBolt(Vector2 origin, Vector2 velocity, float damage, StatusId status)
    {
        var bolt = new ProjectileState(_nextEntityId++, origin, velocity, damage, 0f, 3f, ProjectileTeam.Player)
        {
            Class = DamageClass.Ranged, InflictStatus = status
        };
        _projectiles.Add(bolt);
        return bolt;
    }

    private void UpdateAfflictions(float deltaSeconds)
    {
        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive || enemy.Afflictions.Count == 0)
                continue;
            enemy.AfflictionPulse -= deltaSeconds;
            if (enemy.AfflictionPulse <= 0f)
            {
                enemy.AfflictionPulse += 0.5f;
                var damage = enemy.Afflictions.Keys.Sum(AfflictionDamagePerSecond) * 0.5f;
                if (damage > 0f)
                    enemy.TakeDamage(damage);
            }
            var velocity = enemy.Motion.Velocity;
            if (!enemy.Archetype.Stationary)
            {
                // Frostbite: the creature covers only part of the ground it meant to.
                if (enemy.HasAffliction(StatusId.Frostbite))
                    NudgeEnemy(enemy, -new Vector2(velocity.X, enemy.Archetype.Flying ? velocity.Y : 0f) * deltaSeconds * (enemy.IsBoss ? 0.2f : 0.45f));
                // Confusion: every other moment it stumbles the wrong way.
                if (enemy.HasAffliction(StatusId.Confused) && ((TickNumber / 30 + enemy.Id) & 1) == 0)
                    NudgeEnemy(enemy, new Vector2(-velocity.X * deltaSeconds * 2f, 0f));
            }
            enemy.TickAfflictions(deltaSeconds);
        }
    }

    private void NudgeEnemy(EnemyAgent enemy, Vector2 offset)
    {
        var candidate = enemy.Motion.Position + offset;
        if (offset.LengthSquared() > 0f && !IsSolidActorPosition(candidate, enemy.Motion.Size))
            enemy.Motion.Position = candidate;
    }

    // ================================================================== luck

    /// <summary>
    /// Luck on a −1..+1 scale: lucky gear, the fortune tonic and a full moon raise it, a dark moon lowers it. It tilts
    /// rare drops, crits slightly and calibration rolls.
    /// </summary>
    public float Luck
    {
        get
        {
            var luck = 0f;
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
                if (Player.Equipment[slot] is { } item)
                    luck += PlayerSystemsContent.GearLuck(item);
            if (Player.Statuses.IsActive(StatusId.Lucky))
                luck += 0.3f;
            luck += GrowthLuckBonus;
            if (IsNight && MoonPhase == 4)
                luck += 0.1f;
            else if (IsNight && MoonPhase == 0)
                luck -= 0.1f;
            return Math.Clamp(luck, -1f, 1f);
        }
    }

    /// <summary>Crit percent from luck (a point per quarter of luck).</summary>
    public int LuckCritBonus => (int)(Luck * 4f);

    /// <summary>
    /// A percent roll tilted by luck: good luck gives a failed roll a second chance (with probability = luck), bad luck
    /// makes a success roll again.
    /// </summary>
    public static bool LuckyRoll(Random random, int percent, float luck)
    {
        var success = random.Next(100) < percent;
        if (!success && luck > 0f && random.NextDouble() < luck)
            success = random.Next(100) < percent;
        else if (success && luck < 0f && random.NextDouble() < -luck)
            success = random.Next(100) < percent;
        return success;
    }

    /// <summary>
    /// A calibration roll tilted by luck: with probability |luck| a second calibration is rolled on the luck stream and the
    /// more (good luck) or less (bad luck) valuable of the two is kept. The first roll uses the normal stream unchanged.
    /// </summary>
    public static Calibration LuckyCalibration(Random first, Random extra, ItemDefinition item, float luck)
    {
        var roll = CalibrationCatalog.Roll(first, item);
        if (luck == 0f || extra.NextDouble() >= Math.Abs(luck))
            return roll;
        var other = CalibrationCatalog.Roll(extra, item);
        var otherBetter = CalibrationCatalog.Get(other).ValueMultiplier > CalibrationCatalog.Get(roll).ValueMultiplier;
        return luck > 0f ? (otherBetter ? other : roll) : (otherBetter ? roll : other);
    }

    private Calibration RollCalibrationWithLuck(ItemDefinition item) => LuckyCalibration(CraftRandom, LuckRandom, item, Luck);

    // ================================================================== aggro

    /// <summary>Aggro from worn gear (taunt gear raises it, stealth gear lowers it).</summary>
    public int PlayerAggro
    {
        get
        {
            var aggro = 0;
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
                if (Player.Equipment[slot] is { } item)
                    aggro += PlayerSystemsContent.GearAggro(item);
            return aggro;
        }
    }

    /// <summary>Whom a hostile creature would go after right now (aggro-weighted distance; bosses always hunt the player).</summary>
    public AggroTarget AggroTargetOf(EnemyAgent enemy) =>
        ChooseAggroTarget(enemy, Player.Motion.Position + Player.Motion.Size / 2f);

    /// <summary>The point a creature moves toward this tick (records the target for contact hits on colonists).</summary>
    private Vector2 AggroGoal(EnemyAgent enemy, Vector2 playerCenter)
    {
        var target = ChooseAggroTarget(enemy, playerCenter);
        if (target.Kind == AggroTargetKind.Player)
            _aggroTargets.Remove(enemy.Id);
        else
            _aggroTargets[enemy.Id] = target;
        return target.Center;
    }

    private AggroTarget ChooseAggroTarget(EnemyAgent enemy, Vector2 playerCenter)
    {
        var player = new AggroTarget(AggroTargetKind.Player, PlayerActorId, playerCenter);
        // Bosses, divers, casters, worms and mimics keep hunting the player; walkers, hoppers and plain fliers weigh aggro.
        if (enemy.IsBoss || enemy.IsPassive || enemy.Archetype.Stationary || (_colonists.Count == 0 && _townNpcs.Count == 0) ||
            enemy.Family is EnemyAiFamily.DivingFlyer or EnemyAiFamily.Caster or EnemyAiFamily.Worm or EnemyAiFamily.Mimic)
            return player;
        var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
        var best = player;
        var bestScore = Player.IsDead ? float.MaxValue : Vector2.Distance(center, playerCenter) - PlayerAggro / 100f * AggroTilesPer100;
        foreach (var agent in _colonists)
        {
            if (!agent.IsAlive)
                continue;
            var at = agent.Motion.Position + agent.Motion.Size / 2f;
            if (!Player.IsDead && Vector2.Distance(at, playerCenter) < SharedTargetRadius)
                continue;
            var distance = Vector2.Distance(center, at);
            if (distance <= 22f && distance + ColonistAggroPenalty < bestScore)
            {
                bestScore = distance + ColonistAggroPenalty;
                best = new AggroTarget(AggroTargetKind.Colonist, agent.Id, at);
            }
        }
        foreach (var npc in _townNpcs)
        {
            if (!npc.IsAlive || npc.IsBound)
                continue;
            var at = npc.Motion.Position + npc.Motion.Size / 2f;
            if (!Player.IsDead && Vector2.Distance(at, playerCenter) < SharedTargetRadius)
                continue;
            var distance = Vector2.Distance(center, at);
            if (distance <= 22f && distance + TownNpcAggroPenalty < bestScore)
            {
                bestScore = distance + TownNpcAggroPenalty;
                best = new AggroTarget(AggroTargetKind.TownNpc, npc.Id, at);
            }
        }
        return best;
    }

    /// <summary>Creatures that chose a colonist strike it on contact (residents are already hurt by any overlap).</summary>
    private void UpdateColonistContact()
    {
        if (_aggroTargets.Count == 0)
            return;
        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive || enemy.AttackCooldown > 0f || enemy.AttackDamage <= 0f || enemy.Disguised ||
                !_aggroTargets.TryGetValue(enemy.Id, out var target) || target.Kind != AggroTargetKind.Colonist)
                continue;
            var agent = _colonists.FirstOrDefault(colonist => colonist.Id == target.Id && colonist.IsAlive);
            if (agent is null)
                continue;
            var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
            if (Vector2.Distance(center, agent.Motion.Position + agent.Motion.Size / 2f) > 1.15f)
                continue;
            agent.TakeDamage(ScaleIncoming(enemy.AttackDamage) * 0.6f, ColonistAnatomy.ContactDamageKind(enemy.Archetype));
            enemy.AttackCooldown = enemy.Archetype.ContactCooldown;
            if (PlayerSystemsRandom.Next(3) == 0)
                ShowEmote(agent.Id, EmoteKind.Scared);
        }
        _aggroTargets.Clear();
    }

    // ================================================================== salvage caches, trophies and relics

    /// <summary>On Brutal and Annihilation, a boss leaves a salvage cache instead of its loot.</summary>
    public bool BagReplacesBossLoot(EnemyAgent enemy) =>
        enemy.IsBoss && Difficulty >= WorldDifficulty.Brutal && PlayerSystemsContent.BagFor(enemy.Kind) is not null;

    /// <summary>
    /// The drops of a defeated creature that pass their chance and conditions (Fracture, night, difficulty, event; 0.1.9 C4),
    /// or its salvage cache on Brutal.
    /// </summary>
    private IReadOnlyList<NpcDropDefinition> BossDropsOrBag(EnemyAgent enemy) =>
        BagReplacesBossLoot(enemy) && PlayerSystemsContent.BagFor(enemy.Kind) is { } bag
            ? [new NpcDropDefinition(enemy.Kind, bag, 1)]
            : NpcDropRegistry.Roll(enemy.Kind, DropContext, DropRandom);

    /// <summary>What drop conditions see right now.</summary>
    public NpcDropContext DropContext => new(FractureActive, IsNight, Difficulty,
        _worldEvents.Active.Select(state => state.Kind).ToHashSet());

    /// <summary>Lucky extra drops, trophies (any difficulty, a small luck-tilted chance) and relics (Annihilation only).</summary>
    private void OnCreatureDefeatedPlayerSystems(EnemyAgent enemy)
    {
        var luck = Luck;
        var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
        if (!enemy.IsBoss && luck > 0f && ArsenalItems.MobDrops.TryGetValue(enemy.Kind, out var drops))
            foreach (var (item, percent) in drops)
                if (LuckRandom.NextDouble() < luck * 0.5f && LuckRandom.Next(100) < percent)
                    DropLoot(enemy, item, 1);
        if (!enemy.IsBoss)
            return;
        if (PlayerSystemsContent.TrophyFor(enemy.Kind) is { } trophy && LuckyRoll(LuckRandom, 10, luck))
        {
            SpawnGroundItem(trophy, 1, center, pickupDelay: 1f);
            PostMessage($"The {enemy.DisplayName} left a trophy behind.");
        }
        if (Difficulty == WorldDifficulty.Annihilation && PlayerSystemsContent.RelicFor(enemy.Kind) is { } relic) // relics are Annihilation-only
            SpawnGroundItem(relic, 1, center + new Vector2(-0.6f, 0f), pickupDelay: 1f);
    }

    /// <summary>
    /// Opens a salvage cache: the boss's guaranteed loot (a little more of each stack), one piece of its gear pool,
    /// its exclusive core-chip and some credits.
    /// </summary>
    public bool OpenTreasureBag(ItemId bag)
    {
        if (PlayerSystemsContent.BagBoss(bag) is not { } boss || !Player.Inventory.TryConsume(bag))
            return false;
        var center = PlayerCenter;
        foreach (var drop in NpcDropRegistry.Roll(boss, DropContext, DropRandom))
            CollectOrDrop(drop.Item, drop.Quantity > 1 ? (int)MathF.Ceiling(drop.Quantity * 1.25f) : drop.Quantity, center);
        if (ArsenalItems.BossPools.TryGetValue(boss, out var pool) && pool.Length > 0)
            CollectOrDrop(pool[PlayerSystemsRandom.Next(pool.Length)], 1, center);
        if (PlayerSystemsContent.ExclusiveFor(boss) is { } exclusive)
            CollectOrDrop(exclusive, 1, center);
        CollectOrDrop(ItemId.Credits, 40 * Math.Max(1, Items[bag].Tier), center);
        PostMessage($"Opened the {Items[bag].Name}.");
        return true;
    }

    /// <summary>Remembers which boss item went into a trophy or relic cell (so breaking it returns that item).</summary>
    private void RememberPlacedDecor(ItemDefinition item, GridPoint cell)
    {
        if (item.PlacedTile is not (TileId.BossTrophy or TileId.BossRelic))
            return;
        var key = DecorKey(cell);
        Flags.Clear(key);
        Flags.Increment(key, (long)item.Id);
        _decorCache = null;
    }

    private static string DecorKey(GridPoint cell) => $"decor.{cell.X}.{cell.Y}";

    /// <summary>The boss trophy or relic item placed in a cell, or null.</summary>
    public ItemId? PlacedDecorItem(GridPoint cell) => Flags.Get(DecorKey(cell)) is var value and > 0 ? (ItemId)value : null;

    /// <summary>Trophy and relic cells that were broken give back their own item.</summary>
    private void ScanPlacedDecor()
    {
        _decorCache ??= Flags.All
            .Where(pair => pair.Key.StartsWith("decor.", StringComparison.Ordinal) && pair.Value > 0)
            .Select(pair => (pair.Key, Parse(pair.Key), (ItemId)pair.Value))
            .Where(entry => entry.Item2 is not null)
            .Select(entry => (entry.Key, entry.Item2!.Value, entry.Item3))
            .ToList();
        if (_decorCache.Count == 0)
            return;
        foreach (var (key, cell, item) in _decorCache.ToArray())
        {
            if (World.Contains(cell) && World.GetCell(cell).TileId is TileId.BossTrophy or TileId.BossRelic)
                continue;
            Flags.Clear(key);
            _decorCache.Remove((key, cell, item));
            if (World.Contains(cell))
                SpawnGroundItem(item, 1, new Vector2(cell.X + 0.5f, cell.Y + 0.5f));
        }

        static GridPoint? Parse(string key)
        {
            var parts = key.Split('.');
            return parts.Length == 3 && int.TryParse(parts[1], out var x) && int.TryParse(parts[2], out var y) ? new GridPoint(x, y) : null;
        }
    }

    /// <summary>Test probe: places an item from the inventory exactly as a click would.</summary>
    internal void ProbePlace(ItemId item, GridPoint cell)
    {
        var slot = Player.Inventory.Slots.ToList().FindIndex(stack => !stack.IsEmpty && stack.ItemId == item);
        if (slot < 0)
            return;
        TrySelectInventorySlot(slot);
        Player.ActionCooldownTicks = 0;
        Place(Items[item], cell);
    }

    // ================================================================== item uses and interaction hooks

    /// <summary>Salvage caches, dyes and dye solvent. True when handled.</summary>
    private bool UsePlayerSystemsItem(ItemDefinition item, GridPoint point)
    {
        if (PlayerSystemsContent.BagBoss(item.Id) is not null)
        {
            OpenTreasureBag(item.Id);
            SetPlayerCooldown(12);
            return true;
        }
        if (PlayerSystemsContent.IsDye(item.Id))
        {
            if (NextDyeSlot(item.Id) is { } slot && ApplyDye(item.Id, slot))
                PostMessage($"Dyed your {slot.ToString().ToLowerInvariant()} gear with {item.Name.ToLowerInvariant()}.");
            else
                PostMessage("Wear some armour or an accessory to dye it.");
            SetPlayerCooldown(12);
            return true;
        }
        if (item.Id == ItemId.DyeSolvent)
        {
            if (Enum.GetValues<DyeSlot>().Any(slot => DyeIn(slot) is not null) && Player.Inventory.TryConsume(ItemId.DyeSolvent))
            {
                foreach (var slot in Enum.GetValues<DyeSlot>())
                    Flags.Clear(DyeKey(slot));
                PostMessage("The solvent washes the dye out of your gear.");
            }
            else
                PostMessage("Nothing is dyed.");
            SetPlayerCooldown(12);
            return true;
        }
        return false;
    }

    /// <summary>Interaction: a selected salvage cache opens (right-click), and the sifter sifts sediment. True when handled.</summary>
    public bool TryInteractPlayerSystems(GridPoint target)
    {
        if (Player.Inventory.SelectedItem is { } selected && PlayerSystemsContent.BagBoss(selected.Id) is not null)
            return OpenTreasureBag(selected.Id);
        if (!World.Contains(target) || World.GetCell(target).TileId != TileId.Sifter ||
            Vector2.Distance(PlayerCenter, new Vector2(target.X + 0.5f, target.Y + 0.5f)) > SifterReach + 1f)
            return false;
        var sediment = Player.Inventory.SelectedItem is { } held && PlayerSystemsContent.Sediments.ContainsKey(held.Id)
            ? held.Id
            : PlayerSystemsContent.Sediments.Keys.FirstOrDefault(id => Player.Inventory.Count(id) > 0);
        if (Player.Inventory.Count(sediment) == 0)
        {
            PostMessage("The sifter needs silt, gravel or ash.");
            return true;
        }
        var finds = new List<string>();
        for (var i = 0; i < 5 && Sift(sediment) is { } find; i++)
            finds.Add($"{find.Count} {Items[find.Item].Name}");
        PostMessage(finds.Count > 0 ? $"Sifted: {string.Join(", ", finds)}." : "Nothing turned up.");
        return true;
    }

    // ================================================================== sifting

    /// <summary>True when a sifter stands within reach.</summary>
    public bool SifterNearby => FindNearbyTile(TileId.Sifter, SifterReach) is not null;

    /// <summary>
    /// Sifts one sediment block at a nearby sifter: a weighted find from the table (deeper sediment holds better ores;
    /// after the Fracture hardmode ores appear). Luck may roll twice and keep the rarer find. Null when nothing was sifted.
    /// </summary>
    public (ItemId Item, int Count)? Sift(ItemId sediment)
    {
        if (!PlayerSystemsContent.Sediments.TryGetValue(sediment, out var tier) || !SifterNearby || !Player.Inventory.TryConsume(sediment))
            return null;
        var pool = PlayerSystemsContent.SiftTable.Where(entry => entry.Tier <= tier && (!entry.Fractured || FractureActive)).ToArray();
        var pick = PickSift(pool);
        if (Luck > 0f && LuckRandom.NextDouble() < Luck)
        {
            var other = PickSift(pool);
            if (other.Weight < pick.Weight)
                pick = other;
        }
        var count = PlayerSystemsRandom.Next(pick.Min, pick.Max + 1);
        CollectOrDrop(pick.Item, count, PlayerCenter);
        Flags.Increment("sifter.sifted");
        return (pick.Item, count);
    }

    private (ItemId Item, int Weight, int Min, int Max, int Tier, bool Fractured) PickSift(
        (ItemId Item, int Weight, int Min, int Max, int Tier, bool Fractured)[] pool)
    {
        var roll = PlayerSystemsRandom.Next(pool.Sum(entry => entry.Weight));
        foreach (var entry in pool)
        {
            if (roll < entry.Weight)
                return entry;
            roll -= entry.Weight;
        }
        return pool[^1];
    }

    // ================================================================== dyes

    private static string DyeKey(DyeSlot slot) => $"dye.{slot}";

    /// <summary>The dye applied to a gear slot, or null.</summary>
    public ItemId? DyeIn(DyeSlot slot) => Flags.Get(DyeKey(slot)) is var value and > 0 ? (ItemId)value : null;

    /// <summary>The tint (0xRRGGBB) a dyed slot draws with, or null when undyed or empty.</summary>
    public int? DyeTint(DyeSlot slot) =>
        SlotDressed(slot) && DyeIn(slot) is { } dye && PlayerSystemsContent.DyeColors.TryGetValue(dye, out var color) ? color : null;

    /// <summary>True when the slot shows something (armour or vanity; any accessory for the accessory row).</summary>
    public bool SlotDressed(DyeSlot slot) => slot switch
    {
        DyeSlot.Head => Player.Equipment[EquipmentSlot.Head] is not null || Player.Equipment[VanitySlot.Head] is not null,
        DyeSlot.Body => Player.Equipment[EquipmentSlot.Body] is not null || Player.Equipment[VanitySlot.Body] is not null,
        DyeSlot.Legs => Player.Equipment[EquipmentSlot.Legs] is not null || Player.Equipment[VanitySlot.Legs] is not null,
        _ => EquipmentLoadout.AccessorySlots.Any(accessory => Player.Equipment[accessory] is not null)
    };

    /// <summary>Applies a dye from the inventory to a dressed slot; a dye already there goes back to the pack.</summary>
    public bool ApplyDye(ItemId dye, DyeSlot slot)
    {
        if (!PlayerSystemsContent.IsDye(dye) || !SlotDressed(slot) || !Player.Inventory.TryConsume(dye))
            return false;
        if (DyeIn(slot) is { } old)
            CollectOrDrop(old, 1, PlayerCenter);
        Flags.Clear(DyeKey(slot));
        Flags.Increment(DyeKey(slot), (long)dye);
        return true;
    }

    /// <summary>Using a dye colours the first dressed slot without it (undyed slots first).</summary>
    private DyeSlot? NextDyeSlot(ItemId dye)
    {
        var dressed = Enum.GetValues<DyeSlot>().Where(SlotDressed).ToArray();
        if (dressed.Length == 0)
            return null;
        return dressed.FirstOrDefault(slot => DyeIn(slot) is null, dressed.FirstOrDefault(slot => DyeIn(slot) != dye, dressed[0]));
    }

    // ================================================================== emotes

    /// <summary>Live emote bubbles with their anchor points.</summary>
    public IReadOnlyList<EmoteBubble> Emotes
    {
        get
        {
            var list = new List<EmoteBubble>(_emotes.Count);
            foreach (var (id, (kind, remaining)) in _emotes)
                if (EmoteAnchor(id) is { } anchor)
                    list.Add(new EmoteBubble(id, kind, anchor, remaining));
            return list;
        }
    }

    /// <summary>The player shows an emote for a few seconds; a wave may be returned by nearby crew and residents.</summary>
    public void PlayerEmote(EmoteKind kind)
    {
        ShowEmote(PlayerActorId, kind);
        if (kind != EmoteKind.Wave)
            return;
        foreach (var agent in _colonists.Where(agent => agent.IsAlive && Vector2.Distance(agent.Motion.Position, Player.Motion.Position) < 8f))
            if (PlayerSystemsRandom.Next(2) == 0)
                ShowEmote(agent.Id, EmoteKind.Wave);
        foreach (var npc in _townNpcs.Where(npc => npc.IsAlive && Vector2.Distance(npc.Motion.Position, Player.Motion.Position) < 8f))
            if (PlayerSystemsRandom.Next(2) == 0)
                ShowEmote(npc.Id, EmoteKind.Wave);
    }

    private void ShowEmote(long actorId, EmoteKind kind) => _emotes[actorId] = (kind, EmoteSeconds);

    private Vector2? EmoteAnchor(long id)
    {
        if (id == PlayerActorId)
            return Player.Motion.Position + new Vector2(Player.Motion.Size.X / 2f, -0.4f);
        if (_colonists.FirstOrDefault(agent => agent.Id == id) is { IsAlive: true } colonist)
            return colonist.Motion.Position + new Vector2(colonist.Motion.Size.X / 2f, -0.4f);
        if (_townNpcs.FirstOrDefault(npc => npc.Id == id) is { IsAlive: true } npc)
            return npc.Motion.Position + new Vector2(npc.Motion.Size.X / 2f, -0.4f);
        return null;
    }

    private void UpdateEmotes(float deltaSeconds)
    {
        foreach (var id in _emotes.Keys.ToArray())
        {
            var (kind, remaining) = _emotes[id];
            remaining -= deltaSeconds;
            if (remaining <= 0f || EmoteAnchor(id) is null)
                _emotes.Remove(id);
            else
                _emotes[id] = (kind, remaining);
        }
        _emoteThinkTimer += deltaSeconds;
        if (_emoteThinkTimer < 2.5f)
            return;
        _emoteThinkTimer = 0f;
        var bossAlive = _enemies.Any(enemy => enemy.IsAlive && enemy.IsBoss);
        bool Threatened(Vector2 at) => bossAlive || _enemies.Any(enemy => enemy.IsAlive && !enemy.IsPassive &&
                                                                          Vector2.Distance(enemy.Motion.Position, at) < 7f);
        foreach (var agent in _colonists)
        {
            if (!agent.IsAlive || _emotes.ContainsKey(agent.Id) || PlayerSystemsRandom.Next(100) >= 12)
                continue;
            EmoteKind? kind = Threatened(agent.Motion.Position) ? EmoteKind.Scared
                : agent.Needs.FoodReserve < 0.3f ? EmoteKind.Hungry
                : IsNight && (ClockHour >= 23f || ClockHour < 4f) ? EmoteKind.Sleepy
                : PlayerSystemsRandom.Next(4) == 0 ? EmoteKind.Happy : null;
            if (kind is { } chosen)
                ShowEmote(agent.Id, chosen);
        }
        foreach (var npc in _townNpcs)
        {
            if (!npc.IsAlive || npc.IsBound || _emotes.ContainsKey(npc.Id) || PlayerSystemsRandom.Next(100) >= 10)
                continue;
            EmoteKind? kind = Threatened(npc.Motion.Position) ? EmoteKind.Scared
                : npc.Happiness >= 1f ? (PlayerSystemsRandom.Next(3) == 0 ? EmoteKind.Music : EmoteKind.Happy)
                : npc.Happiness <= -1f ? (npc.HomeCell is null ? EmoteKind.Question : EmoteKind.Sad)
                : IsNight && (ClockHour >= 23f || ClockHour < 4f) ? EmoteKind.Sleepy : null;
            if (kind is { } chosen)
                ShowEmote(npc.Id, chosen);
        }
    }
}
