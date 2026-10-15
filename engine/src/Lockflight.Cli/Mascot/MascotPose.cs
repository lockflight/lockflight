namespace Lockflight.Cli.Mascot;

/// <summary>What Levi, the pixel lock-keeper, is showing.</summary>
internal enum MascotPose
{
    /// <summary>Nothing to report: eyes open, no prop.</summary>
    OnDuty = 1,

    /// <summary>A verdict of PASS: happy eyes and a green flag with a check.</summary>
    Pass = 2,

    /// <summary>A verdict of NOT-EARNED: flat eyes, a raised brow and a question bubble.</summary>
    NotEarned = 3,

    /// <summary>A verdict of FAIL (or ERROR): stern brows and a red flag with a cross.</summary>
    Fail = 4,

    /// <summary>The guard blocked an action: a barrier pole lowered in front.</summary>
    Blocked = 5,
}
