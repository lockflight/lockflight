namespace Shop.Api;

/// <summary>Summarizes an order for checkout (FR-001 to FR-004).</summary>
public sealed class OrderSummarizer
{
    /// <summary>The summary of <paramref name="lines"/>.</summary>
    public Task<OrderSummary> SummarizeAsync(IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
