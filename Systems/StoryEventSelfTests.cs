using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The event catalogue (0.6.4): Terraria's invasions come from the storyteller with a warning and a countdown, not a dusk
/// roll; events that matter write a letter when they begin; rust fever slows someone for a day or two, sooner with a
/// doctor; a power outage drains the station AI; a trader can be sent by day; all of it is saved and on the console.
/// </summary>
public static class StoryEventSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Flat(int seed, float hour = 10f)
    {
        var game = new GameSimulation(seed: seed, width: 500, height: 64) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        game.SetTimeOfDay(hour);
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

    private static void Tick(GameSimulation game, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            game.Player.Heal(1e6f);
            game.Tick(new GameInput(new MovementInput(0f)));
        }
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- invasions: the storyteller sends them, with a warning that counts down; then they begin ----
        var game = Flat(9941);
        Living(game, TownNpcKind.StationAi, 100);
        Living(game, TownNpcKind.Quartermaster, 106);
        game.Player.Motion.Position = new Vector2(110f, Floor - game.Player.Motion.Size.Y - 0.01f);
        var locked = game.InvasionsReady.Any();
        game.Flags.Increment("hearts.broken");
        var warning = game.ScheduleInvasion(warningHours: 0.05f);
        var letter = game.Letters.LastOrDefault();
        var twice = game.ScheduleInvasion();
        var saved = GameSimulation.Restore(game.CaptureSnapshot());
        check(!locked && warning is not null && game.PendingInvasion == WorldEventKind.ScavengerRaid && letter is { Kind: LetterKind.Threat, DueHour: not null } &&
              twice is null && saved.PendingInvasion == WorldEventKind.ScavengerRaid && game.WorldEvents.InvasionsByStoryteller &&
              !new WorldEventSystem(1).InvasionsByStoryteller,
            $"invasions wait for progress, then the storyteller sends them with a warning letter that counts down, not a dusk roll ('{warning}'); the plan is saved");

        for (var i = 0; i < 900 && game.PendingInvasion is not null; i++)
            Tick(game, 1);
        var begun = game.Letters.Where(entry => entry.Title == "Scavenger Raid").ToArray();
        check(game.PendingInvasion is null && game.WorldEvents.IsActive(WorldEventKind.ScavengerRaid) &&
              begun.Length == 1 && begun[0].DueHour is null && game.Letters.All(entry => !entry.Title.EndsWith("coming")),
            "when its hour comes the invasion begins, and its warning gives way to a letter saying so");

        // ---- events that matter write a letter when they begin; weather does not ----
        var events = Flat(9942, hour: 21f);
        events.Flags.Set("boss.stone_sentinel.down");
        var moon = events.WorldEvents.TryStart(WorldEventKind.RustMoon, events, forced: true, out _);
        var rain = events.WorldEvents.TryStart(WorldEventKind.Rain, events, forced: true, out _);
        check(moon && rain && events.Letters.Count(entry => entry.Title == "Rust Moon" && entry.Kind == LetterKind.BadNews) == 1 &&
              events.Letters.All(entry => entry.Title != events.WorldEvents.Spec(WorldEventKind.Rain).Name),
            $"an event that matters writes a letter when it begins (the Rust Moon); the weather does not ({events.Letters.Count} letters)");

        // ---- rust fever: slower for a day or two, sooner with a doctor; saved; on their card ----
        var colony = Flat(9943);
        var patient = Living(colony, TownNpcKind.Cook, 60);
        Living(colony, TownNpcKind.Quartermaster, 70);
        var host = (IStoryHost)colony;
        var struck = host.StrikeSickness(100f);
        var sick = colony.TownNpcs.Single(npc => npc.IsSick);
        var lasting = sick.SickHours;
        var card = colony.Inspect(new InspectTarget(InspectKind.Resident, sick.Id))!.Lines.Any(line => line.Contains("rust fever"));
        var reloaded = GameSimulation.Restore(colony.CaptureSnapshot());
        var keptSick = reloaded.TownNpcs.Single(npc => npc.Name == sick.Name).SickHours;
        check(struck is not null && lasting is >= GameSimulation.ShortestSicknessHours and <= GameSimulation.LongestSicknessHours &&
              GameSimulation.SicknessFactor(sick) == GameSimulation.SickFactor && card && MathF.Abs(keptSick - lasting) < 0.01f,
            $"rust fever slows someone to {GameSimulation.SickFactor:P0} for {lasting:0} hours; it shows on their card and is saved ('{struck}')");

        float RecoveredAfter(bool doctor)
        {
            var world = Flat(9944);
            var ill = Living(world, TownNpcKind.Cook, 60);
            if (doctor)
                Living(world, TownNpcKind.Medic, 70).Stats.SkillLevels[(int)Rimtera.Characters.ColonistSkill.Medical] = 10;
            world.MakeSick(ill, 30f);
            Tick(world, 60 * 60);
            return 30f - ill.SickHours;
        }
        var alone = RecoveredAfter(false);
        var tended = RecoveredAfter(true);
        var brief = Flat(9945);
        var shortIll = Living(brief, TownNpcKind.Cook, 60);
        brief.MakeSick(shortIll, 0.1f);
        Tick(brief, 60 * 30);
        check(tended > alone * 1.8f && !shortIll.IsSick && GameSimulation.SicknessFactor(shortIll) == 1f,
            $"a doctor (Medical {GameSimulation.DoctorMedicalLevel}) sees the sick through twice as fast ({alone:0.0} → {tended:0.0} hours a minute), and it passes");

        // ---- power outages drain the station AI; nothing to take when it is down ----
        var powered = Flat(9946);
        Living(powered, TownNpcKind.StationAi, 100);
        var before = powered.StationPower;
        var outage = ((IStoryHost)powered).CutPower(200f);
        var after = powered.StationPower;
        var dark = Flat(9947);
        var noAi = ((IStoryHost)dark).CutPower(200f);
        check(outage is not null && after < before && after >= before * 0.4f && noAi is null,
            $"a power outage drains part of the station AI's reserve ({before} → {after}); with no AI there is nothing to drain");

        // ---- a trader can be sent by day ----
        var market = Flat(9948, hour: 9f);
        Living(market, TownNpcKind.StationAi, 100);
        Living(market, TownNpcKind.Quartermaster, 106);
        var trader = ((IStoryHost)market).CallTrader();
        var again = ((IStoryHost)market).CallTrader();
        var night = Flat(9949, hour: 22f);
        Living(night, TownNpcKind.StationAi, 100);
        Living(night, TownNpcKind.Quartermaster, 106);
        check(trader is not null && market.TownNpcs.Count(npc => npc.Kind == TownNpcKind.TravellingMerchant) == 1 && again is null &&
              ((IStoryHost)night).CallTrader() is null,
            $"a trader can be sent by day, one at a time, never by night ('{trader}')");

        // ---- the storyteller holds them all; the console fires them ----
        var ids = new Storyteller(1).Incidents.Select(incident => incident.Id).ToHashSet();
        var console = new DevConsole(market);
        var listed = console.Execute("incident");
        check(new[] { "invasion", "sickness", "power-outage", "trader", "cargo-drop", "raid" }.All(ids.Contains) &&
              listed.Any(line => line.Contains("sickness") && line.Contains("trader")),
            "the storyteller's catalogue holds invasions, sickness, power outages, traders, supply drops and raids; the console lists and fires them");
    }
}
