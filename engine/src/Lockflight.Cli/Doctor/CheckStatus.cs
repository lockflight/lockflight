namespace Lockflight.Cli.Doctor;

/// <summary>How one readiness check came out.</summary>
internal enum CheckStatus
{
    /// <summary>Everything this check looks at is in place.</summary>
    Pass = 0,

    /// <summary>Works, but something is missing or degraded; Lockflight still runs.</summary>
    Warn = 1,

    /// <summary>Lockflight cannot work correctly until this is fixed; doctor exits non-zero.</summary>
    Fail = 2,

    /// <summary>Not in use and not needed: reported for information only, such as an optional tool the project does not use.</summary>
    Info = 3,
}
