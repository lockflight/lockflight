namespace Shop.Api;

/// <summary>A bulk quote: the discounted unit price, the discount tier and the total.</summary>
public sealed record PriceQuote(decimal UnitPrice, int DiscountPercent, decimal Total);
