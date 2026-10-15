namespace Lockflight.Cli.Hooks;

/// <summary>
/// A write target with its variables resolved: <see cref="Path"/> when every variable is known, otherwise
/// <see cref="UnknownSuffix"/>, the literal text after the last unknown variable (<c>secrets/k</c> of <c>$X/secrets/k</c>).
/// </summary>
internal sealed record ShellVariableResolution(string? Path, string? UnknownSuffix);
