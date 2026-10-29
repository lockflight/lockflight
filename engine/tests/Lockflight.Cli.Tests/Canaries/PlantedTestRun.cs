namespace Lockflight.Cli.Tests.Canaries;

/// <summary>
/// A test run the test gate is shown without running it: Microsoft.Testing.Platform's exit code and console summary, and the TRX
/// report VSTest would write for the same run (null when VSTest writes none).
/// </summary>
internal sealed record PlantedTestRun(string Case, int PlatformExitCode, string PlatformOutput, int VSTestExitCode, string? Trx);
