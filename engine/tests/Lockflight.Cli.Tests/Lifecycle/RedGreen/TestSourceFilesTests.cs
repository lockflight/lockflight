using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class TestSourceFilesTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-test-sources-");

    [Fact]
    public void TheFileThatDeclaresTheClassAndTheMethodIsFound()
    {
        Write("tests/Shop.Tests/ShippingTests.cs", "public sealed class ShippingTests\n{\n    [Fact]\n    public async Task CostsFive()\n    {\n    }\n}\n");
        Write("tests/Shop.Tests/Helpers.cs", "public static class Helpers { public static void Run() => new ShippingTests().CostsFive(); }\n");

        Assert.Equal(["tests/Shop.Tests/ShippingTests.cs"], TestSourceFiles.Find(root.FullName, ["tests/Shop.Tests"], "Shop.Tests.ShippingTests", "CostsFive"));
    }

    [Fact]
    public void ANestedClassIsFoundByItsInnermostName()
    {
        Write("tests/Shop.Tests/PricingTests.cs", "public class PricingTests\n{\n    public sealed class Shipping\n    {\n        [Fact] public void CostsFive() => Assert.True(true);\n    }\n}\n");

        Assert.Equal(["tests/Shop.Tests/PricingTests.cs"], TestSourceFiles.Find(root.FullName, ["tests/Shop.Tests"], "Shop.Tests.PricingTests+Shipping", "CostsFive"));
    }

    [Fact]
    public void ACallIsNotADeclaration()
    {
        Write("tests/Shop.Tests/ShippingTests.cs", "public sealed class ShippingTests\n{\n    public void Other() { return CostsFive(); }\n}\n");

        Assert.Empty(TestSourceFiles.Find(root.FullName, ["tests/Shop.Tests"], "Shop.Tests.ShippingTests", "CostsFive"));
    }

    [Fact]
    public void BuildOutputAndOtherProjectsAreNotSearched()
    {
        Write("tests/Shop.Tests/obj/Generated.cs", "class ShippingTests { void CostsFive() { } }\n");
        Write("src/Shop/ShippingTests.cs", "class ShippingTests { void CostsFive() { } }\n");

        Assert.Empty(TestSourceFiles.Find(root.FullName, ["tests/Shop.Tests"], "ShippingTests", "CostsFive"));
    }

    [Fact]
    public void EveryPartOfAPartialClassThatDeclaresTheMethodIsReturned()
    {
        Write("tests/Shop.Tests/ShippingTests.cs", "public partial class ShippingTests { [Fact] public void CostsFive() { } }\n");
        Write("tests/Shop.Tests/ShippingTests.More.cs", "public partial class ShippingTests { [Fact] public void CostsFive(int weight) { } }\n");
        Write("tests/Shop.Tests/ShippingTests.Other.cs", "public partial class ShippingTests { [Fact] public void CostsSix() { } }\n");

        Assert.Equal(
            ["tests/Shop.Tests/ShippingTests.More.cs", "tests/Shop.Tests/ShippingTests.cs"],
            TestSourceFiles.Find(root.FullName, ["tests/Shop.Tests"], "ShippingTests", "CostsFive"));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private void Write(string relative, string text)
    {
        string path = Path.Combine(root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
