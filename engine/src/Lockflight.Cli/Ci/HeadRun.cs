using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Step P1: the head built, listed and its whole suite run once (<see cref="Results"/>, null when the runner gave no report), with the
/// tests that failed and then passed on a re-run (<see cref="PassedOnRetry"/>), the runner's abnormal exit, and the known-failing
/// tests of the base's baselines.
/// </summary>
internal sealed record HeadRun(TreeListing Listing, IReadOnlyList<ExecutedTest>? Results, IReadOnlySet<string> PassedOnRetry, string? AbnormalExit, IReadOnlySet<string> KnownFailing);
