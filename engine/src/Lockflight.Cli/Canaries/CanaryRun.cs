using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Canaries;

/// <summary>What one engine command run inside the canary scratch area returned and printed.</summary>
internal sealed record CanaryRun(ExitCode ExitCode, string Output, string Error)
{
    /// <summary>The first line the command printed to standard error, else to standard output: what the control said.</summary>
    public string FirstLine => (Error.Length > 0 ? Error : Output).Split('\n')[0].Trim();
}
