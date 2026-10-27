namespace Shop.Api;

/// <summary>One line of an order.</summary>
public sealed record OrderLine(string Sku, int Quantity, decimal UnitPrice);
