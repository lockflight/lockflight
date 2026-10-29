namespace Lockflight.Cli.EngineLocks;

/// <summary>A lock as read: <see cref="Lock"/>, or the <see cref="Problem"/> that makes it unreadable (a configuration error).</summary>
internal sealed record EngineLockReading(EngineLock? Lock, string? Problem);
