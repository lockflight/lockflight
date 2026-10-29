using System.Text;

namespace Lockflight.Cli.Adoption;

/// <summary>
/// The adoption plan as Markdown for a person, grouped by area: what <c>init</c> found in an existing setup and what it does with
/// each part. <c>init --dry-run</c> prints it.
/// </summary>
internal static class AdoptionPlanWriter
{
    public static string Markdown(AdoptionPlan plan)
    {
        StringBuilder text = new();
        text.AppendLine("# Lockflight adoption plan").AppendLine()
            .AppendLine($"For `{plan.Root}`. Nothing is replaced: the team's own hooks, skills and agents stay, and become protected from")
            .AppendLine("the agent. `.claude/settings.json` gets an `Edit` deny rule for each protected path in `permissions.deny`, so Claude Code")
            .AppendLine("refuses its built-in file tools there too, and the protected files as the sandbox's write denials (`--no-sandbox` skips")
            .AppendLine("those; they take effect while the sandbox is on). Both are merged; nothing is removed.").AppendLine();
        if (plan.Items.Count == 0)
        {
            text.AppendLine("No existing Claude Code or Spec Kit setup was found.").AppendLine();
        }

        foreach (IGrouping<AdoptionArea, AdoptionItem> area in plan.Items.GroupBy(item => item.Area).OrderBy(group => group.Key))
        {
            text.AppendLine($"## {Title(area.Key)}").AppendLine().AppendLine("| Item | Action | Why |").AppendLine("|---|---|---|");
            foreach (AdoptionItem item in area)
            {
                text.AppendLine($"| `{item.Subject.Replace("|", "\\|", StringComparison.Ordinal)}` | {item.Action.ToString().ToLowerInvariant()} | {item.Reason.Replace("|", "\\|", StringComparison.Ordinal)} |");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static string Title(AdoptionArea area) => area switch
    {
        AdoptionArea.Hook => "Hooks",
        AdoptionArea.Skill => "Skills",
        AdoptionArea.Agent => "Agents",
        AdoptionArea.Command => "Commands",
        AdoptionArea.SpecKit => "Spec Kit",
        AdoptionArea.Feature => "Existing features",
        AdoptionArea.ClaudeMd => "CLAUDE.md",
        _ => "Units",
    };
}
