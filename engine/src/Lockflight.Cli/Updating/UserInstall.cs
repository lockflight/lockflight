namespace Lockflight.Cli.Updating;

/// <summary>
/// The user-level install under <c>~/.lockflight</c>: the engine link <c>bin/lockflight</c> pointing at
/// <c>tools/&lt;version&gt;/lockflight</c> (a .NET tool path of its own), and, for an install from source, <c>source</c>, the
/// checkout it was built from. <c>scripts/dev-install.sh</c> and <c>lockflight update</c> both keep this layout.
/// </summary>
internal static class UserInstall
{
    /// <summary>The package id as the .NET tool store names its folder (<c>.store/lockflight.cli/&lt;version&gt;</c>).</summary>
    private const string StorePackageFolder = "lockflight.cli";

    private const string StoreFolder = ".store";

    private static string BinaryName => OperatingSystem.IsWindows() ? "lockflight.exe" : "lockflight";

    public static string Directory(string home) => Path.Combine(home, ".lockflight");

    public static string Engine(string home) => Path.Combine(Directory(home), "bin", BinaryName);

    /// <summary>Where the engine of <paramref name="version"/> is installed as a .NET tool path.</summary>
    public static string ToolPath(string home, string version) => Path.Combine(Directory(home), "tools", version);

    /// <summary>The Lockflight checkout the install was built from; null when unknown (an install from a package).</summary>
    public static string? Source(string home)
    {
        string path = Path.Combine(Directory(home), "source");
        string? source = File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        return source is { Length: > 0 } && System.IO.Directory.Exists(source) ? source : null;
    }

    /// <summary>
    /// The installed engine's version; null without a user install. Read from the tool store folder the link finally resolves into
    /// (<c>.store/lockflight.cli/&lt;version&gt;/…</c>), which also covers a package installed straight into <c>bin</c>, else from
    /// the <c>tools/&lt;version&gt;</c> folder the link points into.
    /// </summary>
    public static string? InstalledVersion(string home)
    {
        try
        {
            FileSystemInfo? next = File.ResolveLinkTarget(Engine(home), returnFinalTarget: false);
            if (next is null)
            {
                return null;
            }

            FileSystemInfo? final = File.ResolveLinkTarget(Engine(home), returnFinalTarget: true);
            string[] segments = (final?.FullName ?? next.FullName).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int index = 0; index + 2 < segments.Length; index++)
            {
                if (segments[index] == StoreFolder && segments[index + 1].Equals(StorePackageFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return segments[index + 2];
                }
            }

            return Path.GetFileName(Path.GetDirectoryName(next.FullName));
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Points <c>bin/lockflight</c> at the engine installed for <paramref name="version"/>, replacing the link in one rename so a
    /// running hook never finds it missing. A package installed straight into <c>bin</c> leaves a tool store there, which goes.
    /// </summary>
    public static void PointAt(string home, string version)
    {
        string engine = Engine(home);
        string bin = Path.GetDirectoryName(engine)!;
        System.IO.Directory.CreateDirectory(bin);
        string temporary = engine + ".new";
        File.Delete(temporary);
        File.CreateSymbolicLink(temporary, Path.Combine(ToolPath(home, version), BinaryName));
        File.Move(temporary, engine, overwrite: true);

        string strayStore = Path.Combine(bin, StoreFolder);
        if (System.IO.Directory.Exists(strayStore))
        {
            System.IO.Directory.Delete(strayStore, recursive: true);
        }
    }

    /// <summary>The single package version a fresh tool path holds; null when it holds none or several.</summary>
    public static string? VersionIn(string toolPath)
    {
        string store = Path.Combine(toolPath, StoreFolder, StorePackageFolder);
        string[] versions = System.IO.Directory.Exists(store) ? System.IO.Directory.GetDirectories(store) : [];
        return versions.Length == 1 ? Path.GetFileName(versions[0]) : null;
    }
}
