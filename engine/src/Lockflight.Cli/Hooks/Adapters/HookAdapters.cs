using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Hooks.Adapters;

/// <summary>
/// Picks the adapter from <c>LOCKFLIGHT_HOOK_ADAPTER</c>. Unset means Claude Code, so an older plugin, which sets nothing, keeps
/// working. The adapter is never a command-line argument: an older engine dispatches only <c>hook &lt;event&gt;</c> and would
/// refuse an extra one, which the shim treats as a failure on every tool call.
/// </summary>
internal static class HookAdapters
{
    /// <summary>The request for the payload, or why there is none.</summary>
    public static HookAdapterResult Parse(EnvironmentReader environment, string rawInput, HookEvent hookEvent)
    {
        string? adapter = environment.Get(EnvironmentVariables.HookAdapter);
        HookRequest? request;
        switch (string.IsNullOrEmpty(adapter) ? ClaudeCodeAdapter.ClientName : adapter)
        {
            case ClaudeCodeAdapter.ClientName:
                request = ClaudeCodeAdapter.Parse(rawInput, environment.Get(EnvironmentVariables.ClaudeProjectDirectory));
                break;
            case NeutralAdapter.Name:
                request = NeutralAdapter.Parse(rawInput, hookEvent);
                break;
            default:
                return new HookAdapterResult(
                    HookAdapterStatus.UnknownAdapter,
                    Request: null,
                    $"lockflight: {EnvironmentVariables.HookAdapter}='{adapter}' names no hook adapter this engine knows ({ClaudeCodeAdapter.ClientName}, {NeutralAdapter.Name}); unset it for Claude Code");
        }

        return HookAdapterResult.Of(request);
    }
}
