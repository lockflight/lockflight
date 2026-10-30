namespace Lockflight.Cli.Ci;

/// <summary>
/// The feature and spec a pull request is traced against: the feature id (null when none was found), how it was found
/// (<c>branch</c>, <c>specs-diff</c>, <c>single</c> or <c>none</c>), the spec's and tasks' text and path, where they were read, and
/// why the lookup cannot vouch for a missing spec (null when "no spec" is a fair answer).
/// </summary>
internal sealed record SpecResolution(string? FeatureId, string Source, string? SpecPath, string? SpecText, string? TasksText, SpecOrigin Origin, string? Problem)
{
    /// <summary>The workspace shell's clone URL (from the target branch's pointer) when the spec is the shell's; else null.</summary>
    public string? ShellUrl { get; init; }

    /// <summary>The shell commit the spec was read at when it is the shell's; else null.</summary>
    public string? ShellCommit { get; init; }

    public static SpecResolution None(string? problem) => new(null, "none", null, null, null, SpecOrigin.Head, problem);
}
