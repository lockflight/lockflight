using System.Text;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Review;

/// <summary>
/// What the review prompt tells the reviewer for writing reproductions that compile, read from the repository without building:
/// the test projects (frameworks, references, the using directives the engine adds), the signatures of the changed C# files,
/// and one sample test. Every part is bounded; the reviewer can read more files itself.
/// </summary>
internal static partial class ReproductionContext
{
    private const int MaxChangedFiles = 12;
    private const int MaxFileSignatureCharacters = 4_000;
    private const int MaxSignatureCharacters = 12_000;
    private const int MaxListedUsings = 30;

    public static string Build(string root, string diff)
    {
        IReadOnlyList<string> projects = TestProjectProfiles.Find(root);
        StringBuilder context = new();
        if (projects.Count > 0)
        {
            context.Append("<test-projects>\n");
            foreach (string project in projects)
            {
                context.Append(Describe(TestProjectProfiles.Describe(project, root))).Append('\n');
            }

            context.Append("</test-projects>\n\n");
        }

        string signatures = Signatures(root, ChangedFiles(diff));
        if (signatures.Length > 0)
        {
            context.Append("<signatures>\n").Append(signatures).Append("</signatures>\n\n");
        }

        if (projects.Count > 0 && TestProjectProfiles.SampleTest(Path.GetDirectoryName(projects[0])!, root) is string sample)
        {
            context.Append("<sample-test>\n").Append(sample).Append("</sample-test>\n");
        }

        return context.ToString().TrimEnd();
    }

    public static string Describe(TestProjectProfile profile)
    {
        string references = profile.ProjectReferences.Count == 0 ? "no projects" : string.Join(", ", profile.ProjectReferences);
        string packages = profile.PackageReferences.Count == 0 ? "none listed" : string.Join(", ", profile.PackageReferences);
        string usings = string.Join(' ', profile.Usings.Take(MaxListedUsings)) + (profile.Usings.Count > MaxListedUsings ? " …" : string.Empty);
        return $"- {profile.Path}: {profile.TargetFrameworks}, {profile.TestFramework}; references {references}; packages {packages}\n  usings: {usings}";
    }

    /// <summary>The C# files the diff changes and still exist, in diff order (at most <see cref="MaxChangedFiles"/>).</summary>
    public static IReadOnlyList<string> ChangedFiles(string diff) =>
        [.. ChangedFile().Matches(diff).Select(match => match.Groups["path"].Value.TrimEnd('\r')).Distinct(StringComparer.Ordinal).Take(MaxChangedFiles)];

    private static string Signatures(string root, IReadOnlyList<string> files)
    {
        StringBuilder listing = new();
        foreach (string file in files)
        {
            string path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || listing.Length >= MaxSignatureCharacters)
            {
                continue;
            }

            string signatures = CSharpSignatures.Extract(File.ReadAllText(path), Math.Min(MaxFileSignatureCharacters, MaxSignatureCharacters - listing.Length));
            if (signatures.Length > 0)
            {
                listing.Append("// ").Append(file).Append('\n').Append(signatures).Append("\n\n");
            }
        }

        return listing.ToString();
    }

    [GeneratedRegex(@"^\+\+\+ b/(?<path>[^\n]+\.cs)\r?$", RegexOptions.Multiline)]
    private static partial Regex ChangedFile();
}
