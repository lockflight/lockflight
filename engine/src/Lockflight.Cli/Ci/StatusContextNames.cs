namespace Lockflight.Cli.Ci;

/// <summary>The status context names a platform shows, and which checks each context judges.</summary>
internal static class StatusContextNames
{
    public static string ToName(StatusContext context) => context switch
    {
        StatusContext.Gates => "gates",
        StatusContext.Judge => "lockflight/judge",
        _ => throw new ArgumentOutOfRangeException(nameof(context), context, "Unknown status context."),
    };

    public static StatusContext? Parse(string name) => name switch
    {
        "gates" => StatusContext.Gates,
        "lockflight/judge" => StatusContext.Judge,
        _ => null,
    };

    /// <summary>The context whose verdict <paramref name="check"/> counts toward.</summary>
    public static StatusContext Of(CheckId check) => check is CheckId.Gates or CheckId.Green ? StatusContext.Gates : StatusContext.Judge;
}
