namespace Lockflight.Cli.Testing;

/// <summary>
/// A project whose evaluation failed or ran out of time, so nobody knows whether it holds tests: its full path, why, and the first
/// line of the evaluation's error (empty for a timeout).
/// </summary>
internal sealed record UnevaluatedProject(string Project, ProjectEvaluationFailure Failure, string Detail);
