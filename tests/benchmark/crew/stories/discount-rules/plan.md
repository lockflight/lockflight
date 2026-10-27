# Implementation Plan: Discount rules

## Technical Context

.NET 10, xUnit v3. Changes the existing `PriceCalculator.ApplyDiscount(decimal amount, decimal percent)` in
`src/Shop.Api/PriceCalculator.cs`; no new members, so no stubs. Part of the behaviour already exists.
