using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Gameplay for world-generation completion tiles: loot from rubble, scrap and shell piles, burrower nests, logs and
/// crystal shrubs; brood pods that wake the hive's matriarch; wired statues that release a creature per pulse; spike
/// strips and dropped boulders that hurt; the precursor altar that summons the vault colossus; loot tables for the new
/// world chests, and the depth top-up for every generated chest.
/// </summary>
public sealed partial class GameSimulation
{
    private readonly Dictionary<GridPoint, long> _statueReady = new();

    /// <summary>Per-tick hazards and the phase pool (called from the Batch B update).</summary>
    private void UpdateWorldGenContent(float deltaSeconds)
    {
        UpdatePhase(deltaSeconds);
        if (Player.IsDead)
            return;
        // Spike strips bite whoever stands in them.
        var motion = Player.Motion;
        var left = (int)MathF.Floor(motion.Position.X);
        var right = (int)MathF.Floor(motion.Position.X + motion.Size.X - 0.01f);
        var top = (int)MathF.Floor(motion.Position.Y);
        var bottom = (int)MathF.Floor(motion.Position.Y + motion.Size.Y - 0.01f);
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
            if (World.Contains(x, y) && World.GetCell(x, y).TileId == TileId.SpikeStrip)
            {
                Player.TakeHit(16f, DamageKind.Cut);
                y = bottom + 1;
                break;
            }
        // A dropped boulder crushes far harder than loose sand.
        foreach (var block in _falling)
            if (block.Tile == TileId.Boulder && block.VelocityY > 3f &&
                Overlaps(Player.Motion, new Vector2(block.X, block.Y), Vector2.One))
                Player.TakeHit(45f, DamageKind.Crush);
    }

    /// <summary>Drops and effects for world-generation completion tiles. True when handled.</summary>
    private bool HandleBrokenWorldGenTile(GridPoint point, TileId broken)
    {
        var at = new Vector2(point.X + 0.5f, point.Y + 0.5f);
        var random = _lootRandom;
        switch (broken)
        {
            case TileId.RubblePile:
                CollectOrDrop(ItemId.RockBlock, 2 + random.Next(4), at);
                if (random.Next(3) == 0)
                    CollectOrDrop(ItemId.Credits, 1 + random.Next(6), at);
                if (random.Next(8) == 0)
                    CollectOrDrop(point.Y > World.Height / 2 ? ItemId.CobaltOre : ItemId.CopperOre, 2 + random.Next(3), at);
                return true;
            case TileId.LootPile:
                CollectOrDrop(ItemId.Credits, 5 + random.Next(26), at);
                if (random.Next(3) == 0)
                    CollectOrDrop(ItemId.ScrapIronOre, 3 + random.Next(6), at);
                if (random.Next(7) == 0)
                    CollectOrDrop(ItemId.PowerCell, 1, at);
                return true;
            case TileId.ShellPile:
                CollectOrDrop(ItemId.CoralShard, 1 + random.Next(2), at);
                if (random.Next(5) == 0)
                    CollectOrDrop(ItemId.Credits, 2 + random.Next(8), at);
                return true;
            case TileId.BurrowerNest:
                CollectOrDrop(ItemId.BioGel, 1 + random.Next(3), at);
                if (random.Next(3) == 0)
                {
                    SpawnCreature(EnemyKind.DuneBorer, at + new Vector2(0f, -1f));
                    PostMessage("Something stirs in the broken nest.");
                }
                return true;
            case TileId.FallenLog:
                CollectOrDrop(ItemId.Timber, 3 + random.Next(4), at);
                return true;
            case TileId.CrystalShrub:
                CollectOrDrop(ContentTiles.GemTiles[random.Next(ContentTiles.GemTiles.Length)].Shard, 1 + random.Next(2), at);
                return true;
            case TileId.BroodPod:
                Flags.Increment("hive.brood.broken");
                if (BossAwaitsFracture(EnemyKind.BloomMatriarch))
                {
                    PostMessage("The brood pod bursts. Its matriarch stays asleep until the Fracture.");
                    SpawnCreature(EnemyKind.HiveGuard, at + new Vector2(0f, -1f));
                }
                else if (TrySpawnBoss(EnemyKind.BloomMatriarch, BossSpawnSource.WorldTrigger, out var boss, out _))
                    PostMessage($"The brood pod bursts. The {boss!.DisplayName} answers its brood!");
                else
                    SpawnCreature(EnemyKind.HiveGuard, at + new Vector2(0f, -1f));
                return true;
            case TileId.CanopyBulb:
                BreakCanopyBulb();
                return true;
        }
        if (ContentTiles.IsSpeleothem(broken))
            return true;
        return false;
    }

