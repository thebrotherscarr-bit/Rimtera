using System.Numerics;
using Rimtera.Entities;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>
/// `--entity-sprites`: every animated actor's base sprite, for tools/comfy/gen_anim.py and beauty_pass.py. One
/// tab-separated line per sprite: category, base sprite, motion (walk | fly | hop | swim | float), whether it already
/// has a hand-made frame set (those are skipped by the generators), its attack (ranged | melee | none), the box it is
/// drawn into, in tiles (collision size times draw scale), so redrawn art can share one pixel density, and what it is
/// called (its name, or a person's role), which its art must show. Duplicate sprites are listed once.
/// </summary>
public static class AnimationCatalog
{
    public static int Print()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Line(string category, string sprite, string motion, Vector2 box, string name, bool handMade = false, string attack = "none")
        {
            if (seen.Add(sprite))
                Console.WriteLine($"{category}\t{sprite}\t{motion}\t{(handMade ? "hand" : "-")}\t{attack}\t{box.X:0.###}\t{box.Y:0.###}\t{name}");
        }

        foreach (var archetype in EnemyArchetypes.All)
        {
            var category = archetype.IsBoss ? "boss" : archetype.Passive ? "critter" : "enemy";
            var motion = archetype.Flying ? "fly" : archetype.Hops ? "hop" : "walk";
            var attack = archetype.Passive ? "none" : archetype.ProjectileSpeed > 0f ? "ranged" : "melee";
            Line(category, SpriteCatalog.ForEnemy(archetype.Kind), motion, archetype.Size * SpriteCatalog.EnemyDrawScale(archetype.Kind, archetype.IsBoss),
                archetype.DisplayName, SpriteCatalog.HasHandMadeFrames(archetype.Kind), attack);
        }
        foreach (var npc in TownNpcCatalog.All)
            Line("npc", npc.Sprite, "walk", HumanoidBody.Size, $"{npc.Role}, a person");
        Line("npc", "npc_bound_survivor", "walk", HumanoidBody.Size, "a survivor tied up in restraints, a person");
        foreach (var crew in SpriteCatalog.CrewSpriteNames)
            Line("crew", crew, "walk", HumanoidBody.Size, crew.Replace('_', ' ') + ", a person");
        var pet = new Vector2(0.8f, 0.7f) * 1.2f; // GameSimulation.Leisure pet size, drawn at 1.2x
        foreach (var name in new[] { "pet_pocket_drone", "pet_lumen_wisp", "pet_lantern_bot" })
            Line("pet", name, "float", pet, name[4..].Replace('_', ' '));
        Line("pet", "pet_pebble_crab", "walk", pet, "pebble crab");
        return 0;
    }

    /// <summary>
    /// `--item-sprites`: every item, for tools/comfy/beauty_pass.py: sprite file name (its prefixed form when that
    /// file exists), display name, census category (weapon: sword, tool: drill, armor: head ...), item id, and its art:
    /// "own" (a file of its own), "borrowed:&lt;sprite&gt;" (no file, so the game tints that stand-in sprite, which
    /// does not look like the item) or "none". Items drawn with block or ore art are left out.
    /// </summary>
    public static int PrintItems()
    {
        var folder = Path.Combine(Environment.CurrentDirectory, "Assets", "Sprites");
        bool Exists(string name) => File.Exists(Path.Combine(folder, name + ".png"));
        foreach (var item in new ItemCatalog().All)
        {
            if (SpriteCatalog.ForItem(item.Id, null) is not { } named || named.StartsWith("terrain_") || named.StartsWith("ore_"))
                continue;
            var sprite = SpriteAtlas.ResolveName(named, Exists);
            var art = Exists(sprite) ? "own" : ItemArt.BaseFor(item) is { } stand ? $"borrowed:{SpriteAtlas.ResolveName(stand, Exists)}" : "none";
            Console.WriteLine($"{sprite}\t{item.Name}\t{ItemCensus.Categorize(item)}\t{item.Id}\t{art}");
        }
        return 0;
    }
}
