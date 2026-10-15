namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>What checking an approval record's SSH signature against the allowed signers found.</summary>
internal enum SignatureState
{
    /// <summary>The record is signed by the approver, who is an allowed signer.</summary>
    Valid = 0,

    /// <summary>The record has no signature file next to it.</summary>
    Unsigned = 1,

    /// <summary>A signature exists but does not verify for the approver (wrong key, not allowed, or the record changed).</summary>
    Invalid = 2,
}
