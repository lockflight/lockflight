namespace Lockflight.Cli.EngineLocks;

/// <summary>The outcome of a fetch: the <see cref="Content"/> when <see cref="EngineFetchStatus.Fetched"/>, else what went wrong.</summary>
internal sealed record EngineFetch(EngineFetchStatus Status, byte[]? Content, string Message)
{
    public static EngineFetch Failed(EngineFetchStatus status, string message) => new(status, null, message);
}
