using System.Text;
using System.Text.Json.Nodes;
using Lockflight.Cli.Mascot;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The PR comment and job summary from a verdict file: a marker line the report finds its own comment by, the verdict line, the
/// single most actionable "Next" line, the spec diff (or, for a unit of a workspace shell, the shell's spec), the checks table, and
/// collapsed sections for the cycles, amends, trace and host-tampering findings. Within GitHub's comment limit the sections are
/// dropped from the end, never the table or the Next line.
/// </summary>
internal static class CommentRenderer
{
    public const string Marker = "<!-- lockflight:ci:v1";
    public const int MaxCommentLength = 65_536;

    private const string AdvisoryMark = " (advisory — would block if required)";

    private const string AdvisoryNote =
        "> `lockflight/judge` blocks a merge only where the platform requires that check (a ruleset or branch protection, a merge "
        + "check, a pipeline policy); Lockflight cannot see which, and `gates` passed. Fix what the judge found (`lockflight ci` gives "
        + "the same verdict locally), or, where the check is not required, merge knowing it blocks nothing.";

    private const string Disclaimer = "this comment says what the CI re-ran; it does not show the tests were written first.";
    private const string Truncated = "\n… more in the job summary\n";

    /// <summary>A spec diff up to this many lines is shown open; a longer one is collapsed.</summary>
    private const int OpenSpecDiffLines = 200;

    private const int MaxSpecDiffPaths = 3;

    /// <summary>The most loosenings (and requirement-marker changes) the comment lists, so the section leaves room for the others.</summary>
    private const int MaxLoosenedShown = 30;
    private const int MaxEntriesShown = 30;
    private const string CutMark = "…";

    /// <summary>The order the Next line looks for the first required check that did not pass.</summary>
    private static readonly string[] NextOrder = ["policy", "red", "tests.sinceRed", "tests.existingChanged", "tests.hostTampering", "green", "gates", "trace", "review", "approvals"];

    public static string Render(JsonNode verdict, int maxLength) => Render(verdict, maxLength, BadgeSize.Small);

    /// <summary>The job summary: Levi's large verdict picture above the comment's content, uncut.</summary>
    public static string Summary(JsonNode verdict) =>
        $"{Badge(verdict, BadgeSize.Large)}\n\n{Render(verdict, int.MaxValue, badge: null)}";

    private static string Render(JsonNode verdict, int maxLength, BadgeSize? badge)
    {
        string head = verdict["head"]?.GetValue<string>() ?? "unknown";
        StringBuilder top = new();
        top.Append($"{Marker} head={Hex(head)} -->\n");
        string overall = CommentText.Plain(verdict["verdict"]?.GetValue<string>() ?? "ERROR");
        string feature = verdict["feature"]?["id"]?.GetValue<string>() is string id ? $" · {CommentText.Code(id)}" : string.Empty;
        string changeClass = verdict["changeClass"]?["class"]?.GetValue<string>() ?? "?";
        string preset = verdict["changeClass"]?["policy"]?["preset"]?.GetValue<string>() is "interim" ? " (interim)" : string.Empty;
        string baseSha = verdict["base"]?["sha"]?.GetValue<string>() ?? string.Empty;
        top.Append($"{(badge is BadgeSize size ? Badge(verdict, size) + " " : string.Empty)}**Lockflight: {overall}**{feature} · class **{CommentText.Plain(changeClass)}**{preset}{Excluded(verdict)} · head `{Short(head)}`{(baseSha.Length > 0 ? $" · base `{Short(baseSha)}`" : string.Empty)}\n\n");
        top.Append(Contexts(verdict));
        if (Next(verdict) is string next)
        {
            top.Append($"> **Next:** {CommentText.Plain(next)}\n\n");
        }

        top.Append(MarkerChanges(verdict));
        top.Append(SpecFromShell(verdict));
        top.Append(SpecDiff(verdict, maxLength));
        top.Append("| Check | Verdict | | Detail |\n|---|---|---|---|\n");
        foreach (JsonNode? check in verdict["checks"]?.AsArray() ?? [])
        {
            string checkId = check?["id"]?.GetValue<string>() ?? "?";
            string title = CheckIdNames.Parse(checkId) is CheckId known ? CheckIdNames.Title(known) : CommentText.Plain(checkId);
            string level = CommentText.Plain(check?["requirement"]?.GetValue<string>() ?? "?");
            string shown = level == "off" ? "—" : CommentText.Plain(check?["verdict"]?.GetValue<string>() ?? "?");
            top.Append($"| {title} | {shown} | {level} | {CommentText.Plain(check?["why"]?.GetValue<string>())} |\n");
        }

        top.Append('\n');
        string footer = $"\n<sub>Evidence: the `lockflight-verdict` artifact · engine {CommentText.Code(verdict["engine"]?["version"]?.GetValue<string>())} · policy from `{Short(verdict["policy"]?["from"]?.GetValue<string>() ?? string.Empty)}` · {Disclaimer}</sub>\n";
        StringBuilder body = new(top.ToString());
        foreach (string section in Sections(verdict))
        {
            if (body.Length + section.Length + footer.Length + Truncated.Length > maxLength)
            {
                body.Append(Truncated);
                break;
            }

            body.Append(section);
        }

        return body.Append(footer).ToString();
    }

