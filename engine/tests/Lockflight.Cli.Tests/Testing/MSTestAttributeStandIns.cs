// Stand-ins for the MSTest attributes the metadata reader matches by namespace and name, so the tests can read this assembly
// without referencing MSTest. Several types in one file on purpose (code style rule 1): they are one fixture.

namespace Microsoft.VisualStudio.TestTools.UnitTesting;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class TestClassAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
internal class TestMethodAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class DataTestMethodAttribute : TestMethodAttribute;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class DataRowAttribute(params object[] data) : Attribute
{
    public object[] Data { get; } = data;
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
internal sealed class IgnoreAttribute : Attribute
{
    public IgnoreAttribute()
    {
    }

    public IgnoreAttribute(string message) => Message = message;

    public string? Message { get; }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
internal sealed class TestPropertyAttribute(string name, string value) : Attribute
{
    public string Name { get; } = name;

    public string Value { get; } = value;
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
internal sealed class TestCategoryAttribute(string testCategory) : Attribute
{
    public string TestCategory { get; } = testCategory;
}
