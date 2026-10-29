namespace Lockflight.Cli.Testing;

/// <summary>
/// A runner's run of an app's tests: its report, or none when the runner wrote no report or its time ran out before the last
/// method batch, which never counts as tests that passed.
/// </summary>
internal sealed record TestRunOutcome(bool Reported, TestRunReport Report)
{
    public static TestRunOutcome NoReport { get; } = new(false, new TestRunReport([], AbnormalExitCode: null));

    public static TestRunOutcome Of(TestRunReport? report) => report is null ? NoReport : new(true, report);
}
