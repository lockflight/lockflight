using Lockflight.Cli.Config;

namespace Lockflight.Cli.Facts;

/// <summary>
/// What <c>lockflight facts</c> reports: deterministic facts read from the manifest, the engine's own tables and the tracked files.
/// No judgement and no advice: the crew's skills read these and give advice.
/// </summary>
/// <param name="Manifest">The loaded manifest; null when the repository has none (Lockflight is not set up).</param>
/// <param name="ProtectedPaths">The effective protected list (built-in plus the manifest's); null without a manifest.</param>
/// <param name="Profiles">The gate profiles with each step's mode under the manifest's settings.</param>
/// <param name="InstructionFiles">The tracked instruction files an agent is given (CLAUDE.md, rules, skills, the constitution).</param>
/// <param name="ArchitectureTests">Tracked C# test files that use ArchUnitNET or NetArchTest.</param>
/// <param name="LedgerFiles">The ledger's monthly files that exist, relative to the project root, oldest first.</param>
internal sealed record ProjectFacts(
    Manifest? Manifest,
    IReadOnlyList<string>? ProtectedPaths,
    IReadOnlyList<GateProfileFact> Profiles,
    IReadOnlyList<string> InstructionFiles,
    IReadOnlyList<AnalyzerSetting> Analyzers,
    IReadOnlyList<BannedSymbolsFile> BannedSymbols,
    IReadOnlyList<MetricThreshold> MetricThresholds,
    IReadOnlyList<string> ArchitectureTests,
    IReadOnlyList<string> HumanOnlyCommands,
    IReadOnlyList<string> LedgerFiles);
