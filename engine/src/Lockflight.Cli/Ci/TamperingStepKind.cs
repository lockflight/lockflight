namespace Lockflight.Cli.Ci;

/// <summary>The kinds of IL instruction the host-tampering scan keeps in order to read a record-mode guard (<see cref="SnapshotRecords"/>).</summary>
internal enum TamperingStepKind
{
    /// <summary>Any instruction the guard pattern does not name.</summary>
    Other = 0,

    /// <summary><c>ldstr</c>: loads a string literal.</summary>
    String = 1,

    /// <summary>A call, <c>callvirt</c>, <c>newobj</c>, <c>ldftn</c> or <c>ldvirtftn</c>.</summary>
    Call = 2,

    /// <summary>An <c>ldc.i4</c> form: an integer constant such as a <c>StringComparison</c> argument.</summary>
    Constant = 3,

    /// <summary>An <c>stloc</c> form; the step's target is the local's slot.</summary>
    StoreLocal = 4,

    /// <summary><c>brfalse</c> or <c>brfalse.s</c>: skips to its target when the value is false.</summary>
    BranchFalse = 5,

    /// <summary>An <c>ldloc</c> form; the step's target is the local's slot.</summary>
    LoadLocal = 6,
}
