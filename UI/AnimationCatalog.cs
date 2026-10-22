using Rimtera.Entities;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>
/// `--entity-sprites`: every animated actor's base sprite, for tools/comfy/gen_anim.py. One tab-separated line per
/// sprite: category, base sprite, motion (walk | fly | hop | swim | float), whether it already has a hand-made
/// frame set (those are skipped by the generator), and its attack (ranged | melee | none). Duplicate sprites are listed once.
/// </summary>
public static class AnimationCatalog
{
    public static int Print()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Line(string category, string sprite, string motion, bool handMade = false, string attack = "none")
        {
            if (seen.Add(sprite))
                Console.WriteLine($"{category}\t{sprite}\t{motion}\t{(handMade ? "hand" : "-")}\t{attack}");
        }

        foreach (var archetype in EnemyArchetypes.All)
        {
            var category = archetype.IsBoss ? "boss" : archetype.Passive ? "critter" : "enemy";
            var motion = archetype.Flying ? "fly" : archetype.Hops ? "hop" : "walk";
            var attack = archetype.Passive ? "none" : archetype.ProjectileSpeed > 0f ? "ranged" : "melee";
            Line(category, SpriteCatalog.ForEnemy(archetype.Kind), motion, SpriteCatalog.HasHandMadeFrames(archetype.Kind), attack);
        }
        foreach (var npc in TownNpcCatalog.All)
            Line("npc", npc.Sprite, "walk");
        Line("npc", "npc_bound_survivor", "walk");
        foreach (var crew in SpriteCatalog.CrewSpriteNames)
            Line("crew", crew, "walk");
        foreach (var pet in new[] { "pet_pocket_drone", "pet_lumen_wisp", "pet_lantern_bot" })
            Line("pet", pet, "float");
        Line("pet", "pet_pebble_crab", "walk");
        return 0;
    }
}
