using System.Globalization;
using System.Text;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>
/// The two lines routing gives the pull request comment, first after the verdict:
/// <code>
/// **Class: feature** · computed feature (floor fix, preset default) · db.migration `src/Infra/Migrations/…` · +1 more
/// Needs: gates · coverage 80 % · suppressions · docs references · ef pending changes · red→green per story · spec + trace · review (key)
/// </code>
/// The class line names each raise, then the top three signals by the class they raise to, then id (a boundary signal is written
/// <c>&lt;id&gt; (policy)</c>), then how many more there are; the job summary and the evidence list them all. Each line stays within
/// 300 characters before its <c>+N more</c>; a long path is shortened from the left with <c>…/</c>.
/// </summary>
internal static class RoutingComment
{
    public const int MaxLineLength = 300;

    private const int ShownSignals = 3;
    private const int MaxPathLength = 70;
    private const string Separator = " · ";

    public static string ClassLine(Classification classification, RoutingPolicy policy)
    {
        StringBuilder line = new();
        line.Append($"**Class: {Name(classification.Class)}**")
            .Append($"{Separator}computed {Name(classification.Computed)} (floor {Name(policy.Settings.Floor)}, preset {classification.Policy.Preset})");
        foreach (ClassRaise raise in classification.Raises)
        {
            if (raise.To > classification.Computed)
            {
                line.Append($"{Separator}raised by {raise.Source}");
            }
            else if (raise.To < classification.Computed)
            {
                line.Append($"{Separator}{raise.Source} says {Name(raise.To)}, ignored (lower than computed)");
            }
        }

        int shown = 0;
        foreach (ChangeSignal signal in classification.Signals)
        {
            string part = $"{Separator}{signal.Id}{(signal.Strength == SignalStrength.Boundary ? " (policy)" : string.Empty)}{(signal.Where.Length == 0 ? string.Empty : $" `{Shorten(signal.Where)}`")}";
            if (shown == ShownSignals || line.Length + part.Length > MaxLineLength)
            {
                break;
            }

            line.Append(part);
            shown++;
        }

        int more = classification.Signals.Count - shown;
        return more > 0 ? line.Append($"{Separator}+{more.ToString(CultureInfo.InvariantCulture)} more").ToString() : line.ToString();
    }

    public static string NeedsLine(RequirementSet requirements)
    {
        List<string> needs = [.. requirements.Items.Where(item => item.Level != RequirementLevel.Off).Select(item => Need(item, requirements.Class))];
        StringBuilder line = new("Needs: ");
        for (int index = 0; index < needs.Count; index++)
        {
            string part = (index == 0 ? string.Empty : Separator) + needs[index];
            if (line.Length + part.Length > MaxLineLength)
            {
                return line.Append($"{Separator}+{(needs.Count - index).ToString(CultureInfo.InvariantCulture)} more").ToString();
            }

            line.Append(part);
        }

        return needs.Count == 0 ? "Needs: nothing" : line.ToString();
    }

    private static string Need(Requirement requirement, ChangeClass changeClass)
    {
        bool required = requirement.Level is RequirementLevel.Required or RequirementLevel.RequiredWithKey;
        string threshold = requirement.Threshold?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return requirement.Kind switch
        {
            RequirementKind.Gates => "gates",
            RequirementKind.PatchCoverage => required ? $"coverage {threshold} %" : $"coverage {threshold} % (with a collector)",
            RequirementKind.Suppressions => "suppressions",
            RequirementKind.DocsReferences => "docs references",
            RequirementKind.EfPendingModelChanges => required ? "ef pending changes" : "ef pending changes (if an EF project changed)",
            RequirementKind.RedGreen => changeClass >= ChangeClass.Feature ? "red→green per story" : "red→green",
            RequirementKind.SpecAndTrace => required ? "spec + trace" : "trace (with a spec)",
            RequirementKind.Review => requirement.Level == RequirementLevel.RequiredWithKey ? "review (key required)" : "review (key)",
            RequirementKind.ArchitectureTests => required ? "architecture" : "architecture (when declared)",
            RequirementKind.SpecFirst => required ? "spec first" : "spec first (with a spec)",
            RequirementKind.Mutation => "mutation (advisory)",
            _ => throw new ArgumentOutOfRangeException(nameof(requirement), requirement.Kind, "Unknown requirement kind."),
        };
    }

    /// <summary>A path (with an optional <c>:line</c>) at most <see cref="MaxPathLength"/> characters, cut from the left at a '/'.</summary>
    private static string Shorten(string where)
    {
        string text = where.Replace('`', '\'');
        if (text.Length <= MaxPathLength)
        {
            return text;
        }

        int cut = text.IndexOf('/', text.Length - MaxPathLength + 2);
        return cut < 0 ? "…" + text[^(MaxPathLength - 1)..] : "…" + text[cut..];
    }

    private static string Name(ChangeClass changeClass) => ChangeClassNames.ToName(changeClass);
}
