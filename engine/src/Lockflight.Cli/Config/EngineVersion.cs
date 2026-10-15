using System.Globalization;

namespace Lockflight.Cli.Config;

/// <summary>
/// An engine package version, ordered by Semantic Versioning 2.0 precedence (build metadata ignored, NuGet's case-insensitive
/// labels). Development builds carry a <c>dev</c> identifier after the version they follow: <c>0.2.0-alpha.4.dev.&lt;stamp&gt;</c>
/// after the prerelease <c>0.2.0-alpha.4</c>, <c>0.2.1-0.dev.&lt;stamp&gt;</c> after the release <c>0.2.0</c>. Either way a
/// development build sorts above the release it was built after and below the next one, so moving to a release is an upgrade.
/// </summary>
/// <param name="Prerelease">The dot-separated prerelease identifiers; empty for a release.</param>
internal sealed record EngineVersion(int Major, int Minor, int Patch, string Prerelease) : IComparable<EngineVersion>
{
    private const string DevelopmentIdentifier = "dev";

    /// <summary>The lowest prerelease identifier, kept when nothing but the development suffix was there.</summary>
    private const string LowestPrerelease = "0";

    /// <summary>Whether this is a development build, which no package feed has.</summary>
    public bool IsDevelopment => Identifiers().Any(identifier => identifier.Equals(DevelopmentIdentifier, StringComparison.OrdinalIgnoreCase));

    /// <summary>The version parsed from <paramref name="text"/>; null when it is not a version.</summary>
    public static EngineVersion? Parse(string text)
    {
        string version = ToolPin.WithoutBuildMetadata(text.Trim());
        int prereleaseStart = version.IndexOf('-', StringComparison.Ordinal);
        string core = prereleaseStart >= 0 ? version[..prereleaseStart] : version;
        string prerelease = prereleaseStart >= 0 ? version[(prereleaseStart + 1)..] : string.Empty;
        string[] numbers = core.Split('.');
        if (numbers.Length != 3 || !numbers.All(IsNumber) || (prereleaseStart >= 0 && !IsPrerelease(prerelease)))
        {
            return null;
        }

        return int.TryParse(numbers[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            && int.TryParse(numbers[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
            && int.TryParse(numbers[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch)
                ? new EngineVersion(major, minor, patch, prerelease)
                : null;
    }

    /// <summary>Whether <paramref name="version"/> names a development build; false when it is not a version.</summary>
    public static bool IsDevelopmentBuild(string version) => Parse(version)?.IsDevelopment == true;

    /// <summary>
    /// The version a development build follows: the prerelease identifiers before <c>dev</c> (<c>0.2.0-alpha.4</c>), or the lowest
    /// prerelease of its version when none are (<c>0.2.0-0</c> for the older <c>0.2.0-dev.&lt;stamp&gt;</c>). A release or
    /// prerelease that is not a development build is its own base.
    /// </summary>
    public EngineVersion DevelopmentBase()
    {
        if (!IsDevelopment)
        {
            return this;
        }

        string[] kept = [.. Identifiers().TakeWhile(identifier => !identifier.Equals(DevelopmentIdentifier, StringComparison.OrdinalIgnoreCase))];
        return this with { Prerelease = kept.Length == 0 ? LowestPrerelease : string.Join('.', kept) };
    }

    public int CompareTo(EngineVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0)
        {
            return core;
        }

        // A release outranks every prerelease of the same version.
        if (Prerelease.Length == 0 || other.Prerelease.Length == 0)
        {
            return (Prerelease.Length == 0).CompareTo(other.Prerelease.Length == 0);
        }

        string[] mine = Identifiers();
        string[] theirs = other.Identifiers();
        for (int index = 0; index < Math.Min(mine.Length, theirs.Length); index++)
        {
            int identifier = CompareIdentifiers(mine[index], theirs[index]);
            if (identifier != 0)
            {
                return identifier;
            }
        }

        return mine.Length.CompareTo(theirs.Length);
    }

    public override string ToString() => Prerelease.Length == 0 ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";

    private string[] Identifiers() => Prerelease.Length == 0 ? [] : Prerelease.Split('.');

    /// <summary>Numeric identifiers compare as numbers (of any length, such as a timestamp) and sort below alphanumeric ones.</summary>
    private static int CompareIdentifiers(string left, string right)
    {
        bool leftNumeric = IsNumber(left);
        bool rightNumeric = IsNumber(right);
        if (leftNumeric && rightNumeric)
        {
            string leftDigits = left.TrimStart('0');
            string rightDigits = right.TrimStart('0');
            return leftDigits.Length != rightDigits.Length
                ? leftDigits.Length.CompareTo(rightDigits.Length)
                : Math.Sign(string.CompareOrdinal(leftDigits, rightDigits));
        }

        return leftNumeric != rightNumeric
            ? (leftNumeric ? -1 : 1)
            : Math.Sign(string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNumber(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);

    private static bool IsPrerelease(string prerelease) =>
        prerelease.Split('.').All(identifier => identifier.Length > 0 && identifier.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
}
