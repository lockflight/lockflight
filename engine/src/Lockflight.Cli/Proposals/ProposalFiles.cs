using System.Globalization;
using System.Text;

namespace Lockflight.Cli.Proposals;

/// <summary>
/// Where proposals are written: <c>.lockflight/local/proposals/&lt;yyyyMMdd-HHmmss&gt;-&lt;slug&gt;.patch</c>, under the per-developer,
/// git-ignored local directory. Only the engine writes there; the guard keeps the agent out of <c>.lockflight/</c>.
/// </summary>
internal static class ProposalFiles
{
    public const string RelativeDirectory = ".lockflight/local/proposals";

    private const string Extension = ".patch";
    private const int MaxSlugLength = 60;
    private const string FallbackSlug = "file";

    /// <summary>A patch path no earlier proposal uses, named for the time and the proposed file.</summary>
    public static string NewPath(string projectRoot, DateTimeOffset now, string relativePath)
    {
        string directory = Path.Combine(projectRoot, RelativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        string stem = $"{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Slug(relativePath)}";
        string path = Path.Combine(directory, stem + Extension);
        for (int attempt = 2; File.Exists(path); attempt++)
        {
            path = Path.Combine(directory, $"{stem}-{attempt}{Extension}");
        }

        return path;
    }

    /// <summary>The path in lower-case letters, digits and single dashes (<c>src/Directory.Build.props</c> → <c>src-directory-build-props</c>).</summary>
    public static string Slug(string relativePath)
    {
        StringBuilder slug = new();
        foreach (char character in relativePath.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                slug.Append(character);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        string text = slug.ToString().Trim('-');
        text = text.Length > MaxSlugLength ? text[..MaxSlugLength].Trim('-') : text;
        return text.Length == 0 ? FallbackSlug : text;
    }
}
