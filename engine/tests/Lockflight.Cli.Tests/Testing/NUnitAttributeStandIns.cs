// Stand-ins for the NUnit attributes the metadata reader matches by namespace and name, so the tests can read this assembly
// without referencing NUnit. Several types in one file on purpose (code style rule 1): they are one fixture.

namespace NUnit.Framework;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class TestCaseAttribute(params object[] arguments) : Attribute
{
    public object[] Arguments { get; } = arguments;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class TestCaseSourceAttribute(string sourceName) : Attribute
{
    public string SourceName { get; } = sourceName;
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class TheoryAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
internal sealed class IgnoreAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
internal sealed class ExplicitAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
internal sealed class PropertyAttribute(string name, string value) : Attribute
{
    public string Name { get; } = name;

    public string Value { get; } = value;
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
internal sealed class CategoryAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
