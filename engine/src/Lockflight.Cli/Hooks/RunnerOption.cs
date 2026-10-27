namespace Lockflight.Cli.Hooks;

/// <summary>One option a runner was given: its name as spelled (<c>-n</c>, <c>--interval</c>) and its value, if it takes one.</summary>
internal sealed record RunnerOption(string Name, string? Value);
