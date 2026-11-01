using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>The dev console (0.3.6): every command reaches its part of the game, and bad input explains itself.</summary>
public static class DevConsoleSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var game = new GameSimulation(seed: 9660, width: 220, height: 90) { FamilySpawnsEnabled = false };
        var console = new DevConsole(game);
        string Say(string line) => string.Join(" | ", console.Execute(line));
        void Clear()
        {
            foreach (var enemy in game.Enemies.ToArray())
                enemy.TakeDamage(1e9f);
        }
        Clear();

        // ---- items, creatures, bosses, NPCs ----
        var stimsBefore = game.Player.Inventory.Count(ItemId.MedStim);
        Say("give med stim 5");
        var stimsGiven = game.Player.Inventory.Count(ItemId.MedStim) - stimsBefore; // measured now: the boss below drops stims too
        Say("give Credits 250");
        Say("spawn rockmite 3");
        var spawned = game.Enemies.Count(enemy => enemy.Kind == EnemyKind.RockMite);
        Say("defeat AlphaSkitter");
        Say("npc Medic");
        check(stimsGiven == 5 && game.Player.Inventory.Count(ItemId.Credits) >= 250 && spawned == 3 &&
              game.DefeatedBosses.Contains(EnemyKind.AlphaSkitter) && game.TownNpcs.Any(npc => npc.Kind == TownNpcKind.Medic && !npc.IsBound),
            $"the console gives items by id or name, spawns creatures, defeats a boss through its real defeat and moves an NPC in " +
            $"(stims given {stimsGiven}, credits {game.Player.Inventory.Count(ItemId.Credits)}, mites {spawned}, " +
            $"skitter down {game.DefeatedBosses.Contains(EnemyKind.AlphaSkitter)}, medic {game.TownNpcs.Any(npc => npc.Kind == TownNpcKind.Medic && !npc.IsBound)})");

        // ---- world state: time, flags, the Fracture, events, weather ----
        Say("time 22");
        Say("flag set test.counter 7");
        var flagged = game.Flags.Get("test.counter") == 7;
        Say("flag clear test.counter");
        Say("fracture");
        var started = Say("event start DefenseProtocol");
        var running = game.WorldEvents.Active.Any(state => state.Kind == WorldEventKind.DefenseProtocol);
        Say("event stop DefenseProtocol");
        Say("weather rain 0.8");
        check(Math.Abs(game.ClockHour - 22f) < 0.05f && flagged && !game.Flags.IsSet("test.counter") && game.FractureActive &&
              running && !game.WorldEvents.Active.Any(state => state.Kind == WorldEventKind.DefenseProtocol) && game.Weather.Rain >= 0.79f,
            $"the console sets the time, flags, the Fracture, events and weather ({started})");

        // ---- the player: health, god mode, statuses, teleport ----
        Say("life 3");
        var maxed = Math.Abs(game.Player.MaximumHitPoints - (100f + 3 * PlayerCharacter.LifeCoreBonus)) < 0.01f;
        Clear();
        Say("god on");
        game.Player.TakeDamage(40f, DamageKind.Blunt);
        game.Tick(new GameInput(new MovementInput(0f)));
        var refilled = game.Player.HitPoints >= game.Player.MaximumHitPoints - 0.01f;
        Say("god off");
        Say("buff Haste 2");
        var buffed = game.Player.Statuses.IsActive(StatusId.Haste);
        Say("unbuff all");
        Say("tp 60 20");
        var moved = Math.Abs(game.Player.Motion.Position.X - 60f) < 0.01f && Math.Abs(game.Player.Motion.Position.Y - 20f) < 0.01f;
        Say("tp spawn");
        check(maxed && refilled && buffed && game.Player.Statuses.Active.Count == 0 && moved && !game.Flags.IsSet(DevConsole.GodFlag),
            "the console sets life cores, god mode, statuses and teleports");

        // ---- spawning switched off, kill all, and the log ----
        Clear();
        Say("spawnrate 0");
        Say("time 23");
        for (var i = 0; i < 600; i++)
            game.Tick(new GameInput(new MovementInput(0f)));
        var quiet = game.Enemies.Count(enemy => !enemy.IsBoss && enemy.EventTag is null);
        Say("spawn rockmite 2");
        Say("killall");
        game.Tick(new GameInput(new MovementInput(0f))); // the dead are cleared on the next tick
        check(quiet == 0 && game.Enemies.Count == 0 && game.Flags.IsSet(DevConsole.UsedFlag),
            $"spawnrate 0 stops natural spawns, killall clears the world, and a run that used the console is marked ({quiet} spawned)");

        // ---- help, find, bad input ----
        var help = console.Execute("help");
        var find = Say("find med stim");
        var unknown = Say("teleport home");
        var badArgument = Say("give NoSuchThing");
        var badNumber = Say("time noon");
        check(help.Count > 15 && find.Contains("MedStim") && unknown.Contains("Unknown command") && badArgument.Contains("No item called") &&
              badNumber.Contains("Usage: time") && console.History.Count > 10 && console.Log.Count > 20,
            "help lists every command, find searches by name, and bad input explains itself with the usage");
    }
}
