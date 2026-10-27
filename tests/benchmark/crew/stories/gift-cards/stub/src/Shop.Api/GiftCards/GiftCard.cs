namespace Shop.Api.GiftCards;

/// <summary>A gift card: its code, the balance left on it and the last day it can be used.</summary>
public sealed record GiftCard(string Code, Money Balance, DateOnly ExpiresOn);
