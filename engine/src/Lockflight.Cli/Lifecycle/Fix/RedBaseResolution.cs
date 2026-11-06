namespace Lockflight.Cli.Lifecycle.Fix;

/// <summary>
/// The target branch a red is compared with: the ref used and its commit, a note when the choice needs saying (a local branch that
/// differs from the remote one), or why there is none.
/// </summary>
internal sealed record RedBaseResolution(string? Reference, string? Commit, string? Note, string? Error)
{
    public static RedBaseResolution Resolved(string reference, string commit, string? note) => new(reference, commit, note, null);

    public static RedBaseResolution Failed(string error) => new(null, null, null, error);
}
