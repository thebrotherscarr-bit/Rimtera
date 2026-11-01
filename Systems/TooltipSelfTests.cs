using System.Numerics;
using System.Text.RegularExpressions;

namespace Rimtera.Systems;

/// <summary>
/// IC-17: the item tooltip tells the truth for every item. No ranged weapon claims Energy cells unless it fires them,
/// every weapon has a class-correct damage line, calibrations show their name and changes, armor shows its set bonus,
/// accessories describe their effects, the comparison line survives the move out of the renderer, and an open shop
/// shows the sell value.
/// </summary>
public static class TooltipSelfTests
{
    private static readonly Regex DamageLine = new(@"^\d+ (melee|ranged|tech|drone|sentry) damage$", RegexOptions.Compiled);

    public static void Run(Action<bool, string> check)
    {
        var catalog = new ItemCatalog();
        var all = catalog.All.ToList();
        var game = new GameSimulation(seed: 9951, width: 80, height: 48) { FamilySpawnsEnabled = false };

        var falseCells = new List<string>();
        var noDamage = new List<string>();
        var wrongClass = new List<string>();
        var empty = new List<string>();
        foreach (var item in all)
        {
            foreach (var context in new[] { new TooltipContext(), new TooltipContext(game) })
            {
                var lines = ItemTooltip.Build(new ItemStack(item.Id, 1), context);
                if (lines.Count == 0 || lines[0].Tone != TooltipTone.Name || lines[0].Text != item.Name)
                    empty.Add(item.Name);
                var usesCells = item.UsesAmmo == AmmoClass.Cell || item.AmmoType == ItemId.Arrow;
                if (item.UseKind == ItemUseKind.Ranged && !usesCells && lines.Any(line => line.Text.Contains("Energy cell", StringComparison.OrdinalIgnoreCase)))
                    falseCells.Add(item.Name);
                if (item.UseKind is ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Tech or ItemUseKind.Command || ItemTooltip.IsWeapon(item))
                {
                    var damage = lines.Where(line => DamageLine.IsMatch(line.Text)).ToList();
                    if (damage.Count != 1)
                        noDamage.Add(item.Name);
                    else if (!damage[0].Text.EndsWith($" {ItemTooltip.ClassWord(item)} damage", StringComparison.Ordinal))
                        wrongClass.Add($"{item.Name}: {damage[0].Text}");
                }
            }
        }
        var ranged = all.Count(item => item.UseKind == ItemUseKind.Ranged);
        check(falseCells.Count == 0, $"tooltip: no ranged weapon claims Energy cells unless it fires them ({ranged} ranged items; wrong: {string.Join(", ", falseCells.Distinct().Take(5))})");
        check(noDamage.Count == 0 && wrongClass.Count == 0,
            $"tooltip: every weapon of the {all.Count} items has one class-correct damage line (missing: {string.Join(", ", noDamage.Distinct().Take(5))}; wrong: {string.Join(", ", wrongClass.Take(3))})");
        check(empty.Count == 0, $"tooltip: every item's first line is its name in its rarity colour ({string.Join(", ", empty.Distinct().Take(5))})");

        // The words follow the data: Tech says tech damage and its charge cost, Command says drone damage and never crits.
        var tech = all.First(item => item.UseKind == ItemUseKind.Tech && item.ChargeCost > 0);
        var techLines = Texts(ItemTooltip.Build(new ItemStack(tech.Id, 1)));
        var drone = catalog[ItemId.ScoutDroneRemote];
        var droneLines = Texts(ItemTooltip.Build(new ItemStack(drone.Id, 1)));
        check(techLines.Any(line => line.EndsWith(" tech damage")) && techLines.Contains($"Uses {tech.ChargeCost} charge") &&
              techLines.Any(line => line.EndsWith("% critical strike chance")) && techLines.Any(line => line.EndsWith(" speed")) &&
              techLines.Any(line => line.EndsWith("knockback")) &&
              droneLines.Contains($"{drone.Damage} drone damage") && !droneLines.Any(line => line.Contains("critical")),
            $"tooltip: \"{techLines.ElementAtOrDefault(1)}\" with charge cost, crit, speed and knockback words; \"{droneLines.ElementAtOrDefault(1)}\" never crits");
        check(ItemTooltip.SpeedWord(8) == "Insanely fast speed" && ItemTooltip.SpeedWord(30) == "Average speed" && ItemTooltip.SpeedWord(60) == "Snail speed" &&
              ItemTooltip.KnockbackWord(0f) == "No knockback" && ItemTooltip.KnockbackWord(5f) == "Average knockback" &&
              ItemTooltip.KnockbackWord(12f) == "Insane knockback",
            "tooltip: Terraria's speed words from use ticks and knockback words");

        // A bow that fires arrows says so (and that it falls back to Energy cells); a gun on rounds never mentions cells.
        var gunOnRounds = all.First(item => item.UseKind == ItemUseKind.Ranged && item.UsesAmmo == AmmoClass.Bullet && item.AmmoType != ItemId.Arrow);
        var roundsLine = ItemTooltip.AmmoLine(gunOnRounds, catalog);
        var cellGun = catalog[ItemId.PlasmaPistol];
        check(roundsLine == "Uses rounds" && ItemTooltip.AmmoLine(cellGun, catalog)!.Contains("Energy cells"),
            $"tooltip: the ammo line names the family a weapon really fires ({gunOnRounds.Name}: {roundsLine}; {cellGun.Name}: {ItemTooltip.AmmoLine(cellGun, catalog)})");

        // Calibration: its name in the title and each change, better in green and worse in red.
        var sword = catalog[ItemId.CopperSword];
        var precise = ItemTooltip.Build(new ItemStack(sword.Id, 1, Calibration.Precise));
        var cracked = ItemTooltip.Build(new ItemStack(sword.Id, 1, Calibration.Cracked));
        check(precise[0].Text == $"Precise {sword.Name}" && precise.Any(line => line is { Text: "Calibration: Precise", Tone: TooltipTone.Better }) &&
              precise.Any(line => line is { Text: "+5% damage", Tone: TooltipTone.Better }) &&
              precise.Any(line => line is { Text: "+6% critical strike chance", Tone: TooltipTone.Better }) &&
              cracked.Any(line => line is { Text: "-15% damage", Tone: TooltipTone.Worse }) &&
              DamageOf(precise) > DamageOf(cracked),
            $"tooltip: a calibration shows its name and changes ({DamageOf(precise)} vs {DamageOf(cracked)} damage, Precise vs Cracked)");

        // Armor: defense, set name and bonus; accessories in plain words; consumables say what they do.
        var setPiece = all.First(item => item.UseKind == ItemUseKind.Equipment && ArmorSets.Get(item.ArmorSetId) is not null && item.Defense > 0);
        var setLines = Texts(ItemTooltip.Build(new ItemStack(setPiece.Id, 1)));
        var set = ArmorSets.Get(setPiece.ArmorSetId)!;
        var glove = all.First(item => item.ImmunityTags.Contains("auto-swing"));
        var gloveLines = Texts(ItemTooltip.Build(new ItemStack(glove.Id, 1)));
        var dodge = all.First(item => item.ImmunityTags.Any(tag => tag.StartsWith("dodge:", StringComparison.Ordinal)));
        var stim = all.First(item => item.UseKind == ItemUseKind.Consumable && item.HealAmount > 0f);
        check(setLines.Contains($"{setPiece.Defense} defense") && setLines.Contains($"Set: {set.DisplayName}") && setLines.Contains($"Set bonus: {set.Description}") &&
              gloveLines.Contains("Melee weapons swing automatically") &&
              Texts(ItemTooltip.Build(new ItemStack(dodge.Id, 1))).Any(line => line.EndsWith("chance to dodge an attack")) &&
              Texts(ItemTooltip.Build(new ItemStack(stim.Id, 1))).Contains($"Restores {stim.HealAmount:0} health"),
            $"tooltip: armor shows defense and its set ({set.DisplayName}: {set.Description}); accessories and consumables in plain words");
        var tagged = all.Where(item => item.UseKind == ItemUseKind.Equipment).SelectMany(item => item.ImmunityTags).Distinct()
            .Where(tag => ItemTooltip.TagWords(tag) is null).ToList();
        check(tagged.Count == 0, $"tooltip: every accessory and armor tag has plain words (missing: {string.Join(", ", tagged.Take(6))})");

        var (r, g, b) = ItemTooltip.RarityColor(ItemRarity.Common);
        check((r, g, b) == (255, 255, 255) && ItemTooltip.RarityColor(ItemRarity.Rare) != ItemTooltip.RarityColor(ItemRarity.Uncommon) &&
              Enum.GetValues<ItemRarity>().Select(ItemTooltip.RarityColor).Distinct().Count() == Enum.GetValues<ItemRarity>().Length,
            "tooltip: every rarity has its own name colour (common white)");

        // The comparison line (since 0.1.9): armor against what is worn, weapons against what is held.
        var compare = new GameSimulation(seed: 9952, width: 80, height: 48) { FamilySpawnsEnabled = false };
        compare.Player.Inventory.Add(ItemId.WoodenSword, 1);
        compare.TrySelectInventorySlot(compare.Player.Inventory.FindSlot(ItemId.WoodenSword));
        var against = ItemTooltip.Build(new ItemStack(ItemId.CopperSword, 1), new TooltipContext(compare));
        var held = compare.Items[ItemId.WoodenSword];
        var gap = (int)MathF.Round(compare.Items[ItemId.CopperSword].Damage - held.Damage);
        var helmet = ItemTooltip.Build(new ItemStack(ItemId.Trailcap, 1), new TooltipContext(compare));
        check(against.Any(line => line.Text == $"{gap:+0;-0} damage compared with {held.Name}" && line.Tone == (gap > 0 ? TooltipTone.Better : TooltipTone.Worse)) &&
              helmet.Any(line => line.Text.Contains("nothing worn there")),
            $"tooltip: the comparison line measures a weapon against the one held ({gap:+0;-0}) and armor against the slot");

        // An open shop shows the sell value.
        var npc = compare.AddTownNpc(TownNpcKind.Quartermaster, compare.Player.Motion.Position + new Vector2(1f, 0f), bound: false);
        var stack = new ItemStack(ItemId.CopperSword, 1);
        var priced = ItemTooltip.Build(stack, new TooltipContext(compare, npc));
        var unit = compare.GetSellPrice(npc, stack);
        check(priced.Any(line => line is { Tone: TooltipTone.Price } && line.Text == ItemTooltip.SellLine(unit)) &&
              !ItemTooltip.Build(stack, new TooltipContext(compare)).Any(line => line.Tone == TooltipTone.Price),
            $"tooltip: an open shop shows the sell value ({ItemTooltip.SellLine(unit)}); with no shop there is none");
    }

    private static List<string> Texts(IReadOnlyList<TooltipLine> lines) => lines.Select(line => line.Text).ToList();

    private static int DamageOf(IReadOnlyList<TooltipLine> lines) =>
        lines.Select(line => DamageLine.Match(line.Text)).Where(match => match.Success).Select(match => int.Parse(match.Value.Split(' ')[0])).FirstOrDefault();
}
