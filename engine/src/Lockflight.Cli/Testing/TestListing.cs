namespace Lockflight.Cli.Testing;

/// <summary>
/// A runner's listing of an app's tests: whether it could list them, the tests, and the mode that worked. A runner that cannot list
/// an app may say why in <see cref="Problem"/> (what the test project needs), for the error message when no runner could.
/// </summary>
internal sealed record TestListing(bool Listed, IReadOnlyList<TestCase> Tests, RunnerMode Mode)
{
    public static TestListing NotListed { get; } = new(false, [], RunnerMode.Unknown);

    public string? Problem { get; init; }

    public static TestListing Of(IReadOnlyList<TestCase> tests, RunnerMode mode) => new(true, tests, mode);

    public static TestListing Unavailable(string problem) => NotListed with { Problem = problem };
}
