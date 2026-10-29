using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The fingerprint of every listed test of a built tree (<see cref="TestFingerprinter"/>), by test key; a test without one is left
/// out, which counts as changed wherever fingerprints are compared. The repository's own assemblies, whose version a commit may
/// stamp, are the projects under the tree.
/// </summary>
internal static class TestFingerprints
{
    public static IReadOnlyDictionary<string, string> Compute(TestWorkspace workspace, IReadOnlyList<TestCase> inventory, string root)
    {
        HashSet<string> repository = new(ProductionProjects.Find(root, workspace.Apps).AssemblyNames, StringComparer.OrdinalIgnoreCase);
        repository.UnionWith(workspace.Apps.Select(app => app.AssemblyName));
        repository.UnionWith(Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension).OfType<string>());
        Dictionary<string, string> fingerprints = new(StringComparer.Ordinal);
        foreach (TestApp app in workspace.Apps)
        {
            using TestFingerprinter fingerprinter = new(app.AssemblyPath, repository);
            foreach (TestCase test in inventory.Where(test => test.Assembly == app.AssemblyName))
            {
                if (fingerprinter.Fingerprint(test.Class, test.Method) is string fingerprint)
                {
                    fingerprints[test.Key] = fingerprint;
                }
            }
        }

        return fingerprints;
    }
}
