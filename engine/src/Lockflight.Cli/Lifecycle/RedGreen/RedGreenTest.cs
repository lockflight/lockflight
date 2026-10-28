namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// A test accepted in a red/green cycle, with the requirements its traits name. <see cref="AssertionFree"/> marks a test whose code
/// neither asserts nor throws: a warning carried into the trace, not a rejection. <see cref="Rows"/> is how many cases the red run
/// reported for the test (a theory's data rows); 0 for a test recorded before row counts.
/// </summary>
internal sealed record RedGreenTest(string Key, string MethodFilter, RedClass RedClass, IReadOnlyList<string> Requirements)
{
    /// <summary>How output and the trace name a test that asserts nothing.</summary>
    public const string AssertionFreeMark = "assertion-free";

    public bool AssertionFree { get; init; }

    public int Rows { get; init; }
}
