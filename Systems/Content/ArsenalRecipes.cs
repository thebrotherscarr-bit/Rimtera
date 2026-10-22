using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Recipes for the arsenal pass: ladder gear from its tier bar at its tier forge, themed uniques from bar plus a
/// biome material, explicit ingredient lists for ammo, accessories (combined ones from their components), wings,
/// potions and food. Loot-only items (boss/creature drops, caches, shops) get no recipe.
/// </summary>
internal static class ArsenalRecipes
{
    public static void RegisterAll(CraftingSystem crafting, ItemCatalog items)
    {
        var used = new HashSet<string>(crafting.Recipes.Select(recipe => recipe.Id), StringComparer.OrdinalIgnoreCase);
        void Add(ItemId output, int count, IEnumerable<CraftingStationId> stations, IEnumerable<(ItemId Item, int Count)> ingredients)
        {
            var id = "ar-" + output.ToString().ToLowerInvariant();
            for (var n = 2; !used.Add(id); n++)
                id = $"ar-{output.ToString().ToLowerInvariant()}-{n}";
            var groups = ingredients.GroupBy(entry => entry.Item)
                .Select(group => IngredientRequirement.Exact(group.Key, group.Sum(entry => entry.Count))).ToArray();
            var set = stations.Where(station => station != CraftingStationId.None).ToHashSet();
            crafting.Register(new RecipeDefinition(id, output, count, groups, set.Count == 0 ? null : set));
        }

        foreach (var id in ArsenalItems.AllIds)
        {
            if (ArsenalItems.LootSources.ContainsKey(id) && !ArsenalItems.RecipeHints.ContainsKey(id))
                continue;
            var item = items[id];
            var yield = ArsenalItems.RecipeYield.GetValueOrDefault(id, 1);
            if (ArsenalItems.RecipeHints.TryGetValue(id, out var hint) && !hint.StartsWith("craft:", StringComparison.Ordinal))
            {
                var (ingredients, station) = Parse(hint);
                Add(id, yield, [station], ingredients);
                continue;
            }
            // Ladder gear: tier bar (or the themed material on top of it) at the tier forge.
            var tier = Math.Clamp(item.Tier, 1, 7);
            var key = ArsenalItems.Materials.Keys.FirstOrDefault(name => id.ToString().StartsWith(name, StringComparison.Ordinal));
            var material = key is null ? default : ArsenalItems.Materials[key];
            var bar = material.Bar != default ? material.Bar : ContentItems.TierBars[tier];
            var extra = material.Bar != default ? (material.Extra, material.ExtraCount) : (ItemId.EnergyCrystal, 2 + tier);
            var stations = new List<CraftingStationId> { ContentRecipes.TierStations[tier] };
            var list = new List<(ItemId, int)>();
            var thrown = item.Style == WeaponStyle.Thrown;
            int bars = thrown ? 1 : item.UseKind switch
            {
                ItemUseKind.Melee => item.SignalWhip ? 6 : 8,
                ItemUseKind.Ranged => 10,
                ItemUseKind.Tech => 7,
                ItemUseKind.Command => 8,
                ItemUseKind.Pickaxe => 10,
                ItemUseKind.Cutter => 8,
                ItemUseKind.Demolisher => 9,
                _ => item.EquipmentSlot switch { EquipmentSlot.Head => 10, EquipmentSlot.Body => 14, EquipmentSlot.Legs => 12, _ => 8 }
            };
            list.Add((bar, bars));
            list.Add(thrown ? (extra.Item1, 1) : extra);
            if (hint is not null && hint.StartsWith("craft:", StringComparison.Ordinal))
            {
                var themed = Enum.Parse<ItemId>(hint[6..]);
                list.Add((themed, thrown ? 2 : item.EquipmentSlot is not null && item.UseKind == ItemUseKind.Equipment ? 6 : 8));
                if (item.WingFlightSeconds > 0f)
                {
                    list.Add((ItemId.ClothRoll, 4));
                    stations = [CraftingStationId.TinkerBench];
                }
            }
            if (item.UseKind == ItemUseKind.Tech)
                list.Add((ItemId.EnergyCrystal, 3));
            if (item.UseKind == ItemUseKind.Command && tier >= 2)
            {
                list.Add((ItemId.DroneSpareParts, 2));
                stations.Add(CraftingStationId.DroneBay);
            }
            if (item.SignalWhip)
                list.Add((ItemId.RopeCoil, 2));
            if (item.UseKind == ItemUseKind.Equipment && item.ArmorSetId is not null && tier >= 3)
                list.Add((ItemId.ClothRoll, 2));
            Add(id, yield, stations, list);
        }
        GrowthItems.RegisterRecipes(crafting); // character growth: the hook ladder
    }

    /// <summary>"A*2+B*1@Station" → ingredients and station.</summary>
    internal static (List<(ItemId Item, int Count)> Ingredients, CraftingStationId Station) Parse(string recipe)
    {
        var at = recipe.IndexOf('@');
        var station = at < 0 ? CraftingStationId.TinkerBench : Enum.Parse<CraftingStationId>(recipe[(at + 1)..]);
        var body = at < 0 ? recipe : recipe[..at];
        var list = new List<(ItemId, int)>();
        foreach (var part in body.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var star = part.IndexOf('*');
            list.Add((Enum.Parse<ItemId>(part[..star]), int.Parse(part[(star + 1)..])));
        }
        return (list, station);
    }
}
