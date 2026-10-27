namespace Shop.Api.Checkout;

/// <summary>Shipping cost of a cart (FR-003, FR-004).</summary>
public sealed class ShippingQuote
{
    /// <summary>The shipping cost for <paramref name="subtotal"/>.</summary>
    public decimal CostFor(decimal subtotal) => throw new NotImplementedException();
}
