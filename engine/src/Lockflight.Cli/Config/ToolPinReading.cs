namespace Lockflight.Cli.Config;

/// <summary>The engine pin read from a tool manifest: the version when pinned, the problem when unreadable.</summary>
internal sealed record ToolPinReading(ToolPinStatus Status, string? Version, string? Problem)
{
    public static ToolPinReading None { get; } = new(ToolPinStatus.None, null, null);

    public static ToolPinReading Pinned(string version) => new(ToolPinStatus.Pinned, version, null);

    public static ToolPinReading Unreadable(string problem) => new(ToolPinStatus.Unreadable, null, problem);
}
