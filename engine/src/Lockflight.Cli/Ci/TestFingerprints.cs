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

    /// <summary>
    /// The assertion count (<see cref="TestFingerprinter.Assertions"/>) of each of <paramref name="tests"/> in a built tree, by key; a
    /// test whose walk cannot be read is left out. Names are not compared, so the repository's assemblies need not be known.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Assertions(TestWorkspace workspace, IReadOnlyList<TestCase> tests)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (TestApp app in workspace.Apps.Where(app => tests.Any(test => test.Assembly == app.AssemblyName)))
        {
            using TestFingerprinter fingerprinter = new(app.AssemblyPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            foreach (TestCase test in tests.Where(test => test.Assembly == app.AssemblyName))
            {
                if (fingerprinter.Assertions(test.Class, test.Method) is int count)
                {
                    counts[test.Key] = count;
                }
            }
        }

        return counts;
    }
}
