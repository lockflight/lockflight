using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class ReproductionMethodTests
{
    private const string Helper = "private static void Arrange(decimal price) { }\n\n";

    [Fact]
    public void AnXunitFactAfterAHelperIsTheTarget() =>
        Assert.Equal("TotalAppliesTheDiscount", Reproductions.MethodName(Helper + "[Fact]\npublic void TotalAppliesTheDiscount() { Arrange(1m); }"));

    [Fact]
    public void AnXunitTheoryWithInlineDataAfterAHelperIsTheTarget() =>
        Assert.Equal(
            "TotalAppliesTheDiscount",
            Reproductions.MethodName(Helper + "[Theory]\n[InlineData(100, 10)]\n[InlineData(50, 5)]\npublic void TotalAppliesTheDiscount(decimal price, decimal discount) { }"));

    [Fact]
    public void ATestWhoseDataHoldsAnArrayIsTheTarget()
    {
        Assert.Equal("TotalAddsTheLines", Reproductions.MethodName(Helper + "[TestCase(new[] { 1, 2 }, 3)]\npublic void TotalAddsTheLines(int[] lines, int total) { }"));
        Assert.Equal("TotalAddsTheLines", Reproductions.MethodName(Helper + "[Theory, InlineData(new int[] { 1 }, 1)]\npublic void TotalAddsTheLines(int[] lines, int total) { }"));
        Assert.Equal("TotalAddsTheLines", Reproductions.MethodName(Helper + "[Theory]\n[InlineData(new int[] { 1 }, 1)]\npublic void TotalAddsTheLines(int[] lines, int total) { }"));
    }

    [Fact]
    public void AnNUnitTestAfterAHelperIsTheTarget() =>
        Assert.Equal("TotalAppliesTheDiscount", Reproductions.MethodName(Helper + "[Test]\npublic void TotalAppliesTheDiscount() { }"));

    [Fact]
    public void AnNUnitTestCaseAfterAHelperIsTheTarget() =>
        Assert.Equal("TotalAppliesTheDiscount", Reproductions.MethodName(Helper + "[TestCase(100, 10)]\npublic void TotalAppliesTheDiscount(int price, int discount) { }"));

    [Fact]
    public void AnMSTestTestMethodAfterAHelperIsTheTarget() =>
        Assert.Equal("TotalAppliesTheDiscount", Reproductions.MethodName(Helper + "[TestMethod]\npublic void TotalAppliesTheDiscount() { }"));

    [Fact]
    public void AnMSTestDataTestMethodWithDataRowsAfterAHelperIsTheTarget() =>
        Assert.Equal("TotalAppliesTheDiscount", Reproductions.MethodName(Helper + "[DataTestMethod]\n[DataRow(100, 10)]\npublic void TotalAppliesTheDiscount(int price, int discount) { }"));

    [Fact]
    public void AnAsyncTestAfterAnAsyncHelperIsTheTarget() =>
        Assert.Equal(
            "TotalIsSaved",
            Reproductions.MethodName("private static async Task SeedAsync() => await Task.Yield();\n\n[Fact]\npublic async Task TotalIsSaved() { await SeedAsync(); }"));

    [Fact]
    public void AValueTaskTestIsTheTarget() =>
        Assert.Equal("TotalIsSaved", Reproductions.MethodName(Helper + "[Fact]\npublic async ValueTask TotalIsSaved() { await Task.Yield(); }"));

    [Fact]
    public void ATestAttributeListedWithOthersOrQualifiedIsRecognised()
    {
        Assert.Equal("Listed", Reproductions.MethodName(Helper + "[Trait(\"Category\", \"Review\"), Fact(Skip = \"no\")]\npublic void Listed() { }"));
        Assert.Equal("Qualified", Reproductions.MethodName(Helper + "[Xunit.FactAttribute]\npublic void Qualified() { }"));
    }

    [Fact]
    public void AHelperWithAnotherAttributeIsNotTheTarget() =>
        Assert.Equal(
            "TotalAppliesTheDiscount",
            Reproductions.MethodName("[MethodImpl(MethodImplOptions.NoInlining)]\nprivate static void Arrange() { }\n\n[Fact]\npublic void TotalAppliesTheDiscount() { }"));

    [Fact]
    public void ATestAttributeInACommentOrAStringDoesNotMakeAHelperTheTarget() =>
        Assert.Equal(
            "TotalAppliesTheDiscount",
            Reproductions.MethodName("// [Fact]\nprivate static void Arrange() { }\n\n[Trait(\"Note\", \"[Fact]\")]\nprivate static void Seed() { }\n\n[Fact]\npublic void TotalAppliesTheDiscount() { }"));

    [Fact]
    public void WithoutATestAttributeTheFirstMethodIsTheTarget() =>
        Assert.Equal("Works", Reproductions.MethodName("public void Works() { }\n\nprivate static void Later() { }"));

    [Fact]
    public void WithoutAMethodThereIsNoTarget() =>
        Assert.Null(Reproductions.MethodName("[Fact]\npublic int Total => 1;"));
}
