namespace Lockflight.Cli.Review;

/// <summary>What one attempt at a reproduction did, with the bounded compiler errors when it did not compile.</summary>
internal sealed record ReproductionVerdict(ReproductionResult Result, string? Errors);
