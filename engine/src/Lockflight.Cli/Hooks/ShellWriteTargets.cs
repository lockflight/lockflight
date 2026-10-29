using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Finds the common writes in a shell command line, to catch accidents: output redirections (<c>&gt;</c>, <c>&gt;&gt;</c>,
/// <c>2&gt;</c>, <c>&amp;&gt;</c>), the writers known to <see cref="CommandWriters"/>, the commands inside substitutions and subshells,
/// and the literal command string of one nested shell (<c>bash -c '…'</c>, <c>pwsh -Command '…'</c>, <c>eval '…'</c>). Nothing else
/// is followed: a <c>cd</c> before a relative path, variables other than <c>$HOME</c> and <c>$PWD</c>, what <c>xargs</c> reads,
/// a shell inside a nested shell, scripts, interpreters and build tools are not seen, and what is not seen is allowed. A guardrail
/// against accidents, not a boundary.
/// </summary>
internal sealed class ShellWriteTargets
{
    private static readonly HashSet<string> PosixShells = ["bash", "sh", "zsh", "dash", "ksh"];
    private static readonly HashSet<string> PowerShells = ["pwsh", "powershell"];
    private static readonly HashSet<string> PowerShellEvaluators = ["invoke-expression", "iex"];

    private readonly List<string> files = [];
    private readonly List<string> trees = [];
    private readonly List<string> sourceTrees = [];
    private string? humanOnly;
    private string? humanOnlyIgnoringHelp;

    public static ShellWrites Find(string command, ShellDialect dialect)
    {
        ShellWriteTargets targets = new();
        targets.Scan(command, dialect, nested: false);
        return new ShellWrites(Distinct(targets.files), Distinct(targets.trees), Distinct(targets.sourceTrees))
        {
            HumanOnly = targets.humanOnly,
            HumanOnlyIgnoringHelp = targets.humanOnlyIgnoringHelp,
        };
    }

    private void Scan(string command, ShellDialect dialect, bool nested)
    {
        List<string> words = [];
        Stack<OpenGroup> groups = new();
        ShellTokenKind? redirect = null;
        foreach (ShellToken token in ShellTokenizer.Tokenize(command, dialect))
        {
            ShellTokenKind? pending = redirect;
            redirect = token.Kind is ShellTokenKind.WriteRedirect or ShellTokenKind.InputRedirect ? token.Kind : null;
            switch (token.Kind)
            {
                case ShellTokenKind.Word when pending == ShellTokenKind.WriteRedirect:
                    files.Add(token.Text);
                    break;
                case ShellTokenKind.Word when pending is null:
                    words.Add(token.Text);
                    break;
                case ShellTokenKind.Separator:
                    Run(words, dialect, nested);
                    words = [];
                    break;
                case ShellTokenKind.SubshellOpen:
                    // A subshell is a command of its own, and so were the words before it.
                    Run(words, dialect, nested);
                    groups.Push(new OpenGroup([], null));
                    words = [];
                    break;
                case ShellTokenKind.SubstitutionOpen or ShellTokenKind.GroupOpen:
                    // The enclosing command continues after it: its output stands for a word, or for a redirection's target.
                    groups.Push(new OpenGroup(words, pending));
                    words = [];
                    break;
                case ShellTokenKind.Close:
                    Run(words, dialect, nested);
                    words = [];
                    if (groups.TryPop(out OpenGroup? group))
                    {
                        words = group.Words;
                        redirect = group.Redirect;
                    }

                    break;
            }
        }

        Run(words, dialect, nested);
    }

    private void Run(List<string> words, ShellDialect dialect, bool nested)
    {
        if (ShellCommand.Parse(words) is not ShellCommand command)
        {
            return;
        }

        humanOnly ??= HumanOnlyCommands.FindInWords(words, honorHelp: true);
        humanOnlyIgnoringHelp ??= HumanOnlyCommands.FindInWords(words, honorHelp: false);
        if (NestedCommand(command, dialect) is (string text, ShellDialect nestedDialect))
        {
            if (!nested)
            {
                Scan(text, nestedDialect, nested: true);
            }

            return;
        }

        CommandWrites writes = CommandWriters.Of(command, dialect);
        files.AddRange(writes.Files);
        trees.AddRange(writes.Trees);
        sourceTrees.AddRange(writes.SourceTrees);
    }

    /// <summary>
    /// The command string a shell or an evaluator runs: <c>sh -c STRING</c> (<c>c</c> in any option cluster), <c>pwsh -Command …</c>,
    /// <c>eval …</c>, <c>Invoke-Expression …</c>; null for anything else (a script file, commands read on stdin).
    /// </summary>
    private static (string Text, ShellDialect Dialect)? NestedCommand(ShellCommand command, ShellDialect dialect)
    {
        IReadOnlyList<string> arguments = command.Arguments;
        if (arguments.Count == 0)
        {
            return null;
        }

        if (command.Program == "eval")
        {
            return (string.Join(' ', arguments), ShellDialect.Posix);
        }

        if (dialect == ShellDialect.PowerShell && PowerShellEvaluators.Contains(command.Program))
        {
            return (string.Join(' ', arguments), ShellDialect.PowerShell);
        }

        if (PosixShells.Contains(command.Program))
        {
            // Options come first; each o in a cluster (-o pipefail, -eo pipefail) takes the next word as its value.
            bool commandString = false;
            int index = 0;
            while (index < arguments.Count && arguments[index].Length > 1 && arguments[index][0] is '-' or '+' && arguments[index] != ShellArguments.EndOfOptions)
            {
                string option = arguments[index];
                bool shortCluster = option[1] != '-';
                commandString |= shortCluster && option[0] == '-' && option.Contains('c', StringComparison.Ordinal);
                index += 1 + (shortCluster ? option.Count(flag => flag == 'o') : 0);
            }

            index += index < arguments.Count && arguments[index] == ShellArguments.EndOfOptions ? 1 : 0;
            return commandString && index < arguments.Count ? (arguments[index], ShellDialect.Posix) : null;
        }

        if (PowerShells.Contains(command.Program))
        {
            int flag = arguments.ToList().FindIndex(argument => argument.Length > 1 && "-command".StartsWith(argument.ToLowerInvariant(), StringComparison.Ordinal));
            return flag >= 0 && flag + 1 < arguments.Count ? (string.Join(' ', arguments.Skip(flag + 1)), ShellDialect.PowerShell) : null;
        }

        return null;
    }

    private static List<string> Distinct(List<string> paths) => [.. paths.Where(path => path.Length > 0).Distinct(StringComparer.Ordinal)];

    /// <summary>An open substitution, subshell or PowerShell group: the words of the command around it, and the redirection it is the target of.</summary>
    private sealed record OpenGroup(List<string> Words, ShellTokenKind? Redirect);
}
