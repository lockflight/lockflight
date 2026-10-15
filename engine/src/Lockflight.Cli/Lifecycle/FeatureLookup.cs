namespace Lockflight.Cli.Lifecycle;

/// <summary>The outcome of finding a feature: exactly one of <see cref="Feature"/> or <see cref="Error"/> is set.</summary>
internal sealed record FeatureLookup(Feature? Feature, string? Error)
{
    public bool Exists => Feature is not null;

    public static FeatureLookup Found(Feature feature) => new(feature, null);

    /// <summary>No feature; <paramref name="error"/> says what to pass instead.</summary>
    public static FeatureLookup NotFound(string error) => new(null, error);
}
