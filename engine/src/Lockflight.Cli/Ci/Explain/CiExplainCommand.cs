using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Ci.Explain;

/// <summary>
/// <c>lockflight ci explain [&lt;file | folder | run URL | run id&gt;] [--json]</c>: what each check that did not pass means and
/// what to do about it. It reads the judge's evidence, the report's verdict, the action's refusal or a gate result as data
/// (<see cref="CiExplainer"/>), with the reason code's Next line and its entry in the reasons guide beside the check's detail.
/// Without an operand it reads the local judge's last evidence; a GitHub run is downloaded with <c>gh run download</c>. Exit 0
/// when the file was explained, whatever its verdict; 4 when there is nothing readable to explain; 3 when <c>gh</c> failed.
/// </summary>
internal sealed class CiExplainCommand(CliContext context) : ICommand
{
    public const string JsonFlag = "--json";

    private const string Command = "ci explain";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        string[] operands = [.. arguments.Rest.Where(argument => !argument.StartsWith('-'))];
        string[] optionArguments = [.. arguments.Rest.Where(argument => argument.StartsWith('-'))];
        if (await CommandOptions.ParseOrReportAsync(Command, optionArguments, [], [JsonFlag], context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        if (operands.Length > 1)
        {
            await context.Error.WriteLineAsync($"lockflight {Command}: name one file, folder, run URL or run id, not {operands.Length}");
            return ExitCode.Config;
        }

        DirectoryInfo? download = null;
        try
        {
            ExplainSources sources;
            if (operands.Length == 0)
            {
                sources = await LocalAsync(cancellationToken);
            }
            else if (GitHubRun.Parse(operands[0]) is not { Repository: not null } && Path.GetFullPath(operands[0], context.WorkingDirectory) is string path
                && (File.Exists(path) || Directory.Exists(path)))
            {
                sources = File.Exists(path) ? new([(path, operands[0])], null) : Folder(path, operands[0]);
            }
            else if (GitHubRun.Parse(operands[0]) is GitHubRun run)
            {
                download = Directory.CreateTempSubdirectory("lockflight-explain-");
                if (await GitHubRunArtifacts.DownloadAsync(context.Processes, run, download.FullName, context.WorkingDirectory, cancellationToken) is string failed)
                {
                    await context.Error.WriteLineAsync($"lockflight {Command}: {failed}");
                    return ExitCode.Error;
                }

                sources = Folder(download.FullName, $"run {run.Id}");
            }
            else
            {
                sources = new([], $"'{TerminalText.Clean(operands[0])}' is not a file or folder here, nor a GitHub Actions run URL or id");
            }

            if (sources.Problem is string problem)
            {
                await context.Error.WriteLineAsync($"lockflight {Command}: {problem}");
                return ExitCode.Config;
            }

            List<CiExplanation> explanations = [];
            foreach ((string file, string shown) in sources.Files)
            {
                CiArtifactReading reading = CiArtifactReader.Read(file);
                CiExplanationReading explained = reading.Document is { } document
                    ? CiExplainer.Explain(document, shown)
                    : new CiExplanationReading(null, reading.Problem ?? "it is not there");
                if (explained.Explanation is not CiExplanation explanation)
                {
                    await context.Error.WriteLineAsync($"lockflight {Command}: {shown} was not explained: {explained.Problem}");
                    return ExitCode.Config;
                }

                explanations.Add(explanation);
            }

            await context.Output.WriteAsync(options.Flag(JsonFlag)
                ? JsonFiles.Format(CiExplanationJson.Render(explanations))
                : string.Join("\n", explanations.Select(CiExplanationText.Render)));
            return ExitCode.Pass;
        }
        finally
        {
            download?.Delete(recursive: true);
        }
    }

    /// <summary>The local judge's last evidence (<c>lockflight ci --platform local</c>) in this repository.</summary>
    private async Task<ExplainSources> LocalAsync(CancellationToken cancellationToken)
    {
        string root = await RootAsync(cancellationToken);
        string relative = $"{JudgeInputsReader.LocalEvidenceDirectory}/{CiEvidence.LocalFileName}";
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path)
            ? new([(path, relative)], null)
            : new([], $"there is no local judge evidence ({relative}): run 'lockflight ci --platform local' first, or name a file, folder, run URL or run id");
    }

    private static ExplainSources Folder(string directory, string shown)
    {
        IReadOnlyList<string> files = ExplainedFiles.Find(directory);
        return files.Count == 0
            ? new([], $"{shown} holds no {CiReportCommand.VerdictFileName}, {CiEvidence.FileName}, {CiReportCommand.RefusalFileName} or {CiEvidence.LocalFileName}")
            : new([.. files.Select(file => (file, $"{shown}/{Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/')}"))], null);
    }

    private async Task<string> RootAsync(CancellationToken cancellationToken)
    {
        try
        {
            ProcessResult top = await context.Processes("git", ["rev-parse", "--show-toplevel"], context.WorkingDirectory, cancellationToken);
            return top.ExitCode == 0 && top.Output.Trim() is { Length: > 0 } root ? Path.GetFullPath(root) : context.WorkingDirectory;
        }
        catch (ExecutableNotFoundException)
        {
            return context.WorkingDirectory;
        }
    }

    /// <summary>The files to explain, each with the name shown for it, or why there are none.</summary>
    private sealed record ExplainSources(IReadOnlyList<(string File, string Shown)> Files, string? Problem);
}
