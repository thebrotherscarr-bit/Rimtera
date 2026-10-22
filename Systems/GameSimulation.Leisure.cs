using System.Numerics;
using Rimtera.AI;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Town pets that move in with a permit (original names; cat, dog and rabbit analogs).</summary>
public enum TownPetKind : byte
{
    Purrbot,
    Rover,
    Hopling
}

/// <summary>A harmless pet wandering near its house.</summary>
public sealed class TownPet
{
    internal TownPet(TownPetKind kind, string name, Vector2 position, GridPoint home)
    {
        Kind = kind;
        Name = name;
        Position = position;
        HomeCell = home;
        WanderTarget = position.X;
    }

    public TownPetKind Kind { get; }
    public string Name { get; }
    /// <summary>Top-left of the pet's box (<see cref="Size"/>).</summary>
    public Vector2 Position { get; internal set; }
    public Vector2 Size => new(0.8f, 0.7f);
    public GridPoint HomeCell { get; }
    public float Facing { get; internal set; } = 1f;
    public bool Moving { get; internal set; }
    /// <summary>Seconds of hearts after being petted (renderer shows a heart).</summary>
    public float HappySeconds { get; internal set; }
    public string SpriteName => Kind switch
    {
        TownPetKind.Purrbot => "pet_purrbot",
        TownPetKind.Rover => "pet_rover",
        _ => "pet_hopling"
    };
    internal float WanderTarget { get; set; }
    internal float WanderTimer { get; set; }
}

/// <summary>A flying kite on its line (renderer and tests).</summary>
public sealed record KiteView(ItemId Item, Vector2 Anchor, Vector2 Position, float Altitude);

/// <summary>
/// Item, tile and recipe data for the world &amp; leisure systems (docs/TERRARIA_GAPS.md plan item 3): wave-moon summons,
/// kites, pet permits, golf gear, paint and coatings, and audio decks. Names and numbers are original.
/// </summary>
public static class LeisureContent
{
    /// <summary>Paint colours by index 1–12 (0 = unpainted): name and RGB.</summary>
    public static readonly (string Name, byte R, byte G, byte B)[] PaintColors =
    [
        ("Unpainted", 0, 0, 0),
        ("Signal red", 220, 50, 50), ("Hazard orange", 245, 140, 30), ("Solar yellow", 245, 225, 60), ("Moss lime", 160, 230, 60),
        ("Canopy green", 40, 170, 70), ("Coolant teal", 30, 170, 160), ("Ion cyan", 70, 220, 255), ("Sky blue", 110, 160, 255),
        ("Deep blue", 40, 60, 200), ("Void violet", 150, 70, 220), ("Neon pink", 255, 90, 190), ("Shadow black", 25, 25, 35)
    ];

    public static readonly ItemId[] Paints =
    [
        ItemId.PaintSignalRed, ItemId.PaintHazardOrange, ItemId.PaintSolarYellow, ItemId.PaintMossLime, ItemId.PaintCanopyGreen,
        ItemId.PaintCoolantTeal, ItemId.PaintIonCyan, ItemId.PaintSkyBlue, ItemId.PaintDeepBlue, ItemId.PaintVoidViolet,
        ItemId.PaintNeonPink, ItemId.PaintShadowBlack
    ];

    /// <summary>Music themes an audio deck can hold (AudioDirector theme names), with their deck item and tile.</summary>
    public static readonly (string Theme, ItemId Item, TileId Tile)[] Decks =
    [
        ("grassland", ItemId.DeckGrassland, TileId.DeckGrassland), ("underground", ItemId.DeckUnderground, TileId.DeckUnderground),
        ("frozen", ItemId.DeckFrozen, TileId.DeckFrozen), ("junk", ItemId.DeckJunk, TileId.DeckJunk),
        ("molten", ItemId.DeckMolten, TileId.DeckMolten), ("radiated", ItemId.DeckRadiated, TileId.DeckRadiated),
        ("overgrowth", ItemId.DeckOvergrowth, TileId.DeckOvergrowth), ("fungal", ItemId.DeckFungal, TileId.DeckFungal),
        ("crystal", ItemId.DeckCrystal, TileId.DeckCrystal), ("canopy", ItemId.DeckCanopy, TileId.DeckCanopy),
        ("coast", ItemId.DeckCoast, TileId.DeckCoast), ("boss", ItemId.DeckBoss, TileId.DeckBoss)
    ];

