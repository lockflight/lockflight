namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>One red → green cycle (typically one user story); open until <see cref="GreenAt"/> is set.</summary>
internal sealed record RedGreenCycle(int Number, DateTimeOffset RedAt, DateTimeOffset? GreenAt, IReadOnlyList<RedGreenTest> Tests)
{
    public bool IsOpen => GreenAt is null;
}
