using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>What is heating, cooling, or stabilising one room this interval.</summary>
public readonly record struct RoomHeatSources(int LavaCells, int Refineries, int Forges, int OxygenUnits, float WaterDepth);

/// <summary>Comfort verdict for one actor's ambient temperature.</summary>
public readonly record struct ThermalComfort(float Ambient, float DegreesOutsideComfort, float DegreesOutsideSafe, bool TooCold)
{
    public bool Comfortable => DegreesOutsideComfort <= 0f;
    public bool Dangerous => DegreesOutsideSafe > 0f;
}

/// <summary>
/// Outdoor temperature from biome, depth, and time of day, plus per-room trackers that drift
/// toward outdoors at a rate set by size and roofing while heat sources push them around.
/// This is the fluid/environment link: lava warms rooms, standing water cools them.
/// All numbers are Rimtera tuning.
/// </summary>
public sealed class TemperatureModel
{
    public const float ComfortMinimum = 10f;
    public const float ComfortMaximum = 30f;
    public const float SafeMinimum = -8f;
    public const float SafeMaximum = 46f;
    /// <summary>How far one piece of cold/heat gear widens the comfortable and safe bands.</summary>
    public const float GearAllowance = 25f;
    public const float OxygenUnitTarget = 21f;

    /// <summary>Storyteller-driven offset (cold snap / heat wave), applied to outdoor air.</summary>
    public float OutdoorOffset { get; set; }

    public static float BiomeBaseline(BiomeZone zone) => zone switch
    {
        BiomeZone.FrozenWasteland => -16f,
        BiomeZone.JunkWasteland => 21f,
        BiomeZone.RadiatedDeeps => 29f,
        BiomeZone.MoltenChasm => 49f,
        BiomeZone.Caverns => 12f,
        _ => 18f
    };

    /// <summary>Half the day/night swing for a biome; the Junk Wasteland bakes by day and freezes by night.</summary>
    public static float DailySwing(BiomeZone zone) => zone switch
    {
        BiomeZone.FrozenWasteland => 5f,
        BiomeZone.JunkWasteland => 19f,
        BiomeZone.Grassland => 6f,
        _ => 1f
    };

    public float OutdoorAt(BiomeZone zone, bool underground, float clockHour)
    {
        // Warmest mid-afternoon, coldest before dawn.
        var sun = MathF.Cos((clockHour - 14f) / 24f * MathF.Tau);
        var value = BiomeBaseline(zone) + DailySwing(zone) * sun * (underground ? 0.15f : 1f);
        // Below the crust the surface biomes fade toward steady cave air.
        if (underground && zone is BiomeZone.Grassland or BiomeZone.FrozenWasteland or BiomeZone.JunkWasteland)
            value = (value + BiomeBaseline(BiomeZone.Caverns)) * 0.5f;
        return value + OutdoorOffset * (underground ? 0.5f : 1f);
    }

    /// <summary>Extra warmth from lava within a few cells of an outdoor position.</summary>
    public static float LavaWarmth(TileGrid world, GridPoint cell, int radius = 3)
    {
        var lava = 0;
        for (var y = cell.Y - radius; y <= cell.Y + radius; y++)
        for (var x = cell.X - radius; x <= cell.X + radius; x++)
        {
            var point = new GridPoint(x, y);
            if (world.Contains(point) && world.GetCell(point) is { LiquidKind: LiquidKind.Lava, LiquidDepth: > 0.1f })
                lava++;
        }
        return Math.Min(40f, lava * 4f);
    }

    public static RoomHeatSources CountSources(RoomInfo room, TileGrid world, TileCatalog tiles)
    {
        var width = world.Width;
        var lava = 0;
        var oxygen = 0;
        var water = 0f;
        var seenSolid = new HashSet<int>();
        int refineries = 0, forges = 0;
        foreach (var index in room.CellIndices)
        {
            var x = index % width;
            var y = index / width;
            var cell = world.GetCell(new GridPoint(x, y));
            if (cell.LiquidKind == LiquidKind.Lava && cell.LiquidDepth > 0.1f)
                lava++;
            else if (cell.LiquidKind == LiquidKind.Water)
                water += cell.LiquidDepth;
            if (cell.TileId == TileId.OxygenUnit)
                oxygen++;

            // Stations are solid, so they sit in the room's boundary.
            foreach (var (dx, dy) in Neighbours)
            {
                var point = new GridPoint(x + dx, y + dy);
                if (!world.Contains(point) || !seenSolid.Add(point.Y * width + point.X))
                    continue;
                var tile = world.GetCell(point).TileId;
                if (tile == TileId.Furnace)
                    refineries++;
                else if (tile == TileId.Anvil)
                    forges++;
            }
        }
        return new RoomHeatSources(lava, refineries, forges, oxygen, water);
    }

    private static readonly (int, int)[] Neighbours = { (-1, 0), (1, 0), (0, -1), (0, 1) };

    /// <summary>Advances one room's temperature by <paramref name="elapsedHours"/>.</summary>
    public static float StepRoom(float current, float outdoor, RoomInfo room, RoomHeatSources sources,
        bool deepUnderground, float elapsedHours)
    {
        if (float.IsNaN(current))
            current = outdoor;
        var cells = Math.Max(1, room.CellCount);
        // Leak rates per hour: open-roof cells leak fast, sheltered cells slowly, and rock deep
        // underground holds heat the longest. Small rooms swing faster than large ones.
        var openFraction = room.OpenRoofFraction;
        var shelteredFraction = Math.Min(1f, room.ShelteredCells / (float)cells);
        var leak = (deepUnderground ? 0.04f : 0.1f) + 0.9f * openFraction + 0.15f * (1f - shelteredFraction) + 2f / (cells + 10f);

        var push = sources.LavaCells * 90f + sources.Refineries * 260f + sources.Forges * 140f - sources.WaterDepth * 35f;
        var heating = push / Math.Max(cells, 6);
        var stabiliser = sources.OxygenUnits > 0
            ? (OxygenUnitTarget - current) * Math.Min(3f, 0.9f * sources.OxygenUnits)
            : 0f;

        var drift = (outdoor - current) * Math.Min(1f, leak * elapsedHours);
        var next = current + drift + (heating + stabiliser) * elapsedHours;
        return Math.Clamp(next, -80f, 150f);
    }

    /// <summary>Comfort and danger margins, widened by "cold"/"heat" immunity gear.</summary>
    public static ThermalComfort Evaluate(float ambient, IReadOnlySet<string>? immunityTags)
    {
        var coldGear = immunityTags?.Contains("cold") == true ? GearAllowance : 0f;
        var heatGear = immunityTags?.Contains("heat") == true ? GearAllowance : 0f;
        var comfortLow = ComfortMinimum - coldGear;
        var comfortHigh = ComfortMaximum + heatGear;
        var safeLow = SafeMinimum - coldGear;
        var safeHigh = SafeMaximum + heatGear;
        var tooCold = ambient < comfortLow;
        var outsideComfort = tooCold ? comfortLow - ambient : Math.Max(0f, ambient - comfortHigh);
        var outsideSafe = ambient < safeLow ? safeLow - ambient : Math.Max(0f, ambient - safeHigh);
        return new ThermalComfort(ambient, outsideComfort, outsideSafe, tooCold);
    }
}
