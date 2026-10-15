using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Appends raw hook payloads to the file named by <c>LOCKFLIGHT_HOOK_LOG</c> (for inspecting what Claude Code sends). A log that
/// cannot be written is reported on stderr and skipped: a debugging aid never decides a hook.
/// </summary>
internal sealed class HookPayloadLog(TimeProvider time, EnvironmentReader environment, TextWriter diagnostics)
{
    public async Task AppendAsync(HookEvent hookEvent, string rawInput, CancellationToken cancellationToken)
    {
        string? logPath = environment.Get(EnvironmentVariables.HookLog);
        if (string.IsNullOrEmpty(logPath))
        {
            return;
        }

        string line = $"{time.GetUtcNow():O}\t{HookEventNames.ToName(hookEvent)}\t{rawInput.ReplaceLineEndings(" ")}{Environment.NewLine}";
        try
        {
            await File.AppendAllTextAsync(logPath, line, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await diagnostics.WriteLineAsync($"lockflight: cannot write the hook log {EnvironmentVariables.HookLog}='{logPath}' ({exception.Message}); continuing without it.");
        }
    }
}
