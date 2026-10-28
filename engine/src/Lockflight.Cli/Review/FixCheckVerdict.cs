namespace Lockflight.Cli.Review;

/// <summary>
/// One fix check: its result, the change's own tests the fix broke (<c>Class.Method</c>) or the own test files whose build it broke, and the bounded reason when the fix did not
/// apply or build. <see cref="OwnTestsChecked"/> counts the change's own tests that ran with the fix.
/// </summary>
internal sealed record FixCheckVerdict(FixCheckResult Result, IReadOnlyList<string> ConflictingTests, string? Errors)
{
    public int OwnTestsChecked { get; init; }
}
