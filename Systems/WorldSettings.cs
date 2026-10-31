namespace Rimtera.Systems;

/// <summary>How hard the world's creatures and incidents hit. Chosen at world creation.</summary>
public enum WorldDifficulty : byte
{
    Explorer,
    Classic,
    Brutal,
    /// <summary>The hardest world tier (Master analog): tougher creatures and a free sixth accessory slot.</summary>
    Annihilation
}

/// <summary>Which hazard biome dominates the underground. Random picks one from the seed.</summary>
public enum WorldHazard : byte
{
    Random,
    RadiatedDeeps,
    MoltenChasm
}

/// <summary>
/// The world's spreading blight, chosen at creation like a world's evil biome: chasms, hearts,
/// altars and creeping conversion of nearby ground. Random picks one from the seed.
/// </summary>
public enum WorldBlight : byte
{
    Random,
    Radiated,
    Overgrowth
}

/// <summary>What happens when the character dies. Chosen at character creation.</summary>
public enum CharacterDifficulty : byte
{
    Explorer,
    Classic,
    Ironman,
    /// <summary>Research and duplicate items; time, spawn and difficulty powers (Systems/Creative.cs).</summary>
    Creative,
    /// <summary>Drops every carried item at the death site to be recovered (Mediumcore analog). Shown as Medium.</summary>
    Salvager,
    /// <summary>Drops the whole bag except tools, and every credit, at the death site; the character still respawns.</summary>
    Hard
}

/// <summary>Display text and tuning for the creation options.</summary>
public static class WorldSettings
{
    public static string Name(WorldDifficulty difficulty) => difficulty switch
    {
        WorldDifficulty.Explorer => "Explorer",
        WorldDifficulty.Brutal => "Brutal",
        WorldDifficulty.Annihilation => "Annihilation",
        _ => "Classic"
    };

    public static string Describe(WorldDifficulty difficulty) => difficulty switch
    {
        WorldDifficulty.Explorer => "Creatures are weaker and raids are smaller. A gentle landing.",
        WorldDifficulty.Brutal => "Creatures hit harder, have more armour, and the storyteller sends bigger threats.",
        WorldDifficulty.Annihilation => "Brutal and then some: creatures have double strength, armour counts in full, bosses leave relics, and you gain a sixth accessory slot.",
        _ => "The intended balance between building and surviving."
    };

    public static string Name(WorldHazard hazard) => hazard switch
    {
        WorldHazard.RadiatedDeeps => "Radiated Deeps",
        WorldHazard.MoltenChasm => "Irradiated Core",
        _ => "Random"
    };

    public static string Describe(WorldHazard hazard) => hazard switch
    {
        WorldHazard.RadiatedDeeps => "Wide radiated caverns of caustic crystal and uranium; toxic fallout is more likely.",
        WorldHazard.MoltenChasm => "A deeper Irradiated Core flooded with liquid uranium; heat waves and pyrocite are more common.",
        _ => "The seed decides which hazard biome spreads through the underground."
    };

    public static string Name(WorldBlight blight) => blight switch
    {
        WorldBlight.Radiated => "Radiated Blight",
        WorldBlight.Overgrowth => "Overgrowth",
        _ => "Random"
    };

    public static string Describe(WorldBlight blight) => blight switch
    {
        WorldBlight.Radiated => "Glowing shafts of caustic crystal sink from the surface; reactor cores pulse at their roots. It creeps into soil and stone.",
        WorldBlight.Overgrowth => "Mutant vines choke tangled caverns around spore hearts. It creeps into soil and stone.",
        _ => "The seed decides which blight takes root."
    };

    public static WorldBlight Resolve(WorldBlight blight, int seed) => blight != WorldBlight.Random
        ? blight
        : ((seed >> 3) & 1) == 0 ? WorldBlight.Radiated : WorldBlight.Overgrowth;

    public static WorldHazard Resolve(WorldHazard hazard, int seed) => hazard != WorldHazard.Random
        ? hazard
        : (seed & 1) == 0 ? WorldHazard.RadiatedDeeps : WorldHazard.MoltenChasm;

    /// <summary>Multiplier for creature hit points and damage.</summary>
    public static float EnemyScale(WorldDifficulty difficulty) => difficulty switch
    {
        WorldDifficulty.Explorer => 0.75f,
        WorldDifficulty.Brutal => 1.4f,
        WorldDifficulty.Annihilation => 2f,
        _ => 1f
    };

    /// <summary>
    /// Player-facing names. The stored values keep their old names (Classic, Salvager) so saves stay valid; the design's
    /// tiers are Easy (drop money), Medium (money and items), Hard (everything but tools) and Ironman (the impossible run).
    /// </summary>
    public static string Name(CharacterDifficulty difficulty) => difficulty switch
    {
        CharacterDifficulty.Explorer => "Explorer",
        CharacterDifficulty.Ironman => "Ironman",
        CharacterDifficulty.Creative => "Creative",
        CharacterDifficulty.Salvager => "Medium",
        CharacterDifficulty.Hard => "Hard",
        _ => "Easy"
    };

    /// <summary>The order character creation steps through: the stakes rise, then the two no-stakes modes.</summary>
    public static IReadOnlyList<CharacterDifficulty> CreationOrder { get; } =
    [
        CharacterDifficulty.Classic, CharacterDifficulty.Salvager, CharacterDifficulty.Hard, CharacterDifficulty.Ironman,
        CharacterDifficulty.Explorer, CharacterDifficulty.Creative
    ];

    public static string Describe(CharacterDifficulty difficulty) => difficulty switch
    {
        CharacterDifficulty.Explorer => "No stakes: keep everything on death and respawn quickly.",
        CharacterDifficulty.Ironman => "The impossible run: you drop everything but your tools, and you die for good. The run ends with you.",
        CharacterDifficulty.Salvager => "Drop your money and every item you carry where you fall (worn gear stays on). Go back and get it.",
        CharacterDifficulty.Hard => "Drop your whole bag except tools, and all your money, where you fall. Go back and get it.",
        CharacterDifficulty.Creative => "Research items to duplicate them freely; bend time, spawns and difficulty from the pause menu.",
        _ => "Drop part of your money where you fall, then respawn. Go back and pick it up."
    };

    private static readonly string[] NamePrefixes =
        ["Cinder", "Frost", "Rust", "Hollow", "Verdant", "Shattered", "Silent", "Ion", "Amber", "Gloam", "Iron", "Drift"];

    private static readonly string[] NameSuffixes =
        ["Reach", "Expanse", "Basin", "Frontier", "Outpost", "Wastes", "Crater", "Haven", "Ridge", "Sprawl", "Shelf", "Hollow"];

    /// <summary>Original two-part world names ("Rust Basin", "Ion Frontier").</summary>
    public static string RandomWorldName(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var prefix = NamePrefixes[random.Next(NamePrefixes.Length)];
        string suffix;
        do suffix = NameSuffixes[random.Next(NameSuffixes.Length)];
        while (suffix == prefix);
        return $"{prefix} {suffix}";
    }
}