    /// <summary>Kites: line length in tiles.</summary>
    public static float KiteLine(ItemId kite) => kite switch
    {
        ItemId.BoxKite => 9f,
        ItemId.RibbonKite => 12f,
        _ => 7f
    };

    public static bool IsKite(ItemId item) => item is ItemId.DeltaKite or ItemId.BoxKite or ItemId.RibbonKite;
    public static bool IsDeckTile(TileId tile) => tile is >= TileId.DeckGrassland and <= TileId.DeckBoss;
    public static string? DeckTheme(TileId tile) => Decks.FirstOrDefault(deck => deck.Tile == tile).Theme;

    public static IReadOnlyList<TileDefinition> TileDefinitions { get; } = BuildTiles();

    private static TileDefinition[] BuildTiles()
    {
        var tiles = new List<TileDefinition>
        {
            new(TileId.GolfTee, "Golf tee", false, false, 10, 0.4f, 140),
            new(TileId.GolfCup, "Golf cup", false, false, 14, 0.5f, 141)
        };
        foreach (var (theme, _, tile) in Decks)
            tiles.Add(new TileDefinition(tile, $"Audio deck ({theme})", false, false, 24, 0.8f, 142));
        return tiles.ToArray();
    }

    public static ItemId? TileDrop(TileId tile) => tile switch
    {
        TileId.GolfTee => ItemId.GolfTee,
        TileId.GolfCup => ItemId.GolfCup,
        _ when IsDeckTile(tile) => Decks.First(deck => deck.Tile == tile).Item,
        _ => null
    };

    public static void RegisterItems(Dictionary<ItemId, ItemDefinition> items)
    {
        void Add(ItemDefinition definition) => items[definition.Id] = definition;
        ItemDefinition Tool(ItemId id, string name, int tier, int value, int cooldown = 12) =>
            new(id, name, ItemUseKind.Utility, 1) { Tier = tier, Rarity = ContentItems.RarityFor(tier), Value = value, UseCooldownTicks = cooldown };

        // Seasonal summons and the wave-moon materials.
        Add(new ItemDefinition(ItemId.HollowLantern, "Hollow lantern", ItemUseKind.Consumable, 20)
            { StartsEvent = WorldEventKind.HollowMoon, Tier = 5, Rarity = ContentItems.RarityFor(5), Value = 160 });
        Add(new ItemDefinition(ItemId.RimeBeacon, "Rime beacon", ItemUseKind.Consumable, 20)
            { StartsEvent = WorldEventKind.RimeMoon, Tier = 5, Rarity = ContentItems.RarityFor(5), Value = 160 });
        Add(new ItemDefinition(ItemId.CryoOrders, "Cryo brigade orders", ItemUseKind.Consumable, 20)
            { StartsEvent = WorldEventKind.CryoBrigade, Tier = 4, Rarity = ContentItems.RarityFor(4), Value = 90 });
        Add(new ItemDefinition(ItemId.HollowEmber, "Hollow ember", ItemUseKind.Material, 999)
            { IsMaterial = true, Tier = 5, Rarity = ContentItems.RarityFor(5), Value = 25 });
        Add(new ItemDefinition(ItemId.RimeShard, "Rime shard", ItemUseKind.Material, 999)
            { IsMaterial = true, Tier = 5, Rarity = ContentItems.RarityFor(5), Value = 25 });

        // Kites.
        Add(Tool(ItemId.DeltaKite, "Delta kite", 1, 30));
        Add(Tool(ItemId.BoxKite, "Box kite", 2, 50));
        Add(Tool(ItemId.RibbonKite, "Ribbon kite", 2, 70));

        // Town pet permits.
        Add(Tool(ItemId.PurrbotPermit, "Purrbot permit", 2, 120));
        Add(Tool(ItemId.RoverPermit, "Rover permit", 2, 120));
        Add(Tool(ItemId.HoplingPermit, "Hopling permit", 2, 120));

        // Golf.
        Add(new ItemDefinition(ItemId.GolfBall, "Golf ball", ItemUseKind.Utility, 99) { Tier = 1, Value = 10, UseCooldownTicks = 10 });
        Add(Tool(ItemId.GolfPutter, "Putter", 1, 40, 0));
        Add(Tool(ItemId.GolfIron, "Approach iron", 1, 60, 0));
        Add(Tool(ItemId.GolfDriver, "Long driver", 2, 90, 0));
        Add(new ItemDefinition(ItemId.GolfTee, "Golf tee", ItemUseKind.PlaceTile, 99) { PlacedTile = TileId.GolfTee, Value = 4 });
        Add(new ItemDefinition(ItemId.GolfCup, "Golf cup", ItemUseKind.PlaceTile, 99) { PlacedTile = TileId.GolfCup, Value = 8 });

        // Paint and coatings.
        for (var index = 0; index < Paints.Length; index++)
            Add(new ItemDefinition(Paints[index], PaintColors[index + 1].Name + " paint", ItemUseKind.Material, 999)
                { IsMaterial = true, Value = 2 });
        Add(Tool(ItemId.Paintbrush, "Paintbrush", 1, 20, 6));
        Add(Tool(ItemId.PaintRoller, "Paint roller", 1, 20, 6));
        Add(Tool(ItemId.PaintScraper, "Paint scraper", 1, 20, 6));
        Add(new ItemDefinition(ItemId.GlowCoating, "Glow coating", ItemUseKind.Utility, 999) { Value = 6, UseCooldownTicks = 6 });

        // Audio decks.
        Add(new ItemDefinition(ItemId.AudioDeck, "Blank audio deck", ItemUseKind.Material, 20) { Tier = 2, Value = 60 });
        foreach (var (theme, item, tile) in Decks)
            Add(new ItemDefinition(item, $"Audio deck ({theme})", ItemUseKind.PlaceTile, 20)
                { PlacedTile = tile, Tier = 2, Rarity = ContentItems.RarityFor(2), Value = 80 });
    }

