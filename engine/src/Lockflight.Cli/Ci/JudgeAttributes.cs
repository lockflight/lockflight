using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Keeps the pull request's own <c>.gitattributes</c> from deciding what git shows the judge. Git reads attributes from the
/// checked-out tree, which in CI is the pull request's head, so a line such as <c>*.cs -diff</c> would turn source changes into
/// "Binary files differ" for routing, the gates, gitleaks' history scan and the spec diff. In a CI clone the judge adds
/// <c>* !diff</c> to the repository's <c>info/attributes</c>, which git reads before any <c>.gitattributes</c>: every file is
/// then told text from binary by its content, as git does with no attribute. A local run never touches the person's repository.
/// </summary>
internal static class JudgeAttributes
{
    public const string Line = "* !diff";

    private const string Executable = "git";

    /// <summary>Adds <see cref="Line"/> to the clone's <c>info/attributes</c> once; an error message when git or the file fails.</summary>
    public static async Task<string?> NeutraliseAsync(string root, CancellationToken cancellationToken)
    {
        ProcessResult common = await ProcessRunner.RunAsync(Executable, ["rev-parse", "--path-format=absolute", "--git-common-dir"], root, cancellationToken);
        if (common.ExitCode != 0 || common.Output.Trim() is not { Length: > 0 } gitDirectory)
        {
            return "git could not name the clone's git directory to set the judge's attributes";
        }

        string path = Path.Combine(gitDirectory, "info", "attributes");
        try
        {
            string existing = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : string.Empty;
            if (existing.Split('\n').Any(line => line.Trim() == Line))
            {
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string separator = existing.Length == 0 || existing.EndsWith('\n') ? string.Empty : "\n";
            await File.WriteAllTextAsync(path, existing + separator + Line + "\n", cancellationToken);
            return null;
        }
        catch (IOException exception)
        {
            return $"the judge could not write {path}: {exception.Message}";
        }
    }
}
