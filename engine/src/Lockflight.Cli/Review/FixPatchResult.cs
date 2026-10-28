namespace Lockflight.Cli.Review;

/// <summary>A fix read against the working tree: the fixed files, or, when it cannot be applied, the reason and no files.</summary>
internal sealed record FixPatchResult(IReadOnlyList<FixedFile> Files, string? Error)
{
    public static FixPatchResult Failed(string error) => new([], error);
}
