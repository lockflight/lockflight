using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

/// <summary>Messages as NUnit 4 and 5, MSTest 3 and 4 write them into a real TRX (VSTest and Microsoft.Testing.Platform alike).</summary>
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
    [InlineData("Test method Lending.Tests.LoanTests.Lends threw exception:\r\nSystem.NotImplementedException: The method or operation is not implemented.", "System.NotImplementedException")]
    [InlineData("Test method Lending.Tests.LoanTests.Lends threw exception: \r\nSystem.InvalidOperationException: boom", "System.InvalidOperationException")]
    [InlineData("Assertion failed. Expected values to be equal.\n\nexpected: 2\nactual:   1", TrxFailureMessages.MSTestAssertion)]
    [InlineData("Assertion failed. Expected condition to be true.\n\nactual: false", TrxFailureMessages.MSTestAssertion)]
    [InlineData("Assert.AreEqual failed. Expected:<2>. Actual:<1>. ", TrxFailureMessages.MSTestAssertion)]
    [InlineData("CollectionAssert.AreEqual failed. Element at index 0 do not match.", TrxFailureMessages.MSTestAssertion)]
    [InlineData("StringAssert.Contains failed. String '1' does not contain string 'x'. .", TrxFailureMessages.MSTestAssertion)]
    [InlineData("Assert.Inconclusive failed. unsure", TrxFailureMessages.MSTestInconclusive)]
    [InlineData("Assert.Inconclusive. unsure", TrxFailureMessages.MSTestInconclusive)]
    [InlineData("Assert.Fail failed. nope", null)]
    [InlineData("Assertion failed.\nnope", null)]
    [InlineData("Assertion failed.", null)]
    [InlineData("Initialization method Lending.Tests.LoanTests.Init threw exception. System.Exception: x.", null)]
    public void MSTestFailuresNameTheirExceptionOrAssertion(string message, string? expected) => Assert.Equal(expected, TrxFailureMessages.MSTest(message));

    [Theory]
    [InlineData("System.NotImplementedException : x", "System.NotImplementedException")]
    [InlineData("Assert.Equal() Failure: Values differ", TrxFailureMessages.XunitAssertion)]
    [InlineData("Class fixture type 'App.Tests.Db' threw in its constructor", null)]
    public void XunitV2FailuresNameTheirExceptionOrAssertion(string message, string? expected) => Assert.Equal(expected, TrxFailureMessages.XunitV2(message));
}
