using Lockflight.Cli.Config;

namespace Lockflight.Cli.Workspace;

/// <summary>
/// The workspace shell's policy for one unit repository, read from a clone of the shell at <see cref="Commit"/>: the shell's
/// manifest and this repository's entry among its units.
/// </summary>
/// <param name="Directory">The shell clone the policy was read from.</param>
/// <param name="Commit">The shell commit whose <c>lockflight.yaml</c> was read.</param>
internal sealed record ShellPolicy(string Directory, string Commit, Manifest Manifest, Unit Unit);
