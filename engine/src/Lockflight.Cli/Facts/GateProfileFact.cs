namespace Lockflight.Cli.Facts;

/// <summary>A gate profile (<c>edit</c>, <c>stop</c>, <c>pr</c>) and its steps in the order a single repository runs them.</summary>
internal sealed record GateProfileFact(string Name, IReadOnlyList<GateStepFact> Steps);
