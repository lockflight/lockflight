using System.Text;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Splits a shell command into words and operators, honouring quotes, the dialect's escape character, line continuations,
/// comments and here-documents. A newline separates commands like <c>;</c>. A command substitution is emitted as a command of
/// its own; the word it sits in continues after it with <see cref="SubstitutionPlaceholder"/> in its place.
/// </summary>
internal sealed class ShellTokenizer(string command, ShellDialect dialect)
{
    /// <summary>Stands for the unknown output of a command substitution inside a word; the <c>$</c> marks it as an expansion.</summary>
    public const string SubstitutionPlaceholder = "$__";

    private const char Newline = '\n';
    private const char Nothing = '\0';

    private static readonly string[] Separators = ["&&", "||", "|&", ";", "|", "&"];

    private readonly char escape = dialect == ShellDialect.PowerShell ? '`' : '\\';
    private readonly List<ShellToken> tokens = [];
    private readonly StringBuilder current = new();
    private readonly Stack<OpenFrame> frames = new();
    private readonly List<(string Delimiter, bool StripsTabs)> pendingHereDocuments = [];
    private int index;
    private char? quote;
    private int? quotedFrom;
    private bool literalDollar;
    private bool? awaitedDelimiterStripsTabs;

    public static List<ShellToken> Tokenize(string command, ShellDialect dialect) => new ShellTokenizer(command, dialect).Run();

    private bool IsPosix => dialect == ShellDialect.Posix;

    private List<ShellToken> Run()
    {
        while (index < command.Length)
        {
            if (quote is char openQuote)
            {
                ReadQuoted(openQuote);
            }
            else
            {
                ReadUnquoted();
            }
        }

        Flush();
        while (frames.Count > 0)
        {
            CloseFrame();
        }

        return tokens;
    }

    private void ReadQuoted(char openQuote)
    {
        char character = command[index];
        if (character == openQuote)
        {
            if (!IsPosix && openQuote == '\'' && Peek(1) == '\'')
            {
                current.Append('\'');
                index += 2;
                return;
            }

            quote = null;
            index++;
            return;
        }

        if (openQuote == '"' && character == escape && index + 1 < command.Length)
        {
            AppendEscape(ShellEscapes.InDoubleQuotes(command, index, dialect));
            return;
        }

        if (openQuote == '"' && TryOpenSubstitution())
        {
            return;
        }

        literalDollar |= openQuote == '\'' && character == '$';
        current.Append(character);
        index++;
    }

    private void ReadUnquoted()
    {
        char character = command[index];
        if (character is '\'' or '"')
        {
            quotedFrom ??= current.Length;
            quote = character;
            index++;
        }
        else if (character == escape)
        {
            AppendEscape(ShellEscapes.Unquoted(command, index));
        }
        else if (character == Newline || (character == '\r' && Peek(1) == Newline))
        {
            Flush();
            tokens.Add(new ShellToken(ShellTokenKind.Separator, "\n"));
            index += character == '\r' ? 2 : 1;
            SkipHereDocumentBodies();
        }
        else if (char.IsWhiteSpace(character))
        {
            Flush();
            index++;
        }
        else if (character == '#' && current.Length == 0)
        {
            int lineEnd = command.IndexOf(Newline, index);
            index = lineEnd < 0 ? command.Length : lineEnd;
        }
        else if (character == ')')
        {
            CloseFrame();
            index++;
        }
        else if (IsPosix && character == '$' && Peek(1) == '\'')
        {
            AppendEscape(ShellEscapes.AnsiC(command, index));
        }
        else if (!TryOpenSubstitution() && !TryReadPowerShellForm() && !TryOpenGroup() && !TryReadRedirection() && !TryReadSeparator())
        {
            current.Append(character);
            index++;
        }
    }

    /// <summary>Escaped and quoted text is literal: a <c>$</c> in it starts no expansion.</summary>
    private void AppendEscape((string Text, int Length) escaped)
    {
        quotedFrom ??= current.Length;
        literalDollar |= escaped.Text.Contains('$', StringComparison.Ordinal);
        current.Append(escaped.Text);
        index += escaped.Length;
    }

    /// <summary><c>$(</c>, POSIX backticks and process substitution <c>&lt;(</c>/<c>&gt;(</c> run a command of their own.</summary>
    private bool TryOpenSubstitution()
    {
        char character = command[index];
        if (IsPosix && character == '`')
        {
            if (frames.TryPeek(out OpenFrame? top) && top.Closer == '`')
            {
                CloseFrame();
                index++;
                return true;
            }

            OpenSubstitution('`', width: 1);
            return true;
        }

        bool opensSubstitution = (character == '$' && Peek(1) == '(')
            || (!IsPosix && character == '@' && Peek(1) == '(')
            || (IsPosix && quote is null && current.Length == 0 && character is '<' or '>' && Peek(1) == '(');
        if (opensSubstitution)
        {
            OpenSubstitution(')', width: 2);
        }

        return opensSubstitution;
    }

