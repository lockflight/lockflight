namespace Lockflight.Cli.Ci;

/// <summary>The evidence spellings of <see cref="ExistingChange"/>.</summary>
internal static class ExistingChangeNames
{
    public static string ToName(ExistingChange change) => change switch
    {
        ExistingChange.Rechecked => "changed-rechecked",
        ExistingChange.PassesOnBase => "changed-passes-on-base",
        ExistingChange.Uncheckable => "changed-uncheckable",
        ExistingChange.Kept => "changed-kept",
        ExistingChange.OldVersionFails => "changed-old-version-fails",
        ExistingChange.OldVersionUncheckable => "changed-old-version-uncheckable",
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown change."),
    };
}
