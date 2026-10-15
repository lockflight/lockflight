namespace Lockflight.Cli.Testing;

/// <summary>
/// What one test application's run reported: the executed test cases from its xUnit XML and, when the runner's exit code is neither
/// "all passed" nor "tests failed" (a crash, an invalid option, a timeout), that exit code.
/// </summary>
internal sealed record TestRunReport(IReadOnlyList<ExecutedTest> Results, int? AbnormalExitCode);