    public static void RegisterRecipes(Action<string, ItemId, int, CraftingStationId[], (ItemId Item, int Count)[]> add)
    {
        CraftingStationId[] bench = [CraftingStationId.Workbench];
        add("leisure-hollow-lantern", ItemId.HollowLantern, 1, [CraftingStationId.PowerForgeMk2],
            [(ItemId.WoodBlock, 20), (ItemId.EnergyCrystal, 4), (ItemId.BlightEssence, 4)]);
        add("leisure-rime-beacon", ItemId.RimeBeacon, 1, [CraftingStationId.PowerForgeMk2],
            [(ItemId.SnowBlock, 30), (ItemId.CircuitBoard, 4), (ItemId.PrismEssence, 4)]);
        add("leisure-cryo-orders", ItemId.CryoOrders, 1, [CraftingStationId.PowerForgeMk2],
            [(ItemId.SnowBlock, 50), (ItemId.ClothRoll, 2), (ItemId.FlightEssence, 2)]);
        add("leisure-delta-kite", ItemId.DeltaKite, 1, bench, [(ItemId.ClothRoll, 2), (ItemId.WoodBlock, 4), (ItemId.RopeCoil, 1)]);
        add("leisure-box-kite", ItemId.BoxKite, 1, bench, [(ItemId.ClothRoll, 4), (ItemId.WoodBlock, 8), (ItemId.RopeCoil, 1)]);
        add("leisure-ribbon-kite", ItemId.RibbonKite, 1, bench, [(ItemId.ClothRoll, 3), (ItemId.SilkThread, 4), (ItemId.RopeCoil, 2)]);
        add("leisure-purrbot", ItemId.PurrbotPermit, 1, bench, [(ItemId.CircuitBoard, 2), (ItemId.Credits, 60)]);
        add("leisure-rover", ItemId.RoverPermit, 1, bench, [(ItemId.CircuitBoard, 2), (ItemId.Credits, 60)]);
        add("leisure-hopling", ItemId.HoplingPermit, 1, bench, [(ItemId.Berry, 10), (ItemId.Credits, 60)]);
        add("leisure-golf-ball", ItemId.GolfBall, 3, bench, [(ItemId.RockBlock, 2), (ItemId.SandBlock, 1)]);
        add("leisure-golf-tee", ItemId.GolfTee, 2, bench, [(ItemId.WoodBlock, 1)]);
        add("leisure-golf-cup", ItemId.GolfCup, 1, bench, [(ItemId.CopperBar, 1), (ItemId.ClothRoll, 1)]);
        add("leisure-putter", ItemId.GolfPutter, 1, bench, [(ItemId.CopperBar, 3), (ItemId.WoodBlock, 2)]);
        for (var index = 0; index < Paints.Length; index++)
            add("leisure-paint-" + (index + 1), Paints[index], 10, bench, [(ItemId.DyePetals, 1), (ItemId.SandBlock, 1)]);
        add("leisure-paintbrush", ItemId.Paintbrush, 1, bench, [(ItemId.WoodBlock, 3), (ItemId.ClothRoll, 1)]);
        add("leisure-paint-roller", ItemId.PaintRoller, 1, bench, [(ItemId.WoodBlock, 3), (ItemId.ClothRoll, 2)]);
        add("leisure-paint-scraper", ItemId.PaintScraper, 1, bench, [(ItemId.CopperBar, 2)]);
        add("leisure-glow-coating", ItemId.GlowCoating, 5, bench, [(ItemId.EnergyCrystal, 1), (ItemId.DyePetals, 1)]);
        add("leisure-audio-deck", ItemId.AudioDeck, 1, bench, [(ItemId.CircuitBoard, 2), (ItemId.PowerCell, 1), (ItemId.CopperBar, 4)]);
    }

