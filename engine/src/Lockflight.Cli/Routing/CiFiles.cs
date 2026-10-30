using System.Text.RegularExpressions;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Routing;

/// <summary>
/// CI definition files of the platforms Lockflight knows, and the version-only edits in them: a removed and an added line that
/// differ only in the ref after <c>uses: …@</c> or the tag after <c>image: …:</c>, and the comment after it (a dependency bot's pin
/// bump, which often writes the version as a comment next to a commit SHA).
/// </summary>
internal static partial class CiFiles
{
    private static readonly string[] Globs =
    [
        ".github/workflows/**", ".github/actions/**", ".gitlab-ci.yml", ".gitlab-ci*.yml", ".gitlab/ci/**", "azure-pipelines*.yml",
        ".azure-pipelines/**", "bitbucket-pipelines.yml",
    ];

    public static bool IsCiFile(string path) => Globs.Any(glob => PathRules.Matches(glob, path));

    /// <summary>The text with the ref of a <c>uses:</c> pin and the tag of an <c>image:</c> blanked.</summary>
    public static string WithoutPins(string text) =>
        ImagePin().Replace(UsesPin().Replace(WhitespacePairs.Collapse(text), "$1@\u0001"), "$1:\u0001");

    [GeneratedRegex(@"(\buses:\s*[^\s@#]+)@[^\s#]+(?:\s+#.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex UsesPin();

    [GeneratedRegex(@"(\bimage:\s*(?:name:\s*)?[^\s:#@]+(?::\d+/[^\s:#@]+)?):[^\s#]+(?:\s+#.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ImagePin();
}
