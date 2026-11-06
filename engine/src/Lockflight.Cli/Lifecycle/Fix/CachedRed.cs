using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Lifecycle.Fix;

/// <summary>A red commit as <c>lockflight red</c> judged it before: whether it was accepted, and its new tests (none when it had none).</summary>
internal sealed record CachedRed(bool Accepted, IReadOnlyList<RedGreenTest> Tests);
