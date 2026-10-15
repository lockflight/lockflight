namespace Lockflight.Cli.Hosting;

/// <summary>
/// How to run this engine, for the commands that messages name. In a project that is set up, the agent and the person both run
/// the linked engine (<c>.lockflight/bin/lockflight</c> from the project root): it is the binary the hooks run, it exists for
/// every install mode, and it needs neither a package feed nor a particular SDK. Otherwise the command follows where this process
/// runs from: the user-level install of <c>scripts/dev-install.sh</c> (<c>~/.lockflight/bin/lockflight</c>), a project's linked
/// engine, or else the project's tool pin (<c>dotnet lockflight</c>).
/// </summary>
internal static class EngineCommand
{
    public const string Tool = "dotnet lockflight";

    public const string UserInstall = "~/.lockflight/bin/lockflight";

    /// <summary>The project's linked engine, relative to the project root.</summary>
    public const string Linked = ".lockflight/bin/lockflight";

    private const string BinaryName = "lockflight";

    public static string Name { get; } = For(Environment.ProcessPath, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static bool IsUserInstall => Name == UserInstall;

    /// <summary>The command for an engine running from <paramref name="processPath"/>, given the user's home.</summary>
    public static string For(string? processPath, string home)
    {
        if (processPath is null)
        {
            return Tool;
        }

        string fullPath = Path.GetFullPath(processPath);
        if (home.Length > 0 && fullPath.StartsWith(Path.Combine(home, ".lockflight") + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return UserInstall;
        }

        string? binDirectory = Path.GetDirectoryName(fullPath);
        bool linked = Path.GetFileNameWithoutExtension(fullPath) == BinaryName
            && Path.GetFileName(binDirectory) == "bin"
            && Path.GetFileName(Path.GetDirectoryName(binDirectory)) == ".lockflight";
        return linked ? Linked : Tool;
    }
}
