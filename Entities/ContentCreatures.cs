using System.Numerics;
using Rimtera.Systems;

namespace Rimtera.Entities;

/// <summary>
/// Content-expansion creature roster: critters, biome creatures for the Acid Coast, Fungal Hollows,
/// Crystal Spires, Verdant Canopy and Precursor Vault, invasion/event creatures, and the new bosses.
/// All tuning is original Rimtera data.
/// </summary>
internal static class ContentCreatures
{
    public static IEnumerable<EnemyArchetype> Archetypes()
    {
        // ---- Critters: passive wildlife. (kind, name, w, h, flying) ----
        (EnemyKind Kind, string Name, float W, float H, bool Fly)[] critters =
        [
            (EnemyKind.GlowGrub, "Glow grub", 0.6f, 0.4f, false), (EnemyKind.SandSkitterer, "Sand skitterer", 0.6f, 0.4f, false),
            (EnemyKind.ScavengerBeetle, "Scavenger beetle", 0.6f, 0.45f, false), (EnemyKind.SkyFlier, "Sky flier", 0.6f, 0.5f, true),
            (EnemyKind.TerraRat, "Terra rat", 0.6f, 0.45f, false), (EnemyKind.SnowHare, "Snow hare", 0.7f, 0.6f, false),
            (EnemyKind.ScrapRat, "Scrap rat", 0.6f, 0.45f, false), (EnemyKind.EmberNewt, "Ember newt", 0.6f, 0.35f, false),
            (EnemyKind.GlowFirefly, "Glow firefly", 0.4f, 0.4f, true), (EnemyKind.PebbleBeetle, "Pebble beetle", 0.5f, 0.4f, false),
            (EnemyKind.GoldenCritter, "Golden critter", 0.6f, 0.5f, false),
            (EnemyKind.ShellCrab, "Shell crab", 0.6f, 0.45f, false), (EnemyKind.GlowJelly, "Glow jelly", 0.6f, 0.6f, true),
            (EnemyKind.BeachSkitter, "Beach skitter", 0.5f, 0.35f, false), (EnemyKind.SeaBird, "Sea bird", 0.7f, 0.5f, true),
            (EnemyKind.GlowSnail, "Glow snail", 0.5f, 0.4f, false), (EnemyKind.SporeMoth, "Spore moth", 0.5f, 0.45f, true),
            (EnemyKind.CaveFrog, "Cave frog", 0.5f, 0.4f, false), (EnemyKind.TruffleMite, "Truffle mite", 0.45f, 0.35f, false),
            (EnemyKind.PrismButterfly, "Prism butterfly", 0.45f, 0.45f, true), (EnemyKind.LightWispBird, "Light-wisp bird", 0.6f, 0.5f, true),
            (EnemyKind.CrystalBunny, "Crystal bunny", 0.6f, 0.55f, false), (EnemyKind.HaloSnail, "Halo snail", 0.5f, 0.45f, false),
            (EnemyKind.CanopyParrot, "Canopy parrot", 0.6f, 0.55f, true), (EnemyKind.LeafFrog, "Leaf frog", 0.5f, 0.4f, false),
            (EnemyKind.PollenBee, "Pollen bee", 0.4f, 0.4f, true), (EnemyKind.VineSnake, "Vine snake", 0.9f, 0.35f, false),
            (EnemyKind.HopBunny, "Hop bunny", 0.6f, 0.55f, false), (EnemyKind.GrassSnake, "Grass snake", 0.9f, 0.3f, false),
            (EnemyKind.PenguinDrone, "Penguin drone", 0.6f, 0.7f, false), (EnemyKind.DuneLizard, "Dune lizard", 0.8f, 0.35f, false),
            (EnemyKind.Worm, "Burrow worm", 0.6f, 0.25f, false), (EnemyKind.ButterflyBlue, "Blue wingflit", 0.45f, 0.45f, true),
            (EnemyKind.ButterflyGold, "Gold wingflit", 0.45f, 0.45f, true), (EnemyKind.Dragonfly, "Glassfly", 0.6f, 0.35f, true),
            (EnemyKind.SquirrelBot, "Squirrel bot", 0.6f, 0.55f, false), (EnemyKind.Owl, "Night owl", 0.6f, 0.6f, true),
            (EnemyKind.Duck, "Pond duck", 0.6f, 0.55f, false), (EnemyKind.FishSilver, "Silver fish", 0.6f, 0.35f, true),
            (EnemyKind.FishGlow, "Glow fish", 0.6f, 0.35f, true), (EnemyKind.CrabSmall, "Little crab", 0.5f, 0.35f, false),
            (EnemyKind.Snail, "Garden snail", 0.5f, 0.4f, false), (EnemyKind.LadybugBot, "Ladybug bot", 0.45f, 0.35f, false),
            (EnemyKind.Frog, "Pond frog", 0.5f, 0.4f, false)
        ];
        foreach (var critter in critters)
            yield return new EnemyArchetype(critter.Kind, critter.Name, new Vector2(critter.W, critter.H), 6f, 0f, critter.Fly)
            {
                Passive = true, Catchable = true, MoveSpeedMultiplier = 0.9f, Hops = !critter.Fly && critter.H >= 0.5f
            };
        yield return new EnemyArchetype(EnemyKind.MegaFauna, "Mega-fauna grazer", new(2.4f, 2f), 160f, 0f, false)
            { Passive = true, MoveSpeedMultiplier = 0.5f };

        // ---- Hostile creatures. (kind, name, w, h, hp, dmg, flying, credits) ----
        (EnemyKind Kind, string Name, float W, float H, float Hp, float Dmg, bool Fly, int Credits)[] hostiles =
        [
            // The oceans (the zone id keeps its old AcidCoast name).
            (EnemyKind.AcidSkitterer, "Brine skitterer", 0.9f, 0.6f, 40f, 11f, false, 3),
            (EnemyKind.AcidShark, "Saltjaw shark", 1.8f, 0.8f, 120f, 22f, true, 8),
            (EnemyKind.TideLeech, "Tide leech", 1f, 0.5f, 50f, 12f, false, 3),
            (EnemyKind.ReefCrawler, "Reef crawler", 1.1f, 0.7f, 70f, 14f, false, 4),
            (EnemyKind.BrineGolem, "Brine golem", 1.5f, 1.9f, 200f, 24f, false, 12),
            (EnemyKind.SpittingBarnacle, "Spitting barnacle", 0.9f, 0.9f, 60f, 10f, false, 4),
            (EnemyKind.StormRay, "Storm ray", 1.4f, 0.6f, 60f, 15f, true, 5),
            (EnemyKind.DrownedDiver, "Drowned diver", 0.9f, 1.7f, 90f, 18f, false, 6),
            (EnemyKind.CoralSentinel, "Coral sentinel", 1.2f, 1.6f, 160f, 20f, false, 10),
            // Irradiated Core natives (theme pass).
            (EnemyKind.CoreHusk, "Irradiated husk", 1.2f, 2f, 210f, 24f, false, 12),
            (EnemyKind.FissionWisp, "Fission wisp", 0.9f, 0.9f, 70f, 16f, true, 6),
            // Fungal Hollows.
            (EnemyKind.SporeZombie, "Spore husk", 0.9f, 1.7f, 70f, 15f, false, 4),
            (EnemyKind.FungalCrawler, "Fungal crawler", 1f, 0.6f, 50f, 12f, false, 3),
            (EnemyKind.PuffDrifter, "Puff drifter", 0.9f, 0.9f, 40f, 10f, true, 3),
            (EnemyKind.MycelidShaman, "Mycelid shaman", 0.9f, 1.6f, 90f, 16f, false, 7),
            (EnemyKind.ShroomStalker, "Shroom stalker", 1f, 1.8f, 110f, 19f, false, 7),
            (EnemyKind.CapTurtle, "Cap turtle", 1.3f, 0.9f, 140f, 14f, false, 6),
            (EnemyKind.LanternBat, "Lantern bat", 0.8f, 0.6f, 35f, 10f, true, 2),
            (EnemyKind.SporeGolem, "Spore golem", 1.6f, 2f, 240f, 26f, false, 14),
            // Crystal Spires.
            (EnemyKind.PrismKnight, "Prism knight", 1f, 1.8f, 220f, 30f, false, 14),
            (EnemyKind.ShardUnicorn, "Shard strider", 1.8f, 1.5f, 260f, 34f, false, 16),
            (EnemyKind.LightMimic, "Light mimic", 1f, 1f, 200f, 32f, false, 20),
            (EnemyKind.CrystalWraith, "Crystal wraith", 1f, 1.5f, 160f, 28f, true, 12),
            (EnemyKind.RefractorTurret, "Refractor turret", 1f, 1.2f, 180f, 20f, false, 12),
            (EnemyKind.HaloBat, "Halo bat", 0.8f, 0.6f, 90f, 22f, true, 8),
            (EnemyKind.PearlGolem, "Pearl golem", 1.6f, 2f, 360f, 36f, false, 20),
            (EnemyKind.SpireDragonfly, "Spire glassfly", 1.1f, 0.5f, 110f, 24f, true, 9),
            // Verdant Canopy and the Bloom Hives.
            (EnemyKind.PollenDroneSwarm, "Pollen drone swarm", 0.9f, 0.7f, 45f, 11f, true, 3),
            (EnemyKind.ThornLurker, "Thorn lurker", 1.1f, 1f, 90f, 18f, false, 6),
            (EnemyKind.CanopyApe, "Canopy ape", 1.4f, 1.6f, 140f, 21f, false, 8),
            (EnemyKind.SnapjawPlant, "Snapjaw plant", 1f, 1.4f, 120f, 22f, false, 7),
            (EnemyKind.HiveGuard, "Hive guard", 1f, 0.9f, 80f, 17f, true, 6),
            (EnemyKind.MudCrawler, "Mud crawler", 1.1f, 0.6f, 60f, 13f, false, 4),
            (EnemyKind.SpittingOrchid, "Spitting orchid", 0.9f, 1.2f, 70f, 14f, false, 5),
            (EnemyKind.JungleStalker, "Jungle stalker", 1.2f, 1.2f, 110f, 20f, false, 8),
            // Precursor Vault and mini-biomes.
            (EnemyKind.VaultSentinel, "Vault sentinel", 1.2f, 2f, 320f, 34f, false, 20),
            (EnemyKind.GlyphDrone, "Glyph drone", 0.9f, 0.9f, 160f, 26f, true, 14),
            (EnemyKind.AncientGuardian, "Ancient guardian", 1.6f, 2.4f, 480f, 40f, false, 30),
            (EnemyKind.GeodeCrab, "Geode crab", 1.1f, 0.8f, 80f, 14f, false, 8),
            (EnemyKind.AlabasterHoplite, "Alabaster hoplite", 1f, 1.8f, 120f, 18f, false, 8),
            (EnemyKind.MiteBroodmother, "Mite broodmother", 1.8f, 1.2f, 200f, 22f, false, 12),
            (EnemyKind.SilkSpiderMite, "Silk spider-mite", 1f, 0.6f, 60f, 14f, false, 4),
            // Wandering threats.
            (EnemyKind.NeoTerraReaver, "Neo-Terra reaver", 1.4f, 1.8f, 260f, 30f, false, 15),
            (EnemyKind.TerraformerDrone, "Terraformer drone", 1.2f, 1f, 180f, 24f, true, 12),
            // Event creatures (invasions, moons, storms).
            (EnemyKind.EventGrunt, "Raider grunt", 0.9f, 1.7f, 60f, 13f, false, 4),
            (EnemyKind.EventScout, "Raider scout", 0.8f, 1.6f, 45f, 11f, false, 3),
            (EnemyKind.EventMarksman, "Raider marksman", 0.9f, 1.7f, 55f, 16f, false, 5),
            (EnemyKind.EventBrute, "Raider brute", 1.3f, 2f, 160f, 24f, false, 9),
            (EnemyKind.EventCommander, "Raider commander", 1f, 1.9f, 220f, 26f, false, 18),
            (EnemyKind.EventDrone, "Strike drone", 0.8f, 0.7f, 50f, 12f, true, 4),
            (EnemyKind.EventSwarmer, "Swarmer", 0.7f, 0.5f, 30f, 9f, true, 2),
            (EnemyKind.EventEnergyVortex, "Energy vortex", 1f, 1f, 90f, 18f, true, 6),
            (EnemyKind.EventRogueTerraformer, "Rogue terraformer", 1.5f, 1.6f, 200f, 26f, false, 12),
            (EnemyKind.EventVoidSiphon, "Void siphon", 1f, 1.4f, 140f, 22f, true, 9),
            (EnemyKind.EventCorruptedSurvivor, "Corrupted survivor", 0.9f, 1.7f, 80f, 15f, false, 5),
            (EnemyKind.EventAbomination, "Lesser abomination", 1.6f, 1.6f, 240f, 28f, false, 14),
            (EnemyKind.EventHusk, "Rust husk", 0.9f, 1.7f, 70f, 15f, false, 4),
            (EnemyKind.EventRustWraith, "Rust wraith", 0.9f, 1.4f, 80f, 17f, true, 5),
            (EnemyKind.EventRustHound, "Rust hound", 1.3f, 0.9f, 70f, 16f, false, 5),
            (EnemyKind.EventEyeDrone, "Eye drone", 0.8f, 0.8f, 50f, 12f, true, 4),
            (EnemyKind.EventCorrodedMech, "Corroded mech", 1.5f, 2f, 260f, 28f, false, 14),
            (EnemyKind.EventSwarmQueen, "Swarm queen", 1.6f, 1.2f, 300f, 24f, true, 20),
            (EnemyKind.EventDustDevil, "Dust devil", 1f, 1.6f, 60f, 12f, true, 3),
            (EnemyKind.EventSandRay, "Sand ray", 1.3f, 0.6f, 55f, 13f, true, 4),
            (EnemyKind.EventScrapElemental, "Scrap elemental", 1.4f, 1.8f, 180f, 22f, false, 10),
            (EnemyKind.EventDustRaider, "Dust raider", 0.9f, 1.7f, 65f, 14f, false, 5),
            (EnemyKind.EventAcidSlime, "Acid slime", 0.9f, 0.7f, 45f, 12f, false, 3),
            (EnemyKind.EventAcidBird, "Acid bird", 0.7f, 0.5f, 35f, 10f, true, 3),
            (EnemyKind.EventToxicCloud, "Toxic cloud", 1.2f, 1f, 50f, 9f, true, 3),
            (EnemyKind.EventAuroraWisp, "Aurora wisp", 0.7f, 0.7f, 20f, 0f, true, 2),
            (EnemyKind.EventFestivalBalloonDrone, "Festival balloon drone", 0.8f, 1f, 15f, 0f, true, 6),
            (EnemyKind.EventMeteorHead, "Meteor head", 1f, 1f, 60f, 16f, true, 5),
            (EnemyKind.EventMeteorCrawler, "Meteor crawler", 1f, 0.6f, 70f, 15f, false, 5),
            (EnemyKind.EventEclipseShade, "Eclipse shade", 0.9f, 1.6f, 180f, 28f, true, 12),
            (EnemyKind.EventEclipseReaper, "Eclipse reaper", 1.2f, 2f, 320f, 36f, false, 20),
            (EnemyKind.EventEclipseMothMech, "Eclipse moth-mech", 1.6f, 1.2f, 280f, 30f, true, 18),
            (EnemyKind.EventScavengerGrunt, "Scavenger grunt", 0.9f, 1.7f, 70f, 14f, false, 5),
            (EnemyKind.EventScavengerBrute, "Scavenger brute", 1.3f, 2f, 180f, 25f, false, 10),
            (EnemyKind.EventScavengerSniper, "Scavenger sniper", 0.9f, 1.7f, 60f, 20f, false, 7),
            (EnemyKind.EventScavengerRigger, "Scavenger rigger", 0.9f, 1.7f, 90f, 16f, false, 7),
            (EnemyKind.EventPirateDeckhand, "Pirate deckhand", 0.9f, 1.7f, 140f, 22f, false, 9),
            (EnemyKind.EventPirateGunner, "Pirate gunner", 0.9f, 1.7f, 130f, 26f, false, 10),
            (EnemyKind.EventPirateParrotDrone, "Parrot drone", 0.7f, 0.6f, 70f, 16f, true, 6),
            (EnemyKind.EventPirateCannon, "Boarding cannon", 1.4f, 1.2f, 260f, 30f, false, 16),
            (EnemyKind.EventProtocolGrunt, "Protocol grunt", 0.9f, 1.7f, 90f, 16f, false, 5),
            (EnemyKind.EventProtocolShieldbearer, "Protocol shieldbearer", 1.1f, 1.9f, 200f, 18f, false, 8),
            (EnemyKind.EventProtocolWyvern, "Protocol wyvern", 1.6f, 1f, 160f, 22f, true, 9),
            (EnemyKind.EventProtocolOgre, "Protocol ogre", 1.8f, 2.4f, 420f, 34f, false, 18),
            (EnemyKind.EventProtocolMage, "Protocol mage", 0.9f, 1.7f, 120f, 26f, false, 10),
            (EnemyKind.EventPrecursorProbe, "Precursor probe", 1f, 0.8f, 200f, 28f, true, 14),
            (EnemyKind.EventProbeGunner, "Probe gunner", 1f, 1.8f, 260f, 32f, false, 16),
            (EnemyKind.EventProbeTeslaWalker, "Tesla walker", 1.6f, 2.2f, 420f, 38f, false, 22),
            (EnemyKind.EventProbeBrainDrone, "Brain drone", 1f, 1f, 240f, 30f, true, 18)
        ];
        foreach (var hostile in hostiles)
            yield return new EnemyArchetype(hostile.Kind, hostile.Name, new Vector2(hostile.W, hostile.H), hostile.Hp, hostile.Dmg, hostile.Fly)
            {
                CreditDrop = hostile.Credits,
                Passive = hostile.Dmg <= 0f,
                Stationary = hostile.Kind is EnemyKind.SpittingBarnacle or EnemyKind.RefractorTurret or EnemyKind.SnapjawPlant
                    or EnemyKind.SpittingOrchid or EnemyKind.EventPirateCannon,
                Hops = hostile.Kind is EnemyKind.EventAcidSlime or EnemyKind.CaveFrog or EnemyKind.FungalCrawler,
                MoveSpeedMultiplier = hostile.W >= 1.5f ? 0.75f : 1.1f,
                Family = hostile.Kind switch
                {
                    EnemyKind.LightMimic => EnemyAiFamily.Mimic,
                    EnemyKind.MycelidShaman or EnemyKind.EventProtocolMage => EnemyAiFamily.Caster,
                    EnemyKind.StormRay => EnemyAiFamily.DivingFlyer,
                    _ => EnemyAiFamily.Default
                }
            };

        // ---- Batch B AI-family creatures ----
        yield return new EnemyArchetype(EnemyKind.TunnelWyrm, "Tunnel wyrm", new(1.1f, 1.1f), 260f, 18f, false)
            { Family = EnemyAiFamily.Worm, SegmentCount = 9, Underground = true, CreditDrop = 14, ContactCooldown = 1f, MoveSpeedMultiplier = 1f };
        yield return new EnemyArchetype(EnemyKind.DuneBorer, "Dune borer", new(0.8f, 0.8f), 110f, 12f, false)
            { Family = EnemyAiFamily.Worm, SegmentCount = 6, CreditDrop = 6, ContactCooldown = 1f, MoveSpeedMultiplier = 1.15f };
        yield return new EnemyArchetype(EnemyKind.RiftCaster, "Rift caster", new(0.9f, 1.7f), 80f, 14f, false)
            { Family = EnemyAiFamily.Caster, Underground = true, CreditDrop = 8, MoveSpeedMultiplier = 0.6f };
        yield return new EnemyArchetype(EnemyKind.DiveKite, "Dive kite", new(1f, 0.6f), 48f, 13f, true)
            { Family = EnemyAiFamily.DivingFlyer, CreditDrop = 4 };
        yield return new EnemyArchetype(EnemyKind.CacheMimic, "Cache mimic", new(1f, 1f), 300f, 26f, false)
            { Family = EnemyAiFamily.Mimic, Underground = true, CreditDrop = 60, ContactCooldown = 1f, MoveSpeedMultiplier = 1.5f };
        yield return new EnemyArchetype(EnemyKind.GelHopper, "Gel hopper", new(0.9f, 0.65f), 30f, 9f, false)
            { Family = EnemyAiFamily.Hopper, Hops = true, CreditDrop = 1, ContactCooldown = 1.2f };
        yield return new EnemyArchetype(EnemyKind.BoulderHopper, "Boulder hopper", new(1.2f, 1f), 85f, 15f, false)
            { Family = EnemyAiFamily.LeapingHopper, Hops = true, CreditDrop = 4, ContactCooldown = 1.4f };
        yield return new EnemyArchetype(EnemyKind.FleaHopper, "Flea hopper", new(0.5f, 0.4f), 18f, 6f, false)
            { Family = EnemyAiFamily.SkitteringHopper, Hops = true, CreditDrop = 1, ContactCooldown = 0.9f };

        // ---- Bosses ----
        yield return new EnemyArchetype(EnemyKind.SporeMonarch, "Spore Monarch", new(2.6f, 2.6f), 900f, 20f, true,
            BossTier.Easy, ProjectileSpeed: 8f, VolleyCount: 4) { CreditDrop = 80 };
        yield return new EnemyArchetype(EnemyKind.BloomMatriarch, "Bloom Matriarch", new(2.8f, 2.4f), 1800f, 26f, true,
            BossTier.Hard, ProjectileSpeed: 10f, VolleyCount: 5) { CreditDrop = 150, MoveSpeedMultiplier = 1.2f };
        yield return new EnemyArchetype(EnemyKind.PrismEmpress, "Prism Empress", new(2.6f, 3.2f), 2600f, 32f, true,
            BossTier.Hard, ProjectileSpeed: 14f, VolleyCount: 7) { CreditDrop = 220, AttackRange = 14f };
        yield return new EnemyArchetype(EnemyKind.VaultColossus, "Vault Colossus", new(3.6f, 4.2f), 3400f, 38f, false,
            BossTier.Hard, ProjectileSpeed: 12f, VolleyCount: 5, PhaseThreshold: 0.4f) { CreditDrop = 260, AttackRange = 14f, MoveSpeedMultiplier = 0.7f };
        yield return new EnemyArchetype(EnemyKind.MoonEater, "Moon Eater", new(3.2f, 2.4f), 1600f, 28f, true,
            BossTier.Event, ProjectileSpeed: 11f, VolleyCount: 4) { CreditDrop = 140 };
        yield return new EnemyArchetype(EnemyKind.EclipseLord, "Eclipse Lord", new(2.6f, 3.4f), 2200f, 34f, true,
            BossTier.Event, ProjectileSpeed: 14f, VolleyCount: 6) { CreditDrop = 200 };
        yield return new EnemyArchetype(EnemyKind.ScavengerWarlord, "Scavenger Warlord", new(1.8f, 2.6f), 1100f, 24f, false,
            BossTier.Event, ProjectileSpeed: 12f, VolleyCount: 3) { CreditDrop = 120, MoveSpeedMultiplier = 1.1f };
        yield return new EnemyArchetype(EnemyKind.PirateDreadnought, "Pirate Dreadnought", new(4.4f, 2.6f), 2600f, 34f, true,
            BossTier.Event, ProjectileSpeed: 14f, VolleyCount: 6) { CreditDrop = 240, AttackRange = 15f };
        yield return new EnemyArchetype(EnemyKind.ProtocolDragon, "Protocol Dragon", new(4f, 2.4f), 2800f, 36f, true,
            BossTier.Event, ProjectileSpeed: 13f, VolleyCount: 5) { CreditDrop = 260 };
        yield return new EnemyArchetype(EnemyKind.BloodfinAmbusher, "Bloodfin ambusher", new(1.3f, 0.9f), 140f, 22f, false)
            { CreditDrop = 10, MoveSpeedMultiplier = 1.3f };
        yield return new EnemyArchetype(EnemyKind.VengefulWisp, "Vengeful wisp", new(0.9f, 1.2f), 120f, 20f, true)
            { CreditDrop = 8 };
        yield return new EnemyArchetype(EnemyKind.AbyssLeviathan, "Abyss Leviathan", new(5.2f, 2.2f), 3000f, 36f, true,
            BossTier.Event, ProjectileSpeed: 13f, VolleyCount: 5) { CreditDrop = 280, AttackRange = 15f };
        // ---- Hardmode progression ----
        yield return new EnemyArchetype(EnemyKind.FissionTyrant, "Fission Tyrant", new(3f, 3.4f), 1150f, 24f, false,
            BossTier.Gate, ProjectileSpeed: 10f, VolleyCount: 4, PhaseThreshold: 0.45f) { CreditDrop = 120, AttackRange = 13f, MoveSpeedMultiplier = 0.8f };
        yield return new EnemyArchetype(EnemyKind.VoidCantor, "Void Cantor", new(2f, 3f), 3200f, 36f, true,
            BossTier.Hard, ProjectileSpeed: 13f, VolleyCount: 6, PhaseThreshold: 0.5f) { CreditDrop = 280, AttackRange = 15f, MoveSpeedMultiplier = 1.1f };
        foreach (var (kind, name) in new[]
                 {
                     (EnemyKind.EmberObelisk, "Ember obelisk"), (EnemyKind.RiftObelisk, "Rift obelisk"),
                     (EnemyKind.GaleObelisk, "Gale obelisk"), (EnemyKind.ShardObelisk, "Shard obelisk")
                 })
            yield return new EnemyArchetype(kind, name, new(2.2f, 5f), 4200f, 30f, false)
                { Stationary = true, CreditDrop = 200, ContactCooldown = 1.2f };
        yield return new EnemyArchetype(EnemyKind.MothershipCore, "Mothership Core", new(4.6f, 3f), 3200f, 38f, true,
            BossTier.Event, ProjectileSpeed: 15f, VolleyCount: 8) { CreditDrop = 300, AttackRange = 16f };
        // ---- Content fill: critters, gold variants and biome hostiles (data in Systems/Content/ContentFillData.cs) ----
        foreach (var archetype in Rimtera.Systems.ContentFillCreatures.Archetypes())
            yield return archetype;
        // ---- Creature coverage: biome variants, event units, mini-bosses and bosses (Systems/Content/CreatureCoverageData.cs) ----
        foreach (var archetype in Rimtera.Systems.CreatureCoverage.Archetypes())
            yield return archetype;
    }
}

