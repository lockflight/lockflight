namespace Lockflight.Cli.Updating;

/// <summary>The outcome of updating the user install from a package feed.</summary>
/// <param name="Version">The engine version now installed (or found on the feed); null when the update failed before knowing it.</param>
/// <param name="Problem">What went wrong, for <see cref="PackageUpdateStatus.Failed"/>; null otherwise.</param>
internal sealed record PackageUpdateResult(PackageUpdateStatus Status, string? Version, string? Problem)
{
    public static PackageUpdateResult Failure(string problem) => new(PackageUpdateStatus.Failed, null, problem);
}
