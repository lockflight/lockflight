# Implementation Plan: Checkout totals

## Technical Context

.NET 10, xUnit v3, nullable reference types on, warnings as errors. Several small types in `src/Shop.Api/Checkout/`; the
calculator composes the other three.

## Declared signatures

```csharp
namespace Shop.Api.Checkout;

public enum TaxCategory { Standard = 1, Food = 2 }

public sealed record CartItem(string Sku, int Quantity, decimal UnitPrice, TaxCategory Category);

public sealed record CheckoutTotal(decimal Net, decimal Discount, decimal Tax, decimal Shipping, decimal GrandTotal);

public sealed class TaxCalculator { public decimal TaxFor(decimal net, TaxCategory category); }

public sealed class ShippingQuote { public decimal CostFor(decimal subtotal); }

public static class PromoCodes { public static bool TryParse(string code, out int percent); }

public sealed class CheckoutCalculator(TaxCalculator tax, ShippingQuote shipping)
{
    public TaxCalculator Tax { get; }
    public ShippingQuote Shipping { get; }
    public CheckoutTotal Total(IReadOnlyList<CartItem> items, string? promoCode);
}
```
