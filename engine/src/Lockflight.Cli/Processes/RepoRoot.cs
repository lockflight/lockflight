namespace Lockflight.Cli.Processes;

/// <summary>
/// One git working tree of a project: the project root itself (empty <see cref="Prefix"/>), or a unit that is its own repository,
/// whose paths are reported under its unit path (<c>services/orders/</c>).
/// </summary>
internal sealed record RepoRoot(string Directory, string Prefix);
