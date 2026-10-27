namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>The test an amend names, resolved: the <see cref="Target"/>, or the <see cref="Problem"/> that says why there is none.</summary>
internal sealed record AmendTargetLookup(AmendTarget? Target, string? Problem)
{
    public static AmendTargetLookup Found(AmendTarget target) => new(target, null);

    public static AmendTargetLookup Refused(string problem) => new(null, problem);
}
