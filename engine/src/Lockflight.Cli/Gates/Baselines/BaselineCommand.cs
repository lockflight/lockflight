using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Gates.Baselines;

/// <summary>
/// <c>lockflight baseline build|tests|deps [--path dir]</c>: accept the current build warnings, failing tests or advisories of a
/// brownfield project; <c>lockflight baseline suppressions</c>: accept the suppressions the branch adds, by file and rule. A human decision — the guard blocks this command inside agent sessions, and CI reads baselines from the base branch.
/// </summary>
internal sealed class BaselineCommand(CliContext context) : ICommand
{
    /// <param name="arguments">The descriptor's second word is the kind (<c>baseline build</c>).</param>
    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options
            || !BaselineKindNames.TryParse(arguments.Command.Words[1], out BaselineKind kind))
        {
            return ExitCode.Config;
        }

        string target = Path.GetFullPath(options.Value("--path") ?? ".", context.WorkingDirectory);
        if (await ManifestRequirement.LoadAsync(context, target, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        BaselineRun run = new(context, manifest);
        BaselineCapture capture = await run.CaptureAsync(kind, target, Timeout.InfiniteTimeSpan, underHook: false, cancellationToken);
        if (capture.Entries is not IReadOnlyList<string> entries)
        {
            await context.Error.WriteLineAsync($"lockflight: {capture.Problem}");
            return capture.FailureExit;
        }

        await context.Output.WriteLineAsync(await run.SaveAsync(kind, capture.UnitPath, entries, cancellationToken));
        return ExitCode.Pass;
    }
}
