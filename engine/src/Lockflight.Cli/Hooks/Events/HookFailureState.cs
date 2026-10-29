namespace Lockflight.Cli.Hooks.Events;

/// <summary>How far the hook got before an engine error, as far as the failure policy depends on it.</summary>
/// <param name="ExpandingSessionCommand">The payload named an in-session human command, which must not reach the agent after an error.</param>
internal sealed record HookFailureState(bool ExpandingSessionCommand);
