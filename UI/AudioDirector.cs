using Rimtera.Entities;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>
/// Plays the simulation once per frame. It drains the simulation's sound events (PE-4: digs, breaks, walls, Tech and
/// Command uses, crafts, coins, drinks, food, doors, containers, splashes, explosions, the death, double jumps, pickups),
/// its death records (PE-5: only kills cry out, despawns are silent, catches are a soft grab) and its hit records (PE-14:
/// crits ring, damage over time is silent), and still infers hits, hurt, weapon and tool swings, lightning, bites, boss
/// arrivals and events from what changed. World sounds pan and fall off with distance, and each kind is rate limited.
/// Also chooses the music theme and tension from biome, depth, bosses and events.
/// </summary>
internal sealed class AudioDirector
{
    private readonly AudioEngine _audio;
    private GameSimulation? _game;
    private readonly Dictionary<long, float> _enemyHp = new();
    private float _playerHp;
    private int _cooldown;
    private int _itemTotal;
    private int _lightningSeen;
    private bool _bossAlive;
    private readonly HashSet<WorldEventKind> _eventKinds = new();
    private int _frame;
    private static readonly Dictionary<WorldEventKind, WorldEventSpec> EventSpecs = new WorldEventSystem(1).Specs.ToDictionary(spec => spec.Kind);
    private static readonly string[] SpotLoops = ["amb_waterfall", "amb_uranium_flow", "amb_machinery", "amb_fire"];
    private bool _biting;
    private float _playerImmunity;
    private long _soundCursor;
    private long _deathCursor;
    private long _hitCursor;
    private readonly List<SoundEvent> _sounds = new();
    private readonly List<DeathRecord> _deaths = new();
    private readonly List<HitRecord> _hits = new();
    private readonly HashSet<long> _struck = new();
    private readonly HashSet<long> _critical = new();
    private readonly SoundRateLimiter _limiter = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public AudioDirector(AudioEngine audio) => _audio = audio;

    public void Title() => _audio.SetMusicMood("title", 0f, true);

