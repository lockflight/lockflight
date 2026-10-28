namespace Lockflight.Cli.Docs;

/// <summary>
/// The docs drift scan: every scanned document (<see cref="DocumentSelection"/>) read for links and anchors, code-span paths with
/// evidence of drift, engine and plugin command lines, and the size budget. Read only; no network, no build.
/// </summary>
internal static class DocsDrift
{
    public static async Task<DocsDriftReport> FindAsync(string directory, CancellationToken cancellationToken)
    {
        if (await RepositoryFiles.LoadAsync(directory, cancellationToken) is not RepositoryFiles files)
        {
            return DocsDriftReport.NotARepository;
        }

        DriftContext context = new(files, cancellationToken);
        PluginCommandCheck plugins = PluginCommandCheck.For(files.Tracked);
        List<DriftFinding> findings = [];
        string[] documents = [.. files.Tracked.Where(DocumentSelection.IsScanned).Order(StringComparer.Ordinal)];
        foreach (string document in documents)
        {
            IReadOnlyList<MarkdownLine> lines = MarkdownDocument.Parse(await File.ReadAllTextAsync(Path.Combine(files.Root, document), cancellationToken));
            if (InstructionBudget.Check(document, lines) is DriftFinding budget)
            {
                findings.Add(budget);
            }

            foreach (MarkdownLine line in lines)
            {
                findings.AddRange(await CheckLineAsync(document, line, context, plugins));
            }
        }

        IReadOnlyList<string> notes = context.HistoryRead ? (await context.HistoryAsync()).Notes : [];
        return new DocsDriftReport(Scanned: true, documents.Length, findings, notes);
    }

    private static async Task<IReadOnlyList<DriftFinding>> CheckLineAsync(string document, MarkdownLine line, DriftContext context, PluginCommandCheck plugins)
    {
        if (line.Kind == MarkdownLineKind.Fenced)
        {
            return CommandLineCheck.IsShellFence(line.FenceLanguage) ? [.. CommandLineCheck.Check(document, line.Number, line.Text)] : [];
        }

        if (line.Kind is not (MarkdownLineKind.Prose or MarkdownLineKind.Heading))
        {
            return [];
        }

        List<DriftFinding> findings = [.. LinkCheck.Check(document, line, context)];
        foreach (string span in InlineCode.Spans(line.Text))
        {
            if (CommandLineCheck.IsInvocation(span))
            {
                findings.AddRange(CommandLineCheck.Check(document, line.Number, span));
            }
            else if (PluginCommandCheck.IsSlashCommand(span))
            {
                findings.AddRange(plugins.Check(document, line.Number, span) is DriftFinding finding ? [finding] : []);
            }
            else if (await CodeSpanPathCheck.CheckAsync(document, line.Number, span, context) is DriftFinding pathFinding)
            {
                findings.Add(pathFinding);
            }
        }

        return findings;
    }
}
