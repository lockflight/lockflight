namespace Lockflight.Cli.Ci;

/// <summary>Whether a check counts toward its status context's verdict.</summary>
internal enum CheckLevel
{
    /// <summary>Not run for this change or event; shown as "off".</summary>
    Off = 0,

    /// <summary>Counts: a FAIL, ERROR or NOT-EARNED changes the verdict.</summary>
    Required = 1,

    /// <summary>Shown, never changes the verdict.</summary>
    Advisory = 2,
}
