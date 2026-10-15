using Lockflight.Cli.Gates.Parsers;

namespace Lockflight.Cli.Tests.Gates.Parsers;

public sealed class VulnerablePackagesTests
{
    private const string Report = """
        {
          "version": 1,
          "projects": [
            { "path": "/r/Vuln.csproj", "frameworks": [ { "framework": "net10.0",
              "topLevelPackages": [ { "id": "Newtonsoft.Json", "requestedVersion": "12.0.1", "resolvedVersion": "12.0.1",
                "vulnerabilities": [ { "severity": "High", "advisoryurl": "https://github.com/advisories/GHSA-5crp-9r3c-p9vr" } ] } ],
              "transitivePackages": [ { "id": "System.Net.Http", "resolvedVersion": "4.3.0",
                "vulnerabilities": [ { "severity": "Moderate", "advisoryurl": "https://github.com/advisories/GHSA-7jgj-8wvc-jh57" } ] } ] } ] },
            { "path": "/r/Clean.csproj" }
          ]
        }
        """;

    [Fact]
    public void DirectAndTransitiveAdvisoriesAreReadFromTheJsonReport()
    {
        IReadOnlyList<VulnerablePackage> found = VulnerablePackages.Parse(Report);

        Assert.Equal(2, found.Count);
        Assert.True(found[0].IsBlocking);
        Assert.Equal("Newtonsoft.Json@12.0.1|https://github.com/advisories/GHSA-5crp-9r3c-p9vr", found[0].Fingerprint);
        Assert.False(found[1].IsBlocking);
    }

    [Fact]
    public void AReportWithoutAdvisoriesIsEmpty() =>
        Assert.Empty(VulnerablePackages.Parse("""{ "version": 1, "projects": [ { "path": "/r/A.csproj" } ] }"""));
}
