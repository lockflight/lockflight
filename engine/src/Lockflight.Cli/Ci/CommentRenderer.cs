using System.Text;
using System.Text.Json.Nodes;

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

    private const string Disclaimer = "this comment says what the CI re-ran; it does not show the tests were written first.";
    private const string Truncated = "\n… more in the job summary\n";

    /// <summary>A spec diff up to this many lines is shown open; a longer one is collapsed.</summary>
    private const int OpenSpecDiffLines = 200;

    private const int MaxSpecDiffPaths = 3;

    /// <summary>The order the Next line looks for the first required check that did not pass.</summary>
    private static readonly string[] NextOrder = ["red", "tests.sinceRed", "tests.existingChanged", "tests.hostTampering", "green", "gates", "trace", "review", "approvals"];

    public static string Render(JsonNode verdict, int maxLength)
    {
        string head = verdict["head"]?.GetValue<string>() ?? "unknown";
        StringBuilder top = new();
        top.Append($"{Marker} head={Hex(head)} -->\n");
        string overall = verdict["verdict"]?.GetValue<string>() ?? "ERROR";
        string feature = verdict["feature"]?["id"]?.GetValue<string>() is string id ? $" · {CommentText.Code(id)}" : string.Empty;
        string changeClass = verdict["changeClass"]?["class"]?.GetValue<string>() ?? "?";
        string preset = verdict["changeClass"]?["policy"]?["preset"]?.GetValue<string>() is "interim" ? " (interim)" : string.Empty;
        string baseSha = verdict["base"]?["sha"]?.GetValue<string>() ?? string.Empty;
        top.Append($"**Lockflight: {overall}**{feature} · class **{CommentText.Plain(changeClass)}**{preset} · head `{Short(head)}`{(baseSha.Length > 0 ? $" · base `{Short(baseSha)}`" : string.Empty)}\n\n");
        top.Append(Contexts(verdict));
        if (Next(verdict) is string next)
        {
            top.Append($"> **Next:** {CommentText.Plain(next)}\n\n");
        }

        top.Append(SpecFromShell(verdict));
        top.Append(SpecDiff(verdict, maxLength));
        top.Append("| Check | Verdict | | Detail |\n|---|---|---|---|\n");
        foreach (JsonNode? check in verdict["checks"]?.AsArray() ?? [])
        {
            string checkId = check?["id"]?.GetValue<string>() ?? "?";
            string title = CheckIdNames.Parse(checkId) is CheckId known ? CheckIdNames.Title(known) : CommentText.Plain(checkId);
            string level = check?["requirement"]?.GetValue<string>() ?? "?";
            string shown = level == "off" ? "—" : check?["verdict"]?.GetValue<string>() ?? "?";
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

    /// <summary>In a unit repository of a workspace shell, the spec the pull request was traced against, which lives in the shell.</summary>
    private static string SpecFromShell(JsonNode verdict) =>
        verdict["feature"]?["spec"] is JsonObject { } spec && spec["origin"]?.GetValue<string>() == "shell"
            ? $"Spec: {CommentText.Code(spec["path"]?.GetValue<string>())} from the workspace shell {CommentText.Code(spec["shell"]?["url"]?.GetValue<string>())} at `{Short(spec["shell"]?["commit"]?.GetValue<string>() ?? string.Empty)}`\n\n"
            : string.Empty;

    private static string Contexts(JsonNode verdict) =>
        verdict["contexts"] is JsonObject contexts
            ? $"Status: {string.Join(" · ", contexts.Select(context => $"`{context.Key}` {context.Value?.GetValue<string>()}"))}\n\n"
            : string.Empty;

    private static IEnumerable<string> Sections(JsonNode verdict)
    {
        JsonArray cycles = verdict["cycles"]?.AsArray() ?? [];
        if (cycles.Count > 0)
        {
            StringBuilder table = new("<details><summary>Red/green cycles</summary>\n\n| Cycle | Red commit | Test | Red | Since red | At head |\n|---|---|---|---|---|---|\n");
            foreach (JsonNode? cycle in cycles)
            {
                JsonArray tests = cycle?["tests"]?.AsArray() ?? [];
                if (tests.Count == 0)
                {
                    table.Append($"| {CommentText.Plain(cycle?["label"]?.GetValue<string>())} | `{Short(cycle?["commit"]?.GetValue<string>() ?? string.Empty)}` | — | {cycle?["verdict"]?.GetValue<string>()} | | |\n");
                }

                foreach (JsonNode? test in tests)
                {
                    table.Append($"| {CommentText.Plain(cycle?["label"]?.GetValue<string>())} | `{Short(cycle?["commit"]?.GetValue<string>() ?? string.Empty)}` | {CommentText.Code(test?["key"]?.GetValue<string>())} | "
                        + $"{test?["red"]?.GetValue<string>()} | {test?["sinceRed"]?.GetValue<string>() ?? "—"} | {test?["atHead"]?.GetValue<string>() ?? "—"} |\n");
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
                trace.Append($"| {CommentText.Plain(row?["requirement"]?.GetValue<string>())} | {(tests.Length > 0 ? tests : "—")} | {row?["status"]?.GetValue<string>()} |\n");
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

            yield return findings.Append("\n</details>\n").ToString();
        }
    }

    private static string Short(string commit) => Hex(commit) is { Length: > 7 } hex ? hex[..7] : Hex(commit);

    /// <summary>Only hex digits of a commit id reach the comment, so a crafted value cannot close the marker or a code span.</summary>
    private static string Hex(string commit) => new([.. commit.Where(character => char.IsAsciiHexDigit(character))]);
}
