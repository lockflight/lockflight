using Lockflight.Cli.Ci;
using Lockflight.Cli.Testing;
using Lockflight.Cli.Tests.Ci.TamperingSamples;

namespace Lockflight.Cli.Tests.Ci;

public sealed class TamperingScanTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-tampering-scan-");

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    [Fact]
    public void AHelperProjectThatNamesItsOwnAssemblyIsScanned()
    {
        string tests = Directory.CreateDirectory(Path.Combine(root.FullName, "tests", "Shop.Tests")).FullName;
        string support = Directory.CreateDirectory(Path.Combine(root.FullName, "tests", "Shop.Testing")).FullName;
        File.WriteAllText(Path.Combine(tests, "Shop.Tests.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(support, "Shop.Testing.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>Shop.Support</AssemblyName></PropertyGroup><ItemGroup><PackageReference Include=\"xunit.v3.assert\" /></ItemGroup></Project>");
        string output = Directory.CreateDirectory(Path.Combine(tests, "bin", "Debug", "net10.0")).FullName;
        File.Copy(typeof(System.Text.Json.JsonDocument).Assembly.Location, Path.Combine(output, "Shop.Tests.dll"));
        File.Copy(typeof(PlainWriter).Assembly.Location, Path.Combine(output, "Shop.Support.dll"));

        IReadOnlyList<TamperingFinding> findings = TamperingScan.Compute([new TestApp(tests, Path.Combine(output, "Shop.Tests.dll"))], root.FullName);

        Assert.Contains(findings, finding => finding.Location == "Lockflight.Cli.Tests.Ci.TamperingSamples.PlainWriter::Write");
    }
}
