using System.Globalization;
using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The dev console (0.3.6, docs/VISION.md §6): one line of text runs one command against a live game, so any state,
/// event or spawn can be reached in seconds, by the owner in the game (the ` key) and by the self-tests. Every later
/// mechanic adds its commands here. State-changing commands set the <c>dev.console.used</c> flag, so a run that used
/// it is marked as such.
/// </summary>
public sealed class DevConsole
{
    public const string GodFlag = "dev.godmode", SpawnRateFlag = "dev.spawn_rate", UsedFlag = "dev.console.used";
    private const int LogLimit = 200, HistoryLimit = 50;

    private readonly GameSimulation _game;
    private readonly List<string> _log = new();
    private readonly List<string> _history = new();

    public DevConsole(GameSimulation game) => _game = game ?? throw new ArgumentNullException(nameof(game));

    /// <summary>Everything the console has printed, oldest first.</summary>
    public IReadOnlyList<string> Log => _log;
    /// <summary>The commands entered, oldest first (the up and down keys walk it).</summary>
    public IReadOnlyList<string> History => _history;

    private sealed record Command(string Name, string Usage, string Help, bool ChangesState, Func<string[], IEnumerable<string>> Run);

    private IReadOnlyList<Command>? _commands;
    private IReadOnlyList<Command> Commands => _commands ??= BuildCommands();

    /// <summary>Runs one line and returns what it printed (also appended to <see cref="Log"/>).</summary>
    public IReadOnlyList<string> Execute(string line)
    {
        var text = (line ?? "").Trim();
        if (text.Length == 0)
            return [];
        if (_history.Count == 0 || _history[^1] != text)
            _history.Add(text);
        if (_history.Count > HistoryLimit)
            _history.RemoveAt(0);
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();
        List<string> output = [$"> {text}"];
        if (Commands.FirstOrDefault(command => command.Name == name) is not { } found)
            output.Add($"Unknown command '{name}'. Type help for the list.");
        else
        {
            try
            {
                output.AddRange(found.Run(args));
                if (found.ChangesState)
                    _game.Flags.Set(UsedFlag);
            }
            catch (ConsoleError error)
            {
                output.Add(error.Message);
                output.Add($"Usage: {found.Usage}");
            }
        }
        _log.AddRange(output);
        if (_log.Count > LogLimit)
            _log.RemoveRange(0, _log.Count - LogLimit);
        return output;
    }

    private sealed class ConsoleError(string message) : Exception(message);

    // ------------------------------------------------------------------ commands

    private IReadOnlyList<Command> BuildCommands() =>
    [
        new("help", "help [command]", "Lists the commands, or explains one.", false, Help),
        new("find", "find <text>", "Searches items, creatures, town NPCs, events and statuses by name.", false, Find),
        new("give", "give <item> [count]", "Puts items in your inventory (credits count as an item).", true, Give),
        new("spawn", "spawn <creature> [count]", "Spawns creatures a few tiles from you.", true, Spawn),
        new("defeat", "defeat <boss>", "Spawns a boss and defeats it at once: its drops, flags and unlocks all happen.", true, Defeat),
        new("npc", "npc <kind>", "A town NPC moves in beside you (not bound).", true, Npc),
        new("killall", "killall", "Removes every creature in the world (bosses too).", true, _ => KillAll()),
        new("fracture", "fracture", "Starts the Fracture (hardmode).", true, _ => [_game.TriggerFracture() ? "The world fractures." : "The Fracture has already happened."]),
        new("event", "event start|stop <kind>, or event list", "Starts or stops a world event, or lists the running ones.", true, Event),
        new("time", "time <hour 0-24>", "Sets the time of day.", true, Time),
        new("flag", "flag get|set|clear <name> [value], or flag list [text]", "Reads or writes a world flag.", true, Flag),
        new("heal", "heal", "Restores your health to full.", true, _ => Heal()),
        new("life", "life <cores>", "Sets how many life cores you have used (max health).", true, Life),
        new("god", "god on|off", "God mode: your health refills every tick, in any world.", true, God),
        new("buff", "buff <status> [minutes]", "Gives you a status (default 5 minutes).", true, Buff),
        new("unbuff", "unbuff <status>|all", "Removes a status, or all of them.", true, Unbuff),
        new("tp", "tp <x> <y> | tp spawn | tp core", "Moves you to a tile, the spawn point, or the Irradiated Core.", true, Teleport),
        new("weather", "weather clear|rain|storm|snow|dust|fog [0-1]", "Forces the weather.", true, Weather),
        new("spawnrate", "spawnrate <multiplier 0-10>", "Scales natural spawning (0 stops it, 1 is normal).", true, SpawnRate),
        new("where", "where", "Prints your tile, biome, depth layer and the time.", false, _ => Where()),
        new("power", "power | power set <0-1000> | power tier <1-5> | power feed", "Shows the station AI's power, sets it or its tier, or feeds it from your bag.", true, Power),
        new("ai", "ai down | ai rebuild", "Takes the station AI down (machines go dark) or rebuilds it.", true, Ai),
        new("storyteller", "storyteller [steady|chaotic|builder] [threat 0.5-2]", "Shows the storyteller, or changes its personality and threat dial.", true, Teller),
        new("raid", "raid | raid warn [hours] | raid now", "Shows the raid on its way, or announces one (default 2 hours' warning), or sends one now.", true, Raid),
        new("injure", "injure scar|eye|arm|leg", "Gives the resident nearest you a lasting injury.", true, Injure),
        new("outposts", "outposts", "Lists the outposts: where they are, who lives there and how many are armed.", false, _ => Outposts()),
        new("research", "research list | research done <tier>|all | research reset <tier>|all", "Lists the research tiers, finishes one or all, or puts them back.", true, Research),
    ];

