namespace Shop.Api.Loyalty;

/// <summary>A customer's loyalty tier, decided by the points they hold (FR-003).</summary>
public enum LoyaltyTier
{
    /// <summary>Below 1000 points.</summary>
    Bronze = 1,

    /// <summary>From 1000 points.</summary>
    Silver = 2,

    /// <summary>From 5000 points.</summary>
    Gold = 3,
}
