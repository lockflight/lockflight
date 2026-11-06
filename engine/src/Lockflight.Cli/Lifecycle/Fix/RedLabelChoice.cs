using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Lifecycle.Fix;

/// <summary>The cycle label the next red commit carries, or why there is none: the exit code and the line that says what to do.</summary>
internal sealed record RedLabelChoice(int? Label, ExitCode Refusal, string? Why)
{
    public static RedLabelChoice Chosen(int label) => new(label, ExitCode.Pass, null);

    public static RedLabelChoice Refused(ExitCode exitCode, string why) => new(null, exitCode, why);
}
