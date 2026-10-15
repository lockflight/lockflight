namespace Lockflight.Cli.Updating;

/// <summary>
/// The user-level install that <c>scripts/dev-install.sh</c> makes under <c>~/.lockflight</c>: the engine link
/// <c>bin/lockflight</c> pointing at <c>tools/&lt;version&gt;/lockflight</c>, and <c>source</c>, the checkout it was built from.
/// </summary>
internal static class UserInstall
{
    public static string Directory(string home) => Path.Combine(home, ".lockflight");

    public static string Engine(string home) => Path.Combine(Directory(home), "bin", OperatingSystem.IsWindows() ? "lockflight.exe" : "lockflight");

    /// <summary>The Lockflight checkout the install was built from; null when unknown.</summary>
    public static string? Source(string home)
    {
        string path = Path.Combine(Directory(home), "source");
        string? source = File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        return source is { Length: > 0 } && System.IO.Directory.Exists(source) ? source : null;
    }

    /// <summary>The installed engine's version, read from the directory its link points into; null without a user install.</summary>
    public static string? InstalledVersion(string home)
    {
        try
        {
            FileSystemInfo? target = File.ResolveLinkTarget(Engine(home), returnFinalTarget: true);
            return target is null ? null : Path.GetFileName(Path.GetDirectoryName(target.FullName));
        }
        catch (IOException)
        {
            return null;
        }
    }
}
