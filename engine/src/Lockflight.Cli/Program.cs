using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes.Survivors;

CliContext context = new(Console.In, Console.Out, Console.Error, Environment.CurrentDirectory, TimeProvider.System)
{
    OutputIsTerminal = !Console.IsOutputRedirected,
    ProcessScope = ProcessScopes.Begin,
};
return (int)await CliApplication.RunAsync(args, context, CancellationToken.None);
