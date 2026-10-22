using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The content-expansion item catalog (CONTENT_EXPANSION.md §2): seven progression tiers, four combat
/// classes, ammunition, accessories, tools, armor sets, stations, summons, critters, blocks, furniture
/// and walls. Every name and number here is original Rimtera data.
/// </summary>
internal static class ContentItems
{
    /// <summary>The tier bar each progression tier is built from.</summary>
    public static readonly IReadOnlyDictionary<int, ItemId> TierBars = new Dictionary<int, ItemId>
    {
        [1] = ItemId.FerriteBar, [2] = ItemId.CobaltBar, [3] = ItemId.PyrociteBar, [4] = ItemId.ViridiumBar,
        [5] = ItemId.LumeniteBar, [6] = ItemId.VoidsteelBar, [7] = ItemId.AstralBar
    };

    public static void RegisterAll(Dictionary<ItemId, ItemDefinition> items)
    {
        var builder = new Builder(items);
        builder.Materials();
        builder.Weapons();
        builder.Ammunition();
        builder.Accessories();
        builder.Tools();
        builder.Armor();
        builder.Placeables();
        builder.Summons();
        builder.Critters();
        builder.Fishing();
    }

    public static ItemRarity RarityFor(int tier) => (ItemRarity)Math.Clamp(tier - 1, 0, 6);

    private sealed class Builder(Dictionary<ItemId, ItemDefinition> items)
    {
        private void Add(ItemDefinition definition) => items[definition.Id] = definition;

        private static int WeaponValue(int tier) => 25 + 30 * tier * tier;

        // ------------------------------------------------------------ materials

        public void Materials()
        {
            void Mat(ItemId id, string name, int tier, int value, int stack = 999) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Material, stack)
                    { IsMaterial = true, Tier = tier, Rarity = RarityFor(tier), Value = value });

