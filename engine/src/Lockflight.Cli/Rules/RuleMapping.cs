using System.Text.RegularExpressions;
using Lockflight.Cli.Docs;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Rules;

/// <summary>
/// Maps a rule to what backs it, from the project's configuration and the controls the engine knows, by exact matches only: a ban
/// on editing a path that the effective protected list covers, a ban on running a human-only command, a symbol a
/// <c>BannedSymbols.txt</c> lists, an analyzer id (or the name of the metric a code metrics rule measures) that the analyzer config
/// turns into a build warning, a dependency ban between named layers that an architecture test checks. No keyword guesses and no
/// model: a rule nothing matches exactly is guidance only.
/// </summary>
internal static partial class RuleMapping
{
    private const string AnalyzerCaveat =
        "; the build gate fails a new one (one the build baseline does not list), only in the projects the setting reaches, and a suppression in code or a project file is not blocked";

    private const string ArchitectureCaveat =
        "; the test gate runs it while it is in the solution, and it checks its own rules, not this sentence";

    private const string BannedSymbolsCaveat =
        "; it is a warning, so the build gate fails it only as a new warning against an existing build baseline, only in projects that add the file as AdditionalFiles, and a suppression in a project file is not blocked";

    /// <summary>The metrics the code metrics rules measure, by the name a rule would use for them.</summary>
    private static readonly (string Metric, string Id)[] MetricRules =
    [
        ("cyclomatic complexity", "CA1502"),
        ("maintainability index", "CA1505"),
        ("class coupling", "CA1506"),
    ];

    public static AuditedRule Map(RuleSentence rule, ProjectChecks checks)
    {
        IReadOnlyList<string>? protectedGlobs = checks.ProtectedGlobs;
        string prose = InlineCode.Blanked(rule.Text);
        string[] paths = [.. rule.Spans.Where(IsPathLike)];
        bool bansWrites = WriteBanPattern().IsMatch(prose) && paths.Length > 0;
        string? guarded = bansWrites && protectedGlobs is not null
            ? paths.Select(path => Covering(path, protectedGlobs) is string glob ? $"guard: `{path}` is protected by `{glob}`" : null).FirstOrDefault(found => found is not null)
            : null;
        string? command = RunBanPattern().IsMatch(prose) ? rule.Spans.FirstOrDefault(IsHumanOnly) : null;
        string? refused = command is null ? null : $"human-only refusal: the engine refuses `{command}` inside Claude Code (CLAUDECODE=1; the variable can be unset)";
        string[] checkedBy = [.. new[] { guarded, refused }.OfType<string>()];
        if (checkedBy.Length > 0)
        {
            return Audited(rule, RuleStatus.Checked, string.Join("; ", checkedBy), null);
        }

        if (bansWrites)
        {
            string protectIt = protectedGlobs is null
                ? $"set up Lockflight (`lockflight init`) and add `{paths[0]}` to protected: in lockflight.yaml (the guard then refuses the agent's writes; a guardrail)"
                : $"add `{paths[0]}` to protected: in lockflight.yaml (the guard then refuses the agent's writes; a guardrail)";
            return Audited(rule, RuleStatus.GuidanceOnly, null, protectIt);
        }

        string[] diagnostics = [.. DiagnosticIds(rule.Text)];
        if (diagnostics.Select(id => checks.Analyzers.GetValueOrDefault(id)).FirstOrDefault(setting => setting is { ReachesTheBuild: true }) is AnalyzerSetting analyzer)
        {
            return Audited(rule, RuleStatus.Related, $"analyzer {analyzer.Id} is set to {analyzer.Severity} in {analyzer.File}{AnalyzerCaveat}", null);
        }

        bool bansDependency = DependencyBanPattern().IsMatch(prose);
        if (bansDependency && NamesTwoLayers(rule.Text) && checks.ArchitectureTests.Count > 0)
        {
            return Audited(rule, RuleStatus.Related, $"an architecture test ({checks.ArchitectureTests[0]}) checks dependencies between layers{ArchitectureCaveat}", null);
        }

        string[] apis = [.. rule.Spans.Where(span => ApiPattern().IsMatch(span))];
        foreach (string api in apis)
        {
            string name = api.EndsWith("()", StringComparison.Ordinal) ? api[..^2] : api;
            if (checks.BannedSymbols.FirstOrDefault(file => file.Value.Any(id => id == name || id.EndsWith("." + name, StringComparison.Ordinal) || id.EndsWith(":" + name, StringComparison.Ordinal))) is { Key: string file })
            {
                return Audited(rule, RuleStatus.Related, $"BannedApiAnalyzers (RS0030) lists it in {file}{BannedSymbolsCaveat}", null);
            }
        }

        string? suggestion = diagnostics.Length > 0
            ? $"`dotnet_diagnostic.{diagnostics[0]}.severity = warning` in .editorconfig (`lockflight init --quality` sets CA1502, CA1505 and CA1506), then a build baseline"
            : UseBanPattern().IsMatch(prose) && apis.Length > 0
                ? $"a BannedSymbols.txt entry for `{apis[0]}` with BannedApiAnalyzers (id not verified: write it as T:, M: or P: with the full namespace)"
                : bansDependency
                    ? "an architecture test: `lockflight init --architecture-tests` writes one with ArchUnitNET for Domain, Application, Infrastructure and Api projects (or write a rule: Types().That().ResideInAssembly(…).Should().NotDependOnAny(…))"
                    : null;
        return Audited(rule, RuleStatus.GuidanceOnly, null, suggestion);
    }

