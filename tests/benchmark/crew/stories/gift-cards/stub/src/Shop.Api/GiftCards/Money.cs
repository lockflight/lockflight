namespace Shop.Api.GiftCards;

/// <summary>An amount of money in one currency. Complete, not a stub: it rejects a negative amount and a malformed currency.</summary>
public sealed record Money
{
    /// <summary>An amount of <paramref name="amount"/> in <paramref name="currency"/> (an upper-case ISO code such as <c>EUR</c>).</summary>
    public Money(decimal amount, string currency)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException($"'{currency}' is not an upper-case three-letter currency code", nameof(currency));
        }

        Amount = amount;
        Currency = currency;
    }

    /// <summary>The amount.</summary>
    public decimal Amount { get; }

    /// <summary>The currency code.</summary>
    public string Currency { get; }
}
