namespace Lockflight.Cli.Testing;

/// <summary>A runner's listing of an app's tests: whether it could list them, the tests, and the mode that worked.</summary>
internal sealed record TestListing(bool Listed, IReadOnlyList<TestCase> Tests, RunnerMode Mode)
{
    public static TestListing NotListed { get; } = new(false, [], RunnerMode.Unknown);

    public static TestListing Of(IReadOnlyList<TestCase> tests, RunnerMode mode) => new(true, tests, mode);
}
