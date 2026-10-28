using System.Text.RegularExpressions;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Docs;

/// <summary>
/// Documented command lines of the engine (<c>lockflight</c>, <c>.lockflight/bin/lockflight</c>, <c>dotnet lockflight</c>, …),
/// compared with this engine's <see cref="CommandCatalog"/>: an unknown command, an option the command does not take, or an
/// argument after a command that takes none. Usage notation is read as usage: <c>&lt;placeholders&gt;</c> and <c>…</c> are not
/// checked, <c>a|b</c> alternatives and <c>[--option]</c> brackets are expanded, a <c>#</c> comment ends the line, and a mention
/// with fewer words than a command needs (<c>lockflight approve</c>) is not drift.
/// </summary>
internal static partial class CommandLineCheck
{
    private const string Engine = "lockflight";

    private static readonly string[] Shells = ["bash", "sh", "shell", "console", "zsh", "powershell", "pwsh", "ps1", "shell-session"];

    public static bool IsShellFence(string language) => Shells.Contains(language, StringComparer.Ordinal);

    /// <summary>Whether a code span is an engine command line, so it is checked here rather than as a path.</summary>
    public static bool IsInvocation(string text) => Segments(text).Any(segment => Invocation(Tokens(segment)) is not null);

    public static IEnumerable<DriftFinding> Check(string document, int lineNumber, string text)
    {
        foreach (string segment in Segments(text))
        {
            List<string> tokens = Tokens(segment);
            if (Invocation(tokens) is not int start)
            {
                continue;
            }

            string reference = string.Join(' ', tokens[(start - 1)..]);
            foreach (string reason in Problems(tokens[start..]))
            {
                yield return new DriftFinding(document, lineNumber, reference, reason, DriftKind.Command);
            }
        }
    }

    private static IEnumerable<string> Problems(List<string> arguments)
    {
        List<string> words = [.. arguments.TakeWhile(IsWord)];
        List<string> named = [.. words.TakeWhile(word => !IsPlaceholder(word))];
        if (named.Count == 0)
        {
            yield break;
        }

        foreach (List<string> alternative in Alternatives(named))
        {
            foreach (string problem in Problems(alternative, arguments.Skip(words.Count), placeholderFollows: named.Count < words.Count))
            {
                yield return problem;
            }
        }
    }

    private static IEnumerable<string> Problems(List<string> words, IEnumerable<string> rest, bool placeholderFollows)
    {
        if (CommandCatalog.Match(words) is not CatalogedCommand command)
        {
            if (!CommandCatalog.IsPrefix(words))
            {
                yield return $"unknown command '{string.Join(' ', ShortestUnknown(words))}'";
            }

            yield break;
        }

        if (words.Count > command.Words.Count && !command.TakesArguments)
        {
            yield return $"'{command.Name}' takes no argument '{words[command.Words.Count]}'";
            yield break;
        }

        if (placeholderFollows && !command.TakesArguments && command.Words.Count == words.Count)
        {
            // `lockflight baseline <build|tests|deps>`: the placeholder stands for the rest of the command's words.
            yield break;
        }

        foreach (string option in rest.SelectMany(Options).Distinct(StringComparer.Ordinal))
        {
            if (!command.Options.Contains(option) && !CommandCatalog.GlobalOptions.Contains(option)
                && !(option.Contains('=') && command.Options.Contains(option[..option.IndexOf('=')])))
            {
                yield return $"'{command.Name}' has no option '{option}'";
            }
        }
    }

    /// <summary>The first words that are neither a command nor the start of one: what to call unknown.</summary>
    private static List<string> ShortestUnknown(List<string> words)
    {
        for (int count = 1; count <= words.Count; count++)
        {
            List<string> prefix = words[..count];
            if (CommandCatalog.Match(prefix) is null && !CommandCatalog.IsPrefix(prefix))
            {
                return prefix;
            }
        }

        return words;
    }

    /// <summary>The options a token names: brackets and parentheses dropped, <c>a|b</c> split, <c>--x[=y]</c> read as <c>--x</c>.</summary>
    private static IEnumerable<string> Options(string token) =>
        token.Trim('[', ']', '(', ')', ',', '"', '\'').Split('|')
            .Select(piece => piece.Trim('[', ']', '(', ')'))
            .Select(piece => piece.Contains('[') ? piece[..piece.IndexOf('[')] : piece)
            .Where(piece => OptionPattern().IsMatch(piece));

    private static IEnumerable<List<string>> Alternatives(List<string> words)
    {
        IEnumerable<List<string>> combinations = [[]];
        foreach (string word in words)
        {
            string[] choices = word.Split('|', StringSplitOptions.RemoveEmptyEntries);
            combinations = [.. combinations.SelectMany(prefix => choices.Select(choice => (List<string>)[.. prefix, choice]))];
        }

        return combinations;
    }

    private static bool IsWord(string token) => !token.StartsWith('-') && !token.StartsWith('[') && !token.StartsWith('(');

    private static bool IsPlaceholder(string token) =>
        token.Contains('<') || token.Contains('…') || token.Contains("...", StringComparison.Ordinal) || token.Contains('$') || token.Contains('"')
        || token.Contains('\'') || token.Contains('{') || !WordPattern().IsMatch(token);

    /// <summary>The index of the first argument after the engine's name; null when the tokens do not start with an engine invocation.</summary>
    private static int? Invocation(List<string> tokens)
    {
        if (tokens is ["dotnet", Engine, ..])
        {
            return 2;
        }

        if (tokens is ["dotnet", "tool", "run", Engine, ..])
        {
            return 4;
        }

        return tokens is [string first, ..] && (first == Engine || first == Engine + ".exe" || first.EndsWith("/" + Engine, StringComparison.Ordinal) || first.EndsWith("/" + Engine + ".exe", StringComparison.Ordinal))
            && !first.Contains("://", StringComparison.Ordinal)
            ? 1
            : null;
    }

    /// <summary>The simple commands of a line: split at <c>&amp;&amp;</c>, <c>||</c>, <c>;</c> and a spaced pipe, after a prompt and before a comment.</summary>
    private static IEnumerable<string> Segments(string text)
    {
        string line = PromptPattern().Replace(text, string.Empty);
        int comment = CommentPattern().Match(line) is { Success: true } match ? match.Index : -1;
        if (comment >= 0)
        {
            line = line[..comment];
        }

        return SeparatorPattern().Split(line).Select(segment => segment.Trim()).Where(segment => segment.Length > 0);
    }

    /// <summary>Whitespace-separated words, a quoted string kept as one.</summary>
    private static List<string> Tokens(string segment) =>
        [.. TokenPattern().Matches(segment).Select(match => match.Value)];

    [GeneratedRegex(@"^\s*(?:\$|>|PS>)\s+")]
    private static partial Regex PromptPattern();

    [GeneratedRegex(@"(?:^|\s)#(?:\s|$)")]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"&&|\|\||;|\s\|\s")]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"""[^""]*""|'[^']*'|\S+")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"^--?[A-Za-z][A-Za-z0-9-]*(?:=[A-Za-z0-9-]+)?$")]
    private static partial Regex OptionPattern();

    [GeneratedRegex(@"^[a-z][a-z0-9|-]*$")]
    private static partial Regex WordPattern();
}