    /// <summary>The golf steward: moves in once a golf ball has been found; sells clubs, balls, tees and cups.</summary>
    public static TownNpcDefinition Greenskeeper() => new(TownNpcKind.Greenskeeper, "Greenskeeper", "npc_colony_worker",
        ["Pell Okafor", "Marit Sondre"],
        [(BiomeZone.Grassland, Affection.Love), (BiomeZone.RadiatedDeeps, Affection.Hate)],
        [(TownNpcKind.Aeronaut, Affection.Like), (TownNpcKind.Demolitionist, Affection.Dislike)],
        [(ItemId.GolfBall, null), (ItemId.GolfTee, null), (ItemId.GolfCup, null), (ItemId.GolfPutter, null), (ItemId.GolfIron, null),
         (ItemId.GolfDriver, "blight.fracture"), (ItemId.DeltaKite, null), (ItemId.BoxKite, null)],
        ["Low gravity, long fairways. This planet was made for golf.", "Keep it out of the coolant; I'm not fishing it out again.",
         "A steady wind is a gift for kites and a curse for drives."]);
}

/// <summary>
/// World &amp; leisure: wind on airborne items and light projectiles, the windy-day push, kites, audio decks (recording and the
/// nearby theme override), town pets, paint and coatings, and the per-tick driver for golf, the wave moons and creative powers.
/// </summary>
public sealed partial class GameSimulation
{
    public const float WindItemPush = 1.4f;
    public const float WindShotPush = 0.12f;
    public const float WindThrownPush = 0.6f;
    public const float KiteMinimumWind = 0.8f;
    public const float DeckRecordSeconds = 6f;
    public const float DeckHearingRadius = 24f;
    public const string PaintFlagPrefix = "paint.";

    private KiteState? _kite;
    private float _deckRecording;
    private int _deckScanTick = -1000;
    private string? _deckOverride;
    private readonly List<TownPet> _pets = new();
    private bool _leisureLoaded;
    private Dictionary<GridPoint, int>? _paint;
    private WorldFlags? _paintSource;

    private sealed class KiteState
    {
        public ItemId Item;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Age;
    }

    /// <summary>The theme the music director is currently playing (set by UI/AudioDirector each frame; tests may set it).</summary>
    public string? HeardMusicTheme { get; set; }

    /// <summary>The theme of the nearest switched-on audio deck within hearing, or null.</summary>
    public string? MusicThemeOverride => _deckOverride;

    /// <summary>The kite in the air, or null.</summary>
    public KiteView? ActiveKite => _kite is { } kite
        ? new KiteView(kite.Item, KiteAnchor, kite.Position, KiteAnchor.Y - kite.Position.Y)
        : null;

    public IReadOnlyList<TownPet> TownPets => _pets;

    private Vector2 KiteAnchor => PlayerCenter + new Vector2(Player.Motion.Facing * 0.4f, -0.4f);

    /// <summary>Per-tick driver for every leisure system (one hook in <see cref="Tick"/>).</summary>
    private void UpdateLeisure(float deltaSeconds)
    {
        if (!_leisureLoaded)
            LoadLeisureFromFlags();
        ApplyWind(deltaSeconds);
        UpdateKite(deltaSeconds);
        UpdateDecks(deltaSeconds);
        UpdateTownPets(deltaSeconds);
        UpdateGolf(deltaSeconds);
        UpdateSeasons(deltaSeconds);
        UpdateCreative(deltaSeconds);
        if (_worldEvents.IsActive(WorldEventKind.WindyDay) && MathF.Abs(Weather.Wind) < 6.5f)
            _weather.Force(wind: (Day % 2 == 0 ? 1f : -1f) * 7.5f, immediate: true);
    }

