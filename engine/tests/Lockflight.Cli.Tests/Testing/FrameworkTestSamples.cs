using NUnitCategory = NUnit.Framework.CategoryAttribute;
using NUnitExplicit = NUnit.Framework.ExplicitAttribute;
using NUnitIgnore = NUnit.Framework.IgnoreAttribute;
using NUnitProperty = NUnit.Framework.PropertyAttribute;
using NUnitTest = NUnit.Framework.TestAttribute;
using NUnitTestCase = NUnit.Framework.TestCaseAttribute;
using NUnitTestCaseSource = NUnit.Framework.TestCaseSourceAttribute;
using NUnitTheory = NUnit.Framework.TheoryAttribute;
using MSTestClass = Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute;
using MSTestDataRow = Microsoft.VisualStudio.TestTools.UnitTesting.DataRowAttribute;
using MSTestDataTestMethod = Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute;
using MSTestIgnore = Microsoft.VisualStudio.TestTools.UnitTesting.IgnoreAttribute;
using MSTestMethod = Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute;
using MSTestProperty = Microsoft.VisualStudio.TestTools.UnitTesting.TestPropertyAttribute;

// Test classes as NUnit and MSTest would see them, read from this assembly's metadata. Several types in one file on purpose
// (code style rule 1): they are one fixture. Test methods are instance methods, as the frameworks expect.
#pragma warning disable CA1822

namespace Lockflight.Cli.Tests.Testing.FrameworkSamples;

public class NUnitFixture
{
    [NUnitTest]
    [NUnitProperty("Requirement", "FR-001")]
    [NUnitProperty("Owner", "FR-999")]
    public void Plain()
    {
    }

    [NUnitTestCase(1)]
    [NUnitTestCase(2)]
    public void Rows(int value) => _ = value;

    [NUnitTestCaseSource(nameof(Plain))]
    public void FromSource(int value) => _ = value;

    [NUnitTheory]
    public void Theory(int value) => _ = value;

    [NUnitTest]
    [NUnitIgnore("later")]
    public void Ignored()
    {
    }

    [NUnitTest]
    [NUnitExplicit]
    public void ExplicitOnly()
    {
    }

    [NUnitTest]
    [NUnitCategory("FR-002")]
    public void Categorised()
    {
    }

    [NUnitTest]
    internal void NotPublic()
    {
    }

    public void NotATest()
    {
    }

    public class Nested
    {
        [NUnitTest]
        public void Inner()
        {
        }
    }
}

[NUnitIgnore("whole class")]
[NUnitExplicit]
[NUnitProperty("Requirement", "FR-003")]
internal sealed class NUnitInternalFixture
{
    [NUnitTest]
    [NUnitProperty("Requirement", "FR-004")]
    public void Marked()
    {
    }
}

public static class NUnitStaticFixture
{
    [NUnitTest]
    public static void Static()
    {
    }
}

public abstract class NUnitAbstractBase
{
    [NUnitTest]
    public void Inherited()
    {
    }
}

public sealed class NUnitDerived : NUnitAbstractBase;

public class NUnitGenericBase<T>
{
    [NUnitTest]
    public void FromGenericBase() => _ = typeof(T);
}

public sealed class NUnitClosedGeneric : NUnitGenericBase<int>;

[MSTestClass]
public class MSTestFixture
{
    [MSTestMethod]
    [MSTestProperty("Requirement", "FR-001")]
    public void Plain()
    {
    }

    [MSTestDataTestMethod]
    [MSTestDataRow(1)]
    [MSTestDataRow(2)]
    public void Rows(int value) => _ = value;

    [MSTestMethod]
    [MSTestIgnore]
    public void IgnoredWithoutReason()
    {
    }

    [MSTestMethod]
    [MSTestIgnore("later")]
    public void Ignored()
    {
    }

    [MSTestDataRow(1)]
    public void RowsWithoutTestMethod(int value) => _ = value;

    [MSTestClass]
    public class Nested
    {
        [MSTestMethod]
        public void Inner()
        {
        }
    }
}

public class MSTestWithoutTestClass
{
    [MSTestMethod]
    public void NotRun()
    {
    }
}

[MSTestClass]
internal sealed class MSTestInternalClass
{
    [MSTestMethod]
    public void NotRunWithoutDiscoverInternals()
    {
    }
}

[MSTestClass]
[MSTestIgnore("whole class")]
public sealed class MSTestIgnoredClass
{
    [MSTestMethod]
    public void InIgnoredClass()
    {
    }
}
