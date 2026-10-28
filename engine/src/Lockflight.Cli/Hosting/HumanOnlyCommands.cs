using System.Text.RegularExpressions;
using Lockflight.Cli.Hooks;

namespace Lockflight.Cli.Hosting;

/// <summary>
/// Engine commands that change what the gates accept or record a human decision. Two guardrails keep them away from the agent: the
/// PreToolUse guard blocks shell commands that invoke them, and the engine refuses to run them inside a Claude Code session
/// (<c>CLAUDECODE</c> is set in every shell the agent starts). Both are guardrails, not boundaries: a determined agent can strip the
/// variable or hide the call; signed approvals checked in CI are the boundary.
/// </summary>
internal static partial class HumanOnlyCommands
{
    /// <summary>
    /// Where a person runs a reserved command. A <c>!</c> command in Claude Code runs inside the session (with <c>CLAUDECODE</c> set),
    /// so the engine refuses it there too.
    /// </summary>
    public const string WhereToRun = $"in a separate terminal window ({NotWithBang})";

    /// <summary>Why a <c>!</c> command in Claude Code is no way to run a reserved command.</summary>
    public const string NotWithBang = "not with `!` in Claude Code: that runs inside the session and is refused";

    /// <summary>A command and its verb (<c>approve spec</c>, <c>redgreen reset</c>): what a refusal names.</summary>
    private const int CommandWords = 2;

    private const string DotnetHost = "dotnet";

    /// <summary>Command words (subcommand, optional sub-subcommand) reserved for a person.</summary>
    private static readonly string[][] Reserved = [["baseline"], ["approve"], ["redgreen", "reset"], ["redgreen", "amend"], ["review", "dismiss"], ["adopt", "apply"], ["adopt", "undo"], ["update"]];

    public static bool IsReserved(IReadOnlyList<string> args) =>
        Reserved.Any(words => args.Count >= words.Length && words.Select((word, index) => args[index] == word).All(match => match))
        && !(args.Count >= 2 && args[0] == "approve" && args[1] == "verify");

    public static bool RunsUnderAgent(EnvironmentReader environment) => environment.Get(EnvironmentVariables.ClaudeCode) == "1";

    /// <summary>
    /// How to hand a terminal-only command line to a person: the separate terminal window first, then the command inside the
    /// sentence, so pasting it after <c>!</c> in Claude Code is not the obvious thing to do. Lower case, to follow "ask the user to".
    /// </summary>
    public static string OpenATerminalAndRun(string commandLine) => $"open a separate terminal window ({NotWithBang}) and run `{commandLine}`";

