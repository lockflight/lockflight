using Desk.Client.ViewModels;

namespace Desk.Client.Tests;

public class OrderViewModelTests
{
    [Fact]
    public void TheTotalIsQuantityTimesUnitPrice()
    {
        OrderViewModel order = new() { Quantity = 3, UnitPrice = 2.50m };

        Assert.Equal(7.50m, order.Total);
    }

    [Fact]
    public void ChangingTheQuantityRaisesTotalChanged()
    {
        OrderViewModel order = new();
        List<string?> changed = [];
        order.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        order.Quantity = 2;

        Assert.Contains(nameof(OrderViewModel.Total), changed);
    }
}
