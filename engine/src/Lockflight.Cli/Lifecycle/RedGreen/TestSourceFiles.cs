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
    public static IReadOnlyList<string> Find(string root, IReadOnlyList<string> testProjects, string className, string method) =>
        FindAll(root, testProjects, [(className, method)])[0];

    /// <summary>As <see cref="Find"/> for several tests, reading each source file once; the results are in the order of <paramref name="tests"/>.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> FindAll(string root, IReadOnlyList<string> testProjects, IReadOnlyList<(string ClassName, string Method)> tests)
    {
        (Regex DeclaresType, Regex DeclaresMethod)[] patterns = [.. tests.Select(test => Patterns(test.ClassName, test.Method))];
        List<string>[] found = [.. tests.Select(_ => new List<string>())];
        IEnumerable<string> files = testProjects
            .Select(project => project is "." or "" ? root : Path.Combine(root, project.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists)
            .SelectMany(SourceFiles)
            .Distinct(StringComparer.Ordinal);
        foreach (string file in files)
        {
            if (Read(file) is not string text)
            {
                continue;
            }

            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            for (int index = 0; index < patterns.Length; index++)
            {
                if (patterns[index].DeclaresType.IsMatch(text) && patterns[index].DeclaresMethod.IsMatch(text))
                {
                    found[index].Add(relative);
                }
            }
        }

        return [.. found.Select(paths => (IReadOnlyList<string>)[.. paths.Order(StringComparer.Ordinal)])];
    }

    /// <summary>Whether a C# source text declares the test's class and a method of that name, as <see cref="Find"/> decides it.</summary>
    public static bool Declares(string text, string className, string method)
    {
        (Regex declaresType, Regex declaresMethod) = Patterns(className, method);
        return declaresType.IsMatch(text) && declaresMethod.IsMatch(text);
    }

    private static (Regex DeclaresType, Regex DeclaresMethod) Patterns(string className, string method)
    {
        string typeName = className.Split('.', '+')[^1];
        int arity = typeName.IndexOf('`', StringComparison.Ordinal);
        typeName = arity >= 0 ? typeName[..arity] : typeName;
        Regex declaresType = new($@"\b(?:class|record|struct)\s+{Regex.Escape(typeName)}\b", RegexOptions.CultureInvariant);
        Regex declaresMethod = new(
            $@"\b(?!(?:{NotAType})\b)[\w>\]?)]+\s+{Regex.Escape(method)}\s*(?:<[^<>()]*>)?\s*\(", RegexOptions.CultureInvariant);
        return (declaresType, declaresMethod);
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

    private static string? Read(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
