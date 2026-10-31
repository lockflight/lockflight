using System.Text.RegularExpressions;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Sorts the changed lines of an MSBuild file (<c>*.csproj</c>, <c>*.props</c>, <c>*.targets</c>) by what they do. A removed and an
/// added line that are equal once their version values are blanked (<c>Version="…"</c>, <c>VersionOverride="…"</c>,
/// <c>&lt;Version&gt;…&lt;/Version&gt;</c> and the other version properties) are a version-only edit: both are
/// <see cref="ProjectLineKind.Package"/>. A line that holds nothing once its XML comments and bare <c>&lt;ItemGroup&gt;</c> or
/// <c>&lt;PropertyGroup&gt;</c> tags (without attributes; they only wrap the lines that do something) are removed is no change.
/// </summary>
internal static partial class ProjectFileLines
{
    private const string VersionPlaceholder = "\u0001";

    public static IReadOnlyList<ProjectLine> Of(IReadOnlyList<RoutedLine> added, IReadOnlyList<RoutedLine> removed)
    {
        List<RoutedLine> addedCode = [.. added.Where(IsCode)];
        List<RoutedLine> removedCode = [.. removed.Where(IsCode)];
        HashSet<RoutedLine> versionOnly = VersionOnlyPairs(addedCode, removedCode);
        return
        [
            .. removedCode.Select(line => new ProjectLine(line, Added: false, versionOnly.Contains(line) ? ProjectLineKind.Package : KindOf(line.Text))),
            .. addedCode.Select(line => new ProjectLine(line, Added: true, versionOnly.Contains(line) ? ProjectLineKind.Package : KindOf(line.Text))),
        ];
    }

    /// <summary>
    /// Whether an MSBuild file's edit only re-versions packages or adds package entries: every changed code line is half of a
    /// version-only pair, or an added package line. A removed package line (a package taken out, or swapped for another) is not.
    /// </summary>
    public static bool OnlyVersionsOrAdditions(IReadOnlyList<RoutedLine> added, IReadOnlyList<RoutedLine> removed)
    {
        List<RoutedLine> addedCode = [.. added.Where(IsCode)];
        List<RoutedLine> removedCode = [.. removed.Where(IsCode)];
        HashSet<RoutedLine> versionOnly = VersionOnlyPairs(addedCode, removedCode);
        return addedCode.Count + removedCode.Count > 0
            && removedCode.All(versionOnly.Contains)
            && addedCode.All(line => versionOnly.Contains(line) || KindOf(line.Text) == ProjectLineKind.Package);
    }

    /// <summary>The text with every version value blanked, so two lines that differ only in a version compare equal.</summary>
    public static string WithoutVersions(string text) =>
        VersionElement().Replace(VersionAttribute().Replace(WhitespacePairs.Collapse(text), $"$1=\"{VersionPlaceholder}\""), $"<$1>{VersionPlaceholder}</$1>");

    /// <summary>The removed and added lines that pair up as version-only edits (same text once versions are blanked, different before).</summary>
    public static HashSet<RoutedLine> VersionOnlyPairs(IReadOnlyList<RoutedLine> added, IReadOnlyList<RoutedLine> removed) =>
        Pairs(added, removed, WithoutVersions);

    /// <summary>
    /// Pairs removed with added lines whose <paramref name="key"/> is equal but whose texts differ, in order; both lines of each pair
    /// are returned. An unchanged line moved elsewhere in the file is not a version edit.
    /// </summary>
    public static HashSet<RoutedLine> Pairs(IReadOnlyList<RoutedLine> added, IReadOnlyList<RoutedLine> removed, Func<string, string> key)
    {
        Dictionary<string, Queue<RoutedLine>> removedByKey = new(StringComparer.Ordinal);
        foreach (RoutedLine line in removed)
        {
            string lineKey = key(line.Text);
            if (!removedByKey.TryGetValue(lineKey, out Queue<RoutedLine>? queue))
            {
                queue = new Queue<RoutedLine>();
                removedByKey[lineKey] = queue;
            }

            queue.Enqueue(line);
        }

        HashSet<RoutedLine> paired = [];
        foreach (RoutedLine line in added)
        {
            if (removedByKey.TryGetValue(key(line.Text), out Queue<RoutedLine>? queue) && queue.TryPeek(out RoutedLine? match)
                && WhitespacePairs.Collapse(match.Text) != WhitespacePairs.Collapse(line.Text) && queue.TryDequeue(out _))
            {
                paired.Add(line);
                paired.Add(match);
            }
        }

        return paired;
    }

