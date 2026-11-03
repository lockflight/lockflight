using System.Text.RegularExpressions;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// The folder a shell command line has moved to with literal <c>cd</c>, <c>pushd</c>, <c>popd</c> and their PowerShell forms
/// (<c>Set-Location</c>, <c>sl</c>, <c>chdir</c>, <c>Push-Location</c>, <c>Pop-Location</c>), as a path for the session's folder
/// to resolve; a relative write target after a move is judged both as written and in it, so a move never hides a write. A move to a folder the guard cannot name (a variable other
/// than <c>$HOME</c>, <c>-</c>, a pattern, another provider, <c>pushd</c> with no folder or a stack offset, <c>popd</c> past the
/// moves it saw) leaves the folder as it was.
/// </summary>
internal sealed partial class WorkingFolder
{
    private static readonly HashSet<string> PosixMoves = ["cd", "chdir"];
    private static readonly HashSet<string> PowerShellMoves = ["set-location", "sl", "cd", "chdir"];
    private static readonly HashSet<string> Pushes = ["pushd", "push-location"];
    private static readonly HashSet<string> Pops = ["popd", "pop-location"];

    private string? path;
    private List<string?> pushed = [];

    /// <summary>The target as written and, when it is relative and the folder moved, below the folder moved to.</summary>
    public IEnumerable<string> Locate(string target) => Moved(target) is string moved ? [target, moved] : [target];

    private string? Moved(string target) =>
        path is null || target.Length == 0 || IsRooted(target) ? null : $"{path.TrimEnd('/', '\\')}/{target}";

    /// <summary>Whether the command changes the folder (it is then followed, and writes nothing).</summary>
    public bool Follow(ShellCommand command, ShellDialect dialect)
    {
        string program = command.Program;
        if (Pops.Contains(program))
        {
            if (command.Arguments.Count == 0 && pushed.Count > 0)
            {
                path = pushed[^1];
                pushed.RemoveAt(pushed.Count - 1);
            }

            return true;
        }

        bool push = Pushes.Contains(program);
        if (!push && !(dialect == ShellDialect.PowerShell ? PowerShellMoves : PosixMoves).Contains(program))
        {
            return false;
        }

        string? target = dialect == ShellDialect.PowerShell ? PowerShellTarget(command.Arguments) : PosixTarget(command.Arguments, push);
        if (target is not null && Nameable(target, dialect))
        {
            if (push)
            {
                pushed.Add(path);
            }

            path = Moved(target) ?? target;
        }

        return true;
    }

    /// <summary>The folder and the folders pushed before it, to come back to when a subshell, substitution or nested shell ends.</summary>
    public State Save() => new(path, [.. pushed]);

    public void Restore(State state)
    {
        path = state.Path;
        pushed = [.. state.Pushed];
    }

    /// <summary>The operand after the options; <c>cd</c> alone goes home, <c>pushd</c> alone swaps the stack (not followed).</summary>
    private static string? PosixTarget(IReadOnlyList<string> arguments, bool push)
    {
        int index = 0;
        while (index < arguments.Count && arguments[index].Length > 1 && arguments[index][0] == '-' && arguments[index] != ShellArguments.EndOfOptions)
        {
            index++;
        }

        index += index < arguments.Count && arguments[index] == ShellArguments.EndOfOptions ? 1 : 0;
        if (index == arguments.Count)
        {
            return push ? null : "~";
        }

        string target = arguments[index];
        return push && target.Length > 1 && target[0] is '+' or '-' && target[1..].All(char.IsAsciiDigit) ? null : target;
    }

    /// <summary>The <c>-Path</c> or <c>-LiteralPath</c> value (any abbreviation), or the first positional argument.</summary>
    private static string? PowerShellTarget(IReadOnlyList<string> arguments)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index].ToLowerInvariant();
            if (argument.Length < 2 || argument[0] != '-')
            {
                return arguments[index];
            }

            if ("-passthru".StartsWith(argument, StringComparison.Ordinal))
            {
                continue;
            }

            // -StackName takes a value that is no folder; -Path and -LiteralPath (and their abbreviations) take the folder.
            bool stackName = "-stackname".StartsWith(argument, StringComparison.Ordinal);
            if (!stackName)
            {
                return index + 1 < arguments.Count ? arguments[index + 1] : null;
            }

            index++;
        }

        return null;
    }

    /// <summary>A folder the guard can name: no variable but a leading <c>$HOME</c>, no pattern, not <c>-</c>, no other provider.</summary>
    private static bool Nameable(string target, ShellDialect dialect)
    {
        string rest = LeadingHome().Replace(target, string.Empty, 1);
        return target != "-"
            && !rest.Contains('$', StringComparison.Ordinal)
            && !rest.Contains('`', StringComparison.Ordinal)
            && !PathWildcardsIn(rest)
            && (dialect != ShellDialect.PowerShell || !rest.Contains(':', StringComparison.Ordinal) || DriveRoot().IsMatch(rest));
    }

    private static bool PathWildcardsIn(string target) => target.IndexOfAny(['*', '?', '[']) >= 0;

    private static bool IsRooted(string target) => target[0] is '/' or '\\' or '~' or '$' || DriveRoot().IsMatch(target);

    [GeneratedRegex(@"^[A-Za-z]:")]
    private static partial Regex DriveRoot();

    [GeneratedRegex(@"^\$(HOME|\{HOME\})(?=$|[/\\])")]
    private static partial Regex LeadingHome();

    /// <summary>A saved folder and the folders pushed before it.</summary>
    internal sealed record State(string? Path, IReadOnlyList<string?> Pushed);
}
