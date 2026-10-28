using Lockflight.Cli.Config;

namespace Lockflight.Cli.Hosting;

/// <summary><c>lockflight version</c> (or <c>--version</c>): the running engine's version.</summary>
internal sealed class VersionCommand(CliContext context) : ICommand
{
    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        await context.Output.WriteLineAsync(ToolPin.RunningVersion);
        return ExitCode.Pass;
    }
}