/// <summary>
/// Creature stat tables applied once, when the roster is built (EN-4, IC-10, EN-5): the defense tier table, the
/// explicit affliction immunities and lava immunity. A value set in a roster (hand table, generator data) always wins.
/// <para>Defense tiers (docs/roadmap/0.1.9.md, A1):</para>
/// <list type="bullet">
/// <item>Pre-boss 0-6 (gel 2, walker 6): pre-Fracture creatures under 100 hit points.</item>
/// <item>Mid pre-Fracture 8-14: tougher pre-Fracture creatures, by hit points.</item>
/// <item>Post-Fracture 16-30: creatures that only spawn after the Fracture, by hit points.</item>
/// <item>Late 30-50: the post-Mutated-Flora dungeon, the Dark Sun, the two late moons and the obelisk sieges.</item>
/// <item>Bosses: the defense band of the Terraria bosses each tier stands for (Eye of Cthulhu, Skeletron, Deerclops ~10;
/// Wall of Flesh 12; mechanical bosses, Plantera, Golem, Queen Slime ~26; pre-hardmode event leaders ~16; Pumpking,
/// Ice Queen, Martian Saucer, Mothron ~30; Moon Lord 50).</item>
/// </list>
/// </summary>
public static class CreatureStatTable
{
    public static readonly IReadOnlySet<StatusId> NoImmunities = new HashSet<StatusId>();

