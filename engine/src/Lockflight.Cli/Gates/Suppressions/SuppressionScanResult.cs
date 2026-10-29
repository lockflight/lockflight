namespace Lockflight.Cli.Gates.Suppressions;

/// <summary>
/// The suppressions a change adds, in file and line order, and how many changed files could hold one; or, when git could not say
/// what changed, why (<see cref="Found"/> null).
/// </summary>
internal sealed record SuppressionScanResult(IReadOnlyList<Suppression>? Found, int FilesChecked, string? Problem)
{
    public static SuppressionScanResult Failed(string problem) => new(null, 0, problem);
}
