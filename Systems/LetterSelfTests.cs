using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The threat budget and warnings (0.6.3): a raid warning comes as a letter that counts down to it, and the raid's arrival
/// replaces it; incidents, deaths and arrivals write letters; letters are read and put away, fade, and are saved; a death
/// eases the next threats for a few days; the console shows the budget's parts.
/// </summary>
public static class LetterSelfTests
{
    private const int Floor = 40;

    /// <summary>A stand-in colony on day 10 with two bosses down (as in StorytellerKindSelfTests).</summary>
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

    private static GameSimulation Flat(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 500, height: 64) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        game.SetTimeOfDay(10f);
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static TownNpc Living(GameSimulation game, TownNpcKind kind, int x)
    {
        var npc = game.AddTownNpc(kind, new Vector2(x, Floor - 3f), bound: false);
        npc.Motion.Position = new Vector2(x, Floor - npc.Motion.Size.Y - 0.01f);
        npc.HomeCell = new GridPoint(x, Floor - 2);
        return npc;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- a raid warning is a letter with a countdown; its arrival replaces it ----
        var game = Flat(9931);
        Living(game, TownNpcKind.StationAi, 100);
        game.Player.Motion.Position = new Vector2(400f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.PlanRaid(200f, out _);
        var warning = game.Letters.SingleOrDefault(letter => letter.Kind == LetterKind.Threat);
        var countdown = warning is null ? "" : GameSimulation.Countdown(game.LetterHoursLeft(warning));
        var reloaded = GameSimulation.Restore(game.CaptureSnapshot());
        var kept = reloaded.Letters.SingleOrDefault(letter => letter.Id == warning?.Id) is { DueHour: not null } again &&
                   again.Title == warning!.Title;
        check(warning is { DueHour: not null } && warning.Title.Contains("the base") && countdown == "2:30" && kept,
            $"a raid warning comes as a letter that counts down to it ('{warning?.Title}', {countdown}), and it is saved with the run");

        var quick = Flat(9932);
        Living(quick, TownNpcKind.StationAi, 100);
        quick.Player.Motion.Position = new Vector2(400f, Floor - quick.Player.Motion.Size.Y - 0.01f);
        quick.PlanRaid(200f, out _, warningHours: 0.05f);
        for (var i = 0; i < 600 && quick.PendingRaid is not null; i++)
        {
            quick.Player.Heal(1e6f);
            quick.Tick(new GameInput(new MovementInput(0f)));
        }
        var raidLetters = quick.Letters.Where(letter => letter.Tag == "raid").ToArray();
        check(quick.PendingRaid is null && raidLetters.Length == 1 && raidLetters[0].Title.StartsWith("Raid!") && raidLetters[0].DueHour is null,
            $"when the raid arrives its warning gives way to a single 'Raid!' letter ({string.Join(", ", raidLetters.Select(letter => letter.Title))})");

        // ---- incidents and deaths write letters ----
        var colony = Flat(9933);
        colony.ForceIncident("cargo-drop");
        var cargo = colony.Letters.LastOrDefault();
        var settler = Living(colony, TownNpcKind.Settler, 60);
        colony.HurtTownNpc(settler, 1e6f);
        var death = colony.Letters.LastOrDefault();
        check(cargo is { Kind: LetterKind.Good, Title: "Cargo drop" } && death is { Kind: LetterKind.BadNews } && death.Title.Contains(settler.Name),
            $"incidents and deaths write letters, coloured by kind ('{cargo?.Title}', '{death?.Title}')");

        // ---- read and put away; the oldest go when they pile up; ordinary letters fade after a day, saved letters keep ids ----
        var pile = Flat(9934);
        for (var i = 0; i < GameSimulation.LetterCapacity + 3; i++)
            pile.SendLetter(LetterKind.Neutral, $"Note {i}", "text");
        var capped = pile.Letters.Count == GameSimulation.LetterCapacity && pile.Letters[0].Title == "Note 3";
        var dismissed = pile.DismissLetter(pile.Letters[0].Id) && pile.Letters.Count == GameSimulation.LetterCapacity - 1;
        var restored = GameSimulation.Restore(pile.CaptureSnapshot());
        var fresh = restored.SendLetter(LetterKind.Neutral, "After", "text");
        var uniqueIds = restored.Letters.Select(letter => letter.Id).Distinct().Count() == restored.Letters.Count;
        for (var i = 0; i < (int)((GameSimulation.LetterLifeHours + 1f) / GameSimulation.GameHoursPerRealSecond * 60f) && pile.Letters.Any(letter => letter.Title.StartsWith("Note")); i += 1)
        {
            pile.Player.Heal(1e6f);
            pile.Tick(new GameInput(new MovementInput(0f)));
        }
        check(capped && dismissed && uniqueIds && fresh.Id > 0 && !pile.Letters.Any(letter => letter.Title.StartsWith("Note")),
            $"letters are put away when read, the oldest go past {GameSimulation.LetterCapacity}, and unread ones fade after a day");

        // ---- a death eases the next threats, and the ease fades ----
        Storyteller Teller() => new(77) { };
        var plainTeller = Teller();
        var easedTeller = Teller();
        var threat = new ProbeThreat();
        plainTeller.Register(threat);
        easedTeller.Register(threat);
        easedTeller.NoteLoss();
        var host = new Host();
        var plainPoints = plainTeller.ForceIncident(threat.Id, host)!.ThreatPoints;
        var easedPoints = easedTeller.ForceIncident(threat.Id, host)!.ThreatPoints;
        for (var hour = 0; hour < 96; hour++)
            easedTeller.OnHour(host);
        var builder = Teller();
        builder.Configure(StorytellerKind.Builder, 1f);
        for (var i = 0; i < 10; i++)
            builder.NoteLoss();
        var saved = Teller();
        saved.NoteLoss();
        var savedAgain = Teller();
        savedAgain.Restore(saved.Capture());
        check(MathF.Abs(easedPoints / plainPoints - (1f - Storyteller.AdaptationPerDeath)) < 0.01f && easedTeller.Adaptation == 0f &&
              builder.Adaptation == Storyteller.MaximumAdaptation && savedAgain.Adaptation == saved.Adaptation &&
              colony.Storyteller.Adaptation > 0f,
            $"a death eases the next threats ({plainPoints:0} → {easedPoints:0} points), up to {Storyteller.MaximumAdaptation:P0}; the ease fades in four days and is saved");

        // ---- the console shows the threat budget and the letters ----
        var console = new DevConsole(colony);
        var budget = console.Execute("threat");
        var listed = console.Execute("letters");
        var cleared = console.Execute("letters clear");
        check(budget.Any(line => line.Contains("wealth") && line.Contains("people") && line.Contains("act")) &&
              budget.Any(line => line.Contains("eased")) && listed.Any(line => line.Contains("Cargo drop")) && colony.Letters.Count == 0 &&
              cleared.Any(line => line.Contains("put away")),
            "the console's threat shows the budget's parts and the ease; letters lists and clears them");
    }

    /// <summary>A major threat that always fires and does nothing, to read the points it was given.</summary>
    private sealed class ProbeThreat : IStoryIncident
    {
        public string Id => "probe";
        public string DisplayName => "Probe";
        public IncidentCategory Category => IncidentCategory.MajorThreat;
        public float RefireHours => 0f;
        public bool CanFire(StoryContext context, Storyteller teller) => true;
        public float ChanceWeight(StoryContext context) => 0f;
        public string? TryExecute(IncidentParms parms, Storyteller teller, IStoryHost host) => "probe";
    }
}
