namespace Lockflight.Cli.Testing;

/// <summary>
/// A whole-suite regression pass: the executed test cases, the keys of the listed tests the stop filters left out (never missing
/// nor skipped), the filters applied (null for none), and any <c>test.stopFilter</c> that could not be applied (not one trait condition).
/// </summary>
internal sealed record RegressionRun(IReadOnlyList<ExecutedTest> Results, IReadOnlySet<string> Excluded, RegressionFilter? Filter, IReadOnlyList<string> Unsupported);
