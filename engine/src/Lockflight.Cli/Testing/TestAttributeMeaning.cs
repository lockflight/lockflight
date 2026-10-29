namespace Lockflight.Cli.Testing;

/// <summary>What an attribute says about a test.</summary>
internal enum TestAttributeMeaning
{
    /// <summary>Nothing red/green reads.</summary>
    None = 0,

    /// <summary>The method is a test.</summary>
    Test = 1,

    /// <summary>The test is skipped (ignored), with an optional reason.</summary>
    Skip = 2,

    /// <summary>A normal run leaves the test out.</summary>
    Explicit = 3,

    /// <summary>A name/value trait or property, a requirement when its name is <c>Requirement</c>.</summary>
    NamedTrait = 4,

    /// <summary>A category (NUnit's <c>[Category]</c>, MSTest's <c>[TestCategory]</c>): a trait without a name of its own.</summary>
    Category = 5,
}
