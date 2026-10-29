namespace Lockflight.Cli.Ci;

/// <summary>Whether a cycle test at head is still the test that went red.</summary>
internal enum SinceRed
{
    /// <summary>Its fingerprint and the test inputs it depends on are as they were at its red commit.</summary>
    Unchanged = 1,

    /// <summary>It changed, and the head's version still fails on the red commit's code (RED_STUB or RED_ASSERT).</summary>
    AmendAccepted = 2,

    /// <summary>It changed, and the head's version passes (or fails for another reason) on the red commit's code.</summary>
    AmendRejected = 3,

    /// <summary>It changed, and the head's test files cannot be built or run on the red commit's code.</summary>
    AmendUncheckable = 4,

    /// <summary>It is gone at head: deleted or renamed after its red.</summary>
    MissingAtHead = 5,
}
