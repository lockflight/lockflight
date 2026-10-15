namespace Lockflight.Cli.Hooks;

/// <summary>
/// A command string another program runs: <c>bash -c</c>, <c>eval</c>, <c>pwsh -Command</c>, <c>Invoke-Expression</c>,
/// <c>cmd /c</c> and <c>Start-Process</c>. <see cref="SameShell"/> is set when it runs in the current shell (a cd inside it
/// persists); <see cref="Hidden"/> names one whose text cannot be read (<c>-EncodedCommand</c>).
/// </summary>
internal sealed record NestedShellCommand(string Command, ShellDialect Dialect, bool SameShell, string? Hidden)
{
    private static readonly HashSet<string> PosixShells = ["bash", "sh", "zsh", "dash", "ksh"];
    private static readonly HashSet<string> PowerShells = ["pwsh", "powershell"];
    private static readonly HashSet<string> Evaluators = ["eval", "invoke-expression", "iex"];
    private static readonly HashSet<string> ProcessStarters = ["start-process", "saps", "start"];

    public static NestedShellCommand? Find(ShellCommand command)
    {
        IReadOnlyList<string> arguments = command.Arguments;
        string program = command.Program;
        if (Evaluators.Contains(program) && arguments.Count > 0)
        {
            return new(string.Join(' ', arguments), program == "eval" ? ShellDialect.Posix : ShellDialect.PowerShell, SameShell: true, null);
        }

        if (PosixShells.Contains(program))
        {
            int flag = IndexOf(arguments, argument => argument.Length > 1 && argument[0] == '-' && argument[1] != '-' && argument.EndsWith('c'));
            return flag >= 0 && flag + 1 < arguments.Count ? new(arguments[flag + 1], ShellDialect.Posix, SameShell: false, null) : null;
        }

        if (program == "cmd")
        {
            // cmd's own commands (del, copy, move, ren, rd) are PowerShell aliases of the same writers, with literal backslashes.
            int flag = IndexOf(arguments, argument => argument.Equals("/c", StringComparison.OrdinalIgnoreCase) || argument.Equals("/k", StringComparison.OrdinalIgnoreCase));
            return flag >= 0 ? new(string.Join(' ', arguments.Skip(flag + 1)), ShellDialect.PowerShell, SameShell: false, null) : null;
        }

        if (PowerShells.Contains(program))
        {
            return PowerShellCommand(program, arguments);
        }

        return ProcessStarters.Contains(program) ? StartedProcess(arguments) : null;
    }

    private static NestedShellCommand? PowerShellCommand(string program, IReadOnlyList<string> arguments)
    {
        if (arguments.Any(argument => argument.Length > 2 && "-encodedcommand".StartsWith(argument.ToLowerInvariant(), StringComparison.Ordinal)))
        {
            return new(string.Empty, ShellDialect.PowerShell, SameShell: false, $"{program} -EncodedCommand");
        }

        int flag = IndexOf(arguments, argument => argument.Length > 1 && "-command".StartsWith(argument.ToLowerInvariant(), StringComparison.Ordinal));
        return flag >= 0 && flag + 1 < arguments.Count ? new(string.Join(' ', arguments.Skip(flag + 1)), ShellDialect.PowerShell, SameShell: false, null) : null;
    }

    /// <summary><c>Start-Process &lt;exe&gt; [-ArgumentList] &lt;args&gt;</c> runs the program with those arguments.</summary>
    private static NestedShellCommand? StartedProcess(IReadOnlyList<string> arguments)
    {
        PowerShellArguments parsed = PowerShellArguments.Parse(arguments);
        IReadOnlyList<string> named = parsed.Values("filepath");
        string? executable = named.Count > 0 ? named[0] : parsed.Positional.Count > 0 ? parsed.Positional[0] : null;
        if (executable is null)
        {
            return null;
        }

        IEnumerable<string> processArguments = parsed.Values("argumentlist").Concat(parsed.Positional.Skip(named.Count > 0 ? 0 : 1));
        string line = string.Join(' ', [executable, .. processArguments.Select(argument => argument.Replace(',', ' '))]);
        return new(line, ShellDialect.PowerShell, SameShell: false, null);
    }

    private static int IndexOf(IReadOnlyList<string> arguments, Func<string, bool> predicate)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            if (predicate(arguments[index]))
            {
                return index;
            }
        }

        return -1;
    }
}
