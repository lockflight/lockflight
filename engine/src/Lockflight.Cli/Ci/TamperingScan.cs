using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The host-tampering findings of a built tree (<see cref="HostTamperingScanner"/>): its test applications and the repository's
/// shared test-helper assemblies next to them (any assembly of a project under the tree that is not production code), read
/// together, with the production assemblies next to them read for what could switch a snapshot variable on.
/// </summary>
internal static class TamperingScan
{
    public static async Task<IReadOnlyList<TamperingFinding>> ComputeAsync(TestWorkspace workspace, string root, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> production = ProductionProjects.Find(root, workspace.Apps).AssemblyNames;
        Dictionary<string, string> projects = new(StringComparer.OrdinalIgnoreCase);
        foreach (string project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            projects.TryAdd(Path.GetFileNameWithoutExtension(project), Path.GetDirectoryName(project)!);
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> scanned = [];
        List<string> context = [];
        foreach (TestApp app in workspace.Apps)
        {
            string output = Path.GetDirectoryName(app.AssemblyPath)!;
            foreach (string assembly in projects.Keys.Where(name => name != app.AssemblyName).Select(name => Path.Combine(output, name + ".dll")).Where(File.Exists)
                .Prepend(app.AssemblyPath).Where(path => seen.Add(Path.GetFileName(path))))
            {
                (production.Contains(Path.GetFileNameWithoutExtension(assembly)) ? context : scanned).Add(assembly);
            }
        }

        Dictionary<string, string> testProjects = new(projects.Where(entry => !production.Contains(entry.Key)), StringComparer.OrdinalIgnoreCase);
        VariableMentions mentions = await VariableMentions.ListAsync(root, testProjects, cancellationToken);
        return HostTamperingScanner.ScanAll(scanned, context, mentions.IsNamedOutside);
    }
}
