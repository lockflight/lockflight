using System.Text.RegularExpressions;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// Finds the source file that declares a test method: a C# file under the test projects that declares the test's class (the
/// innermost one for a nested class) and a method of that name. Read from the source, not the built assembly, because an amend is
/// often needed exactly when the tests no longer compile. A partial class split over several files can match more than one file;
/// every match is returned, so the person sees each file the amend unlocks.
/// </summary>
internal static class TestSourceFiles
{
    private const string SourcePattern = "*.cs";

    /// <summary>Words that can stand before a call (<c>return Method(</c>), so a match after them is not a declaration.</summary>
    private const string NotAType = "return|await|new|throw|yield|else|case|in|is|as|not|and|or|when|nameof|typeof|goto";

    private static readonly HashSet<string> SkippedDirectories = new(["bin", "obj", ".git", ".lockflight", ".claude"], StringComparer.OrdinalIgnoreCase);

    /// <param name="testProjects">Test project directories relative to <paramref name="root"/>, '/'-separated ("." for the root).</param>
    /// <param name="className">The test's class as the runner names it: namespace-qualified, nested classes as <c>Outer+Inner</c>.</param>
    /// <returns>The matching files relative to the root, '/'-separated and ordered.</returns>
    public static IReadOnlyList<string> Find(string root, IReadOnlyList<string> testProjects, string className, string method)
    {
        string typeName = className.Split('.', '+')[^1];
        int arity = typeName.IndexOf('`', StringComparison.Ordinal);
        typeName = arity >= 0 ? typeName[..arity] : typeName;
        Regex declaresType = new($@"\b(?:class|record|struct)\s+{Regex.Escape(typeName)}\b", RegexOptions.CultureInvariant);
        Regex declaresMethod = new(
            $@"\b(?!(?:{NotAType})\b)[\w>\]?)]+\s+{Regex.Escape(method)}\s*(?:<[^<>()]*>)?\s*\(", RegexOptions.CultureInvariant);
        return [.. testProjects
            .Select(project => project is "." or "" ? root : Path.Combine(root, project.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists)
            .SelectMany(SourceFiles)
            .Distinct(StringComparer.Ordinal)
            .Where(file => Declares(file, declaresType, declaresMethod))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> SourceFiles(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, SourcePattern))
        {
            yield return file;
        }

        foreach (string child in Directory.EnumerateDirectories(directory).Where(child => !SkippedDirectories.Contains(Path.GetFileName(child))))
        {
            if (new DirectoryInfo(child).LinkTarget is not null)
            {
                continue;
            }

            foreach (string file in SourceFiles(child))
            {
                yield return file;
            }
        }
    }

    private static bool Declares(string file, Regex declaresType, Regex declaresMethod)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return declaresType.IsMatch(text) && declaresMethod.IsMatch(text);
    }
}
