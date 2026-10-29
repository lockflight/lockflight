using System.Text;
using System.Text.RegularExpressions;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Routing;

/// <summary>
/// The declarations of a C# file, private ones included, each without its body and with its attributes: a light scan over
/// <see cref="CSharpMask"/> (so a <c>public class</c> inside a comment or a string is no declaration), with each header's text
/// taken back from the source so string contents (a route template, a column name) stay part of it. Bodies of methods,
/// accessors and enums are skipped; both branches of an <c>#if</c> are read, as the masked text has them both. It is not a
/// compiler: unusual syntax may give an odd header, which can only make a declaration look changed (a higher class), never
/// hide one that is there.
/// </summary>
internal static partial class CSharpDeclarations
{
    private const string NamespaceKeyword = "namespace";
    private const string DelegateKeyword = "delegate";

    /// <summary>
    /// The declarations of every branch of the source's conditional compilation: read once keeping each <c>#if</c> group's first
    /// branch and once keeping its last, each declaration listed as often as the reading that has it most often.
    /// </summary>
    public static IReadOnlyList<CSharpDeclaration> Extract(string source)
    {
        IReadOnlyList<CSharpDeclaration> first = ExtractOne(PreprocessorBranches.Keep(source, firstBranch: true));
        if (!source.Contains("#if", StringComparison.Ordinal))
        {
            return first;
        }

        List<CSharpDeclaration> merged = [.. first];
        Dictionary<string, int> counts = first.GroupBy(declaration => declaration.Key).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (CSharpDeclaration declaration in ExtractOne(PreprocessorBranches.Keep(source, firstBranch: false)))
        {
            if (counts.GetValueOrDefault(declaration.Key) > 0)
            {
                counts[declaration.Key]--;
            }
            else
            {
                merged.Add(declaration);
            }
        }

        return merged;
    }

    private static List<CSharpDeclaration> ExtractOne(string source)
    {
        string code = CSharpMask.Mask(source);
        List<CSharpDeclaration> declarations = [];
        Stack<Scope> scopes = new();
        string fileNamespace = string.Empty;
        int start = 0;
        int position = 0;
        while (position < code.Length)
        {
            char current = code[position];
            if (current == '}')
            {
                scopes.TryPop(out _);
                start = ++position;
                continue;
            }

            if (current is ';' or '{' || (current == '=' && At(code, position + 1) == '>') || (current == '=' && IsAssignment(code, position)))
            {
                position = Declaration(source, code, start, position, scopes, ref fileNamespace, declarations);
                start = position;
                continue;
            }

            position = current is '(' or '[' or '<' ? SkipGroup(code, position) : position + 1;
        }

        return declarations;
    }

    private static int Declaration(
        string source, string code, int start, int position, Stack<Scope> scopes, ref string fileNamespace, List<CSharpDeclaration> declarations)
    {
        (List<string> attributes, int headerStart) = Attributes(source, code, start, position);
        string maskedText = code[headerStart..position];
        string restoredText = Restore(source, code, headerStart, position);
        string masked = Collapse(maskedText);
        string header = Collapse(restoredText);
        char terminator = code[position];
        int next = terminator switch
        {
            ';' => position + 1,
            '=' => StatementEnd(code, position),
            _ => position + 1,
        };

        if (masked.Length == 0 || IsDirective(masked))
        {
            return terminator == '{' ? BlockEnd(code, position) : next;
        }

        string container = Container(scopes, fileNamespace);
        bool inInterface = scopes.Count > 0 && scopes.Peek().IsInterface;
        int line = LineOf(source, FirstCode(code, start, position));
        if (masked.StartsWith(NamespaceKeyword + " ", StringComparison.Ordinal))
        {
            string namespaceName = header[(NamespaceKeyword.Length + 1)..].Trim();
            if (terminator == ';')
            {
                fileNamespace = namespaceName;
                return next;
            }

            scopes.Push(new Scope(namespaceName, IsInterface: false));
            return position + 1;
        }

        Match typeKeyword = TypeKeyword().Match(maskedText);
        bool isType = typeKeyword.Success && (maskedText.IndexOf('(') is int paren && (paren < 0 || paren > typeKeyword.Index));
        bool isDelegate = !isType && Words(masked).Contains(DelegateKeyword) && masked.Contains('(', StringComparison.Ordinal);
        if (isType && TypeName(maskedText, typeKeyword).Length == 0)
        {
            // A contextual keyword used as a name ('public Record record { get; }'): a member, not a type.
            isType = false;
        }

        DeclarationKind kind = isType || isDelegate ? DeclarationKind.Type : DeclarationKind.Member;
        string name = isType ? TypeName(maskedText, typeKeyword) : MemberName(masked);
        string baseList = isType ? Collapse(BaseListOf(maskedText, restoredText, typeKeyword.Index + typeKeyword.Length)) : string.Empty;
        string body = string.Empty;
        if (terminator == '{' && isType && typeKeyword.Groups["keyword"].Value == "enum")
        {
            int end = BlockEnd(code, position);
            body = " { " + Collapse(Restore(source, code, position + 1, Math.Max(position + 1, end - 1))) + " }";
            next = end;
        }
        else if (terminator == '{' && isType)
        {
            scopes.Push(new Scope(name, IsInterface: typeKeyword.Groups["keyword"].Value == "interface"));
        }
        else if (terminator == '{')
        {
            next = BlockEnd(code, position);
        }

        if (terminator == '=' && At(code, position + 1) != '>' && !isType && (Words(masked).Contains("const") || (Words(masked).Contains("static") && Words(masked).Contains("readonly"))))
        {
            // A constant's value is compiled into its callers, and a route can live in one: the value is part of the declaration.
            body = " = " + Collapse(Restore(source, code, position + 1, Math.Max(position + 1, next - 1)));
        }

        bool publicSurface = IsPublicSurface(Words(masked), inInterface);
        attributes.Sort(StringComparer.Ordinal);
        declarations.Add(new CSharpDeclaration(container, kind, name, header + body, attributes, baseList, line, publicSurface));
        return next;
    }

