using Orders;

namespace Orders.Tests;

public sealed class OrderTests
{
    [Fact]
    [Trait("Requirement", "FR-001")]
    public void TotalIsTheSumOfQuantityTimesUnitPrice()
    {
        Order order = new();
        order.Add(new OrderLine("A", 2, 10m));
        order.Add(new OrderLine("B", 1, 5m));

        Assert.Equal(25m, order.Total);
    }

    [Fact]
    [Trait("Requirement", "FR-002")]
    public void AnOrderWithoutLinesTotalsZero() => Assert.Equal(0m, new Order().Total);
}
