using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Creature stats (0.1.9 stream A): the defense tier table and armor penetration (EN-4, IC-10), no double hardmode
/// rescale (D-18), explicit immunities and lava (EN-5), and Terraria's banner numbers (EN-18, D-21).
/// The StatParitySelfTests and GearCurveSelfTests bands run beside these in CoreSelfTests.
/// </summary>
public static class CreatureDefenseSelfTests
{
    private static GameSimulation NewGame(int seed, int width = 80)
    {
        var game = new GameSimulation(seed: seed, width: width, height: 48) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e7f);
        for (var y = 4; y <= 26; y++)
        for (var x = 4; x <= width - 4; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y >= 20 ? TileId.Rock : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            game.World.SetCell(cell, data);
        }
        Place(game, new Vector2(30f, 20f - game.Player.Motion.Size.Y - 0.01f));
        foreach (var agent in game.Settlers)
            agent.Motion.Position = new Vector2(10f, 20f - agent.Motion.Size.Y - 0.01f);
        Step(game, 3);
        return game;
    }

    private static void Place(GameSimulation game, Vector2 position)
    {
        game.Player.Motion.Position = position;
        game.Player.Motion.Velocity = Vector2.Zero;
    }

    private static void Step(GameSimulation game, int ticks = 1, PlayerAction action = PlayerAction.None, GridPoint? target = null,
        params EnemyAgent[] pinned)
    {
        var positions = pinned.Select(enemy => enemy.Motion.Position).ToArray();
        for (var i = 0; i < ticks; i++)
        {
            for (var p = 0; p < pinned.Length; p++)
            {
                pinned[p].Motion.Position = positions[p];
                pinned[p].Motion.Velocity = Vector2.Zero;
            }
            game.Player.Heal(1e6f);
            game.Tick(new GameInput(new MovementInput(0f), action, target));
        }
    }

    private static EnemyAgent Spawn(GameSimulation game, EnemyKind kind, Vector2 position)
    {
        var enemy = game.SpawnCreature(kind, position);
        enemy.Motion.Position = position;
        enemy.Motion.Velocity = Vector2.Zero;
        return enemy;
    }

    /// <summary>A rock-walled pit of lava, <paramref name="left"/>..<paramref name="right"/> wide, from the floor (y 20) to y 23.</summary>
    private static void LavaPit(GameSimulation game, int left, int right)
    {
        for (var y = 20; y <= 23; y++)
        for (var x = left; x <= right; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidKind = LiquidKind.Lava;
            data.LiquidDepth = 1f;
            game.World.SetCell(cell, data);
        }
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- A1 · EN-4 / IC-10: the defense tier table ----
        var undefended = EnemyArchetypes.All.Where(archetype => !archetype.Passive && !archetype.IsBoss && archetype.HitPoints > 150f &&
                                                                archetype.Defense <= 0).Select(archetype => archetype.Kind).ToArray();
        check(undefended.Length == 0, $"defense: every hostile non-boss creature over 150 HP has defense ({string.Join(", ", undefended)})");
        var gel = EnemyArchetypes.Get(EnemyKind.MeadowGel);
        var walker = EnemyArchetypes.Get(EnemyKind.GraveHusk);
        var outOfBand = EnemyArchetypes.All.Where(archetype => !archetype.Passive && !archetype.IsBoss &&
                                                               archetype.Kind != EnemyKind.DungeonGuardian &&
                                                               (CreatureStatTable.IsLate(archetype.Kind) ? archetype.Defense is < 30 or > 50
                                                                   : CreatureStatTable.IsPostFracture(archetype.Kind) ? archetype.Defense is < 16 or > 30
                                                                   : archetype.Defense is < 0 or > 14))
            .Select(archetype => $"{archetype.Kind}={archetype.Defense}").ToArray();
        check(gel.Defense == 2 && walker.Defense == 6 && EnemyArchetypes.Get(EnemyKind.SporeZombie).Defense == 6 &&
              EnemyArchetypes.All.Where(archetype => archetype.Passive).All(archetype => archetype.Defense == 0) && outOfBand.Length == 0,
            $"defense: gels 2, walkers 6, wildlife 0; pre-Fracture 0-14, post-Fracture 16-30, late 30-50 ({string.Join(", ", outOfBand)})");
        check(EnemyArchetypes.Get(EnemyKind.AstroGod).Defense == 50 && EnemyArchetypes.Get(EnemyKind.FissionTyrant).Defense == 12 &&
              EnemyArchetypes.All.Where(archetype => archetype.IsBoss).All(archetype => archetype.Defense is >= 10 and <= 50),
            "defense: bosses sit in their analog's band (Wall of Flesh 12 … Moon Lord 50)");

        // A 10-damage hit on defense 20: half the defense (Classic) leaves the 1-point floor; penetration gives some back.
        var bare = CombatTuning.CreatureDamageTaken(10f, 20, 0, false);
        var pierced = CombatTuning.CreatureDamageTaken(10f, 20, 10, false);
        check(bare == 1f && pierced > bare && pierced == 5f,
            $"defense: a 10-damage hit on defense 20 does {bare:0} without armor penetration and {pierced:0} with 10");

        // An armor-penetration accessory (the Moon-eater fang, Terraria's shark tooth necklace: 5) raises damage on armor.
        var apGame = NewGame(9201);
        var armored = Spawn(apGame, EnemyKind.GlacierTitan, new Vector2(50f, 18f));
        var defense = armored.Archetype.Defense;
        var plain = apGame.ProbeStrike(armored, 30f, false, defense);
        apGame.Player.Equipment.Set(EquipmentSlot.Accessory, ItemId.MooneaterFang);
        var penetration = apGame.PlayerArmorPenetration(DamageClass.Melee);
        var withAp = apGame.ProbeStrike(armored, 30f, false, defense);
        check(defense >= 10 && penetration == 5 && withAp > plain && withAp == plain + 2f,
            $"defense: an armor-penetration accessory (ap {penetration}) raises a hit on defense {defense} from {plain:0} to {withAp:0}");

        // Knockback resistance: an archetype override wins over the size-and-bulk formula.
        var turtle = EnemyArchetypes.Get(EnemyKind.MagmaTurtle);
        check(turtle.KnockbackResist == 0.7f && CombatTuning.KnockbackResistance(turtle) == 0.7f &&
              EnemyArchetypes.Get(EnemyKind.GelHopper).KnockbackResist is null &&
              CombatTuning.KnockbackResistance(EnemyArchetypes.Get(EnemyKind.GelHopper)) == 0f,
            "knockback: the optional KnockbackResist override replaces the formula; other creatures keep it");

        // ---- D-18: creatures authored for the Fracture are not rescaled; pre-Fracture kinds are, once ----
        var authored = EnemyArchetypes.All.Where(archetype => CreatureStatTable.IsFractureAuthored(archetype.Kind)).ToArray();
        check(authored.Length > 40 && authored.All(archetype => !GameSimulation.HardmodeRescales(archetype)),
            $"D-18: none of the {authored.Length} Fracture-authored kinds takes the hardmode rescale");
        var fractured = NewGame(9202);
        fractured.TriggerFracture();
        var titan = Spawn(fractured, EnemyKind.GlacierTitan, new Vector2(60f, 18f));
        var golem = Spawn(fractured, EnemyKind.RockGolem, new Vector2(66f, 18f));
        Step(fractured, 5);
        Step(fractured, 5);
        var (health, damage) = GameSimulation.HardmodeScale(fractured.Difficulty);
        var titanArchetype = EnemyArchetypes.Get(EnemyKind.GlacierTitan);
        check(titan.MaximumHitPoints == titanArchetype.HitPoints && titan.AttackDamage == titanArchetype.AttackDamage &&
              MathF.Abs(golem.MaximumHitPoints - 70f * health) < 0.01f && MathF.Abs(golem.AttackDamage - 14f * damage) < 0.01f,
            $"D-18: after the Fracture a Fracture-authored creature keeps its authored stats ({titan.MaximumHitPoints:0} HP); " +
            $"a pre-Fracture kind is scaled once ({golem.MaximumHitPoints:0} HP)");

        // ---- A2 · EN-5: explicit immunities ----
        var firefly = EnemyArchetypes.Get(EnemyKind.GlowFirefly);
        var swarm = EnemyArchetypes.Get(EnemyKind.PollenDroneSwarm);
        var immuneGame = NewGame(9203);
        var bug = Spawn(immuneGame, EnemyKind.GlowFirefly, new Vector2(40f, 16f));
        var drones = Spawn(immuneGame, EnemyKind.PollenDroneSwarm, new Vector2(44f, 16f));
        check(!firefly.Immune.Contains(StatusId.Burning) && immuneGame.AfflictEnemy(bug, StatusId.Burning) && bug.HasAffliction(StatusId.Burning),
            "immunities: the Glow firefly can burn");
        check(swarm.Immune.Contains(StatusId.Poison) && !immuneGame.AfflictEnemy(drones, StatusId.Poison) &&
              !EnemyArchetypes.Get(EnemyKind.LatticeWeaver).Immune.Contains(StatusId.Frostbite) &&
              EnemyArchetypes.Get(EnemyKind.MagmaTurtle).Immune.Contains(StatusId.Burning) &&
              EnemyArchetypes.Get(EnemyKind.SnowWolf).Immune.Contains(StatusId.Frostbite) &&
              EnemyArchetypes.All.Where(archetype => archetype.IsBoss).All(archetype => archetype.Immune.Contains(StatusId.Confused)),
            "immunities: the Pollen drone swarm resists poison; fire- and frost-born resist their own; bosses ignore confusion");

        // ---- A2 · EN-5: lava ----
        var lavaGame = NewGame(9204);
        LavaPit(lavaGame, 40, 44);
        LavaPit(lavaGame, 54, 58);
        var husk = Spawn(lavaGame, EnemyKind.SporeZombie, new Vector2(41.5f, 20.3f));
        var coreHusk = Spawn(lavaGame, EnemyKind.CoreHusk, new Vector2(55.5f, 20.3f));
        var coreStart = coreHusk.HitPoints;
        var ticks = 0;
        while (husk.IsAlive && ticks < 300)
        {
            Step(lavaGame, 1, pinned: [husk, coreHusk]);
            ticks++;
        }
        Step(lavaGame, Math.Max(0, 300 - ticks), pinned: [coreHusk]);
        check(!husk.IsAlive && ticks <= 300 && coreHusk.IsAlive && coreHusk.HitPoints == coreStart &&
              !EnemyArchetypes.Get(EnemyKind.SporeZombie).LavaImmune && EnemyArchetypes.Get(EnemyKind.CoreHusk).LavaImmune,
            $"lava: a Spore husk dies in lava within 5 s ({ticks / 60f:0.0} s); an Irradiated (core) husk does not");

        // ---- A3 · EN-18 / D-21: banner numbers (Terraria 1.4.4 Player.GetBannerBuffEffect / NPCBannerBuff, cited in
        // GameSimulation.Player.cs; from memory, not re-measured from the IL) ----
        check(GameSimulation.BannerDamageBonus == 1.5f && GameSimulation.BannerDamageTakenFactor == 0.75f &&
              GameSimulation.BannerHarshDamageBonus == 2f && GameSimulation.BannerHarshDamageTakenFactor == 0.5f &&
              GameSimulation.BannerRangeX == 84f && GameSimulation.BannerRangeY == 62f,
            "banners: ×1.5 dealt / ×0.75 taken (×2 / ×0.5 on Brutal and Annihilation) within Terraria's 169 × 124 tile scan box");
        var hunter = NewGame(9205, width: 120);
        var banner = PlayerItems.BannerFor(EnemyKind.GelHopper);
        hunter.Player.Inventory.Add(banner, 1);
        hunter.TrySelectInventorySlot(hunter.Player.Inventory.FindSlot(banner));
        hunter.Player.ActionCooldownTicks = 0;
        Step(hunter, 31, PlayerAction.UseSelected, new GridPoint((int)hunter.Player.Motion.Position.X + 2, 19));
        Place(hunter, new Vector2(100f, 20f - hunter.Player.Motion.Size.Y - 0.01f)); // ~68 tiles away: outside the old 30-tile radius
        Step(hunter, 31);
        var near = hunter.ActiveBannerKinds.Contains(EnemyKind.GelHopper);
        var classic = hunter.ProbeBannerFactors(EnemyKind.GelHopper);
        var other = hunter.ProbeBannerFactors(EnemyKind.SnowWolf);
        hunter.Difficulty = WorldDifficulty.Brutal;
        var brutal = hunter.ProbeBannerFactors(EnemyKind.GelHopper);
        check(hunter.BannerTiles.Count == 1 && near && classic == (1.5f, 0.75f) && other == (1f, 1f) && brutal == (2f, 0.5f),
            $"banners: a hung banner {(near ? "is" : "is NOT")} active 68 tiles away and gives {classic} (Brutal {brutal})");
    }
}
