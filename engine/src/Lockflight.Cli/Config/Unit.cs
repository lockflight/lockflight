namespace Lockflight.Cli.Config;

/// <summary>
/// One entry of the manifest's <c>units:</c> list: a directory with its own build, relative to the manifest root with
/// forward slashes. A unit whose directory holds its own <c>.git</c> is a separate repository of a workspace shell; otherwise it
/// is a unit of a monorepo.
/// </summary>
internal sealed record Unit(string Path, string? Build, UnitKind Kind, string? TestFramework, string? StopFilter);
