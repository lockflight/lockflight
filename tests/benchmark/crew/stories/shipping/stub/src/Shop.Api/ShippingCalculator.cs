namespace Shop.Api;

/// <summary>Shipping cost of an order (FR-001 to FR-004).</summary>
public sealed class ShippingCalculator
{
    /// <summary>The shipping cost for a parcel of <paramref name="weightKg"/> on an order of <paramref name="subtotal"/>.</summary>
    public decimal Cost(decimal weightKg, decimal subtotal) => throw new NotImplementedException();
}
