using Lockflight.Cli.Ci;
using Lockflight.Cli.Tests.Ci.TamperingSamples;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// The host-tampering scan on this test assembly's own samples: a file write into a temporary folder that the method's type, or a
/// type of the assembly the method reads from, made is ordinary; a write with no temporary folder in sight, or one that names a
/// folder of the run, is a finding.
/// </summary>
public sealed class HostTamperingScannerTests
{
    private static readonly string[] Writes = [.. HostTamperingScanner.Scan(typeof(TempFolderOwner).Assembly.Location)
        .Where(finding => finding.Kind == "file-write" && finding.Location.Contains(".TamperingSamples.", StringComparison.Ordinal))
        .Select(finding => finding.Location)
        .Distinct()
        .Order(StringComparer.Ordinal)];

    [Fact]
    public void AWriteIntoTheTypesOwnTemporaryFolderIsNoFinding() =>
        Assert.DoesNotContain(Writes, location => location.EndsWith("TempFolderOwner::WriteNote", StringComparison.Ordinal));

    [Fact]
    public void AWriteIntoAFixturesTemporaryFolderIsNoFinding() =>
        Assert.DoesNotContain(Writes, location => location.Contains("TempFolderUser", StringComparison.Ordinal));

    [Fact]
    public void AWriteWithNoTemporaryFolderInSightIsAFinding()
    {
        Assert.Contains("Lockflight.Cli.Tests.Ci.TamperingSamples.PlainWriter::Write", Writes);
        Assert.Contains("Lockflight.Cli.Tests.Ci.TamperingSamples.TempFolderOwner::WriteResults", Writes);
    }

    [Fact]
    public void ATypeThatAlsoNamesAFolderOfTheRunIsNoTemporaryFolderOwner() =>
        Assert.Contains("Lockflight.Cli.Tests.Ci.TamperingSamples.ElsewhereUser::WriteIntoTheRun", Writes);

    [Fact]
    public void AWriteNamingTheRunnersResultsIsAFindingEvenBesideAFixture() =>
        Assert.Contains("Lockflight.Cli.Tests.Ci.TamperingSamples.ElsewhereUser::WriteResultsBesideTheFixture", Writes);

    [Fact]
    public void AWriteThatNamesAFolderOfTheRunIsAFinding() =>
        Assert.Contains("Lockflight.Cli.Tests.Ci.TamperingSamples.ElsewhereUser::WriteToTheRun", Writes);

    [Fact]
    public void AWriteBesideATestThatReadsAVariableIsNoFinding() =>
        Assert.DoesNotContain(Writes, location => location.Contains("MixedTests", StringComparison.Ordinal));

    [Fact]
    public void AnAsyncWriteInAClassNamedLikeAResultsFormatIsNoFinding() =>
        Assert.DoesNotContain(Writes, location => location.Contains("TrxSampleTests", StringComparison.Ordinal));

    [Fact]
    public void ARelativeWriteBesideATemporaryFolderItDoesNotUseIsAFinding() =>
        Assert.Contains("Lockflight.Cli.Tests.Ci.TamperingSamples.OutputUser::WriteRelative", Writes);

    [Fact]
    public void AWriteIntoAGenericFixturesTemporaryFolderIsNoFinding() =>
        Assert.DoesNotContain(Writes, location => location.Contains("GenericTempFolderUser", StringComparison.Ordinal));

    [Theory]
    [InlineData("WriteThroughTheFixturesResultsPath")]
    [InlineData("WriteThroughAnEnvironmentHelper")]
    [InlineData("WriteThroughAnEnvironmentHelperAfterAskingForTemp")]
    [InlineData("WriteAboveTheFixture")]
    [InlineData("WriteToARootedPath")]
    [InlineData("WriteRootedAfterAskingForTemp")]
    [InlineData("WriteThroughAHelperOfAHelper")]
    [InlineData("WriteThroughAFieldAHelperFilled")]
    [InlineData("WriteAnAttributesPath")]
    [InlineData("WriteThroughAnAsyncHelper")]
    public void AWriteBesideAFixtureWithAPathFromElsewhereIsAFinding(string method) =>
        Assert.Contains($"Lockflight.Cli.Tests.Ci.TamperingSamples.LaunderingUser::{method}", Writes);

    [Fact]
    public void ABaselineEntryAcceptsTheFindingItNamesOrEveryFindingOfItsKindInOneMethod()
    {
        TamperingFinding finding = new("Shop.Tests", "file-write", "Shop.Tests.CliTests::Writes", "System.IO.File.WriteAllText");

        Assert.True(HostTamperingJudge.Accepts(new HashSet<string> { finding.Id }, finding));
        Assert.False(HostTamperingJudge.Accepts(new HashSet<string> { "Shop.Tests|file-write" }, finding));
        Assert.True(HostTamperingJudge.Accepts(new HashSet<string> { "Shop.Tests|file-write|Shop.Tests.CliTests::Writes" }, finding));
        Assert.False(HostTamperingJudge.Accepts(new HashSet<string> { "Shop.Tests" }, finding));
        Assert.False(HostTamperingJudge.Accepts(new HashSet<string> { "Shop.Tests|file" }, finding));
        Assert.False(HostTamperingJudge.Accepts(new HashSet<string> { "Shop.Tests|process-start" }, finding));
    }
}
