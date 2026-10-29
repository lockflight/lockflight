namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>
/// A blocked report as read: the <see cref="Report"/> when its required parts are there (null when not), and every
/// <see cref="Problems"/> a validation names — missing or mistyped properties, values this engine does not know, a command where
/// none may be, options not ordered recommended first.
/// </summary>
internal sealed record BlockedReading(BlockedReport? Report, IReadOnlyList<string> Problems)
{
    public bool Valid => Report is not null && Problems.Count == 0;
}