    public void Update(GameSimulation game, GamePreferences preferences)
    {
        _audio.SoundVolume = preferences.SoundVolume;
        _audio.MusicVolume = preferences.MusicVolume;
        _audio.AmbientVolume = preferences.AmbientVolume;
        if (!ReferenceEquals(game, _game))
        {
            _game = game;
            _enemyHp.Clear();
            _playerHp = game.Player.HitPoints;
            _playerImmunity = game.Player.ImmunitySeconds;
            _itemTotal = TotalItems(game);
            _lightningSeen = game.LightningStrikes.Count;
            // Start the readers at the end of the logs: a loaded or new world never replays its past.
            _soundCursor = game.SoundEvents.Next;
            _deathCursor = game.RecentDeaths.Next;
            _hitCursor = game.HitRecords.Next;
            _limiter.Reset();
        }

        var player = game.Player;
        var center = player.Motion.Position;
        float Pan(System.Numerics.Vector2 at) => Math.Clamp((at.X - center.X) / 30f, -1f, 1f);
        float Near(System.Numerics.Vector2 at) => Math.Clamp(1f - System.Numerics.Vector2.Distance(at, center) / 45f, 0f, 1f);
        var now = _clock.Elapsed.TotalSeconds;
        void PlayAt(string key, string cue, System.Numerics.Vector2 at, float volume = 1f)
        {
            if (_limiter.Allow(key, now))
                _audio.Play(cue, volume * (0.25f + 0.75f * Near(at)), Pan(at));
        }

        // Direct hits this frame (with their crit flag), read before the hit points so damage over time stays quiet.
        _hits.Clear();
        game.HitRecords.Drain(ref _hitCursor, _hits);
        _struck.Clear();
        _critical.Clear();
        foreach (var hit in _hits)
        {
            if (hit.Kind != HitKind.Direct || hit.TargetId < 0)
                continue;
            _struck.Add(hit.TargetId);
            if (hit.Critical)
                _critical.Add(hit.TargetId);
        }

        var seen = new HashSet<long>();
        foreach (var enemy in game.Enemies)
        {
            seen.Add(enemy.Id);
            if (_enemyHp.TryGetValue(enemy.Id, out var hp) && enemy.HitPoints < hp - 0.01f)
            {
                var (kind, critical) = CombatTextRules.ClassifyCreatureLoss(_struck.Contains(enemy.Id), _critical.Contains(enemy.Id),
                    enemy.Afflictions.Count > 0);
                // Damage over time (poison, burning) ticks silently, as in Terraria.
                if (kind == HitKind.Direct && _limiter.Allow("impact", now))
                {
                    // The weapon lands on the creature's body (impact_metal, impact_slime …), then the creature answers.
                    var volume = 0.8f * Near(enemy.Motion.Position) + 0.2f;
                    var pan = Pan(enemy.Motion.Position);
                    _audio.Play("impact_" + SoundCatalog.CreatureMaterial(enemy.Archetype), volume * 0.8f, pan);
                    if (critical)
                        _audio.Play("impact_crit", volume * 0.7f, pan);
                    // The death cry comes from the death record (real kills only); a living creature answers the hit.
                    if (enemy.IsAlive)
                        _audio.Play(enemy.IsBoss ? SoundCatalog.BossCue(enemy.Kind, "hurt") : SoundCatalog.CreatureVoice(enemy.Archetype) + "_hurt",
                            volume, pan);
                }
            }
            _enemyHp[enemy.Id] = enemy.IsAlive ? enemy.HitPoints : 0f;
        }
        foreach (var id in _enemyHp.Keys.Where(id => !seen.Contains(id)).ToArray())
            _enemyHp.Remove(id);

        // PE-5: only creatures that died cry out. Despawns are silent; a bug-net catch is a soft grab.
        _deaths.Clear();
        game.RecentDeaths.Drain(ref _deathCursor, _deaths);
        foreach (var death in _deaths)
        {
            if (death.Reason == DeathReason.Caught)
                PlayAt("catch", "catch_critter", death.Position, 0.7f);
            else if (death.Reason == DeathReason.Died && EnemyArchetypes.Contains(death.Kind))
            {
                var archetype = EnemyArchetypes.Get(death.Kind);
                PlayAt("death", archetype.IsBoss ? SoundCatalog.BossCue(death.Kind, "death") : SoundCatalog.CreatureVoice(archetype) + "_death",
                    death.Position);
            }
        }

        // The player's hurt grunt is for hits (they start hit immunity); poison and burning tick quietly.
        if (player.HitPoints < _playerHp - 0.5f &&
            CombatTextRules.ClassifyPlayerLoss(player.ImmunitySeconds > _playerImmunity + 0.001f, game.PlayerHasDamagingStatus,
                game.PlayerInHarmfulLiquid) == HitKind.Direct)
            _audio.Play("hurt");
        _playerHp = player.HitPoints;
        _playerImmunity = player.ImmunitySeconds;
        // D-59: a plain jump or landing makes no sound (a double jump is a simulation event, below).

        if (player.ActionCooldownTicks > _cooldown)
        {
            var item = player.Inventory.SelectedItem;
            // Tech and Command uses are simulation events (they sound only when the charge is spent or the drone deploys);
            // beams keep the per-use cue. Walls, food and potions sound from their events too.
            var fromEvents = item is { UseKind: ItemUseKind.Tech or ItemUseKind.Command } && item.Style != WeaponStyle.Beam ||
                             item?.UseKind is ItemUseKind.PlaceWall or ItemUseKind.Food ||
                             item is { UseKind: ItemUseKind.Consumable } && (item.HealAmount > 0f || item.ChargeRestore > 0f || item.GrantsStatus is not null);
            var sound = item is null || fromEvents ? "" : SoundCatalog.WeaponCue(item);
            if (sound.Length == 0 && item is not null && !fromEvents)
                sound = item.UseKind switch
                {
                    ItemUseKind.Melee => "swing",
                    ItemUseKind.Ranged or ItemUseKind.Tech => item.EffectiveClass == DamageClass.Tech ? "zap" : "shoot",
                    _ => SoundCatalog.ToolCue(item.UseKind)
                };
            if (sound.Length > 0)
                _audio.Play(sound, 0.8f);
        }
        _cooldown = player.ActionCooldownTicks;

        // PE-4: everything else the simulation heard, at its place in the world.
        _sounds.Clear();
        game.SoundEvents.Drain(ref _soundCursor, _sounds);
        var gained = false;
        foreach (var sound in _sounds)
        {
            gained |= sound.Kind is SoundEventKind.Pickup or SoundEventKind.Craft or SoundEventKind.Coin or SoundEventKind.Catch;
            if (CueFor(game, sound) is { } cue)
                PlayAt(cue.Key, cue.Cue, sound.Position, cue.Volume);
            if (sound.Kind == SoundEventKind.PlayerDeath)
                _audio.Play("death_sting", 0.9f);
        }

        // Items that arrive without a simulation event (chest loot, fishing catches) still pop; crafts never do.
        var total = TotalItems(game);
        if (total > _itemTotal && !gained)
            _audio.Play("pickup", 0.6f);
        _itemTotal = total;

        if (game.LightningStrikes.Count > _lightningSeen)
            _audio.Play("thunder", 0.9f, Pan(game.LightningStrikes[^1].Position));
        _lightningSeen = game.LightningStrikes.Count;

        var boss = game.Enemies.FirstOrDefault(enemy => enemy.IsAlive && enemy.IsBoss);
        var bossAlive = boss is not null;
        if (boss is not null && !_bossAlive)
            _audio.Play(SoundCatalog.BossCue(boss.Kind, "roar"));
        _bossAlive = bossAlive;

        var events = game.EventProgress.Count;
        foreach (var progress in game.EventProgress)
            if (_eventKinds.Add(progress.Kind))
                _audio.Play(SoundCatalog.EventCue(progress.Kind, "start"));
        foreach (var ended in _eventKinds.Where(kind => game.EventProgress.All(progress => progress.Kind != kind)).ToArray())
        {
            _audio.Play(SoundCatalog.EventCue(ended, "end"), 0.8f);
            _eventKinds.Remove(ended);
        }

        var biting = game.Bobber?.Biting ?? false;
        if (biting && !_biting)
            _audio.Play("bite");
        _biting = biting;

        _audio.SetRain(game.CurrentBiome.Underground ? 0f : game.Weather.Rain);
        var place = Place(game);
        game.HeardMusicTheme = place; place = game.MusicThemeOverride ?? place; // audio decks (GameSimulation.Leisure.cs)
        var night = game.ClockHour is >= 19f or < 5f;
        var weather = game.Weather;
        var moment = new AudioMoment(place, game.CurrentBiome.Underground, night, boss?.Archetype.Tier ?? BossTier.None,
            game.EventProgress.Select(progress => EventSpecs.GetValueOrDefault(progress.Kind)).OfType<WorldEventSpec>().ToList(),
            weather.Rain, weather.Wind, weather.Snow, weather.Dust, weather.AcidRain, game.HeadSubmerged);

        // A recorded or generated track plays when one fits (boss, event, biome); otherwise the synth composes.
        _audio.PlayMusic(AudioScene.Music(moment, _audio.HasLoop));
        _audio.SetAmbience(AudioScene.Ambience(moment, _audio.HasLoop));
        if (_frame++ % 20 == 0)
            UpdateSpots(game);
        var tension = bossAlive ? 1f : events > 0 ? 0.7f : Math.Min(0.5f, game.Enemies.Count(enemy => enemy.IsAlive && !enemy.IsPassive) * 0.08f);
        _audio.SetMusicMood(bossAlive ? "boss" : SynthTheme(place), tension, night);
    }

