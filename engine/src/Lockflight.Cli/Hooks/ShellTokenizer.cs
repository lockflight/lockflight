using System.Text;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Splits a shell command into words and operators, honouring quotes, the dialect's escape character, line continuations,
/// comments and here-documents. A newline separates commands like <c>;</c>. A here-document body or a here-string is a
/// <see cref="ShellTokenKind.HereText"/> token in place of its delimiter or word. A command substitution is emitted as a command of
/// its own; the word it sits in continues after it with <see cref="SubstitutionPlaceholder"/> in its place. An empty quoted word
/// (<c>''</c>, <c>""</c>, <c>$''</c>) is a word, as it is to the shell.
/// </summary>
/// <remarks>
/// Here-document bodies are read the way bash and zsh read them: after the newline that ends the logical line holding the
/// operator, so a line continuation or a quote spanning lines comes first, and only at a newline of the same command
/// substitution (a body opened inside <c>$(…)</c>, backticks or <c>&lt;(…)</c> is read inside it; one still unread when the
/// substitution closes is empty). An empty delimiter (<c>&lt;&lt;''</c>) ends the body at the first empty line. With an unquoted
/// delimiter a backslash at the end of a body line joins it to the next one before the line is compared with the delimiter, and
/// the command substitutions in the body run: they follow the body's newline as commands of their own. bash reads
/// <c>&lt;&lt;$"EOF"</c> as the delimiter <c>EOF</c> and zsh as <c>$EOF</c>: the body ends at whichever line comes first.
/// </remarks>
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
    private readonly List<PendingHereDocument> pendingHereDocuments = [];
    private int index;
    private char? quote;
    private int? quotedFrom;
    private bool literalDollar;
    private bool? awaitedDelimiterStripsTabs;
    private bool hereStringAwaited;

    /// <summary>Where in the current word bash dropped the <c>$</c> of a <c>$"…"</c> that zsh keeps.</summary>
    private List<int> droppedDollars = [];

    /// <summary>Reading an unquoted here-document body: a double quote is plain text there, outside a substitution.</summary>
    private bool inHereDocumentBody;

    public static List<ShellToken> Tokenize(string command, ShellDialect dialect) => new ShellTokenizer(command, dialect).Run();

    private bool IsPosix => dialect == ShellDialect.Posix;

    private int SubstitutionDepth => frames.Count(frame => frame.IsSubstitution);

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
        if (character == openQuote && !(inHereDocumentBody && frames.Count == 0))
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
            ReadUnquotedEscape();
        }
        else if (character == Newline || (character == '\r' && Peek(1) == Newline))
        {
            Flush();

            // An operator with nothing after it on the line is a syntax error to the shell: it reads no word from the next line.
            awaitedDelimiterStripsTabs = null;
            hereStringAwaited = false;
            tokens.Add(new ShellToken(ShellTokenKind.Separator, "\n"));
            index += character == '\r' ? 2 : 1;
            ReadHereDocumentBodies();
        }
        else if (char.IsWhiteSpace(character))
        {
            Flush();
            index++;
        }
        else if (character == '#' && current.Length == 0 && quotedFrom is null)
        {
            // Only a '#' that starts a word starts a comment: after quotes ('' or "") it is part of the word.
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
        else if (IsPosix && character == '$' && Peek(1) == '"')
        {
            droppedDollars.Add(current.Length);
            index++;
        }
        else if (!TryOpenSubstitution() && !TryReadPowerShellForm() && !TryOpenGroup() && !TryReadRedirection() && !TryReadSeparator())
        {
            current.Append(character);
            index++;
        }
    }

    /// <summary>A line continuation joins the lines and quotes nothing; any other escape quotes the character after it.</summary>
    private void ReadUnquotedEscape()
    {
        (string Text, int Length) escaped = ShellEscapes.Unquoted(command, index);
        if (escaped.Text.Length == 0)
        {
            index += escaped.Length;
        }
        else
        {
            AppendEscape(escaped);
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
        frames.Push(new OpenFrame(closer, IsSubstitution: true, current.ToString(), quote)
        {
            QuotedFrom = quotedFrom,
            LiteralDollar = literalDollar,
            AwaitedDelimiterStripsTabs = awaitedDelimiterStripsTabs,
            HereStringAwaited = hereStringAwaited,
        });
        current.Clear();
        ResetWordMarks();
        quote = null;

        // The substitution is the word a here-string or a here-document operator waits for, not the words inside it.
        awaitedDelimiterStripsTabs = null;
        hereStringAwaited = false;
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
            // A here-document whose body did not start inside the substitution has none: what follows is commands again.
            int depth = SubstitutionDepth;
            pendingHereDocuments.RemoveAll(hereDocument => hereDocument.SubstitutionDepth > depth);
            current.Append(frame.SavedWord).Append(SubstitutionPlaceholder);
            quote = frame.SavedQuote;
            quotedFrom = frame.QuotedFrom;
            literalDollar = frame.LiteralDollar;
            awaitedDelimiterStripsTabs = frame.AwaitedDelimiterStripsTabs;
            hereStringAwaited = frame.HereStringAwaited;
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
        if (redirection.Kind == ShellTokenKind.HereText)
        {
            hereStringAwaited = true;
        }
        else if (redirection.Kind is ShellTokenKind kind)
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

    /// <summary>Reads, in order, the bodies of the here-documents opened in this substitution (or at the top) before this newline.</summary>
    private void ReadHereDocumentBodies()
    {
        int depth = SubstitutionDepth;
        foreach (PendingHereDocument hereDocument in pendingHereDocuments.Where(pending => pending.SubstitutionDepth == depth))
        {
            (string body, index) = ShellHereText.ReadHereDocumentBody(command, index, hereDocument.Delimiters, hereDocument.StripsTabs, joinsLines: !hereDocument.Quoted);
            tokens[hereDocument.TokenIndex] = tokens[hereDocument.TokenIndex] with { Text = body };
            if (!hereDocument.Quoted)
            {
                tokens.AddRange(BodySubstitutions(body));
            }
        }

        pendingHereDocuments.RemoveAll(pending => pending.SubstitutionDepth == depth);
    }

    /// <summary>The command substitutions an unquoted here-document body runs, as tokens; the rest of the body is text.</summary>
    private static List<ShellToken> BodySubstitutions(string body)
    {
        if (!body.Contains("$(", StringComparison.Ordinal) && !body.Contains('`', StringComparison.Ordinal))
        {
            return [];
        }

        ShellTokenizer reader = new(body, ShellDialect.Posix) { inHereDocumentBody = true, quote = '"', quotedFrom = 0 };
        List<ShellToken> substitutions = [];
        int depth = 0;
        foreach (ShellToken token in reader.Run())
        {
            depth += token.Kind switch
            {
                ShellTokenKind.SubstitutionOpen or ShellTokenKind.SubshellOpen or ShellTokenKind.GroupOpen => 1,
                ShellTokenKind.Close => -1,
                _ => 0,
            };
            if (depth > 0 || token.Kind == ShellTokenKind.Close)
            {
                substitutions.Add(token);
            }
        }

        return substitutions;
    }

    private void Flush()
    {
        if (current.Length == 0 && quotedFrom is null)
        {
            ResetWordMarks();
            return;
        }

        string word = current.ToString();
        current.Clear();
        if (hereStringAwaited)
        {
            hereStringAwaited = false;
            tokens.Add(new ShellToken(ShellTokenKind.HereText, word));
        }
        else if (awaitedDelimiterStripsTabs is bool stripsTabs)
        {
            // The body follows the logical line: its token stays with the command that reads it and gets its text at the newline.
            pendingHereDocuments.Add(new PendingHereDocument(tokens.Count, Delimiters(word), stripsTabs, Quoted: quotedFrom is not null, SubstitutionDepth));
            awaitedDelimiterStripsTabs = null;
            tokens.Add(new ShellToken(ShellTokenKind.HereText, string.Empty));
        }
        else
        {
            tokens.Add(new ShellToken(ShellTokenKind.Word, word) { QuotedFrom = quotedFrom, LiteralDollar = literalDollar });
        }

        ResetWordMarks();
    }

    /// <summary>The delimiter as bash reads it and, when it held a <c>$"…"</c>, as zsh reads it.</summary>
    private List<string> Delimiters(string word)
    {
        string zshWord = droppedDollars.Count == 0 ? word : droppedDollars.OrderDescending().Aggregate(word, (text, position) => text.Insert(position, "$"));
        return zshWord == word ? [word] : [word, zshWord];
    }

    private void ResetWordMarks()
    {
        quotedFrom = null;
        literalDollar = false;
        droppedDollars = [];
    }

    private char Peek(int offset) => index + offset < command.Length ? command[index + offset] : Nothing;

    private sealed record OpenFrame(char Closer, bool IsSubstitution, string SavedWord, char? SavedQuote)
    {
        public int? QuotedFrom { get; init; }

        public bool LiteralDollar { get; init; }

        public bool? AwaitedDelimiterStripsTabs { get; init; }

        public bool HereStringAwaited { get; init; }
    }

    /// <summary>
    /// A here-document whose body is still to be read: its token, its delimiter after quote removal (in each shell's reading),
    /// whether it strips leading tabs (<c>&lt;&lt;-</c>), whether any part of the delimiter was quoted, and the substitution it was
    /// opened in.
    /// </summary>
    private sealed record PendingHereDocument(int TokenIndex, IReadOnlyList<string> Delimiters, bool StripsTabs, bool Quoted, int SubstitutionDepth);
}
