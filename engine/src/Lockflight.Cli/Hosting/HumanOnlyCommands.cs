using Lockflight.Cli.Hooks;

namespace Lockflight.Cli.Hosting;

/// <summary>
/// Engine commands that change what the gates accept or record a human decision. Two guardrails keep them away from the agent: the
/// PreToolUse guard blocks shell commands that invoke them, and the engine refuses to run them inside a Claude Code session
/// (<c>CLAUDECODE</c> is set in every shell the agent starts). Both are guardrails, not boundaries: a determined agent can strip the
/// variable or hide the call; signed approvals checked in CI are the boundary.
/// </summary>
internal static class HumanOnlyCommands
{
    /// <summary>
    /// Where a person runs a reserved command. A <c>!</c> command in Claude Code runs inside the session (with <c>CLAUDECODE</c> set),
    /// so the engine refuses it there too.
    /// </summary>
    public const string WhereToRun = "in a separate terminal window (not with `!` in Claude Code: that runs inside the session and is refused)";

    /// <summary>A command and its verb (<c>approve spec</c>, <c>redgreen reset</c>): what a refusal names.</summary>
    private const int CommandWords = 2;

    /// <summary>Command words (subcommand, optional sub-subcommand) reserved for a person.</summary>
    private static readonly string[][] Reserved = [["baseline"], ["approve"], ["redgreen", "reset"], ["redgreen", "amend"], ["review", "dismiss"], ["adopt", "apply"], ["adopt", "undo"], ["update"]];

    public static bool IsReserved(IReadOnlyList<string> args) =>
        Reserved.Any(words => args.Count >= words.Length && words.Select((word, index) => args[index] == word).All(match => match))
        && !(args.Count >= 2 && args[0] == "approve" && args[1] == "verify");

    public static bool RunsUnderAgent(EnvironmentReader environment) => environment.Get(EnvironmentVariables.ClaudeCode) == "1";

    /// <summary>What the agent is told when a reserved command is due: who runs it, with which command line, and where.</summary>
    public static string AskTheUser(string command) =>
        $"ask the user to run '{EngineCommand.Name} {command}' from the project root {WhereToRun}";

    /// <summary>
    /// The engine's refusal of a reserved command run inside a Claude Code session, ending with the exact command line a person
    /// copies into another terminal: <c>cd</c> to <paramref name="directory"/>, then <paramref name="engine"/> with the same arguments.
    /// </summary>
    public static string Refusal(IReadOnlyList<string> args, string directory, string engine) =>
        $"lockflight: '{string.Join(' ', args.Take(CommandWords))}' records a human decision and does not run inside a Claude Code session. "
        + $"Run it {WhereToRun}:\n  cd {ShellQuoting.Quote(directory)} && {engine} {string.Join(' ', args.Select(ShellQuoting.QuoteIfNeeded))}";

    /// <summary>
    /// Finds a reserved command in a shell command line: <c>lockflight …</c>, <c>dotnet lockflight …</c> or a path to the binary,
    /// also inside the command strings it runs (<c>bash -c "…"</c>, <c>eval</c>, a here-document fed to <c>sh</c>).
    /// </summary>
    public static string? FindIn(string command, ShellDialect dialect)
    {
        List<string> words = [];
        foreach (ShellToken token in ShellTokenizer.Tokenize(command, dialect).Append(new ShellToken(ShellTokenKind.Separator, ";")))
        {
            if (token.IsWord)
            {
                words.Add(token.Text);
            }
            else if (FindInWords(words) is string found)
            {
                return found;
            }
            else
            {
                words.Clear();
            }
        }

        return ShellWriteTargets.Find(command, dialect).HumanOnly;
    }

    /// <summary>The reserved command (<c>approve spec</c>) the words of one simple command start, or null.</summary>
    public static string? FindInWords(IReadOnlyList<string> words)
    {
        for (int index = 0; index < words.Count; index++)
        {
            if (!IsEngine(words[index]))
            {
                continue;
            }

            List<string> arguments = [.. words.Skip(index + 1).Where(word => !word.StartsWith('-'))];
            if (IsReserved(arguments))
            {
                return string.Join(' ', arguments.Take(CommandWords));
            }
        }

        return null;
    }

    private static bool IsEngine(string token)
    {
        string name = Path.GetFileName(token.Replace('\\', '/'));
        return name.Equals("lockflight", StringComparison.OrdinalIgnoreCase) || name.Equals("lockflight.exe", StringComparison.OrdinalIgnoreCase);
    }
}
