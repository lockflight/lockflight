namespace Lockflight.Cli.Review;

/// <summary>
/// What the reviewer gets to fix a reproduction that did not compile: the finding with its reproduction, the bounded compiler
/// errors, the test project it is built in, the signatures of the cited file, and a sample test of that project (null when it has none).
/// </summary>
internal sealed record ReproductionRepairRequest(ReviewFinding Finding, string Errors, TestProjectProfile Project, string Signatures, string? SampleTest);