    /// <summary>
    /// Kinds whose stats were authored for the Fracture: the post-Fracture rows of the generated content fill and of the
    /// creature coverage pass. Their numbers are final; the hardmode rescale skips them (D-18).
    /// </summary>
    private static readonly HashSet<EnemyKind> FractureAuthoredKinds =
        ContentFillData.Hostiles.Where(row => row.Hard).Select(row => row.Kind)
            .Concat(CreatureCoverageData.Creatures.Where(row => row.Hard).Select(row => row.Kind)).ToHashSet();

    /// <summary>
    /// Hand-roster kinds that only spawn after the Fracture (hard biome rosters, the Fracture-gated Dark Sun, pirate
    /// boarding and precursor probes, hardmode event upgrades). They take the post-Fracture defense tier; their hit
    /// points were authored on the pre-Fracture scale, so they still take the hardmode rescale.
    /// </summary>
    private static readonly HashSet<EnemyKind> HandPostFractureKinds =
    [
        EnemyKind.NeoTerraReaver, EnemyKind.TerraformerDrone, EnemyKind.PearlGolem, EnemyKind.CacheMimic, EnemyKind.BasaltGolem,
        EnemyKind.EventAbomination, EnemyKind.EventCorruptedSurvivor, EnemyKind.EventVoidSiphon,
        EnemyKind.EventEclipseShade, EnemyKind.EventEclipseReaper, EnemyKind.EventEclipseMothMech,
        EnemyKind.EventPirateDeckhand, EnemyKind.EventPirateGunner, EnemyKind.EventPirateParrotDrone, EnemyKind.EventPirateCannon,
        EnemyKind.EventPrecursorProbe, EnemyKind.EventProbeGunner, EnemyKind.EventProbeTeslaWalker, EnemyKind.EventProbeBrainDrone
    ];

