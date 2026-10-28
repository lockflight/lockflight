namespace Lockflight.Cli.Tests.Hooks;

/// <summary>The plugin's hook shim in this checkout, found above the test binaries.</summary>
internal static class ShimLocation
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = System.IO.Path.Combine(directory.FullName, "plugins", "lockflight", "bin", "lockflight-hook");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("plugins/lockflight/bin/lockflight-hook was not found above the test binaries");
    }
}
