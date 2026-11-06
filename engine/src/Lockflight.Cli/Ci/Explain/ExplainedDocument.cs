namespace Lockflight.Cli.Ci.Explain;

/// <summary>What kind of Lockflight file <c>ci explain</c> read, told apart by its schema and the fields its writer sets.</summary>
internal enum ExplainedDocument
{
    /// <summary>The CI judge's <c>evidence.json</c>, before the report joined the review.</summary>
    Evidence = 0,

    /// <summary>The local judge's evidence (<c>lockflight ci --platform local</c>, <c>.lockflight/local/ci/last.json</c>).</summary>
    LocalEvidence = 1,

    /// <summary>The report's <c>verdict.json</c>: the judge's evidence with the review joined, the verdict of record.</summary>
    Verdict = 2,

    /// <summary>The action refused the engine pin and the judge never ran (<c>refused.json</c>, or a verdict made from it).</summary>
    Refusal = 3,

    /// <summary>The report's short form when the judge's evidence was missing or could not be used.</summary>
    JudgeMissing = 4,

    /// <summary>A gate result, <c>lockflight.result/v1</c> (<c>gate run --result</c>).</summary>
    GateResult = 5,
}
