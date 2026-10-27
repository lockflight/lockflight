namespace Lockflight.Cli.Hooks;

/// <summary>
/// The command lines <c>script</c> and <c>watch</c> run, written as one POSIX command string. <c>script</c> also writes its
/// typescript file and log files, which the string writes with redirections so they are checked like any other target.
/// </summary>
internal static class RunnerCommandLines
{
    /// <summary>The short options of BSD and util-linux <c>script</c> that take a value (<c>-t</c> is BSD's interval).</summary>
    private const string ScriptShortWithValue = "cEIOBTmot";
    private const string WatchShortWithValue = "nqs";

    private static readonly HashSet<string> ScriptLongWithValue =
        ["--command", "--echo", "--log-in", "--log-out", "--log-io", "--log-timing", "--logging-format", "--output-limit"];

    private static readonly string[] ScriptCommand = ["-c", "--command"];
    private static readonly string[] ScriptLogs = ["-I", "-O", "-B", "-T", "--log-in", "--log-out", "--log-io", "--log-timing"];
    private static readonly string[] ScriptAppend = ["-a", "--append"];
    private static readonly string[] ScriptPlayback = ["-p"];

    private static readonly HashSet<string> WatchLongWithValue = ["--interval", "--equexit", "--shotsdir"];
    private static readonly string[] WatchExec = ["-x", "--exec"];

    /// <summary>
    /// <c>script [options] [FILE [COMMAND …]]</c> (BSD) or <c>script [options] -c STRING [FILE]</c> (util-linux, options in any
    /// order): FILE is written (appended with <c>-a</c>, only read with BSD's playback <c>-p</c>), and so is each log file.
    /// </summary>
    public static NestedShellCommand? Script(IReadOnlyList<string> arguments)
    {
        RunnerOptions linux = RunnerOptions.Read(arguments, ScriptShortWithValue, ScriptLongWithValue, permute: true);
        bool commandString = linux.Has(ScriptCommand);
        RunnerOptions options = commandString ? linux : RunnerOptions.Read(arguments, ScriptShortWithValue, ScriptLongWithValue, permute: false);
        List<string> lines = [];
        if (options.Operands.Count > 0 && !options.Has(ScriptPlayback))
        {
            lines.Add($": {(options.Has(ScriptAppend) ? ">>" : ">")} {ShellWordQuoting.Word(options.Operands[0])}");
        }

        lines.AddRange(options.Values(ScriptLogs).Select(log => $": >> {ShellWordQuoting.Word(log)}"));
        if (commandString)
        {
            lines.AddRange(options.Values(ScriptCommand));
        }
        else if (options.Operands.Count > 1)
        {
            lines.Add(ShellWordQuoting.Line(options.Operands.Skip(1)));
        }

        return lines.Count > 0 ? new(string.Join('\n', lines), ShellDialect.Posix, SameShell: false, null) : null;
    }

    /// <summary>
    /// <c>watch [options] COMMAND …</c> runs its words joined into one string with <c>sh -c</c>, or as they are with <c>-x</c>.
    /// </summary>
    public static NestedShellCommand? Watch(IReadOnlyList<string> arguments)
    {
        RunnerOptions options = RunnerOptions.Read(arguments, WatchShortWithValue, WatchLongWithValue, permute: false);
        if (options.Operands.Count == 0)
        {
            return null;
        }

        string command = options.Has(WatchExec) ? ShellWordQuoting.Line(options.Operands) : string.Join(' ', options.Operands);
        return new(command, ShellDialect.Posix, SameShell: false, null);
    }
}
