using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Workspace;

/// <summary>
/// The shell's protected paths as seen from inside a unit repository: the CI judge and the gates of a unit that points at its shell
/// (<see cref="ShellPolicyOverlay"/>) add the shell's globs that can match inside the unit, re-based to it.
/// </summary>
internal static class UnitPolicy
{
    private const string AnyDirectories = "**/";

    private const string AnyDepth = "**";

    /// <summary>
    /// A shell glob as seen from inside the unit: the globs that, matched against a path relative to the unit, protect every
    /// path the shell's glob protects there (empty when it cannot match inside the unit). The glob is read the way the guard
    /// reads it (<see cref="PathRules"/>): <c>**/</c> is any number of directories, any other <c>**</c> (<c>a**b</c> too) any
    /// characters across directories, <c>*</c> and <c>?</c> stay within a segment. The unit's path is matched against the glob
    /// character by character; each way the glob can be part-way through when the path is used up gives what is left of it,
    /// written back as a glob — the same paths, or wider (<c>**/</c> left part-way through a directory), never narrower.
    /// A glob that names the unit directory itself covers the whole unit.
    /// </summary>
    public static IReadOnlyList<string> Rebase(string glob, string unitPath)
    {
        string[] tokens = Tokens(UnicodeNormalization.Compose(glob.Replace('\\', '/')));
        string unit = UnicodeNormalization.Compose(unitPath.Replace('\\', '/').Trim('/'));
        HashSet<int> states = Closure(tokens, [0]);
        foreach (char character in unit)
        {
            states = Closure(tokens, Step(tokens, states, character));
        }

        if (states.Contains(tokens.Length))
        {
            return [AnyDepth];
        }

        // Stopped right after the unit's own directory: what is left of the glob from each of these points.
        HashSet<int> left = Step(tokens, states, '/');
        return [.. left.Order()
            .Where(index => !left.Any(other => other < index && Skippable(tokens, other, index)))
            .Select(index => string.Concat(tokens.Skip(index)))
            .Where(rest => rest.Length > 0)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The glob's tokens as the guard compiles them: every <c>**/</c> first, then <c>**</c>, <c>*</c>, <c>?</c> and single characters.</summary>
    private static string[] Tokens(string glob)
    {
        List<string> tokens = [];
        int position = 0;
        while (position <= glob.Length)
        {
            int next = glob.IndexOf(AnyDirectories, position, StringComparison.Ordinal);
            string chunk = next < 0 ? glob[position..] : glob[position..next];
            for (int index = 0; index < chunk.Length; index++)
            {
                if (chunk[index] == '*' && index + 1 < chunk.Length && chunk[index + 1] == '*')
                {
                    tokens.Add(AnyDepth);
                    index++;
                }
                else
                {
                    tokens.Add(chunk[index].ToString());
                }
            }

            if (next < 0)
            {
                break;
            }

            tokens.Add(AnyDirectories);
            position = next + AnyDirectories.Length;
        }

        return [.. tokens];
    }

    /// <summary>The positions reached from <paramref name="states"/> by reading one character of the path.</summary>
    private static HashSet<int> Step(string[] tokens, HashSet<int> states, char character)
    {
        HashSet<int> next = [];
        foreach (int state in states.Where(state => state < tokens.Length))
        {
            switch (tokens[state])
            {
                // Inside "**/" the position stays on the token: what is left is "**/…", wider than "the rest of a directory, then …".
                case AnyDirectories:
                    next.Add(state);
                    if (character == '/')
                    {
                        next.Add(state + 1);
                    }

                    break;
                case AnyDepth:
                    next.Add(state);
                    break;
                case "*":
                    if (character != '/')
                    {
                        next.Add(state);
                    }

                    break;
                case "?":
                    if (character != '/')
                    {
                        next.Add(state + 1);
                    }

                    break;
                case string literal:
                    if (char.ToUpperInvariant(literal[0]) == char.ToUpperInvariant(character))
                    {
                        next.Add(state + 1);
                    }

                    break;
            }
        }

        return next;
    }

    /// <summary>The states with every position a wildcard that may match nothing lets the path skip to.</summary>
    private static HashSet<int> Closure(string[] tokens, HashSet<int> states)
    {
        HashSet<int> closed = [.. states];
        foreach (int state in states)
        {
            for (int index = state; index < tokens.Length && IsWildcard(tokens[index]); index++)
            {
                closed.Add(index + 1);
            }
        }

        return closed;
    }

    /// <summary>Whether the tokens from <paramref name="from"/> up to <paramref name="to"/> may all match nothing (the rest from <paramref name="to"/> is then covered).</summary>
    private static bool Skippable(string[] tokens, int from, int to) => tokens[from..to].All(IsWildcard);

    private static bool IsWildcard(string token) => token is AnyDirectories or AnyDepth or "*";
}
