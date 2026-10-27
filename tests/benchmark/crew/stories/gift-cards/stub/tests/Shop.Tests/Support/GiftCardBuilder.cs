using Shop.Api.GiftCards;

namespace Shop.Tests.Support;

/// <summary>Builds gift cards for tests: a 100.00 EUR card that expires on 2099-12-31 unless told otherwise.</summary>
public sealed class GiftCardBuilder
{
    private decimal balance = 100m;
    private string currency = "EUR";

    /// <summary>Sets the card's balance.</summary>
    public GiftCardBuilder WithBalance(decimal amount, string currencyCode = "EUR")
    {
        balance = amount;
        currency = currencyCode;
        return this;
    }

    /// <summary>Sets the card's expiry date.</summary>
    public GiftCardBuilder ExpiringOn(DateOnly date) => throw new NotImplementedException("expiry is not supported by the builder yet");

    /// <summary>The card.</summary>
    public GiftCard Build() => new("GC-TEST-0001", new Money(balance, currency), new DateOnly(2099, 12, 31));
}
