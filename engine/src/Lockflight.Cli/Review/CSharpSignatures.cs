using System.Text;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Review;

/// <summary>
/// The declarations of a C# file without their bodies: namespaces, types, and the members a test can reach (everything not
/// marked <c>private</c>, since test projects often see internals), so a reviewer writing a reproduction knows the namespaces,
/// constructors and member signatures without guessing. A light scan over <see cref="CSharpMask"/>, not a compiler: comments,
/// attributes and string contents are dropped, bodies become <c>{ … }</c>, and the listing stops at a size limit.
/// </summary>
internal static partial class CSharpSignatures
{
    private const string Indent = "  ";
    private const string Truncated = "// … (truncated)";

    public static string Extract(string source, int maxCharacters)
    {
        string code = CSharpMask.Mask(source);
        StringBuilder listing = new();
        Stack<SignatureScope> scopes = new();
        int start = 0;
        int position = 0;
        while (position < code.Length && listing.Length <= maxCharacters)
        {
            char current = code[position];
            if (current == '}')
            {
                if (scopes.TryPop(out _))
                {
                    Append(listing, scopes.Count, "}");
                }

                start = ++position;
                continue;
            }

            if (current is ';' or '{' || (current == '=' && At(code, position + 1) == '>') || (current == '=' && IsAssignment(code, position)))
            {
                string header = Header(code[start..position]);
                position = Declaration(code, position, header, scopes, listing);
                start = position;
                continue;
            }

            if (current is '(' or '[' or '<')
            {
                position = SkipGroup(code, position);
                continue;
            }

            position++;
        }

        if (listing.Length > maxCharacters)
        {
            listing.Length = Math.Max(0, maxCharacters);
            listing.AppendLine().Append(Truncated);
        }

        return listing.ToString().TrimEnd();
    }

    /// <summary>Handles the declaration whose header ends at <paramref name="position"/> and returns where scanning goes on.</summary>
    private static int Declaration(string code, int position, string header, Stack<SignatureScope> scopes, StringBuilder listing)
    {
        int depth = scopes.Count;
        SignatureScope container = scopes.Count == 0 ? SignatureScope.Namespace : scopes.Peek();
        char current = code[position];
        if (current == ';')
        {
            if (header.Length > 0 && Visible(header, container) && !IsUsing(header) && !header.StartsWith('='))
            {
                Append(listing, depth, header + ";");
            }

            return position + 1;
        }

        if (current == '=')
        {
            // An expression body or an initializer: its end is the next ';' outside brackets.
            int end = StatementEnd(code, position);
            if (header.Length > 0 && Visible(header, container))
            {
                Append(listing, depth, header + (At(code, position + 1) == '>' ? " => …;" : " = …;"));
            }

            return end;
        }

        SignatureScope kind = KindOf(header);
        if (kind == SignatureScope.Enum)
        {
            int end = BlockEnd(code, position);
            if (Visible(header, container))
            {
                Append(listing, depth, $"{header} {{ {Collapse(code[(position + 1)..Math.Max(position + 1, end - 1)])} }}");
            }

            return end;
        }

        if (kind == SignatureScope.Code)
        {
            int end = BlockEnd(code, position);
            if (header.Length > 0 && Visible(header, container))
            {
                Append(listing, depth, header + " { … }");
            }

            return end;
        }

        if (!Visible(header, container))
        {
            return BlockEnd(code, position);
        }

        Append(listing, depth, header + " {");
        scopes.Push(kind);
        return position + 1;
    }

    private static SignatureScope KindOf(string header)
    {
        Match keyword = TypeKeyword().Match(header);
        if (!keyword.Success || header.IndexOf('(') is int paren and >= 0 && paren < keyword.Index)
        {
            return SignatureScope.Code;
        }

        return keyword.Groups["keyword"].Value switch
        {
            "namespace" => SignatureScope.Namespace,
            "interface" => SignatureScope.Interface,
            "enum" => SignatureScope.Enum,
            _ => SignatureScope.Type,
        };
    }

    private static bool Visible(string header, SignatureScope container)
    {
        string[] words = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Contains("private"))
        {
            return false;
        }

        return container is SignatureScope.Namespace or SignatureScope.Interface
            || words.Any(word => word is "public" or "internal" or "protected")
            || words.FirstOrDefault() is "namespace";
    }

    /// <summary>The declaration text without leading attributes, with whitespace collapsed.</summary>
    private static string Header(string text)
    {
        string header = Collapse(text);
        while (header.StartsWith('['))
        {
            int close = SkipGroup(header, 0);
            header = header[Math.Min(close, header.Length)..].TrimStart();
        }

        return header;
    }

    private static bool IsUsing(string header) =>
        header.StartsWith("using ", StringComparison.Ordinal) || header.StartsWith("global using ", StringComparison.Ordinal);

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();

    /// <summary>An '=' that assigns at the top level of a header (a field or property initializer), not '==', '!=', '&lt;=' or '&gt;='.</summary>
    private static bool IsAssignment(string code, int position) =>
        At(code, position + 1) != '=' && position > 0 && code[position - 1] is not ('=' or '!' or '<' or '>');

    /// <summary>The position after the brace that closes the block opening at <paramref name="open"/>.</summary>
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

    /// <summary>The position after the ';' that ends the statement, skipping brackets, braces and parentheses.</summary>
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
                    // The container closes without a ';': stop before its brace.
                    return index;
                case ';' when depth == 0:
                    return index + 1;
            }
        }

        return code.Length;
    }

    /// <summary>The position after the bracket that closes the group opening at <paramref name="open"/> ('(' , '[' or a generic '&lt;').</summary>
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
                // Not a generic argument list after all ('a < b'): resume right after the '<'.
                return open + 1;
            }
        }

        return code.Length;
    }

    private static void Append(StringBuilder listing, int depth, string line)
    {
        for (int level = 0; level < depth; level++)
        {
            listing.Append(Indent);
        }

        listing.Append(line).Append('\n');
    }

    private static char At(string text, int position) => position < text.Length ? text[position] : '\0';

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?:^|\s)(?<keyword>namespace|class|struct|interface|record|enum)(?:\s|$)")]
    private static partial Regex TypeKeyword();
}
