namespace Lockflight.Cli.Hosting;

/// <summary>
/// How the user runs this engine, for the commands that messages tell them to run: the user-level install of
/// <c>scripts/dev-install.sh</c> (<c>~/.lockflight/bin/lockflight</c>) when this process runs from it, else the project's tool pin
/// (<c>dotnet lockflight</c>).
/// </summary>
internal static class EngineCommand
{
    public const string Tool = "dotnet lockflight";

    public const string UserInstall = "~/.lockflight/bin/lockflight";

    public static string Name { get; } = For(Environment.ProcessPath, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static bool IsUserInstall => Name == UserInstall;

    /// <summary>The command for an engine running from <paramref name="processPath"/>, given the user's home.</summary>
    public static string For(string? processPath, string home) =>
        processPath is not null && home.Length > 0
        && Path.GetFullPath(processPath).StartsWith(Path.Combine(home, ".lockflight") + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? UserInstall
            : Tool;
}
