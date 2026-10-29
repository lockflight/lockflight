using System.Text.RegularExpressions;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Gates.Suppressions;

/// <summary>
/// The suppressions a line holds, by file type. C# is read through <see cref="CSharpMask"/> with each line's leading <c>#</c> kept as
/// code, so a directive or attribute in a comment or a string is not one, while the attribute's arguments are read from the source.
/// MSBuild files are read for <c>&lt;NoWarn&gt;</c> and <c>&lt;WarningsNotAsErrors&gt;</c> (property or item attribute) on one line,
/// XML comments left out; MSBuild properties such as <c>$(NoWarn)</c> are not diagnostics. Analyzer config files yield every
/// severity setting, and <see cref="SuppressionScan"/> decides which one lowers a severity. A light, line-based scan, not a parser.
/// </summary>
internal static partial class SuppressionPatterns
{
    public const string AllRules = "all";
    public const string NullableRule = "nullable";
    public const string CoverageRule = "ExcludeFromCodeCoverage";

    /// <summary>The severity index of <c>warning</c>.</summary>
    public const int WarningSeverity = 3;

    /// <summary>What the severities are called, lowest first: below <see cref="WarningSeverity"/> a diagnostic no longer shows in a build.</summary>
    private static readonly string[] Severities = ["none", "silent", "suggestion", "warning", "error"];

    private static readonly string[] MsBuildExtensions = [".csproj", ".vbproj", ".fsproj", ".props", ".targets"];

    /// <summary>The git pathspecs of the files that can hold a suppression.</summary>
    public static IReadOnlyList<string> Pathspecs { get; } = ["*.cs", "*.csproj", "*.vbproj", "*.fsproj", "*.props", "*.targets", "*.editorconfig", "*.globalconfig"];