    /// <summary>Late-tier coverage slots: the post-Mutated-Flora dungeon, the Dark Sun, the late moons and the obelisk sieges.</summary>
    private static readonly HashSet<string> LateSlots =
    [
        "dungeon-hard", "event:DarkSun", "event:HollowMoon", "event:RimeMoon",
        "event:EmberObelisk", "event:GaleObelisk", "event:RiftObelisk", "event:ShardObelisk"
    ];

    private static readonly HashSet<EnemyKind> LateKinds =
        CreatureCoverageData.Creatures.Where(row => LateSlots.Contains(row.Slot)).Select(row => row.Kind)
            .Concat([EnemyKind.EmberObelisk, EnemyKind.RiftObelisk, EnemyKind.GaleObelisk, EnemyKind.ShardObelisk]).ToHashSet();

    /// <summary>Defense written into the generated content-fill rows (tools/sprites/content_fill_gen.py).</summary>
    private static readonly Dictionary<EnemyKind, int> GeneratedDefense =
        ContentFillData.Hostiles.ToDictionary(row => row.Kind, row => row.Defense);

    /// <summary>Hand-roster defense overrides: Terraria's zombie ("walker") sits at 6 even at low hit points.</summary>
    private static readonly Dictionary<EnemyKind, int> HandDefense = new()
    {
        [EnemyKind.SporeZombie] = 6, [EnemyKind.EventHusk] = 6, [EnemyKind.SlickerHusk] = 6, [EnemyKind.TombHusk] = 6,
        [EnemyKind.EventDustRaider] = 6, [EnemyKind.EventScavengerGrunt] = 6,
        // Boss analog: the Fission Tyrant stands for the Wall of Flesh (12).
        [EnemyKind.FissionTyrant] = 12
    };

