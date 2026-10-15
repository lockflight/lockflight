using System.Text;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Proposals;

/// <summary>
/// Makes a git unified diff for one proposal with <c>git diff --no-index</c> over a private staging copy (<c>a/&lt;path&gt;</c>, the
/// file now; <c>b/&lt;path&gt;</c>, the proposal), so the headers read <c>a/&lt;path&gt;</c> <c>b/&lt;path&gt;</c> relative to the project
/// root and <c>git apply</c> takes it there. Bytes go through untouched: line-ending conversion is switched off, the diff is
/// written to a file rather than decoded, and the person's diff settings (external drivers, colour, text conversion, blank-line
/// suppression) are overridden. A new file comes from <c>/dev/null</c> and a deletion goes to it, which git marks as such.
/// </summary>
internal static class PatchBuilder
{
    private const string Executable = "git";
    private const string Before = "a";
    private const string After = "b";
    private const string NullDevice = "/dev/null";
    private const string PatchFileName = "proposal.patch";

    /// <summary><c>git diff --no-index</c> exits 1 when the files differ, which is the expected outcome here.</summary>
    private const int DifferencesFound = 1;

    private static readonly string[] Settings =
    [
        "-c", "core.autocrlf=false",
        "-c", "core.safecrlf=false",
        "-c", "core.quotePath=false",
        "-c", "diff.suppressBlankEmpty=false",
    ];

    private static readonly string[] DiffOptions = ["diff", "--no-index", "--no-color", "--no-ext-diff", "--no-textconv"];

    private static readonly string[] Prefixes = [$"--src-prefix={Before}/", $"--dst-prefix={After}/"];

    public static async Task<PatchBuild> BuildAsync(ProposalTarget target, CancellationToken cancellationToken)
    {
        string staging = Path.Combine(Path.GetTempPath(), $"lockflight-propose-{Guid.CreateVersion7()}");
        try
        {
            Stage(Path.Combine(staging, Before), target.RelativePath, target.OldContent, target.Mode);
            Stage(Path.Combine(staging, After), target.RelativePath, target.NewContent, target.Mode);
            string output = Path.Combine(staging, PatchFileName);
            (string directory, string[] paths) = Operands(staging, target);
            ProcessResult diff = await ProcessRunner.RunAsync(
                Executable, [.. Settings, .. DiffOptions, $"--output={output}", .. paths], directory, cancellationToken);
            if (diff.ExitCode != DifferencesFound || !File.Exists(output))
            {
                return PatchBuild.Failed($"git diff exited {diff.ExitCode}: {diff.Combined.Trim()}");
            }

            byte[] patch = await File.ReadAllBytesAsync(output, cancellationToken);
            return Encoding.UTF8.GetString(patch).Contains("\nBinary files ", StringComparison.Ordinal)
                ? PatchBuild.Failed("git treats the file as binary, which a text patch cannot carry")
                : PatchBuild.Built(patch);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    /// <summary>
    /// Where git runs and what it compares. Both sides present: the two staged trees, without prefixes, since their names already
    /// start with a/ and b/. One side missing: that side is the null device and the prefixes are added to the other's relative
    /// path, run from inside its tree.
    /// </summary>
    private static (string Directory, string[] Paths) Operands(string staging, ProposalTarget target) => target.Change switch
    {
        ProposalChange.Added => (Path.Combine(staging, After), [.. Prefixes, NullDevice, target.RelativePath]),
        ProposalChange.Deleted => (Path.Combine(staging, Before), [.. Prefixes, target.RelativePath, NullDevice]),
        _ => (staging, ["--no-prefix", $"{Before}/{target.RelativePath}", $"{After}/{target.RelativePath}"]),
    };

    private static void Stage(string tree, string relativePath, byte[]? content, UnixFileMode? mode)
    {
        Directory.CreateDirectory(tree);
        if (content is null)
        {
            return;
        }

        string path = Path.Combine(tree, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        if (mode is UnixFileMode unixMode && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, unixMode);
        }
    }
}
