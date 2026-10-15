namespace Lockflight.Cli.Proposals;

/// <summary>One <c>&lt;path&gt; --from &lt;file&gt;</c> or <c>&lt;path&gt; --delete</c> pair, as typed.</summary>
/// <param name="ContentPath">The file holding the complete new content; null for a deletion.</param>
internal sealed record ProposalRequest(string Path, string? ContentPath);
