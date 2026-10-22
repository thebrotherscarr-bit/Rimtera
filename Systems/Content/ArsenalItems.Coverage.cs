using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// Creature coverage loot (docs/CREATURE_COVERAGE.md): two unique weapons per new boss (rolled from its boss pool) and one
/// rare weapon per event mini-boss, mirroring how Terraria's event bosses and mini-bosses carry their own weapons. Stats come
/// from the arsenal tier curve like every other unique; names are original.
/// </summary>
internal static partial class ArsenalItems
{
    private static WeaponRow[] CoverageWeaponRows() =>
    [
        // Bosses.
        new(ItemId.RegentScythe, "Regent's scythe", WeaponStyle.Broadsword, 6, "Unique", 1.1f, 1.0f, "kb=1.5", "boss:HollowRegent", true),
        new(ItemId.EmberLanternStaff, "Ember-lantern staff", WeaponStyle.Staff, 6, "Unique", 1.1f, 1.0f, "count=2", "boss:HollowRegent", true),
        new(ItemId.SeraphIcicleBow, "Seraph's icicle bow", WeaponStyle.Bow, 6, "Unique", 1.1f, 1.0f, "count=3", "boss:RimeSeraph", true),
        new(ItemId.FrostChoirStaff, "Frost-choir staff", WeaponStyle.Staff, 6, "Unique", 1.1f, 1.0f, "pierce=3", "boss:RimeSeraph", true),
        new(ItemId.BehemothMaul, "Behemoth's maul", WeaponStyle.Flail, 3, "Unique", 1.1f, 1.0f, "kb=1.6", "boss:HoarfrostBehemoth", true),
        new(ItemId.HoarfrostFang, "Hoarfrost fang", WeaponStyle.Spear, 3, "Unique", 1.1f, 1.0f, "reach=1", "boss:HoarfrostBehemoth", true),
        new(ItemId.SovereignGyro, "Sovereign gyro", WeaponStyle.Yoyo, 4, "Unique", 1.1f, 1.0f, null, "boss:GelSovereign", true),
        new(ItemId.GelburstLauncher, "Gelburst launcher", WeaponStyle.Launcher, 4, "Unique", 1.1f, 1.0f, "kb=1.3", "boss:GelSovereign", true),
        // Mini-bosses (percent chance per kill).
        new(ItemId.PaladinWarhammer, "Paladin's warhammer", WeaponStyle.Flail, 6, "Unique", 1.1f, 1.0f, "kb=2", "mob:CryptPaladin:12", true),
        new(ItemId.ColossusFrostlance, "Colossus frostlance", WeaponStyle.Spear, 4, "Unique", 1.1f, 1.0f, "reach=1", "mob:BlizzardColossus:15", true),
        new(ItemId.GritStormRod, "Grit-storm rod", WeaponStyle.Staff, 4, "Unique", 1.1f, 1.0f, "pierce=2", "mob:GritElemental:15", true),
        new(ItemId.NautilusHarpoonGun, "Nautilus harpoon-gun", WeaponStyle.Gun, 5, "Unique", 1.1f, 1.0f, "pierce=2", "mob:CorrosionNautilus:15", true),
        new(ItemId.HexerSceptre, "Hexer's sceptre", WeaponStyle.Staff, 4, "Unique", 1.1f, 1.0f, "count=2", "mob:ScavengerHexer:20", true),
        new(ItemId.LancerJoust, "Hollow lancer's joust", WeaponStyle.Spear, 6, "Unique", 1.1f, 1.0f, "reach=1", "mob:HollowLancer:12", true),
        new(ItemId.RootwalkerBranch, "Rootwalker branch", WeaponStyle.Staff, 6, "Unique", 1.1f, 1.0f, "count=3", "mob:EmberRootwalker:12", true),
        new(ItemId.PineNeedleRepeater, "Pine-needle repeater", WeaponStyle.Gun, 6, "Unique", 1.1f, 1.0f, "speed=1.4", "mob:FrostspirePine:12", true),
        new(ItemId.CryoTankCannon, "Cryo-tank cannon", WeaponStyle.Launcher, 6, "Unique", 1.1f, 1.0f, "kb=1.4", "mob:CryoTank:12", true)
    ];
}
