namespace Shop.Api.GiftCards;

/// <summary>Redeems gift cards at checkout (FR-001 to FR-005).</summary>
public sealed class GiftCardRedeemer
{
    /// <summary>The card after <paramref name="amount"/> is taken off its balance on <paramref name="today"/>.</summary>
    public GiftCard Redeem(GiftCard card, Money amount, DateOnly today) => throw new NotImplementedException();
}
