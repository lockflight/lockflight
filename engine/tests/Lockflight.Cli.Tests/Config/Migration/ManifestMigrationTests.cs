using System.Runtime.CompilerServices;
using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Config.Migration;

namespace Lockflight.Cli.Tests.Config.Migration;

/// <summary>
/// Each case under <c>Cases/</c> holds the <c>before.yaml</c> of a version 1 manifest and the expected outcome: <c>after.yaml</c>
/// when it migrates, else <c>outcome.txt</c> (the status, the message and any snippet). <c>LOCKFLIGHT_UPDATE_GOLDENS=1</c> writes
/// them; the diff is reviewed like code. A migrated manifest migrated again is already current.
/// </summary>
public sealed class ManifestMigrationTests
{
    private const string UpdateVariable = "LOCKFLIGHT_UPDATE_GOLDENS";

    public static TheoryData<string> Cases() =>
        [.. Directory.EnumerateDirectories(CasesDirectory()).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Cases))]
    public void EachCaseMigratesAsRecorded(string name)
    {
        string directory = Path.Combine(CasesDirectory(), name);
        string before = Read(Path.Combine(directory, "before.yaml"));

        ManifestMigrationResult result = ManifestMigration.Migrate(before, "/repo", "/repo/lockflight.yaml");

        if (result.Status == ManifestMigrationStatus.Migrated)
        {
            Verify(Path.Combine(directory, "after.yaml"), result.Text!);
            Assert.Equal(ManifestMigrationStatus.AlreadyCurrent, ManifestMigration.Migrate(result.Text!, "/repo", "/repo/lockflight.yaml").Status);
            Assert.Equal(ManifestLoadStatus.Loaded, new ManifestParser("/repo", "/repo/lockflight.yaml").Parse(result.Text!.TrimStart('﻿')).Status);
        }
        else
        {
            Verify(Path.Combine(directory, "outcome.txt"), $"{result.Status}\n{result.Message}\n{result.Snippet}");
        }
    }

    [Fact]
    public void TheByteOrderMarkAndTheLineEndingsAreKept()
    {
        string before = Read(Path.Combine(CasesDirectory(), "crlf-bom", "before.yaml"));

        string after = ManifestMigration.Migrate(before, "/repo", "/repo/lockflight.yaml").Text!;

        Assert.StartsWith("﻿version: 2\r\n", after, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", after.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMigratedManifestKeepsEverySettingAndReportsTheNewChecks()
    {
        string before = Read(Path.Combine(CasesDirectory(), "hand-written", "before.yaml"));
        ManifestMigrationResult result = ManifestMigration.Migrate(before, "/repo", "/repo/lockflight.yaml");
        Manifest after = new ManifestParser("/repo", "/repo/lockflight.yaml").Parse(result.Text!).Manifest!;

        Assert.Equal((2, 900, GateMode.Fail, GateMode.Report, GateMode.Auto), (after.Version, after.Gates.TimeoutSeconds, after.Gates.Suppressions, after.Gates.DocsReferences, after.Gates.EfPendingModelChanges));
        Assert.Equal(ApprovalCheckMode.Report, after.Approvals.Check);
        Assert.Empty(after.Routing.FilledFromPreset);
        Assert.Equal("opus", after.Review.Model);
        Assert.Equal(
            [
                "gates.docs.references: report (fail: references the change breaks fail the pr profile)",
                "gates.ef.pendingModelChanges: auto (auto: fails when the repository pins dotnet-ef; fail forces it)",
                "approvals.check: report (fail: a missing CODEOWNERS coverage or spec-first violation fails the CI check)",
                "routing: preset default with every value written down (no check judges by it yet)",
            ],
            result.NewChecks);
    }

    private static void Verify(string path, string actual)
    {
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(actual));
            return;
        }

        Assert.True(File.Exists(path), $"{path} does not exist; run the tests with {UpdateVariable}=1 to write it");
        Assert.Equal(Read(path), actual);
    }

    /// <summary>The file's text with a byte-order mark kept as the first character (File.ReadAllText drops it).</summary>
    private static string Read(string path) => Encoding.UTF8.GetString(File.ReadAllBytes(path));

    private static string CasesDirectory([CallerFilePath] string path = "") => Path.Combine(Path.GetDirectoryName(path)!, "Cases");
}
