using Shop.Api;

namespace Shop.Tests;

public sealed class PriceCalculatorTests
{
    private readonly PriceCalculator calculator = new();

    [Fact]
    [Trait("Requirement", "FR-001")]
    public void LineTotal_MultipliesUnitPriceByQuantity() =>
        Assert.Equal(25.50m, calculator.LineTotal(8.50m, 3));

    [Fact]
    [Trait("Requirement", "FR-001")]
    public void LineTotal_RoundsMidpointToEven() =>
        Assert.Equal(0.12m, calculator.LineTotal(0.0625m, 2));

    [Fact]
    [Trait("Requirement", "FR-002")]
    public void ApplyDiscount_ReducesByPercent() =>
        Assert.Equal(90m, calculator.ApplyDiscount(100m, 10m));

    [Fact]
    [Trait("Requirement", "FR-002")]
    public void ApplyDiscount_RejectsMoreThanHundredPercent() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.ApplyDiscount(100m, 101m));
}
