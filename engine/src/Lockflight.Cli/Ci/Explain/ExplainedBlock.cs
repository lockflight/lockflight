using Lockflight.Cli.Evidence.Blocked;

namespace Lockflight.Cli.Ci.Explain;

/// <summary>The run waits for a person (the evidence's <c>blocked</c> report): its code as written, summary and what each person must do.</summary>
internal sealed record ExplainedBlock(string? CodeName, string Summary, IReadOnlyList<string> Needs)
{
    public BlockedReasonCode Code => BlockedNames.CodeOf(CodeName);
}
