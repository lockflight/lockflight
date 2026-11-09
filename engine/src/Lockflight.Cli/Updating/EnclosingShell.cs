namespace Lockflight.Cli.Updating;

/// <summary>
/// The workspace shell a unit repository is checked out in: its directory, the unit's path among its units, and whether its
/// <c>lockflight.yaml</c> is still a version 1 manifest, which this engine reads only to migrate it.
/// </summary>
internal sealed record EnclosingShell(string Directory, string UnitPath, bool Version1);