    /// <summary>Levi's picture for the verdict, its alt text the verdict alone (a person sees Levi, a reader of the text sees the verdict).</summary>
    private static string Badge(JsonNode verdict, BadgeSize size)
    {
        string overall = verdict["verdict"]?.GetValue<string>() ?? "ERROR";
        MascotPose pose = VerdictBadge.PoseOf(overall, verdict["blocked"] is JsonObject);
        return $"![{CommentText.Plain(overall)}]({VerdictBadge.Url(verdict["engine"]?["version"]?.GetValue<string>(), pose, size)})";
    }

    /// <summary>The first required check that did not pass, in <see cref="NextOrder"/>, as its why; null when every one passed.</summary>
    public static string? Next(JsonNode verdict)
    {
        JsonNode?[] checks = [.. verdict["checks"]?.AsArray() ?? []];
        foreach (string id in NextOrder)
        {
            if (checks.FirstOrDefault(check => check?["id"]?.GetValue<string>() == id) is JsonNode check
                && check["requirement"]?.GetValue<string>() == "required" && check["verdict"]?.GetValue<string>() != "PASS")
            {
                return $"{(CheckIdNames.Parse(id) is CheckId known ? CheckIdNames.Title(known) : id)}: {check["why"]?.GetValue<string>()}";
            }
        }

        return null;
    }

    /// <summary>
    /// The spec diff, first after the Next line because the spec is what a person approves: open up to
    /// <see cref="OpenSpecDiffLines"/> lines, collapsed above, and in the comment cut to a third of its length limit.
    /// </summary>
    private static string SpecDiff(JsonNode verdict, int maxLength)
    {
        if (verdict["specDiff"] is not JsonObject diff || diff["text"]?.GetValue<string>() is not { Length: > 0 } text)
        {
            return string.Empty;
        }

        string[] paths = [.. (diff["paths"]?.AsArray() ?? []).Select(path => path?.GetValue<string>() ?? string.Empty)];
        string shown = string.Join(", ", paths.Take(MaxSpecDiffPaths).Select(CommentText.Plain)) + (paths.Length > MaxSpecDiffPaths ? $" and {paths.Length - MaxSpecDiffPaths} more" : string.Empty);
        int lines = diff["lines"]?.GetValue<int>() ?? 0;
        int room = maxLength / 3;
        string cut = text.Length <= room ? text : text[..(text.LastIndexOf('\n', room - 1) is int lastLine and > 0 ? lastLine + 1 : room)];
        string note = cut.Length < text.Length ? "\n… the whole spec diff is in the job summary\n"
            : diff["truncated"]?.GetValue<bool>() == true ? "\n… the spec diff is longer than the evidence keeps; read it in the pull request's files\n"
            : string.Empty;
        string open = lines <= OpenSpecDiffLines ? " open" : string.Empty;
        return $"<details{open}><summary>Spec changes ({lines} lines): {shown}</summary>\n\n{CommentText.Fenced(cut, "diff")}{note}\n</details>\n\n";
    }

    /// <summary>
    /// The counts of requirements trace left out as verified outside .NET and of structural requirements, for the headline; empty
    /// when none.
    /// </summary>
    private static string Excluded(JsonNode verdict) =>
        Count(verdict["trace"]?["excluded"], "requirement verified outside .NET", "requirements verified outside .NET")
        + Count(verdict["trace"]?["structural"], "structural requirement", "structural requirements");

    private static string Count(JsonNode? node, string one, string many) =>
        node is JsonValue value && value.TryGetValue(out int count) && count > 0 ? $" · **{count} {(count == 1 ? one : many)}**" : string.Empty;

