using System.Numerics;

namespace Rimtera.Systems;

/// <summary>
/// Persisted weather (FISHING_EVENTS_WEATHER.md §3.3). Every value is 0–1 except <see cref="Wind"/>
/// (signed tiles/s). Current values ease toward the targets rolled on the time boundaries.
/// </summary>
public sealed record WeatherState(
    float CloudCover = 0.2f,
    float Rain = 0f,
    float Wind = 0f,
    bool Storm = false,
    float Dust = 0f,
    float Fog = 0f,
    float Snow = 0f,
    bool AcidRain = false,
    float TargetCloudCover = 0.2f,
    float TargetRain = 0f,
    float TargetWind = 0f,
    float TargetDust = 0f,
    float TargetFog = 0f,
    float TargetSnow = 0f,
    int LastRollDay = 0,
    int LastRollSlot = -1);

/// <summary>A lightning strike for the renderer (flash at <see cref="Position"/>) and gameplay.</summary>
public readonly record struct LightningStrike(Vector2 Position, float AgeSeconds);

/// <summary>Climate-driven weather rolls and easing. Rolls are a pure function of seed, day and slot (deterministic).</summary>
public sealed class WeatherSystem
{
    /// <summary>Hours at which new targets are rolled (dawn, noon, dusk, midnight).</summary>
    public static readonly float[] RollHours = [6f, 12f, 18f, 0f];

    private readonly int _seed;

    public WeatherSystem(int seed) => _seed = seed;

    public WeatherState State { get; set; } = new();

    /// <summary>Deterministic 0–1 value for (day, slot, salt).</summary>
    public float Roll(int day, int slot, int salt)
    {
        unchecked
        {
            var hash = (uint)_seed * 2654435761u ^ (uint)day * 40503u ^ (uint)slot * 2246822519u ^ (uint)salt * 3266489917u;
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            hash *= 3266489917u;
            hash ^= hash >> 16;
            return (hash & 0xFFFFFF) / (float)0x1000000;
        }
    }

    /// <summary>Rolls new targets for a time slot from the biome climate.</summary>
    public void RollTargets(int day, int slot, BiomeZone zone, bool fractured)
    {
        var state = State;
        float R(int salt) => Roll(day, slot, salt);
        var rainChance = zone switch
        {
            BiomeZone.VerdantCanopy or BiomeZone.BloomHives => 0.55f,
            BiomeZone.AcidCoast => 0.4f,
            BiomeZone.JunkWasteland => 0.12f,
            BiomeZone.FrozenWasteland => 0.35f,
            BiomeZone.MoltenChasm => 0f,
            _ => 0.28f
        };
        var cloud = Math.Clamp(R(1) * 0.8f + (R(2) < rainChance ? 0.4f : 0f), 0f, 1f);
        var precipitation = R(2) < rainChance ? 0.3f + R(3) * 0.7f : 0f;
        var snowy = zone == BiomeZone.FrozenWasteland;
        var dust = zone == BiomeZone.JunkWasteland && R(4) < 0.3f ? 0.4f + R(5) * 0.6f : 0f;
        var fog = slot == 0 && R(6) < 0.3f ? 0.3f + R(7) * 0.5f : R(6) < 0.08f ? 0.3f : 0f;
        var wind = (R(8) * 2f - 1f) * (dust > 0f ? 6f : 3f);
        var storm = precipitation > 0.75f && R(9) < 0.5f;
        State = state with
        {
            TargetCloudCover = cloud,
            TargetRain = snowy ? 0f : precipitation,
            TargetSnow = snowy ? precipitation : 0f,
            TargetDust = dust,
            TargetFog = fog,
            TargetWind = wind,
            Storm = storm,
            AcidRain = !snowy && precipitation > 0f && (zone is BiomeZone.AcidCoast or BiomeZone.RadiatedDeeps ||
                                                         (fractured && R(10) < 0.25f)),
            LastRollDay = day,
            LastRollSlot = slot
        };
    }

    /// <summary>Forces targets (weather events, commands, tests).</summary>
    public void Force(float? rain = null, float? snow = null, float? dust = null, float? fog = null, float? wind = null,
        bool? storm = null, bool? acid = null, bool immediate = false)
    {
        var state = State with
        {
            TargetRain = rain ?? State.TargetRain,
            TargetSnow = snow ?? State.TargetSnow,
            TargetDust = dust ?? State.TargetDust,
            TargetFog = fog ?? State.TargetFog,
            TargetWind = wind ?? State.TargetWind,
            TargetCloudCover = Math.Max(State.TargetCloudCover, Math.Max(rain ?? 0f, snow ?? 0f)),
            Storm = storm ?? State.Storm,
            AcidRain = acid ?? State.AcidRain
        };
        if (immediate)
            state = state with
            {
                Rain = state.TargetRain, Snow = state.TargetSnow, Dust = state.TargetDust, Fog = state.TargetFog,
                Wind = state.TargetWind, CloudCover = state.TargetCloudCover
            };
        State = state;
    }

    /// <summary>Eases current values toward targets over <paramref name="hours"/>.</summary>
    public void Ease(float hours)
    {
        if (hours <= 0f)
            return;
        var t = Math.Clamp(hours * 0.9f, 0f, 1f);
        float Lerp(float a, float b) => a + (b - a) * t;
        var state = State;
        State = state with
        {
            CloudCover = Lerp(state.CloudCover, state.TargetCloudCover),
            Rain = Lerp(state.Rain, state.TargetRain),
            Snow = Lerp(state.Snow, state.TargetSnow),
            Dust = Lerp(state.Dust, state.TargetDust),
            Fog = Lerp(state.Fog, state.TargetFog),
            Wind = Lerp(state.Wind, state.TargetWind)
        };
    }
}
