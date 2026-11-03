using System.Buffers;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Recognizes a POSIX shell command that does nothing but write literal text to files: every simple command is <c>cat</c> (no
/// operands, reading a here-document, a here-string or a pipe), <c>echo</c>, <c>printf</c> or <c>tee</c>, joined only by
/// <c>;</c>, newlines, <c>&amp;&amp;</c>, <c>||</c> and <c>|</c>, and at least one file is written. Anything that could run
/// something else or write somewhere unseen fails the test: substitutions, subshells, groups, background jobs, other programs,
/// variable assignments, a <c>$</c> or a backtick anywhere in a word or a here-text (quoted or not, since quoting is not kept),
/// a here-document line ending in a backslash (which can end the body early), comment-hiding or unusual whitespace, input read
/// from a file, and a target that is a pattern, starts with <c>~</c> or <c>=</c>, holds a <c>!</c> (zsh's <c>&gt;!</c>), lies
/// under <c>/dev</c> or <c>/proc</c> (except <c>/dev/null</c>), has a name ending in a dot or a space, or is a prompt queue file once <c>.</c> and <c>..</c> are
/// resolved. When in doubt it says no; a guardrail, not a boundary.
/// </summary>
internal static class ShellLiteralWrite
{
    private const string DevNull = "/dev/null";
    private const string EndOfOptions = "--";

    private static readonly HashSet<string> Writers = ["cat", "echo", "printf", "tee"];
    private static readonly HashSet<string> Joiners = [";", "\n", "&&", "||", "|"];
    private static readonly HashSet<string> TeeOptions = ["-a", "--append", "-i", "--ignore-interrupts"];
    private static readonly HashSet<string> DeviceDirectories = ["dev", "proc"];

    /// <summary>Characters that start an expansion or a command substitution.</summary>
    private static readonly SearchValues<char> Expansions = SearchValues.Create("$`");

    /// <summary>Characters that make a target a pattern or something other than the path as written.</summary>
    private static readonly SearchValues<char> TargetSpecials = SearchValues.Create("*?[]{}~!$`");

    public static bool OnlyWritesLiteralText(string command)
    {
        if (command.Any(IsUnusualSpace))
        {
            return false;
        }

        List<string> targets = [];
        List<string> words = [];
        bool hasRedirections = false;
        ShellTokenKind? redirect = null;
        foreach (ShellToken token in ShellTokenizer.Tokenize(command, ShellDialect.Posix))
        {
            ShellTokenKind? pending = redirect;
            redirect = null;
            switch (token.Kind)
            {
                case ShellTokenKind.Word when token.Text.AsSpan().ContainsAny(Expansions) || token.Text.Contains('\n', StringComparison.Ordinal):
                    return false;
                case ShellTokenKind.Word when pending == ShellTokenKind.WriteRedirect:
                    targets.Add(token.Text);
                    break;
                case ShellTokenKind.Word when pending is null:
                    words.Add(token.Text);
                    break;
                case ShellTokenKind.HereText when (pending is null or ShellTokenKind.InputRedirect) && IsLiteralHereText(token.Text):
                    hasRedirections = true;
                    break;
                case ShellTokenKind.WriteRedirect or ShellTokenKind.InputRedirect when pending is null:
                    redirect = token.Kind;
                    hasRedirections = true;
                    break;
                case ShellTokenKind.Separator when pending is null && Joiners.Contains(token.Text):
                    if (!IsLiteralWriter(words, hasRedirections, targets))
                    {
                        return false;
                    }

                    words = [];
                    hasRedirections = false;
                    break;
                default:
                    // An input file, a subshell, a group, a substitution, a background job or a dangling redirection.
                    return false;
            }
        }

        return redirect is null && IsLiteralWriter(words, hasRedirections, targets) && targets.Count > 0 && targets.All(IsPlainFile);
    }

    /// <summary>Whether one simple command is an allowed writer; its <c>tee</c> operands are added to the targets.</summary>
    private static bool IsLiteralWriter(List<string> words, bool hasRedirections, List<string> targets)
    {
        if (words.Count == 0)
        {
            return !hasRedirections;
        }

        string program = words[0];
        if (!Writers.Contains(program))
        {
            return false;
        }

        List<string> arguments = [.. words.Skip(1)];
        return program switch
        {
            "cat" => arguments.Count == 0,
            "printf" => arguments.Count > 0 && !arguments[0].StartsWith("-v", StringComparison.Ordinal),
            "tee" => AddTeeTargets(arguments, targets),
            _ => true,
        };
    }

    private static bool AddTeeTargets(List<string> arguments, List<string> targets)
    {
        int index = 0;
        while (index < arguments.Count && arguments[index].StartsWith('-') && arguments[index] != EndOfOptions)
        {
            if (!TeeOptions.Contains(arguments[index]))
            {
                return false;
            }

            index++;
        }

        index += index < arguments.Count && arguments[index] == EndOfOptions ? 1 : 0;
        targets.AddRange(arguments.Skip(index));
        return true;
    }

    /// <summary>
    /// Here-text with no <c>$</c> or backtick expands to itself whether or not its delimiter was quoted; a line ending in a backslash
    /// is joined to the next one by the shell, which can end the body where the tokenizer does not.
    /// </summary>
    private static bool IsLiteralHereText(string text) =>
        !text.AsSpan().ContainsAny(Expansions) && !text.Split('\n').Any(line => line.EndsWith('\\'));

    private static bool IsPlainFile(string target)
    {
        if (target.Length == 0 || target == "-" || target[0] == '=' || target.AsSpan().ContainsAny(TargetSpecials))
        {
            return false;
        }

        string resolved = Resolve(target);
        if (resolved == DevNull)
        {
            return true;
        }

        string[] segments = resolved.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Windows drops a trailing dot or space from a name, so "loop.md." is "loop.md" there.
        if (segments.Any(segment => segment != ".." && (segment.EndsWith('.') || segment.EndsWith(' '))))
        {
            return false;
        }

        bool outsideTheFolder = resolved.StartsWith('/') || (segments.Length > 0 && segments[0] == "..");
        bool device = outsideTheFolder && segments.Any(segment => DeviceDirectories.Contains(segment.ToLowerInvariant()));
        return !device && !PromptQueueFiles.Contains(target) && !PromptQueueFiles.Contains(resolved);
    }

    /// <summary>The path with <c>.</c> segments dropped and each <c>..</c> applied to the segment before it, where there is one.</summary>
    private static string Resolve(string path)
    {
        string normalized = path.Replace('\\', '/');
        bool rooted = normalized.StartsWith('/');
        List<string> segments = [];
        foreach (string segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == ".." && segments.Count > 0 && segments[^1] != "..")
            {
                segments.RemoveAt(segments.Count - 1);
            }
            else if (segment != ".." || !rooted)
            {
                segments.Add(segment);
            }
        }

        return (rooted ? "/" : string.Empty) + string.Join('/', segments);
    }

    /// <summary>Whitespace or a control character the shell does not split on (or treats differently from the tokenizer).</summary>
    private static bool IsUnusualSpace(char character) =>
        character is not (' ' or '\t' or '\n') && (char.IsWhiteSpace(character) || char.IsControl(character));
}
