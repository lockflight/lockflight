namespace Lockflight.Cli.Config;

/// <summary>
/// A loaded <c>lockflight.yaml</c>: the project root it was found in and the rules it declares. <see cref="Units"/> is empty for a
/// single repository, which is then the one implicit unit at the root.
/// </summary>
internal sealed record Manifest(string Root, IReadOnlyList<string> ProtectedGlobs, bool StopGate, string? StopTarget, GateSettings Gates)
{
    public const string FileName = "lockflight.yaml";

    /// <summary>The policy <c>workspace sync</c> vendors into a unit repository for its CI.</summary>
    public const string UnitFileName = "lockflight.unit.yaml";

    public IReadOnlyList<Unit> Units { get; init; } = [];
}
