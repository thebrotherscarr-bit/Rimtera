using System.Text.Json;
using System.Text.Json.Serialization;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Defense against hits, the per-band balance it is tuned to, and accessory calibrations surviving saves.</summary>
public static class DefenseSelfTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private static GameSimulation NewGame(int seed) => new(seed: seed, width: 80, height: 48);

    /// <summary>The best complete armor set of a tier (head, body and legs pieces).</summary>
    private static ItemDefinition[] BestSet(ItemCatalog catalog, int tier) => catalog.All
        .Where(item => item.ArmorSetId is not null && item.Tier == tier && item.EquipmentSlot is EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Legs)
        .GroupBy(item => item.ArmorSetId!, StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Select(item => item.EquipmentSlot).Distinct().Count() == 3)
        .OrderByDescending(group => group.Sum(item => item.Defense) + group.Max(item => item.SetBonusDefense))
        .First().ToArray();

    private static void Wear(GameSimulation game, IEnumerable<ItemDefinition> pieces)
    {
        foreach (var piece in pieces)
            game.Player.Equipment.Set(piece.EquipmentSlot!.Value, piece.Id);
    }

    public static void Run(Action<bool, string> check)
    {
        var catalog = new ItemCatalog();
        var setT4 = BestSet(catalog, 4);

        // ---- Defense on hits ----
        var plain = NewGame(8301);
        var armored = NewGame(8301);
        Wear(armored, setT4);
        var defense = armored.TotalDefense;
        var plainHit = plain.GearProbeHit(30f);
        var armoredHit = armored.GearProbeHit(30f);
        check(defense > 0 && Math.Abs(plainHit - 30f) < 0.01f && Math.Abs(armoredHit - (30f - defense * 0.5f)) < 0.01f,
            $"worn defense ({defense}) takes half its value off a creature hit on Classic ({armoredHit:0.#} of 30)");
        check(Math.Abs(armored.GearProbeHit(2f) - 1f) < 0.01f, "a hit weaker than the defense share still does 1 damage");

        var brutal = NewGame(8301);
        brutal.Difficulty = WorldDifficulty.Brutal;
        Wear(brutal, setT4);
        check(Math.Abs(brutal.GearProbeHit(30f) - (30f - defense * 0.75f)) < 0.01f, "Brutal worlds let defense take off three quarters of its value");

        var poisoned = NewGame(8302);
        Wear(poisoned, setT4);
        var before = poisoned.Player.HitPoints;
        poisoned.Player.TakeDamage(5f, DamageKind.Toxin);
        check(Math.Abs(before - poisoned.Player.HitPoints - 5f) < 0.01f, "damage over time and hazards ignore defense");

        // ---- Balance: tier armor against its band ----
        var band = EnemyArchetypes.All.Where(archetype => !archetype.Passive && !archetype.IsBoss && archetype.HitPoints is >= 150f and < 400f)
            .Select(archetype => archetype.AttackDamage * DefenseBalance.EnemyHitScale(archetype)).OrderBy(damage => damage).ToArray();
        var median = band[band.Length / 2];
        var t4Defense = setT4.Sum(item => item.Defense) + setT4.Max(item => item.SetBonusDefense);
        var cut = 1f - DefenseBalance.ApplyDefense(median, t4Defense, WorldDifficulty.Classic) / median;
        check(cut is >= 0.3f and <= 0.6f, $"tier-4 armor removes 30-60% of a typical 150-400 HP creature's hit ({cut:P0})");
        check(catalog.All.Where(item => item.EquipmentSlot is >= EquipmentSlot.Accessory).All(item => item.Defense <= DefenseBalance.MaximumAccessoryDefense),
            $"no accessory gives more than {DefenseBalance.MaximumAccessoryDefense} defense");
        var early = EnemyArchetypes.All.Where(archetype => !archetype.IsBoss && archetype.HitPoints <= DefenseBalance.EarlyGameHitPoints);
        check(early.All(archetype => DefenseBalance.EnemyHitScale(archetype) == 1f) &&
              DefenseBalance.EnemyHitScale(EnemyArchetypes.Get(EnemyKind.AstroGod)) > DefenseBalance.EnemyHitScale(EnemyArchetypes.Get(EnemyKind.StoneSentinel)),
            "early creatures keep their damage; later bosses hit harder than the gate boss");

        // ---- Accessory calibrations persist ----
        var accessory = catalog.All.First(item => CalibrationCatalog.IsAccessory(item) && item.GrantsStatus is null);
        var calibrated = NewGame(8303);
        calibrated.Player.Inventory.Add(accessory.Id, 1, Calibration.Plated);
        calibrated.TryEquipFromInventory(calibrated.Player.Inventory.FindSlot(accessory.Id));
        var slot = EquipmentLoadout.AccessorySlots.FirstOrDefault(candidate => calibrated.Player.Equipment[candidate] == accessory.Id);
        var worn = calibrated.Player.Equipment[slot] == accessory.Id && calibrated.EquippedCalibration(slot) == Calibration.Plated;
        var viaJson = GameSimulation.Restore(JsonSerializer.Deserialize<GameSnapshot>(JsonSerializer.Serialize(calibrated.CaptureSnapshot(), Json), Json)!);
        check(worn && viaJson.EquippedCalibration(slot) == Calibration.Plated && viaJson.TotalDefense == calibrated.TotalDefense,
            "a worn accessory's calibration survives a JSON save and load (defense included)");

        var directory = Path.Combine(Path.GetTempPath(), "rimtera-defense-" + Guid.NewGuid().ToString("N"));
        try
        {
            var saves = new Rimtera.UI.WorldSaveRepository(directory);
            var worldId = Guid.NewGuid();
            saves.Save(worldId, calibrated.CaptureSnapshot(binaryCells: true));
            var loaded = saves.TryLoad(worldId, out var snapshot) && snapshot is not null ? GameSimulation.Restore(snapshot) : null;
            check(loaded?.EquippedCalibration(slot) == Calibration.Plated, "a worn accessory's calibration survives the binary save files");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }

        var switching = calibrated;
        var switched = switching.SwitchLoadout(1) && switching.EquippedCalibration(slot) == Calibration.None && switching.SwitchLoadout(0);
        check(switched && switching.EquippedCalibration(slot) == Calibration.Plated, "switching loadouts and back keeps a worn accessory's calibration");
    }
}
