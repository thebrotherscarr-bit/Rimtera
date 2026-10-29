using Rimtera.Systems;

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
        var theme = bossAlive ? "boss"
            : game.CurrentBiome.Underground && game.CurrentBiome.Zone is BiomeZone.Grassland or BiomeZone.Caverns ? "underground"
            : game.CurrentBiome.Zone switch
            {
                BiomeZone.FrozenWasteland => "frozen",
                BiomeZone.JunkWasteland => "junk",
                BiomeZone.MoltenChasm => "molten",
                BiomeZone.RadiatedDeeps => "radiated",
                BiomeZone.Overgrowth => "overgrowth",
                _ => game.CurrentBiome.DisplayName.ToLowerInvariant() switch
                {
                    var name when name.Contains("fungal") => "fungal",
                    var name when name.Contains("crystal") => "crystal",
                    var name when name.Contains("canopy") || name.Contains("verdant") => "canopy",
                    var name when name.Contains("coast") => "coast",
                    _ => "grassland"
                }
            };
        game.HeardMusicTheme = theme; theme = game.MusicThemeOverride ?? theme; // audio decks (GameSimulation.Leisure.cs)
        var tension = bossAlive ? 1f : events > 0 ? 0.7f : Math.Min(0.5f, game.Enemies.Count(enemy => enemy.IsAlive && !enemy.IsPassive) * 0.08f);
        _audio.SetMusicMood(theme, tension, game.ClockHour is >= 19f or < 5f);
    }

    public void Ui(string sound) => _audio.Play(sound, 0.7f);

    private static int TotalItems(GameSimulation game) => game.Player.Inventory.Slots.Sum(stack => stack.IsEmpty ? 0 : stack.Count);
}
