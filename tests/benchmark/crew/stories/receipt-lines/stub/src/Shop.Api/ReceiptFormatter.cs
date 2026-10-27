namespace Shop.Api;

/// <summary>Formats receipt lines for the till (FR-001 to FR-004).</summary>
public sealed class ReceiptFormatter
{
    /// <summary>A 32-character line: <paramref name="description"/> on the left, <paramref name="amount"/> on the right.</summary>
    public string FormatLine(string description, decimal amount) => throw new NotImplementedException();

    /// <summary>The total line of <paramref name="amounts"/>.</summary>
    public string FormatTotal(IReadOnlyList<decimal> amounts) => throw new NotImplementedException();
}
