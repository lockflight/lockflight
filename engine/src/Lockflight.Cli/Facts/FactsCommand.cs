using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Facts;

/// <summary>
/// <c>lockflight facts [--json]</c>: the project's deterministic facts (<see cref="ProjectFacts"/>). With <c>--json</c>, the
/// versioned <c>lockflight.facts/v1</c> document the crew's skills read; without it, a short summary for a person. Read only, exit 0
/// whatever it finds; no gate reads it.
/// </summary>
internal sealed class FactsCommand(CliContext context) : ICommand
{
    public const string JsonFlag = "--json";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        string directory = ManifestLocator.Discover(context.WorkingDirectory) is { Manifest: Manifest manifest } ? manifest.Root : context.WorkingDirectory;
        if (await FactsCollector.CollectAsync(directory, cancellationToken) is not ProjectFacts facts)
        {
            await context.Error.WriteLineAsync("lockflight facts: not a git repository; the facts are read from the tracked files, so run it inside one");
            return ExitCode.Config;
        }

        if (options.Flag(JsonFlag))
        {
            await context.Output.WriteLineAsync(FactsJson.Render(facts, ToolPin.RunningVersion));
            return ExitCode.Pass;
        }

        TextWriter output = context.Output;
        await output.WriteLineAsync($"facts (engine {ToolPin.RunningVersion}); {JsonFlag} for the full document");
        await output.WriteLineAsync(facts.ProtectedPaths is null ? "manifest: none (run lockflight init)" : $"protected paths: {facts.ProtectedPaths.Count}");
        foreach (GateProfileFact profile in facts.Profiles)
        {
            await output.WriteLineAsync($"gates {profile.Name}: {string.Join(", ", profile.Steps.Select(step => $"{step.Name} ({GateModeNames.ToName(step.Mode)})"))}");
        }

        await output.WriteLineAsync($"instruction files: {facts.InstructionFiles.Count} · analyzer settings: {facts.Analyzers.Count} · banned-symbol files: {facts.BannedSymbols.Count} · code metrics thresholds: {facts.MetricThresholds.Count} · architecture tests: {facts.ArchitectureTests.Count}");
        await output.WriteLineAsync($"ledger: {facts.LedgerFiles.Count} files in .lockflight/ledger/");
        return ExitCode.Pass;
    }
}
