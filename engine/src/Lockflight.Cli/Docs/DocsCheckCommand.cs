using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Docs;

/// <summary>
/// <c>lockflight docs check</c>: lists the references in the project's instructions and docs that no longer resolve (paths,
/// anchors, engine and plugin commands) and instruction files over Claude Code's size targets. Read only and advisory: exit 1
/// when it finds something, so a CI step can show it, but no gate runs it.
/// </summary>
internal sealed class DocsCheckCommand(CliContext context) : ICommand
{
    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is null)
        {
            return ExitCode.Config;
        }

        string directory = ManifestLocator.Discover(context.WorkingDirectory) is { Manifest: Manifest manifest } ? manifest.Root : context.WorkingDirectory;
        DocsDriftReport report = await DocsDrift.FindAsync(directory, cancellationToken);
        if (!report.Scanned)
        {
            await context.Error.WriteLineAsync("lockflight docs check: not a git repository; the check reads the tracked files, so run it inside one");
            return ExitCode.Config;
        }

        await context.Output.WriteLineAsync($"docs check (engine {ToolPin.RunningVersion}): {report.Documents} documents");
        foreach (DriftFinding finding in report.Findings)
        {
            await context.Output.WriteLineAsync($"{finding.File}:{finding.Line}  {finding.Reference}  — {finding.Reason}");
        }

        foreach (string note in report.Notes)
        {
            await context.Output.WriteLineAsync($"note: {note}");
        }

        await context.Output.WriteLineAsync(report.Findings.Count == 0
            ? "every reference resolves"
            : $"{report.Findings.Count} finding{(report.Findings.Count == 1 ? string.Empty : "s")}; a deliberate example can carry {MarkdownDocument.IgnoreMarker} on its line");
        return report.Findings.Count == 0 ? ExitCode.Pass : ExitCode.Fail;
    }
}
