# Implementation Plan: Order summary

## Technical Context

.NET 10, xUnit v3, nullable reference types on, warnings as errors. One service and two records in `src/Shop.Api`.

## Declared signatures

```csharp
public sealed record OrderLine(string Sku, int Quantity, decimal UnitPrice);

public sealed record OrderSummary(int ItemCount, decimal Subtotal, string? MostExpensiveSku);

public sealed class OrderSummarizer
{
    public Task<OrderSummary> SummarizeAsync(IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken);
}
```
