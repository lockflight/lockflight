namespace Lockflight.Cli.Config;

/// <summary>
/// The manifest and evidence spellings of <see cref="ChangeClass"/>: <c>fix</c>, <c>behaviour</c>, <c>feature</c>, <c>critical</c>,
/// and <c>none</c> where a key allows switching a requirement off.
/// </summary>
internal static class ChangeClassNames
{
    public const string None = "none";

    public static bool TryParse(string? name, out ChangeClass changeClass)
    {
        ChangeClass? parsed = name switch
        {
            "fix" => ChangeClass.Fix,
            "behaviour" => ChangeClass.Behaviour,
            "feature" => ChangeClass.Feature,
            "critical" => ChangeClass.Critical,
            _ => null,
        };
        changeClass = parsed ?? default;
        return parsed is not null;
    }

    public static string ToName(ChangeClass changeClass) => changeClass switch
    {
        ChangeClass.Fix => "fix",
        ChangeClass.Behaviour => "behaviour",
        ChangeClass.Feature => "feature",
        ChangeClass.Critical => "critical",
        _ => throw new ArgumentOutOfRangeException(nameof(changeClass), changeClass, "Unknown change class."),
    };

    /// <summary>The class's name, or <c>none</c> for null (a requirement that never switches on).</summary>
    public static string ToName(ChangeClass? changeClass) => changeClass is ChangeClass known ? ToName(known) : None;

    /// <summary>The allowed spellings from <paramref name="lowest"/> up, with <c>none</c> last when it is allowed: "feature, critical or none".</summary>
    public static string Choices(ChangeClass lowest, bool allowNone)
    {
        string[] names = [.. Enum.GetValues<ChangeClass>().Where(changeClass => changeClass >= lowest).Select(changeClass => ToName(changeClass)), .. allowNone ? [None] : Array.Empty<string>()];
        return names.Length == 1 ? names[0] : $"{string.Join(", ", names[..^1])} or {names[^1]}";
    }
}