            Mat(ItemId.Credits, "Credit chip", 0, 1, 99999);
            Mat(ItemId.FerriteBar, "Ferrite bar", 1, 6);
            Mat(ItemId.CobaltBar, "Cobalt bar", 2, 14);
            Mat(ItemId.GlacialBar, "Glacial bar", 3, 24);
            Mat(ItemId.PyrociteBar, "Pyrocite bar", 3, 26);
            Mat(ItemId.UraniumRod, "Uranium rod", 3, 26);
            Mat(ItemId.ViridiumBar, "Viridium bar", 4, 40);
            Mat(ItemId.LumeniteBar, "Lumenite bar", 5, 60);
            Mat(ItemId.VoidsteelBar, "Voidsteel bar", 6, 90);
            Mat(ItemId.AstralBar, "Astral bar", 7, 140);
            Mat(ItemId.MeteoriteBar, "Meteorite bar", 3, 30);
            Mat(ItemId.LumeniteOre, "Lumenite ore", 5, 15);
            Mat(ItemId.VoidsteelOre, "Voidsteel ore", 6, 22);
            Mat(ItemId.MeteoriteOre, "Meteorite ore", 3, 8);
            Mat(ItemId.AstralOre, "Astral ore", 7, 40);
            Mat(ItemId.RubyShard, "Ruby shard", 2, 20);
            Mat(ItemId.SapphireShard, "Sapphire shard", 2, 20);
            Mat(ItemId.EmeraldShard, "Emerald shard", 2, 22);
            Mat(ItemId.AmethystShard, "Amethyst shard", 2, 18);
            Mat(ItemId.TopazShard, "Topaz shard", 2, 18);
            Mat(ItemId.DiamondShard, "Diamond shard", 3, 40);
            Mat(ItemId.PowerCell, "Power cell", 2, 12);
            Mat(ItemId.BioGel, "Bio-gel", 1, 3);
            Mat(ItemId.OverseerCircuit, "Overseer circuit", 3, 50);
            Mat(ItemId.HeartFragment, "Blight heart fragment", 4, 45);
            // Dropped by the Fission Tyrant (the Fracture boss); the Alloy Anvil needs it (G4).
            Mat(ItemId.FissionSlag, "Fission slag", 4, 60);
            Mat(ItemId.VaultKey, "Precursor vault key", 5, 200, 9);
            Mat(ItemId.DroneSpareParts, "Drone spare parts", 2, 10);
            Mat(ItemId.SilkThread, "Mite silk thread", 1, 2);
            Mat(ItemId.ClothRoll, "Cloth roll", 1, 8);
            Mat(ItemId.CircuitBoard, "Circuit board", 2, 16);
            Mat(ItemId.AncientPowerCell, "Ancient power cell", 5, 120);
            Mat(ItemId.PrecursorRelic, "Precursor relic", 5, 300, 99);
            Mat(ItemId.CoralShard, "Coral shard", 2, 6);
            Mat(ItemId.GlowSpore, "Glow spore", 2, 5);
            Mat(ItemId.PrismShard, "Prism shard", 5, 18);
            Mat(ItemId.VoidEssence, "Void essence", 6, 30);
            Mat(ItemId.RopeCoil, "Rope coil", 1, 4, 99);
            Add(new ItemDefinition(ItemId.MossBlock, "Moss clump", ItemUseKind.PlaceTile, 999)
                { PlacedTile = TileId.MossBlue, IsMaterial = true, Value = 1 });
        }

        // ------------------------------------------------------------ weapons

        private void Melee(ItemId id, string name, int tier, int damage, int cooldown, float knockback = 2.5f,
            int crit = 4, float reach = 0f, bool thrown = false, float speed = 0f, int pierce = 0) =>
            Add(new ItemDefinition(id, name, ItemUseKind.Melee, 1)
            {
                Class = DamageClass.Melee, Tier = tier, Rarity = RarityFor(tier), Value = WeaponValue(tier),
                Damage = GearCurve.RescaleAuthoredDamage(DamageClass.Melee, tier, damage), UseCooldownTicks = GearCurve.RescaleAuthoredUseTime(DamageClass.Melee, tier, cooldown), Knockback = knockback, CritChance = crit,
                Reach = reach, Thrown = thrown, ProjectileSpeed = speed, Pierce = pierce,
                ProjectileLifetime = thrown ? 0.8f : 3f
            });

        private void Gun(ItemId id, string name, int tier, int damage, int cooldown, ItemId ammo, float speed,
            float knockback = 1.5f, int count = 1, float spread = 0.12f, float life = 3f, int pierce = 0, int crit = 4) =>
            Add(new ItemDefinition(id, name, ItemUseKind.Ranged, 1)
            {
                Class = DamageClass.Ranged, Tier = tier, Rarity = RarityFor(tier), Value = WeaponValue(tier),
                Damage = GearCurve.RescaleAuthoredDamage(DamageClass.Ranged, tier, damage), UseCooldownTicks = GearCurve.RescaleAuthoredUseTime(DamageClass.Ranged, tier, cooldown), Knockback = knockback, AmmoType = ammo,
                ProjectileSpeed = speed, ProjectileCount = count, ProjectileSpread = spread,
                ProjectileLifetime = life, Pierce = pierce, CritChance = crit
            });

        private void Tech(ItemId id, string name, int tier, int damage, int cooldown, int charge, float speed = 14f,
            int count = 1, float spread = 0.15f, float life = 2.5f, int pierce = 0, float knockback = 1.5f) =>
            Add(new ItemDefinition(id, name, ItemUseKind.Tech, 1)
            {
                Class = DamageClass.Tech, Tier = tier, Rarity = RarityFor(tier), Value = WeaponValue(tier),
                Damage = GearCurve.RescaleAuthoredDamage(DamageClass.Tech, tier, damage), UseCooldownTicks = GearCurve.RescaleAuthoredUseTime(DamageClass.Tech, tier, cooldown), ChargeCost = charge, ProjectileSpeed = speed,
                ProjectileCount = count, ProjectileSpread = spread, ProjectileLifetime = life, Pierce = pierce,
                Knockback = knockback, CritChance = 4
            });

        private void Command(ItemId id, string name, int tier, int damage, MinionKind minion) =>
            Add(new ItemDefinition(id, name, ItemUseKind.Command, 1)
            {
                Class = DamageClass.Command, Tier = tier, Rarity = RarityFor(tier), Value = WeaponValue(tier),
                Damage = GearCurve.RescaleAuthoredDamage(DamageClass.Command, tier, damage), UseCooldownTicks = GearCurve.RescaleAuthoredUseTime(DamageClass.Command, tier, 30), Summons = minion
            });

        private void Whip(ItemId id, string name, int tier, int damage, int cooldown, float reach) =>
            Add(new ItemDefinition(id, name, ItemUseKind.Melee, 1)
            {
                Class = DamageClass.Command, Tier = tier, Rarity = RarityFor(tier), Value = WeaponValue(tier),
                Damage = GearCurve.RescaleAuthoredDamage(DamageClass.Command, tier, damage), UseCooldownTicks = GearCurve.RescaleAuthoredUseTime(DamageClass.Command, tier, cooldown, whip: true), Knockback = 1.2f, Reach = reach, SignalWhip = true
            });

        // Damage and use-time numbers below were authored against the old per-tier bases; the helpers rescale them onto GearCurve.
        public void Weapons()
        {
            // Tier 1: ferrite and scrap.
            Melee(ItemId.FerriteShiv, "Ferrite shiv", 1, 14, 10, 1.8f, crit: 8);
            Melee(ItemId.ScrapCleaver, "Scrap cleaver", 1, 19, 18, 3.2f);
            Melee(ItemId.BoneClub, "Bone club", 1, 16, 16, 3.5f);
            Melee(ItemId.ReturnBoomerang, "Return boomerang", 1, 13, 22, 2f, thrown: true, speed: 12f, pierce: 1);
            Gun(ItemId.ScrapPistol, "Scrap pistol", 1, 8, 12, ItemId.SlugRounds, 15f, 1f);
            Gun(ItemId.FerriteRevolver, "Ferrite revolver", 1, 12, 18, ItemId.SlugRounds, 17f);
            Gun(ItemId.DartBlower, "Dart blower", 1, 9, 16, ItemId.ToxicDarts, 13f, 0.8f);
            Tech(ItemId.SparkEmitter, "Spark emitter", 1, 11, 16, 4, 12f);
            Command(ItemId.ScoutDroneRemote, "Scout drone remote", 1, 8, MinionKind.ScoutDrone);

            // Tier 2: cobalt.
            Melee(ItemId.CobaltSaber, "Cobalt saber", 2, 24, 13, 2.6f);
            Melee(ItemId.ChainFlail, "Chain flail", 2, 28, 24, 4f, reach: 6.5f);
            Melee(ItemId.ShockSpear, "Shock spear", 2, 26, 20, 3f, reach: 7f);
            Melee(ItemId.TetherWhip, "Tether whip", 2, 22, 20, 2f, reach: 8f);
            Melee(ItemId.ThrowingDisc, "Throwing disc", 2, 20, 16, 2f, thrown: true, speed: 15f);
            Melee(ItemId.YoyoDrone, "Yo-yo drone", 2, 18, 12, 1.5f, thrown: true, speed: 10f, pierce: 2);
            Melee(ItemId.LaserScalpel, "Laser scalpel", 2, 18, 8, 1f, crit: 15);
            Melee(ItemId.EnergyCutlassMk2, "Energy cutlass Mk2", 2, 27, 14, 2.8f);
            Gun(ItemId.CobaltCarbine, "Cobalt carbine", 2, 15, 9, ItemId.SlugRounds, 19f);
            Gun(ItemId.NailGun, "Nail gun", 2, 10, 6, ItemId.SlugRounds, 16f, 0.6f);
            Tech(ItemId.ArcGlove, "Arc glove", 2, 18, 12, 5, 16f, life: 0.5f);
            Tech(ItemId.DatapadFlare, "Datapad: Flare", 2, 20, 20, 6, 11f);
            Tech(ItemId.DatapadSonic, "Datapad: Sonic", 2, 14, 18, 7, 13f, count: 3, spread: 0.25f, pierce: 1);
            Command(ItemId.HornetDroneRemote, "Hornet drone remote", 2, 13, MinionKind.HornetDrone);
            Command(ItemId.RepairDroneRemote, "Repair drone remote", 2, 4, MinionKind.RepairDrone);
            Command(ItemId.MechCompanionKey, "Mech companion key", 2, 15, MinionKind.MechCompanion);
            Whip(ItemId.SignalWhipChain, "Signal whip: Chain", 2, 14, 18, 8f);
            Command(ItemId.TurretAutogun, "Autogun turret kit", 2, 12, MinionKind.TurretAutogun);

            // Tier 3: the glacial / pyrocite / uranium trio and biome drops.
            Melee(ItemId.FrostbiteEdge, "Frostbite edge", 3, 36, 14, 2.8f);
            Melee(ItemId.PyrociteMaul, "Pyrocite maul", 3, 46, 26, 6f);
            Melee(ItemId.UraniumGlaive, "Uranium glaive", 3, 38, 18, 3f, reach: 7f);
            Melee(ItemId.MushroomMace, "Mushroom mace", 3, 34, 20, 4f);
            Melee(ItemId.CoralTrident, "Coral trident", 3, 32, 18, 3f, reach: 7f);
            Melee(ItemId.SpikedChain, "Spiked chain", 3, 34, 22, 2.5f, reach: 8f);
            Gun(ItemId.GlacialCrossbow, "Glacial crossbow", 3, 30, 22, ItemId.CryoRounds, 22f, 2f, pierce: 1);
            Gun(ItemId.PyrociteFlamer, "Pyrocite flamer", 3, 9, 4, ItemId.FuelCanister, 9f, 0.2f, life: 0.45f, pierce: 3);
            Gun(ItemId.PumpScattergun, "Pump scattergun", 3, 11, 30, ItemId.ShotgunShells, 16f, 2.5f, count: 5, spread: 0.09f);
            Gun(ItemId.HarpoonGun, "Harpoon gun", 3, 34, 30, ItemId.ExplosiveCharge, 18f, 3f, pierce: 2);
            Gun(ItemId.UraniumRailgun, "Uranium railgun", 3, 42, 40, ItemId.Arrow, 34f, 3f, pierce: 4, crit: 10);
            Tech(ItemId.FrostProjector, "Frost projector", 3, 26, 14, 7, 15f);
            Tech(ItemId.EmberRod, "Ember rod", 3, 30, 16, 8, 13f);
            Tech(ItemId.RadBeamRod, "Rad beam rod", 3, 24, 8, 5, 30f, life: 0.6f, pierce: 3);
            Tech(ItemId.CoralWand, "Coral wand", 3, 27, 18, 7, 13f);
            Tech(ItemId.SporeCenser, "Spore censer", 3, 16, 20, 9, 9f, count: 4, spread: 0.4f);
            Tech(ItemId.DatapadSeeker, "Datapad: Seeker", 3, 28, 22, 9, 12f);
            Command(ItemId.FrostDroneRemote, "Frost drone remote", 3, 20, MinionKind.FrostDrone);
            Command(ItemId.EmberDroneRemote, "Ember drone remote", 3, 22, MinionKind.EmberDrone);
            Command(ItemId.RadSwarmHive, "Rad swarm hive", 3, 14, MinionKind.RadSwarm);
            Command(ItemId.CoralJellyFamiliar, "Coral jelly familiar", 3, 20, MinionKind.CoralJelly);
            Command(ItemId.SporeFamiliar, "Spore familiar", 3, 18, MinionKind.SporeFamiliar);
            Command(ItemId.TurretFlame, "Flame turret kit", 3, 9, MinionKind.TurretFlame);

            // Tier 4: viridium.
            Melee(ItemId.ViridiumKatana, "Viridium katana", 4, 52, 12, 3f, crit: 8);
            Melee(ItemId.ChainsawSword, "Chainsaw sword", 4, 30, 5, 1f);
            Melee(ItemId.HiveStingerRapier, "Hive stinger rapier", 4, 44, 9, 2f, crit: 12);
            Gun(ItemId.ViridiumMinigun, "Viridium minigun", 4, 14, 5, ItemId.SlugRounds, 20f, 0.8f);
            Gun(ItemId.RadMinigun, "Rad minigun", 4, 16, 6, ItemId.Arrow, 22f, 0.8f);
            Gun(ItemId.MortarTube, "Mortar tube", 4, 70, 45, ItemId.MiniRocket, 11f, 8f);
            Tech(ItemId.DatapadChainLightning, "Datapad: Chain lightning", 4, 34, 14, 9, 26f, pierce: 4);
            Tech(ItemId.DatapadTesla, "Datapad: Tesla", 4, 40, 18, 10, 20f, count: 2);
            Command(ItemId.HiveFamiliar, "Hive familiar", 4, 30, MinionKind.HiveFamiliar);
            Whip(ItemId.SignalWhipMark, "Signal whip: Mark", 4, 30, 16, 9f);
            Command(ItemId.TurretMortar, "Mortar turret kit", 4, 55, MinionKind.TurretMortar);

            // Tier 5: lumenite (Crystal Spires).
            Melee(ItemId.LumeniteGreatsword, "Lumenite greatsword", 5, 78, 18, 5f);
            Melee(ItemId.PowerFist, "Power fist", 5, 64, 8, 7f);
            Gun(ItemId.LumeniteLongbow, "Lumenite longbow", 5, 52, 18, ItemId.Arrow, 28f, 2f, pierce: 2);
            Gun(ItemId.MagneticCarbine, "Magnetic carbine", 5, 34, 8, ItemId.SlugRounds, 24f);
            Tech(ItemId.PrismLensStaff, "Prism lens staff", 5, 50, 12, 10, 30f, pierce: 2);
            Tech(ItemId.CrystalShardWand, "Crystal shard wand", 5, 30, 10, 8, 18f, count: 3, spread: 0.2f);
            Tech(ItemId.DatapadStorm, "Datapad: Storm", 5, 44, 16, 12, 16f, count: 4, spread: 0.5f);
            Tech(ItemId.MeteorCaller, "Meteor caller", 5, 80, 30, 18, 14f, knockback: 6f);
            Command(ItemId.LumeniteSpriteBeacon, "Lumenite sprite beacon", 5, 40, MinionKind.LumeniteSprite);
            Command(ItemId.SwarmController, "Swarm controller", 5, 26, MinionKind.RadSwarmling);
            Command(ItemId.TurretTesla, "Tesla turret kit", 5, 42, MinionKind.TurretTesla);

            // Tier 6: voidsteel (altars).
            Melee(ItemId.VoidsteelScythe, "Voidsteel scythe", 6, 110, 18, 4f, reach: 6f);
            Melee(ItemId.PlasmaPike, "Plasma pike", 6, 96, 14, 3.5f, reach: 8f);
            Gun(ItemId.VoidsteelSniper, "Voidsteel sniper", 6, 140, 40, ItemId.SlugRounds, 40f, 4f, pierce: 3, crit: 20);
            Gun(ItemId.ChargeRifle, "Charge rifle", 6, 60, 12, ItemId.Arrow, 30f, 2f);
            Tech(ItemId.VoidOrbFocus, "Void orb focus", 6, 70, 14, 12, 16f);
            Tech(ItemId.DatapadGravityWell, "Datapad: Gravity well", 6, 50, 24, 16, 6f, life: 3f, pierce: 8);
            Tech(ItemId.DatapadSwarmMissiles, "Datapad: Swarm missiles", 6, 36, 14, 14, 18f, count: 4, spread: 0.35f);
            Command(ItemId.VoidFamiliarCore, "Void familiar core", 6, 56, MinionKind.VoidFamiliar);
            Whip(ItemId.SignalWhipOverdrive, "Signal whip: Overdrive", 6, 50, 14, 10f);

            // Tier 7: astral alloy (after the Astro-God).
            Melee(ItemId.AstralBlade, "Astral blade", 7, 160, 12, 5f, crit: 10);
            Gun(ItemId.AstralRepeater, "Astral repeater", 7, 70, 6, ItemId.Arrow, 34f, 2f, pierce: 1);
            Tech(ItemId.AstralConductor, "Astral conductor", 7, 110, 10, 14, 32f, pierce: 4);
            Tech(ItemId.DatapadNova, "Datapad: Nova", 7, 90, 20, 20, 15f, count: 6, spread: 1f);
            Command(ItemId.AstralGuardianCore, "Astral guardian core", 7, 85, MinionKind.AstralGuardian);
        }

        public void Ammunition()
        {
            void Ammo(ItemId id, string name, int tier, int bonus, int value) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Ammunition, 999)
                    { Class = DamageClass.Ranged, Tier = tier, Rarity = RarityFor(tier), AmmoDamage = bonus, Value = value });
            Ammo(ItemId.SlugRounds, "Slug rounds", 1, 2, 1);
            Ammo(ItemId.ShotgunShells, "Shotgun shells", 2, 1, 1);
            Ammo(ItemId.ToxicDarts, "Toxic darts", 1, 3, 1);
            Ammo(ItemId.CryoRounds, "Cryo rounds", 3, 5, 2);
            Ammo(ItemId.FuelCanister, "Fuel canister", 3, 2, 2);
            Ammo(ItemId.MiniRocket, "Mini rocket", 4, 10, 5);
            Ammo(ItemId.ExplosiveCharge, "Explosive charge", 3, 14, 6);
        }

        // ------------------------------------------------------------ accessories and consumables

        public void Accessories()
        {
            void Accessory(ItemId id, string name, int tier, Func<ItemDefinition, ItemDefinition> configure) =>
                Add(configure(new ItemDefinition(id, name, ItemUseKind.Equipment, 1)
                {
                    EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Accessory, Tier = tier, Rarity = RarityFor(tier),
                    Value = 40 + tier * tier * 25
                }));

            Accessory(ItemId.FocusLensCrystal, "Focus lens crystal", 3, item => item with { TechDamageBonus = 0.1f, CritBonus = 4 });
            Accessory(ItemId.OverchargeCapacitor, "Overcharge capacitor", 3, item => item with { MaximumManaBonus = 40f, ChargeRegenBonus = 1f });
            Accessory(ItemId.CommandVisor, "Command visor", 3, item => item with { DroneSlotBonus = 1, CommandDamageBonus = 0.05f });
            Accessory(ItemId.DroneBattery, "Drone battery", 3, item => item with { SentrySlotBonus = 1 });
            Accessory(ItemId.DroneController, "Drone controller", 4, item => item with { DroneSlotBonus = 1, CommandDamageBonus = 0.08f });
            Accessory(ItemId.ReinforcedArmorSuit, "Reinforced armor suit", 4, item => item with { Defense = 4 });
            Accessory(ItemId.JetBoots, "Jet boots", 3, item => item with { MoveSpeedMultiplier = 1.15f });
            Accessory(ItemId.EnergyShieldEmitter, "Energy shield emitter", 4, item => item with { Defense = 3, MaximumLifeBonus = 20f });
            Accessory(ItemId.NightVisionGoggles, "Night-vision goggles", 2, item => item with
                { CritBonus = 3, ImmunityTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dark" } });
            Accessory(ItemId.MagnetRing, "Magnet ring", 2, item => item with
                { ImmunityTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "magnet" } });
            Accessory(ItemId.MeleeModule, "Close-quarters module", 4, item => item with { MeleeDamageBonus = 0.12f });
            Accessory(ItemId.BallisticsModule, "Ballistics module", 4, item => item with { RangedDamageBonus = 0.12f });
            Accessory(ItemId.TechModule, "Tech module", 4, item => item with { TechDamageBonus = 0.12f, ChargeRegenBonus = 2f });
            Accessory(ItemId.CommandModule, "Command module", 4, item => item with { CommandDamageBonus = 0.12f, DroneSlotBonus = 1 });
            static HashSet<string> Tags(params string[] tags) => new(tags, StringComparer.OrdinalIgnoreCase);
            // Mobility gear grants tags the movement layer can read ("flight", "glide", "double-jump", "grapple", "climb").
            Accessory(ItemId.RocketPack, "Rocket pack", 4, item => item with { ImmunityTags = Tags("flight") });
            Accessory(ItemId.GliderWings, "Glider wings", 3, item => item with { ImmunityTags = Tags("glide", "featherfall") });
            Accessory(ItemId.GrappleHook, "Grapple hook", 2, item => item with { ImmunityTags = Tags("grapple") });
            Accessory(ItemId.DoubleJumpCanister, "Double-jump canister", 2, item => item with { ImmunityTags = Tags("double-jump") });
            Accessory(ItemId.ClimbingClaws, "Climbing claws", 2, item => item with { ImmunityTags = Tags("climb") });
            Accessory(ItemId.LifeMonitor, "Life monitor", 1, item => item with { ImmunityTags = Tags("life-readout") });
            Accessory(ItemId.OreScanner, "Ore scanner", 2, item => item with { ImmunityTags = Tags("ore-readout") });
            Accessory(ItemId.DepthMeter, "Depth meter", 1, item => item with { ImmunityTags = Tags("depth-readout") });
            Accessory(ItemId.CompassChip, "Compass chip", 1, item => item with { ImmunityTags = Tags("position-readout") });
            Accessory(ItemId.RegenImplant, "Regen implant", 3, item => item with { LifeRegenerationPerHour = 0.6f });
            Accessory(ItemId.RebreatherMask, "Rebreather mask", 2, item => item with { ImmunityTags = Tags("breath", "toxic") });
            Accessory(ItemId.LavaWaderBoots, "Core wader boots", 4, item => item with { ImmunityTags = Tags("heat", "lava") });
            Accessory(ItemId.FrostGripGloves, "Frost-grip gloves", 3, item => item with { ImmunityTags = Tags("cold"), MeleeDamageBonus = 0.04f });
            Accessory(ItemId.LuckyCharm, "Lucky charm", 2, item => item with { CritBonus = 4 });
            Accessory(ItemId.ThornPlating, "Thorn plating", 3, item => item with { Defense = 2, ImmunityTags = Tags("thorns:33") });
            Accessory(ItemId.AstralHalo, "Astral halo", 7, item => item with
                { MeleeDamageBonus = 0.08f, RangedDamageBonus = 0.08f, TechDamageBonus = 0.08f, CommandDamageBonus = 0.08f, Defense = 4 });

            void Consumable(ItemId id, string name, int value, Func<ItemDefinition, ItemDefinition> configure) =>
                Add(configure(new ItemDefinition(id, name, ItemUseKind.Consumable, 30) { Value = value, Tier = 1 }));
            Consumable(ItemId.ChargeCellSmall, "Small charge cell", 8, item => item with { ChargeRestore = 40f });
            Consumable(ItemId.ChargeStim, "Charge stim", 20, item => item with { GrantsStatus = StatusId.ChargeSurge, StatusDurationHours = 3f });
            Consumable(ItemId.MedStim, "Med stim", 25, item => item with { HealAmount = 50f });
            Consumable(ItemId.SpeedStim, "Speed stim", 20, item => item with { GrantsStatus = StatusId.Haste, StatusDurationHours = 3f });
            Consumable(ItemId.ArmorStim, "Armor stim", 20, item => item with { GrantsStatus = StatusId.Armored, StatusDurationHours = 3f });
            Consumable(ItemId.RegenPatch, "Regen patch", 15, item => item with { GrantsStatus = StatusId.Regeneration, StatusDurationHours = 6f });
            Consumable(ItemId.SpelunkerSpray, "Spelunker spray", 18, item => item with { GrantsStatus = StatusId.Spelunker, StatusDurationHours = 5f });
            Add(new ItemDefinition(ItemId.RationBar, "Ration bar", ItemUseKind.Food, 99) { FoodRestore = 0.45f, Value = 6 });
            Add(new ItemDefinition(ItemId.TideFish, "Tide fish", ItemUseKind.Food, 99) { FoodRestore = 0.3f, Value = 5 });
            Consumable(ItemId.NightOwlDrops, "Night-owl drops", 15, item => item with { GrantsStatus = StatusId.Spelunker, StatusDurationHours = 6f });
            Consumable(ItemId.HunterLens, "Hunter lens", 18, item => item with { GrantsStatus = StatusId.Guarded, StatusDurationHours = 4f });
            Consumable(ItemId.FeatherfallPill, "Featherfall pill", 12, item => item with { GrantsStatus = StatusId.Haste, StatusDurationHours = 2f });
            Consumable(ItemId.WaterBreathCapsule, "Water-breath capsule", 14, item => item with { GrantsStatus = StatusId.Regeneration, StatusDurationHours = 2f });
            void Food(ItemId id, string name, float restore, int value) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Food, 99) { FoodRestore = restore, Value = value });
            Food(ItemId.SnowWolfSteak, "Snow-wolf steak", 0.4f, 8);
            Food(ItemId.MushroomStew, "Mushroom stew", 0.45f, 9);
            Food(ItemId.NectarTea, "Nectar tea", 0.25f, 7);
            Food(ItemId.CoralSushi, "Coral sushi", 0.4f, 10);
            Food(ItemId.NanoBananaSplit, "Nano-banana split", 0.55f, 12);
        }

        // ------------------------------------------------------------ tools

        public void Tools()
        {
            // Power and use time per tier come from GearCurve (Terraria's pick/axe/hammer ladder; docs/GEAR_DIFF_ITEMS.md).
            void Drill(ItemId id, string name, int tier, string material) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Pickaxe, 1)
                {
                    Tier = tier, Rarity = RarityFor(tier), ToolPower = GearCurve.DrillPower(tier, material),
                    UseCooldownTicks = GearCurve.DrillCooldown(tier, material), Value = 20 + tier * tier * 20
                });
            void Cutter(ItemId id, string name, int tier) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Cutter, 1)
                {
                    Tier = tier, Rarity = RarityFor(tier), ToolPower = GearCurve.CutterPower(tier),
                    UseCooldownTicks = GearCurve.CutterCooldown(tier), Value = 15 + tier * tier * 15
                });
            void Demolisher(ItemId id, string name, int tier) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Demolisher, 1)
                {
                    Tier = tier, Rarity = RarityFor(tier), ToolPower = GearCurve.DemolisherPower(tier),
                    UseCooldownTicks = GearCurve.DemolisherCooldown(tier), Value = 15 + tier * tier * 15
                });

            Drill(ItemId.FerriteDrill, "Ferrite rock drill", 1, "Ferrite");
            Drill(ItemId.CobaltDrill, "Cobalt drill", 2, "Cobalt");
            Drill(ItemId.GlacialDrill, "Glacial drill", 3, "Glacial");
            Drill(ItemId.ViridiumDrill, "Viridium drill", 4, "Viridium");
            Drill(ItemId.LumeniteDrill, "Lumenite drill", 5, "Lumenite");
            Drill(ItemId.VoidsteelDrill, "Voidsteel drill", 6, "Voidsteel");
            Drill(ItemId.AstralDrill, "Astral drill", 7, "Astral");

            var materials = new[] { "Ferrite", "Cobalt", "Glacial", "Viridium", "Lumenite", "Voidsteel", "Astral" };
            ItemId[] cutters = [ItemId.FerriteCutter, ItemId.CobaltCutter, ItemId.GlacialCutter, ItemId.ViridiumCutter,
                ItemId.LumeniteCutter, ItemId.VoidsteelCutter, ItemId.AstralCutter];
            ItemId[] demolishers = [ItemId.FerriteDemolisher, ItemId.CobaltDemolisher, ItemId.GlacialDemolisher, ItemId.ViridiumDemolisher,
                ItemId.LumeniteDemolisher, ItemId.VoidsteelDemolisher, ItemId.AstralDemolisher];
            for (var tier = 1; tier <= 7; tier++)
            {
                Cutter(cutters[tier - 1], $"{materials[tier - 1]} cutter", tier);
                Demolisher(demolishers[tier - 1], $"{materials[tier - 1]} demolisher", tier);
            }

            void Utility(ItemId id, string name, ItemUseKind kind, int value, int stack = 1) =>
                Add(new ItemDefinition(id, name, kind, stack) { Tier = 1, Value = value, UseCooldownTicks = 12 });
            Utility(ItemId.BugNet, "Bug net", ItemUseKind.BugNet, 30);
            Utility(ItemId.FishingRod, "Fishing rod", ItemUseKind.Utility, 30);
            Utility(ItemId.EmptyBucket, "Empty bucket", ItemUseKind.Utility, 8, 10);
            Utility(ItemId.BucketWater, "Water bucket", ItemUseKind.Utility, 10, 10);
            Utility(ItemId.BucketLava, "Liquid uranium canister", ItemUseKind.Utility, 14, 10);
            Utility(ItemId.BucketFuel, "Fuel bucket", ItemUseKind.Utility, 12, 10);
            Utility(ItemId.PaintSprayer, "Plating sprayer", ItemUseKind.Utility, 40);
            Utility(ItemId.WireTool, "Wire tool", ItemUseKind.Utility, 35);
            Utility(ItemId.GlowSticks, "Glow sticks", ItemUseKind.Utility, 2, 99);
            Utility(ItemId.TeleportBeacon, "Teleport beacon", ItemUseKind.Utility, 120);
        }

        // ------------------------------------------------------------ armor

        public void Armor()
        {
            // (set id, display base, tier, class, three item ids)
            var sets = new (string Set, string Name, int Tier, DamageClass Class, ItemId Head, ItemId Chest, ItemId Legs)[]
            {
                ("scav", "Scav", 1, DamageClass.None, ItemId.ScavHelmet, ItemId.ScavChest, ItemId.ScavLegs),
                ("cobalt-trooper", "Cobalt trooper", 2, DamageClass.None, ItemId.CobaltTrooperHelmet, ItemId.CobaltTrooperChest, ItemId.CobaltTrooperLegs),
                ("pyrocite-bulwark", "Pyrocite bulwark", 3, DamageClass.Melee, ItemId.PyrociteBulwarkHelmet, ItemId.PyrociteBulwarkChest, ItemId.PyrociteBulwarkLegs),
                ("glacial-ranger", "Glacial ranger", 3, DamageClass.Ranged, ItemId.GlacialRangerHelmet, ItemId.GlacialRangerChest, ItemId.GlacialRangerLegs),
                ("uranium-hazmat", "Uranium hazmat", 3, DamageClass.Tech, ItemId.UraniumHazmatHelmet, ItemId.UraniumHazmatChest, ItemId.UraniumHazmatLegs),
                ("glacial-handler", "Glacial handler", 3, DamageClass.Command, ItemId.GlacialHandlerHelmet, ItemId.GlacialHandlerChest, ItemId.GlacialHandlerLegs),
                ("viridium-warden", "Viridium warden", 4, DamageClass.Melee, ItemId.ViridiumWardenHelmet, ItemId.ViridiumWardenChest, ItemId.ViridiumWardenLegs),
                ("viridium-stalker", "Viridium stalker", 4, DamageClass.Ranged, ItemId.ViridiumStalkerHelmet, ItemId.ViridiumStalkerChest, ItemId.ViridiumStalkerLegs),
                ("viridium-sage", "Viridium sage", 4, DamageClass.Tech, ItemId.ViridiumSageHelmet, ItemId.ViridiumSageChest, ItemId.ViridiumSageLegs),
                ("viridium-handler", "Viridium handler", 4, DamageClass.Command, ItemId.ViridiumHandlerHelmet, ItemId.ViridiumHandlerChest, ItemId.ViridiumHandlerLegs),
                ("lumenite-paladin", "Lumenite paladin", 5, DamageClass.Melee, ItemId.LumenitePaladinHelmet, ItemId.LumenitePaladinChest, ItemId.LumenitePaladinLegs),
                ("lumenite-marksman", "Lumenite marksman", 5, DamageClass.Ranged, ItemId.LumeniteMarksmanHelmet, ItemId.LumeniteMarksmanChest, ItemId.LumeniteMarksmanLegs),
                ("lumenite-sage", "Lumenite sage", 5, DamageClass.Tech, ItemId.LumeniteSageHelmet, ItemId.LumeniteSageChest, ItemId.LumeniteSageLegs),
                ("lumenite-handler", "Lumenite handler", 5, DamageClass.Command, ItemId.LumeniteHandlerHelmet, ItemId.LumeniteHandlerChest, ItemId.LumeniteHandlerLegs),
                ("voidsteel-reaver", "Voidsteel reaver", 6, DamageClass.Melee, ItemId.VoidsteelReaverHelmet, ItemId.VoidsteelReaverChest, ItemId.VoidsteelReaverLegs),
                ("voidsteel-marksman", "Voidsteel marksman", 6, DamageClass.Ranged, ItemId.VoidsteelMarksmanHelmet, ItemId.VoidsteelMarksmanChest, ItemId.VoidsteelMarksmanLegs),
                ("voidsteel-wraith", "Voidsteel wraith", 6, DamageClass.Tech, ItemId.VoidsteelWraithHelmet, ItemId.VoidsteelWraithChest, ItemId.VoidsteelWraithLegs),
                ("voidsteel-handler", "Voidsteel handler", 6, DamageClass.Command, ItemId.VoidsteelHandlerHelmet, ItemId.VoidsteelHandlerChest, ItemId.VoidsteelHandlerLegs),
                ("astral-warden", "Astral warden", 7, DamageClass.Melee, ItemId.AstralWardenHelmet, ItemId.AstralWardenChest, ItemId.AstralWardenLegs),
                ("astral-marksman", "Astral marksman", 7, DamageClass.Ranged, ItemId.AstralMarksmanHelmet, ItemId.AstralMarksmanChest, ItemId.AstralMarksmanLegs),
                ("astral-sage", "Astral sage", 7, DamageClass.Tech, ItemId.AstralSageHelmet, ItemId.AstralSageChest, ItemId.AstralSageLegs),
                ("astral-handler", "Astral handler", 7, DamageClass.Command, ItemId.AstralHandlerHelmet, ItemId.AstralHandlerChest, ItemId.AstralHandlerLegs)
            };

            foreach (var set in sets)
            {
                var t = set.Tier;
                // Piece and set-bonus defense follow the Terraria-shaped armour curve (GearCurve.ArmorPieces).
                var (headDefense, bodyDefense, legsDefense, setDefense) = GearCurve.ArmorPieces(t, set.Class);
                var bonus = set.Class switch
                {
                    DamageClass.Melee => new ArmorSetBonus(set.Set, set.Name, set.Class,
                        $"+{setDefense} defense, {3 + t}% lifesteal, attackers take {10 * t}% thorns", Defense: setDefense,
                        ClassDamageBonus: 0.05f, Lifesteal: 0.03f + 0.01f * t, Thorns: 0.1f * t),
                    DamageClass.Ranged => new ArmorSetBonus(set.Set, set.Name, set.Class,
                        $"+{10 + 2 * t}% ballistics damage, +{5 + t}% crit, {20 + 3 * t}% ammo saving",
                        ClassDamageBonus: 0.1f + 0.02f * t, CritBonus: 5 + t, AmmoSaveChance: 0.2f + 0.03f * t),
                    DamageClass.Tech => new ArmorSetBonus(set.Set, set.Name, set.Class,
                        $"+{20 + 10 * t} max charge, +{2 + t} charge regen, +{8 + 2 * t}% tech damage",
                        ClassDamageBonus: 0.08f + 0.02f * t, ChargeMaxBonus: 20 + 10 * t, ChargeRegenBonus: 2 + t),
                    DamageClass.Command => new ArmorSetBonus(set.Set, set.Name, set.Class,
                        $"+{(t >= 5 ? 2 : 1)} drone slots{(t >= 4 ? ", +1 sentry slot" : "")}, +{10 + 2 * t}% drone damage",
                        ClassDamageBonus: 0.1f + 0.02f * t, DroneSlots: t >= 5 ? 2 : 1, SentrySlots: t >= 4 ? 1 : 0),
                    _ => new ArmorSetBonus(set.Set, set.Name, set.Class, $"+{setDefense} defense", Defense: setDefense)
                };
                ArmorSets.Register(bonus);

                ItemDefinition Piece(ItemId id, string suffix, EquipmentSlot slot, int defense, int value) =>
                    new(id, $"{set.Name} {suffix}", ItemUseKind.Equipment, 1)
                    {
                        EquipmentSlot = slot, Defense = defense, ArmorSetId = set.Set, SetBonusDefense = setDefense,
                        Tier = t, Rarity = RarityFor(t), Value = value, Class = set.Class,
                        MeleeDamageBonus = set.Class == DamageClass.Melee ? 0.03f : 0f,
                        RangedDamageBonus = set.Class == DamageClass.Ranged ? 0.03f : 0f,
                        TechDamageBonus = set.Class == DamageClass.Tech ? 0.03f : 0f,
                        CommandDamageBonus = set.Class == DamageClass.Command ? 0.03f : 0f,
                        MaximumManaBonus = set.Class == DamageClass.Tech ? 10f : 0f
                    };
                var baseValue = 30 + 25 * t * t;
                Add(Piece(set.Head, "helmet", global::Rimtera.Systems.EquipmentSlot.Head, headDefense, baseValue));
                Add(Piece(set.Chest, "chestplate", global::Rimtera.Systems.EquipmentSlot.Body, bodyDefense, baseValue * 3 / 2));
                Add(Piece(set.Legs, "greaves", global::Rimtera.Systems.EquipmentSlot.Legs, legsDefense, baseValue * 5 / 4));
            }
        }

        // ------------------------------------------------------------ stations, blocks, furniture, walls

        public void Placeables()
        {
            void Station(ItemId id, string name, int tier) =>
                Add(new ItemDefinition(id, name, ItemUseKind.PlaceTile, 99)
                    { PlacedTile = Enum.Parse<TileId>(id.ToString()), Tier = tier, Value = 30 + tier * tier * 30 });
            Station(ItemId.FieldWorkbench, "Field workbench", 1);
            Station(ItemId.NanoFabricatorMk2, "Nano-Fabricator Mk2", 5);
            Station(ItemId.OreRefineryMk2, "Ore Refinery Mk2", 4);
            Station(ItemId.PowerForgeMk2, "Power Forge Mk2", 3);
            Station(ItemId.AlloyAnvil, "Alloy anvil", 4);
            Station(ItemId.VoidForge, "Void forge", 6);
            Station(ItemId.AstralAssembler, "Astral assembler", 7);
            Station(ItemId.ChemLab, "Chem lab", 1);
            Station(ItemId.BioLab, "Bio lab", 2);
            Station(ItemId.Loom, "Loom", 1);
            Station(ItemId.Sawmill, "Sawmill", 1);
            Station(ItemId.Kiln, "Kiln", 1);
            Station(ItemId.CookingStation, "Cooking station", 1);
            Station(ItemId.TinkerBench, "Tinker bench", 2);
            Station(ItemId.DroneBay, "Drone bay", 2);
            Station(ItemId.AmmoPress, "Ammo press", 1);
            Station(ItemId.ChargeReactor, "Charge reactor", 2);
            Station(ItemId.ResearchTerminal, "Research terminal", 2);

            foreach (var definition in ContentTiles.Definitions)
            {
                if (definition.ItemId is not { } itemId || items.ContainsKey(itemId))
                    continue;
                Add(new ItemDefinition(itemId, definition.Name, ItemUseKind.PlaceTile, definition.Furniture ? 99 : 999)
                {
                    PlacedTile = definition.Id, IsMaterial = !definition.Furniture, Value = definition.Value,
                    Tier = definition.Tier
                });
            }

            foreach (var (item, wall, name) in ContentTiles.WallItems)
                Add(new ItemDefinition(item, name, ItemUseKind.PlaceWall, 999) { PlacedWall = wall, IsMaterial = true, Value = 1 });
        }

        public void Summons()
        {
            void Boss(ItemId id, string name, EnemyKind boss, int tier) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Consumable, 20)
                    { SummonsBoss = boss, Tier = tier, Rarity = RarityFor(tier), Value = 40 + tier * 30 });
            Boss(ItemId.SummonReactorKey, "Reactor key", EnemyKind.StoneSentinel, 1);
            Boss(ItemId.SummonSwarmPheromone, "Swarm pheromone", EnemyKind.AlphaSkitter, 2);
            Boss(ItemId.SummonFaultyMiningChip, "Faulty mining chip", EnemyKind.DamagedMiningDrone, 2);
            Boss(ItemId.SummonRottingSeed, "Rotting seed", EnemyKind.PlantOvergrowth, 2);
            Boss(ItemId.SummonScoutBeacon, "Scout beacon", EnemyKind.ScoutMech, 2);
            Boss(ItemId.FalloutHiveCell, "Fallout hive cell", EnemyKind.FalloutHivemind, 2);
            Boss(ItemId.SwarmResonator, "Swarm resonator", EnemyKind.CyberSwarmLeader, 4);
            Boss(ItemId.RenegadeCredentials, "Renegade credentials", EnemyKind.RenegadeOverseer, 4);
            Boss(ItemId.MutagenBloom, "Mutagen bloom", EnemyKind.MutatedFlora, 4);
            Boss(ItemId.TaintedTissue, "Tainted tissue", EnemyKind.Abomination, 5);
            Boss(ItemId.WarshipTransponder, "Warship transponder", EnemyKind.WarshipCore, 5);
            Boss(ItemId.SunspotLens, "Sunspot lens", EnemyKind.SolarFlareEntity, 5);
            Boss(ItemId.VoidLure, "Void lure", EnemyKind.VoidStalker, 5);
            Boss(ItemId.ClaimMarker, "Claim marker", EnemyKind.AsteroidMinerMech, 5);
            Boss(ItemId.BountyNotice, "Bounty notice", EnemyKind.CyberPirateCaptain, 5);
            Boss(ItemId.SummonAstralSigil, "Astral sigil", EnemyKind.AstroGod, 7);
            Boss(ItemId.SporeCrown, "Spore crown", EnemyKind.SporeMonarch, 3);
            Boss(ItemId.RoyalNectar, "Royal nectar", EnemyKind.BloomMatriarch, 4);
            Boss(ItemId.PrismCatalyst, "Prism catalyst", EnemyKind.PrismEmpress, 5);
            Boss(ItemId.VaultCore, "Vault core", EnemyKind.VaultColossus, 6);
            Boss(ItemId.MoonEaterLure, "Rusted lure", EnemyKind.MoonEater, 4);
            Boss(ItemId.EclipseOrb, "Eclipse orb", EnemyKind.EclipseLord, 5);
            Boss(ItemId.WarlordBanner, "Warlord banner", EnemyKind.ScavengerWarlord, 3);
            Boss(ItemId.DreadnoughtFlag, "Dreadnought flag", EnemyKind.PirateDreadnought, 5);
            Boss(ItemId.ProtocolKey, "Protocol key", EnemyKind.ProtocolDragon, 5);
            Boss(ItemId.MothershipBeacon, "Mothership beacon", EnemyKind.MothershipCore, 6);

            void Event(ItemId id, string name, WorldEventKind kind, int tier) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Consumable, 20)
                    { StartsEvent = kind, Tier = tier, Rarity = RarityFor(tier), Value = 50 + tier * 30 });
            Event(ItemId.ScrapBeacon, "Scrap beacon", WorldEventKind.ScavengerRaid, 3);
            Event(ItemId.SummonPirateTransmitter, "Pirate transmitter", WorldEventKind.PirateBoarding, 5);
            Event(ItemId.SummonSignalCore, "Signal core", WorldEventKind.DefenseProtocol, 4);
            Event(ItemId.FestivalFlare, "Festival flare", WorldEventKind.ColonyFestival, 1);
        }

        /// <summary>Bait strength of caught critters: worms and grubs low, golden critters best.</summary>
        private static int BaitPowerFor(ItemId kind) => kind switch
        {
            ItemId.Worm => 10,
            ItemId.GlowGrub or ItemId.Snail or ItemId.TruffleMite => 12,
            ItemId.GlowFirefly or ItemId.Dragonfly or ItemId.LadybugBot => 15,
            ItemId.Frog or ItemId.CaveFrog or ItemId.LeafFrog or ItemId.GlowSnail or ItemId.HaloSnail => 20,
            ItemId.ButterflyBlue or ItemId.ButterflyGold or ItemId.PrismButterfly or ItemId.SporeMoth => 25,
            ItemId.GlowJelly or ItemId.ShellCrab or ItemId.CrabSmall => 30,
            ItemId.GoldenCritter => 50,
            _ => 8
        };

        public void Fishing()
        {
            void Rod(ItemId id, string name, int tier, int power) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Utility, 1)
                    { Tier = tier, Rarity = RarityFor(tier), FishingPower = power, UseCooldownTicks = 20, Value = 30 + tier * tier * 25 });
            Rod(ItemId.FishingRod, "Fishing rod", 0, 8);
            Rod(ItemId.FerriteFishingRod, "Ferrite fishing rod", 1, 10);
            Rod(ItemId.CobaltFishingRod, "Cobalt fishing rod", 2, 18);
            Rod(ItemId.GlacialFishingRod, "Glacial fishing rod", 3, 26);
            Rod(ItemId.ViridiumFishingRod, "Viridium fishing rod", 4, 34);
            Rod(ItemId.LumeniteFishingRod, "Lumenite fishing rod", 5, 42);
            Rod(ItemId.VoidsteelFishingRod, "Voidsteel fishing rod", 6, 50);
            Rod(ItemId.AstralFishingRod, "Astral fishing rod", 7, 60);
            Rod(ItemId.AcidFishingRod, "Acid-etched rod", 3, 24);
            Rod(ItemId.MagmaFishingRod, "Fission rod", 4, 30);

            static HashSet<string> Tags(params string[] tags) => new(tags, StringComparer.OrdinalIgnoreCase);
            void Gear(ItemId id, string name, int tier, int power, HashSet<string> tags) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Equipment, 1)
                {
                    EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Accessory, Tier = tier, Rarity = RarityFor(tier),
                    FishingPower = power, ImmunityTags = tags, Value = 60 + tier * 40
                });
            Gear(ItemId.FishingLine, "Braided fishing line", 2, 0, Tags("fishing-line"));
            Gear(ItemId.FishingLure, "Shimmer lure", 2, 10, Tags("fishing-lure"));
            Gear(ItemId.AcidWader, "Acid wader", 3, 0, Tags("acid-fishing", "acid"));
            Gear(ItemId.LavaLure, "Uranium lure", 4, 5, Tags("lava-fishing"));
            Gear(ItemId.CatchScanner, "Catch scanner", 2, 0, Tags("catch-scanner"));
            Add(new ItemDefinition(ItemId.AbyssBait, "Abyss bait", ItemUseKind.Material, 20)
                { BaitPower = 30, Tier = 5, Rarity = RarityFor(5), Value = 150, IsMaterial = true });

            void Fish(ItemId id, string name, int value, float food = 0.15f) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Food, 99) { FoodRestore = food, Value = value, IsMaterial = true });
            Fish(ItemId.FrostCod, "Frost cod", 10);
            Fish(ItemId.EmberKoi, "Glowscale koi", 30);
            Fish(ItemId.AcidEel, "Slick eel", 16);
            Fish(ItemId.ReefSnapper, "Reef snapper", 14);
            Fish(ItemId.NectarMinnow, "Nectar minnow", 18);
            Fish(ItemId.BlightCarp, "Blight carp", 15);
            Fish(ItemId.PrismTrout, "Prism trout", 35);
            Fish(ItemId.SporeGuppy, "Spore guppy", 12);
            Fish(ItemId.MudSkipper, "Mud skipper", 9);
            Fish(ItemId.CaveBlindfish, "Cave blindfish", 12);
            Fish(ItemId.CookedFish, "Cooked fish", 12, 0.4f);
            Fish(ItemId.CookedEel, "Smoked eel", 20, 0.45f);
            Fish(ItemId.GrilledKoi, "Grilled koi", 40, 0.55f);
            Fish(ItemId.FishFillet, "Fish fillet", 8, 0.3f);

            void Junk(ItemId id, string name) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Material, 99) { IsMaterial = true, Value = 1 });
            Junk(ItemId.RustedCan, "Rusted can");
            Junk(ItemId.TangledWire, "Tangled wire");
            Junk(ItemId.SoggyDatapad, "Soggy datapad");

            void Crate(ItemId id, string name, int tier) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Consumable, 99)
                    { IsCrate = true, Tier = tier, Rarity = RarityFor(tier), Value = 20 + tier * tier * 20 });
            Crate(ItemId.ScrapCrate, "Scrap crate", 1);
            Crate(ItemId.AlloyCrate, "Alloy crate", 3);
            Crate(ItemId.AstralCrate, "Astral crate", 6);
            Crate(ItemId.CoastCrate, "Tidal crate", 2);
            Crate(ItemId.FrozenCrate, "Frost-rimed crate", 2);
            Crate(ItemId.JunkCrate, "Salvager's crate", 2);
            Crate(ItemId.FungalCrate, "Mycelium crate", 2);
            Crate(ItemId.CanopyCrate, "Vine-wrapped crate", 2);
            Crate(ItemId.CrystalCrate, "Prismatic crate", 4);
            Crate(ItemId.BlightCrate, "Tainted crate", 3);
            Crate(ItemId.MoltenCrate, "Reactor crate", 4);

            void Quest(ItemId id, string name) =>
                Add(new ItemDefinition(id, name, ItemUseKind.Material, 1) { IsQuestFish = true, IsMaterial = true, Value = 5 });
            Quest(ItemId.SpinyRustback, "Spiny rustback");
            Quest(ItemId.LanternEel, "Lantern eel");
            Quest(ItemId.GlassPike, "Glass pike");
            Quest(ItemId.TwinTailSnapper, "Twin-tail snapper");
            Quest(ItemId.MottledGrouper, "Mottled grouper");
            Quest(ItemId.Hexscale, "Hexscale");
            Quest(ItemId.BubbleRay, "Bubble ray");
            Quest(ItemId.CrownedTetra, "Crowned tetra");
            Quest(ItemId.VoidAngler, "Void angler");
            Quest(ItemId.StarfallMinnow, "Starfall minnow");
        }
        public void Critters()
        {
            // Every catchable critter archetype has a same-named item (critter_* art resolves for both).
            foreach (var archetype in EnemyArchetypes.All.Where(archetype => archetype.Catchable))
            {
                var creature = archetype.Kind;
                if (!Enum.TryParse<ItemId>(creature.ToString(), out var kind))
                    continue;
                var name = EnemyArchetypes.Get(creature).DisplayName;
                Add(new ItemDefinition(kind, name, ItemUseKind.Critter, 20)
                    { ReleasesCritter = creature, Value = kind == ItemId.GoldenCritter ? 500 : 12, BaitPower = BaitPowerFor(kind) });
            }
        }
    }
}
