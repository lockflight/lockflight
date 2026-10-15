using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Proposals;

/// <summary>
/// Files whose change alters how the project is built, checked or guarded, rather than what it does. A proposal for one is
/// flagged so the person reads it line by line: a single property there can switch a control off.
/// </summary>
internal static class HighRiskFiles
{
    private const string Build = "MSBuild configuration: it changes how every project builds";
    private const string Analyzers = "analyzer and code-style policy: the rules the build gate enforces";
    private const string ClaudeCode = "Claude Code hooks and settings: they decide what the agent may do";

    private static readonly (string Glob, string Reason)[] Rules =
    [
        ("**/*.props", Build),
        ("**/*.targets", Build),
        ("**/*.*proj", "an MSBuild project file: it can switch analyzers and warnings off"),
        ("**/Directory.Build.rsp", Build),
        ("**/.editorconfig", Analyzers),
        ("**/.globalconfig", Analyzers),
        ("**/*.globalconfig", Analyzers),
        ("**/*.ruleset", Analyzers),
        ("**/stylecop.json", Analyzers),
        ("**/BannedSymbols.txt", Analyzers),
        ("**/.claude/hooks/**", ClaudeCode),
        ("**/.claude/settings.json", ClaudeCode),
        ("**/.claude/settings.local.json", ClaudeCode),
        ("**/.github/workflows/**", "a CI workflow: it decides what is checked before a merge"),
        ("**/NuGet.config", "NuGet configuration: where packages come from"),
        ("**/global.json", "the .NET SDK selection"),
        ("**/lockflight.yaml", "Lockflight's policy: protected paths and gates"),
        ("**/lockflight.unit.yaml", "Lockflight's policy: protected paths and gates"),
        ("**/dotnet-tools.json", "the tool pins, including the Lockflight engine that judges the work"),
    ];

    /// <summary>Why a change to the '/'-separated project-relative path is high risk; null when it is not.</summary>
    public static string? Reason(string relativePath) =>
        Rules.FirstOrDefault(rule => PathRules.Matches(rule.Glob, relativePath)).Reason;
}
