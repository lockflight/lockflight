namespace Lockflight.Cli.Processes;

/// <summary>
/// The outcome of <see cref="ExecutableHash.CopyAndVerifyAsync"/>: the private copy to run (<see cref="Path"/>), or why there is
/// none, either because the binary is not the one installed (<see cref="ReplacedWhy"/>) or because it could not be copied and read
/// (<see cref="UnreadableWhy"/>).
/// </summary>
internal sealed record VerifiedExecutable(string? Path, string? ReplacedWhy, string? UnreadableWhy)
{
    public static VerifiedExecutable Verified(string path) => new(path, null, null);

    public static VerifiedExecutable Replaced(string why) => new(null, why, null);

    public static VerifiedExecutable Unreadable(string why) => new(null, null, why);
}
