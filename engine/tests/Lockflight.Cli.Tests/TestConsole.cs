using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests;

/// <summary>
/// Runs the CLI in-process against string streams and an empty environment (unless a test supplies variables), on the system
/// clock unless a test supplies its own.
/// </summary>
internal static class TestConsole
{
    public static Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string[] args, string input, string workingDirectory) =>
        RunAsync(args, input, workingDirectory, new Dictionary<string, string>(), TimeProvider.System);

    public static Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string[] args, string input, string workingDirectory, TimeProvider time) =>
        RunAsync(args, input, workingDirectory, new Dictionary<string, string>(), time);

    public static Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(
        string[] args, string input, string workingDirectory, IReadOnlyDictionary<string, string> environment) =>
        RunAsync(args, input, workingDirectory, environment, TimeProvider.System);

    public static async Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(
        string[] args, string input, string workingDirectory, IReadOnlyDictionary<string, string> environment, TimeProvider time)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(input), output, error, workingDirectory, time)
        {
            Environment = EnvironmentReader.Of(environment),
        };

        ExitCode exitCode = await CliApplication.RunAsync(args, context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString().Trim(), error.ToString().Trim());
    }
}
