# Implementation Plan: Coupon codes

## Technical Context

.NET 10, xUnit v3. One new class and one exception type in `src/Shop.Api`.

## Declared signatures

```csharp
public sealed class CouponBook
{
    public decimal Apply(string code, decimal subtotal);
}

public sealed class UnknownCouponException(string code) : Exception
{
    public string Code { get; }
}
```
