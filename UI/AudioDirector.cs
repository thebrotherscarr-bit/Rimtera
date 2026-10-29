using Rimtera.Entities;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>
/// Watches the simulation once per frame and turns changes into sound: hits, deaths, hurt, landing,
/// tool and weapon use, pickups, crafting, lightning, bites, boss arrivals and events starting.
/// Also chooses the music theme and tension from biome, depth, bosses and events.
/// </summary>
internal sealed class AudioDirector
{
    private readonly AudioEngine _audio;
    private GameSimulation? _game;
    private readonly Dictionary<long, float> _enemyHp = new();
    private float _playerHp;
    private bool _grounded = true;
    private int _cooldown;
    private int _itemTotal;
    private int _lightningSeen;
    private bool _bossAlive;
    private readonly HashSet<WorldEventKind> _eventKinds = new();
    private int _frame;
    private static readonly Dictionary<WorldEventKind, WorldEventSpec> EventSpecs = new WorldEventSystem(1).Specs.ToDictionary(spec => spec.Kind);
    private static readonly string[] SpotLoops = ["amb_waterfall", "amb_uranium_flow", "amb_machinery", "amb_fire"];
    private bool _biting;

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
            _itemTotal = TotalItems(game);
            _lightningSeen = game.LightningStrikes.Count;
        }

        var player = game.Player;
        var center = player.Motion.Position;
        float Pan(System.Numerics.Vector2 at) => Math.Clamp((at.X - center.X) / 30f, -1f, 1f);
        float Near(System.Numerics.Vector2 at) => Math.Clamp(1f - System.Numerics.Vector2.Distance(at, center) / 45f, 0f, 1f);

        var seen = new HashSet<long>();
        foreach (var enemy in game.Enemies)
        {
            seen.Add(enemy.Id);
            if (_enemyHp.TryGetValue(enemy.Id, out var hp) && enemy.HitPoints < hp - 0.01f)
            {
                // The weapon lands on the creature's body (impact_metal, impact_slime …), then the creature answers.
                var volume = 0.8f * Near(enemy.Motion.Position) + 0.2f;
                var pan = Pan(enemy.Motion.Position);
                _audio.Play("impact_" + SoundCatalog.CreatureMaterial(enemy.Archetype), volume * 0.8f, pan);
                var voice = enemy.IsBoss ? SoundCatalog.BossCue(enemy.Kind, enemy.IsAlive ? "hurt" : "death")
                    : SoundCatalog.CreatureVoice(enemy.Archetype) + (enemy.IsAlive ? "_hurt" : "_death");
                _audio.Play(voice, volume, pan);
            }
            _enemyHp[enemy.Id] = enemy.IsAlive ? enemy.HitPoints : 0f;
        }
        foreach (var id in _enemyHp.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            // A creature removed before its fatal hit was seen (one-shot kills) still gets the generic death.
            if (_enemyHp[id] > 0f)
                _audio.Play("death", 0.6f);
            _enemyHp.Remove(id);
        }

        if (player.HitPoints < _playerHp - 0.5f)
            _audio.Play("hurt");
        _playerHp = player.HitPoints;

        if (player.Motion.Grounded && !_grounded)
            _audio.Play("land", 0.6f);
        else if (!player.Motion.Grounded && _grounded && player.Motion.Velocity.Y < -1f)
            _audio.Play("jump", 0.7f);
        _grounded = player.Motion.Grounded;

        if (player.ActionCooldownTicks > _cooldown)
        {
            var item = player.Inventory.SelectedItem;
            var sound = item is null ? "" : SoundCatalog.WeaponCue(item);
            if (sound.Length == 0 && item is not null)
                sound = item.UseKind switch
                {
                    ItemUseKind.Melee => "swing",
                    ItemUseKind.Ranged or ItemUseKind.Tech => item.EffectiveClass == DamageClass.Tech ? "zap" : "shoot",
                    ItemUseKind.Command => "use_summon_staff",
                    _ => SoundCatalog.ToolCue(item.UseKind)
                };
            if (sound.Length > 0)
                _audio.Play(sound, 0.8f);
        }
        _cooldown = player.ActionCooldownTicks;

        var total = TotalItems(game);
        if (total > _itemTotal)
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
