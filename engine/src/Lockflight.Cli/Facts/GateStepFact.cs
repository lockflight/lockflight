using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Facts;

/// <summary>One step of a gate profile as the manifest configures it: its name, where it judges and how.</summary>
internal sealed record GateStepFact(string Name, StepScope Scope, GateMode Mode);
