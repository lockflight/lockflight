namespace Shop.Api;

/// <summary>Thrown when a coupon code is not known (FR-004).</summary>
public sealed class UnknownCouponException(string code) : Exception($"Unknown coupon '{code}'.")
{
    /// <summary>The code that was not recognised.</summary>
    public string Code { get; } = code;
}
