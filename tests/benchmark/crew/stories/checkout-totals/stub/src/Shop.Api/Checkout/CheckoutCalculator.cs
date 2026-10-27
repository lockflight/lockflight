namespace Shop.Api.Checkout;

/// <summary>Totals a cart at checkout from tax, shipping and an optional promo code (FR-006 to FR-008).</summary>
public sealed class CheckoutCalculator(TaxCalculator tax, ShippingQuote shipping)
{
    /// <summary>The tax rules this calculator uses.</summary>
    public TaxCalculator Tax { get; } = tax;

    /// <summary>The shipping rules this calculator uses.</summary>
    public ShippingQuote Shipping { get; } = shipping;

    /// <summary>The totals of <paramref name="items"/> with <paramref name="promoCode"/> applied, if any.</summary>
    public CheckoutTotal Total(IReadOnlyList<CartItem> items, string? promoCode) => throw new NotImplementedException();
}
