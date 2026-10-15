namespace Lockflight.Cli.Ledger;

/// <summary>What started a gate run, so the ledger can tell a run someone asked for from the one the Stop hook made.</summary>
internal enum GateTrigger
{
    /// <summary><c>lockflight gate run</c> on the command line, by a person, an agent or CI.</summary>
    Command = 1,

    /// <summary>The Stop hook at the end of an agent's turn.</summary>
    StopHook = 2,
}
