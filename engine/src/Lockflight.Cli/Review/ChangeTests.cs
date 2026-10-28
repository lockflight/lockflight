using System.Globalization;
using System.Text.RegularExpressions;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Review;

/// <summary>
/// The change's own tests: the listed test methods whose declaration (from the test attribute to the end of the body) holds a line
/// the diff adds, in a C# file of a test project. Read from the working tree's files and the diff's new-side line numbers, not
/// from the build; a test the diff only deletes is not one of them. The class is matched by its name in the file, so a nested
/// class or two classes of the same name in different namespaces may add a test too many, never one too few.
/// </summary>
internal static partial class ChangeTests
{
    public static IReadOnlyList<TestCase> Find(string diff, string root, IReadOnlyList<TestApp> apps, IReadOnlyList<TestCase> tests)
    {
        HashSet<(string Class, string Method)> changed = [];
        Dictionary<string, IReadOnlySet<int>> added = AddedLines(diff);
        foreach (string path in Files(diff, root, apps))
        {
            changed.UnionWith(ChangedMethods(File.ReadAllText(Full(root, path)), added[path]));
        }

        return [.. tests.Where(test => changed.Contains((ShortName(test.Class), test.Method)))];
    }

    /// <summary>The C# files of test projects the diff adds lines to and that still exist, relative to the root, '/'-separated.</summary>
    public static IReadOnlyList<string> Files(string diff, string root, IReadOnlyList<TestApp> apps) =>
        [.. AddedLines(diff).Keys
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && File.Exists(Full(root, path)) && apps.Any(app => IsUnder(Full(root, path), app.ProjectDirectory)))
            .Order(StringComparer.Ordinal)];

    private static string Full(string root, string path) => Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Each file's added lines on the new side, by the file's path relative to the root.</summary>
    private static Dictionary<string, IReadOnlySet<int>> AddedLines(string diff)
    {
        Dictionary<string, IReadOnlySet<int>> files = new(StringComparer.Ordinal);
        HashSet<int>? current = null;
        int line = 0;
        foreach (string text in diff.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (text.StartsWith("+++ ", StringComparison.Ordinal))
            {
                string path = text[4..].Split('\t')[0].Trim();
                current = path.StartsWith("b/", StringComparison.Ordinal) ? [] : null;
                if (current is not null)
                {
                    files[path[2..]] = current;
                }

                continue;
            }

            if (current is null || text.StartsWith("--- ", StringComparison.Ordinal) || text.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                continue;
            }

            if (NewStart().Match(text) is { Success: true } header)
            {
                line = int.Parse(header.Groups["start"].Value, CultureInfo.InvariantCulture);
            }
            else if (text.StartsWith('+'))
            {
                current.Add(line++);
            }
            else if (text.StartsWith(' ') || text.Length == 0)
            {
                line++;
            }
        }

        return files;
    }

    /// <summary>(class name, method name) of each test method whose lines hold one of <paramref name="added"/>.</summary>
    private static IEnumerable<(string Class, string Method)> ChangedMethods(string source, IReadOnlySet<int> added)
    {
        string code = CSharpMask.Mask(source);
        foreach (Match test in Reproductions.TestAttributedMethod().Matches(code))
        {
            int first = LineAt(code, test.Index);
            int last = LineAt(code, BodyEnd(code, test.Index + test.Length));
            if (added.Any(line => line >= first && line <= last)
                && TypeDeclaration().Matches(code[..test.Index]).LastOrDefault() is { } type)
            {
                yield return (type.Groups["name"].Value, test.Groups["name"].Value);
            }
        }
    }

    /// <summary>The position of the end of the method whose parameter list opens just before <paramref name="from"/>: its closing brace, or the semicolon of an expression body.</summary>
    private static int BodyEnd(string code, int from)
    {
        int depth = 1;
        for (int position = from; position < code.Length; position++)
        {
            switch (code[position])
            {
                case '(' or '{' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth--;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return position;
                    }

                    break;
                case ';' when depth <= 0:
                    return position;
            }
        }

        return code.Length - 1;
    }

    private static int LineAt(string code, int position) => code.AsSpan(0, Math.Min(position, code.Length)).Count('\n') + 1;

    private static string ShortName(string className) => className.Split('.', '+')[^1].Split('`')[0];

    private static bool IsUnder(string path, string directory)
    {
        string parent = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(?<start>\d+)(?:,\d+)? @@")]
    private static partial Regex NewStart();

    [GeneratedRegex(@"\b(?:class|record|struct)\s+(?<name>\w+)")]
    private static partial Regex TypeDeclaration();
}
