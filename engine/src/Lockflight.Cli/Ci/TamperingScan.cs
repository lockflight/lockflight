using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The host-tampering findings of a built tree (<see cref="HostTamperingScanner"/>): its test applications and the repository's
/// shared test-helper assemblies next to them (any assembly of a project under the tree that is not production code).
/// </summary>
internal static class TamperingScan
{
    public static IReadOnlyList<TamperingFinding> Compute(TestWorkspace workspace, string root)
    {
        IReadOnlySet<string> production = ProductionProjects.Find(root, workspace.Apps).AssemblyNames;
        HashSet<string> projects = new(Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        HashSet<string> scanned = new(StringComparer.OrdinalIgnoreCase);
        List<TamperingFinding> findings = [];
        foreach (TestApp app in workspace.Apps)
        {
            string output = Path.GetDirectoryName(app.AssemblyPath)!;
            IEnumerable<string> helpers = projects.Where(name => !production.Contains(name) && name != app.AssemblyName)
                .Select(name => Path.Combine(output, name + ".dll")).Where(File.Exists);
            foreach (string assembly in helpers.Prepend(app.AssemblyPath).Where(path => scanned.Add(Path.GetFileName(path))))
            {
                findings.AddRange(HostTamperingScanner.Scan(assembly));
            }
        }

        return findings;
    }
}
