namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>The result of verifying an approval record's signature, with a line a person can act on.</summary>
internal sealed record SignatureCheck(SignatureState State, string Detail);
