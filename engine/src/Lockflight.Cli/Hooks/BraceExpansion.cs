namespace Lockflight.Cli.Hooks;

/// <summary>
/// Bash brace expansion of a write target (<c>secrets/{a,b}</c> → <c>secrets/a</c>, <c>secrets/b</c>). A <c>${…}</c> is a variable,
/// not a brace list. Quoted braces were already removed by the tokenizer, so a quoted <c>{a,b}</c> is expanded too: an extra path
/// to check, never a missed one.
/// </summary>
internal static class BraceExpansion
{
    public const int MaxExpansions = 64;

    /// <summary>The words the target expands to; null when there are more than <see cref="MaxExpansions"/>.</summary>
    public static IReadOnlyList<string>? Expand(string word)
    {
        List<string> pending = [word];
        List<string> done = [];
        while (pending.Count > 0)
        {
            string current = pending[^1];
            pending.RemoveAt(pending.Count - 1);
            if (FindList(current) is not (int open, int close, List<string> alternatives))
            {
                done.Add(current);
            }
            else
            {
                pending.AddRange(alternatives.Select(alternative => current[..open] + alternative + current[(close + 1)..]));
            }

            if (done.Count + pending.Count > MaxExpansions)
            {
                return null;
            }
        }

        return done;
    }

    /// <summary>The first brace list with a top-level comma: its braces' positions and its alternatives.</summary>
    private static (int Open, int Close, List<string> Alternatives)? FindList(string word)
    {
        for (int open = word.IndexOf('{', StringComparison.Ordinal); open >= 0; open = word.IndexOf('{', open + 1))
        {
            if (open > 0 && word[open - 1] == '$')
            {
                continue;
            }

            int depth = 0;
            int start = open + 1;
            List<string> alternatives = [];
            for (int index = open + 1; index < word.Length; index++)
            {
                char character = word[index];
                if (character == '{')
                {
                    depth++;
                }
                else if (character == '}' && depth > 0)
                {
                    depth--;
                }
                else if (character == ',' && depth == 0)
                {
                    alternatives.Add(word[start..index]);
                    start = index + 1;
                }
                else if (character == '}')
                {
                    if (alternatives.Count == 0)
                    {
                        break;
                    }

                    alternatives.Add(word[start..index]);
                    return (open, index, alternatives);
                }
            }
        }

        return null;
    }
}
