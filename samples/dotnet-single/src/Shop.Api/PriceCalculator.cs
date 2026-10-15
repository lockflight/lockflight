namespace Shop.Api;

/// <summary>Order pricing rules (sample domain for Lockflight experiments).</summary>
public sealed class PriceCalculator
{
    /// <summary>Line total: unit price × quantity, rounded to cents (banker's rounding).</summary>
    public decimal LineTotal(decimal unitPrice, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unitPrice);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        return Math.Round(unitPrice * quantity, 2, MidpointRounding.ToEven);
    }

    /// <summary>Applies a percentage discount (0–100) to an amount.</summary>
    public decimal ApplyDiscount(decimal amount, decimal percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100m);
        return Math.Round(amount * (100m - percent) / 100m, 2, MidpointRounding.ToEven);
    }
}
