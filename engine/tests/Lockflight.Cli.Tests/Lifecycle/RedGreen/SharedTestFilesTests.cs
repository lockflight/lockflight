using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class SharedTestFilesTests : IDisposable
{
    private const string Project = "tests/Shop.Tests";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-shared-test-files-");

    [Fact]
    public void ANewTestInAFileThatExistedAtAnEarlierRedIsReported()
    {
        Write("tests/Shop.Tests/PriceTests.cs", "public sealed class PriceTests\n{\n    [Fact] public void ShippingIsTwice() { }\n    [Fact] public void TaxIsAFifth() { }\n}\n");

        IReadOnlyList<SharedTestFile> shared = SharedTestFiles.Find(
            root.FullName, [Project], [Cycle(1, "ShippingIsTwice", "tests/Shop.Tests/PriceTests.cs")], [New("PriceTests", "TaxIsAFifth")]);

        SharedTestFile file = Assert.Single(shared);
        Assert.Equal("tests/Shop.Tests/PriceTests.cs", file.Path);
        Assert.Equal([1], file.Cycles);
        Assert.Equal(["PriceTests.TaxIsAFifth"], file.NewTests);
    }

    [Fact]
    public void AFileFromBeforeTheFeatureCountsAsWellAsAnEarlierStorysFile()
    {
        Write("tests/Shop.Tests/LegacyTests.cs", "public sealed class LegacyTests\n{\n    [Fact] public void Works() { }\n    [Fact] public void TaxIsAFifth() { }\n}\n");

        IReadOnlyList<SharedTestFile> shared = SharedTestFiles.Find(
            root.FullName, [Project], [Cycle(1, "ShippingIsTwice", "tests/Shop.Tests/PriceTests.cs", "tests/Shop.Tests/LegacyTests.cs")], [New("LegacyTests", "TaxIsAFifth")]);

        Assert.Equal("tests/Shop.Tests/LegacyTests.cs", Assert.Single(shared).Path);
    }

    [Fact]
    public void ANewTestInAFileAddedSinceTheEarlierRedsIsNotReported()
    {
        Write("tests/Shop.Tests/PriceTests.cs", "public sealed class PriceTests\n{\n    [Fact] public void ShippingIsTwice() { }\n}\n");
        Write("tests/Shop.Tests/TaxTests.cs", "public sealed class TaxTests\n{\n    [Fact] public void TaxIsAFifth() { }\n}\n");

        Assert.Empty(SharedTestFiles.Find(root.FullName, [Project], [Cycle(1, "ShippingIsTwice", "tests/Shop.Tests/PriceTests.cs")], [New("TaxTests", "TaxIsAFifth")]));
    }

    [Fact]
    public void ANewTestInANewPartOfAPartialClassIsNotReported()
    {
        Write("tests/Shop.Tests/PriceTests.cs", "public partial class PriceTests\n{\n    [Fact] public void ShippingIsTwice() { }\n}\n");
        Write("tests/Shop.Tests/PriceTests.Tax.cs", "public partial class PriceTests\n{\n    [Fact] public void TaxIsAFifth() { }\n}\n");

        Assert.Empty(SharedTestFiles.Find(root.FullName, [Project], [Cycle(1, "ShippingIsTwice", "tests/Shop.Tests/PriceTests.cs")], [New("PriceTests", "TaxIsAFifth")]));
    }

    [Fact]
    public void ANestedClassIsFoundInItsFile()
    {
        Write("tests/Shop.Tests/PriceTests.cs", "public class PriceTests\n{\n    public sealed class Tax\n    {\n        [Fact] public void IsAFifth() { }\n    }\n}\n");

        IReadOnlyList<SharedTestFile> shared = SharedTestFiles.Find(
            root.FullName, [Project], [Cycle(1, "ShippingIsTwice", "tests/Shop.Tests/PriceTests.cs")], [New("PriceTests+Tax", "IsAFifth")]);

        Assert.Equal("tests/Shop.Tests/PriceTests.cs", Assert.Single(shared).Path);
    }

    [Fact]
    public void EveryEarlierCycleWhoseRedHadTheFileIsNamed()
    {
        Write("tests/Shop.Tests/PriceTests.cs", "public sealed class PriceTests\n{\n    [Fact] public void TaxIsAFifth() { }\n}\n");

        IReadOnlyList<SharedTestFile> shared = SharedTestFiles.Find(
            root.FullName,
            [Project],
            [Cycle(1, "ShippingIsTwice", "tests/Shop.Tests/PriceTests.cs"), Cycle(2, "DiscountIsTen", "tests/Shop.Tests/PriceTests.cs")],
            [New("PriceTests", "TaxIsAFifth")]);

        Assert.Equal([1, 2], Assert.Single(shared).Cycles);
    }

    [Fact]
    public void ACycleRecordedWithoutFileHashesFallsBackToTheFilesThatDeclareItsTests()
    {
        Write("tests/Shop.Tests/PriceTests.cs", "public sealed class PriceTests\n{\n    [Fact] public void ShippingIsTwice() { }\n    [Fact] public void TaxIsAFifth() { }\n}\n");

        IReadOnlyList<SharedTestFile> shared = SharedTestFiles.Find(root.FullName, [Project], [Cycle(1, "ShippingIsTwice")], [New("PriceTests", "TaxIsAFifth")]);

        Assert.Equal([1], Assert.Single(shared).Cycles);
    }

    [Fact]
    public void ATestProjectAddedInThisCycleIsSearched()
    {
        Write("tests/Tax.Tests/TaxTests.cs", "public sealed class TaxTests\n{\n    [Fact] public void IsAFifth() { }\n}\n");

        IReadOnlyList<SharedTestFile> shared = SharedTestFiles.Find(
            root.FullName, [Project, "tests/Tax.Tests"], [Cycle(1, "ShippingIsTwice", "tests/Tax.Tests/TaxTests.cs")], [New("TaxTests", "IsAFifth")]);

        Assert.Single(shared);
    }

    [Fact]
    public void TheWarningSaysWhyAndWhatToDo()
    {
        string warning = SharedTestFiles.Warning([new SharedTestFile("tests/Shop.Tests/PriceTests.cs", [1], ["PriceTests.TaxIsAFifth"])]);

        Assert.Contains("tests/Shop.Tests/PriceTests.cs (cycle 1; new: PriceTests.TaxIsAFifth)", warning, StringComparison.Ordinal);
        Assert.Contains("new test files of their own", warning, StringComparison.Ordinal);
        Assert.Contains("project file", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("reset", warning, StringComparison.Ordinal);
        Assert.Contains("red commit", warning, StringComparison.Ordinal);
        Assert.Contains("NOT-EARNED", warning, StringComparison.Ordinal);
        Assert.Contains("rename", warning, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private static RedGreenCycle Cycle(int number, string method, params string[] filesAtRed) =>
        new(number, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [new RedGreenTest($"Shop.Tests:Shop.Tests.PriceTests.{method}", $"Shop.Tests.PriceTests.{method}", RedClass.RedStub, [])])
        {
            TestFiles = filesAtRed.ToDictionary(file => file, _ => "hash", StringComparer.Ordinal),
        };

    private static TestCase New(string className, string method) => new("Shop.Tests", $"Shop.Tests.{className}", method, [], null);

    private void Write(string relative, string text)
    {
        string path = Path.Combine(root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
