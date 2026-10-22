using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Self-tests for Batch B: player systems, items, creature AI families, saves and character files.</summary>
public static class BatchBSelfTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private static GameSimulation RoundTrip(GameSimulation game) =>
        GameSimulation.Restore(JsonSerializer.Deserialize<GameSnapshot>(JsonSerializer.Serialize(game.CaptureSnapshot(), Json), Json)!,
            game.PlayerProfile);

    private static GameSimulation NewGame(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 80, height: 48) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static void Tick(GameSimulation game, int ticks = 1, MovementInput movement = default, PlayerAction action = PlayerAction.None,
        GridPoint? target = null)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(movement, i == 0 ? action : PlayerAction.None, target));
    }

    /// <summary>Clears a sky box (air), lays a rock floor at <paramref name="floorY"/>, and puts the player on it.</summary>
    private static void Arena(GameSimulation game, int left, int top, int right, int floorY)
    {
        for (var y = top; y <= floorY + 1; y++)
        for (var x = left; x <= right; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y >= floorY ? TileId.Rock : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            game.World.SetCell(cell, data);
        }
        Place(game, new Vector2((left + right) / 2f, floorY - game.Player.Motion.Size.Y - 0.01f));
    }

    private static void Place(GameSimulation game, Vector2 position)
    {
        game.Player.Motion.Position = position;
        game.Player.Motion.Velocity = Vector2.Zero;
        Tick(game, 2);
    }

    private static void Fill(GameSimulation game, int left, int top, int right, int bottom, LiquidKind kind)
    {
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            var cell = new GridPoint(x, y);
            var data = game.World.GetCell(cell);
            data.LiquidKind = kind;
            data.LiquidDepth = 1f;
            game.World.SetCell(cell, data);
        }
    }

    private static void Equip(GameSimulation game, EquipmentSlot slot, ItemId item) => game.Player.Equipment.Set(slot, item);

    private static void SetCharge(GameSimulation game, float value) =>
        typeof(GameSimulation).GetField("_charge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(game, value);

    public static void Run(Action<bool, string> check)
    {
        // ---- Ground items ----
        var drops = NewGame(9101);
        Arena(drops, 10, 4, 60, 20);
        var far = drops.SpawnGroundItem(ItemId.CopperOre, 3, drops.Player.Motion.Position + new Vector2(14f, -3f))!;
        var startY = far.Position.Y;
        drops.SpawnGroundItem(ItemId.CopperOre, 2, far.Position + new Vector2(0.3f, 0f));
        Tick(drops, 90);
        check(drops.GroundItems.Count == 1 && drops.GroundItems[0].Count == 5 && drops.GroundItems[0].Grounded &&
              drops.GroundItems[0].Position.Y > startY && drops.Player.Inventory.Count(ItemId.CopperOre) == 0,
            "ground items pop, fall onto the floor and merge with matching items; far items stay on the ground");
        var oreBefore = drops.Player.Inventory.Count(ItemId.CopperOre);
        drops.SpawnGroundItem(ItemId.CopperOre, 4, drops.Player.Motion.Position + new Vector2(1.6f, 0.5f));
        Tick(drops, 40);
        var grabbed = drops.Player.Inventory.Count(ItemId.CopperOre) - oreBefore == 4;
        drops.Player.Inventory.Add(ItemId.MagnetRing, 1);
        Equip(drops, EquipmentSlot.Accessory, ItemId.MagnetRing);
        far = drops.GroundItems[0];
        var farDistance = Vector2.Distance(far.Position, drops.Player.Motion.Position);
        drops.Player.Motion.Position = far.Position + new Vector2(-7f, -1.9f);
        Tick(drops, 90);
        check(grabbed && drops.ItemPickupRadius == GameSimulation.ItemMagnetRadius && drops.GroundItems.Count == 0 &&
              drops.Player.Inventory.Count(ItemId.CopperOre) == 9 && farDistance > GameSimulation.ItemGrabRadius,
            "items within ~2 tiles fly to the player; a magnet accessory pulls from ~10 tiles");
        var stale = drops.SpawnGroundItem(ItemId.Berry, 1, drops.Player.Motion.Position + new Vector2(20f, -2f))!;
        stale.AgeSeconds = GameSimulation.GroundItemLifetimeSeconds - 0.01f;
        Tick(drops, 2);
        var nearKill = drops.Player.Inventory.Count(ItemId.BioGel);
        drops.CollectOrDrop(ItemId.BioGel, 2, drops.Player.Motion.Position);
        check(drops.GroundItems.All(item => item.ItemId != ItemId.Berry) && drops.Player.Inventory.Count(ItemId.BioGel) == nearKill + 2,
            "ground items despawn after ten minutes; drops at the player's position are collected at once");
        drops.Player.Inventory.Add(ItemId.SoilBlock, 50);
        var thrown = drops.DropInventorySlot(drops.Player.Inventory.FindSlot(ItemId.SoilBlock), 5);
        Tick(drops, 20);
        check(thrown is { Count: 5 } && drops.GroundItems.Contains(thrown),
            "thrown items wait out a pickup delay before the magnet can reclaim them");

        // ---- Immunity frames ----
        var immune = NewGame(9102);
        var first = immune.Player.TakeHit(10f, DamageKind.Bite);
        var second = immune.Player.TakeHit(10f, DamageKind.Bite);
        Tick(immune, 42);
        var third = immune.Player.TakeHit(10f, DamageKind.Bite);
        var target = immune.SpawnCreature(EnemyKind.GelHopper, immune.Player.Motion.Position + new Vector2(12f, -2f));
        target.MarkHitBy(-1, 0.1f);
        var blocked = !target.CanBeHitBy(-1) && target.CanBeHitBy(42);
        Tick(immune, 8);
        check(first > 0f && second == 0f && third > 0f && blocked && target.CanBeHitBy(-1),
            "player hits grant ~0.67 s immunity; creatures keep a per-attacker hit cooldown");

        // ---- Breath, drowning and the rebreather ----
        var diver = NewGame(9103);
        Arena(diver, 10, 4, 40, 20);
        for (var y = 10; y <= 19; y++)
        {
            diver.World.SetTile(new GridPoint(22, y), TileId.Rock);
            diver.World.SetTile(new GridPoint(28, y), TileId.Rock);
        }
        Fill(diver, 23, 10, 27, 19, LiquidKind.Water);
        Place(diver, new Vector2(24.6f, 17.5f));
        var fullBreath = diver.Breath;
        Tick(diver, 120);
        var breathDropped = diver.HeadSubmerged && diver.Breath < fullBreath - 1.5f && diver.IsSwimming;
        var hpBefore = diver.Player.HitPoints;
        Tick(diver, 60 * 12);
        var drowned = diver.Player.HitPoints < hpBefore;
        diver.Player.Inventory.Add(ItemId.RebreatherMask, 1);
        Equip(diver, EquipmentSlot.Accessory, ItemId.RebreatherMask);
        check(breathDropped && drowned && diver.MaximumBreath == GameSimulation.BaseBreathSeconds * GameSimulation.RebreatherBreathMultiplier,
            "a submerged head drains breath, empty breath drowns, and the rebreather triples capacity");

        // ---- Swimming ----
        var swimmer = NewGame(9104);
        Arena(swimmer, 10, 4, 40, 20);
        for (var y = 6; y <= 19; y++)
        {
            swimmer.World.SetTile(new GridPoint(22, y), TileId.Rock);
            swimmer.World.SetTile(new GridPoint(28, y), TileId.Rock);
        }
        Fill(swimmer, 23, 8, 27, 19, LiquidKind.Water);
        swimmer.Player.Motion.Position = new Vector2(24.6f, 9f);
        swimmer.Player.Motion.Velocity = Vector2.Zero;
        Tick(swimmer, 20);
        var sinkSpeed = swimmer.Player.Motion.Velocity.Y;
        Tick(swimmer, 1, new MovementInput(0f, JumpPressed: true, JumpHeld: true));
        var stroke = swimmer.Player.Motion.Velocity.Y;
        check(sinkSpeed is > 0f and <= 2.25f && stroke < -3f && swimmer.SwimmingIn == LiquidKind.Water,
            "liquids slow sinking and jump strokes swim upward");

        // ---- Fall damage and featherfall ----
        var faller = NewGame(9105);
        Arena(faller, 10, 1, 40, 44);
        Place(faller, new Vector2(25f, 4f));
        var fallHp = faller.Player.HitPoints;
        Tick(faller, 240);
        var tookFall = faller.Player.HitPoints < fallHp - 10f;
        var glider = NewGame(9105);
        Arena(glider, 10, 1, 40, 44);
        glider.Player.Inventory.Add(ItemId.GliderWings, 1);
        Equip(glider, EquipmentSlot.Accessory, ItemId.GliderWings);
        Place(glider, new Vector2(25f, 4f));
        var glideHp = glider.Player.HitPoints;
        Tick(glider, 400);
        check(tookFall && glider.Player.HitPoints >= glideHp - 0.01f && glider.Player.Motion.Grounded,
            "falls beyond 25 tiles hurt; glider wings (featherfall) negate them");

        // ---- Mobility accessories ----
        var mover = NewGame(9106);
        Arena(mover, 5, 1, 70, 44);
        mover.Player.Inventory.Add(ItemId.DoubleJumpCanister, 1);
        Equip(mover, EquipmentSlot.Accessory, ItemId.DoubleJumpCanister);
        Place(mover, new Vector2(30f, 10f));
        Tick(mover, 12);
        var hadDouble = mover.DoubleJumpAvailable;
        Tick(mover, 1, new MovementInput(0f, JumpPressed: true, JumpHeld: true));
        var doubleJumped = mover.Player.Motion.Velocity.Y < -7f && !mover.DoubleJumpAvailable;
        Equip(mover, EquipmentSlot.Accessory2, ItemId.RocketPack);
        var standing = 44f - mover.Player.Motion.Size.Y - 0.01f; // feet on the arena floor (row 44)
        Place(mover, new Vector2(30f, standing));
        Tick(mover, 3);
        var fuelFull = mover.RocketFuel;
        Tick(mover, 1, new MovementInput(0f, JumpPressed: true, JumpHeld: true));
        Tick(mover, 50, new MovementInput(0f, JumpHeld: true));
        var rocketed = mover.RocketFuel < fuelFull && mover.Player.Motion.Position.Y < standing - 4.19f;
        check(hadDouble && doubleJumped && fuelFull >= 3f && rocketed,
            "the double-jump canister gives a mid-air jump and the rocket pack lifts while jump is held");

        var gliderWings = NewGame(9107);
        Arena(gliderWings, 5, 1, 70, 44);
        Equip(gliderWings, EquipmentSlot.Accessory, ItemId.GliderWings);
        Place(gliderWings, new Vector2(30f, 4f));
        gliderWings.Player.Motion.WingEnergy = 0f;
        Tick(gliderWings, 30, new MovementInput(0f, JumpHeld: true));
        var glideSpeed = gliderWings.Player.Motion.Velocity.Y;
        check(gliderWings.IsGliding && glideSpeed <= 2.3f,
            "glider wings spend wing energy, then glide at a slow fall");

        var climber = NewGame(9108);
        Arena(climber, 5, 1, 70, 44);
        for (var y = 2; y < 44; y++)
            climber.World.SetTile(new GridPoint(40, y), TileId.Rock);
        Equip(climber, EquipmentSlot.Accessory, ItemId.ClimbingClaws);
        climber.Player.Motion.Position = new Vector2(40f - climber.Player.Motion.Size.X - 0.01f, 8f); // flush against the wall
        climber.Player.Motion.Velocity = Vector2.Zero;
        Tick(climber, 40, new MovementInput(1f));
        var slide = climber.Player.Motion.Velocity.Y;
        var clinging = climber.IsWallClinging;
        Tick(climber, 1, new MovementInput(1f, JumpPressed: true));
        check(clinging && slide <= 1.61f && climber.Player.Motion.Velocity.X < 0f && climber.Player.Motion.Velocity.Y < -5f,
            "climbing claws slow a wall slide and allow wall jumps");

        var hooker = NewGame(9109);
        Arena(hooker, 5, 1, 70, 44);
        for (var x = 5; x <= 70; x++)
            hooker.World.SetTile(new GridPoint(x, 30), TileId.Rock);
        Place(hooker, new Vector2(30f, 44f - hooker.Player.Motion.Size.Y - 0.01f));
        var noHook = !hooker.TryFireGrapple(hooker.Player.Motion.Position + new Vector2(0f, -10f));
        Equip(hooker, EquipmentSlot.Accessory, ItemId.GrappleHook);
        hooker.TryFireGrapple(hooker.Player.Motion.Position + new Vector2(0.4f, -10f));
        Tick(hooker, 30);
        var latched = hooker.Grapple is { Latched: true };
        var hookY = hooker.Player.Motion.Position.Y;
        Tick(hooker, 60);
        check(noHook && latched && hooker.Player.Motion.Position.Y < hookY - 3f,
            "the grappling hook flies out, latches into solid tiles and reels the player in");

        // ---- Quick keys, potion sickness and charge crystals ----
        var quick = NewGame(9110);
        quick.Player.TakeDamage(60f);
        quick.Player.Inventory.Add(ItemId.MedStim, 2);
        var healHp = quick.Player.HitPoints;
        var healed = quick.QuickHeal() && quick.Player.HitPoints > healHp && quick.PotionSick;
        var blockedHeal = !quick.QuickHeal() && quick.Player.Inventory.Count(ItemId.MedStim) == 1;
        var sickness = quick.ActiveBuffs.FirstOrDefault(buff => buff.Status == StatusId.PotionSickness);
        SetCharge(quick, 0f);
        quick.Player.Inventory.Add(ItemId.ChargeCellSmall, 1);
        var charged = quick.QuickCharge() && quick.Charge > 0f;
        quick.Player.Inventory.Add(ItemId.SpeedStim, 1);
        quick.Player.Inventory.Add(ItemId.ArmorStim, 1);
        var buffs = quick.QuickBuff();
        check(healed && blockedHeal && sickness is { IsDebuff: true, RemainingSeconds: > 30f and <= 46f, IconName: "buff_potion_sickness" } &&
              charged && buffs == 2 && quick.Player.Statuses.IsActive(StatusId.Haste) && quick.Player.Statuses.IsActive(StatusId.Armored),
            "quick heal, quick charge and quick buff use the right items; potion sickness blocks a second heal");
        var maxCharge = quick.MaximumCharge;
        quick.Player.Inventory.Add(ItemId.ChargeCrystal, 2);
        quick.TrySelectInventorySlot(quick.Player.Inventory.FindSlot(ItemId.ChargeCrystal));
        quick.Player.ActionCooldownTicks = 0;
        Tick(quick, 1, action: PlayerAction.UseSelected, target: new GridPoint(1, 1));
        var crystalLoaded = RoundTrip(quick);
        check(quick.Player.ChargeCrystalsUsed == 1 && quick.MaximumCharge == maxCharge + PlayerItems.ChargeCrystalBonus &&
              crystalLoaded.Player.ChargeCrystalsUsed == 1,
            "charge crystals raise maximum Charge and persist");

        // ---- Tombstones and the boss respawn rule ----
        var mortal = NewGame(9111);
        Arena(mortal, 10, 4, 60, 20);
        mortal.Player.TakeDamage(1e6f);
        Tick(mortal, 2);
        var grave = mortal.Signs.FirstOrDefault(pair => mortal.World.GetCell(pair.Key).TileId == TileId.Tombstone);
        check(mortal.IsOver && grave.Value?.Contains(mortal.Player.Name) == true && mortal.RespawnSecondsRemaining is > 4.9f and <= 5f,
            "death leaves a named tombstone tile and starts a respawn timer");
        var bossDeath = NewGame(9112);
        bossDeath.TrySummonNextBoss();
        bossDeath.Player.TakeDamage(1e6f);
        Tick(bossDeath, 2);
        bossDeath.RespawnPlayer(CharacterDifficulty.Classic);
        Tick(bossDeath, 2);
        check(bossDeath.BossBlocksRespawn && bossDeath.GetRespawnDelay(CharacterDifficulty.Classic) == GameSimulation.BossRespawnSeconds &&
              !bossDeath.IsOver && bossDeath.RespawnSecondsRemaining == 0f,
            "a living boss lengthens the respawn timer");

        // ---- Loadouts ----
        var dresser = NewGame(9113);
        Equip(dresser, EquipmentSlot.Head, ItemId.Trailcap);
        var switched = dresser.SwitchLoadout(1) && dresser.Player.Equipment[EquipmentSlot.Head] is null;
        Equip(dresser, EquipmentSlot.Head, ItemId.RadFilterMask);
        var back = dresser.SwitchLoadout(0) && dresser.Player.Equipment[EquipmentSlot.Head] == ItemId.Trailcap &&
                   dresser.Loadouts[1].Head == ItemId.RadFilterMask && dresser.Loadouts.Count == 3;
        check(switched && back && !dresser.SwitchLoadout(3), "three equipment loadouts swap worn gear");

        // ---- Bank, trash, favorites, sort, quick stack, loot all and deposit all ----
        var keeper = NewGame(9114);
        var noBank = keeper.OpenBank() is null;
        keeper.Player.Inventory.Add(ItemId.CargoPod, 1);
        var bank = keeper.OpenBank();
        keeper.Player.Inventory.PlaceIntoSlot(15, new ItemStack(ItemId.CopperOre, 7));
        keeper.Player.Inventory.PlaceIntoSlot(16, new ItemStack(ItemId.Berry, 3));
        keeper.ToggleFavorite(16);
        var deposited = keeper.DepositAll();
        var looted = keeper.LootAll();
        check(noBank && bank is not null && keeper.IsBankOpen && deposited == 7 && looted == 7 &&
              keeper.Player.Inventory.Slots[16] is { ItemId: ItemId.Berry, Favorite: true },
            "the Cargo pod opens a 40-slot bank anywhere; deposit/loot all skip favorites");
        var trashSlot = keeper.Player.Inventory.FindSlot(ItemId.CopperOre);
        var trashed = keeper.TrashInventorySlot(trashSlot) && keeper.TrashSlot.Count == 7 && !keeper.TrashInventorySlot(16);
        var recovered = keeper.RecoverTrash() == 7 && keeper.TrashSlot.IsEmpty;
        check(trashed && recovered, "the trash slot takes a stack (not favorites) and gives it back");
        keeper.Player.Inventory.PlaceIntoSlot(20, new ItemStack(ItemId.RockBlock, 5));
        keeper.Player.Inventory.PlaceIntoSlot(12, new ItemStack(ItemId.RockBlock, 6));
        keeper.Player.Inventory.PlaceIntoSlot(22, new ItemStack(ItemId.MedStim, 1));
        keeper.SortInventory();
        var sortedSlots = keeper.Player.Inventory.Slots.Skip(Inventory.HotbarSlots).ToArray();
        check(keeper.Player.Inventory.Slots[16].ItemId == ItemId.Berry &&
              sortedSlots.Count(stack => !stack.IsEmpty && stack.ItemId == ItemId.RockBlock) == 1 &&
              sortedSlots.First(stack => !stack.IsEmpty && stack.ItemId == ItemId.RockBlock).Count == 11 &&
              Array.FindIndex(sortedSlots, stack => stack.ItemId == ItemId.MedStim && !stack.IsEmpty) <
              Array.FindIndex(sortedSlots, stack => stack.ItemId == ItemId.RockBlock && !stack.IsEmpty),
            "sort merges stacks, orders by category and leaves favorites in place");
        var chestTile = ContentTiles.Definitions.First(definition => (definition.Traits & TileTraits.Container) != 0 &&
                                                                     (definition.Traits & TileTraits.Locked) == 0 &&
                                                                     definition.Id is not (TileId.CoastalHabPod or TileId.FungalHabRuin)).Id;
        var chestCell = new GridPoint((int)keeper.Player.Motion.Position.X + 3, (int)keeper.Player.Motion.Position.Y);
        keeper.World.SetTile(chestCell, chestTile);
        keeper.GetContainer(chestCell)!.Add(ItemId.RockBlock, 1);
        keeper.CloseBank();
        keeper.Player.Inventory.TryConsume(ItemId.CargoPod);
        var stacked = keeper.QuickStackToNearbyChests();
        check(stacked == 11 && keeper.GetContainer(chestCell)!.Count(ItemId.RockBlock) == 12 &&
              keeper.Player.Inventory.Count(ItemId.MedStim) == 1 && keeper.LootAll(chestCell) == 12,
            "quick stack moves matching stacks into nearby containers; loot all empties one");
        // Terraria's chest links beside the open container: quick stack (into this one), restock, sort.
        var chest = keeper.GetContainer(chestCell)!;
        keeper.Player.Inventory.PlaceIntoSlot(30, new ItemStack(ItemId.RockBlock, 4));
        chest.Add(ItemId.RockBlock, 1);
        chest.Add(ItemId.MedStim, 3);
        var stackedHere = keeper.QuickStackInto(chestCell);
        var restocked = keeper.Restock(chestCell);
        chest.Add(ItemId.Berry, 2);
        var sortedChest = keeper.SortContainer(chestCell) && chest.Slots[0].IsEmpty is false;
        // Quick stack sends the 4 loose rocks and the leftover med stim (the chest holds both); restock brings the rocks
        // back (the hotbar still carries rock) but not the med stim (no longer carried).
        check(stackedHere == 5 && restocked == 5 && chest.Count(ItemId.RockBlock) == 0 && chest.Count(ItemId.MedStim) == 4 &&
              keeper.Player.Inventory.Count(ItemId.MedStim) == 0 && sortedChest,
            "chest links: quick stack fills the open chest with what it holds, restock tops up carried items, sort tidies it");
        keeper.PutInTrash(new ItemStack(ItemId.Berry, 3));
        var dropped = keeper.TrashSlot is { ItemId: ItemId.Berry, Count: 3 };
        var taken = keeper.TakeTrash();
        check(dropped && taken.Count == 3 && keeper.TrashSlot.IsEmpty,
            "a held stack dropped on the trash slot replaces its contents, and an empty-handed click takes it back");

        // The inventory hub: Terraria's 10 × 5 grid with the hotbar on top, trash, sort / quick stack, chest links, tabs, settings.
        var hub = new Rimtera.UI.InventoryUiState(keeper.Player.Inventory, chest: chest, game: keeper, chestCell: chestCell);
        var ids = hub.DescendantsAndSelf().Select(element => element.Id).ToHashSet();
        var playerSlots = hub.Slots.Where(slot => ReferenceEquals(slot.Inventory, keeper.Player.Inventory)).ToArray();
        check(keeper.Player.Inventory.SlotCount == GameSimulation.PlayerInventorySlots && playerSlots.Length == 50 &&
              playerSlots[0].Badge == "1" && playerSlots[9].Badge == "0" && playerSlots[10].Badge is null &&
              new[] { "inventory-sort", "inventory-quick-stack", "inventory-trash", "inventory-wires", "chest-loot-all", "chest-deposit-all",
                      "chest-quick-stack", "chest-restock", "chest-sort", "crafting-guide", "tab-housing", "tab-crew", "tab-bestiary",
                      "tab-feats", "loadout-1", "inventory-settings" }.All(ids.Contains),
            "the inventory hub lays out Terraria's 10 × 5 grid (hotbar numbered 1-0) with trash, sort, quick stack, chest links, crafting help, tabs, loadouts and Settings");
        keeper.Player.Inventory.PlaceIntoSlot(31, new ItemStack(ItemId.Berry, 2));
        hub.ClickPlayerSlot(31, alt: true);
        var favorited = keeper.Player.Inventory.IsFavorite(31);
        hub.ClickPlayerSlot(31, alt: true);
        hub.ClickPlayerSlot(31, ctrl: true);
        var ctrlTrashed = keeper.Player.Inventory.Slots[31].IsEmpty && keeper.TrashSlot.ItemId == ItemId.Berry;
        keeper.Player.Inventory.PlaceIntoSlot(32, new ItemStack(ItemId.RockBlock, 3));
        hub.ClickPlayerSlot(32, shift: true);
        var shiftMoved = keeper.Player.Inventory.Slots[32].IsEmpty && chest.Count(ItemId.RockBlock) == 3;
        check(favorited && ctrlTrashed && shiftMoved,
            "inventory clicks follow Terraria: Alt favorites, Ctrl trashes, Shift moves to the open chest");

        // ---- Signs ----
        var writer = NewGame(9115);
        var signCell = new GridPoint((int)writer.Player.Motion.Position.X + 2, (int)writer.Player.Motion.Position.Y);
        writer.World.SetTile(signCell, TileId.SignPost);
        var wrote = writer.SetSignText(signCell, "Landing site: water is east.");
        var read = writer.Interact(signCell);
        var signLoaded = RoundTrip(writer);
        check(wrote && writer.GetSignText(signCell) == "Landing site: water is east." && read.Kind == WorldInteractionKind.SignRead &&
              read.Message.Contains("water is east") && signLoaded.GetSignText(signCell) == "Landing site: water is east." &&
              !writer.SetSignText(new GridPoint(1, 1), "x"),
            "signs hold editable text that interaction reads and saves keep");

        // ---- Pets, light pets and the hover-bike ----
        var owner = NewGame(9116);
        Arena(owner, 5, 1, 70, 20);
        owner.Player.Inventory.Add(ItemId.PetCrabShell, 1);
        owner.Player.Inventory.Add(ItemId.LightPetWisp, 1);
        owner.TogglePet(ItemId.PetCrabShell);
        owner.TogglePet(ItemId.LightPetWisp);
        Tick(owner, 60);
        check(owner.ActivePet is { Name: "Pebble crab" } pet && Vector2.Distance(pet.Position, owner.Player.Motion.Position) < 4f &&
              owner.ActiveLightPet is { IsLightPet: true } && owner.PlayerLightSources.Any(light => light.Radius >= 7f),
            "pets follow the player and light pets emit light");
        var walker = NewGame(9116);
        Arena(walker, 5, 1, 70, 20);
        Tick(walker, 40, new MovementInput(1f));
        var walkSpeed = walker.Player.Motion.Velocity.X;
        owner.Player.Inventory.Add(ItemId.HoverBikeKey, 1);
        var mounted = owner.ToggleMount() && owner.IsMounted;
        Place(owner, new Vector2(30f, 20f - owner.Player.Motion.Size.Y - 0.01f));
        Tick(owner, 40, new MovementInput(1f));
        check(mounted && owner.Player.Motion.Velocity.X > walkSpeed * 1.4f && owner.ActiveBuffs.Any(buff => buff.Key == "mount"),
            "the hover-bike runs faster and shows in the buff bar");

        // ---- Recall and phase rod ----
        var traveler = NewGame(9117);
        var landing = traveler.Player.Motion.Position;
        traveler.Player.Motion.Position += new Vector2(15f, -6f);
        traveler.Player.Inventory.Add(ItemId.RecallCapsule, 1);
        traveler.TrySelectInventorySlot(traveler.Player.Inventory.FindSlot(ItemId.RecallCapsule));
        Tick(traveler, 1, action: PlayerAction.UseSelected, target: new GridPoint(1, 1));
        var recalled = Vector2.Distance(traveler.Player.Motion.Position, landing) < 2.5f && traveler.Player.Inventory.Count(ItemId.RecallCapsule) == 0;
        Arena(traveler, 5, 1, 70, 20);
        var phased = traveler.TryPhaseTo(new GridPoint(50, 19)) &&
                     MathF.Abs(traveler.Player.Motion.Position.X + traveler.Player.Motion.Size.X / 2f - 50.5f) < 0.01f &&
                     traveler.Player.Statuses.IsActive(StatusId.PhaseStrain) && !traveler.TryPhaseTo(new GridPoint(50, 21));
        check(recalled && phased, "the recall capsule returns to the landing site and the phase rod teleports to open space");

        // ---- Kill banners, bestiary and achievements ----
        var hunter = NewGame(9118);
        Arena(hunter, 5, 1, 70, 20);
        for (var i = 0; i < GameSimulation.KillsPerBanner; i++)
        {
            var hopper = hunter.SpawnCreature(EnemyKind.GelHopper, hunter.Player.Motion.Position + new Vector2(2f, -1f));
            hopper.TakeDamage(1e6f);
            Tick(hunter);
        }
        var banner = PlayerItems.BannerFor(EnemyKind.GelHopper);
        var earned = hunter.Player.Inventory.Count(banner) == 1 && hunter.KillCounts[EnemyKind.GelHopper] == 50;
        hunter.TrySelectInventorySlot(hunter.Player.Inventory.FindSlot(banner));
        var bannerCell = new GridPoint((int)hunter.Player.Motion.Position.X + 2, 19);
        hunter.Player.ActionCooldownTicks = 0;
        Tick(hunter, 31, action: PlayerAction.UseSelected, target: bannerCell);
        var entry = hunter.Bestiary.Get(EnemyKind.GelHopper);
        check(earned && hunter.BannerTiles.Count == 1 && hunter.ActiveBannerKinds.Contains(EnemyKind.GelHopper) &&
              PlayerItems.BannerKindOf(banner) == EnemyKind.GelHopper && hunter.Items[banner].Name == "Gel hopper banner",
            "every 50 kills of a kind drops its banner; a hung banner is active nearby");
        check(entry is { Seen: true, Kills: 50, UnlockLevel: 3 } && entry.DropsSeen.Contains(ItemId.BioGel) &&
              hunter.Achievements.IsUnlocked("first-blood") && hunter.Achievements.IsUnlocked("trophy-hunter") &&
              AchievementCatalog.All.Count >= 30 && hunter.Achievements.RecentUnlocks.Count > 0,
            "the bestiary logs sightings, kills and drops; achievements unlock on their hooks");
        hunter.Player.Inventory.Add(PlayerItems.BannerFor(EnemyKind.SnowWolf), 2);
        var hunterLoaded = RoundTrip(hunter);
        check(hunterLoaded.Player.Inventory.Count(PlayerItems.BannerFor(EnemyKind.SnowWolf)) == 2 &&
              hunterLoaded.KillCounts[EnemyKind.GelHopper] == 50 && hunterLoaded.BannerTiles.Count == 1 &&
              hunterLoaded.Bestiary.Get(EnemyKind.GelHopper)?.Kills == 50 && hunterLoaded.Achievements.IsUnlocked("trophy-hunter"),
            "kill counts, banners, the bestiary and achievements survive a save round-trip");

        // ---- Live creatures and projectiles ----
        var live = NewGame(9130);
        Arena(live, 10, 4, 60, 20);
        var creature = live.SpawnCreature(EnemyKind.GelHopper, live.Player.Motion.Position + new Vector2(7f, 0f));
        creature.TakeDamage(creature.MaximumHitPoints * 0.4f);
        creature.MarkHitBy(-1, 1.5f);
        creature.Afflict(StatusId.Poison, 2.5f);
        Tick(live, 2);
        var bolt = live.ProbeFireAfflictedBolt(live.Player.Motion.Position + new Vector2(0.6f, 0f), new Vector2(18f, 0f), 4f, StatusId.Burning);
        bolt.Homing = 0.25f;
        bolt.PierceRemaining = 2;
        bolt.Tethered = true;
        bolt.HitEnemies = new HashSet<long> { creature.Id };
        var savedCreature = creature.HitPoints;
        var savedBoltPosition = bolt.Position;
        var liveLoaded = RoundTrip(live);
        var loadedCreature = liveLoaded.Enemies.Single();
        var loadedBolt = liveLoaded.Projectiles.Single();
        check(loadedCreature.Kind == creature.Kind && Math.Abs(loadedCreature.HitPoints - savedCreature) < 0.01f &&
              loadedCreature.MaximumHitPoints == creature.MaximumHitPoints &&
              loadedCreature.AttackDamage == creature.AttackDamage &&
              loadedCreature.HasAffliction(StatusId.Poison) && !loadedCreature.CanBeHitBy(-1) &&
              loadedBolt.Position == savedBoltPosition && loadedBolt.PierceRemaining == 2 &&
              Math.Abs(loadedBolt.Homing - 0.25f) < 0.001f && loadedBolt.Tethered &&
              loadedBolt.HitEnemies is { Count: 1 } && loadedBolt.HitEnemies.Contains(loadedCreature.Id),
            "live creatures (health, afflictions, immunity) and projectiles (motion, pierce, tether, hit list) survive a save round-trip");

        // ---- AI families ----
        var zoo = NewGame(9119);
        Arena(zoo, 5, 1, 75, 20);
        var wyrmStart = new Vector2(zoo.Player.Motion.Position.X + 8f, 30f);
        var wyrm = zoo.SpawnCreatureAt(EnemyKind.TunnelWyrm, wyrmStart);
        Tick(zoo, 90);
        var spaced = wyrm.Segments.Count == 9 && wyrm.Segments.Zip(wyrm.Segments.Skip(1))
            .All(pair => Vector2.Distance(pair.First, pair.Second) <= GameSimulation.WormSegmentSpacing + 0.01f);
        check(spaced && Vector2.Distance(wyrm.Motion.Position, wyrmStart) > 2f && wyrm.Family == EnemyAiFamily.Worm,
            "worms burrow through tiles with a trailing chain of segments");
        wyrm.TakeDamage(1e6f);
        var caster = zoo.SpawnCreature(EnemyKind.RiftCaster, zoo.Player.Motion.Position + new Vector2(9f, -1f));
        var casterStart = caster.Motion.Position;
        var sawHoming = false;
        for (var i = 0; i < 360 && !sawHoming; i++)
        {
            Tick(zoo);
            sawHoming = zoo.Projectiles.Any(projectile => projectile.Homing > 0f && projectile.Team == ProjectileTeam.Enemy);
        }
        check(sawHoming && caster.Motion.Position != casterStart, "casters teleport near the player and fire slow homing bolts");
        caster.TakeDamage(1e6f);
        var kite = zoo.SpawnCreature(EnemyKind.DiveKite, zoo.Player.Motion.Position + new Vector2(6f, -7f));
        var dived = false;
        for (var i = 0; i < 400 && !dived; i++)
        {
            Tick(zoo);
            dived = kite.Lunging;
        }
        check(dived && kite.Family == EnemyAiFamily.DivingFlyer, "diving flyers hover and then dive at the player");
        kite.TakeDamage(1e6f);
        var mimic = zoo.SpawnCreature(EnemyKind.CacheMimic, zoo.Player.Motion.Position + new Vector2(12f, -1f));
        Tick(zoo, 3);
        var disguised = mimic.Disguised;
        mimic.Motion.Position = zoo.Player.Motion.Position + new Vector2(1.5f, 0.8f);
        Tick(zoo, 3);
        check(disguised && !mimic.Disguised, "mimics pose as containers until the player comes close");
        mimic.TakeDamage(1e6f);
        var flea = zoo.SpawnCreature(EnemyKind.FleaHopper, zoo.Player.Motion.Position + new Vector2(10f, -1f));
        var boulder = zoo.SpawnCreature(EnemyKind.BoulderHopper, zoo.Player.Motion.Position + new Vector2(-10f, -1.5f));
        float fleaRise = 0f, boulderRise = 0f;
        for (var i = 0; i < 200; i++)
        {
            Tick(zoo);
            fleaRise = Math.Min(fleaRise, flea.Motion.Velocity.Y);
            boulderRise = Math.Min(boulderRise, boulder.Motion.Velocity.Y);
        }
        check(fleaRise < -2f && boulderRise < fleaRise - 4f, "hopper variants hop with their own height and rhythm");

        // ---- Character files ----
        var root = Path.Combine(Path.GetTempPath(), "rimtera-batchb-" + Guid.NewGuid().ToString("N"));
        var store = new CharacterFileStore(Path.Combine(root, "characters"));
        var hero = CharacterDraft.CreateRandom(77, "Wayfarer").ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow).Normalized();
        var worldA = new GameSimulation(seed: 9120, width: 80, height: 48, colonist: hero) { FamilySpawnsEnabled = false };
        worldA.Player.Inventory.Add(ItemId.DiamondShard, 4);
        worldA.Player.LifeCoresUsed = 2;
        worldA.Player.ChargeCrystalsUsed = 3;
        worldA.Bank.Add(ItemId.AstralBar, 5);
        Equip(worldA, EquipmentSlot.Head, ItemId.Trailcap);
        worldA.SwitchLoadout(2);
        worldA.Achievements.TryUnlock("first-ore");
        var saved = worldA.SaveCharacterFile(store);
        var path = store.PathFor(hero.Id);
        var worldB = new GameSimulation(seed: 9121, width: 80, height: 48, colonist: hero) { FamilySpawnsEnabled = false };
        var cleanB = worldB.Player.Inventory.Count(ItemId.DiamondShard) == 0;
        var loadedB = worldB.LoadCharacterFile(store);
        check(saved && File.Exists(path) && path.EndsWith(".rchar", StringComparison.Ordinal) && cleanB && loadedB &&
              worldB.Player.Inventory.Count(ItemId.DiamondShard) == 4 && worldB.Player.LifeCoresUsed == 2 &&
              worldB.Player.ChargeCrystalsUsed == 3 && worldB.Bank.Count(ItemId.AstralBar) == 5 && worldB.ActiveLoadout == 2 &&
              worldB.Loadouts[0].Head == ItemId.Trailcap && worldB.Achievements.IsUnlocked("first-ore"),
            "character files carry inventory, gear, loadouts, upgrades, bank and achievements to another world");
        var previous = CharacterFileStore.Active;
        CharacterFileStore.Active = store;
        try
        {
            var worldC = new GameSimulation(seed: 9122, width: 80, height: 48, colonist: hero) { FamilySpawnsEnabled = false };
            var autoApplied = worldC.Player.Inventory.Count(ItemId.DiamondShard) == 4;
            worldC.Player.Inventory.Add(ItemId.RubyShard, 2);
            var snapshot = worldC.CaptureSnapshot();
            var reloadedC = GameSimulation.Restore(JsonSerializer.Deserialize<GameSnapshot>(JsonSerializer.Serialize(snapshot, Json), Json)!, hero);
            worldA.Player.Inventory.Add(ItemId.EmeraldShard, 1);
            worldA.CaptureSnapshot();
            var reloadedAfterOtherWorld = GameSimulation.Restore(snapshot, hero);
            check(autoApplied && store.TryLoad(hero.Id) is not null && reloadedC.Player.Inventory.Count(ItemId.RubyShard) == 2 &&
                  reloadedAfterOtherWorld.Player.Inventory.Count(ItemId.EmeraldShard) == 1 &&
                  reloadedAfterOtherWorld.Player.Inventory.Count(ItemId.RubyShard) == 0,
                "with an active store, new worlds apply the character file and loading a world uses the newest character file");
        }
        finally
        {
            CharacterFileStore.Active = previous;
        }
        var oldStyle = NewGame(9123);
        oldStyle.Player.Inventory.Add(ItemId.TopazShard, 3);
        var legacy = oldStyle.CaptureSnapshot() with { PlayerWorld = null, Character = null };
        var legacyLoaded = GameSimulation.Restore(JsonSerializer.Deserialize<GameSnapshot>(JsonSerializer.Serialize(legacy, Json), Json)!);
        check(legacyLoaded.Player.Inventory.Count(ItemId.TopazShard) == 3 && legacyLoaded.GroundItems.Count == 0,
            "older saves without character data fall back to the snapshot inventory");
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
