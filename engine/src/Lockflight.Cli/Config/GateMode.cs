namespace Lockflight.Cli.Config;

/// <summary>
/// How a gate step that a project switches in <c>lockflight.yaml</c> judges (<c>gates.quality.suppressions</c>): whether its
/// findings can fail the run, are only reported, or the step does not run.
/// </summary>
internal enum GateMode
{
    /// <summary>The step judges and can FAIL the run (<c>fail</c>).</summary>
    Fail = 1,

    /// <summary>The step runs and lists its findings, but a FAIL counts as PASS; an ERROR stays an ERROR (<c>report</c>).</summary>
    Report = 2,

    /// <summary>The step does not run; the result lists it as off (<c>off</c>).</summary>
    Off = 3,

    /// <summary>
    /// Only for <c>gates.ef.pendingModelChanges</c>: judge as <see cref="Fail"/> when the repository pins <c>dotnet-ef</c> in a tool
    /// manifest, and do not run (N/A) otherwise (<c>auto</c>).
    /// </summary>
    Auto = 4,
}
