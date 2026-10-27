namespace Shop.Api.Loyalty;

/// <summary>Awards loyalty points for purchases (FR-001 to FR-006).</summary>
public sealed class LoyaltyProgram(TimeProvider time)
{
    /// <summary>The clock that decides whether a purchase falls on a weekend.</summary>
    public TimeProvider Time { get; } = time;

    /// <summary>The points <paramref name="purchaseTotal"/> earns for a customer holding <paramref name="pointsBefore"/>.</summary>
    public ValueTask<PointsAward> AwardAsync(CustomerId customer, decimal purchaseTotal, int pointsBefore, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    /// <summary>The tier for a customer holding <paramref name="points"/>.</summary>
    public static LoyaltyTier TierFor(int points) => throw new NotImplementedException();
}
