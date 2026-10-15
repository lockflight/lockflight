namespace Lockflight.Cli.Ledger;

/// <summary>Where a person gave an approval, so the ledger can tell a terminal approval from one typed in a Claude Code session.</summary>
internal enum ApprovalChannel
{
    /// <summary><c>lockflight approve</c> run in a terminal outside the session.</summary>
    Terminal = 1,

    /// <summary><c>/lockflight:approve</c> typed by the person in an interactive Claude Code session.</summary>
    Session = 2,
}
