namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>Contract names of <see cref="RedClass"/> as they appear in evidence files and output.</summary>
internal static class RedClassNames
{
    public static string ToName(RedClass redClass) => redClass switch
    {
        RedClass.RedStub => "RED_STUB",
        RedClass.RedAssert => "RED_ASSERT",
        RedClass.RedOther => "RED_OTHER",
        RedClass.RedInTest => "RED_IN_TEST",
        RedClass.GreenBefore => "GREEN_BEFORE",
        RedClass.Skipped => "SKIPPED",
        RedClass.Flaky => "FLAKY",
        _ => throw new ArgumentOutOfRangeException(nameof(redClass), redClass, null),
    };

    public static RedClass Parse(string name) =>
        Enum.GetValues<RedClass>().FirstOrDefault(value => ToName(value) == name) is RedClass parsed and not 0
            ? parsed
            : throw new FormatException($"unknown red class '{name}'");

    public static bool IsAccepted(RedClass redClass) => redClass is RedClass.RedStub or RedClass.RedAssert or RedClass.RedOther;
}
