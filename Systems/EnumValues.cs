namespace Rimtera.Systems;

/// <summary>
/// An enum's values, built once (0.8.2, the performance pass). <see cref="Enum.GetValues{TEnum}"/> returns a new array on
/// every call; the loops that run each tick (gear slots for every creature's aggro check, for one) iterate this instead.
/// Read-only by convention: never write into it.
/// </summary>
public static class EnumValues<T> where T : struct, Enum
{
    public static readonly T[] All = Enum.GetValues<T>();
}
