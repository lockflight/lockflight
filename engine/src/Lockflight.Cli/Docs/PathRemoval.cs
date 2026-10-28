namespace Lockflight.Cli.Docs;

/// <summary>The newest commit that removed a path: deleted, or renamed to <see cref="RenamedTo"/>.</summary>
internal sealed record PathRemoval(string Commit, string? RenamedTo);
