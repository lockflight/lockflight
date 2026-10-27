# Implementation Plan: Loyalty points

## Technical Context

.NET 10, xUnit v3, nullable reference types on, warnings as errors. A new namespace `Shop.Api.Loyalty` in `src/Shop.Api/Loyalty/`.
The current time comes from an injected `TimeProvider`; the test project has no time-faking package and none is added.

## Declared signatures

```csharp
namespace Shop.Api.Loyalty;

public enum LoyaltyTier { Bronze = 1, Silver = 2, Gold = 3 }

public readonly record struct CustomerId(Guid Value);

public sealed record PointsAward
{
    public required CustomerId Customer { get; init; }
    public required int Points { get; init; }
    public required LoyaltyTier Tier { get; init; }
}

public sealed class LoyaltyProgram(TimeProvider time)
{
    public TimeProvider Time { get; }
    public ValueTask<PointsAward> AwardAsync(CustomerId customer, decimal purchaseTotal, int pointsBefore, CancellationToken cancellationToken);
    public static LoyaltyTier TierFor(int points);
}
```
