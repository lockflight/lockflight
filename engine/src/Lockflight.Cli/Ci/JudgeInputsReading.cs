namespace Lockflight.Cli.Ci;

/// <summary>The validated inputs, or the configuration error that stopped them (exit 4), said so it can be fixed in the template.</summary>
internal sealed record JudgeInputsReading(JudgeInputs? Inputs, string? Error);
