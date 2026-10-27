namespace Shop.Api;

/// <summary>What checkout shows for an order (FR-001, FR-002).</summary>
public sealed record OrderSummary(int ItemCount, decimal Subtotal, string? MostExpensiveSku);
