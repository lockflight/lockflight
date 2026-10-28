namespace Lockflight.Cli.Review;

/// <summary>What the reviewer gets to repair a fix: the finding with its reproduction and fix, what went wrong (did not apply, did not build), and the bounded errors.</summary>
internal sealed record FixRepairRequest(ReviewFinding Finding, FixCheckResult Problem, string Errors);
