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
    /// <summary>Command words (subcommand, optional sub-subcommand) reserved for a person.</summary>
    private static readonly string[][] Reserved = [["baseline"], ["approve"], ["redgreen", "reset"], ["adopt", "apply"], ["adopt", "undo"], ["update"]];

    public static bool IsReserved(IReadOnlyList<string> args) =>
        Reserved.Any(words => args.Count >= words.Length && words.Select((word, index) => args[index] == word).All(match => match))
        && !(args.Count >= 2 && args[0] == "approve" && args[1] == "verify");

    public static bool RunsUnderAgent(EnvironmentReader environment) => environment.Get(EnvironmentVariables.ClaudeCode) == "1";

    /// <summary>Finds a reserved command in a shell command line: <c>lockflight …</c>, <c>dotnet lockflight …</c> or a path to the binary.</summary>
    public static string? FindIn(string command, ShellDialect dialect)
    {
        List<ShellToken> tokens = ShellTokenizer.Tokenize(command, dialect);
        for (int index = 0; index < tokens.Count; index++)
        {
            if (!tokens[index].IsWord || !IsEngine(tokens[index].Text))
            {
                continue;
            }

            List<string> words = [.. tokens.Skip(index + 1).TakeWhile(token => token.IsWord).Select(token => token.Text).Where(word => !word.StartsWith('-'))];
            if (IsReserved(words))
            {
                return string.Join(' ', words.Take(2));
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
