namespace Lockflight.Cli.Routing;

/// <summary>How reading the raises ended.</summary>
internal enum RaiseReadStatus
{
    /// <summary>Every source was read; a value that is no class was noted and ignored.</summary>
    Read = 1,

    /// <summary>A git call failed while reading a spec or a record: ERROR, never "no raise".</summary>
    GitFailed = 2,
}
