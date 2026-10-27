using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Review;

/// <summary>A build and run of reproductions: how it ended, the compiler errors when it did not compile, and the test results when it ran.</summary>
internal sealed record ReproductionBuildResult(ReproductionBuildStatus Status, IReadOnlyList<CompileError> Errors, IReadOnlyList<ExecutedTest> Results);
