using Lockflight.Cli.Config;

namespace Lockflight.Cli.Tests.Hooks;

/// <summary>A project with thousands of package directories, built once for all the guard work tests.</summary>
public sealed class GuardPerformanceTree : IDisposable
{
    public const int PackageCount = 1500;

    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-perf-");

    public GuardPerformanceTree()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n  - secrets/**\n");
        for (int index = 0; index < PackageCount; index++)
        {
            string package = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "node_modules", $"p{index}", "lib")).FullName;
            File.WriteAllText(Path.Combine(package, "index.js"), string.Empty);
        }
    }

    public string Root => projectRoot.FullName;

    public void Dispose() => projectRoot.Delete(recursive: true);
}
