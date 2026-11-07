using System.Text;
using Lockflight.Cli.Evidence.Blocked;

namespace Lockflight.Cli.Ci.Explain;

/// <summary>
/// <c>ci explain</c>'s text for a person: a header with the verdict, the head and what the file is, then per check that did not
/// pass its detail, reason, Next line and the reason's entry in the reasons guide, with the gate steps under the gates check.
/// </summary>
internal static class CiExplanationText
{
    private const string Indent = "  ";
    private const int ShortHeadLength = 7;

    public static string Render(CiExplanation explanation)
    {
        StringBuilder text = new();
        string head = explanation.Head is { Length: > ShortHeadLength } full ? full[..ShortHeadLength] : explanation.Head ?? "unknown";
        string what = explanation.Document == ExplainedDocument.GateResult ? "gate run" : "lockflight ci";
        text.Append($"{explanation.Verdict} {what} for head {head}: {ExplainedDocumentNames.Describe(explanation.Document)}\n");
        text.Append($"{Indent}Source: {explanation.Source}\n");
        if (explanation.Origin is string origin)
        {
            text.Append($"{Indent}From: {origin}\n");
        }

        if (explanation.Contexts.Count > 0)
        {
            text.Append($"{Indent}Contexts: {string.Join(" · ", explanation.Contexts.Select(context => $"{context.Key} {context.Value}"))}\n");
        }

        if (explanation.Blocked is ExplainedBlock blocked)
        {
            text.Append($"\nWaiting on a person: {blocked.Summary}\n");
            foreach (string need in blocked.Needs)
            {
                text.Append($"{Indent}- {need}\n");
            }

            if (BlockedNextSteps.ConsoleLine(blocked.Code) is string line)
            {
                text.Append($"{Indent}{line}\n");
            }
        }

        if (explanation.Checks.Count == 0)
        {
            text.Append(explanation.Document == ExplainedDocument.GateResult
                ? "\nNothing to explain: every step passed or did not apply.\n"
                : "\nNothing to explain: every check passed, is off, or does not count toward the verdict.\n");
            return text.ToString();
        }

        foreach (ExplainedCheck check in explanation.Checks)
        {
            text.Append('\n');
            Check(text, check, string.Empty, explanation.Document == ExplainedDocument.GateResult);
            foreach (ExplainedCheck step in check.Steps)
            {
                Check(text, step, $"{Indent}Step ", isStep: true);
            }
        }

        return text.ToString();
    }

    /// <param name="isStep">A gate step, whose requirement is the mode the manifest gives it.</param>
    private static void Check(StringBuilder text, ExplainedCheck check, string prefix, bool isStep)
    {
        string indent = prefix.Length == 0 ? Indent : Indent + Indent;
        text.Append($"{prefix}{check.Verdict} {check.Id} ({check.Title}), {(isStep ? $"mode {check.Requirement}" : check.Requirement)}\n");
        if (check.Detail.Length > 0)
        {
            text.Append($"{indent}Detail: {check.Detail}\n");
        }

        ExplainedReason reason = check.Reason;
        text.Append(reason switch
        {
            { Known: true } => $"{indent}Reason: {reason.Name}\n{indent}Next: {reason.Next}\n",
            { Name: string unknown } => $"{indent}Reason: {unknown} (this engine has no entry for it: a newer engine wrote it; update the engine or read the guide)\n",
            _ => $"{indent}Reason: none named\n",
        });
        text.Append($"{indent}See: {reason.Link}\n");
    }
}
