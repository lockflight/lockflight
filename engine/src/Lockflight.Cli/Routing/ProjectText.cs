using System.Text.RegularExpressions;

namespace Lockflight.Cli.Routing;

/// <summary>
/// What routing reads from MSBuild project text without evaluating it. XML comments are dropped first, so a commented-out package
/// reference changes nothing.
/// </summary>
internal static partial class ProjectText
{
    /// <summary>
    /// Whether the text makes its project a test project: it references a test SDK or framework package (<c>Microsoft.NET.Test.Sdk</c>,
    /// <c>xunit</c>, <c>xunit.v3</c>, <c>NUnit</c>, <c>MSTest</c>), uses the <c>MSTest.Sdk</c> project SDK, or sets
    /// <c>&lt;IsTestProject&gt;true</c>.
    /// </summary>
    public static bool MakesTestProject(string text)
    {
        string code = WithoutComments(text);
        return TestPackage().IsMatch(code) || TestSdk().IsMatch(code) || IsTestProjectProperty().IsMatch(code);
    }

    public static string WithoutComments(string text) => XmlComment().Replace(text, string.Empty);

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComment();

    [GeneratedRegex(@"<PackageReference\b[^>]*\bInclude\s*=\s*""\s*(?:Microsoft\.NET\.Test\.Sdk|xunit|xunit\.core|xunit\.v3(?:\.[\w.-]+)?|NUnit|MSTest|MSTest\.TestFramework)\s*""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TestPackage();

    [GeneratedRegex(@"<Project\b[^>]*\bSdk\s*=\s*""[^""]*\bMSTest\.Sdk\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TestSdk();

    [GeneratedRegex(@"<IsTestProject\b[^>]*>\s*true\s*</IsTestProject>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IsTestProjectProperty();
}
