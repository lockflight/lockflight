namespace Shop.Api.Checkout;

/// <summary>Parses promo codes (FR-005).</summary>
public static class PromoCodes
{
    /// <summary>Whether <paramref name="code"/> is valid, and the percentage it gives.</summary>
    public static bool TryParse(string code, out int percent) => throw new NotImplementedException();
}
