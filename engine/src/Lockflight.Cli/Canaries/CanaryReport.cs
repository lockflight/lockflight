using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Canaries;

/// <summary>One canary: the control and the violation planted against it, how it came out, and what the control answered.</summary>
internal sealed record CanaryReport(string Name, CanaryResult Result, string Detail)
{
    /// <summary>
    /// The registered control the violation is planted in front of: a guard rule's id or a gate step's name; null for a control
    /// that is not in a registry. A test in the engine's canary suite requires a caught canary for every registered rule and step.
    /// </summary>
    public string? Control { get; init; }

    /// <summary>A gate step that is anything but PASS is caught: FAIL, NOT-EARNED and ERROR all stop the agent's turn.</summary>
    public static CanaryReport NotAPass(string name, GateStep step) => step.Verdict == Verdict.Pass
        ? new CanaryReport(name, CanaryResult.Missed, $"PASS ({step.Detail})")
        : new CanaryReport(name, CanaryResult.Caught, $"{VerdictNames.ToContractName(step.Verdict)}: {step.Detail}");
}
