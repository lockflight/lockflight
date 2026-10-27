namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// What the built test assemblies say about a red's new tests, by test key: the first production member each reaches (none for a
/// test that reaches no production code) and which assert nothing.
/// </summary>
internal sealed record NewTestScan(IReadOnlyDictionary<string, string> ProductionCalls, IReadOnlySet<string> AssertionFree);
