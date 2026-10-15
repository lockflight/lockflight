using System.Text.RegularExpressions;

namespace Lockflight.Cli.Gates.Parsers;

/// <summary>
/// Recognizes failures of the machine rather than the code — a missing runtime or native toolchain, no network for restore, no
/// Docker — so the verdict is ERROR with a fix instead of an unexplained exit code.
/// </summary>
internal static partial class EnvironmentProblems
{
    public static EnvironmentProblem? Recognize(string output)
    {
        if (MissingRuntime().Match(output) is { Success: true } runtime)
        {
            string version = runtime.Groups["version"].Value;
            return new EnvironmentProblem(
                "missing-runtime",
                $"tests need the .NET {version} runtime, which is not installed. Install it, or set gates.test.framework in lockflight.yaml to a target you have (e.g. net10.0).");
        }

        if (output.Contains("Platform linker ('clang' or 'gcc') not found", StringComparison.Ordinal))
        {
            return new EnvironmentProblem("missing-native-toolchain", "a test publishes with Native AOT and needs clang or gcc. Install the toolchain or exclude those tests from the gate.");
        }

        if (output.Contains("NU1301", StringComparison.Ordinal) || output.Contains("Unable to load the service index for source", StringComparison.Ordinal))
        {
            return new EnvironmentProblem("restore-offline", "package restore could not reach a NuGet source. Check network access and nuget.config credentials.");
        }

        if (output.Contains("Docker is either not running", StringComparison.Ordinal) || output.Contains("Cannot connect to the Docker daemon", StringComparison.Ordinal))
        {
            return new EnvironmentProblem("docker-unavailable", "a test needs Docker (Testcontainers) and no Docker daemon is reachable. Start Docker or exclude integration tests from the gate.");
        }

        return null;
    }

    [GeneratedRegex(@"You must install or update \.NET to run this application\.[\s\S]{0,400}?Framework: 'Microsoft\.NETCore\.App', version '(?<version>[0-9.]+)'")]
    private static partial Regex MissingRuntime();
}
