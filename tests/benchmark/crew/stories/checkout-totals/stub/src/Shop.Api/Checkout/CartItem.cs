namespace Shop.Api.Checkout;

/// <summary>One item of a cart.</summary>
public sealed record CartItem(string Sku, int Quantity, decimal UnitPrice, TaxCategory Category);
