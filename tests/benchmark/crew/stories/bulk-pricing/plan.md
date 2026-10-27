# Implementation Plan: Bulk pricing

## Technical Context

.NET 10, xUnit v3. One new class and one record in `src/Shop.Api`.

## Declared signatures

```csharp
public sealed record PriceQuote(decimal UnitPrice, int DiscountPercent, decimal Total);

public sealed class BulkPricing
{
    public PriceQuote Quote(decimal listPrice, int quantity);
}
```
