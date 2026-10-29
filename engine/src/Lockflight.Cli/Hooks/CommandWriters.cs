namespace Lockflight.Cli.Hooks;

/// <summary>Routes a simple command to the parser of the common writer it names; any other program writes nothing the guard sees.</summary>
internal static class CommandWriters
{
    private static readonly HashSet<string> OtherWriters = ["git", "dotnet"];

    public static CommandWrites Of(ShellCommand command, ShellDialect dialect)
    {
        string program = command.Program;
        IReadOnlyList<string> arguments = command.Arguments;
        if (PowerShellWriteTargets.Cmdlet(program, dialect) is string cmdlet)
        {
            return PowerShellWriteTargets.Of(cmdlet, arguments);
        }

        return program switch
        {
            "git" => GitWriteTargets.Of(arguments),
            "dotnet" => DotnetFormatTargets.Of(arguments),
            _ when CopyCommandTargets.Programs.Contains(program) => CopyCommandTargets.Of(program, arguments),
            _ when InPlaceEditTargets.Programs.Contains(program) => InPlaceEditTargets.Of(program, arguments),
            _ when FileCommandTargets.Programs.Contains(program) => FileCommandTargets.Of(program, arguments),
            _ => CommandWrites.None,
        };
    }

    /// <summary>Whether the program is one the guard knows to write files given suitable arguments.</summary>
    public static bool IsWriter(string program, ShellDialect dialect) =>
        PowerShellWriteTargets.Cmdlet(program, dialect) is not null
        || OtherWriters.Contains(program)
        || CopyCommandTargets.Programs.Contains(program)
        || InPlaceEditTargets.Programs.Contains(program)
        || FileCommandTargets.Programs.Contains(program);
}