    /// <summary>Markers put on, changed on or taken off requirements the base already had, open above the checks.</summary>
    private static string MarkerChanges(JsonNode verdict)
    {
        if (verdict["trace"]?["markerChanges"] is not JsonArray { Count: > 0 } changes)
        {
            return string.Empty;
        }

        StringBuilder list = new("> **Requirement markers changed on requirements the base already had** (each takes a requirement out of the usual trace; check it):\n");
        foreach (JsonNode? change in changes.Take(MaxLoosenedShown))
        {
            list.Append($"> - {CommentText.Plain(change is JsonValue text && text.TryGetValue(out string? line) ? line : null)}\n");
        }

        if (changes.Count > MaxLoosenedShown)
        {
            list.Append($"> - … and {changes.Count - MaxLoosenedShown} more, in the evidence's trace.markerChanges\n");
        }

        return list.Append('\n').ToString();
    }

    /// <summary>In a unit repository of a workspace shell, the spec the pull request was traced against, which lives in the shell.</summary>
    private static string SpecFromShell(JsonNode verdict) =>
        verdict["feature"]?["spec"] is JsonObject { } spec && spec["origin"]?.GetValue<string>() == "shell"
            ? $"Spec: {CommentText.Code(spec["path"]?.GetValue<string>())} from the workspace shell {CommentText.Code(spec["shell"]?["url"]?.GetValue<string>())} at `{Short(spec["shell"]?["commit"]?.GetValue<string>() ?? string.Empty)}`\n\n"
            : string.Empty;

    /// <summary>
    /// The status contexts and their verdicts. A <c>lockflight/judge</c> that did not pass while <c>gates</c> did is marked advisory, with
    /// what to do: it blocks a merge only where a person made it a required check, which the report cannot see.
    /// </summary>
    private static string Contexts(JsonNode verdict)
    {
        if (verdict["contexts"] is not JsonObject contexts)
        {
            return string.Empty;
        }

        string gates = StatusContextNames.ToName(StatusContext.Gates);
        string judge = StatusContextNames.ToName(StatusContext.Judge);
        string pass = CheckVerdictNames.ToName(CheckVerdict.Pass);
        bool advisory = contexts[gates]?.GetValue<string>() == pass && contexts[judge]?.GetValue<string>() is string judged && judged != pass;
        string line = string.Join(" · ", contexts.Select(context =>
            $"{CommentText.Code(context.Key)} {CommentText.Plain(context.Value?.GetValue<string>())}{(advisory && context.Key == judge ? AdvisoryMark : string.Empty)}"));
        return advisory ? $"Status: {line}\n\n{AdvisoryNote}\n\n" : $"Status: {line}\n\n";
    }

