namespace Lockflight.Cli.Processes;

/// <summary>
/// The processes, in this run of the engine, whose output stayed open after they exited (something they started still held it), so
/// the runner stopped waiting for it. The CI judge reports them.
/// </summary>
internal static class HeldOutputs
{
    private static readonly List<string> Commands = [];

    public static void Record(string command)
    {
        lock (Commands)
        {
            Commands.Add(command);
        }
    }

    /// <summary>The commands in the order their output was given up, each as its program and first argument.</summary>
    public static IReadOnlyList<string> Snapshot()
    {
        lock (Commands)
        {
            return [.. Commands];
        }
    }
}
