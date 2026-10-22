using System.Numerics;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Self-tests for the content fill (docs/TERRARIA_GAPS.md plan item 4): critter and hostile counts, sprites and spawn slots for
/// every new kind, gold variants, bait critters, drops and banners, a tree species per biome with its timber, world-generated
/// biome trees and wild plants, town gels and the travelling merchant.
/// </summary>
public static class ContentFillSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        // ---- 1. Counts, --entity-sprites style (distinct sprites per category) ----
        var critterSprites = new HashSet<string>();
        var hostileSprites = new HashSet<string>();
        foreach (var archetype in EnemyArchetypes.All.Where(archetype => !archetype.IsBoss))
            (archetype.Passive ? critterSprites : hostileSprites).Add(SpriteCatalog.ForEnemy(archetype.Kind));
        check(critterSprites.Count >= 95 && hostileSprites.Count >= 240,
            $"the creature roster reaches Terraria-like breadth: {critterSprites.Count} critters (>= 95) and {hostileSprites.Count} hostiles (>= 240)");

        // ---- 2. Every new kind: archetype, sprite name, spawn slot, behaviour, drops and banner ----
        var zoneKinds = Enum.GetValues<BiomeZone>().SelectMany(zone => BiomeSpawnTables.KindsFor(zone).Concat(BiomeSpawnTables.HardKindsFor(zone)))
            .Concat(new[] { "grass", "under", "cave" }.SelectMany(BiomeSpawnTables.FillKinds)).Concat(BiomeSpawnTables.DungeonKinds).ToHashSet();
        var fillKinds = ContentFillCreatures.AllKinds;
        var unslotted = fillKinds.Where(kind => !zoneKinds.Contains(kind)).ToArray();
        var badSprite = fillKinds.Where(kind => SpriteCatalog.ForEnemy(kind) != SpriteCatalog.SnakeCase(kind.ToString())).ToArray();
        var spriteDir = new[] { Path.Combine(Environment.CurrentDirectory, "Assets", "Sprites"), Path.Combine(AppContext.BaseDirectory, "Assets", "Sprites") }
            .FirstOrDefault(Directory.Exists);
        string[] prefixes = ["critter_", "creature_", ""];
        bool SpriteFile(EnemyKind kind) => spriteDir is not null &&
            prefixes.Any(prefix => File.Exists(Path.Combine(spriteDir, prefix + SpriteCatalog.ForEnemy(kind) + ".png")));
        // Only judge files when this checkout's art includes the content-fill sprites (stale asset copies skip this part).
        var missingArt = spriteDir is not null && fillKinds.Any(SpriteFile) ? fillKinds.Where(kind => !SpriteFile(kind)).ToArray() : [];
        var hostileFill = ContentFillData.Hostiles.Select(hostile => hostile.Kind).ToArray();
        var catalog = new ItemCatalog();
        var undropped = hostileFill.Where(kind => NpcDropRegistry.GetDrops(kind).Count == 0).ToArray();
        var unbannered = hostileFill.Where(kind => !PlayerItems.EarnsBanner(kind) || !catalog.Contains(PlayerItems.BannerFor(kind)) ||
                                                   PlayerItems.BannerKindOf(PlayerItems.BannerFor(kind)) != kind).ToArray();
        var families = ContentFillData.Hostiles.Select(hostile => hostile.Family).Distinct().Count();
        var hardCount = ContentFillData.Hostiles.Count(hostile => hostile.Hard);
        var hardZones = BiomeSpawnTables.HardZones.Count(zone => BiomeSpawnTables.HardKindsFor(zone).Any(kind => fillKinds.Contains(kind)));
        check(unslotted.Length == 0 && badSprite.Length == 0 && missingArt.Length == 0 && undropped.Length == 0 && unbannered.Length == 0 &&
              fillKinds.All(EnemyArchetypes.Contains) && families >= 8 && hardCount >= 40 && hardZones >= 13 &&
              new BestiaryLog().TotalKinds == Enum.GetValues<EnemyKind>().Length,
            $"every content-fill creature has a sprite name, a spawn slot, biome drops and a banner; {families} AI families, " +
            $"{hardCount} hardmode versions across {hardZones} biomes (unslotted {string.Join(",", unslotted.Take(3))}; sprite {string.Join(",", badSprite.Take(3))}; " +
            $"art {string.Join(",", missingArt.Take(3))}; drops {string.Join(",", undropped.Take(3))}; banners {string.Join(",", unbannered.Take(3))})");

        // ---- 3. Gold variants: rare, catchable and valuable ----
        var goldOk = ContentFillCreatures.GoldVariants.All(pair =>
        {
            var gold = EnemyArchetypes.Get(pair.Gold);
            Enum.TryParse<ItemId>(pair.Gold.ToString(), out var goldItem);
            Enum.TryParse<ItemId>(pair.Base.ToString(), out var baseItem);
            var places = new[] { "grass", "under", "cave" }.Concat(Enum.GetValues<BiomeZone>().Select(zone => zone.ToString()));
            var rare = places.All(place =>
            {
                var (goldDay, goldNight) = BiomeSpawnTables.WeightIn(place, pair.Gold);
                var (baseDay, baseNight) = BiomeSpawnTables.WeightIn(place, pair.Base);
                return goldDay <= baseDay * 0.1f + 1e-6f && goldNight <= baseNight * 0.1f + 1e-6f;
            });
            return gold is { Passive: true, Catchable: true } && rare && catalog.Contains(goldItem) && catalog.Contains(baseItem) &&
                   catalog[goldItem].CreditValue >= 10 * catalog[baseItem].CreditValue && catalog[goldItem].Rarity >= ItemRarity.Rare;
        });
        int goldSeen = 0, baseSeen = 0;
        var goldKinds = ContentFillCreatures.GoldVariants.Select(pair => pair.Gold).ToHashSet();
        var baseKinds = ContentFillCreatures.GoldVariants.Select(pair => pair.Base).ToHashSet();
        for (var i = 0; i < 40000; i++)
        {
            var pick = BiomeSpawnTables.PickFill("grass", (i + 0.5) / 40000.0, night: false);
            if (goldKinds.Contains(pick)) goldSeen++;
            if (baseKinds.Contains(pick)) baseSeen++;
        }
        var goldShare = goldSeen / (float)Math.Max(1, goldSeen + baseSeen);
        check(goldOk && ContentFillCreatures.GoldVariants.Count() >= 8 && goldSeen > 0 && goldShare < 0.1f,
            $"gold critters are rare (share {goldShare:P1} of their bases' spawns), catchable and worth at least ten times their base");

        // ---- 4. Bait critters: every new critter is bait; caught with the net and used on the line ----
        var newCritters = ContentFillData.Critters.Select(critter => critter.Kind).ToArray();
        var allBait = newCritters.All(kind => Enum.TryParse<ItemId>(kind.ToString(), out var item) && catalog[item].BaitPower > 0 &&
                                               catalog[item].ReleasesCritter == kind);
        var angler = new GameSimulation(seed: 8801, width: 80, height: 48) { FamilySpawnsEnabled = false };
        foreach (var enemy in angler.Enemies.ToArray())
            enemy.TakeDamage(1e7f);
        var anglerCell = new GridPoint((int)MathF.Floor(angler.Player.Motion.Position.X) + 2, (int)MathF.Floor(angler.Player.Motion.Position.Y) + 1);
        var cricket = angler.SpawnCreature(EnemyKind.MeadowCricket, new Vector2(anglerCell.X, anglerCell.Y));
        cricket.Motion.Position = new Vector2(anglerCell.X + 0.5f - cricket.Motion.Size.X / 2f, anglerCell.Y + 0.5f - cricket.Motion.Size.Y / 2f);
        angler.Player.Inventory.Add(ItemId.BugNet, 1);
        angler.Player.ActionCooldownTicks = 0;
        angler.TrySelectInventorySlot(angler.Player.Inventory.FindSlot(ItemId.BugNet));
        angler.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, anglerCell));
        var caught = angler.Player.Inventory.Count(ItemId.MeadowCricket) == 1;
        angler.Player.Inventory.Add(ItemId.FishingRod, 1);
        var rod = angler.Items[ItemId.FishingRod];
        var bare = rod.FishingPower;
        if (!caught)
            angler.Player.Inventory.Add(ItemId.MeadowCricket, 1);
        var baited = angler.CurrentBait == ItemId.MeadowCricket && angler.GetFishingPower(rod) >= bare + angler.Items[ItemId.MeadowCricket].BaitPower;
        angler.Player.Inventory.Add(ItemId.GildedCricket, 1);
        var goldBest = angler.CurrentBait == ItemId.GildedCricket;
        check(allBait && caught && baited && goldBest,
            $"bait critters: every new critter item is bait and releases its critter; the bug net catches one (caught {caught}), " +
            $"it adds its bait power on the line (baited {baited}), and a gilded critter is the best bait ({goldBest})");

        // ---- 5. Swimmers stay in water rosters ----
        var swimmers = ContentFillData.Critters.Where(critter => critter.Swims).Select(critter => critter.Kind)
            .Concat(ContentFillData.Hostiles.Where(hostile => hostile.Swims).Select(hostile => hostile.Kind)).ToArray();
        check(swimmers.Length >= 10 && swimmers.All(BiomeSpawnTables.IsSwimmer) && swimmers.All(kind => EnemyArchetypes.Get(kind).Flying) &&
              !BiomeSpawnTables.IsSwimmer(EnemyKind.RegolithVole),
            $"{swimmers.Length} water critters and swimmers spawn inside water or sea fuel like the ocean swimmers");

        // ---- 6. A tree species per biome, with its own palette and timber ----
        var trunks = ContentFillFlora.BiomeTrees.Select(tree => tree.Trunk).ToArray();
        var grows = ContentFillFlora.BiomeTrees.Where(tree => tree.Ground != TileId.Rock)
            .All(tree => TilePhysics.TreeFor(tree.Ground)?.Trunk == tree.Trunk);
        var palettes = ContentFillFlora.Species.All(species => ContentFillFlora.BarkColor(species.Trunk) is not null &&
                                                                ContentFillFlora.CanopyColor(species.Canopy) is not null) &&
                       ContentFillFlora.Species.Select(species => species.Leaf).Distinct().Count() == ContentFillFlora.Species.Length;
        var forest = new GameSimulation(seed: 8802, width: 120, height: 48) { FamilySpawnsEnabled = false };
        var felled = new List<string>();
        var x0 = 6;
        foreach (var (biome, ground, trunk) in ContentFillFlora.BiomeTrees)
        {
            var x = x0;
            x0 += 9;
            for (var y = 4; y <= 20; y++)
            for (var dx = -4; dx <= 4; dx++)
                forest.World.SetCell(new GridPoint(x + dx, y), default);
            forest.World.SetTile(new GridPoint(x, 21), ground);
            var grown = trunk == TileId.GemTreeTrunk
                ? ContentFillFlora.TryGrowGemTree(forest.World, x, 20, new Random(1))
                : TilePhysics.TryGrowTree(forest.World, forest.Tiles, x, 20, new Random(1));
            if (!grown || forest.World.TileAt(x, 20) != trunk)
                continue;
            var timber = ContentFillFlora.TimberFor(trunk);
            var before = forest.Player.Inventory.Count(timber);
            forest.ForceBreakTile(new GridPoint(x, 20));
            if (forest.Player.Inventory.Count(timber) > before && !TilePhysics.IsTreePart(forest.World.TileAt(x, 18)))
                felled.Add(biome);
        }
        var gemDrops = ContentFillFlora.GemCrowns.All(gem => forest.Tiles.Contains(gem.Canopy));
        check(trunks.Distinct().Count() == trunks.Length && trunks.Length >= 9 && grows && palettes && gemDrops &&
              felled.Count == ContentFillFlora.BiomeTrees.Length,
            $"each biome grows its own tree type with its own palette and timber, gem trees included (felled: {string.Join(",", felled)})");

        // ---- 7. World generation plants biome trees, gem trees and harvestable wild plants ----
        var generated = new GameSimulation(seed: 8803, width: 400, height: 160, blight: WorldBlight.Overgrowth) { FamilySpawnsEnabled = false };
        int fillTrunks = 0, wildPlants = 0;
        var plantKinds = new HashSet<TileId>();
        for (var y = 0; y < generated.World.Height; y++)
        for (var x = 0; x < generated.World.Width; x++)
        {
            var tile = generated.World.TileAt(x, y);
            if (ContentFillFlora.IsTrunk(tile))
                fillTrunks++;
            if (ContentFillFlora.IsWildPlant(tile))
            {
                wildPlants++;
                plantKinds.Add(tile);
            }
        }
        var plantPoint = Enumerable.Range(0, generated.World.Width).SelectMany(x => Enumerable.Range(0, generated.World.Height)
            .Select(y => new GridPoint(x, y))).FirstOrDefault(point => ContentFillFlora.IsWildPlant(generated.World.TileAt(point.X, point.Y)));
        var harvested = false;
        if (ContentFillFlora.IsWildPlant(generated.World.TileAt(plantPoint.X, plantPoint.Y)))
        {
            var drop = ContentFillFlora.WildPlants.First(plant => plant.Tile == generated.World.TileAt(plantPoint.X, plantPoint.Y)).Drop;
            generated.ForceBreakTile(plantPoint);
            harvested = generated.Player.Inventory.Count(drop) > 0;
        }
        check(fillTrunks > 0 && wildPlants >= 10 && plantKinds.Count >= 3 && harvested,
            $"world generation grows biome trees ({fillTrunks} trunk cells) and harvestable wild plants ({wildPlants} of {plantKinds.Count} kinds; harvested {harvested})");

        // ---- 8. Travelling merchant: arrives some mornings with a rotating stock and leaves at dusk ----
        var market = new GameSimulation(seed: 8804, width: 80, height: 48) { FamilySpawnsEnabled = false };
        market.AddTownNpc(TownNpcKind.StationAi, market.Player.Motion.Position + new Vector2(2f, 0f), bound: false);
        market.AddTownNpc(TownNpcKind.Quartermaster, market.Player.Motion.Position + new Vector2(3f, 0f), bound: false);
        var visitDay = Enumerable.Range(market.Day + 1, 30).First(day => ContentFillTown.MerchantComes(market.Seed, day));
        var quietDay = Enumerable.Range(market.Day + 1, 30).First(day => !ContentFillTown.MerchantComes(market.Seed, day));
        bool MerchantHere() => market.TownNpcs.Any(npc => npc.Kind == TownNpcKind.TravellingMerchant);
        void AdvanceTo(int day, float hour)
        {
            while (market.Day < day || market.Day == day && market.ClockHour < hour)
            {
                market.ProbeAdvanceHours(1f);
                market.CheckTownArrivals();
            }
        }
        AdvanceTo(visitDay, 7f);
        var arrived = MerchantHere();
        var merchant = market.TownNpcs.FirstOrDefault(npc => npc.Kind == TownNpcKind.TravellingMerchant);
        var stock = merchant is null ? [] : market.GetShop(merchant).Select(offer => offer.Item).ToArray();
        var stockOk = stock.Length is >= 3 and <= ContentFillTown.MerchantStockSize &&
                      stock.All(item => ContentFillTown.TodaysStock(market.Seed, visitDay).Contains(item));
        AdvanceTo(visitDay, 18.5f);
        var left = !MerchantHere();
        var rotates = Enumerable.Range(1, 10).Any(day => !ContentFillTown.TodaysStock(market.Seed, day).SetEquals(ContentFillTown.TodaysStock(market.Seed, day + 1)));
        var quiet = new GameSimulation(seed: 8804, width: 80, height: 48) { FamilySpawnsEnabled = false };
        quiet.AddTownNpc(TownNpcKind.StationAi, quiet.Player.Motion.Position + new Vector2(2f, 0f), bound: false);
        quiet.AddTownNpc(TownNpcKind.Quartermaster, quiet.Player.Motion.Position + new Vector2(3f, 0f), bound: false);
        while (quiet.Day < quietDay || quiet.ClockHour < 9f)
        {
            quiet.ProbeAdvanceHours(1f);
            if (quiet.Day == quietDay)
                quiet.CheckTownArrivals();
        }
        var stayedAway = !quiet.TownNpcs.Any(npc => npc.Kind == TownNpcKind.TravellingMerchant);
        check(arrived && stockOk && left && rotates && stayedAway,
            $"the travelling merchant arrives on some mornings (day {visitDay}: {arrived}; not day {quietDay}: {stayedAway}) with a rotating " +
            $"stock ({stock.Length} items, rotates {rotates}) and leaves at dusk ({left})");

        // ---- 9. Town gels: found in their biome once the colony has residents, harmless, then move into free housing ----
        var gels = new GameSimulation(seed: 8805, width: 80, height: 48) { FamilySpawnsEnabled = false };
        foreach (var kind in new[] { TownNpcKind.StationAi, TownNpcKind.Quartermaster, TownNpcKind.Medic })
            gels.AddTownNpc(kind, gels.Player.Motion.Position + new Vector2(2f, 0f), bound: false);
        var foundGel = false;
        for (var hour = 0; hour < 72 && !foundGel; hour++)
        {
            gels.ProbeAdvanceHours(1f);
            gels.CheckTownArrivals();
            foundGel = gels.TownNpcs.Any(npc => ContentFillTown.IsGel(npc.Kind));
        }
        var gel = gels.TownNpcs.FirstOrDefault(npc => ContentFillTown.IsGel(npc.Kind));
        var gelHarmless = gel is not null && gels.GetShop(gel).Count == 0 && !gel.IsBound &&
                          gels.Flags.IsSet($"npc.{WorldFlags.Key(gel.Kind)}.unlocked");
        check(foundGel && gelHarmless && ContentFillTown.Gels.Length >= 3 &&
              ContentFillTown.Gels.All(entry => TownNpcCatalog.Get(entry.Kind).Shop.Length == 0),
            $"town gels ({ContentFillTown.Gels.Length} colours) are found in their biome by a growing colony, are harmless residents and move in ({gel?.Name})");
    }
}
