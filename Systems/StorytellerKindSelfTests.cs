namespace Rimtera.Systems;

/// <summary>Storytellers to pick (0.6.1): personalities change pace, mix and strength; Steady is unchanged; the dial scales threats; saved.</summary>
public static class StorytellerKindSelfTests
{
    /// <summary>A stand-in colony on day 10 with two bosses down, where every incident can fire.</summary>
    private sealed class Host : IStoryHost
    {
        public StoryContext CaptureStoryContext() => new(10, 12f, BiomeZone.Grassland, 3, 2, 400f, false, false, 1f);
        public int SpawnRaidWave(BiomeZone biome, float threatPoints, out string description)
        {
            description = $"a raid of {threatPoints:0}";
            return 3;
        }
        public string DeliverCargo(BiomeZone biome, float threatPoints) => "supplies";
        public bool TryAddWanderer(int wandererIndex, out string name) { name = "x"; return false; }
        public bool TryWelcomeArrival(out string summary, out bool joinedCrew) { summary = ""; joinedCrew = false; return false; }
        public void Announce(string message) { }
    }

    private static List<StoryIncidentRecord> Simulate(StorytellerKind? kind, float scale = 1f, int hours = 600)
    {
        var teller = new Storyteller(4242);
        if (kind is { } chosen)
            teller.Configure(chosen, scale);
        var host = new Host();
        var records = new List<StoryIncidentRecord>();
        for (var hour = 0; hour < hours; hour++)
            if (teller.OnHour(host) is { } record)
                records.Add(record);
        return records;
    }

    private static float MajorShare(List<StoryIncidentRecord> records, Storyteller teller) =>
        records.Count == 0 ? 0f : records.Count(record => teller.Incidents.First(incident => incident.Id == record.IncidentId).Category == IncidentCategory.MajorThreat) / (float)records.Count;

    public static void Run(Action<bool, string> check)
    {
        var reference = new Storyteller(1);
        var plain = Simulate(null);
        var steady = Simulate(StorytellerKind.Steady);
        var chaotic = Simulate(StorytellerKind.Chaotic);
        var builder = Simulate(StorytellerKind.Builder);
        var unchanged = plain.Select(record => (record.IncidentId, record.Day, record.Summary)).SequenceEqual(steady.Select(record => (record.IncidentId, record.Day, record.Summary)));
        check(unchanged && chaotic.Count > steady.Count && steady.Count > builder.Count &&
              MajorShare(chaotic, reference) > MajorShare(builder, reference),
            $"Steady paces the run as before; Chaotic brings more ({chaotic.Count}) and more major threats ({MajorShare(chaotic, reference):P0}), " +
            $"Builder fewer ({builder.Count}, {MajorShare(builder, reference):P0} major) than Steady ({steady.Count}) over 600 hours");

        var gentle = Simulate(StorytellerKind.Steady, 0.5f).Where(record => record.IncidentId == "raid").Select(record => record.ThreatPoints).DefaultIfEmpty(0f).Average();
        var hard = Simulate(StorytellerKind.Steady, 2f).Where(record => record.IncidentId == "raid").Select(record => record.ThreatPoints).DefaultIfEmpty(0f).Average();
        var teller = new Storyteller(7);
        teller.Configure(StorytellerKind.Builder, 1.5f);
        var restored = new Storyteller(8);
        restored.Restore(teller.Capture());
        var clamped = new Storyteller(9);
        clamped.Configure(StorytellerKind.Chaotic, 9f);
        check(gentle > 0f && hard > gentle * 3f && restored.Kind == StorytellerKind.Builder && restored.ThreatScale == 1.5f &&
              clamped.ThreatScale == Storyteller.MaximumThreatScale && new Storyteller(1).Kind == StorytellerKind.Steady,
            $"the threat dial scales raids (×0.5: {gentle:0} points, ×2: {hard:0}); the storyteller and its dial are saved; the dial stays within ×0.5–×2");
    }
}