    /// <summary>Restores pets, a golf ball in play and similar leisure state recorded in the world flags (after a load).</summary>
    private void LoadLeisureFromFlags()
    {
        _leisureLoaded = true;
        foreach (var kind in Enum.GetValues<TownPetKind>())
        {
            var packed = Flags.Get(PetFlag(kind));
            if (packed <= 0 || _pets.Any(pet => pet.Kind == kind))
                continue;
            var home = new GridPoint((int)(packed >> 16), (int)(packed & 0xFFFF));
            if (World.Contains(home))
                _pets.Add(new TownPet(kind, PetName(kind), new Vector2(home.X, home.Y + 0.3f), home));
        }
        RestoreGolfFromFlags();
    }

    private static string PetFlag(TownPetKind kind) => $"pet.{WorldFlags.Key(kind)}.home";

    /// <summary>Writes a flag value exactly (0 clears it).</summary>
    private void SetFlagValue(string name, long value)
    {
        if (value == 0)
            Flags.Clear(name);
        else
            Flags.Increment(name, value - Flags.Get(name));
    }

    /// <summary>Leisure items handled ahead of the normal use switch. False for items it does not own.</summary>
    private bool TryUseLeisureItem(ItemDefinition item, GridPoint point)
    {
        if (LeisureContent.IsKite(item.Id))
        {
            ToggleKite(item.Id);
            return true;
        }
        switch (item.Id)
        {
            case ItemId.PurrbotPermit:
                UsePetPermit(item.Id, TownPetKind.Purrbot);
                return true;
            case ItemId.RoverPermit:
                UsePetPermit(item.Id, TownPetKind.Rover);
                return true;
            case ItemId.HoplingPermit:
                UsePetPermit(item.Id, TownPetKind.Hopling);
                return true;
            case ItemId.Paintbrush or ItemId.PaintRoller or ItemId.PaintScraper or ItemId.GlowCoating:
                UsePaintTool(item, point);
                return true;
            case ItemId.GolfBall:
                PlaceGolfBall(point);
                return true;
            case ItemId.GolfPutter or ItemId.GolfIron or ItemId.GolfDriver:
                ChargeGolfClub(item.Id, point);
                return true;
        }
        return false;
    }

    /// <summary>Pets and audio decks; null when the target is neither.</summary>
    private InteractionResult? InteractLeisure(GridPoint target)
    {
        var point = new Vector2(target.X + 0.5f, target.Y + 0.5f);
        var pet = _pets.FirstOrDefault(candidate => Vector2.Distance(candidate.Position + candidate.Size / 2f, point) <= 1.1f);
        if (pet is not null)
            return PetTownPet(pet);
        var cell = World.GetCell(target);
        if (LeisureContent.IsDeckTile(cell.TileId))
        {
            cell.State = (byte)(cell.State ^ 1);
            World.SetCell(target, cell);
            _deckScanTick = -1000;
            var on = (cell.State & 1) != 0;
            return new InteractionResult(WorldInteractionKind.None,
                on ? $"The audio deck spins up: {LeisureContent.DeckTheme(cell.TileId)}." : "The audio deck clicks off.", Cell: target);
        }
        return null;
    }

    // ------------------------------------------------------------------ wind

    /// <summary>True when the point is outdoors: no background wall and nothing solid for a long way above.</summary>
    public bool IsOpenAir(Vector2 position)
    {
        var x = (int)MathF.Floor(position.X);
        var y = (int)MathF.Floor(position.Y);
        if (!World.Contains(x, Math.Clamp(y, 0, World.Height - 1)))
            return false;
        if (y >= 0 && y < World.Height && World.GetCell(x, y).WallId != 0)
            return false;
        for (var probe = Math.Min(y - 1, World.Height - 1); probe >= Math.Max(0, y - 48); probe--)
        {
            var tile = World.GetCell(x, probe).TileId;
            if (tile != TileId.Air && Tiles.Contains(tile) && Tiles[tile].IsSolid)
                return false;
        }
        return true;
    }

