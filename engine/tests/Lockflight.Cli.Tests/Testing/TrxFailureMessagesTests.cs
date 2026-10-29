using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

/// <summary>Messages as NUnit 4 and 5 write them into a real TRX (VSTest and Microsoft.Testing.Platform alike).</summary>
public sealed class TrxFailureMessagesTests
{
    [Theory]
    [InlineData("System.NotImplementedException : The method or operation is not implemented.", "System.NotImplementedException")]
    [InlineData("  Assert.That(stock.OnHand(\"A\"), Is.EqualTo(5))\n  Expected: 5\n  But was:  4\n", TrxFailureMessages.NUnitAssertion)]
    [InlineData("  custom\n  Assert.That(x, Is.True)\n  Expected: True\n  But was:  False\n", TrxFailureMessages.NUnitAssertion)]
    [InlineData("Multiple failures or warnings in test:\n  1)   Assert.That(a, Is.EqualTo(2))\n  Expected: 2\n  But was:  1\n", TrxFailureMessages.NUnitMultipleAssertion)]
    [InlineData("nope", null)]
    [InlineData("  Expected: 1\n", null)]
    [InlineData("OneTimeSetUp: System.InvalidOperationException : boom", null)]
    public void NUnitFailuresNameTheirExceptionOrAssertion(string message, string? expected) => Assert.Equal(expected, TrxFailureMessages.NUnit(message));

    [Theory]
    [InlineData("System.NotImplementedException : x", "System.NotImplementedException")]
    [InlineData("Assert.Equal() Failure: Values differ", TrxFailureMessages.XunitAssertion)]
    [InlineData("Class fixture type 'App.Tests.Db' threw in its constructor", null)]
    public void XunitV2FailuresNameTheirExceptionOrAssertion(string message, string? expected) => Assert.Equal(expected, TrxFailureMessages.XunitV2(message));
}
