namespace Lockflight.Cli.Ci.Explain;

/// <summary>The spellings of <see cref="ExplainedDocument"/> in <c>ci explain --json</c> and the line a person reads.</summary>
internal static class ExplainedDocumentNames
{
    public static string ToName(ExplainedDocument document) => document switch
    {
        ExplainedDocument.Evidence => "evidence",
        ExplainedDocument.LocalEvidence => "local-evidence",
        ExplainedDocument.Verdict => "verdict",
        ExplainedDocument.Refusal => "refusal",
        ExplainedDocument.JudgeMissing => "judge-missing",
        ExplainedDocument.GateResult => "gate-result",
        _ => throw new ArgumentOutOfRangeException(nameof(document), document, "Unknown document."),
    };

    public static string Describe(ExplainedDocument document) => document switch
    {
        ExplainedDocument.Evidence => "the CI judge's evidence",
        ExplainedDocument.LocalEvidence => "the local judge's evidence (advice; the CI judge is the verdict of record)",
        ExplainedDocument.Verdict => "the CI report's verdict of record",
        ExplainedDocument.Refusal => "the CI action refused the engine pin, so the judge did not run",
        ExplainedDocument.JudgeMissing => "the CI report's verdict without usable judge evidence",
        ExplainedDocument.GateResult => "a gate result",
        _ => throw new ArgumentOutOfRangeException(nameof(document), document, "Unknown document."),
    };
}
