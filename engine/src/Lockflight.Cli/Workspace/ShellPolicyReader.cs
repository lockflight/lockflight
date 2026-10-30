using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Workspace;

/// <summary>
/// Reads a unit's policy from a clone of its workspace shell: the shell's <c>lockflight.yaml</c> at the clone's <c>HEAD</c>, read with
/// <c>git show</c> (the committed policy, never a file in the working tree), and the unit the pointer names among its units. A clone
/// given by hand must come from the shell the pointer names (its <c>origin</c>, compared without scheme, user and <c>.git</c>).
/// </summary>
internal static class ShellPolicyReader
{
    public static async Task<ShellPolicyReading> ReadAsync(string shellDirectory, ShellSettings pointer, bool checkOrigin, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(shellDirectory))
        {
            return Failed($"the workspace shell clone '{shellDirectory}' does not exist; clone it with `lockflight workspace clone --into <dir>`");
        }

        ProcessResult top = await ProcessRunner.RunAsync("git", ["rev-parse", "--show-cdup"], shellDirectory, cancellationToken);
        if (top.ExitCode != 0 || top.Output.Trim().Length > 0 || await Git.CommitOfAsync(shellDirectory, "HEAD", cancellationToken) is not string commit)
        {
            return Failed($"'{shellDirectory}' is not a git clone of the workspace shell {pointer.Url} with a commit");
        }

        if (checkOrigin && await Git.ConfigValueAsync(shellDirectory, "remote.origin.url", cancellationToken) is var origin && !SameRepository(origin, pointer.Url))
        {
            return Failed($"'{shellDirectory}' is a clone of {origin ?? "no origin"}, not of the workspace shell {pointer.Url} that {Manifest.FileName} points at");
        }

        ProcessResult show = await ProcessRunner.RunAsync("git", ["-c", "core.quotePath=false", "show", "--no-textconv", $"{commit}:{Manifest.FileName}"], shellDirectory, cancellationToken);
        if (show.ExitCode != 0)
        {
            return Failed($"the workspace shell {pointer.Url} has no {Manifest.FileName} at {CommitIds.Short(commit)}");
        }

        ManifestLoadResult parsed = new ManifestParser(shellDirectory, $"the workspace shell's {Manifest.FileName} at {CommitIds.Short(commit)}").Parse(show.Output);
        if (parsed.Manifest is not Manifest manifest)
        {
            return Failed($"the workspace shell's {Manifest.FileName} at {CommitIds.Short(commit)} is invalid: {parsed.Error?.Describe()}");
        }

        if (manifest.Units.FirstOrDefault(unit => unit.Path.Equals(pointer.Unit, StringComparison.OrdinalIgnoreCase)) is not Unit unit)
        {
            string listed = manifest.Units.Count == 0 ? "none" : string.Join(", ", manifest.Units.Select(listedUnit => listedUnit.Path));
            return Failed($"the workspace shell's {Manifest.FileName} at {CommitIds.Short(commit)} lists no unit '{pointer.Unit}' (its units: {listed}); fix shell.unit in this repository's {Manifest.FileName} or add the unit to the shell");
        }

        return new ShellPolicyReading(new ShellPolicy(shellDirectory, commit, manifest, unit), Error: null);
    }

    /// <summary>Whether two clone URLs name the same repository: host and path, without scheme, user, port separator form or <c>.git</c>.</summary>
    public static bool SameRepository(string? first, string second) =>
        first is not null && string.Equals(Canonical(first), Canonical(second), StringComparison.OrdinalIgnoreCase);

    private static string Canonical(string url)
    {
        string text = url.Trim();
        int scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            text = text[(scheme + 3)..];
        }
        else
        {
            int colon = text.IndexOf(':', StringComparison.Ordinal);
            text = colon >= 0 ? text[..colon] + "/" + text[(colon + 1)..] : text;
        }

        int at = text.IndexOf('@', StringComparison.Ordinal);
        text = at >= 0 && at < text.IndexOf('/', StringComparison.Ordinal) ? text[(at + 1)..] : text;
        text = text.TrimEnd('/');
        return text.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? text[..^4] : text;
    }

    private static ShellPolicyReading Failed(string error) => new(Policy: null, error);
}
