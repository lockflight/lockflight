using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// One tree built and listed in the judge's worktree: how it ended, the workspace (with its test applications, to run tests in), the
/// tests it lists (null unless <see cref="TreeOutcome.Ok"/>) and, when it did not end well, why (the build's error lines for a
/// failed build).
/// </summary>
internal sealed record TreeListing(string Commit, TreeOutcome Outcome, TestWorkspace Workspace, IReadOnlyList<TestCase>? Inventory, string? Detail);
