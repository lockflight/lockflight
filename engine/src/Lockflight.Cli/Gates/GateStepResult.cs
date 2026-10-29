namespace Lockflight.Cli.Gates;

/// <summary>A step's judgement and the run's facts after it: the ones it was given plus any it adds for later steps.</summary>
internal sealed record GateStepResult(GateStep Step, GateRunFacts Facts);
