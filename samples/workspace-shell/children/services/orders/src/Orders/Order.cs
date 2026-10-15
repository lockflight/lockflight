namespace Orders;

public sealed class Order
{
    private readonly List<OrderLine> lines = [];

    public IReadOnlyList<OrderLine> Lines => lines;

    public decimal Total => lines.Sum(line => line.Quantity * line.UnitPrice);

    public void Add(OrderLine line) => lines.Add(line);
}
