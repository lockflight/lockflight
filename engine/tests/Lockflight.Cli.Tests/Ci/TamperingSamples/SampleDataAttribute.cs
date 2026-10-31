namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>Carries a string to a sample method the way a data attribute does. Scanned, never run.</summary>
/// <param name="value">The string the attribute holds.</param>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SampleDataAttribute(string value) : Attribute
{
    public string Value => value;
}