    /// <summary>The attributes in front of a header (each as written, collapsed) and where the header itself starts.</summary>
    private static (List<string> Attributes, int HeaderStart) Attributes(string source, string code, int start, int end)
    {
        List<string> attributes = [];
        int position = start;
        while (true)
        {
            while (position < end && char.IsWhiteSpace(code[position]))
            {
                position++;
            }

            if (position >= end || code[position] != '[')
            {
                return (attributes, position);
            }

            int close = Math.Min(SkipGroup(code, position), end);
            attributes.Add(Collapse(Restore(source, code, position, close)));
            position = close;
        }
    }

    /// <summary>The source between two positions with comments still blanked but string and character contents put back.</summary>
    private static string Restore(string source, string code, int start, int end)
    {
        StringBuilder text = new(end - start);
        bool inLiteral = false;
        char quote = '\0';
        for (int index = start; index < end; index++)
        {
            char masked = code[index];
            if (masked == '"' && QuoteRun(code, index) >= 3)
            {
                int run = QuoteRun(code, index);
                text.Append(code, index, run);
                inLiteral = !inLiteral;
                quote = '"';
                index += run - 1;
                continue;
            }

            if (!inLiteral && masked is '"' or '\'' && source[index] == masked)
            {
                inLiteral = true;
                quote = masked;
                text.Append(masked);
                continue;
            }

            if (inLiteral && masked == quote && source[index] == quote)
            {
                inLiteral = false;
                text.Append(masked);
                continue;
            }

            text.Append(inLiteral ? source[index] : masked);
        }

        return text.ToString();
    }

    private static int QuoteRun(string code, int start)
    {
        int end = start;
        while (end < code.Length && code[end] == '"')
        {
            end++;
        }

        return end - start;
    }

    private static string Container(Stack<Scope> scopes, string fileNamespace)
    {
        IEnumerable<string> names = scopes.Reverse().Select(scope => scope.Name);
        return string.Join('.', (fileNamespace.Length == 0 ? names : names.Prepend(fileNamespace)).Where(name => name.Length > 0));
    }

    private static bool IsPublicSurface(string[] words, bool inInterface)
    {
        if (words.Contains("private"))
        {
            return false;
        }

        return inInterface || words.Contains("public") || words.Contains("protected");
    }

    private static string TypeName(string masked, Match keyword)
    {
        string rest = masked[(keyword.Index + keyword.Length)..].TrimStart();
        Match name = Identifier().Match(rest);
        if (!name.Success)
        {
            return string.Empty;
        }

        int after = name.Index + name.Length;
        if (after < rest.Length && rest[after] == '<')
        {
            int close = SkipGroup(rest, after);
            return name.Value + "`" + (rest[after..close].Count(character => character == ',') + 1);
        }

        return name.Value;
    }

    private static string MemberName(string masked)
    {
        int paren = TopLevelIndex(masked, '(');
        string head = (paren < 0 ? masked : masked[..paren]).TrimEnd();
        if (head.EndsWith('>'))
        {
            int open = head.LastIndexOf('<');
            head = open < 0 ? head : head[..open].TrimEnd();
        }

        Match last = LastIdentifier().Match(head);
        return last.Success ? last.Groups["name"].Value : head;
    }

