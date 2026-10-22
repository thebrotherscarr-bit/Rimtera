using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Locks in the item audit (docs/ITEM_AUDIT.md, `dotnet run -- --item-audit`): every item does something and has a
/// sprite file of its own, plus the functions the audit pass added (information gear, thorn plating, the companion
/// permit). Deliberate exceptions are listed below and must stay empty unless a reason is written next to them.
/// </summary>
public static class ItemAuditSelfTests
{
    /// <summary>Items allowed to have no function (none today).</summary>
    private static readonly HashSet<ItemId> FunctionExceptions = [];

    /// <summary>Items allowed to share or lack a sprite file (none today).</summary>
    private static readonly HashSet<ItemId> SpriteExceptions = [];

    public static void Run(Action<bool, string> check)
    {
        var spriteDirectory = ItemAudit.SpriteDirectory();
        check(Directory.Exists(spriteDirectory), "item audit: the sprite folder ships beside the build");
        var entries = ItemAudit.Run(spriteDirectory);
        var catalog = new ItemCatalog();
        check(entries.Count == catalog.All.Count(), "item audit: every registered item is audited");

        var idle = entries.Where(entry => !entry.HasFunction && entry.Kind != "material" && !FunctionExceptions.Contains(entry.Item.Id))
            .Select(entry => $"{entry.Item.Id} ({entry.Function})").ToArray();
        check(idle.Length == 0, "item audit: every non-material item has a function" + Missing(idle));
        var unused = entries.Where(entry => !entry.HasFunction && entry.Kind == "material" && !FunctionExceptions.Contains(entry.Item.Id))
            .Select(entry => entry.Item.Id.ToString()).ToArray();
        check(unused.Length == 0, "item audit: every material is crafted with, sold or worth credits" + Missing(unused));

        var unread = ItemAudit.UnknownTags(catalog.All).Select(pair => $"{pair.Item.Id}:{pair.Tag}").ToArray();
        check(unread.Length == 0, "item audit: every equipment tag is read by some system" + Missing(unread));

        var noSprite = entries.Where(entry => !entry.OwnSprite && !SpriteExceptions.Contains(entry.Item.Id))
            .Select(entry => entry.SpriteFile ? $"{entry.Item.Id} shares {entry.Sprite}" : $"{entry.Item.Id} needs {entry.Sprite}").ToArray();
        check(noSprite.Length == 0, "item audit: every item resolves to a sprite file of its own" + Missing(noSprite));

        // The probe itself: a material reaches the "no direct use" fallthrough, a handled utility does not.
        var probe = new GameSimulation(seed: 7272, width: 96, height: 56) { FamilySpawnsEnabled = false };
        var unhandled = probe.UnhandledUses;
        Use(probe, ItemId.CopperOre);
        var materialFlagged = probe.UnhandledUses == unhandled + 1;
        Use(probe, ItemId.WireTool);
        check(materialFlagged && probe.UnhandledUses == unhandled + 1, "item audit: the use probe tells a handled use from the fallthrough");

        // Companion permit: using it files it (or explains why not) instead of doing nothing.
        var permitGame = new GameSimulation(seed: 7273, width: 96, height: 56) { FamilySpawnsEnabled = false };
        var before = permitGame.UnhandledUses;
        Use(permitGame, ItemId.CompanionPermit);
        check(permitGame.UnhandledUses == before && permitGame.RecentMessages.Any(message => message.Contains("companion", StringComparison.OrdinalIgnoreCase)),
            "item audit: using a companion permit files it or says why it cannot");

        // Information gear works while carried: readouts appear and the ore scanner finds a planted ore.
        var info = new GameSimulation(seed: 7274, width: 96, height: 56) { FamilySpawnsEnabled = false };
        var noReadouts = info.InfoReadouts.Count == 0;
        foreach (var id in new[] { ItemId.CompassChip, ItemId.DepthMeter, ItemId.OreScanner, ItemId.LifeMonitor })
            info.Player.Inventory.Add(id, 1);
        var cell = RegionMap.CellAt(info.Player.Motion.Position + info.Player.Motion.Size / 2f);
        for (var y = cell.Y - GameSimulation.OreScanRadius; y <= cell.Y + GameSimulation.OreScanRadius; y++)
        for (var x = cell.X - GameSimulation.OreScanRadius; x <= cell.X + GameSimulation.OreScanRadius; x++)
            if (info.World.Contains(new GridPoint(x, y)) &&
                info.World.GetCell(new GridPoint(x, y)).TileId.ToString() is var name && (name.EndsWith("Ore") || name.EndsWith("Gem") || name == "EnergyCrystal"))
                info.World.SetTile(new GridPoint(x, y), TileId.Rock);
        var orePoint = new GridPoint(cell.X + 3, cell.Y + 4);
        info.World.SetTile(orePoint, TileId.CobaltOre);
        var lines = info.InfoReadouts;
        check(noReadouts && lines.Count == 4 && lines.Any(line => line.StartsWith("Compass:")) && lines.Any(line => line.StartsWith("Depth meter:")) &&
              info.BestOreNearby(cell) == ItemId.CobaltOre && lines.Any(line => line.Contains("cobalt", StringComparison.OrdinalIgnoreCase)),
            "item audit: compass, depth meter, ore scanner and life monitor give readouts while carried");

        // Thorn plating reflects a third of contact damage (it carried a bare "thorns" tag nothing read).
        var thorns = new GameSimulation(seed: 7275, width: 96, height: 56) { FamilySpawnsEnabled = false };
        Use(thorns, ItemId.ThornPlating);
        check(Math.Abs(thorns.GearTagValue("thorns") - 33f) < 0.01f, "item audit: thorn plating's thorns are read by the combat stats");
    }

    private static void Use(GameSimulation game, ItemId item)
    {
        game.Player.Inventory.Add(item, 1);
        game.Player.ActionCooldownTicks = 0;
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
        var target = new GridPoint((int)game.Player.Motion.Position.X + 2, (int)game.Player.Motion.Position.Y);
        game.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, target));
    }

    private static string Missing(string[] items) =>
        items.Length == 0 ? "" : $" (missing: {string.Join(", ", items.Take(12))}{(items.Length > 12 ? $" +{items.Length - 12} more" : "")})";
}
