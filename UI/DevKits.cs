using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>Development kits for <c>--quickstart --kit &lt;name&gt;</c>: fill the inventory to play-test content.</summary>
internal static class DevKits
{
    public static Action<GameSimulation>? Get(string? name) => name?.ToLowerInvariant() switch
    {
        "arsenal" => game => Arsenal(game, restFirst: false),
        "travel" => Travel,
        "tools" => Tools,
        "arsenal2" => game => Arsenal(game, restFirst: true),
        _ => null
    };

    /// <summary>Glide-walks, pylons and every mount key, plus a demo walkway laid into the ground at the spawn.</summary>
    private static void Travel(GameSimulation game)
    {
        var stacks = new List<ItemStack> { new(ItemId.GlideWalk, 200), new(ItemId.ExpressWalk, 200), new(ItemId.ColonyPylon, 2) };
        stacks.AddRange(TravelContent.Mounts.Values.Select(spec => new ItemStack(spec.Item, 1)));
        var slots = new ItemStack[game.Player.Inventory.SlotCount];
        for (var i = 0; i < slots.Length && i < stacks.Count; i++)
            slots[i] = stacks[i];
        game.Player.Inventory.RestoreState(slots, 0);

        var motion = game.Player.Motion;
        var row = (int)MathF.Floor(motion.Position.Y + motion.Size.Y + 0.05f);
        var startX = (int)MathF.Floor(motion.Position.X) - 4;
        for (var i = 0; i < 40; i++)
        {
            var tile = i < 20 ? TileId.GlideWalkRight : TileId.ExpressWalkRight;
            var cell = new GridPoint(startX + i, row);
            for (var y = row - 3; y < row; y++)
                game.World.SetTile(new GridPoint(cell.X, y), TileId.Air);
            game.PlaceTravelTileForTests(cell, tile);
        }
    }

    /// <summary>
    /// Every non-weapon use style on the hotbar (hatchet, pickaxe, hammer, food, potion, boss summon, block, bucket, rod,
    /// bug net), more behind it (drill, torch, recall, glow sticks, sprayer, drone remote), and worn accessories of the
    /// visual gear categories (wings, rocket boots, a lamp, speed boots, a shield).
    /// </summary>
    private static void Tools(GameSimulation game)
    {
        var items = game.Items.All.ToArray();
        ItemDefinition? Pick(Func<ItemDefinition, bool> match) =>
            items.Where(match).OrderBy(item => Math.Abs(item.Tier - 3)).ThenBy(item => item.Id).FirstOrDefault();
        bool Tagged(ItemDefinition item, string tag) => item.ImmunityTags.Any(t => t.StartsWith(tag, StringComparison.OrdinalIgnoreCase));
        bool Accessory(ItemDefinition item) => item.EquipmentSlot == EquipmentSlot.Accessory && item.UseKind == ItemUseKind.Equipment;
        var picks = new List<(ItemDefinition? Item, int Count)>
        {
            (Pick(item => item.UseKind == ItemUseKind.Cutter && !ToolParity.IsChannelTool(item.Id)), 1),
            (Pick(item => item.UseKind == ItemUseKind.Pickaxe && !ToolParity.IsChannelTool(item.Id)), 1),
            (Pick(item => item.UseKind == ItemUseKind.Demolisher && !ToolParity.IsChannelTool(item.Id)), 1),
            (Pick(item => item.UseKind == ItemUseKind.Food), 20),
            (Pick(item => item.UseKind == ItemUseKind.Consumable && item.HealAmount > 0f), 20),
            (Pick(item => item.SummonsBoss is not null), 3),
            (items.FirstOrDefault(item => item.Id == ItemId.SoilBlock) ?? Pick(item => item.UseKind == ItemUseKind.PlaceTile), 200),
            (items.FirstOrDefault(item => item.Id == ItemId.BucketWater), 5),
            (items.FirstOrDefault(item => item.Id == ItemId.FishingRod), 1),
            (Pick(item => item.UseKind == ItemUseKind.BugNet), 1),
            (Pick(item => ToolParity.IsChannelTool(item.Id)), 1),
            (Pick(item => item.Name.Contains("torch", StringComparison.OrdinalIgnoreCase) && item.UseKind == ItemUseKind.PlaceTile), 50),
            (items.FirstOrDefault(item => item.Id == ItemId.RecallCapsule), 5),
            (items.FirstOrDefault(item => item.Id == ItemId.GlowSticks), 50),
            (items.FirstOrDefault(item => item.Id == ItemId.PaintSprayer), 1),
            (Pick(item => item.StartsEvent is not null), 2),
            (Pick(item => item.UseKind == ItemUseKind.Consumable && item.ChargeRestore > 0f), 10)
        };
        var accessories = new List<ItemDefinition?>
        {
            Pick(item => Accessory(item) && item.WingFlightSeconds > 0f),
            Pick(item => Accessory(item) && Tagged(item, "rocket-boots")),
            Pick(item => Accessory(item) && Tagged(item, "light")),
            Pick(item => Accessory(item) && item.MoveSpeedMultiplier > 1.001f && !Tagged(item, "rocket-boots")),
            Pick(item => Accessory(item) && item.Defense > 0)
        };
        var slots = new ItemStack[game.Player.Inventory.SlotCount];
        var index = 0;
        foreach (var (item, count) in picks)
            if (item is not null && index < slots.Length)
                slots[index++] = new ItemStack(item.Id, Math.Min(count, item.MaxStack));
        var firstAccessory = Math.Max(index, 30);
        var at = firstAccessory;
        foreach (var accessory in accessories.Where(item => item is not null).Distinct())
            if (at < slots.Length)
                slots[at++] = new ItemStack(accessory!.Id, 1);
        game.Player.Inventory.RestoreState(slots, 0);
        for (var slot = firstAccessory; slot < at; slot++)
            game.TryEquipFromInventory(slot);
    }

