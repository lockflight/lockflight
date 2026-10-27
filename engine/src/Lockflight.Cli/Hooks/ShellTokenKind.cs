namespace Lockflight.Cli.Hooks;

/// <summary>What a shell token is to the write scanner.</summary>
internal enum ShellTokenKind
{
    /// <summary>A word after quote removal (a program, an option, an operand).</summary>
    Word = 0,

    /// <summary>Ends a simple command: <c>;</c>, a newline, <c>&amp;&amp;</c>, <c>||</c>, <c>|</c>, <c>&amp;</c> or a PowerShell brace.</summary>
    Separator = 1,

    /// <summary>A POSIX subshell <c>(</c>: a <c>cd</c> inside it ends with it.</summary>
    SubshellOpen = 2,

    /// <summary>A PowerShell grouping <c>(</c>: its value stands in for a word of the enclosing command.</summary>
    GroupOpen = 3,

    /// <summary>A command substitution (<c>$(</c>, a backtick, <c>&lt;(</c>, PowerShell <c>$(</c>/<c>@(</c>): a command of its own.</summary>
    SubstitutionOpen = 4,

    /// <summary>Closes the innermost open subshell, group or substitution.</summary>
    Close = 5,

    /// <summary>An output redirection (<c>&gt;</c>, <c>&gt;&gt;</c>, <c>2&gt;</c>, <c>&amp;&gt;</c>, <c>&gt;|</c>, …): the next word is written.</summary>
    WriteRedirect = 6,

    /// <summary>An input redirection or here-document marker: the next word is read, not written.</summary>
    InputRedirect = 7,

    /// <summary>Text a command reads on stdin as written in the command line: a here-document body or a here-string.</summary>
    HereText = 8,
}
