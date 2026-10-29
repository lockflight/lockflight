namespace Lockflight.Cli.Testing;

/// <summary>The test frameworks whose tests are read from a built assembly's metadata, each with its own test attributes.</summary>
internal enum TestFrameworkKind
{
    /// <summary>xUnit v2: <c>[Fact]</c>, <c>[Theory]</c> and attributes derived by name, <c>Skip = "…"</c>, <c>[Trait("Requirement", …)]</c>.</summary>
    XunitV2 = 1,

    /// <summary>NUnit: <c>[Test]</c>, <c>[TestCase]</c>, <c>[TestCaseSource]</c>, <c>[Theory]</c>, <c>[Ignore]</c>, <c>[Explicit]</c>, <c>[Property("Requirement", …)]</c>.</summary>
    NUnit = 2,

    /// <summary>MSTest: <c>[TestMethod]</c> (and <c>[DataTestMethod]</c>) in a <c>[TestClass]</c>, <c>[Ignore]</c>, <c>[TestProperty("Requirement", …)]</c>.</summary>
    MSTest = 3,
}
