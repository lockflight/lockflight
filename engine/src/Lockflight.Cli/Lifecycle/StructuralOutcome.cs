namespace Lockflight.Cli.Lifecycle;

/// <summary>What a structural test did at head and on the code before the change, and so whether it covers its requirements.</summary>
internal enum StructuralOutcome
{
    /// <summary>Counts: it passes on the code before the change and at head, so the change kept the behaviour it pins.</summary>
    Preserved = 0,

    /// <summary>
    /// Counts: on the code before the change it fails on an assertion without calling production code (an architecture test that
    /// reads types or sources), and it passes at head: a rule the change establishes.
    /// </summary>
    ArchitectureRule = 1,

    /// <summary>Does not count: on the code before the change it fails another way (an assertion on what production code returns, a stub, another exception), so the change altered behaviour.</summary>
    BehaviourChanged = 2,

    /// <summary>Does not count: it does not pass at head.</summary>
    FailsAtHead = 3,

    /// <summary>Does not count: it could not be built or run on the code before the change.</summary>
    Uncheckable = 4,
}