    private void OpenSubstitution(char closer, int width)
    {
        frames.Push(new OpenFrame(closer, IsSubstitution: true, current.ToString(), quote) { QuotedFrom = quotedFrom, LiteralDollar = literalDollar });
        current.Clear();
        ResetWordMarks();
        quote = null;
        tokens.Add(new ShellToken(ShellTokenKind.SubstitutionOpen, command.Substring(index, width)));
        index += width;
    }

    private bool TryOpenGroup()
    {
        if (command[index] != '(')
        {
            return false;
        }

        Flush();
        frames.Push(new OpenFrame(')', IsSubstitution: false, string.Empty, null));
        tokens.Add(new ShellToken(IsPosix ? ShellTokenKind.SubshellOpen : ShellTokenKind.GroupOpen, "("));
        index++;
        return true;
    }

    private void CloseFrame()
    {
        Flush();
        tokens.Add(new ShellToken(ShellTokenKind.Close, ")"));
        if (frames.TryPop(out OpenFrame? frame) && frame.IsSubstitution)
        {
            current.Append(frame.SavedWord).Append(SubstitutionPlaceholder);
            quote = frame.SavedQuote;
            quotedFrom = frame.QuotedFrom;
            literalDollar = frame.LiteralDollar;
        }
    }

    /// <summary>PowerShell braces (script blocks) separate commands; <c>${name}</c> is one word; <c>@'…'@</c> is a here-string.</summary>
    private bool TryReadPowerShellForm()
    {
        if (IsPosix)
        {
            return false;
        }

        char character = command[index];
        if (character == '$' && Peek(1) == '{')
        {
            int end = command.IndexOf('}', index);
            end = end < 0 ? command.Length - 1 : end;
            current.Append(command, index, end - index + 1);
            index = end + 1;
            return true;
        }

        if (ShellHereText.StartsHereString(command, index))
        {
            (string body, int end) = ShellHereText.ReadHereString(command, index);
            current.Append(body);
            index = end;
            return true;
        }

        if (character == '<' && Peek(1) == '#')
        {
            int end = command.IndexOf("#>", index + 2, StringComparison.Ordinal);
            index = end < 0 ? command.Length : end + 2;
            return true;
        }

        if (character is '{' or '}')
        {
            Flush();
            tokens.Add(new ShellToken(ShellTokenKind.Separator, character.ToString()));
            index++;
            return true;
        }

        return false;
    }

    /// <summary>Output redirections in every fd form (<c>&gt;</c>, <c>&gt;&gt;</c>, <c>&gt;|</c>, <c>N&gt;</c>, <c>&amp;&gt;</c>, <c>&amp;&gt;&gt;</c>, <c>&gt;&amp;file</c>); fd duplications write nothing.</summary>
    private bool TryReadRedirection()
    {
        if (ShellRedirection.Read(command, index) is not ShellRedirection redirection)
        {
            return false;
        }

        // "2>file": the digits are the descriptor, not a word (PowerShell also has "*>").
        string word = current.ToString();
        bool descriptorPrefix = command[index] != '&' && word.Length > 0 && (word.All(char.IsAsciiDigit) || (!IsPosix && word == "*"));
        if (descriptorPrefix)
        {
            current.Clear();
            ResetWordMarks();
        }
        else
        {
            Flush();
        }

        index += redirection.Length;
        if (redirection.Kind is ShellTokenKind kind)
        {
            tokens.Add(new ShellToken(kind, kind == ShellTokenKind.WriteRedirect ? ">" : "<"));
        }

        awaitedDelimiterStripsTabs = redirection.HereDocumentStripsTabs ?? awaitedDelimiterStripsTabs;
        return true;
    }

    private bool TryReadSeparator()
    {
        string? separator = Separators.FirstOrDefault(candidate => string.CompareOrdinal(command, index, candidate, 0, candidate.Length) == 0);
        if (separator is null)
        {
            return false;
        }

        Flush();
        tokens.Add(new ShellToken(ShellTokenKind.Separator, separator));
        index += separator.Length;
        return true;
    }

    private void SkipHereDocumentBodies()
    {
        index = ShellHereText.SkipHereDocumentBodies(command, index, pendingHereDocuments);
        pendingHereDocuments.Clear();
    }

    private void Flush()
    {
        if (current.Length == 0)
        {
            ResetWordMarks();
            return;
        }

        string word = current.ToString();
        current.Clear();
        if (awaitedDelimiterStripsTabs is bool stripsTabs)
        {
            pendingHereDocuments.Add((word, stripsTabs));
            awaitedDelimiterStripsTabs = null;
        }

        tokens.Add(new ShellToken(ShellTokenKind.Word, word) { QuotedFrom = quotedFrom, LiteralDollar = literalDollar });
        ResetWordMarks();
    }

    private void ResetWordMarks()
    {
        quotedFrom = null;
        literalDollar = false;
    }

    private char Peek(int offset) => index + offset < command.Length ? command[index + offset] : Nothing;

    private sealed record OpenFrame(char Closer, bool IsSubstitution, string SavedWord, char? SavedQuote)
    {
        public int? QuotedFrom { get; init; }

        public bool LiteralDollar { get; init; }
    }
}