    /// <summary>
    /// What a line does, judged by everything on it (a line can hold several elements): a policy property anywhere makes it
    /// <see cref="ProjectLineKind.PolicyProperty"/>; else a project reference makes it <see cref="ProjectLineKind.Reference"/>; it is
    /// <see cref="ProjectLineKind.Package"/> only when nothing but package elements and bare group tags is on it.
    /// </summary>
    private static ProjectLineKind KindOf(string text)
    {
        string code = Comment().Replace(text, " ");
        if (PolicyPropertyLine().IsMatch(code) || OutsideCompileLine().IsMatch(code))
        {
            return ProjectLineKind.PolicyProperty;
        }

        if (ReferenceLine().IsMatch(code))
        {
            return ProjectLineKind.Reference;
        }

        string rest = GroupTag().Replace(PackageElement().Replace(VersionElement().Replace(code, " "), " "), " ");
        return PackageLine().IsMatch(code) && rest.Trim().Length == 0 ? ProjectLineKind.Package : ProjectLineKind.Other;
    }

    /// <summary>A line with no code: blank, comments only (each <c>&lt;!-- … --&gt;</c> on it removed), or bare group tags only.</summary>
    private static bool IsCode(RoutedLine line) => GroupTag().Replace(Comment().Replace(line.Text, " "), " ").Trim().Length > 0;

    [GeneratedRegex(@"\b(Version|VersionOverride)\s*=\s*""[^""]*""", RegexOptions.CultureInvariant)]
    private static partial Regex VersionAttribute();

    [GeneratedRegex(@"<(Version|VersionPrefix|VersionSuffix|AssemblyVersion|FileVersion|PackageVersion|InformationalVersion)>[^<]*</\1>", RegexOptions.CultureInvariant)]
    private static partial Regex VersionElement();

    [GeneratedRegex(@"<(PackageReference|PackageVersion)\b", RegexOptions.CultureInvariant)]
    private static partial Regex PackageLine();

    [GeneratedRegex(@"<(?:PackageReference|PackageVersion)\b[^<>]*?/?>|</(?:PackageReference|PackageVersion)>", RegexOptions.CultureInvariant)]
    private static partial Regex PackageElement();

    [GeneratedRegex(@"<ProjectReference\b", RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceLine();

    /// <summary>
    /// Settings that change what the build enforces, compiles or runs: warnings as errors, analyzers and their configuration, the
    /// test-project switch, imports, inline targets and tasks, analyzers given to every project (<c>GlobalPackageReference</c>) and
    /// assets of a package excluded or limited.
    /// </summary>
    [GeneratedRegex(@"<(?:TreatWarningsAsErrors|WarningsAsErrors|CodeAnalysisTreatWarningsAsErrors|RunAnalyzers|RunAnalyzersDuringBuild|EnableNETAnalyzers|AnalysisLevel\w*|AnalysisMode\w*|EnforceCodeStyleInBuild|CodeAnalysisRuleSet|GlobalAnalyzerConfigFiles|EditorConfigFiles|IsTestProject|Import|Target|UsingTask|Exec|Analyzer|GlobalPackageReference)\b|\b(?:ExcludeAssets|IncludeAssets)\s*=|<(?:ExcludeAssets|IncludeAssets)>", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyPropertyLine();

    [GeneratedRegex(@"<Compile\b[^>]*\b(Include|Remove)\s*=\s*(""[^""]*|'[^']*)(\.\.|\$\()", RegexOptions.CultureInvariant)]
    private static partial Regex OutsideCompileLine();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.CultureInvariant)]
    private static partial Regex Comment();

    [GeneratedRegex(@"</?(?:ItemGroup|PropertyGroup)\s*>", RegexOptions.CultureInvariant)]
    private static partial Regex GroupTag();
}
