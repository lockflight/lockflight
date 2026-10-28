using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Rules;

/// <summary>
/// <c>lockflight rules audit</c>: every rule in the project's instruction files with what backs it, grouped per file. A report:
/// exit 0 whatever it finds; no gate reads it.
/// </summary>
internal sealed class RulesAuditCommand(CliContext context)
{
    public async Task<ExitCode> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync("rules audit", args, [], [], context.Error, cancellationToken) is null)
        {
            return ExitCode.Config;
        }

        string directory = ManifestLocator.Discover(context.WorkingDirectory) is { Manifest: Manifest manifest } ? manifest.Root : context.WorkingDirectory;
        RulesAuditReport report = await RulesAudit.RunAsync(directory, cancellationToken);
        if (!report.Scanned)
        {
            await context.Error.WriteLineAsync("lockflight rules audit: not a git repository; the audit reads the tracked files, so run it inside one");
            return ExitCode.Config;
        }

        int Count(RuleStatus status) => report.Rules.Count(rule => rule.Status == status);
        await context.Output.WriteLineAsync(
            $"rules audit (engine {ToolPin.RunningVersion}): {report.Rules.Count} rules; {Count(RuleStatus.Checked)} checked (G), {Count(RuleStatus.Related)} related, {Count(RuleStatus.GuidanceOnly)} guidance only");
        await context.Output.WriteLineAsync("checked (G): a Lockflight guardrail refuses exactly this; related: a check touches it but does not prove it. None of these is a boundary.");
        foreach (IGrouping<string, AuditedRule> file in report.Rules.GroupBy(rule => rule.File))
        {
            await context.Output.WriteLineAsync($"{Environment.NewLine}{file.Key} ({file.Count()} rule{(file.Count() == 1 ? string.Empty : "s")})");
            foreach (AuditedRule rule in file)
            {
                await context.Output.WriteLineAsync($"  {rule.Line,4}  {Label(rule.Status),-13}  {rule.Text}");
                if (rule.By is string by)
                {
                    await context.Output.WriteLineAsync($"        by: {by}");
                }

                if (rule.Suggestion is string suggestion)
                {
                    await context.Output.WriteLineAsync($"        suggestion: {suggestion}");
                }
            }
        }

        return ExitCode.Pass;
    }

    private static string Label(RuleStatus status) => status switch
    {
        RuleStatus.Checked => "checked (G)",
        RuleStatus.Related => "related",
        _ => "guidance only",
    };
}
