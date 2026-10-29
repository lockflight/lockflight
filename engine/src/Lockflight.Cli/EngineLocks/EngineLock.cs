namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// The engine a repository pins (<c>.lockflight/engine.lock</c>, <c>lockflight.engine-lock/v1</c>): the version, and the SHA-256 of
/// the executable per platform and of the vendored install script. The hash is of the executable inside the RID package (what
/// <c>link</c> copies and CI installs), not of the package, which nuget.org re-signs.
/// </summary>
/// <param name="InstallScriptSha256">The hash of <c>.lockflight/ci/install.sh</c>, which CI checks before running it.</param>
/// <param name="Binaries">Released RID → executable hash (64 lower-case hex); a RID this engine does not know is not kept.</param>
internal sealed record EngineLock(string Version, EngineLockChannel Channel, string InstallScriptSha256, IReadOnlyDictionary<string, string> Binaries)
{
    public const string Schema = "lockflight.engine-lock/v1";
    public const string Package = "Lockflight.Cli";

    /// <summary>The committed lock, relative to the project root (protected; restored from the base commit in CI).</summary>
    public const string RelativePath = ".lockflight/engine.lock";

    /// <summary>The git-ignored lock of a development build, which the shim and the hooks read before the committed one; CI never does.</summary>
    public const string LocalRelativePath = ".lockflight/local/engine.lock";

    /// <summary>The executable hash for <paramref name="rid"/>; null when the lock has none for it.</summary>
    public string? HashFor(string rid) => Binaries.TryGetValue(rid, out string? hash) ? hash : null;
}
