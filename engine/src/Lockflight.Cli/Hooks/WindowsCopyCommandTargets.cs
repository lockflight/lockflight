namespace Lockflight.Cli.Hooks;

/// <summary>
/// The Windows copy programs, with <c>/</c> options: <c>robocopy SRC DST [FILE…]</c> rewrites anything under its destination
/// (<c>/MIR</c> and <c>/PURGE</c> also delete there) and, with <c>/MOV</c> or <c>/MOVE</c>, removes what it copied from under its
/// source; <c>xcopy SRC [DST]</c> writes the destination (the current directory when none is given) as a file or a tree.
/// </summary>
internal static class WindowsCopyCommandTargets
{
    public static IReadOnlySet<string> Programs { get; } = new HashSet<string>(["robocopy", "xcopy"]);

    public static CommandWrites Of(string program, IReadOnlyList<string> arguments)
    {
        List<string> operands = [.. arguments.Where(argument => !argument.StartsWith('/'))];
        if (program == "xcopy")
        {
            string destination = operands.Count > 1 ? operands[1] : ShellArguments.CurrentDirectory;
            return operands.Count == 0 ? CommandWrites.None : CommandWrites.OfFilesAndTrees([destination]);
        }

        if (operands.Count < 2)
        {
            return CommandWrites.None;
        }

        bool moves = arguments.Any(argument => argument.Equals("/MOV", StringComparison.OrdinalIgnoreCase) || argument.Equals("/MOVE", StringComparison.OrdinalIgnoreCase));
        return CommandWrites.OfFilesAndTrees(moves ? [operands[1], operands[0]] : [operands[1]]);
    }
}
