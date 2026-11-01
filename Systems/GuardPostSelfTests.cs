using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Guards and guard posts (0.4.1): an armed resident holds a post you set near their home, day and night, and fights what
/// comes near it; a badly hurt guard falls back home to heal; people who do not fight take cover when creatures come.
/// </summary>
public static class GuardPostSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Flat(int seed, float hour = 10f)
    {
        var game = new GameSimulation(seed: seed, width: 140, height: 56) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        game.Player.Motion.Position = new Vector2(8f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.SetTimeOfDay(hour);
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static TownNpc Guard(GameSimulation game, float x, bool armed = true)
    {
        var npc = game.AddTownNpc(TownNpcKind.Armorer, new Vector2(x, Floor - 3f), bound: false);
        npc.Motion.Position = new Vector2(x, Floor - npc.Motion.Size.Y - 0.01f);
        npc.HomeCell = new GridPoint((int)x, Floor - 2);
        if (armed)
        {
            game.Player.Inventory.Add(ItemId.ScrapPistol, 1);
            game.EquipResident(npc, ItemId.ScrapPistol);
        }
        return npc;
    }

    private static void Tick(GameSimulation game, int ticks, Action? each = null, EnemyAgent? keep = null)
    {
        for (var i = 0; i < ticks; i++)
        {
            each?.Invoke();
            game.Player.Heal(1e6f);
            game.Tick(new GameInput(new MovementInput(0f)));
            foreach (var enemy in game.Enemies.Where(enemy => !ReferenceEquals(enemy, keep)).ToArray())
                enemy.TakeDamage(1e6f);
        }
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- setting a post: armed, housed, near home ----
        var game = Flat(9861);
        var unarmed = Guard(game, 30f, armed: false);
        var refusedUnarmed = game.SetGuardPost(unarmed, new GridPoint(34, Floor - 1)).Contains("weapon");
        var guard = Guard(game, 50f);
        var tooFar = game.SetGuardPost(guard, new GridPoint(50 + (int)GameSimulation.MaxPostDistance + 5, Floor - 1)).Contains("Too far");
        game.SetGuardPost(guard, new GridPoint(62, Floor - 1));
        var reloaded = GameSimulation.Restore(game.CaptureSnapshot());
        var savedPost = reloaded.TownNpcs.Single(npc => npc.Name == guard.Name).Post == new GridPoint(62, Floor - 1);
        game.UnequipResident(guard);
        var clearedWithWeapon = guard.Post is null;
        check(refusedUnarmed && tooFar && savedPost && clearedWithWeapon,
            "a guard post needs a weapon and must be near home; it is saved with the world, and taking the weapon back clears it");

        // ---- an armed guard holds their post by night ----
        var night = Flat(9862, hour: 23f);
        var watch = Guard(night, 40f);
        night.SetGuardPost(watch, new GridPoint(55, Floor - 1));
        Tick(night, 900);
        var atPost = MathF.Abs(watch.Motion.Position.X + watch.Motion.Size.X / 2f - 55.5f) <= GameSimulation.PostBeat + 1f;
        check(atPost && watch.Activity == "On guard at their post",
            $"an armed guard holds their post by night instead of going indoors ({watch.Activity}, at x {watch.Motion.Position.X:0.0})");

        // ---- they fight what comes near the post, even far from home ----
        var slime = night.SpawnCreatureAt(EnemyKind.MossSlime, new Vector2(64f, Floor - 1.2f));
        var health = slime.HitPoints;
        Tick(night, 600, keep: slime);
        check(!slime.IsAlive || slime.HitPoints < health,
            $"a guard fights a creature near their post, twenty tiles from home (slime {(slime.IsAlive ? $"{slime.HitPoints:0}/{health:0}" : "dead")})");

        // ---- badly hurt, a guard falls back home, and returns once healed ----
        var hurt = Flat(9863);
        var bloodied = Guard(hurt, 40f);
        hurt.SetGuardPost(bloodied, new GridPoint(58, Floor - 1));
        Tick(hurt, 300);
        bloodied.HitPoints = bloodied.MaximumHitPoints * 0.2f;
        var fellBack = false;
        Tick(hurt, 60, () => fellBack |= bloodied.Activity == "Falling back to heal");
        var homeward = bloodied.Motion.Position.X < 52f;
        Tick(hurt, 60 * 90);
        check(fellBack && homeward && !bloodied.FallingBack && bloodied.Activity == "On guard at their post",
            $"a guard below {GameSimulation.FallBackShare:P0} life falls back home to heal, and returns to their post from {GameSimulation.RecoveredShare:P0} ({bloodied.Activity})");

        // ---- people who do not fight take cover when creatures come ----
        var cover = Flat(9864);
        var cook = cover.AddTownNpc(TownNpcKind.Cook, new Vector2(40f, Floor - 3f), bound: false);
        cook.Motion.Position = new Vector2(46f, Floor - cook.Motion.Size.Y - 0.01f);
        cook.HomeCell = new GridPoint(40, Floor - 2);
        foreach (var type in Enum.GetValues<WorkType>())
            cook.Work.Set(type, WorkCatalog.Off);
        var beast = cover.SpawnCreatureAt(EnemyKind.MossSlime, new Vector2(50f, Floor - 1.2f));
        var tookCover = false;
        Tick(cover, 120, () => tookCover |= cook.Activity == "Taking cover", keep: beast);
        check(tookCover && cook.Motion.Position.X < 46f,
            $"someone who does not fight takes cover at home when a creature comes near ({cook.Activity}, x {cook.Motion.Position.X:0.0})");
    }
}
