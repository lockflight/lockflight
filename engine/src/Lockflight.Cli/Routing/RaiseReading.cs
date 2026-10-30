namespace Lockflight.Cli.Routing;

/// <summary>The result of <see cref="RaiseReader.ReadAsync"/>: its status, the raises in source order, and notes about values ignored.</summary>
internal sealed record RaiseReading(RaiseReadStatus Status, IReadOnlyList<ClassRaise> Raises, IReadOnlyList<string> Notes);
