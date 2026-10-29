namespace Lockflight.Cli.Routing;

/// <summary>A path's role on one side of a change, and the project file that owns it there (null when no project does).</summary>
internal sealed record FileRoleAt(FileRole Role, string? Project);
