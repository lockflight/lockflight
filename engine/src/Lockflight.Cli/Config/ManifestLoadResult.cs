namespace Lockflight.Cli.Config;

/// <summary>The outcome of loading a manifest: exactly one of <see cref="Manifest"/> or <see cref="Error"/> is set unless nothing was found.</summary>
internal sealed record ManifestLoadResult(ManifestLoadStatus Status, Manifest? Manifest, ManifestError? Error)
{
    public static ManifestLoadResult NotFound { get; } = new(ManifestLoadStatus.NotFound, null, null);

    public static ManifestLoadResult Loaded(Manifest manifest) => new(ManifestLoadStatus.Loaded, manifest, null);

    public static ManifestLoadResult Invalid(ManifestError error) => new(ManifestLoadStatus.Invalid, null, error);
}
