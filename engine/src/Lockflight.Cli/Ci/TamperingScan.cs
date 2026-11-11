using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The host-tampering findings of a built tree (<see cref="HostTamperingScanner"/>): its test applications and the repository's
/// shared test-helper assemblies next to them (any assembly of a project under the tree that is not production code), and what their
/// runs load besides the tests (<see cref="TestHostExtensions"/>). A project's assembly is found under the name it builds
/// (<see cref="ProductionProjects.AssemblyName"/>), so a helper project with its own <c>&lt;AssemblyName&gt;</c> is scanned too.
/// </summary>
internal static class TamperingScan
{
    public static IReadOnlyList<TamperingFinding> Compute(IReadOnlyList<TestApp> apps, string root)
    {
        IReadOnlySet<string> production = ProductionProjects.Find(root, apps).AssemblyNames;
        HashSet<string> projects = new(StringComparer.OrdinalIgnoreCase);
        foreach (string project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            // Both names: the project file's, as before, and the assembly it builds when <AssemblyName> sets another.
            projects.Add(Path.GetFileNameWithoutExtension(project));
            projects.Add(ProductionProjects.AssemblyName(project, File.ReadAllText(project)));
        }

        HashSet<string> scanned = new(StringComparer.OrdinalIgnoreCase);
        List<TamperingFinding> findings = [];
        foreach (TestApp app in apps)
        {
            string output = Path.GetDirectoryName(app.AssemblyPath)!;
            IEnumerable<string> helpers = projects.Where(name => !production.Contains(name) && name != app.AssemblyName)
                .Select(name => Path.Combine(output, name + ".dll")).Where(File.Exists);
            foreach (string assembly in helpers.Prepend(app.AssemblyPath).Where(path => scanned.Add(Path.GetFileName(path))))
            {
                findings.AddRange(HostTamperingScanner.Scan(assembly));
            }
        }

        findings.AddRange(TestHostExtensions.Find(apps, root, projects));
        return findings;
    }
}
