namespace Lockflight.Cli.Proposals;

/// <summary>The outcome of checking one request: exactly one of <see cref="Target"/> or <see cref="Error"/> is set.</summary>
internal sealed record ProposalTargetResolution(ProposalTarget? Target, string? Error)
{
    public static ProposalTargetResolution Resolved(ProposalTarget target) => new(target, null);

    public static ProposalTargetResolution Refused(string error) => new(null, error);
}
