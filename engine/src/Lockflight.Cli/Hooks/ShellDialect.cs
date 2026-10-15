namespace Lockflight.Cli.Hooks;

/// <summary>Which quoting rules a command follows; they differ on what a backslash means.</summary>
internal enum ShellDialect
{
    /// <summary>bash/sh (the Bash tool, Git Bash on Windows): backslash escapes the next character.</summary>
    Posix = 1,

    /// <summary>PowerShell (the PowerShell tool): backslash is a path separator, the backtick escapes.</summary>
    PowerShell = 2,
}