    /// <summary>The weather's wind pushes airborne ground items, ballistic shots (a little) and thrown weapons (more).</summary>
    private void ApplyWind(float deltaSeconds)
    {
        var wind = Weather.Wind;
        if (MathF.Abs(wind) < 0.05f)
            return;
        foreach (var item in _groundItems)
        {
            if (item.Grounded || item.Attracted || !IsOpenAir(item.Position))
                continue;
            item.Velocity += new Vector2(wind * WindItemPush * deltaSeconds, 0f);
        }
        foreach (var projectile in _projectiles)
        {
            if (projectile.Team != ProjectileTeam.Player || projectile.Tethered || projectile.Returning)
                continue;
            var push = projectile.Style is WeaponStyle.Thrown or WeaponStyle.Boomerang ? WindThrownPush
                : projectile.Class == DamageClass.Ranged ? WindShotPush : 0f;
            if (push > 0f && IsOpenAir(projectile.Position))
                projectile.Velocity += new Vector2(wind * push * deltaSeconds, 0f);
        }
    }

    // ------------------------------------------------------------------ kites

    private void ToggleKite(ItemId kite)
    {
        if (_kite is not null)
        {
            _kite = null;
            PostMessage("You reel in the kite.");
            SetPlayerCooldown(10);
            return;
        }
        if (!IsOpenAir(PlayerCenter))
        {
            PostMessage("Kites need open sky.");
            return;
        }
        if (MathF.Abs(Weather.Wind) < KiteMinimumWind)
        {
            PostMessage("There is not enough wind to fly a kite.");
            return;
        }
        var side = MathF.Sign(Weather.Wind);
        _kite = new KiteState { Item = kite, Position = KiteAnchor + new Vector2(side * 1f, -1f) };
        PostMessage("The kite catches the wind.");
        SetPlayerCooldown(10);
    }

    private void UpdateKite(float deltaSeconds)
    {
        if (_kite is not { } kite)
            return;
        if (Player.Inventory.SelectedItem?.Id != kite.Item || Player.IsDead)
        {
            _kite = null;
            return;
        }
        kite.Age += deltaSeconds;
        var wind = Weather.Wind;
        var strength = MathF.Abs(wind);
        var line = LeisureContent.KiteLine(kite.Item);
        var anchor = KiteAnchor;
        var lift = Math.Clamp((strength - 0.5f) / 5f, 0f, 1.2f);
        Vector2 target;
        if (strength < 0.5f || !IsOpenAir(anchor))
            target = anchor + new Vector2(MathF.Sign(kite.Position.X - anchor.X) * 1.5f, 1.5f);
        else
        {
            var bob = MathF.Sin(kite.Age * (2f + strength * 0.4f)) * 0.25f * MathF.Min(strength, 6f) / 3f;
            target = anchor + new Vector2(MathF.Sign(wind) * line * (0.45f + 0.3f * (1.2f - lift)),
                -line * (0.2f + 0.6f * lift) + bob);
        }
        kite.Velocity += (target - kite.Position) * 4f * deltaSeconds;
        kite.Velocity *= MathF.Pow(0.15f, deltaSeconds);
        kite.Position += kite.Velocity * deltaSeconds;
        // The line never stretches past its length.
        var offset = kite.Position - anchor;
        if (offset.Length() > line)
            kite.Position = anchor + Vector2.Normalize(offset) * line;
        if (strength < 0.5f && kite.Position.Y > anchor.Y + 1f && kite.Age > 1f)
        {
            _kite = null;
            PostMessage("The wind dies and the kite drops into your hands.");
        }
    }

    // ------------------------------------------------------------------ audio decks

    private void UpdateDecks(float deltaSeconds)
    {
        if (Player.Inventory.SelectedItem?.Id == ItemId.AudioDeck && HeardMusicTheme is { } theme &&
            LeisureContent.Decks.FirstOrDefault(deck => deck.Theme == theme) is { Theme: not null } deck)
        {
            _deckRecording += deltaSeconds;
            if (_deckRecording >= DeckRecordSeconds && Player.Inventory.TryConsume(ItemId.AudioDeck))
            {
                _deckRecording = 0f;
                CollectOrDrop(deck.Item, 1, PlayerCenter);
                PostMessage($"The audio deck has recorded the {theme} theme.");
            }
        }
        else
            _deckRecording = 0f;

        if (TickNumber - _deckScanTick < 20)
            return;
        _deckScanTick = (int)TickNumber;
        _deckOverride = null;
        var center = PlayerCenter;
        var best = float.MaxValue;
        var radius = (int)DeckHearingRadius;
        for (var y = Math.Max(0, (int)center.Y - radius); y <= Math.Min(World.Height - 1, (int)center.Y + radius); y++)
        for (var x = Math.Max(0, (int)center.X - radius); x <= Math.Min(World.Width - 1, (int)center.X + radius); x++)
        {
            var cell = World.GetCell(x, y);
            if (!LeisureContent.IsDeckTile(cell.TileId) || (cell.State & 1) == 0)
                continue;
            var distance = Vector2.DistanceSquared(center, new Vector2(x + 0.5f, y + 0.5f));
            if (distance < best && distance <= DeckHearingRadius * DeckHearingRadius)
            {
                best = distance;
                _deckOverride = LeisureContent.DeckTheme(cell.TileId);
            }
        }
    }

