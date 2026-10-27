using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Adoption;

/// <summary>
/// Writes a plan for people (<c>plan.md</c>, grouped by area) and its fingerprint (<c>plan.sha256</c>): apply refuses when the setup
/// changed after the plan was reviewed.
/// </summary>
internal static class AdoptionPlanWriter
{
    public const string FingerprintFile = "plan.sha256";

    /// <param name="applyLine">The <c>adopt apply</c> command line to paste, from <see cref="AdoptApplyLine"/>.</param>
    /// <param name="applyNote">What to say under it (<see cref="AdoptApplyLine.Note"/>); null for nothing.</param>
    public static void Write(AdoptionPlan plan, string applyLine, string? applyNote)
    {
        string directory = Path.Combine(plan.Root, AdoptionPlan.RelativeDirectory);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, AdoptionPlan.MarkdownFile), Markdown(plan, applyLine, applyNote));
        File.WriteAllText(Path.Combine(directory, FingerprintFile), Fingerprint(plan) + "\n");
    }

    /// <summary>A hash of what the plan decides — items, actions and globs — not of when it was made.</summary>
    public static string Fingerprint(AdoptionPlan plan)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartArray();
            foreach (AdoptionItem item in plan.Items)
            {
                writer.WriteStartArray();
                writer.WriteStringValue(item.Area.ToString());
                writer.WriteStringValue(item.Subject);
                writer.WriteStringValue(item.Action.ToString());
                foreach (string glob in item.Globs)
                {
                    writer.WriteStringValue(glob);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    public static string Markdown(AdoptionPlan plan, string applyLine, string? applyNote)
    {
        StringBuilder text = new();
        text.AppendLine("# Lockflight adoption plan").AppendLine()
            .AppendLine($"Made {plan.At:yyyy-MM-dd HH:mm} UTC for `{plan.Root}`. Review it, then run this {HumanOnlyCommands.WhereToRun}:")
            .AppendLine().AppendLine("```sh").AppendLine(applyLine).AppendLine("```").AppendLine();
        if (applyNote is not null)
        {
            text.AppendLine(applyNote).AppendLine();
        }

        text.AppendLine("It backs up first; `adopt undo` restores. Nothing is replaced automatically: the team's own hooks, skills and agents")
            .AppendLine("stay, and become protected from the agent.").AppendLine();
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
