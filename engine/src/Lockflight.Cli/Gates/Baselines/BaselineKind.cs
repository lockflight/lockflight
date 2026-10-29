namespace Lockflight.Cli.Gates.Baselines;

/// <summary>What a baseline accepts, as a person takes it with <c>lockflight baseline build|tests|deps|suppressions</c>.</summary>
internal enum BaselineKind
{
    /// <summary>The build's current warnings (<c>build</c>).</summary>
    Build = 1,

    /// <summary>The tests that fail now (<c>tests</c>).</summary>
    Tests = 2,

    /// <summary>The vulnerable-package advisories found now (<c>deps</c>).</summary>
    Dependencies = 3,

    /// <summary>The suppressions the change adds, by file and rule, added to those accepted before (<c>suppressions</c>).</summary>
    Suppressions = 4,
}
