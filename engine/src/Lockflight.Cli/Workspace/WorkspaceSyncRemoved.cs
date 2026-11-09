using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Workspace;

/// <summary>
/// <c>lockflight workspace sync</c>, which vendored the shell's policy into each unit repository (<c>lockflight.unit.yaml</c>), was
/// removed: a unit points at its shell instead (<c>init --unit-of</c>), and <c>update --project</c> moves a vendored unit there.
/// The command says so in one line and exits as a configuration error, whatever it was given.
/// </summary>
internal sealed class WorkspaceSyncRemoved(CliContext context) : ICommand
{
    public static string Message { get; } =
        $"lockflight: workspace sync was removed; each unit repository points at the shell instead. Run `{EngineCommand.Name} update --project` in a unit repository that holds {Manifest.UnitFileName}, or `{EngineCommand.Name} init --unit-of <shell url> --unit-path <path>` in one that has neither.";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        await context.Error.WriteLineAsync(Message.AsMemory(), cancellationToken);
        return ExitCode.Config;
    }
}
