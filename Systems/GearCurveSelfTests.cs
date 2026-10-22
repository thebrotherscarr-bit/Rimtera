using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The Terraria-shaped gear curve (GearCurve, docs/GEAR_DIFF_ITEMS.md): drills climb per tier and each mines the next
/// tier's ore, armour sets climb with Terraria's shape (a hardmode jump, then a late jump), weapon bases climb per class,
/// and the three starting items sit on the copper tier.
/// </summary>
public static class GearCurveSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var items = new ItemCatalog();
        var tiles = new TileCatalog();
        int Gate(TileId tile) => tiles[tile].MinimumToolPower;

        // ---- Tools: drills climb per tier, mine faster per tier, and each one opens the next ore ----
        ItemId[] drills = [ItemId.FerriteDrill, ItemId.CobaltDrill, ItemId.GlacialDrill, ItemId.ViridiumDrill,
            ItemId.LumeniteDrill, ItemId.VoidsteelDrill, ItemId.AstralDrill];
        ItemId[] picks = [ItemId.FerritePick, ItemId.CobaltPick, ItemId.GlacialPick, ItemId.ViridiumPick,
            ItemId.LumenitePick, ItemId.VoidsteelPick, ItemId.AstralPick];
        bool Climbs(IEnumerable<float> values) => values.Zip(values.Skip(1)).All(pair => pair.Second > pair.First);
        bool Climbs2(IEnumerable<float> values) => values.Zip(values.Skip(1)).All(pair => pair.Second >= pair.First);
        var drillPower = drills.Select(id => (float)items[id].ToolPower).ToArray();
        var drillRate = drills.Select(id => items[id].ToolPower / (float)items[id].UseCooldownTicks).ToArray();
        check(Climbs(drillPower) && Climbs(drillRate) && drills.Zip(picks).All(pair => items[pair.First].ToolPower == items[pair.Second].ToolPower),
            $"drill power and mining rate climb every tier, and each tier's pick matches its drill ({string.Join("/", drillPower)})");

        var ferrite = items[ItemId.FerriteDrill].ToolPower;
        var cobalt = items[ItemId.CobaltDrill].ToolPower;
        var glacial = items[ItemId.GlacialDrill].ToolPower;
        var viridium = items[ItemId.ViridiumDrill].ToolPower;
        var lumenite = items[ItemId.LumeniteDrill].ToolPower;
        var voidsteel = items[ItemId.VoidsteelDrill].ToolPower;
        check(ferrite >= Gate(TileId.CobaltOre) &&                                             // tier 1 mines tier 2 ore
              cobalt >= Gate(TileId.GlacialOre) && cobalt >= Gate(TileId.UraniumOre) && cobalt >= Gate(TileId.PyrociteOre) &&
              cobalt < Gate(TileId.Meteorite) &&                                              // iron/lead (40-43) cannot mine meteorite (50)
              glacial >= Gate(TileId.Meteorite) && glacial >= Gate(TileId.ViridiumOre) && glacial < Gate(TileId.LumeniteOre) &&
              viridium >= Gate(TileId.LumeniteOre) && viridium < Gate(TileId.VoidsteelOre) &&  // cobalt pick (110) mines mythril, not adamantite
              lumenite >= Gate(TileId.VoidsteelOre) && voidsteel > lumenite,
            $"each tier's drill mines the next tier's ore and no further (ferrite {ferrite}, cobalt {cobalt}, glacial {glacial}, viridium {viridium}, lumenite {lumenite})");
        check(items[ItemId.PyrocitePick].ToolPower >= Gate(TileId.DungeonBrickPrecursor) && items[ItemId.PyrocitePick].ToolPower < Gate(TileId.LumeniteOre) &&
              items[ItemId.MeteoritePick].ToolPower > items[ItemId.GlacialPick].ToolPower,
            "the pyrocite pick (the molten analog) opens dungeon plating but not lumenite; the meteorite pick sits above glacial");
        check(GearCurve.DrillPowerFromTerraria(35) == 35 && GearCurve.DrillPowerFromTerraria(55) == 55 &&
              GearCurve.DrillPowerFromTerraria(110) == Gate(TileId.LumeniteOre) && GearCurve.DrillPowerFromTerraria(150) == Gate(TileId.VoidsteelOre),
            "Terraria pick power maps 1:1 before hardmode and lands Terraria's mythril/adamantite gates on the lumenite/voidsteel gates");
        var tiered = items.All.Where(item => item.UseKind == ItemUseKind.Pickaxe && item.Tier is >= 1 and <= 7 && item.Id != ItemId.CoreBreaker).ToArray();
        var offCurve = tiered.Where(item => item.ToolPower < GearCurve.DrillPower(item.Tier) ||
                                            item.Tier < 7 && item.ToolPower >= GearCurve.DrillPower(item.Tier + 1)).Select(item => item.Id).ToArray();
        check(offCurve.Length == 0,
            $"every drill and pick sits between its tier's curve value and the next tier's (off: {string.Join(",", offCurve)})");

        var cutters = new[] { ItemId.FerriteCutter, ItemId.CobaltCutter, ItemId.GlacialCutter, ItemId.ViridiumCutter, ItemId.LumeniteCutter,
            ItemId.VoidsteelCutter, ItemId.AstralCutter }.Select(id => items[id]).ToArray();
        var hammers = new[] { ItemId.FerriteDemolisher, ItemId.CobaltDemolisher, ItemId.GlacialDemolisher, ItemId.ViridiumDemolisher,
            ItemId.LumeniteDemolisher, ItemId.VoidsteelDemolisher, ItemId.AstralDemolisher }.Select(id => items[id]).ToArray();
        check(Climbs(cutters.Select(item => (float)item.ToolPower)) && Climbs2(cutters.Select(item => -(float)item.UseCooldownTicks)) &&
              Climbs(hammers.Select(item => (float)item.ToolPower)) && Climbs2(hammers.Select(item => -(float)item.UseCooldownTicks)),
            "cutter (axe %) and demolisher (hammer) power climb per tier while their use time never grows");

        // ---- Starting kit: the three items sit on Terraria's copper tier ----
        var stiletto = items[ItemId.FerriteStiletto];
        var drill = items[ItemId.CopperPickaxe];
        var hatchet = items[ItemId.FerriteHatchet];
        check(stiletto.Damage == (int)MathF.Round(5 * GearCurve.DamageScale) && stiletto.UseCooldownTicks is >= 11 and <= 13 &&
              stiletto.CritChance == 4 && stiletto.Damage < items[ItemId.FerriteLongblade].Damage &&
              items[ItemId.FerriteLongblade].Damage == (int)MathF.Round(9 * GearCurve.DamageScale),
            $"the ferrite stiletto is the copper shortsword on Rimtera's damage scale ({stiletto.Damage} dmg, {stiletto.UseCooldownTicks} ticks; copper: 5 dmg, 13 ticks)");
        check(drill.ToolPower == 35 && drill.UseCooldownTicks == (int)MathF.Round(15 * GearCurve.ToolUseTimeScale) &&
              drill.ToolPower >= Gate(TileId.CobaltOre) && drill.ToolPower < Gate(TileId.Meteorite),
            $"the starting drill is the copper pickaxe (power {drill.ToolPower}, {drill.UseCooldownTicks} ticks = useTime 15 × {GearCurve.ToolUseTimeScale})");
        check(hatchet.ToolPower == 7 * 5 && hatchet.UseCooldownTicks == (int)MathF.Round(21 * GearCurve.ToolUseTimeScale) &&
              hatchet.ToolPower == items[ItemId.FerriteCutter].ToolPower,
            $"the ferrite hatchet is the copper axe (cutter power = axe × 5 = {hatchet.ToolPower}%)");

        // ---- Armour: set defense climbs on Terraria's curve shape ----
        int SetDefense(IEnumerable<ItemDefinition> set) => set.Sum(item => item.Defense) + set.Max(item => item.SetBonusDefense);
        var sets = items.All.Where(item => item.ArmorSetId is not null && item.Tier is >= 1 and <= 7 &&
                                           item.EquipmentSlot is EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Legs)
            .GroupBy(item => item.ArmorSetId!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.EquipmentSlot).Distinct().Count() == 3).ToArray();
        var best = Enumerable.Range(1, 7).Select(tier => sets.Where(set => set.First().Tier == tier).Select(SetDefense).DefaultIfEmpty(0).Max()).ToArray();
        check(Climbs(best.Select(value => (float)value)) && best.Select((value, index) => Math.Abs(value - GearCurve.HeavySetDefense[index + 1]) <= 1).All(ok => ok),
            $"the heaviest set of every tier climbs and matches the curve ({string.Join("/", best)})");
        check(best[3] >= 1.6f * best[2] && best[6] >= 1.3f * best[5] && best[2] <= 2.5f * best[0],
            $"armour keeps Terraria's shape: a hardmode jump at tier 4 (Terraria cobalt 32 vs gold 18) and a late jump at tier 7 (solar 78 vs hallowed 50)");
        bool ClassOrder(int tier)
        {
            int Of(DamageClass damageClass) => SetDefense(Enumerable.Range(0, 3).Select(index =>
            {
                var (head, body, legs, bonus) = GearCurve.ArmorPieces(tier, damageClass);
                return new ItemDefinition(ItemId.Berry, "probe", ItemUseKind.Equipment, 1) { Defense = index switch { 0 => head, 1 => body, _ => legs }, SetBonusDefense = bonus };
            }));
            return Of(DamageClass.Melee) > Of(DamageClass.Ranged) && Of(DamageClass.Ranged) > Of(DamageClass.Tech) && Of(DamageClass.Tech) > Of(DamageClass.Command);
        }
        check(Enumerable.Range(4, 4).All(ClassOrder) &&
              sets.All(set => set.Single(item => item.EquipmentSlot == EquipmentSlot.Body).Defense >= set.Single(item => item.EquipmentSlot == EquipmentSlot.Head).Defense),
            "hardmode class sets order melee > ballistics > tech > command like Terraria's, and every chestplate is at least its helmet");

        // ---- Weapons: class bases climb, hardmode jumps, charge costs follow Terraria's mana ----
        check(new[] { GearCurve.MeleeBase, GearCurve.RangedBase, GearCurve.TechBase, GearCurve.CommandBase, GearCurve.ChargeBase }
                  .All(table => Climbs2(table.Skip(1).Select(value => (float)value)) && table[7] > table[1]) &&
              GearCurve.MeleeBase[4] >= 1.3f * GearCurve.MeleeBase[3] && GearCurve.MeleeBase[7] >= 1.5f * GearCurve.MeleeBase[6],
            "per-class weapon bases climb every tier, with Terraria's hardmode jump (tier 4) and endgame jump (tier 7)");
        check(items[ItemId.FerriteEmitterStaff].ChargeCost == GearCurve.ChargeBase[1] && items[ItemId.AstralEmitterStaff].ChargeCost == GearCurve.ChargeBase[7] &&
              items[ItemId.FerriteEmitterStaff].UseCooldownTicks > items[ItemId.AstralEmitterStaff].UseCooldownTicks,
            "emitter staves cost Terraria's mana per cast (7 early, 15 late) and cast faster at higher tiers (gem staff 37 ticks → 14)");
        check(items[ItemId.FerriteLongblade].UseCooldownTicks == 20 && items[ItemId.FerriteArcSpear].UseCooldownTicks == 28,
            "broadswords and spears use Terraria's use times (20 and 28 ticks at 60 ticks a second)");
    }
}
