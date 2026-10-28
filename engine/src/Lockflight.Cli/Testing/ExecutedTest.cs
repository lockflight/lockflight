namespace Lockflight.Cli.Testing;

/// <summary>
/// One executed test case from the runner's xUnit XML (a theory yields one per data row, all with the same <see cref="Key"/>).
/// <see cref="TopFrame"/> is the first stack frame of a failure, e.g. <c>Shop.Api.PriceCalculator.ShippingCost(Decimal weightKg)</c>, and
/// <see cref="TopFrameFile"/> its source file when symbols are available.
/// </summary>
internal sealed record ExecutedTest(string Key, string Name, TestOutcome Outcome, string? ExceptionType, string? TopFrame, string? TopFrameFile = null)
{
    /// <summary>The line of <see cref="TopFrameFile"/> the top frame points at; null without symbols.</summary>
    public int? TopFrameLine { get; init; }
}
