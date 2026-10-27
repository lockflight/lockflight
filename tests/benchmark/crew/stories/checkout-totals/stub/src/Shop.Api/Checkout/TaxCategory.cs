namespace Shop.Api.Checkout;

/// <summary>The tax rate a cart item falls under (FR-001, FR-002).</summary>
public enum TaxCategory
{
    /// <summary>The standard rate, 20 percent.</summary>
    Standard = 1,

    /// <summary>The reduced food rate, 5 percent.</summary>
    Food = 2,
}