    /// <summary>
    /// The slash command a person can type in Claude Code instead of <c>lockflight <paramref name="command"/></c> (approve, baseline,
    /// redgreen reset, redgreen amend); null for a command that runs in a terminal only.
    /// </summary>
    public static string? InSession(string command) => command.Split(' ', 3) switch
    {
        ["approve", string phase, ..] when phase is "spec" or "tasks" => $"/lockflight:approve {phase}",
        ["approve", ..] => "/lockflight:approve spec|tasks",
        ["baseline", string kind, ..] when kind is "build" or "tests" or "deps" => $"/lockflight:baseline {kind}",
        ["baseline", ..] => "/lockflight:baseline build|tests|deps",
        ["redgreen", "reset", ..] => "/lockflight:reset",
        ["redgreen", "amend", string rest] => $"/lockflight:amend {rest.Replace("\"", string.Empty, StringComparison.Ordinal)}",
        ["redgreen", "amend"] => "/lockflight:amend <test> --reason <why>",
        _ => null,
    };

    /// <summary>
    /// What the agent is told when a reserved command is due: the session command the person types when there is one, else a
    /// separate terminal window, and only then the command line.
    /// </summary>
    public static string AskTheUser(string command) => $"ask the user to {HandOff(command)}";

    /// <summary>What the person does, as it follows "ask the user to": <c>type /lockflight:… in Claude Code, or open a separate terminal window …</c>.</summary>
    public static string HandOff(string command)
    {
        string terminal = $"{OpenATerminalAndRun($"{EngineCommand.Name} {command}")} from the project root";
        return InSession(command) is string slash ? $"type {slash} in Claude Code, or {terminal}" : terminal;
    }

    /// <summary>
    /// The engine's refusal of a reserved command run inside a Claude Code session. It names the separate terminal window before
    /// the command line to copy (<c>cd</c> to <paramref name="directory"/>, then <paramref name="engine"/> with the same arguments),
    /// and the session command when there is one.
    /// </summary>
    public static string Refusal(IReadOnlyList<string> args, string directory, string engine)
    {
        string command = string.Join(' ', args.Take(CommandWords));
        string commandLine = $"cd {ShellQuoting.Quote(directory)} && {engine} {string.Join(' ', args.Select(ShellQuoting.QuoteIfNeeded))}";
        string slash = InSession(command) is string inSession ? $" Or type {inSession} in Claude Code." : string.Empty;
        return $"lockflight: '{command}' records a human decision and does not run inside a Claude Code session. "
            + $"{Sentence(OpenATerminalAndRun(commandLine))}.{slash}";
    }

    /// <summary>
    /// Finds a reserved command in a shell command line: <c>lockflight …</c>, <c>dotnet lockflight …</c> or a path to the binary,
    /// also inside the command strings it runs (<c>bash -c "…"</c>, <c>eval</c>, a here-document fed to <c>sh</c>). A request for
    /// help passes (<see cref="FindInWords"/>) unless the command line defines a function or an alias, which could take the help
    /// option away before the engine sees it.
    /// </summary>
    public static string? FindIn(string command, ShellDialect dialect)
    {
        bool honorHelp = !CommandDefinition().IsMatch(command);
        List<string> words = [];
        foreach (ShellToken token in ShellTokenizer.Tokenize(command, dialect).Append(new ShellToken(ShellTokenKind.Separator, ";")))
        {
            if (token.IsWord)
            {
                words.Add(token.Text);
            }
            else if (FindInWords(words, honorHelp) is string found)
            {
                return found;
            }
            else
            {
                words.Clear();
            }
        }

        ShellWrites nested = ShellWriteTargets.Find(command, dialect);
        return honorHelp ? nested.HumanOnly : nested.HumanOnlyIgnoringHelp;
    }

    /// <summary>
    /// The reserved command (<c>approve spec</c>) the words of one simple command start, or null. With <paramref name="honorHelp"/>,
    /// a request for help is not one: the
    /// engine prints its usage for any command line with <c>--help</c> or <c>-h</c> before it parses or runs anything. That holds
    /// only when every word after the engine reaches it, so help counts only when the engine is the program itself (after variable
    /// assignments, or <c>dotnet</c>), never under a wrapper (<c>find -exec</c>, <c>xargs</c>, <c>env</c>) that may pass on fewer words.
    /// </summary>
    public static string? FindInWords(IReadOnlyList<string> words, bool honorHelp)
    {
        for (int index = 0; index < words.Count; index++)
        {
            if (!IsEngine(words[index]))
            {
                continue;
            }

            if (honorHelp && IsProgram(words, index) && words.Skip(index + 1).Any(IsHelp))
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

    /// <summary>The phrase as the start of a sentence.</summary>
    public static string Sentence(string phrase) => phrase.Length == 0 ? phrase : char.ToUpperInvariant(phrase[0]) + phrase[1..];

    /// <summary>Whether the engine word at <paramref name="index"/> is the program the command runs, so it receives every later word.</summary>
    private static bool IsProgram(IReadOnlyList<string> words, int index)
    {
        int start = index > 0 && words[index - 1] == DotnetHost ? index - 1 : index;
        return words.Take(start).All(word => Assignment().IsMatch(word));
    }

    private static bool IsHelp(string word) => word is "--help" or "-h";

    private static bool IsEngine(string token)
    {
        string name = Path.GetFileName(token.Replace('\\', '/'));
        return name.Equals("lockflight", StringComparison.OrdinalIgnoreCase) || name.Equals("lockflight.exe", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*=")]
    private static partial Regex Assignment();

    /// <summary>A function or alias definition anywhere in the text, nested command strings included (<c>name()</c>, <c>function</c>, <c>alias</c>, <c>Set-Alias</c>).</summary>
    [GeneratedRegex(@"[\w.-]\s*\(\s*\)|\b(?:function|filter|alias|Set-Alias|New-Alias|sal|nal)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CommandDefinition();
}