    private IEnumerable<string> Help(string[] args)
    {
        if (args.Length > 0 && Commands.FirstOrDefault(command => command.Name == args[0].ToLowerInvariant()) is { } one)
            return [$"{one.Usage}: {one.Help}"];
        return Commands.Select(command => $"{command.Usage,-48} {command.Help}");
    }

    private IEnumerable<string> Find(string[] args)
    {
        var text = Rest(args, 0, "what to find").ToLowerInvariant();
        bool Match(string name) => Normal(name).Contains(Normal(text));
        var hits = new List<string>();
        hits.AddRange(_game.Items.All.Where(item => Match(item.Id.ToString()) || Match(item.Name)).Take(12).Select(item => $"item     {item.Id} ({item.Name})"));
        hits.AddRange(EnemyArchetypes.All.Where(archetype => Match(archetype.Kind.ToString()) || Match(archetype.DisplayName)).Take(12)
            .Select(archetype => $"creature {archetype.Kind} ({archetype.DisplayName}{(archetype.IsBoss ? ", boss" : "")})"));
        hits.AddRange(Enum.GetValues<TownNpcKind>().Where(kind => Match(kind.ToString())).Select(kind => $"npc      {kind}"));
        hits.AddRange(Enum.GetValues<WorldEventKind>().Where(kind => Match(kind.ToString())).Select(kind => $"event    {kind}"));
        hits.AddRange(Enum.GetValues<StatusId>().Where(kind => Match(kind.ToString())).Select(kind => $"status   {kind}"));
        return hits.Count == 0 ? [$"Nothing matches '{text}'."] : hits;
    }

    private IEnumerable<string> Give(string[] args)
    {
        var (name, count) = NameAndCount(args, "an item", 1_000_000);
        var item = ParseItem(name);
        var left = _game.Player.Inventory.Add(item, count);
        var added = count - left;
        return [left > 0 ? $"Gave {added} {_game.Items[item].Name} ({left} did not fit)." : $"Gave {added} {_game.Items[item].Name}."];
    }

    private IEnumerable<string> Spawn(string[] args)
    {
        var (name, count) = NameAndCount(args, "a creature", 50);
        var kind = ParseCreature(name);
        for (var i = 0; i < count; i++)
            _game.SpawnCreatureAt(kind, Beside(6f + i * 1.5f));
        return [$"Spawned {count} {EnemyArchetypes.Get(kind).DisplayName}."];
    }

    private IEnumerable<string> Defeat(string[] args)
    {
        var kind = ParseCreature(Rest(args, 0, "a boss"));
        if (!EnemyArchetypes.Get(kind).IsBoss)
            throw new ConsoleError($"{EnemyArchetypes.Get(kind).DisplayName} is not a boss.");
        var boss = _game.SpawnCreatureAt(kind, Beside(10f));
        boss.TakeDamage(1e9f);
        _game.Tick(new GameInput(new MovementInput(0f)));
        return [_game.DefeatedBosses.Contains(kind)
            ? $"The {boss.DisplayName} is defeated: drops, flags and unlocks applied."
            : $"The {boss.DisplayName} died, but its defeat did not register."];
    }

