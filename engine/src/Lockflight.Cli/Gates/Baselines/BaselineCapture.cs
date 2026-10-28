using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Gates.Baselines;

/// <summary>
/// What taking a baseline found: the entries it would accept (build warning fingerprints, one per occurrence; failing test names;
/// advisory fingerprints) for the unit (null for the repository), or why it could not be taken and with which exit code.
/// </summary>
internal sealed record BaselineCapture(IReadOnlyList<string>? Entries, string? UnitPath, string? Problem, ExitCode FailureExit)
{
    public static BaselineCapture Taken(IReadOnlyList<string> entries, string? unitPath) => new(entries, unitPath, null, ExitCode.Pass);

    public static BaselineCapture Failed(string problem, ExitCode exitCode) => new(null, null, problem, exitCode);
}
