namespace Lockflight.Cli.Proposals;

/// <summary>The outcome of making one patch: exactly one of <see cref="Patch"/> or <see cref="Error"/> is set.</summary>
internal sealed record PatchBuild(byte[]? Patch, string? Error)
{
    public static PatchBuild Built(byte[] patch) => new(patch, null);

    public static PatchBuild Failed(string error) => new(null, error);
}
