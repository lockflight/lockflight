namespace Inventory;

public sealed class Stock
{
    private readonly Dictionary<string, int> onHand = new(StringComparer.Ordinal);

    public void Receive(string sku, int quantity) => onHand[sku] = OnHand(sku) + quantity;

    public int OnHand(string sku) => onHand.GetValueOrDefault(sku);
}
