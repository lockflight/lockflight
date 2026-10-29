using Lockflight.Cli.Doctor;

namespace Lockflight.Cli.Tests.Doctor;

public sealed class ManifestSettingsChecksTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-doctor-manifest-");

    [Fact]
    public void AVersionOneManifestHasNoRoutingOrAutonomyLines()
    {
        Write("version: 1\n");

        Assert.Empty(ManifestSettingsChecks.Check(root.FullName));
    }

    [Fact]
    public void WithoutARoutingSectionDoctorSaysTheDefaultPresetAppliesWithNoCriticalPaths()
    {
        Write("version: 2\n");

        DoctorCheck routing = Assert.Single(ManifestSettingsChecks.Check(root.FullName));

        Assert.Equal(new DoctorCheck("routing", CheckStatus.Warn, "no routing section: preset default with no critical paths",
            "add routing: with preset: and its values spelled out (a human edit; the manifest is protected)"), routing);
    }

    [Fact]
    public void RoutingValuesTakenFromThePresetAreNamed()
    {
        Write("version: 2\nrouting:\n  preset: strict\n  floor: behaviour\n");

        DoctorCheck routing = Assert.Single(ManifestSettingsChecks.Check(root.FullName));

        Assert.Equal(CheckStatus.Warn, routing.Status);
        Assert.Equal("preset strict; 13 value(s) not written down, taken from the preset: fix.maxLines, fix.maxFiles, largeLines, structural, coverage.fix and 8 more", routing.Detail);
    }

    [Fact]
    public void ACriticalGlobThatMatchesEverythingIsAWarning()
    {
        Write("version: 2\nrouting:\n  preset: default\n  critical: [\"**\"]\n");

        Assert.Contains("matches every path", Assert.Single(ManifestSettingsChecks.Check(root.FullName)).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ReservedAutonomyIsSaidToHaveNoEffect()
    {
        Write("""
            version: 2
            routing:
              preset: default
              floor: fix
              fix: { maxLines: 20, maxFiles: 2 }
              largeLines: 400
              structural: critical
              coverage: { fix: 80, behaviour: 80, feature: 80, critical: 90 }
              require: { specFrom: feature, reviewFrom: behaviour, reviewKeyFrom: critical, architectureFrom: feature, architectureDeclaredFrom: none }
            autonomy:
              maxClass: fix
            """);

        Assert.Equal(
            [new DoctorCheck("routing", CheckStatus.Pass, "preset default, every value written down", null),
             new DoctorCheck("autonomy", CheckStatus.Pass, "reserved, has no effect in this version", null)],
            ManifestSettingsChecks.Check(root.FullName));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private void Write(string yaml) => File.WriteAllText(Path.Combine(root.FullName, "lockflight.yaml"), yaml);
}
