namespace Shop.Api.Checkout;

/// <summary>What checkout shows for a cart (FR-006, FR-007).</summary>
public sealed record CheckoutTotal(decimal Net, decimal Discount, decimal Tax, decimal Shipping, decimal GrandTotal);
