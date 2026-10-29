namespace Lockflight.Cli.Config;

/// <summary>
/// The risk class of a change, lowest first: what a pull request must prove grows with it. The same four names are used by
/// <c>routing</c>, <c>approvals.specFirstFrom</c>, <c>autonomy.maxClass</c> and the evidence; the set is closed.
/// </summary>
internal enum ChangeClass
{
    /// <summary>A small change to existing behaviour (<c>fix</c>).</summary>
    Fix = 1,

    /// <summary>A change of behaviour larger than a fix (<c>behaviour</c>).</summary>
    Behaviour = 2,

    /// <summary>A change of an API surface, the database or the project structure, or a large change (<c>feature</c>).</summary>
    Feature = 3,

    /// <summary>A change to a critical path, the policy or the build (<c>critical</c>).</summary>
    Critical = 4,
}
