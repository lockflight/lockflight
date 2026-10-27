namespace Lockflight.Cli.Hooks;

/// <summary>Routes a simple command to the parser of the writer it names.</summary>
internal static class CommandWriters
{
    private static readonly HashSet<string> OtherWriters = ["git", "dotnet"];

    public static CommandWrites Of(ShellCommand command, ShellDialect dialect)
    {
        string program = command.Program;
        IReadOnlyList<string> arguments = command.Arguments;
        if (command.ArgumentsFromInput)
        {
            // xargs: the operands come from stdin, so a writer behind it has targets the command line does not show.
            return IsWriter(command, dialect) ? new CommandWrites([], [], [], $"xargs {program}") : CommandWrites.None;
        }

        if (PowerShellWriteTargets.Cmdlet(program, dialect) is string cmdlet)
        {
            return PowerShellWriteTargets.Of(cmdlet, arguments, command.PipedInput && dialect == ShellDialect.PowerShell);
        }

        return program switch
        {
            "git" => GitWriteTargets.Of(arguments),
            "dotnet" => DotnetFormatTargets.Of(arguments),
            _ when CopyCommandTargets.Programs.Contains(program) => CopyCommandTargets.Of(program, arguments),
            _ when InPlaceEditTargets.Programs.Contains(program) => InPlaceEditTargets.Of(program, arguments),
            _ when FilterCommandTargets.Programs.Contains(program) => FilterCommandTargets.Of(program, arguments),
            _ when WindowsCopyCommandTargets.Programs.Contains(program) => WindowsCopyCommandTargets.Of(program, arguments),
            _ when ExtractCommandTargets.Programs.Contains(program) => ExtractCommandTargets.Of(program, arguments),
            _ when FileCommandTargets.Programs.Contains(program) => FileCommandTargets.Of(program, arguments, words => Writes(words, dialect)),
            _ => CommandWrites.None,
        };
    }

    /// <summary>Whether a command another program runs (<c>find -exec …</c>) writes: a writer, or a shell whose command string writes.</summary>
    private static bool Writes(IReadOnlyList<string> words, ShellDialect dialect)
    {
        if (ShellCommand.Parse(words) is not ShellCommand command)
        {
            return false;
        }

        if (NestedShellCommand.Find(command, StandardInput.None) is NestedShellCommand nested)
        {
            return nested.Hidden is not null || ShellWriteTargets.Find(nested.Command, nested.Dialect).HasTargets;
        }

        return IsWriter(command, dialect);
    }

    /// <summary>
    /// Whether the command is one the scanner knows to write files given suitable arguments; a filter (<c>sort</c>, <c>awk</c>, …)
    /// only with the option that makes it write (<c>-o</c>, <c>-i inplace</c>, …).
    /// </summary>
    public static bool IsWriter(ShellCommand command, ShellDialect dialect) =>
        FilterCommandTargets.Programs.Contains(command.Program)
            ? FilterCommandTargets.Of(command.Program, command.Arguments).Files.Count > 0
            : IsWriter(command.Program, dialect);

    /// <summary>Whether the program is one the scanner knows to write files given suitable arguments.</summary>
    private static bool IsWriter(string program, ShellDialect dialect) =>
        PowerShellWriteTargets.Cmdlet(program, dialect) is not null
        || OtherWriters.Contains(program)
        || CopyCommandTargets.Programs.Contains(program)
        || InPlaceEditTargets.Programs.Contains(program)
        || WindowsCopyCommandTargets.Programs.Contains(program)
        || ExtractCommandTargets.Programs.Contains(program)
        || (FileCommandTargets.Programs.Contains(program) && program != "find");
}
