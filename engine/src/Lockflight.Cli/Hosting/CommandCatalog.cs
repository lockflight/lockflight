namespace Lockflight.Cli.Hosting;

/// <summary>
/// Every command this engine runs, with its options: what <c>docs check</c> compares documented command lines with. It is the
/// listed part of <see cref="CommandRegistry"/>, and a test runs each entry against the real parser and the help text.
/// </summary>
internal static class CommandCatalog
{
    /// <summary>Options every command accepts: they print the help.</summary>
    public static IReadOnlyList<string> GlobalOptions { get; } = ["--help", "-h", "--version"];

    public static IReadOnlyList<CatalogedCommand> All { get; } =
    [
        .. CommandRegistry.All
            .Where(command => command.Listing == CommandListing.Listed)
            .Select(command => new CatalogedCommand(
                command.Words, [.. command.ValueOptions, .. command.FlagOptions], command.Operands is not (CommandOperands.None or CommandOperands.Options))),
    ];

    /// <summary>The command whose words begin <paramref name="words"/>, the longest when several do; null when none does.</summary>
    public static CatalogedCommand? Match(IReadOnlyList<string> words) =>
        All.Where(command => command.Words.Count <= words.Count && command.Words.Select((word, index) => words[index] == word).All(same => same))
            .OrderByDescending(command => command.Words.Count)
            .FirstOrDefault();

    /// <summary>Whether <paramref name="words"/> are the start of some command's words (<c>approve</c> of <c>approve spec</c>).</summary>
    public static bool IsPrefix(IReadOnlyList<string> words) =>
        All.Any(command => command.Words.Count > words.Count && words.Select((word, index) => command.Words[index] == word).All(same => same));
}
