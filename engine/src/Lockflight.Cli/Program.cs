using Lockflight.Cli.Hosting;

CliContext context = new(Console.In, Console.Out, Console.Error, Environment.CurrentDirectory, TimeProvider.System)
{
    OutputIsTerminal = !Console.IsOutputRedirected,
};
return (int)await CliApplication.RunAsync(args, context, CancellationToken.None);
