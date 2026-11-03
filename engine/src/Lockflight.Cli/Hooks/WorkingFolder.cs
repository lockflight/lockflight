using System.Text.RegularExpressions;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// The folder a shell command line has moved to with literal <c>cd</c>, <c>pushd</c>, <c>popd</c> and their PowerShell forms
/// (<c>Set-Location</c>, <c>sl</c>, <c>chdir</c>, <c>Push-Location</c>, <c>Pop-Location</c>), as a path for the session's
/// folder to resolve; a relative write target after a move is judged both as written and in it, so a move never hides a write.
/// A move to a folder the guard cannot name (<c>cd</c> alone, a variable other than <c>$HOME</c>, <c>-</c>, a pattern, a
/// drive's current folder, another provider, <c>pushd</c> without a folder or with a stack offset) leaves the folder as it
/// was; such a <c>pushd</c> is still remembered, so the <c>popd</c> after it comes back to the folder it left.
/// </summary>
internal sealed partial class WorkingFolder
{
    private static readonly HashSet<string> PosixMoves = ["cd", "chdir"];
    private static readonly HashSet<string> PowerShellMoves = ["set-location", "sl", "cd", "chdir"];
    private static readonly HashSet<string> Pushes = ["pushd", "push-location"];
    private static readonly HashSet<string> Pops = ["popd", "pop-location"];

    /// <summary>PowerShell parameters that name the folder, each with its shortest unambiguous prefix.</summary>
    private static readonly (string Name, int Shortest)[] FolderParameters = [("-path", 4), ("-literalpath", 2), ("-lp", 3), ("-pspath", 3)];

    /// <summary>PowerShell parameters that take a value that is no folder (the common ones and <c>-StackName</c>).</summary>
    private static readonly string[] ValueParameters =
    [
        "-stackname", "-erroraction", "-warningaction", "-informationaction", "-progressaction", "-errorvariable", "-warningvariable",
        "-informationvariable", "-outvariable", "-outbuffer", "-pipelinevariable",
    ];

    /// <summary>The common parameters' aliases that take a value.</summary>
    private static readonly HashSet<string> ValueAliases = ["-ea", "-wa", "-infa", "-proga", "-ev", "-wv", "-iv", "-ov", "-ob", "-pv"];

    private string? path;
    private List<string?> pushed = [];

    /// <summary>The target as written and, when it is relative and the folder moved, below the folder moved to.</summary>
    public IEnumerable<string> Locate(string target) => Moved(target) is string moved ? [target, moved] : [target];

    /// <summary>Whether the command changes the folder (it is then followed, and writes nothing).</summary>
    public bool Follow(ShellCommand command, ShellDialect dialect)
    {
        string program = command.Program;
        if (Pops.Contains(program))
        {
            bool passThruOnly = command.Arguments.All(argument => IsPassThru(argument.ToLowerInvariant()));
            if (passThruOnly && pushed.Count > 0)
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
        if (push)
        {
            pushed.Add(path);
        }

        if (target is not null && Nameable(target, dialect))
        {
            path = Moved(target) ?? target;
        }

        return true;
    }

    /// <summary>The folder and the folders pushed before it, to come back to when a subshell, substitution or child shell ends.</summary>
    public State Save() => new(path, [.. pushed]);

    public void Restore(State state)
    {
        path = state.Path;
        pushed = [.. state.Pushed];
    }

    /// <summary>The operand after the options; null for <c>cd</c> alone and for <c>pushd</c> alone or with a stack offset.</summary>
    private static string? PosixTarget(IReadOnlyList<string> arguments, bool push)
    {
        int index = 0;
        while (index < arguments.Count && arguments[index].Length > 1 && arguments[index][0] == '-'
            && arguments[index] != ShellArguments.EndOfOptions)
        {
            index++;
        }

        index += index < arguments.Count && arguments[index] == ShellArguments.EndOfOptions ? 1 : 0;
        if (index == arguments.Count)
        {
            return null;
        }

        string target = arguments[index];
        bool stackOffset = target.Length > 1 && target[0] is '+' or '-' && target[1..].All(char.IsAsciiDigit);
        return push && stackOffset ? null : target;
    }

    /// <summary>
    /// The <c>-Path</c>, <c>-LiteralPath</c> or <c>-PSPath</c> value (abbreviated, or joined with a colon), or the first
    /// positional argument; switches take no value, and the common parameters' values are skipped.
    /// </summary>
    private static string? PowerShellTarget(IReadOnlyList<string> arguments)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument.Length < 2 || argument[0] != '-')
            {
                return argument;
            }

            int colon = argument.IndexOf(':', StringComparison.Ordinal);
            string name = (colon < 0 ? argument : argument[..colon]).ToLowerInvariant();
            if (FolderParameters.Any(parameter => name.Length >= parameter.Shortest && parameter.Name.StartsWith(name, StringComparison.Ordinal)))
            {
                return colon >= 0 ? argument[(colon + 1)..] : index + 1 < arguments.Count ? arguments[index + 1] : null;
            }

            bool takesValue = ValueAliases.Contains(name)
                || (name.Length > 2 && ValueParameters.Any(parameter => parameter.StartsWith(name, StringComparison.Ordinal)));
            index += takesValue && colon < 0 ? 1 : 0;
        }

        return null;
    }

    private static bool IsPassThru(string argument) => argument.Length >= 4 && "-passthru".StartsWith(argument, StringComparison.Ordinal);

    /// <summary>A folder the guard can name: no variable but a leading <c>$HOME</c>, no pattern, not <c>-</c>, no other provider.</summary>
    private static bool Nameable(string target, ShellDialect dialect)
    {
        string rest = LeadingHome().Replace(target, string.Empty, 1);
        bool otherProvider = dialect == ShellDialect.PowerShell && rest.Contains(':', StringComparison.Ordinal) && !DriveRoot().IsMatch(rest);
        return target.Length > 0
            && target != "-"
            && !DriveOnly().IsMatch(target)
            && !rest.Contains('$', StringComparison.Ordinal)
            && !rest.Contains('`', StringComparison.Ordinal)
            && rest.IndexOfAny(['*', '?', '[']) < 0
            && !otherProvider;
    }

    private static bool IsRooted(string target) => target[0] is '/' or '\\' or '~' or '$' || DriveRoot().IsMatch(target);

    private string? Moved(string target) =>
        path is null || target.Length == 0 || IsRooted(target) ? null : $"{path.TrimEnd('/', '\\')}/{target}";

    [GeneratedRegex(@"^[A-Za-z]:")]
    private static partial Regex DriveRoot();

    [GeneratedRegex(@"^[A-Za-z]:$")]
    private static partial Regex DriveOnly();

    [GeneratedRegex(@"^\$(HOME|\{HOME\})(?=$|[/\\])")]
    private static partial Regex LeadingHome();

    /// <summary>A saved folder and the folders pushed before it.</summary>
    internal sealed record State(string? Path, IReadOnlyList<string?> Pushed);
}
