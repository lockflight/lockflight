using System.Buffers;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// A <c>NAME=value</c> word. <see cref="Value"/> is the text it assigns when that is plain literal text a later <c>$NAME</c> can be
/// replaced by; null when it is not (an expansion left in it, a glob, a tilde, whitespace, an empty value, an append
/// <c>NAME+=…</c> or an array element <c>NAME[i]=…</c>). <see cref="Expanded"/> says whether the value was written with an
/// expansion (<c>B=$A</c>), which may stand for a variable assigned earlier in the same statement.
/// </summary>
internal sealed partial record ShellAssignment(string Name, string? Value, bool Expanded)
{
    private static readonly SearchValues<char> NonLiteralCharacters = SearchValues.Create(
        ['$', '`', '~', '*', '?', '[', ']', '{', '}', ',', '\\', '\'', '"', ';', '&', '|', '<', '>', '(', ')', '#', '!', '=']);

    /// <summary>Whether a word assigns a variable (<c>NAME=</c>, <c>NAME+=</c>, <c>NAME[i]=</c>) rather than naming a program.</summary>
    public static bool IsAssignment(string word) => Prefix().IsMatch(word);

    /// <summary>The assignment a word makes, or null when it is no assignment: the name and the <c>=</c> must not be quoted.</summary>
    public static ShellAssignment? Of(ShellToken source, string expandedText)
    {
        Match prefix = Prefix().Match(source.Text);
        if (!prefix.Success || source.QuotedFrom < prefix.Length)
        {
            return null;
        }

        string name = prefix.Groups["name"].Value;
        bool plain = !prefix.Groups["index"].Success && !prefix.Groups["append"].Success;
        string value = expandedText[prefix.Length..];
        bool expanded = !source.LiteralDollar && source.Text.IndexOf('$', prefix.Length) >= 0;
        return new ShellAssignment(name, plain && IsLiteral(value) ? value : null, expanded);
    }

    /// <summary>
    /// Text a variable can be replaced by without changing how the word splits, globs or expands. A drive-relative value
    /// (<c>C:dir</c>, what <c>C:\dir</c> becomes once the shell removes its backslashes) is not: Git Bash and Windows resolve it
    /// against a per-drive current directory the guard does not know.
    /// </summary>
    private static bool IsLiteral(string value) =>
        value.Length > 0
        && value.AsSpan().IndexOfAny(NonLiteralCharacters) < 0
        && !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
        && !DriveRelative().IsMatch(value);

    [GeneratedRegex(@"^[A-Za-z]:(?![\\/])")]
    private static partial Regex DriveRelative();

    [GeneratedRegex(@"^(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<index>\[[^\]]*\])?(?<append>\+)?=")]
    private static partial Regex Prefix();
}
