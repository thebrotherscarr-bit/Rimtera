namespace Rimtera.Systems;

/// <summary>
/// One of Terraria's invasions (the scavengers, the pirates, the precursor probes), sent by the storyteller with a warning
/// instead of a dusk roll of its own (0.6.4). The host picks which one the colony's progress allows.
/// </summary>
public sealed class InvasionIncident : IStoryIncident
{
    public string Id => "invasion";
    public string DisplayName => "Invasion";
    public IncidentCategory Category => IncidentCategory.MajorThreat;
    public float RefireHours => 72f;
    public bool CanFire(StoryContext context, Storyteller teller) => context.InvasionReady && !context.BossActive;
    public float ChanceWeight(StoryContext context) => 0.8f;
    public string? TryExecute(IncidentParms parms, Storyteller teller, IStoryHost host) => host.ScheduleInvasion(parms.ThreatPoints);
}

/// <summary>Rust fever: someone falls sick for a day or two, slower at everything until it passes (0.6.4).</summary>
public sealed class SicknessIncident : IStoryIncident
{
    public string Id => "sickness";
    public string DisplayName => "Rust fever";
    public IncidentCategory Category => IncidentCategory.MinorThreat;
    public float RefireHours => 72f;
    public bool CanFire(StoryContext context, Storyteller teller) => context.Day >= Storyteller.FirstWeatherDay && context.ColonistCount >= 2;
    public float ChanceWeight(StoryContext context) => 0.8f;
    public string? TryExecute(IncidentParms parms, Storyteller teller, IStoryHost host) => host.StrikeSickness(parms.ThreatPoints);
}

/// <summary>A power outage: a surge drains part of the station AI's reserve (0.6.4).</summary>
public sealed class PowerOutageIncident : IStoryIncident
{
    public string Id => "power-outage";
    public string DisplayName => "Power outage";
    public IncidentCategory Category => IncidentCategory.MinorThreat;
    public float RefireHours => 60f;
    public bool CanFire(StoryContext context, Storyteller teller) => context.PowerOnline;
    public float ChanceWeight(StoryContext context) => 0.7f;
    public string? TryExecute(IncidentParms parms, Storyteller teller, IStoryHost host) => host.CutPower(parms.ThreatPoints);
}

/// <summary>A trader passing through: the travelling merchant comes today, whatever the day (0.6.4).</summary>
public sealed class TraderIncident : IStoryIncident
{
    public string Id => "trader";
    public string DisplayName => "Trader";
    public IncidentCategory Category => IncidentCategory.Boon;
    public float RefireHours => 72f;
    public bool CanFire(StoryContext context, Storyteller teller) => context.TraderCanCome;
    public float ChanceWeight(StoryContext context) => 0.8f;
    public string? TryExecute(IncidentParms parms, Storyteller teller, IStoryHost host) => host.CallTrader();
}
