using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests;

/// <summary>Runs the CLI in-process against string streams and an empty environment (unless a test supplies variables).</summary>
internal static class TestConsole
{
    public static Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string[] args, string input, string workingDirectory) =>
        RunAsync(args, input, workingDirectory, new Dictionary<string, string>());

    public static async Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(
        string[] args, string input, string workingDirectory, IReadOnlyDictionary<string, string> environment)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(input), output, error, workingDirectory, TimeProvider.System)
        {
            Environment = EnvironmentReader.Of(environment),
        };

        ExitCode exitCode = await CliApplication.RunAsync(args, context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString().Trim(), error.ToString().Trim());
    }
}
