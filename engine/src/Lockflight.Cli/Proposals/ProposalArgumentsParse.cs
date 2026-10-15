namespace Lockflight.Cli.Proposals;

/// <summary>The outcome of parsing <c>propose</c>'s arguments: exactly one of <see cref="Requests"/> or <see cref="Error"/> is set.</summary>
internal sealed record ProposalArgumentsParse(IReadOnlyList<ProposalRequest>? Requests, string? Error)
{
    public static ProposalArgumentsParse Parsed(IReadOnlyList<ProposalRequest> requests) => new(requests, null);

    public static ProposalArgumentsParse Failed(string error) => new(null, error);
}
