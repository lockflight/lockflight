using System.Security.Cryptography;

namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// The engine install script (<c>scripts/install.sh</c>) as this engine carries it: <c>init</c> and <c>update</c> write it into a
/// project as <see cref="RelativePath"/>, and its SHA-256 goes into the engine lock, which CI checks the script against before
/// running it. A test keeps the embedded copy equal to the script in the same commit.
/// </summary>
internal static class InstallScript
{
    public const string RelativePath = ".lockflight/ci/install.sh";

    private const string ResourceName = "Lockflight.Cli.install.sh";

    public static byte[] Bytes { get; } = Load();

    public static string Sha256 { get; } = Convert.ToHexStringLower(SHA256.HashData(Bytes));

    private static byte[] Load()
    {
        using Stream stream = typeof(InstallScript).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the engine was built without {ResourceName}");
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
