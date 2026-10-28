namespace Lockflight.Cli.Gates;

/// <summary>One check inside a gate run, registered in <see cref="GateProfiles"/>.</summary>
internal interface IGateStep
{
    /// <summary>The step's name in progress lines, result JSON and ledger codes (<c>build</c>, <c>security.deps</c>).</summary>
    string Name { get; }

    /// <summary>Whether the step judges the whole project once, or each unit.</summary>
    StepScope Scope { get; }

    /// <summary>
    /// The inputs that decide this step's verdict beyond the tree and the gate settings, canonical and stable: part of the Stop
    /// cache key, so a change to them never reuses an older PASS. A built-in step has none beyond its name.
    /// </summary>
    string CacheKey { get; }

    /// <param name="facts">What earlier steps of the same run reported (coverage reads the test results).</param>
    Task<GateStepResult> RunAsync(GateRunContext run, GateRunFacts facts, CancellationToken cancellationToken);
}
