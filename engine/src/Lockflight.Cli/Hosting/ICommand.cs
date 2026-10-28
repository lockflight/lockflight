namespace Lockflight.Cli.Hosting;

/// <summary>One engine command, constructed for one invocation from its <see cref="CommandDescriptor"/>.</summary>
internal interface ICommand
{
    Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken);
}
