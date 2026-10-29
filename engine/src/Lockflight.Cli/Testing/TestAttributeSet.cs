namespace Lockflight.Cli.Testing;

/// <summary>
/// What a method's or a class's attributes say about its tests: whether the method is a test, its skip reason (empty for a skip
/// without one), whether a normal run leaves it out (<c>[Explicit]</c>), its <c>Requirement</c> values, and every trait (name and
/// value, categories included) a trait filter is evaluated on.
/// </summary>
internal sealed record TestAttributeSet(
    bool IsTest, string? SkipReason, bool IsExplicit, IReadOnlyList<string> Requirements, IReadOnlyList<KeyValuePair<string, string>> Traits)
{
    public static TestAttributeSet None { get; } = new(false, null, false, [], []);
}