    /// <summary>One mid-tier weapon of every style on the hotbar and beyond, ammo for each family, a full armor set and wings.</summary>
    /// <param name="restFirst">Put the styles that do not fit the hotbar (shortsword, gun, thrown, staff, tome, summons) first.</param>
    private static void Arsenal(GameSimulation game, bool restFirst)
    {
        var items = game.Items.All.ToArray();
        ItemDefinition? Best(Func<ItemDefinition, bool> match) =>
            items.Where(match).OrderBy(item => Math.Abs(item.Tier - 4)).ThenBy(item => item.Id).FirstOrDefault();
        WeaponStyle[] hotbar =
        [
            WeaponStyle.Broadsword, WeaponStyle.Spear, WeaponStyle.Yoyo, WeaponStyle.Boomerang, WeaponStyle.Flail,
            WeaponStyle.Whip, WeaponStyle.Bow, WeaponStyle.Shotgun, WeaponStyle.Launcher, WeaponStyle.Beam
        ];
        WeaponStyle[] rest =
        [
            WeaponStyle.Shortsword, WeaponStyle.Gun, WeaponStyle.Thrown, WeaponStyle.Staff, WeaponStyle.Tome,
            WeaponStyle.SummonStaff, WeaponStyle.SentryStaff
        ];
        var stacks = new List<ItemStack>();
        foreach (var style in restFirst ? rest.Concat(hotbar) : hotbar.Concat(rest))
            if (Best(item => item.Style == style) is { } weapon)
                stacks.Add(new ItemStack(weapon.Id, weapon.Style == WeaponStyle.Thrown ? 200 : 1));
        foreach (var family in new[] { AmmoClass.Arrow, AmmoClass.Bullet, AmmoClass.Rocket, AmmoClass.Dart, AmmoClass.Fuel })
            if (Best(item => item.AmmoKind == family && item.UseKind == ItemUseKind.Ammunition) is { } ammo)
                stacks.Add(new ItemStack(ammo.Id, Math.Min(ammo.MaxStack, 999)));
        var set = items.Where(item => item.ArmorSetId is not null && item.Tier >= 4)
            .GroupBy(item => item.ArmorSetId!).FirstOrDefault(group => group.Select(item => item.EquipmentSlot).Distinct().Count() == 3);
        if (set is not null)
            stacks.AddRange(set.Select(item => new ItemStack(item.Id, 1)));
        if (Best(item => item.UseKind == ItemUseKind.Equipment && item.Id.ToString().Contains("Wing", StringComparison.Ordinal)) is { } wings)
            stacks.Add(new ItemStack(wings.Id, 1));

        var slots = new ItemStack[game.Player.Inventory.SlotCount];
        for (var i = 0; i < slots.Length && i < stacks.Count; i++)
            slots[i] = stacks[i];
        game.Player.Inventory.RestoreState(slots, 0);
    }
}
