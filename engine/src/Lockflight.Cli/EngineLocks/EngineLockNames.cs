namespace Lockflight.Cli.EngineLocks;

/// <summary>The lock's spellings of <see cref="EngineLockChannel"/>.</summary>
internal static class EngineLockNames
{
    public static string ToName(EngineLockChannel channel) => channel switch
    {
        EngineLockChannel.Release => "release",
        EngineLockChannel.Development => "development",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown lock channel."),
    };

    public static EngineLockChannel? ChannelOf(string? name) => name switch
    {
        "release" => EngineLockChannel.Release,
        "development" => EngineLockChannel.Development,
        _ => null,
    };
}
