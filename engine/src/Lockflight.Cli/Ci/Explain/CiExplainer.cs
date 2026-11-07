using System.Text.Json.Nodes;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Ci.Explain;

/// <summary>
/// Reads a Lockflight file as data and lists what did not pass: the checks of <c>lockflight.ci-evidence/v1</c> (the judge's
/// evidence, the local judge's, the report's verdict, the action's refusal) or the steps of a gate result. A check that passed,
/// is off, or did not apply or run where it is not required is left out; the gates check carries the gate steps that did not pass. The
/// file may come from another machine's run, or from a judge job that ran a pull request's code, so nothing in it is run or
/// trusted beyond being shown: every string it shows is one line with its control characters made visible
/// (<see cref="TerminalText"/>), so a detail cannot recolour the terminal, rewrite a line or forge one.
/// </summary>
internal static class CiExplainer
{
    private const string JudgeRefused = "refused";
    private const string JudgeMissing = "missing";
    private const string ReviewExpected = "expected";
    private const string ReviewNotExpected = "not-expected";
    private const string LocalPlatform = "local";
    private const string Required = "required";
    private const string Off = "off";
    private const string Pass = "PASS";
    private const string NotApplicable = "N/A";
    private const string Skipped = "SKIPPED";
    private const string GatesCheck = "gates";

    /// <param name="origin">Which job wrote a run's artifact and how far it is trusted; null for a file the person named.</param>
    public static CiExplanationReading Explain(JsonObject document, string source, string? origin)
    {
        string? schema = Text(document, "schema");
        if (schema == GateResultWriter.Schema)
        {
            return new CiExplanationReading(GateResult(document, source) with { Origin = Printable(origin) }, null);
        }

        if (schema != CiEvidence.Schema)
        {
            return new CiExplanationReading(null, $"it is not {CiEvidence.Schema} evidence or a {GateResultWriter.Schema} gate result (schema: {Printable(schema) ?? "none"})");
        }

        if (document["checks"] is not JsonArray checks)
        {
            return new CiExplanationReading(null, "it has no checks");
        }

        JsonObject? gates = document["gates"] as JsonObject;
        List<ExplainedCheck> explained = [];
        foreach (JsonObject check in checks.OfType<JsonObject>())
        {
            string id = Text(check, "id") ?? "?";
            string requirement = Text(check, "requirement") ?? "?";
            string verdict = Text(check, "verdict") ?? "?";
            if (!Shown(verdict, requirement))
            {
                continue;
            }

            IReadOnlyList<ExplainedCheck> steps = id == GatesCheck && gates is not null ? Steps(gates) : [];
            explained.Add(new ExplainedCheck(Line(id), Line(Title(id)), Line(requirement), Line(verdict), Line(Text(check, "why")), ExplainedReason.Of(Text(check, "reason")), steps));
        }

        return new CiExplanationReading(
            new CiExplanation(Line(source), Kind(document), Printable(Text(document, "head")), Line(Text(document, "verdict") ?? "?"), Contexts(document), explained, Blocked(document))
            {
                Origin = Printable(origin),
            },
            null);
    }

    private static CiExplanation GateResult(JsonObject result, string source) =>
        new(Line(source), ExplainedDocument.GateResult, Printable(Text(result, "head")), Line(Text(result, "verdict") ?? "?"), [], Steps(result), null);

    /// <summary>The gate steps that did not pass and applied (N/A did not apply); each with its mode as the requirement.</summary>
    private static IReadOnlyList<ExplainedCheck> Steps(JsonObject result) =>
    [
        .. (result["steps"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(step => Text(step, "verdict") is not (Pass or NotApplicable))
            .Select(step => new ExplainedCheck(
                Line(Text(step, "id") ?? "?"),
                Line(Text(step, "name") ?? Text(step, "id") ?? "?"),
                Line(Text(step, "mode") ?? "?"),
                Line(Text(step, "verdict") ?? "?"),
                Line(Text(step, "detail")),
                ExplainedReason.Of(Text(step, "reason")),
                [])),
    ];

    /// <summary>
    /// A check is shown unless it passed or is off. N/A and SKIPPED are shown only where the check is required, since there they
    /// make the verdict NOT-EARNED; elsewhere they block nothing (an advisory review without a key), as in the PR comment.
    /// </summary>
    private static bool Shown(string verdict, string requirement) =>
        verdict != Pass && requirement != Off && (requirement == Required || verdict is not (NotApplicable or Skipped));

    private static ExplainedDocument Kind(JsonObject evidence) => Text(evidence, "judge") switch
    {
        JudgeRefused => ExplainedDocument.Refusal,
        JudgeMissing => ExplainedDocument.JudgeMissing,
        _ when Text(evidence, "platform") == LocalPlatform => ExplainedDocument.LocalEvidence,
        _ when evidence["review"] is JsonObject review && Text(review, "state") is not (null or ReviewExpected or ReviewNotExpected) => ExplainedDocument.Verdict,
        _ => ExplainedDocument.Evidence,
    };

    private static IReadOnlyList<KeyValuePair<string, string>> Contexts(JsonObject evidence) =>
        evidence["contexts"] is JsonObject contexts
            ? [.. contexts.Select(context => KeyValuePair.Create(Line(context.Key), context.Value is JsonValue value && value.TryGetValue(out string? verdict) ? Line(verdict) : "?"))]
            : [];

    private static ExplainedBlock? Blocked(JsonObject evidence)
    {
        if (evidence["blocked"] is not JsonObject blocked)
        {
            return null;
        }

        JsonObject? reason = blocked["reason"] as JsonObject;
        IReadOnlyList<string> needs =
        [
            .. (blocked["needs"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(need => $"{Line(Text(need, "who") ?? "person")}: {Line(Text(need, "detail"))}"),
        ];
        return new ExplainedBlock(Printable(reason is null ? null : Text(reason, "code")), Line(reason is null ? null : Text(reason, "summary")), needs);
    }

    private static string Title(string id) => CheckIdNames.Parse(id) is CheckId check ? CheckIdNames.Title(check) : id;

    /// <summary>The text as one printable line (an absent text is empty).</summary>
    private static string Line(string? text) => Printable(text) ?? string.Empty;

    /// <summary>The text with its line breaks made spaces and every other control character made visible; null stays null.</summary>
    private static string? Printable(string? text) => text is null ? null : TerminalText.Clean(text.ReplaceLineEndings(" "));

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
