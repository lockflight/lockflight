using System.Reflection.Metadata;

namespace Lockflight.Cli.Ci;

/// <summary>
/// What a method body of a scanned test assembly does (<see cref="HostTamperingScanner"/>): the outermost type it belongs to, its
/// own member key, the members it calls (an empty type for one of the assembly itself), the strings it loads and those its
/// attributes hold (an <c>InlineData</c> path reaches the method as an argument), the outermost types of the assembly whose fields
/// it reads or whose methods it calls, and the keys of those fields and methods and of the fields it stores to.
/// </summary>
internal sealed record TamperingBody(
    TypeDefinitionHandle Owner,
    string Key,
    List<(string Type, string Member)> Calls,
    List<string> Strings,
    List<string> AttributeStrings,
    HashSet<TypeDefinitionHandle> Touched,
    HashSet<string> Members,
    HashSet<string> Stores)
{
    /// <summary>For an async or iterator state machine's method, the key of the method it was generated from; null otherwise.</summary>
    public string? StubKey { get; init; }

    /// <summary>Whether the body is a constructor: its stores are judged per <see cref="Segments"/>, and it marks no caller.</summary>
    public bool IsConstructor { get; init; }

    /// <summary>A constructor's field stores, each with the code since the previous store.</summary>
    public List<TamperingSegment> Segments { get; } = [];
}