    /// <summary>A pulse reached a statue: it releases its creature (rate-limited, at most three nearby).</summary>
    private int TriggerStatue(GridPoint point, TileId statue)
    {
        var kind = statue switch
        {
            TileId.StatueCrab => EnemyKind.GeodeCrab,
            TileId.StatueBat => EnemyKind.LanternBat,
            _ => EnemyKind.GelHopper
        };
        if (_statueReady.TryGetValue(point, out var ready) && _batchBTick < ready)
            return 0;
        var origin = new Vector2(point.X + 0.5f, point.Y - 0.5f);
        if (_enemies.Count(enemy => enemy.IsAlive && enemy.Kind == kind && Vector2.Distance(enemy.Motion.Position, origin) < 24f) >= 3)
            return 0;
        _statueReady[point] = _batchBTick + 90;
        SpawnCreature(kind, origin + new Vector2(0f, -1f)).FromStatue = true; // EN-6: no loot, credits, kill count or banner
        Flags.Increment("statues.pulsed");
        return 1;
    }

    /// <summary>The precursor altar: offering an ancient power cell wakes the vault colossus.</summary>
    private bool TryInteractWorldGen(GridPoint target, TileId tile, out InteractionResult result)
    {
        result = null!;
        if (tile != TileId.VaultAltar)
            return false;
        if (Player.Inventory.Count(ItemId.AncientPowerCell) == 0)
        {
            result = new InteractionResult(WorldInteractionKind.None, "The altar's socket is dark. An ancient power cell would fit it.", Cell: target);
            return true;
        }
        if (!TrySpawnBoss(EnemyKind.VaultColossus, BossSpawnSource.WorldTrigger, out var boss, out _))
        {
            result = new InteractionResult(WorldInteractionKind.None, "The altar will not wake while the Vault Colossus stirs.", Cell: target);
            return true;
        }
        Player.Inventory.TryConsume(ItemId.AncientPowerCell);
        Flags.Increment("vault.altar.used");
        result = new InteractionResult(WorldInteractionKind.None, $"The cell sinks into the altar. The {boss!.DisplayName} wakes!", Cell: target);
        return true;
    }

    /// <summary>Loot for the chests world generation places (empty for others).</summary>
    private static (ItemId Item, int Min, int Max)[] WorldGenChestLoot(TileId tile) => tile switch
    {
        TileId.SunkenChest =>
        [
            (ItemId.Credits, 30, 120), (ItemId.CoralShard, 2, 6), (ItemId.TideFish, 1, 3), (ItemId.HarpoonGun, 0, 1),
            (ItemId.RestorativeTonic, 0, 2)
        ],
        TileId.SurfaceCrate =>
        [
            (ItemId.Credits, 10, 40), (ItemId.WallTorch, 5, 12), (ItemId.ClimbingRope, 10, 20), (ItemId.Berry, 2, 5),
            (ItemId.Sapling, 1, 3), (ItemId.Bomb, 0, 2)
        ],
        TileId.ShrineChest =>
        [
            (ItemId.Credits, 40, 150), (ItemId.MoonglowSeeds, 1, 3), (ItemId.RestorativeTonic, 1, 2), (ItemId.ClimbingClaws, 0, 1),
            (ItemId.GlowSpore, 0, 4)
        ],
        TileId.SkyChest =>
        [
            (ItemId.Credits, 60, 200), (ItemId.GliderWings, 0, 1), (ItemId.PrismShard, 0, 3), (ItemId.AncientPowerCell, 0, 1)
        ],
        TileId.LanderChest =>
        [
            (ItemId.Credits, 80, 260), (ItemId.PowerCell, 1, 3), (ItemId.DroneSpareParts, 1, 4), (ItemId.Dynamite, 0, 3),
            (ItemId.AncientPowerCell, 0, 1)
        ],
        TileId.SlagChest =>
        [
            (ItemId.Credits, 150, 500), (ItemId.PyrociteBar, 3, 8), (ItemId.ObsidianShard, 4, 10), (ItemId.UraniumOre, 2, 6),
            (ItemId.Dynamite, 1, 4), (ItemId.RadFilterMask, 0, 1), (ItemId.VoidEssence, 0, 2)
        ],
        _ => []
    };

