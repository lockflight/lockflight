using System.Globalization;
using System.Text;

namespace Lockflight.Cli.Gates;

/// <summary>
/// A cache key written field by field: every name and value is length-prefixed, so a value that contains a separator (<c>=</c>,
/// <c>|</c>, <c>;</c>) cannot make two different settings read the same, and an absent value differs from any present one.
/// </summary>
internal sealed class CanonicalKey
{
    private const string Absent = "-";

    private readonly StringBuilder text = new();

    public CanonicalKey Add(string name, string? value)
    {
        text.Append(CultureInfo.InvariantCulture, $"{name.Length}:{name}=");
        text.Append(value is null ? Absent : $"{value.Length.ToString(CultureInfo.InvariantCulture)}:{value}");
        text.Append(';');
        return this;
    }

    public CanonicalKey Add(string name, int value) => Add(name, value.ToString(CultureInfo.InvariantCulture));

    public CanonicalKey Add(string name, bool value) => Add(name, value ? "true" : "false");

    public override string ToString() => text.ToString();
}
