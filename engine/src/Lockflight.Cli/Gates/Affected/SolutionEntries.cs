using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// The projects a solution lists, as it writes their paths (relative to the solution, with its separators): the
/// <c>Project</c> elements of a <c>.slnx</c>, the <c>Project(…)</c> lines of a <c>.sln</c> other than solution folders. A solution
/// filter lists the same strings, so the entries are what <see cref="SolutionFilter"/> writes. Null when the file cannot be read.
/// </summary>
internal static partial class SolutionEntries
{
    private const string SolutionFolderType = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    public static IReadOnlyList<string>? Read(string solutionFile)
    {
        try
        {
            return solutionFile.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ? ReadSlnx(solutionFile) : ReadSln(solutionFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    /// <summary>The solution entry as a full path on this machine.</summary>
    public static string FullPath(string solutionFile, string entry) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(solutionFile)!, entry.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));

    private static IReadOnlyList<string> ReadSlnx(string solutionFile) =>
        [.. XDocument.Load(solutionFile).Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attribute("Path")?.Value)
            .OfType<string>()];

    private static IReadOnlyList<string> ReadSln(string solutionFile) =>
        [.. ProjectLine().Matches(File.ReadAllText(solutionFile))
            .Where(match => !match.Groups["type"].Value.Equals(SolutionFolderType, StringComparison.OrdinalIgnoreCase))
            .Select(match => match.Groups["path"].Value)];

    [GeneratedRegex(@"^Project\(""\{(?<type>[^}]+)\}""\)\s*=\s*""[^""]*""\s*,\s*""(?<path>[^""]+)""", RegexOptions.Multiline)]
    private static partial Regex ProjectLine();
}
