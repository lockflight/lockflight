using System.Text.RegularExpressions;

namespace Lockflight.Cli.Adoption;

/// <summary>
/// The sources a Spec Kit version is installed from, in order: PyPI first, then the version's tag on GitHub. Some releases
/// (<c>1.0.0</c>, <c>1.1.0</c>) were never published to PyPI, and projects installed from git carry versions PyPI lacks, so
/// the tag is the fallback. A version is used only when it looks like a version: it comes from a project file, and the
/// requirement must not be able to name another package or URL.
/// </summary>
internal static partial class SpecKitSources
{
    /// <summary>The Spec Kit package name on PyPI.</summary>
    public const string PackageName = "specify-cli";

    /// <summary>The Spec Kit repository, whose tags are <c>v&lt;version&gt;</c>.</summary>
    public const string Repository = "https://github.com/github/spec-kit";

    /// <summary>The sources to try for a version; none when it is not a plain version string.</summary>
    public static IReadOnlyList<SpecKitSource> For(string version)
    {
        if (!VersionText().IsMatch(version))
        {
            return [];
        }

        SpecKitSource packageIndex = new($"{PackageName}=={version}", "PyPI");
        return ReleaseText().IsMatch(version)
            ? [packageIndex, new SpecKitSource($"git+{Repository}@v{version}", $"GitHub tag v{version}")]
            : [packageIndex];
    }

    /// <summary>Digits, letters, dots, plus and hyphen only (PEP 440 versions and local labels).</summary>
    [GeneratedRegex(@"^\d+(\.\d+)*[0-9A-Za-z.+-]*$")]
    private static partial Regex VersionText();

    /// <summary>A release such as <c>1.1.0</c>: the versions that have a tag.</summary>
    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex ReleaseText();
}
