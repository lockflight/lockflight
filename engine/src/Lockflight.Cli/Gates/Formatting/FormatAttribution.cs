using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Gates.Formatting;

/// <summary>
/// Decides which format diagnostics a change introduced. A diagnostic replaces a span of the file that starts at its line and
/// column and may run into the next lines (a whitespace span such as "trailing spaces, newline, indentation"); it belongs to the
/// change when the replacement changes the content of a changed line. A span that differs from its replacement only in carriage
/// returns is an end-of-line difference: it comes from how git checked the file out (<c>core.autocrlf</c>, <c>.gitattributes</c>)
/// against <c>.editorconfig</c>'s <c>end_of_line</c>, not from the change, and is never attributed. When the file or the span
/// cannot be read, the whole file is judged (conservatively attributed at the diagnostic's line). A <c>CHARSET</c> diagnostic (the
/// file's byte order mark disagrees with <c>.editorconfig</c>'s <c>charset</c>) is about the whole file's encoding, which an edit
/// inside the file does not touch: it is attributed only to a file the change adds or whose byte order mark the change adds or
/// removes (<c>encodingChanged</c>), like an end-of-line difference otherwise.
/// </summary>
internal static partial class FormatAttribution
{
    /// <summary>dotnet format's diagnostic for a file whose encoding (byte order mark) disagrees with <c>.editorconfig</c>'s <c>charset</c>.</summary>
    public const string CharsetDiagnostic = "CHARSET";

    public static FormatJudgement Judge(
        IReadOnlyList<FormatDiagnostic> diagnostics,
        IReadOnlyDictionary<string, IReadOnlySet<int>> changedLines,
        Func<string, string?> readText,
        IReadOnlySet<string> encodingChanged)
    {
        Dictionary<string, string?> texts = new(StringComparer.Ordinal);
        List<FormatFinding> attributed = [];
        SortedSet<string> wholeFiles = new(StringComparer.Ordinal);
        int endOfLineOnly = 0;
        int untouched = 0;
        int charsetOnly = 0;

        foreach (FormatDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.DiagnosticId == CharsetDiagnostic)
            {
                if (encodingChanged.Contains(diagnostic.File))
                {
                    attributed.Add(new FormatFinding(diagnostic.File, diagnostic.Line, diagnostic.DiagnosticId));
                }
                else
                {
                    charsetOnly++;
                }

                continue;
            }

            if (!texts.TryGetValue(diagnostic.File, out string? text))
            {
                text = readText(diagnostic.File);
                texts[diagnostic.File] = text;
            }

            if (text is null || !changedLines.TryGetValue(diagnostic.File, out IReadOnlySet<int>? changed) || SpanOf(text, diagnostic) is not (string original, string replacement))
            {
                attributed.Add(new FormatFinding(diagnostic.File, diagnostic.Line, diagnostic.DiagnosticId));
                wholeFiles.Add(diagnostic.File);
            }
            else if (LinesAffected(diagnostic.Line, original, replacement) is not { Count: > 0 } affected)
            {
                endOfLineOnly++;
            }
            else if (affected.Where(changed.Contains).ToArray() is { Length: > 0 } changedAffected)
            {
                attributed.AddRange(changedAffected.Select(line => new FormatFinding(diagnostic.File, line, diagnostic.DiagnosticId)));
            }
            else
            {
                untouched++;
            }
        }

        return new FormatJudgement([.. attributed.Distinct()], endOfLineOnly, untouched, [.. wholeFiles], charsetOnly);
    }

    /// <summary>The replaced text and its replacement, from the description; null when the description or position cannot be read.</summary>
    private static (string Original, string Replacement)? SpanOf(string text, FormatDiagnostic diagnostic)
    {
        int length;
        string replacement;
        if (Replace().Match(diagnostic.Description) is { Success: true } replace)
        {
            length = int.Parse(replace.Groups["count"].Value, CultureInfo.InvariantCulture);
            replacement = Unescape(replace.Groups["with"].Value);
        }
        else if (Insert().Match(diagnostic.Description) is { Success: true } insert)
        {
            length = 0;
            replacement = Unescape(insert.Groups["with"].Value);
        }
        else if (Delete().Match(diagnostic.Description) is { Success: true } delete)
        {
            length = int.Parse(delete.Groups["count"].Value, CultureInfo.InvariantCulture);
            replacement = string.Empty;
        }
        else
        {
            return null;
        }

        int? start = OffsetOf(text, diagnostic.Line, diagnostic.Column);
        return start is int offset && offset + length <= text.Length ? (text.Substring(offset, length), replacement) : null;
    }

    private static int? OffsetOf(string text, int line, int column)
    {
        if (line < 1 || column < 1)
        {
            return null;
        }

        int lineStart = 0;
        for (int current = 1; current < line; current++)
        {
            int newline = text.IndexOf('\n', lineStart);
            if (newline < 0)
            {
                return null;
            }

            lineStart = newline + 1;
        }

        int offset = lineStart + column - 1;
        return offset <= text.Length ? offset : null;
    }

    /// <summary>
    /// The lines whose content (ignoring carriage returns) the replacement changes. When the replacement keeps the number of line
    /// breaks, each line of the span is compared on its own, so trailing spaces on an untouched line do not implicate the changed
    /// line whose indentation the same span ends in; otherwise every line the span reaches counts. Empty for an end-of-line-only
    /// difference.
    /// </summary>
    private static List<int> LinesAffected(int startLine, string original, string replacement)
    {
        string[] before = original.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        string[] after = replacement.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        if (before.Length == after.Length)
        {
            return [.. Enumerable.Range(0, before.Length).Where(index => before[index] != after[index]).Select(index => startLine + index)];
        }

        int reached = original.EndsWith('\n') ? before.Length - 1 : before.Length;
        return [.. Enumerable.Range(startLine, Math.Max(reached, 1))];
    }

    /// <summary>dotnet format writes whitespace in descriptions as <c>\r</c>, <c>\n</c>, <c>\t</c> and <c>\s</c> (a space).</summary>
    private static string Unescape(string escaped)
    {
        StringBuilder builder = new(escaped.Length);
        for (int index = 0; index < escaped.Length; index++)
        {
            if (escaped[index] == '\\' && index + 1 < escaped.Length)
            {
                char code = escaped[++index];
                builder.Append(code switch
                {
                    'r' => '\r',
                    'n' => '\n',
                    't' => '\t',
                    's' => ' ',
                    _ => code,
                });
            }
            else
            {
                builder.Append(escaped[index]);
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"Replace (?<count>\d+) characters? with '(?<with>.*)'")]
    private static partial Regex Replace();

    [GeneratedRegex(@"Insert '(?<with>.*)'")]
    private static partial Regex Insert();

    [GeneratedRegex(@"Delete (?<count>\d+) characters?")]
    private static partial Regex Delete();
}
