namespace Lockflight.Cli.Testing;

/// <summary>What evaluating one project found: a test application, a project that could not be evaluated, or neither (not a test application).</summary>
internal sealed record ProjectEvaluation(TestApp? App, UnevaluatedProject? Unevaluated)
{
    public static ProjectEvaluation NotATestApp { get; } = new(null, null);

    public static ProjectEvaluation Of(TestApp app) => new(app, null);

    public static ProjectEvaluation Of(UnevaluatedProject unevaluated) => new(null, unevaluated);
}
