namespace Lockflight.Cli.Config;

/// <summary>
/// Paths protected in every Lockflight project in addition to the manifest's own list. A manifest can add rules but
/// never remove these: the controls that judge the agent must not be editable by it.
/// </summary>
internal static class DefaultProtectedPaths
{
    public static IReadOnlyList<string> Globs { get; } =
    [
        Manifest.FileName,
        Manifest.UnitFileName,
        "dotnet-tools.json",
        ".config/dotnet-tools.json",
        ".lockflight/**",
        ".claude/settings.json",
        ".claude/settings.local.json",
        // The team's own controls: a kept guard hook edited to exit 0 would be a silent bypass.
        ".claude/hooks/**",
        ".claude/agents/**",
        ".claude/skills/**",
        ".claude/commands/**",
        "**/Directory.Build.props",
        "**/Directory.Build.targets",
        "**/Directory.Build.rsp",
        "**/Directory.Packages.props",
        "global.json",
        // Code-style and analyzer policy: the rules the build gate enforces must not be loosened by the agent.
        "**/.editorconfig",
        "**/.globalconfig",
        "**/*.globalconfig",
        "**/*.ruleset",
        "**/stylecop.json",
        "**/BannedSymbols.txt",
        "**/CodeMetricsConfig.txt",
        // Git attributes decide what git diffs show: '*.cs -diff' would hide a change from the review and the gates' diffs.
        "**/.gitattributes",
        "**/NuGet.config",
        "**/nuget.config",
        ".gitleaks.toml",
        ".gitleaksignore",
        ".github/workflows/lockflight-gates.yml",
        // The Lockflight job on the other CI platforms. GitLab and Bitbucket decide whether the job runs at all in one shared
        // file (a job defined there overrides an included one), so that whole file is policy, as Directory.Build.props is.
        ".azure-pipelines/lockflight-gates.yml",
        ".gitlab/ci/lockflight-gates.yml",
        ".gitlab-ci.yml",
        "bitbucket-pipelines.yml",
    ];

    /// <summary>
    /// Root-anchored defaults repeated inside every unit of a workspace, plus the unit's CI workflows: a child
    /// repository's own CI is its only hard gate.
    /// </summary>
    private static IEnumerable<string> PerUnit(string unitPath) =>
        Globs.Where(glob => !glob.StartsWith("**/", StringComparison.Ordinal))
            .Append(".github/workflows/**")
            .Select(glob => $"{unitPath}/{glob}");

    /// <summary>The defaults followed by the manifest's own rules, without duplicates.</summary>
    public static IReadOnlyList<string> With(IEnumerable<string> manifestGlobs) => [.. Globs.Concat(manifestGlobs).Distinct(StringComparer.Ordinal)];

    /// <summary>The defaults, the defaults of every unit, then the manifest's own rules, without duplicates.</summary>
    public static IReadOnlyList<string> For(Manifest manifest) =>
        [.. Globs.Concat(manifest.Units.SelectMany(unit => PerUnit(unit.Path))).Concat(manifest.ProtectedGlobs).Distinct(StringComparer.Ordinal)];
}