    /// <summary>Re-scans for playing decks on the next tick (tests, after placing tiles directly).</summary>
    public void RefreshAudioDecks() => _deckScanTick = -1000;

    // ------------------------------------------------------------------ town pets

    private static string PetName(TownPetKind kind) => kind switch
    {
        TownPetKind.Purrbot => "Purrbot",
        TownPetKind.Rover => "Rover",
        _ => "Hopling"
    };

    private void UsePetPermit(ItemId permit, TownPetKind kind)
    {
        if (_pets.Any(pet => pet.Kind == kind))
        {
            PostMessage($"A {PetName(kind)} already lives in the colony.");
            return;
        }
        if (FindPetHouse() is not { } home)
        {
            PostMessage("A pet needs a free, valid house in the colony.");
            return;
        }
        if (!Player.Inventory.TryConsume(permit))
            return;
        var pet = new TownPet(kind, PetName(kind), new Vector2(home.X, home.Y + 0.3f), home);
        _pets.Add(pet);
        SetFlagValue(PetFlag(kind), ((long)home.X << 16) | (uint)home.Y);
        PostMessage($"A {pet.Name} moves into the colony!");
        SetPlayerCooldown(20);
    }

    /// <summary>A valid house that no resident and no other pet has claimed.</summary>
    private GridPoint? FindPetHouse()
    {
        _regions.EnsureCurrent();
        foreach (var room in _regions.Rooms.Where(room => room.CellCount >= HousingValidator.MinimumCells)
                     .OrderBy(room => Vector2.Distance(room.Centroid, PlayerCenter)))
        {
            var cell = room.CellIndices.Count > 0
                ? new GridPoint(room.CellIndices[0] % World.Width, room.CellIndices[0] / World.Width)
                : RegionMap.CellAt(room.Centroid);
            if (!CheckHousing(cell).Valid || _pets.Any(pet => _regions.GetRegion(pet.HomeCell) == room))
                continue;
            return FloorCellOf(room) ?? cell;
        }
        return null;
    }

    private InteractionResult PetTownPet(TownPet pet)
    {
        pet.HappySeconds = 4f;
        Player.Statuses.Apply(StatusId.Regeneration, 0.5f, Player.Stats.ImmunityTags);
        Player.Needs.Restore(NeedKind.Joy, 0.1f);
        return new InteractionResult(WorldInteractionKind.None, $"You pet {pet.Name}. You feel content.");
    }

    private void UpdateTownPets(float deltaSeconds)
    {
        foreach (var pet in _pets)
        {
            pet.HappySeconds = Math.Max(0f, pet.HappySeconds - deltaSeconds);
            pet.WanderTimer -= deltaSeconds;
            if (pet.WanderTimer <= 0f)
            {
                pet.WanderTimer = 2f + (float)_worldRandom.NextDouble() * 4f;
                pet.WanderTarget = pet.HomeCell.X + ((float)_worldRandom.NextDouble() * 2f - 1f) * 6f;
            }
            var dx = pet.WanderTarget - pet.Position.X;
            pet.Moving = MathF.Abs(dx) > 0.2f && pet.HappySeconds <= 0f;
            if (!pet.Moving)
                continue;
            pet.Facing = MathF.Sign(dx);
            var nextX = pet.Position.X + pet.Facing * 1.3f * deltaSeconds;
            var feet = (int)MathF.Floor(pet.Position.Y + pet.Size.Y - 0.05f);
            var column = (int)MathF.Floor(nextX + (pet.Facing > 0 ? pet.Size.X : 0f));
            if (!World.Contains(column, Math.Clamp(feet, 0, World.Height - 1)) || SolidAt(column, feet) && SolidAt(column, feet - 1))
            {
                pet.WanderTarget = pet.Position.X;
                continue;
            }
            // Step up one block, otherwise settle onto the ground below.
            var floor = feet + 1;
            if (SolidAt(column, feet))
                floor = feet;
            else
                while (floor < Math.Min(World.Height - 1, feet + 4) && !SolidAt(column, floor))
                    floor++;
            if (floor >= feet + 4)
            {
                pet.WanderTarget = pet.Position.X;
                continue;
            }
            pet.Position = new Vector2(nextX, floor - pet.Size.Y);
        }
    }

