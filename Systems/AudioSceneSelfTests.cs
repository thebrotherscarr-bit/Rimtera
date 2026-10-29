using Rimtera.Entities;
using Rimtera.UI;

namespace Rimtera.Systems;

/// <summary>
/// The music player and ambience layer choose like Terraria (boss, then event, then biome by depth and time) and fall
/// back to the synth when no file fits; streamed loops loop, resample, turn mono into stereo and crossfade.
/// </summary>
public static class AudioSceneSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var specs = new WorldEventSystem(1).Specs.ToDictionary(spec => spec.Kind);
        AudioMoment Moment(string theme = "grassland", bool underground = false, bool night = false, BossTier boss = BossTier.None,
            WorldEventKind[]? events = null, float rain = 0f, float wind = 0f, float snow = 0f, bool acid = false, bool underwater = false) =>
            new(theme, underground, night, boss, (events ?? []).Select(kind => specs[kind]).ToList(), rain, wind, snow, 0f, acid, underwater);

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mus_grassland_day", "mus_underground", "mus_boss_gate", "mus_event_rust_moon", "mus_fungal",
            "amb_grassland_day", "amb_underground_day", "amb_rain_light", "amb_rain_heavy", "amb_acid_rain"
        };
        bool Has(string name) => files.Contains(name);

        var bossFirst = AudioScene.Music(Moment(boss: BossTier.Hard, events: [WorldEventKind.RustMoon]), Has);
        var eventNext = AudioScene.Music(Moment(events: [WorldEventKind.RustMoon]), Has);
        var nightFallsBack = AudioScene.Music(Moment(night: true), Has);
        var undergroundFallsBack = AudioScene.Music(Moment(underground: true), Has);
        var placeTrack = AudioScene.Music(Moment(theme: "fungal", underground: true), Has);
        var noneFits = AudioScene.Music(Moment(theme: "frozen"), Has);
        check(bossFirst == "mus_boss_gate" && eventNext == "mus_event_rust_moon" && nightFallsBack == "mus_grassland_day" &&
              undergroundFallsBack == "mus_underground" && placeTrack == "mus_fungal" && noneFits is null,
            $"music picks boss, then event, then biome (night and underground fall back), else the synth ({bossFirst}, {eventNext}, {nightFallsBack}, {undergroundFallsBack}, {placeTrack}, {noneFits ?? "synth"})");

        var rainEventUnderground = AudioScene.Music(Moment(underground: true, events: [WorldEventKind.Rain]), name => name == "mus_event_rain" || Has(name));
        check(rainEventUnderground == "mus_underground", $"weather events only score the surface ({rainEventUnderground})");

        var light = AudioScene.Ambience(Moment(rain: 0.3f), Has);
        var heavy = AudioScene.Ambience(Moment(rain: 0.9f), Has);
        var acid = AudioScene.Ambience(Moment(rain: 0.5f, acid: true), Has);
        var below = AudioScene.Ambience(Moment(underground: true, rain: 0.9f), Has);
        var calm = AudioScene.Ambience(Moment(), Has);
        check(light.Weather == "amb_rain_light" && heavy.Weather == "amb_rain_heavy" && acid.Weather == "amb_acid_rain" &&
              below is { Bed: "amb_underground_day", Weather: null } && calm is { Bed: "amb_grassland_day", Weather: null } && heavy.WeatherVolume > light.WeatherVolume * 0.9f,
            $"ambience layers a biome bed with the weather, and only the bed below ground ({light.Weather}, {heavy.Weather}, {acid.Weather}, {below.Bed})");

        // A one-second mono stream at half the mixer rate: two seconds of output must loop it, upsample it and copy it to both sides.
        var track = new StreamedTrack("test", new RampStream(22050), 44100, 0.01f) { Gain = 1f };
        var left = new float[88200];
        var right = new float[88200];
        track.Render(left, right, left.Length, 1f);
        var looped = left[44100 + 100] < left[44000] && left[44100 + 100] >= 0f;
        var mirrored = left.Zip(right).All(pair => Math.Abs(pair.First - pair.Second) < 1e-6f);
        var smooth = Enumerable.Range(1, 40000).All(i => Math.Abs(left[i] - left[i - 1]) < 0.001f);
        check(looped && mirrored && smooth && !track.Finished, "streamed loops repeat at the end, resample smoothly and play mono on both sides");

        track.Target = 0f;
        track.Render(left, right, 4410, 1f);
        check(track.Finished, "a streamed loop fades out and is released");
    }

    /// <summary>A rising ramp from 0 to 1 over one second, mono.</summary>
    private sealed class RampStream(int rate) : IAudioStream
    {
        private int _position;
        public int Channels => 1;
        public int SampleRate => rate;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = Math.Min(count, rate - _position);
            for (var i = 0; i < read; i++)
                buffer[offset + i] = (_position + i) / (float)rate;
            _position += read;
            return read;
        }

        public void Rewind() => _position = 0;
        public void Dispose() { }
    }
}
