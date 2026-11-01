using System.Numerics;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>The colonist bar and inspect cards (0.2.8): picking, card contents, live updates and the bar's layout.</summary>
public static class InspectSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Town(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 120, height: 56) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        game.Player.Motion.Position = new Vector2(8f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.AddStartingCrewForChecks();
        for (var i = 0; i < game.Colonists.Count; i++)
        {
            var agent = game.Colonists[i];
            agent.Motion.Position = new Vector2(40f + i * 10f, Floor - agent.Motion.Size.Y - 0.01f);
            agent.HomePosition = agent.Motion.Position;
        }
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static Vector2 Center(PawnMotionState motion) => motion.Position + motion.Size / 2f;

    public static void Run(Action<bool, string> check)
    {
        // ---- picking: whoever is under the point, crew first; nothing on empty ground ----
        var game = Town(9981);
        var crew = game.Colonists[0];
        var npc = game.AddTownNpc(TownNpcKind.Mechanic, new Vector2(80f, Floor - 3f), bound: false);
        npc.HomeCell = new GridPoint(80, Floor - 2);
        var slime = game.SpawnCreatureAt(EnemyKind.MossSlime, new Vector2(100f, Floor - 1.2f));
        check(game.PickInspectTarget(Center(crew.Motion)) == new InspectTarget(InspectKind.Colonist, crew.Id) &&
              game.PickInspectTarget(Center(npc.Motion)) == new InspectTarget(InspectKind.Resident, npc.Id) &&
              game.PickInspectTarget(Center(slime.Motion)) == new InspectTarget(InspectKind.Creature, slime.Id) &&
              game.PickInspectTarget(new Vector2(20f, Floor - 6f)) is null,
            "a middle click picks the crew member, resident or creature under the cursor, and nothing on empty ground");

        // ---- cards: what each says ----
        var crewCard = game.Inspect(new InspectTarget(InspectKind.Colonist, crew.Id));
        var npcCard = game.Inspect(new InspectTarget(InspectKind.Resident, npc.Id));
        var slimeCard = game.Inspect(new InspectTarget(InspectKind.Creature, slime.Id));
        check(crewCard is { Mood: not null } && crewCard.Title == crew.Name && crewCard.Lines.Any(line => line.StartsWith("Mood")) &&
              crewCard.Lines.Any(line => line.StartsWith("Food")) && crewCard.Lines.Any(line => line.StartsWith("Doing:")) &&
              crewCard.Lines.Any(line => line.StartsWith("Best at:")) &&
              npcCard is { Mood: null } && npcCard.Subtitle.Contains(npc.Definition.Role) && npcCard.Lines.Any(line => line.Contains("bills")) &&
              slimeCard is not null && slimeCard.Title == slime.DisplayName && slimeCard.Lines.Any(line => line.StartsWith("Defense")),
            "a crew card shows health, mood, needs, what they are doing, their work and best skills; a resident's shows role, home and bill work; a creature's shows health and defense");

        // ---- live: the card follows health, and closes when its subject dies ----
        var before = game.Inspect(new InspectTarget(InspectKind.Creature, slime.Id))!.Health;
        slime.TakeDamage(slime.MaximumHitPoints * 0.5f);
        var after = game.Inspect(new InspectTarget(InspectKind.Creature, slime.Id))?.Health ?? 1f;
        slime.TakeDamage(1e6f);
        check(after < before && game.Inspect(new InspectTarget(InspectKind.Creature, slime.Id)) is null,
            $"the card is rebuilt live ({before:0.00} → {after:0.00} health) and is gone once its subject dies");

        // ---- the colonist bar: one portrait per living crew member, with initials, health and mood ----
        var entries = ColonistBar.Entries(game);
        check(entries.Count == game.Colonists.Count(agent => agent.IsAlive) && entries[0].Id == crew.Id &&
              entries.All(entry => entry.Health is > 0f and <= 1f && entry.Mood is >= 0f and <= 1f) &&
              ColonistBar.Initials("Sable Lark") == "SL" && ColonistBar.Initials("Ash") == "A",
            "the colonist bar has a portrait per living crew member with initials, health and mood");

        // ---- layout: centred between hotbar and life panel; shrinks, then wraps ----
        var few = ColonistBar.Layout(3, 500f, 900f);
        var many = ColonistBar.Layout(12, 500f, 740f);
        var centred = Math.Abs((few[0].Left - 500f) - (900f - few[^1].Right)) < 1f;
        check(few.Count == 3 && few[0].Width == ColonistBar.Slot && centred && many.Count == 12 &&
              many.All(rect => rect.Left >= 500f && rect.Right <= 740f + 0.5f) && many[0].Width < ColonistBar.Slot &&
              many.Max(rect => rect.Top) > many[0].Top,
            "the bar centres its portraits in the gap, shrinks them when crowded and wraps onto a second row");
    }
}
