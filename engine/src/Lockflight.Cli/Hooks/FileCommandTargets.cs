namespace Lockflight.Cli.Hooks;

/// <summary>
/// Commands that write or delete the files they name: <c>rm</c> (its operands are also trees: <c>rm -r</c>), <c>unlink</c>,
/// <c>truncate</c>, <c>touch</c> and <c>tee</c>.
/// </summary>
internal static class FileCommandTargets
{
    private static readonly string[] OptionsWithValue = ["-s", "--size", "-r", "--reference", "-t", "-d", "--date"];

    public static IReadOnlySet<string> Programs { get; } = new HashSet<string>(["rm", "unlink", "truncate", "touch", "tee"]);

    public static CommandWrites Of(string program, IReadOnlyList<string> arguments) => program == "rm"
        ? CommandWrites.OfFilesAndTrees(ShellArguments.Operands(arguments, []))
        : CommandWrites.OfFiles(ShellArguments.Operands(arguments, OptionsWithValue));
}
