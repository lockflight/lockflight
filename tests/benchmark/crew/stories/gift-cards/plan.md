# Implementation Plan: Gift cards

## Technical Context

.NET 10, xUnit v3, nullable reference types on, warnings as errors. New code in `src/Shop.Api/GiftCards/`.

- `Money` and `GiftCard` are complete value types, not stubs: `Money` already validates itself (no negative amount, a
  three-letter upper-case currency code).
- `GiftCardRedeemer.Redeem` is the stub this story implements.
- Tests build cards with the existing test helper `tests/Shop.Tests/Support/GiftCardBuilder.cs`.

## Declared signatures

```csharp
namespace Shop.Api.GiftCards;

public sealed record Money { public Money(decimal amount, string currency); public decimal Amount { get; } public string Currency { get; } }

public sealed record GiftCard(string Code, Money Balance, DateOnly ExpiresOn);

public sealed class GiftCardRedeemer
{
    public GiftCard Redeem(GiftCard card, Money amount, DateOnly today);
}
```
