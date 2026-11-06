using System.Security.Cryptography;

namespace Lockflight.Cli.Processes;

/// <summary>
/// Checks a binary against the SHA-256 it was installed with, just before it runs, so a tool swapped after its install (by a
/// build the engine started, in the same job) is refused rather than trusted. A guardrail: the check and the start are two
/// steps, and a process running beside the engine could still swap the file between them.
/// </summary>
internal static class ExecutableHash
{
    /// <summary>Null when <paramref name="path"/> has the SHA-256 <paramref name="expectedSha256"/> (hex, any case); else why not.</summary>
    public static async Task<string?> MismatchAsync(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        string actual;
        try
        {
            await using FileStream binary = File.OpenRead(path);
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(binary, cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"{path} could not be read to check its SHA-256: {exception.Message}";
        }

        return actual.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{path} has SHA-256 {actual}, not the {expectedSha256.Trim()} it was installed with; something replaced it after the install";
    }
}
