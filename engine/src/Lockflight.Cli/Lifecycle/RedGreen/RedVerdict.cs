namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// How a new test's red was classified and why: <see cref="Why"/> is one short line (where it failed, and the production member the
/// test reaches or that it reaches none); <see cref="Detail"/> adds the exception type, the deciding stack frame and its source line,
/// for <c>redgreen red --verbose</c>.
/// </summary>
internal sealed record RedVerdict(RedClass Class, string Why, string Detail);
