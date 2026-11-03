namespace Lockflight.Cli.Lifecycle;

/// <summary>The verdict trace reaches from its rows, the same locally and in CI.</summary>
internal enum TraceOutcome
{
    /// <summary>Every requirement is covered or verified outside .NET, and at least one is covered.</summary>
    Pass = 0,

    /// <summary>At least one requirement is neither covered nor verified outside .NET.</summary>
    Fail = 1,

    /// <summary>Every requirement is verified outside .NET: there is nothing for trace to vouch for, which is never a pass.</summary>
    NotApplicable = 2,
}
