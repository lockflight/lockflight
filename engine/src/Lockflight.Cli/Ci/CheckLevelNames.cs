namespace Lockflight.Cli.Ci;

/// <summary>The contract spellings of <see cref="CheckLevel"/>: <c>off</c>, <c>required</c>, <c>advisory</c>.</summary>
internal static class CheckLevelNames
{
    public static string ToName(CheckLevel level) => level switch
    {
        CheckLevel.Off => "off",
        CheckLevel.Required => "required",
        CheckLevel.Advisory => "advisory",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown check level."),
    };

    public static CheckLevel? Parse(string name) => name switch
    {
        "off" => CheckLevel.Off,
        "required" => CheckLevel.Required,
        "advisory" => CheckLevel.Advisory,
        _ => null,
    };
}
