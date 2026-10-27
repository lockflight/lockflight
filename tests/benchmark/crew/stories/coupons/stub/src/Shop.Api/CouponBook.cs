namespace Shop.Api;

/// <summary>The coupon codes a customer can redeem at checkout (FR-001 to FR-004).</summary>
public sealed class CouponBook
{
    /// <summary>The subtotal after applying <paramref name="code"/>.</summary>
    public decimal Apply(string code, decimal subtotal) => throw new NotImplementedException();
}
