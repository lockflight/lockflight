namespace Lockflight.Cli.Hosting;

/// <summary>
/// Every command this engine runs, with its options: what <c>docs check</c> compares documented command lines with. A test runs
/// each entry against the real parser and the help text, so the catalog cannot drift from the commands themselves.
/// </summary>
internal static class CommandCatalog
{
    /// <summary>Options every command accepts: they print the help.</summary>
    public static IReadOnlyList<string> GlobalOptions { get; } = ["--help", "-h", "--version"];

    public static IReadOnlyList<CatalogedCommand> All { get; } =
    [
        new(["version"], [], TakesArguments: false),
        new(["link"], [], TakesArguments: false),
        new(["init"], ["--profile", "--ci", "--ci-platform", "--no-link", "--no-sandbox"], TakesArguments: false),
        new(["doctor"], ["--canaries", "--canaries=full"], TakesArguments: false),
        new(["statusline"], [], TakesArguments: false),
        new(["update"], ["--source", "--project"], TakesArguments: false),
        new(["docs", "check"], [], TakesArguments: false),
        new(["rules", "audit"], [], TakesArguments: false),
        new(["hook"], [], TakesArguments: true),
        new(["baseline", "build"], ["--path"], TakesArguments: false),
        new(["baseline", "tests"], ["--path"], TakesArguments: false),
        new(["baseline", "deps"], ["--path"], TakesArguments: false),
        new(["gate", "run"], ["--profile", "--path", "--unit", "--base", "--result"], TakesArguments: false),
        new(["phase", "check"], ["--feature"], TakesArguments: true),
        new(["redgreen", "start"], ["--feature"], TakesArguments: false),
        new(["redgreen", "red"], ["--feature", "--verbose"], TakesArguments: false),
        new(["redgreen", "green"], ["--feature"], TakesArguments: false),
        new(["redgreen", "status"], ["--feature"], TakesArguments: false),
        new(["redgreen", "reset"], ["--feature"], TakesArguments: false),
        new(["redgreen", "amend"], ["--feature", "--reason"], TakesArguments: true),
        new(["review"], ["--feature", "--base", "--model", "--budget"], TakesArguments: false),
        new(["review", "status"], ["--feature"], TakesArguments: false),
        new(["review", "dismiss"], ["--reason", "--feature"], TakesArguments: true),
        new(["ledger", "retro"], ["--days", "--min"], TakesArguments: false),
        new(["propose"], ["--from", "--delete"], TakesArguments: true),
        new(["trace"], ["--feature"], TakesArguments: false),
        new(["approve", "spec"], ["--feature", "--key", "--sign"], TakesArguments: false),
        new(["approve", "tasks"], ["--feature", "--key", "--sign"], TakesArguments: false),
        new(["approve", "verify"], ["--feature", "--base", "--allowed-signers", "--require"], TakesArguments: false),
        new(["workspace", "sync"], ["--check"], TakesArguments: false),
        new(["adopt", "plan"], [], TakesArguments: false),
        new(["adopt", "apply"], ["--speckit-preset", "--skip-speckit", "--no-sandbox"], TakesArguments: false),
        new(["adopt", "undo"], ["--backup", "--force"], TakesArguments: false),
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
