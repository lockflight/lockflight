namespace Lockflight.Cli.Gates.Baselines;

/// <summary>The command-line names of <see cref="BaselineKind"/> (<c>build</c>, <c>tests</c>, <c>deps</c>, <c>suppressions</c>) and what each accepts.</summary>
internal static class BaselineKindNames
{
    public static string ToName(BaselineKind kind) => kind switch
    {
        BaselineKind.Build => "build",
        BaselineKind.Tests => "tests",
        BaselineKind.Dependencies => "deps",
        BaselineKind.Suppressions => "suppressions",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static bool TryParse(string name, out BaselineKind kind)
    {
        kind = name switch
        {
            "build" => BaselineKind.Build,
            "tests" => BaselineKind.Tests,
            "deps" => BaselineKind.Dependencies,
            "suppressions" => BaselineKind.Suppressions,
            _ => 0,
        };
        return kind != 0;
    }

    /// <summary>What one accepted entry is, singular and plural (<c>build warning</c>, <c>build warnings</c>).</summary>
    public static string Entries(BaselineKind kind, int count) => (kind, count == 1) switch
    {
        (BaselineKind.Build, true) => "build warning",
        (BaselineKind.Build, false) => "build warnings",
        (BaselineKind.Tests, true) => "failing test",
        (BaselineKind.Tests, false) => "failing tests",
        (BaselineKind.Suppressions, true) => "suppression",
        (BaselineKind.Suppressions, false) => "suppressions",
        (_, true) => "advisory",
        _ => "advisories",
    };
}