    /// <summary>Leaders of Fracture-gated events (Dark Sun, pirate boarding, probes, the late moons): the ~30 band.</summary>
    private static readonly HashSet<EnemyKind> LateEventBosses =
        [EnemyKind.EclipseLord, EnemyKind.PirateDreadnought, EnemyKind.MothershipCore, EnemyKind.HollowRegent, EnemyKind.RimeSeraph];

    /// <summary>The Irradiated Core's natives: liquid uranium is their home (lava-immune).</summary>
    private static readonly HashSet<EnemyKind> CoreNatives =
        new[]
        {
            EnemyKind.PyroSkitter, EnemyKind.MagmaTurtle, EnemyKind.LavaLeech, EnemyKind.EmberRat, EnemyKind.BasaltGolem,
            EnemyKind.CoreHusk, EnemyKind.FissionWisp
        }.Concat(ContentFillData.Hostiles.Where(row => row.Roster == "core").Select(row => row.Kind)).ToHashSet();

    /// <summary>True when the kind's stats were authored for the Fracture (the hardmode rescale skips it, D-18).</summary>
    public static bool IsFractureAuthored(EnemyKind kind) => FractureAuthoredKinds.Contains(kind);

    /// <summary>True when the kind only spawns after the Fracture (authored for it, or a hand-roster hard-only kind).</summary>
    public static bool IsPostFracture(EnemyKind kind) => FractureAuthoredKinds.Contains(kind) || HandPostFractureKinds.Contains(kind);

