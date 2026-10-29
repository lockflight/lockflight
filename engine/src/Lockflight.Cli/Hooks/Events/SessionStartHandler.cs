using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Updating;

namespace Lockflight.Cli.Hooks.Events;

/// <summary>SessionStart: the banner for the agent and, for the person, what to update (<see cref="SessionBanner"/>).</summary>
internal sealed class SessionStartHandler : IHookHandler
{
    public HookEvent Event => HookEvent.SessionStart;

    public HookFailurePolicy OnEngineError(HookFailureState state) => HookFailurePolicy.Allow;

    public bool Claims(HookRequest input) => true;

    public Task<HookReply> DecideAsync(HookInvocation invocation, CancellationToken cancellationToken) =>
        Task.FromResult(new HookReply(
            HookDecision.Allow, SessionBanner.ToHookOutput(invocation.Manifest, invocation.ProjectDirectory, SessionNotice(invocation.Context.Environment))));

    /// <summary>For the person, at session start: what to update, or null when nothing is behind.</summary>
    private static string? SessionNotice(EnvironmentReader environment)
    {
        string[] notices = [.. new[] { PluginNotice(environment), UpdateNotice(environment) }.OfType<string>()];
        return notices.Length == 0 ? null : string.Join(" ", notices);
    }

    /// <summary>The plugin updated from its marketplace past what this project's engine supports.</summary>
    private static string? PluginNotice(EnvironmentReader environment) =>
        PluginHandshake.EngineTooOld(environment, ToolPin.RunningVersion) is string minimum
            ? $"The Lockflight plugin needs engine {minimum} or newer; this project runs {ToolPin.RunningVersion}. {HumanOnlyCommands.Sentence(HumanOnlyCommands.OpenATerminalAndRun($"{EngineCommand.UserInstall} update"))} in the project."
            : null;

    /// <summary>
    /// A newer engine is installed than the one this project runs (after an update in another project). Null when they match or
    /// there is no user install.
    /// </summary>
    private static string? UpdateNotice(EnvironmentReader environment) =>
        environment.HomeDirectory() is string home && UserInstall.InstalledVersion(home) is string installed
        && !ToolPin.Matches(installed, ToolPin.RunningVersion)
            ? $"Lockflight {installed} is installed; this project runs {ToolPin.RunningVersion}. {HumanOnlyCommands.Sentence(HumanOnlyCommands.OpenATerminalAndRun($"{EngineCommand.UserInstall} update"))} in the project."
            : null;
}
