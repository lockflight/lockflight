namespace Lockflight.Cli.Lifecycle;

/// <summary>The role of a task and the wording that decided it, shown when the test-first order fails.</summary>
internal sealed record TaskClassification(TaskRole Role, string Reason);
