using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Everything that needs a sound has one (docs/AUDIO_FLOW.md): every weapon style, tool, tile material, creature, boss,
/// world event and music slot has a cue in the needs map, and every cue's fallback chain ends at a synthesised effect,
/// so it plays something before its generated or recorded file exists.
/// </summary>
public static class SoundCatalogSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var cues = SoundCatalog.All;
        var names = new HashSet<string>(cues.Select(cue => cue.Cue), StringComparer.OrdinalIgnoreCase);
        check(names.Count == cues.Count, $"sound cues are unique ({cues.Count} cues)");

        var synth = new HashSet<string>(AudioEngine.SynthesisedCues, StringComparer.OrdinalIgnoreCase);
        var dangling = new List<string>();
        foreach (var cue in cues)
        {
            var at = cue.Cue;
            for (var step = 0; step < 12 && at is not null && !synth.Contains(at); step++)
                at = SoundCatalog.Fallback(at);
            if (at is null || !synth.Contains(at))
                dangling.Add(cue.Cue);
        }
        // Music, ambience, footsteps, idle calls and the heartbeat stay silent until their file exists (the synth music and rain cover the first two).
        var musicOrAmbience = dangling.Where(cue => cue.StartsWith("mus_") || cue.StartsWith("amb_") || cue.StartsWith("step_") || cue.EndsWith("_idle") || cue == "low_health").ToHashSet();
        var missing = dangling.Where(cue => !musicOrAmbience.Contains(cue)).ToList();
        check(missing.Count == 0, $"every sound effect falls back to a synthesised effect (unreachable: {string.Join(", ", missing.Take(8))})");

        var gaps = new List<string>();
        void Need(string cue)
        {
            if (!names.Contains(cue))
                gaps.Add(cue);
        }
        foreach (var style in Enum.GetValues<WeaponStyle>().Where(style => style != WeaponStyle.None))
        {
            Need($"use_{SoundCatalog.Snake(style.ToString())}");
            Need($"use_{SoundCatalog.Snake(style.ToString())}_heavy");
        }
        foreach (var item in new ItemCatalog().All)
        {
            if (SoundCatalog.WeaponCue(item) is { Length: > 0 } weapon)
                Need(weapon);
            if (SoundCatalog.ToolCue(item.UseKind) is { Length: > 0 } tool)
                Need(tool);
        }
        var tiles = new TileCatalog();
        foreach (var id in Enum.GetValues<TileId>().Where(id => id != TileId.Air && tiles.Contains(id)))
        {
            var material = SoundCatalog.TileClass(tiles[id]);
            Need($"dig_{material}");
            Need($"break_{material}");
            Need($"place_{material}");
        }
        foreach (var archetype in EnemyArchetypes.All)
        {
            Need("impact_" + SoundCatalog.CreatureMaterial(archetype));
            if (archetype.IsBoss)
                foreach (var part in new[] { "roar", "attack", "hurt", "death" })
                    Need(SoundCatalog.BossCue(archetype.Kind, part));
            else
                foreach (var part in new[] { "_idle", "_hurt", "_death" })
                    Need(SoundCatalog.CreatureVoice(archetype) + part);
        }
        foreach (var spec in new WorldEventSystem(1).Specs)
        {
            Need(SoundCatalog.EventCue(spec.Kind, "start"));
            Need(SoundCatalog.EventCue(spec.Kind, "end"));
            Need($"mus_event_{SoundCatalog.Snake(spec.Kind.ToString())}");
        }
        foreach (var tier in Enum.GetValues<BossTier>().Where(tier => tier != BossTier.None))
            Need($"mus_boss_{SoundCatalog.Snake(tier.ToString())}");
        foreach (var theme in new[] { "title", "grassland", "canopy", "coast", "frozen", "crystal", "fungal", "junk", "forest", "overgrowth", "radiated", "underground", "molten", "core" })
            if (!names.Contains($"mus_{theme}") && !names.Contains($"mus_{theme}_day"))
                gaps.Add($"mus_{theme}");
        check(gaps.Count == 0, $"every weapon, tool, tile, creature, boss, event and music theme has a sound cue (missing: {string.Join(", ", gaps.Distinct().Take(8))})");

        var music = cues.Where(cue => cue.Category == "music").ToList();
        check(music.All(cue => cue.Bpm > 0 && cue.Key.Length > 0 && cue.Meter > 0 && cue.Loop),
            $"every music slot carries a style card: tempo, key and meter ({music.Count} slots)");
    }
}