    private static IEnumerable<string> Sections(JsonNode verdict)
    {
        if (verdict["policy"]?["loosened"]?.AsArray() is { Count: > 0 } loosened)
        {
            StringBuilder list = new($"<details open><summary>Policy loosened ({loosened.Count})</summary>\n\n");
            foreach (JsonNode? setting in loosened.Take(MaxLoosenedShown))
            {
                list.Append($"- {CommentText.Plain(setting?.GetValue<string>())}\n");
            }

            if (loosened.Count > MaxLoosenedShown)
            {
                list.Append($"- and {loosened.Count - MaxLoosenedShown} more in the evidence's policy.loosened\n");
            }

            yield return list.Append("\n</details>\n").ToString();
        }

        JsonArray cycles = verdict["cycles"]?.AsArray() ?? [];
        if (cycles.Count > 0)
        {
            StringBuilder table = new("<details><summary>Red/green cycles</summary>\n\n| Cycle | Red commit | Test | Red | Since red | At head |\n|---|---|---|---|---|---|\n");
            foreach (JsonNode? cycle in cycles)
            {
                JsonArray tests = cycle?["tests"]?.AsArray() ?? [];
                if (tests.Count == 0)
                {
                    table.Append($"| {CommentText.Plain(cycle?["label"]?.GetValue<string>())} | `{Short(cycle?["commit"]?.GetValue<string>() ?? string.Empty)}` | — | {CommentText.Plain(cycle?["verdict"]?.GetValue<string>())} | | |\n");
                }

                foreach (JsonNode? test in tests)
                {
                    table.Append($"| {CommentText.Plain(cycle?["label"]?.GetValue<string>())} | `{Short(cycle?["commit"]?.GetValue<string>() ?? string.Empty)}` | {CommentText.Code(test?["key"]?.GetValue<string>())} | "
                        + $"{CommentText.Plain(test?["red"]?.GetValue<string>())} | {CommentText.Plain(test?["sinceRed"]?.GetValue<string>() ?? "—")} | {CommentText.Plain(test?["atHead"]?.GetValue<string>() ?? "—")} |\n");
                }
            }

            yield return table.Append("\n</details>\n").ToString();
        }

        JsonNode?[] amends = [.. cycles.SelectMany(cycle => cycle?["tests"]?.AsArray() ?? []).Where(test => test?["amend"] is JsonObject)];
        if (amends.Length > 0)
        {
            StringBuilder list = new($"<details><summary>Amends after red ({amends.Length})</summary>\n\n");
            foreach (JsonNode? test in amends)
            {
                string files = string.Join(", ", (test?["amend"]?["files"]?.AsArray() ?? []).Select(file => CommentText.Code(file?.GetValue<string>())));
                list.Append($"- {CommentText.Code(test?["key"]?.GetValue<string>())}: {CommentText.Plain(test?["amend"]?["summary"]?.GetValue<string>())}{(files.Length > 0 ? $" ({files})" : string.Empty)}\n");
            }

            yield return list.Append("\n</details>\n").ToString();
        }

        if (verdict["trace"]?["rows"]?.AsArray() is { Count: > 0 } rows)
        {
            StringBuilder trace = new("<details><summary>Trace</summary>\n\n| Requirement | Tests (red → green) | Status |\n|---|---|---|\n");
            foreach (JsonNode? row in rows)
            {
                string tests = string.Join("<br>", (row?["tests"]?.AsArray() ?? []).Select(test => CommentText.Code(test?.GetValue<string>())));
                string note = row?["note"] is JsonValue noteValue && noteValue.TryGetValue(out string? noteText) ? $" — {CommentText.Plain(noteText)}" : string.Empty;
                trace.Append($"| {CommentText.Plain(row?["requirement"]?.GetValue<string>())} | {(tests.Length > 0 ? tests : "—")} | {CommentText.Plain(row?["status"]?.GetValue<string>())}{note} |\n");
            }

            yield return trace.Append("\n</details>\n").ToString();
        }

        if (verdict["hostTampering"] is JsonObject tampering && ((tampering["new"]?.AsArray().Count ?? 0) + (tampering["existing"]?.AsArray().Count ?? 0)) > 0)
        {
            StringBuilder findings = new("<details><summary>Host-tampering findings</summary>\n\n");
            foreach ((string label, JsonArray? list) in new[] { ("new", tampering["new"]?.AsArray()), ("already on the base or accepted", tampering["existing"]?.AsArray()) })
            {
                foreach (JsonNode? finding in list ?? [])
                {
                    findings.Append($"- {label}: {CommentText.Plain(finding?["kind"]?.GetValue<string>())} in {CommentText.Code(finding?["location"]?.GetValue<string>())} ({CommentText.Code(finding?["member"]?.GetValue<string>())})\n");
                }
            }

            findings.Append(BaselineEntries(tampering["new"]?.AsArray() ?? []));

            yield return findings.Append("\n</details>\n").ToString();
        }
    }

    /// <summary>
    /// The new findings' baseline entries, ready to paste, after how a person accepts one (the engine's own text, so neither escaped
    /// nor capped like the findings). An entry is shown only when it names exactly its finding; the rest, and those past
    /// <see cref="MaxEntriesShown"/>, are counted and left to the job log.
    /// </summary>
    private static string BaselineEntries(JsonArray found)
    {
        if (found.Count == 0)
        {
            return string.Empty;
        }

        string[] entries = [.. found.Select(ShownEntry).OfType<string>()];
        StringBuilder text = new($"\n{HostTamperingJudge.AcceptAdvice} The new findings' entries:\n\n");
        if (entries.Length > 0)
        {
            text.Append(CommentText.Fenced(string.Join(",\n", entries.Take(MaxEntriesShown)), "json"));
        }

        int missing = found.Count - Math.Min(entries.Length, MaxEntriesShown);
        return missing > 0 ? text.Append($"\n{missing} more {(missing == 1 ? "entry is" : "entries are")} in the `lockflight ci` output of the job log.\n").ToString() : text.ToString();
    }

    /// <summary>A finding's baseline entry when it names that finding: the evidence's id, unless the id was cut to fit the evidence.</summary>
    private static string? ShownEntry(JsonNode? finding)
    {
        string? id = finding?["id"] is JsonValue idValue && idValue.TryGetValue(out string? text) ? text : null;
        return finding?["baselineEntry"] is JsonValue value && value.TryGetValue(out string? entry) && HostTamperingJudge.EntryId(entry) is string named
            && (id is null || id.EndsWith(CutMark, StringComparison.Ordinal) || id == named)
            ? entry
            : null;
    }

    private static string Short(string commit) => Hex(commit) is { Length: > 7 } hex ? hex[..7] : Hex(commit);

    /// <summary>Only hex digits of a commit id reach the comment, so a crafted value cannot close the marker or a code span.</summary>
    private static string Hex(string commit) => new([.. commit.Where(character => char.IsAsciiHexDigit(character))]);
}
