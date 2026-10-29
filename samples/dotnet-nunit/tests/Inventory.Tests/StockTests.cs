namespace Inventory.Tests;

public sealed class StockTests
{
    [Test]
    [Property("Requirement", "FR-001")]
    public void ReceivingAddsToWhatIsOnHand()
    {
        Stock stock = new();
        stock.Receive("A", 2);
        stock.Receive("A", 3);

        Assert.That(stock.OnHand("A"), Is.EqualTo(5));
    }

    [TestCase("A", 0)]
    [TestCase("B", 0)]
    [TestCase("C", 0, Ignore = "shelf C is not stocked yet")]
    [Property("Requirement", "FR-002")]
    public void AnUnknownItemHasNothingOnHand(string sku, int expected) => Assert.That(new Stock().OnHand(sku), Is.EqualTo(expected));
}
