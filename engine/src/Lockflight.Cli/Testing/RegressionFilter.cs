namespace Lockflight.Cli.Testing;

/// <summary>
/// What a red/green regression pass left out, for its evidence: the <c>test.stopFilter</c> values it applied, how many listed
/// tests they excluded and which (by key; null in a record written before the keys were kept). The pr gate and the CI judge run
/// every test.
/// </summary>
internal sealed record RegressionFilter(IReadOnlyList<string> Filters, int Excluded)
{
    public IReadOnlyList<string>? ExcludedTests { get; init; }

    /// <summary><c>test.stopFilter 'Category!=Integration' left out 3 tests</c>, as status and the steps print it.</summary>
    public string Describe() =>
        $"test.stopFilter {string.Join(", ", Filters.Select(filter => $"'{filter}'"))} left out {Excluded} {(Excluded == 1 ? "test" : "tests")} (the pr gate and CI run them)";
}
