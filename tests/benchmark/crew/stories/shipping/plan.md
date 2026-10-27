# Implementation Plan: Shipping cost

## Technical Context

.NET 10, xUnit v3. One new class in `src/Shop.Api`.

## Declared signatures

```csharp
public sealed class ShippingCalculator
{
    public decimal Cost(decimal weightKg, decimal subtotal);
}
```