    private IEnumerable<string> Npc(string[] args)
    {
        var kind = Parse<TownNpcKind>(Rest(args, 0, "a town NPC kind"), "town NPC");
        // 0.3.10: a settler is a generated person, not the catalog's placeholder name.
        var npc = kind == TownNpcKind.Settler ? _game.AddSettlerAt(Beside(2f)) : _game.AddTownNpc(kind, Beside(2f), bound: false);
        return [$"{npc.Name} the {npc.Definition.Role} is here."];
    }

    private IEnumerable<string> Teller(string[] args)
    {
        var teller = _game.Storyteller;
        if (args.Length > 0)
        {
            var kind = args[0].ToLowerInvariant() switch
            {
                "steady" => StorytellerKind.Steady,
                "chaotic" => StorytellerKind.Chaotic,
                "builder" => StorytellerKind.Builder,
                _ => throw new ConsoleError("Use storyteller steady, chaotic or builder, then a threat from 0.5 to 2.")
            };
            var threat = args.Length > 1 && float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var given)
                ? given : teller.ThreatScale;
            teller.Configure(kind, threat);
        }
        return [$"Storyteller: {Storyteller.Name(teller.Kind)}, threat ×{teller.ThreatScale:0.##}. {Storyteller.Describe(teller.Kind)}"];
    }

    private IEnumerable<string> Raid(string[] args)
    {
        var verb = args.Length == 0 ? "show" : args[0].ToLowerInvariant();
        if (verb == "show")
            return [_game.PendingRaid is { } raid
                ? $"A raid of about {raid.Count} is coming for {raid.TargetName} from the {raid.SideName} in {_game.RaidHoursLeft:0.0} hours."
                : "No raid is on its way."];
        var hours = verb == "now" ? 0.01f
            : verb == "warn" ? (args.Length > 1 && float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var given)
                ? Math.Clamp(given, 0.01f, 24f) : GameSimulation.RaidWarningHours)
            : throw new ConsoleError("Use raid, raid warn [hours] or raid now.");
        return [_game.PlanRaid(200f, out var description, hours) > 0 ? description : "A raid is already on its way."];
    }

    private IEnumerable<string> Injure(string[] args)
    {
        var injury = Need(args, 0, "scar, eye, arm or leg").ToLowerInvariant() switch
        {
            "scar" => LastingInjury.Scar,
            "eye" => LastingInjury.LostEye,
            "arm" => LastingInjury.LostArm,
            "leg" => LastingInjury.LostLeg,
            _ => throw new ConsoleError("Use injure scar, eye, arm or leg.")
        };
        var near = _game.People.MinBy(npc => System.Numerics.Vector2.Distance(npc.Motion.Position, _game.Player.Motion.Position))
                   ?? throw new ConsoleError("Nobody lives here yet.");
        return [_game.Injure(near, injury) ? _game.LastMessage : $"{near.Name} cannot take another injury of that kind."];
    }

    private IEnumerable<string> Outposts()
    {
        var outposts = _game.Outposts;
        if (outposts.Count == 0)
            return [$"No outposts: everyone lives within {GameSimulation.OutpostDistance:0} tiles of the base."];
        return outposts.Select(outpost => $"{outpost.Label} at {outpost.Center.X:0},{outpost.Center.Y:0}: " +
                                          $"{string.Join(", ", outpost.People.Select(npc => npc.Name))} ({outpost.Guards} armed)");
    }

    private IEnumerable<string> KillAll()
    {
        var count = 0;
        foreach (var enemy in _game.Enemies.ToArray())
        {
            enemy.TakeDamage(1e9f);
            count++;
        }
        return [$"Killed {count} creatures."];
    }

    private IEnumerable<string> Event(string[] args)
    {
        var verb = Need(args, 0, "start, stop or list").ToLowerInvariant();
        if (verb == "list")
        {
            var running = _game.WorldEvents.Active.Select(state => state.Kind.ToString()).ToArray();
            return [running.Length == 0 ? "No events running." : "Running: " + string.Join(", ", running)];
        }
        var kind = Parse<WorldEventKind>(Rest(args, 1, "an event kind"), "event");
        return verb switch
        {
            "start" => [_game.WorldEvents.TryStart(kind, _game, forced: true, out var reason) ? $"{kind} started." : $"{kind} did not start: {reason}"],
            "stop" => [_game.WorldEvents.Stop(kind, _game) ? $"{kind} stopped." : $"{kind} is not running."],
            _ => throw new ConsoleError($"'{verb}' is not start, stop or list.")
        };
    }

    private IEnumerable<string> Time(string[] args)
    {
        var hour = Float(Need(args, 0, "an hour"), 0f, 24f);
        _game.SetTimeOfDay(hour % 24f);
        return [$"The time is now {hour % 24f:00.0} h."];
    }

    private IEnumerable<string> Flag(string[] args)
    {
        var verb = Need(args, 0, "get, set, clear or list").ToLowerInvariant();
        if (verb == "list")
        {
            var filter = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            var flags = _game.Flags.All.Where(pair => pair.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)).OrderBy(pair => pair.Key).ToArray();
            return flags.Length == 0 ? ["No flags match."] : flags.Take(60).Select(pair => $"{pair.Key} = {pair.Value}");
        }
        var name = Need(args, 1, "a flag name");
        switch (verb)
        {
            case "get":
                return [$"{name} = {_game.Flags.Get(name)}"];
            case "set":
                var value = args.Length > 2 ? Long(args[2]) : 1;
                _game.Flags.Clear(name);
                if (value != 0)
                    _game.Flags.Increment(name, value);
                return [$"{name} = {_game.Flags.Get(name)}"];
            case "clear":
                _game.Flags.Clear(name);
                return [$"{name} cleared."];
            default:
                throw new ConsoleError($"'{verb}' is not get, set, clear or list.");
        }
    }

    private IEnumerable<string> Heal()
    {
        _game.Player.Heal(_game.Player.MaximumHitPoints);
        return [$"Health {_game.Player.HitPoints:0}/{_game.Player.MaximumHitPoints:0}."];
    }

    private IEnumerable<string> Life(string[] args)
    {
        _game.Player.LifeCoresUsed = Int(Need(args, 0, "a number of life cores"), 0, PlayerCharacter.MaximumLifeCores);
        _game.Player.Heal(_game.Player.MaximumHitPoints);
        return [$"Max health {_game.Player.MaximumHitPoints:0} ({_game.Player.LifeCoresUsed} life cores)."];
    }

    private IEnumerable<string> God(string[] args)
    {
        var on = OnOff(Need(args, 0, "on or off"));
        if (on)
            _game.Flags.Set(GodFlag);
        else
            _game.Flags.Clear(GodFlag);
        return [$"God mode {(on ? "on" : "off")}."];
    }

    private IEnumerable<string> Buff(string[] args)
    {
        var status = Parse<StatusId>(Need(args, 0, "a status"), "status");
        var minutes = args.Length > 1 ? Float(args[1], 0.05f, 120f) : 5f;
        return [_game.Player.Statuses.Apply(status, BuffCurve.Hours(minutes), _game.Player.Stats.ImmunityTags)
            ? $"{status} for {minutes:0.#} min." : $"{status} was blocked (an immunity)."];
    }

    private IEnumerable<string> Unbuff(string[] args)
    {
        var what = Need(args, 0, "a status or all");
        if (what.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var active = _game.Player.Statuses.Active.Select(status => status.Id).ToArray();
            foreach (var id in active)
                _game.Player.Statuses.Remove(id);
            return [$"Removed {active.Length} statuses."];
        }
        var status = Parse<StatusId>(what, "status");
        return [_game.Player.Statuses.Remove(status) ? $"Removed {status}." : $"{status} was not active."];
    }

    private IEnumerable<string> Teleport(string[] args)
    {
        var where = Need(args, 0, "x y, spawn or core").ToLowerInvariant();
        Vector2 target;
        if (where == "spawn")
            target = _game.LandingSpot;
        else if (where == "core")
        {
            var x = (int)_game.Player.Motion.Position.X;
            var molten = _game.BiomeLayout.MoltenRows;
            target = new Vector2(x, Math.Clamp(molten[Math.Clamp(x, 0, molten.Length - 1)] + 4, 2, _game.World.Height - 4));
        }
        else
            target = new Vector2(Int(where, 2, _game.World.Width - 3), Int(Need(args, 1, "y"), 2, _game.World.Height - 3));
        _game.Player.Motion.Position = target;
        _game.Player.Motion.Velocity = Vector2.Zero;
        return [$"Moved to {target.X:0},{target.Y:0}."];
    }

    private IEnumerable<string> Weather(string[] args)
    {
        var kind = Need(args, 0, "a kind of weather").ToLowerInvariant();
        var amount = args.Length > 1 ? Float(args[1], 0f, 1f) : 1f;
        switch (kind)
        {
            case "clear": _game.ForceWeather(rain: 0f, snow: 0f, dust: 0f, fog: 0f, storm: false); break;
            case "rain": _game.ForceWeather(rain: amount); break;
            case "storm": _game.ForceWeather(rain: amount, storm: true); break;
            case "snow": _game.ForceWeather(snow: amount); break;
            case "dust": _game.ForceWeather(dust: amount); break;
            case "fog": _game.ForceWeather(fog: amount); break;
            default: throw new ConsoleError($"'{kind}' is not a kind of weather.");
        }
        return [$"Weather: {kind}{(kind == "clear" ? "" : $" {amount:0.##}")}."];
    }

    private IEnumerable<string> SpawnRate(string[] args)
    {
        var multiplier = Float(Need(args, 0, "a multiplier"), 0f, 10f);
        _game.Flags.Clear(SpawnRateFlag);
        if (Math.Abs(multiplier - 1f) > 0.001f)
            _game.Flags.Increment(SpawnRateFlag, 1 + (long)MathF.Round(multiplier * 100f));
        return [$"Spawn rate ×{multiplier:0.##}."];
    }

    private IEnumerable<string> Where()
    {
        var cell = new GridPoint((int)_game.Player.Motion.Position.X, (int)_game.Player.Motion.Position.Y);
        var reading = BiomeScanner.Scan(_game.World, cell);
        return [$"Tile {cell.X},{cell.Y}; {reading.Zone}{(reading.Underground ? ", underground" : "")}; {_game.ClockHour:00.0} h; " +
                $"health {_game.Player.HitPoints:0}/{_game.Player.MaximumHitPoints:0}; the Fracture {(_game.FractureActive ? "has" : "has not")} happened."];
    }

    private IEnumerable<string> Research(string[] args)
    {
        var verb = Need(args, 0, "list, done or reset").ToLowerInvariant();
        if (verb == "list")
            return GameSimulation.ResearchTiers.Select(tier =>
                $"{tier.Id,-9} {tier.Name}: {(_game.ResearchDone(tier.Id) ? "done" : _game.ResearchOpen(tier) ? $"open, {(int)(100f * _game.ResearchProgress(tier.Id) / tier.Cost)}%" : "not opened")}" +
                $" (bosses: {string.Join(", ", tier.Bosses)})");
        var which = Rest(args, 1, "a tier id or all");
        var tiers = which.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? GameSimulation.ResearchTiers.ToArray()
            : [GameSimulation.FindTier(which) ?? throw new ConsoleError($"No research tier '{which}'. Try: {string.Join(", ", GameSimulation.ResearchTiers.Select(tier => tier.Id))}.")];
        switch (verb)
        {
            case "done":
                foreach (var tier in tiers)
                    _game.FinishResearch(tier);
                return [$"Finished: {string.Join(", ", tiers.Select(tier => tier.Name))}."];
            case "reset":
                foreach (var tier in tiers)
                    _game.ResetResearch(tier);
                return [$"Reset: {string.Join(", ", tiers.Select(tier => tier.Name))}."];
            default:
                throw new ConsoleError($"'{verb}' is not list, done or reset.");
        }
    }

    private IEnumerable<string> Power(string[] args)
    {
        var verb = args.Length == 0 ? "show" : args[0].ToLowerInvariant();
        switch (verb)
        {
            case "show":
                break;
            case "set":
                _game.SetStationPower(power: Int(Need(args, 1, "an amount"), 0, GameSimulation.MaxPower));
                break;
            case "tier":
                _game.SetStationPower(tier: Int(Need(args, 1, "a tier"), 1, 5));
                break;
            case "feed":
                return [_game.FeedStationAi()];
            default:
                throw new ConsoleError($"'{verb}' is not set, tier or feed.");
        }
        return [$"Station AI {(_game.StationAiOnline ? "online" : "DOWN")}: power {_game.StationPower}/{GameSimulation.MaxPower}, tier {_game.StationPowerTier}."];
    }

    private IEnumerable<string> Ai(string[] args)
    {
        var verb = Need(args, 0, "down or rebuild").ToLowerInvariant();
        switch (verb)
        {
            case "down":
                if (_game.TownNpcs.FirstOrDefault(npc => npc.Kind == TownNpcKind.StationAi && !npc.IsBound) is { } ai)
                    _game.HurtTownNpc(ai, 1e9f);
                else
                    _game.Flags.Set(GameSimulation.AiDownFlag);
                return [_game.StationAiOnline ? "The station AI is still online (on this setting it comes back like any resident)." : "The station AI is down."];
            case "rebuild":
                return [_game.RebuildStationAi() ? "The station AI is rebuilt." : "The station AI is already online."];
            default:
                throw new ConsoleError($"'{verb}' is not down or rebuild.");
        }
    }

    // ------------------------------------------------------------------ parsing

    private Vector2 Beside(float tiles) =>
        _game.Player.Motion.Position + new Vector2(tiles, -1f);

    /// <summary>Every word from <paramref name="index"/> on, so names with spaces work ("med stim").</summary>
    private static string Rest(string[] args, int index, string what) =>
        index < args.Length ? string.Join(' ', args.Skip(index)) : throw new ConsoleError($"Needs {what}.");

    /// <summary>A name of one or more words, then an optional count ("give med stim 5").</summary>
    private static (string Name, int Count) NameAndCount(string[] args, string what, int max)
    {
        if (args.Length == 0)
            throw new ConsoleError($"Needs {what}.");
        if (args.Length > 1 && int.TryParse(args[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return (string.Join(' ', args[..^1]), Int(args[^1], 1, max));
        return (string.Join(' ', args), 1);
    }

    private static string Need(string[] args, int index, string what) =>
        index < args.Length ? args[index] : throw new ConsoleError($"Needs {what}.");

    private static int Int(string text, int min, int max) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max
            ? value : throw new ConsoleError($"'{text}' is not a whole number from {min} to {max}.");

    private static long Long(string text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : throw new ConsoleError($"'{text}' is not a number.");

    private static float Float(string text, float min, float max) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max
            ? value : throw new ConsoleError($"'{text}' is not a number from {min} to {max}.");

    private static bool OnOff(string text) => text.ToLowerInvariant() switch
    {
        "on" or "1" or "true" => true,
        "off" or "0" or "false" => false,
        _ => throw new ConsoleError($"'{text}' is not on or off.")
    };

    /// <summary>Lower case with spaces, dashes and underscores removed, so "med_stim", "Med stim" and "MedStim" all match.</summary>
    private static string Normal(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static T Parse<T>(string text, string what) where T : struct, Enum
    {
        var normal = Normal(text);
        foreach (var value in Enum.GetValues<T>())
            if (Normal(value.ToString()) == normal)
                return value;
        var close = Enum.GetValues<T>().Select(value => value.ToString()).Where(name => Normal(name).Contains(normal)).Take(6).ToArray();
        throw new ConsoleError(close.Length == 0 ? $"No {what} called '{text}'." : $"No {what} called '{text}'. Did you mean: {string.Join(", ", close)}?");
    }

    private ItemId ParseItem(string text)
    {
        var normal = Normal(text);
        foreach (var item in _game.Items.All)
            if (Normal(item.Id.ToString()) == normal || Normal(item.Name) == normal)
                return item.Id;
        var close = _game.Items.All.Where(item => Normal(item.Id.ToString()).Contains(normal) || Normal(item.Name).Contains(normal))
            .Select(item => item.Id.ToString()).Take(6).ToArray();
        throw new ConsoleError(close.Length == 0 ? $"No item called '{text}'." : $"No item called '{text}'. Did you mean: {string.Join(", ", close)}?");
    }

    private static EnemyKind ParseCreature(string text)
    {
        var normal = Normal(text);
        foreach (var archetype in EnemyArchetypes.All)
            if (Normal(archetype.Kind.ToString()) == normal || Normal(archetype.DisplayName) == normal)
                return archetype.Kind;
        var close = EnemyArchetypes.All.Where(archetype => Normal(archetype.Kind.ToString()).Contains(normal) || Normal(archetype.DisplayName).Contains(normal))
            .Select(archetype => archetype.Kind.ToString()).Take(6).ToArray();
        throw new ConsoleError(close.Length == 0 ? $"No creature called '{text}'." : $"No creature called '{text}'. Did you mean: {string.Join(", ", close)}?");
    }
}
