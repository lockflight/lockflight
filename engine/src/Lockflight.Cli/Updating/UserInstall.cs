using Lockflight.Cli.Linking;

namespace Lockflight.Cli.Updating;

/// <summary>
/// The user-level install under <c>~/.lockflight</c>: the engine <c>bin/lockflight</c>, which reaches
/// <c>tools/&lt;version&gt;</c> (a .NET tool path of its own) as <see cref="PlatformLayout"/> says, and, for an install from source,
/// <c>source</c>, the checkout it was built from. <c>scripts/dev-install.sh</c>, <c>scripts/dev-install.ps1</c> and
/// <c>lockflight update</c> all keep this layout.
/// </summary>
internal static class UserInstall
{
    /// <summary>Beside a copied engine (<see cref="EngineLayout.Copy"/>): the version it is a copy of.</summary>
    public const string VersionFileName = "lockflight.version";

    /// <summary>The package id as the .NET tool store names its folder (<c>.store/lockflight.cli/&lt;version&gt;</c>).</summary>
    private const string StorePackageFolder = "lockflight.cli";

    private const string StoreFolder = ".store";

    /// <summary>The engine's file name (<c>.exe</c> on Windows).</summary>
    public static string BinaryName => OperatingSystem.IsWindows() ? "lockflight.exe" : "lockflight";

    /// <summary>How this platform's install reaches its engine: a copy on Windows, a link elsewhere.</summary>
    public static EngineLayout PlatformLayout => OperatingSystem.IsWindows() ? EngineLayout.Copy : EngineLayout.Link;

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
    /// The installed engine's version; null without a user install. For a linked engine, read from the tool store folder the link
    /// finally resolves into (<c>.store/lockflight.cli/&lt;version&gt;/…</c>), else from the <c>tools/&lt;version&gt;</c> folder
    /// the link points into. For a copied engine, read from <see cref="VersionFileName"/> beside it. A package installed straight
    /// into <c>bin</c> is read from the tool store there.
    /// </summary>
    public static string? InstalledVersion(string home)
    {
        string engine = Engine(home);
        try
        {
            if (File.ResolveLinkTarget(engine, returnFinalTarget: false) is FileSystemInfo next)
            {
                return LinkedVersion(engine, next);
            }

            if (!File.Exists(engine))
            {
                return null;
            }

            string recorded = Path.Combine(Path.GetDirectoryName(engine)!, VersionFileName);
            return File.Exists(recorded) && File.ReadAllText(recorded).Trim() is { Length: > 0 } version
                ? version
                : VersionIn(Path.GetDirectoryName(engine)!);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Points <c>bin/lockflight</c> at the engine installed for <paramref name="version"/>, the way this platform does.</summary>
    public static void PointAt(string home, string version) => PointAt(home, version, PlatformLayout);

    /// <summary>
    /// Points <c>bin/lockflight</c> at the engine installed for <paramref name="version"/>: a new link, or a fresh copy of the
    /// native engine with its version recorded, put in place by <see cref="ExecutableReplacement"/>, which also works while the old
    /// engine runs. A package installed straight into <c>bin</c> leaves a tool store there, which goes.
    /// </summary>
    public static void PointAt(string home, string version, EngineLayout layout)
    {
        string engine = Engine(home);
        string bin = Path.GetDirectoryName(engine)!;
        System.IO.Directory.CreateDirectory(bin);
        string temporary = engine + ".new";
        File.Delete(temporary);
        if (layout == EngineLayout.Link)
        {
            File.CreateSymbolicLink(temporary, Path.Combine(ToolPath(home, version), BinaryName));
            ExecutableReplacement.Replace(temporary, engine);
            File.Delete(Path.Combine(bin, VersionFileName));
        }
        else
        {
            File.Copy(NativeEngine(ToolPath(home, version)), temporary);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, LinkCommand.ExecutableMode);
            }

            ExecutableReplacement.Replace(temporary, engine);
            File.WriteAllText(Path.Combine(bin, VersionFileName), version + "\n");
        }

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

    private static string LinkedVersion(string engine, FileSystemInfo next)
    {
        FileSystemInfo? final = File.ResolveLinkTarget(engine, returnFinalTarget: true);
        string[] segments = (final?.FullName ?? next.FullName).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int index = 0; index + 2 < segments.Length; index++)
        {
            if (segments[index] == StoreFolder && segments[index + 1].Equals(StorePackageFolder, StringComparison.OrdinalIgnoreCase))
            {
                return segments[index + 2];
            }
        }

        return Path.GetFileName(Path.GetDirectoryName(next.FullName))!;
    }

    /// <summary>
    /// The native engine in a tool path's store. The launcher beside the store is not one on every platform (on Windows it is a
    /// small program that starts the engine from the store), so the copy is taken from the store.
    /// </summary>
    private static string NativeEngine(string toolPath)
    {
        string store = Path.Combine(toolPath, StoreFolder, StorePackageFolder);
        string[] engines = System.IO.Directory.Exists(store)
            ? System.IO.Directory.GetFiles(store, BinaryName, SearchOption.AllDirectories)
            : [];
        return engines.Length == 1
            ? engines[0]
            : throw new IOException($"expected one {BinaryName} in {store}, found {engines.Length}; reinstall with lockflight update");
    }
}
