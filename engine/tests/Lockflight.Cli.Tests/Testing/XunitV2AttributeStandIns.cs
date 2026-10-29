// Stand-ins for xUnit v2's attributes, which the metadata reader matches by name in any namespace, so the tests can read an
// xUnit v2-shaped class without the engine's own xUnit v3 running it. Several types in one file on purpose (code style rule 1):
// they are one fixture.
#pragma warning disable CA1822

namespace Lockflight.Cli.Tests.Testing.FrameworkSamples.XunitV2;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class FactAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
internal sealed class TraitAttribute(string name, string value) : Attribute
{
    public string Name { get; } = name;

    public string Value { get; } = value;
}

[Trait("Category", "Integration")]
public class XunitV2Fixture
{
    [Fact]
    [Trait("Requirement", "FR-001")]
    public void Traited()
    {
    }
}
