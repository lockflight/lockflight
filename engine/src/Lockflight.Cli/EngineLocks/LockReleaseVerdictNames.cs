namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// The name <c>engine verify-lock</c> prints for each <see cref="LockReleaseVerdict"/>, so a failed verification says which check
/// failed in a word a reader (or a script) can match, ahead of the line that explains it.
/// </summary>
internal static class LockReleaseVerdictNames
{
    public static string ToName(LockReleaseVerdict verdict) => verdict switch
    {
        LockReleaseVerdict.Matches => "matches",
        LockReleaseVerdict.Differs => "hash-mismatch",
        LockReleaseVerdict.NoRelease => "no-release",
        LockReleaseVerdict.Unreadable => "unreadable-lock",
        LockReleaseVerdict.Unattested => "unattested",
        LockReleaseVerdict.ScriptDiffers => "script-hash-mismatch",
        LockReleaseVerdict.ScriptUnpublished => "script-hash-unpublished",
        LockReleaseVerdict.VendoredScriptDiffers => "vendored-script-mismatch",
        LockReleaseVerdict.VendoredScriptMissing => "vendored-script-missing",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unknown lock verdict."),
    };
}
