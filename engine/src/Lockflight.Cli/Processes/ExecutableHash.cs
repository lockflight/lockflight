using System.Security.Cryptography;

namespace Lockflight.Cli.Processes;

/// <summary>
/// Checks a binary against the SHA-256 it was installed with, just before it runs, so a tool swapped after its install (by a
/// build the engine started, in the same job) is refused rather than trusted. The binary is copied into a private directory
/// first and the copy is hashed and run, so the file checked is the file started: replacing the installed binary after the
/// check changes nothing. A guardrail still: a process of the same user could write to the copy between the hash and the start.
/// </summary>
internal static class ExecutableHash
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// Copies <paramref name="path"/> into <paramref name="privateDirectory"/> (a directory only the current user can open; the
    /// caller deletes it) with owner-only permissions, and checks the copy against <paramref name="expectedSha256"/> (hex, any case).
    /// </summary>
    public static async Task<VerifiedExecutable> CopyAndVerifyAsync(string path, string expectedSha256, DirectoryInfo privateDirectory, CancellationToken cancellationToken)
    {
        string copy = Path.Combine(privateDirectory.FullName, Path.GetFileName(path));
        string actual;
        try
        {
            await using (FileStream source = File.OpenRead(path))
            await using (FileStream target = CreateOwnerOnly(copy))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            await using FileStream binary = File.OpenRead(copy);
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(binary, cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return VerifiedExecutable.Unreadable($"{path} could not be copied and read to check its SHA-256: {exception.Message}");
        }

        return actual.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase)
            ? VerifiedExecutable.Verified(copy)
            : VerifiedExecutable.Replaced($"{path} has SHA-256 {actual}, not the {expectedSha256.Trim()} it was installed with; something replaced it after the install");
    }

    private static FileStream CreateOwnerOnly(string path)
    {
        FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnly;
        }

        return new FileStream(path, options);
    }
}
