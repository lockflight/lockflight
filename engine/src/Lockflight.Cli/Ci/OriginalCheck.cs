namespace Lockflight.Cli.Ci;

/// <summary>
/// One amended test's red-commit version run on the head's code: whether it passed every row there (null when it could not be
/// built, matched or run), and why.
/// </summary>
internal sealed record OriginalCheck(string Key, bool? Passed, string Why)
{
    /// <summary>How many rows of the red commit's version ran on the head's code; 0 when it did not run.</summary>
    public int Rows { get; init; }

    /// <summary>The red commit's version's assertion count (<see cref="TestFingerprinter.Assertions"/>); null when unknown.</summary>
    public int? Assertions { get; init; }
}
