using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// One kind of character (0.3.10): every resident is a person with skills; a trade's skills are strong in the people who
/// practise it; settlers replace the crew; residents guard their area by night too, if they are fighters; bargaining is the
/// Trade Consul's.
/// </summary>
public static class TownPeopleSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Flat(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 120, height: 56) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        game.Player.Motion.Position = new Vector2(8f, Floor - game.Player.Motion.Size.Y - 0.01f);
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static void Tick(GameSimulation game, int ticks, EnemyAgent? keep = null)
    {
        for (var i = 0; i < ticks; i++)
        {
            game.Player.Heal(1e6f);
            game.Tick(new GameInput(new MovementInput(0f)));
            foreach (var enemy in game.Enemies.Where(enemy => !ReferenceEquals(enemy, keep)).ToArray())
                enemy.TakeDamage(1e6f);
        }
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- every trade has its skills, and its people are good at them ----
        var trades = TownNpcCatalog.All.Select(definition => definition.Kind)
            .Where(kind => kind != TownNpcKind.Settler && !(kind is >= TownNpcKind.GelAzure and <= TownNpcKind.GelVerdant)).ToArray();
        var missing = trades.Where(kind => TownPeople.SkillsOf(kind).Count == 0).ToArray();
        var weak = trades.Where(kind =>
        {
            var stats = TownPeople.ProfileFor(kind, "Test Person", 77).Stats;
            var skills = TownPeople.SkillsOf(kind);
            return skills.Count > 0 && (stats.Level(skills[0]) < TownPeople.MainSkill || stats.Passion(skills[0]) != PassionLevel.Burning ||
                                        skills.Skip(1).Any(skill => stats.Level(skill) < TownPeople.SecondSkill));
        }).ToArray();
        var same = TownPeople.ProfileFor(TownNpcKind.Medic, "Dr. Senna Abara", 5).Stats.SkillLevels
            .SequenceEqual(TownPeople.ProfileFor(TownNpcKind.Medic, "Dr. Senna Abara", 5).Stats.SkillLevels);
        check(missing.Length == 0 && weak.Length == 0 && same,
            $"every trade has its skills and its people are good at them: main skill 10+ with a burning passion, second 7+, the same person " +
            $"each time (no skills: {string.Join(", ", missing)}; weak: {string.Join(", ", weak)})");

        // ---- a resident is a person, saved with the world ----
        var world = Flat(9801);
        var medic = world.AddTownNpc(TownNpcKind.Medic, new Vector2(40f, Floor - 3f), bound: false);
        medic.HomeCell = new GridPoint(40, Floor - 2);
        var reloaded = GameSimulation.Restore(world.CaptureSnapshot());
        var again = reloaded.TownNpcs.Single(npc => npc.Kind == TownNpcKind.Medic);
        check(medic.Stats.Level(ColonistSkill.Medical) >= TownPeople.MainSkill && again.Name == medic.Name &&
              again.Stats.SkillLevels.SequenceEqual(medic.Stats.SkillLevels) && again.Profile.Backstory == medic.Profile.Backstory &&
              again.Work.Capture().SequenceEqual(medic.Work.Capture()),
            $"a resident is a person (Medical {medic.Stats.Level(ColonistSkill.Medical)}, {medic.Profile.BackstoryName}) and stays the same person through a save");

        // ---- the station AI only runs machines and studies; only fighters guard; bargaining is the consul's ----
        var ai = TownPeople.WorkFor(TownNpcKind.StationAi, TownPeople.ProfileFor(TownNpcKind.StationAi, "ORACLE-7", 1).Stats);
        var aiWork = ai.Order().ToHashSet();
        var meek = TownPeople.ProfileFor(TownNpcKind.Cook, "Meek", 3);
        var meekLevels = meek.SkillLevels!.ToArray();
        meekLevels[(int)ColonistSkill.Melee] = 0;
        var meekPassions = meek.Passions!.ToArray();
        meekPassions[(int)ColonistSkill.Melee] = PassionLevel.None;
        var meekWork = TownPeople.WorkFor(TownNpcKind.Cook, (meek with { SkillLevels = meekLevels, Passions = meekPassions }).Stats);
        var consul = TownPeople.WorkFor(TownNpcKind.TradeConsul, TownPeople.ProfileFor(TownNpcKind.TradeConsul, "Consul Ysolde", 2).Stats);
        var clerk = TownPeople.WorkFor(TownNpcKind.Quartermaster, TownPeople.ProfileFor(TownNpcKind.Quartermaster, "Dorrin Vask", 2).Stats);
        check(aiWork.SetEquals([WorkType.Operate, WorkType.Research]) && meekWork.Priority(WorkType.Guard) == WorkCatalog.Off &&
              meekWork.Priority(WorkType.Cook) == WorkCatalog.Highest &&
              consul.Priority(WorkType.Trade) != WorkCatalog.Off && consul.Order().Count() == 1 && clerk.Priority(WorkType.Trade) == WorkCatalog.Off,
            "the station AI only runs machines and studies; someone who cannot fight does not guard; the Trade Consul bargains and a shopkeeper does not");

        // ---- a fighter guards their area at night too; residents who do not guard stay indoors ----
        var night = Flat(9802);
        night.SetTimeOfDay(23f);
        var guard = night.AddStartingCrewForChecks()[0];
        guard.Motion.Position = new Vector2(60f, Floor - guard.Motion.Size.Y - 0.01f);
        guard.HomeCell = new GridPoint(60, Floor - 2);
        foreach (var type in Enum.GetValues<WorkType>())
            guard.Work.Set(type, WorkCatalog.Off);
        guard.Work.Set(WorkType.Guard, WorkCatalog.Highest);
        var slime = night.SpawnCreatureAt(EnemyKind.MossSlime, new Vector2(64f, Floor - 1.2f));
        var health = slime.HitPoints;
        var guarded = false;
        for (var i = 0; i < 600 && slime.IsAlive; i++)
        {
            Tick(night, 1, keep: slime);
            guarded |= guard.Activity.StartsWith("Guarding");
        }
        check(guarded && (!slime.IsAlive || slime.HitPoints < health),
            $"a resident who fights guards their area against a creature by night ({guard.Activity}; slime {(slime.IsAlive ? $"{slime.HitPoints:0}/{health:0}" : "dead")})");

        // ---- settlers: generated people, any number; the console brings one; one dying does not bring them back ----
        var town = Flat(9803);
        var console = new DevConsole(town);
        console.Execute("npc settler");
        console.Execute("npc settler");
        var settlers = town.Settlers;
        var named = settlers.Count == 2 && settlers.All(npc => npc.Name != "Settler") && settlers[0].Name != settlers[1].Name;
        var anyStation = settlers.All(npc => ((IStationWorker)npc).CanWorkAt(CraftingStationId.ChemLab) && ((IStationWorker)npc).CanWorkAt(CraftingStationId.AmmoPress));
        town.HurtTownNpc(settlers[0], 1e6f);
        for (var hour = 0; hour < 30; hour++)
            town.CheckTownArrivals();
        check(named && anyStation && town.Settlers.Count == 1 && !town.LostResidentKinds().Contains(TownNpcKind.Settler),
            "settlers are generated people (the console's 'npc settler' brings one), any number can live here, they work any station, and a dead one is not replaced by the town");
    }
}
