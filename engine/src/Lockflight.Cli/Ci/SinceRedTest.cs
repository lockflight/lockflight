using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Ci;

/// <summary>
/// One cycle test compared from its red commit to head: the state, why, the test-side files that changed (for the person who
/// merges), and the class its head version got on the red commit's code when it was re-checked.
/// </summary>
internal sealed record SinceRedTest(string Key, string Cycle, SinceRed State, string Why, IReadOnlyList<string> ChangedFiles, RedClass? Recheck);
