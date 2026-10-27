namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// One red → green cycle (typically one user story); open until <see cref="GreenAt"/> is set. <see cref="Amends"/> are the tests
/// a person allowed to change after the red, in the order given.
/// </summary>
internal sealed record RedGreenCycle(int Number, DateTimeOffset RedAt, DateTimeOffset? GreenAt, IReadOnlyList<RedGreenTest> Tests)
{
    public IReadOnlyList<RedGreenAmend> Amends { get; init; } = [];

    public bool IsOpen => GreenAt is null;

    /// <summary>Amends whose test no red has re-checked yet: their files are writable, and the cycle cannot go green.</summary>
    public IEnumerable<RedGreenAmend> OpenAmends => Amends.Where(amend => amend.IsOpen);
}
