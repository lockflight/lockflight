namespace Lockflight.Cli.Ledger;

/// <summary>Contract spellings of <see cref="StopCacheResult"/> in the JSONL ledger.</summary>
internal static class StopCacheResultNames
{
    public const string None = "none";
    public const string Hit = "hit";
    public const string Miss = "miss";

    public static string ToName(StopCacheResult result) => result switch
    {
        StopCacheResult.None => None,
        StopCacheResult.Hit => Hit,
        StopCacheResult.Miss => Miss,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown Stop cache result."),
    };
}