    public static bool IsCSharp(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    public static bool IsMsBuild(string path) => MsBuildExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public static bool IsAnalyzerConfig(string path)
    {
        string name = path[(path.LastIndexOf('/') + 1)..];
        return name is ".editorconfig" or ".globalconfig" || name.EndsWith(".globalconfig", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanHoldSuppressions(string path) => IsCSharp(path) || IsMsBuild(path) || IsAnalyzerConfig(path);

    /// <summary>
    /// The masked lines of a C# source (same count and lengths as its lines): comments and literal contents blanked, a leading
    /// <c>#</c> turned into <c>_</c> first so that a directive outside a comment survives the mask.
    /// </summary>
    public static string[] MaskedCSharpLines(string source)
    {
        string[] lines = source.Split('\n');
        string prepared = string.Join('\n', lines.Select(line => DirectiveStart().Replace(line, "${indent}_", 1)));
        return CSharpMask.Mask(prepared).Split('\n');
    }

    /// <summary>The suppressions in one line of a file; <paramref name="masked"/> is the line from <see cref="MaskedCSharpLines"/> for C#.</summary>
    public static IReadOnlyList<SuppressionHit> InLine(string path, string raw, string masked)
    {
        if (IsCSharp(path))
        {
            return InCSharp(raw.TrimEnd('\r'), masked.TrimEnd('\r'));
        }

        if (IsMsBuild(path))
        {
            return InMsBuild(raw.TrimEnd('\r'));
        }

        return IsAnalyzerConfig(path) && InAnalyzerConfig(raw.TrimEnd('\r')) is SuppressionHit setting ? [setting] : [];
    }

    private static List<SuppressionHit> InCSharp(string raw, string masked)
    {
        List<SuppressionHit> hits = [];
        if (PragmaDisable().Match(masked) is { Success: true } pragma)
        {
            string[] codes = Codes(pragma.Groups["codes"].Value);
            hits.AddRange(codes.Length == 0 ? [new SuppressionHit(AllRules, SuppressionKind.Pragma)] : codes.Select(code => new SuppressionHit(code, SuppressionKind.Pragma)));
        }

        if (NullableDisable().IsMatch(masked))
        {
            hits.Add(new SuppressionHit(NullableRule, SuppressionKind.NullableDisable));
        }

        foreach (Match attribute in SuppressMessageAttribute().Matches(masked))
        {
            Match arguments = SuppressMessageArguments().Match(raw[(attribute.Index + attribute.Length)..]);
            string rule = arguments.Success ? arguments.Groups["check"].Value.Split(':')[0].Trim() : string.Empty;
            hits.Add(new SuppressionHit(rule.Length > 0 ? rule : "SuppressMessage", SuppressionKind.SuppressMessage));
        }

        if (ExcludeFromCodeCoverageAttribute().IsMatch(masked))
        {
            hits.Add(new SuppressionHit(CoverageRule, SuppressionKind.ExcludeFromCodeCoverage));
        }

        return hits;
    }

    private static List<SuppressionHit> InMsBuild(string raw)
    {
        string line = XmlComment().Replace(raw, string.Empty);
        List<SuppressionHit> hits = [];
        foreach (Match property in MsBuildList().Matches(line))
        {
            SuppressionKind kind = property.Groups["name"].Value == "NoWarn" ? SuppressionKind.NoWarn : SuppressionKind.WarningsNotAsErrors;
            hits.AddRange(Codes(property.Groups["codes"].Value).Select(code => new SuppressionHit(code, kind)));
        }

        foreach (Match attribute in NoWarnAttribute().Matches(line))
        {
            hits.AddRange(Codes(attribute.Groups["codes"].Value).Select(code => new SuppressionHit(code, SuppressionKind.NoWarn)));
        }

        return hits;
    }

    private static SuppressionHit? InAnalyzerConfig(string raw)
    {
        if (SeveritySetting().Match(raw) is not { Success: true } setting)
        {
            return null;
        }

        int severity = Array.IndexOf(Severities, setting.Groups["level"].Value.ToLowerInvariant());
        if (severity < 0)
        {
            return null;
        }

        string rule = setting.Groups["id"].Success ? setting.Groups["id"].Value
            : setting.Groups["category"].Success ? $"category-{setting.Groups["category"].Value}"
            : AllRules;
        return new SuppressionHit(rule, SuppressionKind.LoweredSeverity, setting.Groups["key"].Value.ToLowerInvariant(), severity);
    }

    /// <summary>Diagnostic ids in a list separated by commas, semicolons or spaces; MSBuild properties and items are not ids.</summary>
    private static string[] Codes(string list) =>
        [.. list.Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(code => !code.StartsWith("$(", StringComparison.Ordinal) && !code.StartsWith("@(", StringComparison.Ordinal) && !code.StartsWith("%(", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)];

    [GeneratedRegex(@"^(?<indent>\s*)#")]
    private static partial Regex DirectiveStart();

    [GeneratedRegex(@"^\s*_\s*pragma\s+warning\s+disable\b(?<codes>.*)$")]
    private static partial Regex PragmaDisable();

    [GeneratedRegex(@"^\s*_\s*nullable\s+disable\b")]
    private static partial Regex NullableDisable();

    [GeneratedRegex(@"(?<![\w.])(?:global::)?(?:System\.Diagnostics\.CodeAnalysis\.)?(?:Unconditional)?SuppressMessage(?:Attribute)?\s*\(")]
    private static partial Regex SuppressMessageAttribute();

    /// <summary>The attribute's first two arguments, read from the source after its opening parenthesis: category and check id.</summary>
    [GeneratedRegex(@"^\s*@?""(?<category>[^""]*)""\s*,\s*@?""(?<check>[^""]*)""")]
    private static partial Regex SuppressMessageArguments();

    [GeneratedRegex(@"(?<![\w.])(?:global::)?(?:System\.Diagnostics\.CodeAnalysis\.)?ExcludeFromCodeCoverage(?:Attribute)?\b")]
    private static partial Regex ExcludeFromCodeCoverageAttribute();

    [GeneratedRegex(@"<!--.*?(?:-->|$)")]
    private static partial Regex XmlComment();

    [GeneratedRegex(@"<(?<name>NoWarn|WarningsNotAsErrors)(?:\s[^>]*)?>(?<codes>[^<]*)</\k<name>\s*>")]
    private static partial Regex MsBuildList();

    [GeneratedRegex(@"\sNoWarn\s*=\s*""(?<codes>[^""]*)""")]
    private static partial Regex NoWarnAttribute();

    [GeneratedRegex(@"^\s*(?<key>dotnet_diagnostic\.(?<id>[^.\s=]+)\.severity|dotnet_analyzer_diagnostic(?:\.category-(?<category>[^.\s=]+))?\.severity)\s*=\s*(?<level>[A-Za-z]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SeveritySetting();
}
