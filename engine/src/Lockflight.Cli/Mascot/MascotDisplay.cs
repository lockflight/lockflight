using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Mascot;

/// <summary>
/// When Levi is drawn: only for a person reading a terminal. Never when the output is piped or read by an agent (a Claude Code
/// shell sets <c>CLAUDECODE</c>), where the art would cost the model tokens; never with <c>NO_COLOR</c> or
/// <c>LOCKFLIGHT_MASCOT=0</c>.
/// </summary>
internal static class MascotDisplay
{
    public static bool ShouldShow(CliContext context) =>
        context.OutputIsTerminal
        && context.Environment.Get(EnvironmentVariables.ClaudeCode) is null
        && context.Environment.Get(EnvironmentVariables.NoColor) is null
        && context.Environment.Get(EnvironmentVariables.Mascot) != "0";

    /// <summary>The pose for a verdict.</summary>
    public static MascotPose For(Verdict verdict) => verdict switch
    {
        Verdict.Pass or Verdict.NotApplicable => MascotPose.Pass,
        Verdict.NotEarned => MascotPose.NotEarned,
        _ => MascotPose.Fail,
    };

    /// <summary>Draws the pose with a caption beside its middle line, when <see cref="ShouldShow"/> allows.</summary>
    public static async Task DrawAsync(CliContext context, MascotPose pose, string caption)
    {
        if (!ShouldShow(context))
        {
            return;
        }

        IReadOnlyList<string> lines = MascotArt.Render(pose);
        TextWriter terminal = context.RawOutput ?? context.Output;
        await terminal.WriteLineAsync();
        for (int index = 0; index < lines.Count; index++)
        {
            string text = index == lines.Count / 2 ? $"   {TerminalText.Clean(caption)}" : string.Empty;
            await terminal.WriteLineAsync($"  {lines[index]}{text}");
        }
    }
}
