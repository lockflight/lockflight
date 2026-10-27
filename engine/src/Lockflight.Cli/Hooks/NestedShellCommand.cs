namespace Lockflight.Cli.Hooks;

/// <summary>
/// A command string another program runs: <c>bash -c</c> (in any option spelling, with its positional parameters bound), the
/// commands a POSIX shell without a script reads on stdin (a here-document, a here-string, an <c>echo</c> piped into it),
/// <c>eval</c>, <c>flock -c</c>, <c>pwsh -Command</c>, <c>Invoke-Expression</c>, <c>cmd /c</c> and <c>Start-Process</c>. <see cref="SameShell"/> is
/// set when it runs in the current shell (a cd inside it persists); <see cref="Hidden"/> names one whose text cannot be read
/// (<c>-EncodedCommand</c>, <c>curl … | sh</c>, <c>bash &lt;(…)</c>).
/// </summary>
internal sealed record NestedShellCommand(string Command, ShellDialect Dialect, bool SameShell, string? Hidden)
{
    private static readonly HashSet<string> PosixShells = ["bash", "sh", "zsh", "dash", "ksh"];

    /// <summary>Long options of the POSIX shells that take the next word as their value.</summary>
    private static readonly HashSet<string> ShellLongOptionsWithValue = ["--rcfile", "--init-file"];
    private static readonly HashSet<string> PowerShells = ["pwsh", "powershell"];
    private static readonly HashSet<string> Evaluators = ["eval", "invoke-expression", "iex"];
    private static readonly HashSet<string> ProcessStarters = ["start-process", "saps", "start"];

    public static NestedShellCommand? Find(ShellCommand command, StandardInput input)
    {
        IReadOnlyList<string> arguments = command.Arguments;
        string program = command.Program;
        if (Evaluators.Contains(program) && arguments.Count > 0)
        {
            return new(string.Join(' ', arguments), program == "eval" ? ShellDialect.Posix : ShellDialect.PowerShell, SameShell: true, null);
        }

        if (PosixShells.Contains(program))
        {
            return PosixShell(program, arguments, input);
        }

        if (program == "flock")
        {
            // flock [options] FILE -c STRING runs the string with sh -c.
            int flag = IndexOf(arguments, argument => argument is "-c" or "--command");
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

    /// <summary>
    /// <c>sh [options] -c STRING [$0 [$1 …]]</c> runs the string; without <c>-c</c> the first operand is a script file (invisible
    /// like any script) unless <c>-s</c> is given or there is none: the shell then runs what it reads on stdin.
    /// </summary>
    private static NestedShellCommand? PosixShell(string program, IReadOnlyList<string> arguments, StandardInput input)
    {
        (bool commandString, bool readsInput, int operandsStart) = ReadShellOptions(arguments);
        List<string> operands = [.. arguments.Skip(operandsStart)];
        if (commandString)
        {
            return operands.Count > 0 ? new(ShellPositionalParameters.Bind(operands[0], operands[1..]), ShellDialect.Posix, SameShell: false, null) : null;
        }

        if (!readsInput && operands.Count > 0)
        {
            bool fromSubstitution = operands[0].Contains(ShellTokenizer.SubstitutionPlaceholder, StringComparison.Ordinal);
            return fromSubstitution ? new(string.Empty, ShellDialect.Posix, SameShell: false, $"{program} <(…)") : null;
        }

        if (input.Hidden is string source)
        {
            return new(string.Empty, ShellDialect.Posix, SameShell: false, $"{source} | {program}");
        }

        return input.Text is string text ? new(ShellPositionalParameters.Bind(text, [program, .. operands]), ShellDialect.Posix, SameShell: false, null) : null;
    }

    /// <summary>
    /// Reads the options before the operands: single-dash clusters (<c>-c</c> and <c>-s</c> anywhere in one), <c>+</c> clusters,
    /// the value taken by each <c>o</c>/<c>O</c> in a cluster, long options; <c>--</c> or <c>-</c> ends them.
    /// </summary>
    private static (bool CommandString, bool ReadsInput, int OperandsStart) ReadShellOptions(IReadOnlyList<string> arguments)
    {
        bool commandString = false;
        bool readsInput = false;
        int index = 0;
        while (index < arguments.Count)
        {
            string argument = arguments[index];
            if (argument is ShellArguments.EndOfOptions or "-")
            {
                index++;
                break;
            }

            if (argument.StartsWith(ShellArguments.EndOfOptions, StringComparison.Ordinal))
            {
                index += ShellLongOptionsWithValue.Contains(argument) ? 2 : 1;
                continue;
            }

            if (argument.Length < 2 || argument[0] is not ('-' or '+'))
            {
                break;
            }

            string cluster = argument[1..];
            commandString |= argument[0] == '-' && cluster.Contains('c', StringComparison.Ordinal);
            readsInput |= argument[0] == '-' && cluster.Contains('s', StringComparison.Ordinal);
            index += 1 + cluster.Count(flag => flag is 'o' or 'O');
        }

        return (commandString, readsInput, Math.Min(index, arguments.Count));
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