    /// <summary>
    /// The cue for a simulation sound event (PE-4), with its rate-limit key and volume: material break and dig cues,
    /// wall placing, Tech and Command uses, station crafts, shop coins, drinks, food, doors, containers, splashes by
    /// liquid, explosions, the death, the double jump and pickups. Null when the event has no cue of its own.
    /// </summary>
    internal static (string Key, string Cue, float Volume)? CueFor(GameSimulation game, SoundEvent sound)
    {
        // The simulation's materials as catalog tile classes (plant sounds organic); anything else plays the plain cue.
        static string? Material(string variant) => variant switch
        {
            "plant" => "organic",
            "stone" or "soil" or "glass" or "metal" => variant,
            _ => null
        };
        switch (sound.Kind)
        {
            case SoundEventKind.Break:
                return ("break", sound.Variant == "wall" || Material(sound.Variant) is not { } broke ? "break" : $"break_{broke}", 0.9f);
            case SoundEventKind.Dig:
                return ("dig", sound.Variant == "wall" ? "tool_hammer_wall" : Material(sound.Variant) is { } struck ? $"dig_{struck}" : "dig", 0.7f);
            case SoundEventKind.Place:
                return ("place", "place_wall", 0.6f);
            case SoundEventKind.Use:
            {
                var item = game.Items.Contains((ItemId)sound.Detail) ? game.Items[(ItemId)sound.Detail] : null;
                var styled = item is null ? "" : SoundCatalog.WeaponCue(item);
                return ("use", styled.Length > 0 ? styled : sound.Variant == "command" ? "use_summon_staff" : "zap", 0.8f);
            }
            case SoundEventKind.Craft:
                return ("craft", sound.Variant is "smelt" or "forge" or "brew" or "weave" ? $"craft_{sound.Variant}" : "craft", 0.8f);
            case SoundEventKind.Coin:
                return ("coin", sound.Variant == "sell" ? "shop_sell" : "shop_buy", 0.8f);
            case SoundEventKind.Drink:
                return ("drink", "potion", 0.8f);
            case SoundEventKind.Eat:
                return ("eat", "eat", 0.8f);
            case SoundEventKind.DoorOpen:
                return ("door", "door_open", 0.8f);
            case SoundEventKind.DoorClose:
                return ("door", "door_close", 0.8f);
            case SoundEventKind.ChestOpen:
                return ("chest", "chest_open", 0.8f);
            case SoundEventKind.ChestClose:
                return ("chest", "chest_close", 0.7f);
            case SoundEventKind.Splash:
                return ("splash", sound.Variant switch
                {
                    "water" => "splash_water",
                    "lava" => "splash_uranium",
                    "acid" => "splash_acid",
                    "fuel" => "splash_fuel",
                    _ => "splash"
                }, sound.Detail == 1 ? 0.8f : 0.5f);
            case SoundEventKind.Explosion:
                return ("explosion", "explosion", 1f);
            case SoundEventKind.PlayerDeath:
                return ("player_death", "death", 1f);
            case SoundEventKind.DoubleJump:
                return ("double_jump", "jump_double", 0.7f);
            case SoundEventKind.Pickup:
                return ("pickup", sound.Variant == "coin" ? "pickup_coin" : "pickup", 0.6f);
            default:
                return null; // a catch sounds from its death record (DeathReason.Caught)
        }
    }