    /// <summary>
    /// The base list of a type header: after the first ':' outside brackets, up to a <c>where</c> clause. The masked and the restored
    /// header are the same length, so a position in one is the same place in the other.
    /// </summary>
    private static string BaseListOf(string masked, string header, int from)
    {
        int colon = -1;
        int depth = 0;
        for (int index = from; index < masked.Length && index < header.Length; index++)
        {
            char current = masked[index];
            if (current is '(' or '<' or '[')
            {
                depth++;
            }
            else if (current is ')' or '>' or ']')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (current == ':' && depth == 0)
            {
                colon = index;
                break;
            }
        }

        if (colon < 0)
        {
            return string.Empty;
        }

        Match whereClause = WhereClause().Match(masked, colon);
        int where = whereClause.Success ? whereClause.Index : -1;
        int end = Math.Min(where < 0 ? masked.Length : where, header.Length);
        return header[(colon + 1)..end];
    }

    private static int TopLevelIndex(string text, char target)
    {
        int depth = 0;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (current == target && depth == 0)
            {
                return index;
            }

            if (current is '<' or '[')
            {
                depth++;
            }
            else if (current is '>' or ']')
            {
                depth = Math.Max(0, depth - 1);
            }
        }

        return -1;
    }

    private static bool IsDirective(string masked) =>
        masked.StartsWith("using ", StringComparison.Ordinal) || masked.StartsWith("global using ", StringComparison.Ordinal)
        || masked.StartsWith("extern alias ", StringComparison.Ordinal);

    private static string[] Words(string text) => text.Split([' ', '(', '<', '['], StringSplitOptions.RemoveEmptyEntries);

    private static int FirstCode(string code, int start, int end)
    {
        for (int index = start; index < end; index++)
        {
            if (!char.IsWhiteSpace(code[index]))
            {
                return index;
            }
        }

        return start;
    }

    private static int LineOf(string source, int position)
    {
        int line = 1;
        for (int index = 0; index < position && index < source.Length; index++)
        {
            if (source[index] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>Whitespace collapsed, and none next to brackets, commas and colons, so a reflowed header reads the same.</summary>
    private static string Collapse(string text) => AroundPunctuation().Replace(Whitespace().Replace(text, " ").Trim(), "$1");

    private static bool IsAssignment(string code, int position) =>
        At(code, position + 1) != '=' && position > 0 && code[position - 1] is not ('=' or '!' or '<' or '>');

    private static int BlockEnd(string code, int open)
    {
        int depth = 0;
        for (int position = open; position < code.Length; position++)
        {
            if (code[position] == '{')
            {
                depth++;
            }
            else if (code[position] == '}' && --depth == 0)
            {
                return position + 1;
            }
        }

        return code.Length;
    }

    private static int StatementEnd(string code, int position)
    {
        int depth = 0;
        for (int index = position; index < code.Length; index++)
        {
            switch (code[index])
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}' when depth > 0:
                    depth--;
                    break;
                case '}':
                    return index;
                case ';' when depth == 0:
                    return index + 1;
            }
        }

        return code.Length;
    }

    private static int SkipGroup(string code, int open)
    {
        char opening = code[open];
        char closing = opening switch { '(' => ')', '[' => ']', _ => '>' };
        int depth = 0;
        for (int position = open; position < code.Length; position++)
        {
            char current = code[position];
            if (current == opening)
            {
                depth++;
            }
            else if (current == closing && --depth == 0)
            {
                return position + 1;
            }
            else if (opening == '<' && current is ';' or '{' or '}' or '=' && !(current == '=' && At(code, position + 1) == '>'))
            {
                return open + 1;
            }
        }

        return code.Length;
    }

    private static char At(string text, int position) => position < text.Length ? text[position] : '\0';

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@" ?([()\[\]<>,:]) ?")]
    private static partial Regex AroundPunctuation();

    [GeneratedRegex(@"\swhere\s")]
    private static partial Regex WhereClause();

    [GeneratedRegex(@"(?:^|\s)(?<keyword>class|struct|interface|record|enum)(?:\s+(?:class|struct))?(?=\s)")]
    private static partial Regex TypeKeyword();

    [GeneratedRegex(@"@?[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"(?<name>@?[A-Za-z_][A-Za-z0-9_]*|this)\s*$")]
    private static partial Regex LastIdentifier();

    private sealed record Scope(string Name, bool IsInterface);
}
