namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>What the built test assemblies say about a red's new tests, by test key: which call production code and which assert nothing.</summary>
internal sealed record NewTestScan(IReadOnlySet<string> CallsProduction, IReadOnlySet<string> AssertionFree);