    /// <summary>Useful finds per depth tier: a generated chest that rolled none of its tier's gets one.</summary>
    private static readonly ItemId[][] ChestDepthFinds =
    [
        [ItemId.ClimbingClaws, ItemId.RestorativeTonic, ItemId.ShapingHammer],
        [ItemId.GrappleHook, ItemId.ClimbingClaws, ItemId.Bomb],
        [ItemId.GrappleHook, ItemId.Dynamite, ItemId.TeleporterPad],
        [ItemId.Dynamite, ItemId.AncientPowerCell, ItemId.VoidEssence]
    ];

    /// <summary>Everything a generated chest may gain on top of its table: the depth finds and the rare thruster fins (crafting audit).</summary>
    internal static IEnumerable<ItemId> ChestExtraFinds => ChestDepthFinds.SelectMany(finds => finds).Append(ItemId.SalvagedThrusterFins);

    private void ImproveChestLoot(GridPoint cell, Inventory inventory)
    {
        var tier = Features?.ChestTier(cell) ?? -1;
        if (tier is < 0 or > 3)
            return;
        // Every generated chest also carries a material of the biome it sits in (frost in the ice, uranium in the core...).
        if (BiomeChestMaterial(BiomeScanner.Scan(World, cell).Zone) is { } material && inventory.Count(material.Item) == 0)
            inventory.Add(material.Item, material.Min + _lootRandom.Next(material.Max - material.Min + 1));
        var finds = ChestDepthFinds[tier];
        if (finds.Any(item => inventory.Count(item) > 0))
            return;
        inventory.Add(finds[_lootRandom.Next(finds.Length)], 1);
    }

    /// <summary>The themed material a generated chest gains from its biome (BIOME_THEMES.md), or null for plain ground.</summary>
    internal static (ItemId Item, int Min, int Max)? BiomeChestMaterial(BiomeZone zone) => zone switch
    {
        BiomeZone.FrozenWasteland => (ItemId.FrostCrystal, 2, 5),
        BiomeZone.JunkWasteland => (ItemId.OxidizedShard, 2, 5),
        BiomeZone.RadiatedDeeps => (ItemId.CausticShard, 2, 4),
        BiomeZone.Overgrowth => (ItemId.ViridiumOre, 2, 5),
        BiomeZone.MoltenChasm => (ItemId.UraniumOre, 3, 7),
        BiomeZone.AcidCoast => (ItemId.CoralShard, 2, 6),
        BiomeZone.FungalHollows => (ItemId.GlowSpore, 3, 8),
        BiomeZone.VerdantCanopy => (ItemId.JungleRootBlock, 4, 10),
        BiomeZone.BloomHives => (ItemId.NectarBlock, 3, 8),
        BiomeZone.CrystalSpires => (ItemId.PrismShard, 1, 3),
        BiomeZone.MiteWarrens => (ItemId.SilkThread, 3, 8),
        _ => null
    };
}