    /// <summary>
    /// Where the player is, as a music and ambience theme: the biome (with Terraria's special places: space high up,
    /// a town among three or more residents, the hives, warrens and vault below).
    /// </summary>
    private static string Place(GameSimulation game)
    {
        var biome = game.CurrentBiome;
        var position = game.Player.Motion.Position;
        if (!biome.Underground && position.Y < game.World.Height * 0.08f)
            return "space";
        if (!biome.Underground && game.TownNpcs.Count(npc => System.Numerics.Vector2.Distance(npc.Motion.Position, position) < 40f) >= 3)
            return "town";
        if (biome.Underground && biome.Zone is BiomeZone.Grassland or BiomeZone.Caverns)
            return "underground";
        return biome.Zone switch
        {
            BiomeZone.FrozenWasteland => "frozen",
            BiomeZone.JunkWasteland => "junk",
            BiomeZone.MoltenChasm => "molten",
            BiomeZone.RadiatedDeeps => "radiated",
            BiomeZone.Overgrowth => "overgrowth",
            BiomeZone.FungalHollows => "fungal",
            BiomeZone.CrystalSpires => "crystal",
            BiomeZone.VerdantCanopy => "canopy",
            BiomeZone.AcidCoast => "coast",
            BiomeZone.BloomHives => "hives",
            BiomeZone.MiteWarrens => "warrens",
            BiomeZone.PrecursorVault => "vault",
            _ => biome.DisplayName.ToLowerInvariant() switch
            {
                var name when name.Contains("fungal") => "fungal",
                var name when name.Contains("crystal") => "crystal",
                var name when name.Contains("canopy") || name.Contains("verdant") => "canopy",
                var name when name.Contains("coast") => "coast",
                _ => "grassland"
            }
        };
    }

