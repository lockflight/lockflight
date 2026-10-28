namespace Lockflight.Cli.Rules;

/// <summary>The project's own deterministic checks a rule can be matched against, read from tracked files.</summary>
/// <param name="ProtectedGlobs">The effective protected list; null when the project has no manifest, so the guard is not set up.</param>
/// <param name="BannedSymbols">Each <c>BannedSymbols.txt</c> by path with the symbol ids it lists (<c>P:System.DateTime.UtcNow</c>).</param>
/// <param name="Analyzers">The analyzer severities the <c>.editorconfig</c> and <c>.globalconfig</c> files set, by diagnostic id.</param>
/// <param name="ArchitectureTests">Test files that use ArchUnitNET or NetArchTest.</param>
internal sealed record ProjectChecks(
    IReadOnlyList<string>? ProtectedGlobs,
    IReadOnlyDictionary<string, IReadOnlyList<string>> BannedSymbols,
    IReadOnlyDictionary<string, AnalyzerSetting> Analyzers,
    IReadOnlyList<string> ArchitectureTests);