    /// <summary>The analyzer ids a rule names (<c>CA1502</c>, <c>IDE0005</c>), and those of the metrics it names, in order.</summary>
    private static IEnumerable<string> DiagnosticIds(string text) =>
        DiagnosticIdPattern().Matches(text).Select(match => match.Value.ToUpperInvariant())
            .Concat(MetricRules.Where(metric => text.Contains(metric.Metric, StringComparison.OrdinalIgnoreCase)).Select(metric => metric.Id))
            .Distinct(StringComparer.Ordinal);

    /// <summary>Whether the rule names two of the layers (in any letter case), so a dependency ban is between layers.</summary>
    private static bool NamesTwoLayers(string text) =>
        LayerNamePattern().Matches(text).Select(match => match.Value.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Count() >= 2;

    private static AuditedRule Audited(RuleSentence rule, RuleStatus status, string? by, string? suggestion) =>
        new(rule.File, rule.Line, rule.Text, status, by, suggestion);

    private static string? Covering(string path, IReadOnlyList<string> globs)
    {
        string trimmed = path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path;
        string probe = trimmed.EndsWith('/') || trimmed.EndsWith("/**", StringComparison.Ordinal) ? trimmed.TrimEnd('*').TrimEnd('/') + "/file" : trimmed;
        return globs.FirstOrDefault(glob => glob == trimmed || PathRules.Matches(glob, probe));
    }

    private static bool IsPathLike(string span) => !span.Contains(' ') && !span.StartsWith('/') && !ApiPattern().IsMatch(span) && (span.Contains('/') || PathExtensionPattern().IsMatch(span));

    private static bool IsHumanOnly(string span)
    {
        string[] words = span.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int start = words is ["lockflight", ..] || (words.Length > 0 && words[0].EndsWith("/lockflight", StringComparison.Ordinal)) ? 1 : words is ["dotnet", "lockflight", ..] ? 2 : 0;
        return words.Length > start && HumanOnlyCommands.IsReserved(words[start..]);
    }

    [GeneratedRegex(@"\b(?:never|do not|don't|must not|may not|cannot|should not|not)\b[^.;]{0,40}?\b(?:edit|edited|edits|write|written|writes|modify|modified|change|changed|touch|delete|deleted|remove|removed|overwrite|overwritten|rewrite)\b|\bread[- ]only\b", RegexOptions.IgnoreCase)]
    private static partial Regex WriteBanPattern();

    [GeneratedRegex(@"\b(?:never|do not|don't|must not|may not|cannot|should not|not)\b[^.;]{0,60}?\b(?:run|runs|ran|execute|executed|invoke|invoked|call|called)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RunBanPattern();

    [GeneratedRegex(@"\b(?:never|do not|don't|must not|may not|should not|avoid|no)\b[^.;]{0,30}?\b(?:use|uses|used|using|call|calls|called)\b|\bbanned\b|\bforbidden\b", RegexOptions.IgnoreCase)]
    private static partial Regex UseBanPattern();

    [GeneratedRegex(@"\b(?:never|do not|don't|must not|may not|cannot|should not|not)\b[^.;]{0,40}?\b(?:depend|depends|reference|references|import|imports)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DependencyBanPattern();

    [GeneratedRegex(@"\b(?:domain|application|infrastructure|api)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LayerNamePattern();

    [GeneratedRegex(@"\b(?:CA|IDE)\d{4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DiagnosticIdPattern();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*(?:\.[A-Z][A-Za-z0-9]*)+(?:\(\))?$")]
    private static partial Regex ApiPattern();

    [GeneratedRegex(@"\.[A-Za-z0-9]{1,10}$")]
    private static partial Regex PathExtensionPattern();
}
