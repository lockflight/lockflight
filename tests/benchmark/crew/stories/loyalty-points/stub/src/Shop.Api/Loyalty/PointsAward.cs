namespace Shop.Api.Loyalty;

/// <summary>The points a purchase earned and the tier the customer holds afterwards (FR-001 to FR-003).</summary>
public sealed record PointsAward
{
    /// <summary>The customer the points were awarded to.</summary>
    public required CustomerId Customer { get; init; }

    /// <summary>The points this purchase earned.</summary>
    public required int Points { get; init; }

    /// <summary>The tier after the award.</summary>
    public required LoyaltyTier Tier { get; init; }
}
