namespace Lockflight.Cli.Config;

/// <summary>
/// The manifest's <c>shell:</c> section: this repository is a unit of a workspace shell, whose policy its CI reads from
/// <see cref="Url"/>. A repository is either a shell or monorepo (<c>units:</c>) or a unit of a shell, never both.
/// </summary>
/// <param name="Url">The shell's clone URL: <c>https://</c> or SSH, never with credentials.</param>
/// <param name="Ref">The shell's branch; null for its default branch.</param>
/// <param name="Unit">This repository's path among the shell's units.</param>
internal sealed record ShellSettings(string Url, string? Ref, string Unit);
