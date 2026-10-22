using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Content-fill items (band 8600–8999): a catch item for every new critter (bait power from the data table; gold variants
/// are rare, valuable bait), the biome timbers dropped by the new tree species, and the materials of the harvestable
/// wild plants, plus a few recipes that put them to use. Names and numbers are original Rimtera data.
/// </summary>
public static class ContentFillItems
{
    /// <summary>Biome timber items with their display names.</summary>
    public static readonly (ItemId Item, string Name)[] Timbers =
    [
        (ItemId.Vinewood, "Vinewood log"), (ItemId.Palmwood, "Fuel-palm log"), (ItemId.Stalkwood, "Shroom stalk"),
        (ItemId.Prismwood, "Prismwood log"), (ItemId.Blightwood, "Blightwood log"), (ItemId.Gemwood, "Geode bark")
    ];

    /// <summary>Wild-plant harvest materials with their display names.</summary>
    public static readonly (ItemId Item, string Name)[] PlantMaterials =
    [
        (ItemId.PlantFiber, "Plant fiber"), (ItemId.FrostLichen, "Frost lichen"), (ItemId.ScrapThistle, "Scrap thistle"),
        (ItemId.SeaFennel, "Sea fennel"), (ItemId.CanopyPod, "Canopy pod"), (ItemId.GlowcapCluster, "Glowcap cluster"),
        (ItemId.PrismBloom, "Prism bloom"), (ItemId.BlightBulb, "Blight bulb")
    ];

    public static void RegisterAll(Dictionary<ItemId, ItemDefinition> items)
    {
        foreach (var critter in ContentFillData.Critters)
        {
            if (!Enum.TryParse<ItemId>(critter.Kind.ToString(), out var id))
                continue;
            var gold = critter.GoldOf is not null;
            items[id] = new ItemDefinition(id, critter.Name, ItemUseKind.Critter, gold ? 5 : 20)
            {
                ReleasesCritter = critter.Kind, Value = ContentFillCreatures.CatchValue(critter), BaitPower = critter.BaitPower,
                Rarity = gold ? ItemRarity.Rare : ItemRarity.Common
            };
        }
        foreach (var (id, name) in Timbers)
            items[id] = new ItemDefinition(id, name, ItemUseKind.Material, 999) { IsMaterial = true, Value = 3 };
        foreach (var (id, name) in PlantMaterials)
            items[id] = new ItemDefinition(id, name, ItemUseKind.Material, 999) { IsMaterial = true, Value = 4 };
    }

    public static void RegisterRecipes(CraftingSystem crafting)
    {
        void Add(string id, ItemId output, int count, CraftingStationId? station, params (ItemId Item, int Count)[] ingredients) =>
            crafting.Register(new RecipeDefinition("fill-" + id, output, count,
                ingredients.Select(entry => IngredientRequirement.Exact(entry.Item, entry.Count)).ToArray(),
                station is { } required ? new HashSet<CraftingStationId> { required } : null));

        // Every biome timber saws into ordinary timber, so the new trees feed the existing building recipes.
        foreach (var (id, _) in Timbers)
            Add("timber-" + WorldFlags.Key(id), ItemId.Timber, 1, null, (id, 1));
        Add("fiber-cloth", ItemId.ClothRoll, 1, CraftingStationId.Loom, (ItemId.PlantFiber, 6));
        Add("fiber-rope", ItemId.RopeCoil, 1, null, (ItemId.PlantFiber, 4));
        Add("lichen-tonic", ItemId.RestorativeTonic, 1, CraftingStationId.ChemLab, (ItemId.FrostLichen, 3), (ItemId.Berry, 1));
        Add("thistle-gel", ItemId.BioGel, 2, null, (ItemId.ScrapThistle, 3));
        Add("fennel-ration", ItemId.RationBar, 1, CraftingStationId.CookingStation, (ItemId.SeaFennel, 2), (ItemId.Berry, 1));
        Add("canopy-pod", ItemId.Berry, 2, null, (ItemId.CanopyPod, 1));
        Add("glowcap-spore", ItemId.GlowSpore, 2, null, (ItemId.GlowcapCluster, 1));
        Add("prism-bloom", ItemId.PrismShard, 1, CraftingStationId.Workbench, (ItemId.PrismBloom, 3));
        Add("blight-bulb", ItemId.PurifierGel, 1, CraftingStationId.ChemLab, (ItemId.BlightBulb, 4));
    }
}