    /// <summary>The procedural theme closest to a place that has no synth theme of its own.</summary>
    private static string SynthTheme(string place) => place switch
    {
        "hives" => "canopy",
        "warrens" => "underground",
        "vault" => "crystal",
        "space" => "frozen",
        "town" => "grassland",
        "ocean" => "coast",
        _ => place
    };

    /// <summary>
    /// Positional loops near the player: falling water (a waterfall), molten flows, running stations (machinery) and
    /// campfires. Each grows louder with how much of it is close and pans toward where it is.
    /// </summary>
    private void UpdateSpots(GameSimulation game)
    {
        const int radius = 24;
        Span<float> weight = stackalloc float[SpotLoops.Length];
        Span<float> side = stackalloc float[SpotLoops.Length];
        var world = game.World;
        var center = game.Player.Motion.Position;
        var cx = (int)center.X;
        var cy = (int)center.Y;
        for (var y = Math.Max(1, cy - radius); y < Math.Min(world.Height - 1, cy + radius); y++)
        {
            for (var x = Math.Max(0, cx - radius); x < Math.Min(world.Width, cx + radius); x++)
            {
                var cell = world.GetCell(x, y);
                var spot = -1;
                if (cell.LiquidUnits > 0 && cell.LiquidKind is LiquidKind.Water or LiquidKind.Lava)
                {
                    var below = world.GetCell(x, y + 1);
                    var falling = !game.Tiles[below.TileId].IsSolid && below.LiquidUnits < WorldCell.LiquidUnitsMax / 2;
                    spot = cell.LiquidKind == LiquidKind.Lava ? 1 : falling ? 0 : -1;
                }
                else if (cell.TileId == TileId.Campfire)
                    spot = 3;
                else if (cell.TileId != TileId.Air && game.Tiles[cell.TileId].Station != CraftingStationId.None)
                    spot = 2;
                if (spot < 0)
                    continue;
                var dx = x + 0.5f - center.X;
                var near = 1f / (1f + MathF.Sqrt(dx * dx + (y + 0.5f - center.Y) * (y + 0.5f - center.Y)) / 6f);
                weight[spot] += near;
                side[spot] += near * Math.Clamp(dx / 20f, -1f, 1f);
            }
        }
        for (var i = 0; i < SpotLoops.Length; i++)
        {
            var volume = Math.Clamp(weight[i] / (i == 0 ? 6f : i == 1 ? 10f : 3f), 0f, 1f);
            _audio.SetSpot(SpotLoops[i], volume, weight[i] > 0f ? side[i] / weight[i] : 0f);
        }
    }

    public void Ui(string sound) => _audio.Play(sound, 0.7f);

    private static int TotalItems(GameSimulation game) => game.Player.Inventory.Slots.Sum(stack => stack.IsEmpty ? 0 : stack.Count);
}
