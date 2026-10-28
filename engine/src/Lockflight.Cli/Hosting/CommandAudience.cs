namespace Lockflight.Cli.Hosting;

/// <summary>Who may run a command.</summary>
internal enum CommandAudience
{
    /// <summary>The agent and a person.</summary>
    Anyone = 0,

    /// <summary>A person only: refused under <c>CLAUDECODE=1</c> and blocked by the PreToolUse guard.</summary>
    Human = 1,
}
