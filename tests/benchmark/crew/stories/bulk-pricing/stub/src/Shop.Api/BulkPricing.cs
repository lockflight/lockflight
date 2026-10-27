namespace Shop.Api;

/// <summary>Quantity discounts for bulk orders (FR-001 to FR-004).</summary>
public sealed class BulkPricing
{
    /// <summary>The quote for <paramref name="quantity"/> units at <paramref name="listPrice"/>.</summary>
    public PriceQuote Quote(decimal listPrice, int quantity) => throw new NotImplementedException();
}
