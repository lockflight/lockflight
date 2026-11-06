namespace Desk.Server;

public sealed class PriceList
{
    private readonly Dictionary<string, decimal> prices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pen"] = 1.50m,
        ["notebook"] = 4.00m,
    };

    public decimal? PriceOf(string item) => prices.TryGetValue(item, out decimal price) ? price : null;
}
