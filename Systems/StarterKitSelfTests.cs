using Rimtera.Characters;

namespace Rimtera.Systems;

/// <summary>The new-character starting kit (StarterKits): Classic gets the ferrite trio plus food, Creative the cobalt supply drop.</summary>
public static class StarterKitSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var previous = CharacterFileStore.Active;
        CharacterFileStore.Active = null; // no character file may feed extra items into these fresh characters
        try
        {
            var classicProfile = CharacterDraft.CreateRandom(5150, "Rookie").ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow) with
                { Difficulty = CharacterDifficulty.Classic };
            var classic = new GameSimulation(seed: 5150, width: 80, height: 48, colonist: classicProfile);
            var carried = classic.Player.Inventory.Slots.Where(stack => !stack.IsEmpty)
                .GroupBy(stack => stack.ItemId).ToDictionary(group => group.Key, group => group.Sum(stack => stack.Count));
            var expected = new Dictionary<ItemId, int>
            {
                [ItemId.FerriteStiletto] = 1, [ItemId.CopperPickaxe] = 1, [ItemId.FerriteHatchet] = 1, [ItemId.Berry] = 12
            };
            var exactKit = carried.Count == expected.Count && expected.All(pair => carried.GetValueOrDefault(pair.Key) == pair.Value);
            var stilettoSelected = classic.Player.Inventory.SelectedStack.ItemId == ItemId.FerriteStiletto;

            var creativeProfile = CharacterDraft.CreateRandom(5151, "Maker").ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow) with
                { Difficulty = CharacterDifficulty.Creative };
            var creative = new GameSimulation(seed: 5151, width: 80, height: 48, colonist: creativeProfile);
            var inventory = creative.Player.Inventory;
            var cobaltKit = creative.CreativeKitGranted &&
                            new[] { ItemId.CobaltStiletto, ItemId.CobaltDrill, ItemId.CobaltHatchet, ItemId.CobaltDemolisher }
                                .All(item => inventory.Count(item) == 1) &&
                            inventory.Count(ItemId.FerriteStiletto) == 0 && inventory.Count(ItemId.CopperPickaxe) == 0 &&
                            inventory.SelectedStack.ItemId == ItemId.CobaltStiletto;
            var wingsWorn = creative.ActiveAccessorySlots.Any(slot => creative.Player.Equipment[slot] == ItemId.GliderWings) &&
                            inventory.Count(ItemId.GliderWings) == 0;

            check(exactKit && stilettoSelected && cobaltKit && wingsWorn,
                $"a Classic character starts with exactly the ferrite stiletto, drill and hatchet plus food, stiletto selected; " +
                $"a Creative character gets the cobalt kit with glider wings worn " +
                $"(exact={exactKit}, selected={stilettoSelected}, cobalt={cobaltKit}, wings={wingsWorn}, carried={string.Join(",", carried)})");

            // A character saved with the old kit (no hatchet) gets the current kit's tools once; nothing it carried is lost.
            var veteran = new GameSimulation(seed: 5152, width: 80, height: 48, colonist: classicProfile);
            var oldKit = new ItemStack[veteran.Player.Inventory.SlotCount];
            oldKit[0] = new ItemStack(ItemId.WoodenPickaxe, 1);
            oldKit[1] = new ItemStack(ItemId.WoodenSword, 1);
            oldKit[2] = new ItemStack(ItemId.CopperBow, 1);
            var saved = new CharacterFileData(CharacterFileData.CurrentFormatVersion, classicProfile.Id, classicProfile.Name,
                DateTimeOffset.UtcNow, oldKit, 0);
            veteran.ApplyCharacter(saved);
            var toppedUp = new[] { ItemId.FerriteStiletto, ItemId.CopperPickaxe, ItemId.FerriteHatchet }.All(item => veteran.Player.Inventory.Count(item) == 1) &&
                           veteran.Player.Inventory.Count(ItemId.WoodenSword) == 1 && veteran.Player.Inventory.Count(ItemId.CopperBow) == 1;
            veteran.ApplyCharacter(veteran.CaptureCharacter());
            var once = veteran.Player.Inventory.Count(ItemId.FerriteHatchet) == 1;
            check(toppedUp && once, $"a character saved with the old kit gets the starter tools once on load (topped up {toppedUp}, once {once})");
        }
        finally
        {
            CharacterFileStore.Active = previous;
        }
    }
}
