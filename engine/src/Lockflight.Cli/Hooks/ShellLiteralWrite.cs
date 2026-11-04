using System.Buffers;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Recognizes a POSIX shell command that does nothing but write literal text to files: every simple command is <c>cat</c> (no
/// operands, reading a here-document, a here-string or a pipe), <c>echo</c>, <c>printf</c> or <c>tee</c>, joined only by
/// <c>;</c>, newlines, <c>&amp;&amp;</c>, <c>||</c> and <c>|</c>, and at least one file is written. Anything that could run
/// something else or write somewhere unseen fails the test: substitutions, subshells, groups, background jobs, other programs,
/// variable assignments, a <c>$</c> or a backtick anywhere in a word or a here-text (quoted or not, since quoting is not kept),
/// an empty quoted word, a here-string over several lines, unusual whitespace, input read from a file,
/// and a target that is a pattern, starts with <c>~</c> or <c>=</c>, holds a <c>!</c> (zsh's <c>&gt;!</c>), lies under
/// <c>/dev</c> or <c>/proc</c> (except <c>/dev/null</c>), has a name ending in a dot or a space, or is a prompt queue file once
/// resolved against the working directory (a relative target with no working directory fails). A here-document is checked
/// line by line, without the tokenizer, and only one plain form passes (<see cref="IsPlainHereDocument"/>). When in doubt it
/// says no; a guardrail, not a boundary.
/// </summary>
internal static partial class ShellLiteralWrite
{
    private const string DevNull = "/dev/null";
    private const string EndOfOptions = "--";

    private static readonly HashSet<string> Writers = ["cat", "echo", "printf", "tee"];
    private static readonly HashSet<string> Joiners = [";", "\n", "&&", "||", "|"];
    private static readonly HashSet<string> TeeOptions = ["-a", "--append", "-i", "--ignore-interrupts"];
    private static readonly string[] DeviceDirectories = ["/dev/", "/proc/"];

    /// <summary>Characters that start an expansion or a command substitution.</summary>
    private static readonly SearchValues<char> Expansions = SearchValues.Create("$`");

    /// <summary>Characters that make a target a pattern or something other than the path as written.</summary>
    private static readonly SearchValues<char> TargetSpecials = SearchValues.Create("*?[]{}~!$`");

    /// <summary>What the line that opens a here-document may not hold: another command, a group, a continuation.</summary>
    private static readonly SearchValues<char> OpenerSpecials = SearchValues.Create("\\;|&()$`");

    /// <param name="workingDirectory">The directory the command runs in; null when unknown, and then a relative target fails.</param>
    public static bool OnlyWritesLiteralText(string command, string? workingDirectory)
    {
        bool hereDocument = HereDocumentOperator().IsMatch(command);
        if (command.Any(IsUnusualSpace) || (hereDocument ? !IsPlainHereDocument(command) : HasEmptyQuotes(command)))
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
                case ShellTokenKind.HereText when pending is null && IsLiteralHereText(token.Text) && !token.Text.Contains('\n', StringComparison.Ordinal):
                    hasRedirections = true;
                    break;
                case ShellTokenKind.HereText when pending == ShellTokenKind.InputRedirect && IsLiteralHereText(token.Text):
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

        return redirect is null && IsLiteralWriter(words, hasRedirections, targets) && targets.Count > 0 && targets.All(target => IsPlainFile(target, workingDirectory));
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
    /// would be joined to the next one, or kept, depending on the quoting, so such text is never taken as literal.
    /// </summary>
    private static bool IsLiteralHereText(string text) =>
        !text.AsSpan().ContainsAny(Expansions) && !text.Split('\n').Any(line => line.EndsWith('\\'));

    /// <summary>
    /// Whether the command is one simple command whose first line opens one here-document with a plain delimiter (a name, bare
    /// or wholly in single or double quotes; <c>&lt;&lt;-</c> strips tabs), and whose last line, and no line before it, ends the
    /// body. The opening line may not continue onto the next (a backslash, an open quote), so the body starts where the
    /// tokenizer reads it; the body may not expand (<c>$</c>, a backtick) or join lines (a trailing backslash).
    /// </summary>
    private static bool IsPlainHereDocument(string command)
    {
        string[] lines = (command.EndsWith('\n') ? command[..^1] : command).Split('\n');
        string opener = lines[0];
        Match opening = HereDocumentOpening().Match(opener);
        if (lines.Length < 2 || !opening.Success || opener.Count(character => character == '<') != 2
            || opener.AsSpan().ContainsAny(OpenerSpecials) || HasEmptyQuotes(opener) || !QuotesClose(opener))
        {
            return false;
        }

        string delimiter = opening.Groups["delimiter"].Value;
        bool stripsTabs = opening.Groups["strip"].Value.Length > 0;
        string[] body = [.. lines.Skip(1).Select(line => stripsTabs ? line.TrimStart('\t') : line)];
        return body[^1] == delimiter
            && !body[..^1].Any(line => line == delimiter || line.AsSpan().ContainsAny(Expansions) || line.EndsWith('\\'));
    }

    private static bool QuotesClose(string line)
    {
        char? quote = null;
        foreach (char character in line)
        {
            if (quote is null && character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == quote)
            {
                quote = null;
            }
        }

        return quote is null;
    }

    /// <summary>
    /// An empty quoted word has no place in a note and changes how the lines around it are read (<c>&lt;&lt;''</c> ends its body at
    /// the first empty line), so a command with one is never exempt.
    /// </summary>
    private static bool HasEmptyQuotes(string text) =>
        text.Contains("''", StringComparison.Ordinal) || text.Contains("\"\"", StringComparison.Ordinal);

    private static bool IsPlainFile(string target, string? workingDirectory)
    {
        if (target.Length == 0 || target == "-" || target[0] == '=' || target.AsSpan().ContainsAny(TargetSpecials))
        {
            return false;
        }

        // A colon only as a drive root (C:/ or C:\): Windows reads C:name against the drive's own current folder, and a later
        // colon names an alternate data stream; neither is the path as written.
        int colon = target.IndexOf(':', StringComparison.Ordinal);
        bool driveRoot = colon == 1 && char.IsAsciiLetter(target[0]) && target.Length > 2 && target[2] is '/' or '\\';
        if (colon >= 0 && (!driveRoot || target.IndexOf(':', colon + 1) >= 0))
        {
            return false;
        }

        string path = target.Replace('\\', '/');
        if (!path.StartsWith('/') && !DriveRoot().IsMatch(path))
        {
            if (workingDirectory is null)
            {
                return false;
            }

            path = $"{workingDirectory.Replace('\\', '/')}/{path}";
        }

        string resolved = Resolve(path);

        // A drive root is a root: /dev and /proc are checked below it too, as Git Bash on Windows has no other.
        string fromRoot = DriveRoot().IsMatch(resolved) ? resolved[2..] : resolved;
        if (fromRoot == DevNull)
        {
            return true;
        }

        // Windows drops a trailing dot or space from a name, so "loop.md." is "loop.md" there.
        string[] segments = resolved.Split('/', StringSplitOptions.RemoveEmptyEntries);
        bool windowsAlias = segments.Any(segment => segment != ".." && (segment.EndsWith('.') || segment.EndsWith(' ')));
        bool device = DeviceDirectories.Any(directory => $"{fromRoot}/".StartsWith(directory, StringComparison.OrdinalIgnoreCase));
        return !windowsAlias && !device && !PromptQueueFiles.Contains(target) && !PromptQueueFiles.Contains(resolved);
    }

    /// <summary>The path with <c>.</c> segments dropped and each <c>..</c> applied to the segment before it, where there is one.</summary>
    private static string Resolve(string path)
    {
        string normalized = path.Replace('\\', '/');
        string drive = DriveRoot().IsMatch(normalized) ? normalized[..2] : string.Empty;
        string rest = normalized[drive.Length..];
        bool rooted = rest.StartsWith('/');
        List<string> segments = [];
        foreach (string segment in rest.Split('/', StringSplitOptions.RemoveEmptyEntries))
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

        return drive + (rooted ? "/" : string.Empty) + string.Join('/', segments);
    }

    /// <summary>A here-document operator (<c>&lt;&lt;</c> or <c>&lt;&lt;-</c>), not a here-string's <c>&lt;&lt;&lt;</c>.</summary>
    [GeneratedRegex("(?<!<)<<(?!<)")]
    private static partial Regex HereDocumentOperator();

    [GeneratedRegex("""<<(?<strip>-?)[ \t]*(?:'(?<delimiter>[A-Za-z_][A-Za-z0-9_]*)'|"(?<delimiter>[A-Za-z_][A-Za-z0-9_]*)"|(?<delimiter>[A-Za-z_][A-Za-z0-9_]*))(?=[ \t>]|$)""")]
    private static partial Regex HereDocumentOpening();

    [GeneratedRegex("^[A-Za-z]:/")]
    private static partial Regex DriveRoot();

    /// <summary>Whitespace or a control character the shell does not split on (or treats differently from the tokenizer).</summary>
    private static bool IsUnusualSpace(char character) =>
        character is not (' ' or '\t' or '\n') && (char.IsWhiteSpace(character) || char.IsControl(character));
}
