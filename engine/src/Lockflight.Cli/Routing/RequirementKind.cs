namespace Lockflight.Cli.Routing;

/// <summary>What a change class can require of a pull request; the CI judge maps each kind to its checks.</summary>
internal enum RequirementKind
{
    /// <summary>The <c>pr</c> gate profile.</summary>
    Gates = 1,

    /// <summary>Patch coverage of the changed lines.</summary>
    PatchCoverage = 2,

    /// <summary>The suppressions a change adds.</summary>
    Suppressions = 3,

    /// <summary>References a change breaks in the instruction files.</summary>
    DocsReferences = 4,

    /// <summary>Entity Framework model changes without a migration.</summary>
    EfPendingModelChanges = 5,

    /// <summary>Red at a red commit and green at head, with the tests unchanged since red.</summary>
    RedGreen = 6,

    /// <summary>A spec, and every requirement traced to a test that went red, then green.</summary>
    SpecAndTrace = 7,

    /// <summary>The review of record.</summary>
    Review = 8,

    /// <summary>Architecture tests.</summary>
    ArchitectureTests = 9,

    /// <summary>The spec approved on the base branch before the code.</summary>
    SpecFirst = 10,

    /// <summary>Mutation testing.</summary>
    Mutation = 11,
}