    /// <summary>True for the late tier (post-Mutated-Flora dungeon, Dark Sun, late moons, obelisk sieges).</summary>
    public static bool IsLate(EnemyKind kind) => LateKinds.Contains(kind);

    /// <summary>The roster archetype with its defense, immunities and lava immunity filled in.</summary>
    public static EnemyArchetype Apply(EnemyArchetype archetype)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        var defense = archetype.Defense > 0 ? archetype.Defense
            : HandDefense.TryGetValue(archetype.Kind, out var hand) ? hand
            : GeneratedDefense.TryGetValue(archetype.Kind, out var generated) && generated > 0 ? generated
            : TierDefense(archetype);
        var immune = new HashSet<StatusId>(archetype.Immune);
        immune.UnionWith(DefaultImmunities(archetype));
        return archetype with
        {
            Defense = defense,
            Immune = immune.Count == 0 ? NoImmunities : immune,
            LavaImmune = archetype.LavaImmune || DefaultLavaImmune(archetype)
        };
    }

    /// <summary>The defense tier table (see the class summary). Wildlife has none.</summary>
    public static int TierDefense(EnemyArchetype archetype)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        if (archetype.Passive)
            return 0;
        if (archetype.IsBoss)
            return archetype.Tier switch
            {
                BossTier.Hard => 26,
                BossTier.Event => LateEventBosses.Contains(archetype.Kind) ? 30 : 16,
                BossTier.Final => 50,
                _ => 10
            };
        var hp = archetype.HitPoints;
        if (IsLate(archetype.Kind))
            return hp switch { < 300f => 30, < 400f => 34, < 500f => 38, < 700f => 42, < 1200f => 46, _ => 50 };
        if (IsPostFracture(archetype.Kind))
            return hp switch { < 200f => 16, < 250f => 18, < 300f => 20, < 350f => 22, < 400f => 24, < 500f => 26, < 1000f => 28, _ => 30 };
        return hp switch
        {
            < 45f => 2, < 60f => 4, < 100f => 6,              // pre-boss: gel 2 … walker 6
            < 150f => 8, < 250f => 10, < 400f => 12, _ => 14 // mid pre-Fracture
        };
    }

    // ---- immunities (EN-5): the old name keywords, with their oddities fixed ----
    private static bool Has(string name, params string[] words) => words.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Default immunities. Machines (droids, drones, mechs, golems, sentinels, turrets, cannons, tanks, obelisks) resist
    /// poison; fire-born creatures burning; frost-born frostbite; bosses, obelisks and turrets confusion.
    /// Decisions: the Glow firefly (and every other firefly) can burn: the old "fire" keyword caught it by accident.
    /// The Pollen drone swarm is immune to Poison: it is a swarm of pollinator drones, machines like every other drone.
    /// "Ice" alone no longer counts (it caught the Lattice weaver); icicle, glacier and blizzard creatures do.
    /// </summary>
    public static IEnumerable<StatusId> DefaultImmunities(EnemyArchetype archetype)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        var name = archetype.DisplayName + " " + archetype.Kind;
        if (Has(name, "droid", "drone", "mech", "golem", "sentinel", "obelisk", "turret", "construct", "warship", "cannon", "tank"))
            yield return StatusId.Poison;
        if (FireBorn(name))
            yield return StatusId.Burning;
        if (Has(name, "frost", "glacial", "glacier", "icicle", "snow", "cryo", "rime", "blizzard", "flurry", "whiteout", "tundra"))
            yield return StatusId.Frostbite;
        if (archetype.IsBoss || Has(name, "obelisk", "turret"))
            yield return StatusId.Confused;
    }

    private static bool FireBorn(string name) =>
        Has(name, "ember", "magma", "pyro", "pyre", "flare", "solar", "lava", "molten", "cinder", "slag", "inferno", "blaze") ||
        Has(name.Replace("firefly", "", StringComparison.OrdinalIgnoreCase), "fire");

    /// <summary>
    /// Lava immunity: fire-born creatures, the Irradiated Core's natives (the Irradiated husk lives in liquid uranium),
    /// bosses, the obelisks and the Dungeon Guardian.
    /// </summary>
    public static bool DefaultLavaImmune(EnemyArchetype archetype)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        return archetype.IsBoss || archetype.Kind == EnemyKind.DungeonGuardian || CoreNatives.Contains(archetype.Kind) ||
               archetype.Kind.ToString().Contains("Obelisk", StringComparison.Ordinal) ||
               FireBorn(archetype.DisplayName + " " + archetype.Kind);
    }
}
