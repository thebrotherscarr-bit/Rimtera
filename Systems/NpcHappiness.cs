namespace Rimtera.Systems;

public static class NpcHappiness
{
    public static float PriceMultiplier(IEnumerable<string> biomeTags, int nearbyNpcCount)
    {
        var multiplier = 1f;
        var tags = new HashSet<string>(biomeTags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (tags.Contains("snow")) multiplier *= 0.94f;
        if (tags.Contains("desert")) multiplier *= 1.06f;
        if (tags.Contains("jungle")) multiplier *= 0.94f;
        if (nearbyNpcCount > 3)
            multiplier *= 1f + 0.05f * (nearbyNpcCount - 3);
        return Math.Clamp(multiplier, 0.75f, 1.5f);
    }
}