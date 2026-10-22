using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>Development kits for <c>--quickstart --kit &lt;name&gt;</c>: fill the inventory to play-test content.</summary>
internal static class DevKits
{
    public static Action<GameSimulation>? Get(string? name) => name?.ToLowerInvariant() switch
    {
        "arsenal" => Arsenal,
        "travel" => Travel,
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

    /// <summary>One mid-tier weapon of every style on the hotbar and beyond, ammo for each family, a full armor set and wings.</summary>
    private static void Arsenal(GameSimulation game)
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
        foreach (var style in hotbar.Concat(rest))
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