    private bool SolidAt(int x, int y)
    {
        if (!World.Contains(x, y))
            return true;
        var tile = World.GetCell(x, y).TileId;
        return tile != TileId.Air && Tiles.Contains(tile) && Tiles[tile].IsSolid;
    }

    // ------------------------------------------------------------------ paint and coatings

    /// <summary>Packed paint per cell: bits 0–3 tile colour, 4–7 wall colour, bit 8 glow coating.</summary>
    private Dictionary<GridPoint, int> PaintMap
    {
        get
        {
            if (_paint is not null && ReferenceEquals(_paintSource, Flags))
                return _paint;
            _paintSource = Flags;
            _paint = new Dictionary<GridPoint, int>();
            foreach (var (key, value) in Flags.All)
            {
                if (!key.StartsWith(PaintFlagPrefix, StringComparison.Ordinal) || value == 0)
                    continue;
                var parts = key[PaintFlagPrefix.Length..].Split('.');
                if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y) && World.Contains(x, y))
                    _paint[new GridPoint(x, y)] = (int)value;
            }
            return _paint;
        }
    }

    /// <summary>Every painted cell (renderer).</summary>
    public IReadOnlyDictionary<GridPoint, int> PaintedCells => PaintMap;
    public int TilePaintAt(GridPoint cell) => PaintMap.TryGetValue(cell, out var value) ? value & 0xF : 0;
    public int WallPaintAt(GridPoint cell) => PaintMap.TryGetValue(cell, out var value) ? (value >> 4) & 0xF : 0;
    public bool IsGlowCoated(GridPoint cell) => PaintMap.TryGetValue(cell, out var value) && (value & 0x100) != 0;

    private void SetPaint(GridPoint cell, int value)
    {
        if (value == 0)
            PaintMap.Remove(cell);
        else
            PaintMap[cell] = value;
        SetFlagValue($"{PaintFlagPrefix}{cell.X}.{cell.Y}", value);
    }

    /// <summary>Paints a cell directly (tests, tools): tile colour, wall colour (1–12, 0 clears) and glow coating.</summary>
    public void PaintCell(GridPoint cell, int tileColor, int wallColor = 0, bool glow = false) =>
        SetPaint(cell, Math.Clamp(tileColor, 0, 12) | Math.Clamp(wallColor, 0, 12) << 4 | (glow ? 0x100 : 0));

    private void UsePaintTool(ItemDefinition item, GridPoint point)
    {
        if (!World.Contains(point) || !IsInReach(point))
        {
            PostMessage("That is out of reach.");
            return;
        }
        var cell = World.GetCell(point);
        PaintMap.TryGetValue(point, out var value);
        switch (item.Id)
        {
            case ItemId.PaintScraper:
                if (value == 0)
                    return;
                SetPaint(point, 0);
                PostMessage("Scraped the paint away.");
                break;
            case ItemId.GlowCoating:
                if (cell.TileId == TileId.Air)
                    return;
                if ((value & 0x100) != 0 || !Player.Inventory.TryConsume(ItemId.GlowCoating))
                    return;
                SetPaint(point, value | 0x100);
                break;
            default:
            {
                var roller = item.Id == ItemId.PaintRoller;
                if (roller ? cell.WallId == 0 : cell.TileId == TileId.Air)
                {
                    PostMessage(roller ? "The roller needs a background wall." : "The brush needs a block to paint.");
                    return;
                }
                var paintIndex = Array.FindIndex(LeisureContent.Paints, paint => Player.Inventory.Count(paint) > 0);
                if (paintIndex < 0)
                {
                    PostMessage("You have no paint.");
                    return;
                }
                var color = paintIndex + 1;
                var current = roller ? (value >> 4) & 0xF : value & 0xF;
                if (current == color || !Player.Inventory.TryConsume(LeisureContent.Paints[paintIndex]))
                    return;
                SetPaint(point, roller ? (value & ~0xF0) | color << 4 : (value & ~0xF) | color);
                break;
            }
        }
        SetPlayerCooldown(item.UseCooldownTicks);
    }
}
