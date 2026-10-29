using Rimtera.Entities;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>What the player is hearing about: everything the music and ambience choices depend on.</summary>
internal readonly record struct AudioMoment(
    string Theme,
    bool Underground,
    bool Night,
    BossTier Boss,
    IReadOnlyList<WorldEventSpec> Events,
    float Rain,
    float Wind,
    float Snow,
    float Dust,
    bool AcidRain,
    bool Underwater);

/// <summary>The ambience layers to play: a biome bed, a weather layer and how loud the weather is.</summary>
internal readonly record struct AmbienceChoice(string? Bed, string? Weather, float WeatherVolume);

/// <summary>
/// Chooses music and ambience the way Terraria does: a boss's music wins, then an active event's, then the biome's
/// (its underground or night version first), and a slot without a file walks its catalog fallback (night → day). Null
/// means no file fits and the procedural synth plays instead (AudioEngine.MusicState for music, the rain noise for rain).
/// </summary>
internal static class AudioScene
{
    /// <summary>Biomes whose music and ambience only exist below ground (they have no day or night version).</summary>
    private static readonly HashSet<string> UndergroundThemes = ["fungal", "hives", "warrens", "vault", "underground", "molten", "core"];

    /// <summary>Which events win when several run at once: invasions, then night events, then the rest; weather last.</summary>
    private static int EventRank(WorldEventCategory category) => category switch
    {
        WorldEventCategory.Invasion => 0,
        WorldEventCategory.Night => 1,
        WorldEventCategory.Day => 2,
        WorldEventCategory.World => 3,
        WorldEventCategory.Party => 4,
        WorldEventCategory.Calm => 5,
        _ => 6
    };

    public static string? Music(AudioMoment moment, Func<string, bool> exists)
    {
        if (moment.Boss != BossTier.None && Resolve($"mus_boss_{SoundCatalog.Snake(moment.Boss.ToString())}", exists) is { } boss)
            return boss;
        // Weather events (rain, storms) only score the surface; everything else plays wherever you are.
        foreach (var spec in moment.Events.OrderBy(spec => EventRank(spec.Category)))
            if ((spec.Category != WorldEventCategory.Weather || !moment.Underground) &&
                Resolve($"mus_event_{SoundCatalog.Snake(spec.Kind.ToString())}", exists) is { } eventTrack)
                return eventTrack;
        if (moment.Underwater && Resolve("mus_ocean", exists) is { } ocean)
            return ocean;
        var theme = moment.Theme;
        if (UndergroundThemes.Contains(theme) || theme is "title" or "space" or "town" or "ocean")
            return Resolve($"mus_{theme}", exists);
        if (moment.Underground)
            return Resolve($"mus_{theme}_underground", exists) ?? Resolve("mus_underground", exists);
        return moment.Night ? Resolve($"mus_{theme}_night", exists) : Resolve($"mus_{theme}_day", exists);
    }

    public static AmbienceChoice Ambience(AudioMoment moment, Func<string, bool> exists)
    {
        string? bed;
        if (moment.Underwater)
            bed = Resolve("amb_underwater", exists);
        else if (UndergroundThemes.Contains(moment.Theme))
            bed = Resolve($"amb_{moment.Theme}_day", exists) ?? Resolve("amb_underground_day", exists);
        else if (moment.Underground)
            bed = Resolve("amb_underground_day", exists);
        else
            bed = Resolve($"amb_{moment.Theme}_{(moment.Night ? "night" : "day")}", exists);

        if (moment.Underground || moment.Underwater)
            return new(bed, null, 0f);
        var (weather, level) =
            moment.AcidRain && moment.Rain > 0.05f ? ("amb_acid_rain", moment.Rain) :
            moment.Snow > 0.4f && moment.Wind > 0.45f ? ("amb_blizzard", Math.Max(moment.Snow, moment.Wind)) :
            moment.Dust > 0.35f ? ("amb_sandstorm", moment.Dust) :
            moment.Rain > 0.55f ? ("amb_rain_heavy", moment.Rain) :
            moment.Rain > 0.05f ? ("amb_rain_light", moment.Rain * 1.5f) :
            moment.Wind > 0.55f ? ("amb_wind", moment.Wind) :
            ((string?)null, 0f);
        return new(bed, weather is null ? null : Resolve(weather, exists), Math.Clamp(level, 0f, 1f));
    }

    /// <summary>The first cue along <paramref name="slot"/>'s fallback chain that has a file.</summary>
    public static string? Resolve(string slot, Func<string, bool> exists)
    {
        for (var (cue, steps) = ((string?)slot, 0); cue is not null && steps < 8; cue = SoundCatalog.Fallback(cue), steps++)
            if (exists(cue))
                return cue;
        return null;
    }
}
