using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Lockflight.Cli.Testing;

/// <summary>
/// xUnit v3's in-process console runner, which a test application references and runs as its entry point. Its options changed
/// between versions: the xUnit XML report is <c>-xml</c> up to 3.x and <c>-result-xml</c> from 4.0 (where <c>-xml</c> is
/// deprecated, to be removed in the next major version), so the version decides the switch.
/// </summary>
internal static class InProcessRunner
{
    private const string RunnerAssembly = "xunit.v3.runner.inproc.console";

    /// <summary>The first runner version whose XML report switch is <c>-result-xml</c>.</summary>
    private static readonly Version ResultSwitches = new(4, 0);

    /// <summary>The runner version the test assembly references, read from its metadata without loading it; null when it references none or cannot be read.</summary>
    public static Version? VersionOf(string assemblyPath)
    {
        try
        {
            using PEReader pe = new(File.OpenRead(assemblyPath));
            MetadataReader reader = pe.GetMetadataReader();
            foreach (AssemblyReferenceHandle handle in reader.AssemblyReferences)
            {
                AssemblyReference reference = reader.GetAssemblyReference(handle);
                if (reader.StringComparer.Equals(reference.Name, RunnerAssembly))
                {
                    return reference.Version;
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The switch that writes the xUnit XML report; an unknown version gets the current one.</summary>
    public static string ResultXmlSwitch(Version? runner) => runner is not null && runner < ResultSwitches ? "-xml" : "-result-xml";
}
