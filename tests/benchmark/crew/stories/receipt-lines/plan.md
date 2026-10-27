# Implementation Plan: Receipt lines

## Technical Context

.NET 10, xUnit v3, nullable reference types on, warnings as errors. One class in `src/Shop.Api`.

## Declared signatures

```csharp
namespace Shop.Api;

public sealed class ReceiptFormatter
{
    public string FormatLine(string description, decimal amount);
    public string FormatLine(string description, decimal amount, int width);
    public string FormatTotal(IReadOnlyList<decimal> amounts);
}
```
