using Billing;

namespace Billing.Tests;

public sealed class ChargePolicyTests
{
    [Fact]
    public void RetriesBelowTheLimit() => Assert.True(ChargePolicy.ShouldRetry(2));

    [Fact]
    public void StopsAtTheLimit() => Assert.False(ChargePolicy.ShouldRetry(3));
}
