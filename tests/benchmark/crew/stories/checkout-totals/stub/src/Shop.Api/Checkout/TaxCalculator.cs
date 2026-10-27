namespace Shop.Api.Checkout;

/// <summary>Tax on a net amount (FR-001, FR-002).</summary>
public sealed class TaxCalculator
{
    /// <summary>The tax on <paramref name="net"/> in <paramref name="category"/>.</summary>
    public decimal TaxFor(decimal net, TaxCategory category) => throw new NotImplementedException();
}
