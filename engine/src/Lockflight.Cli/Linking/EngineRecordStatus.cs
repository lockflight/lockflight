namespace Lockflight.Cli.Linking;

/// <summary>How a project's linked engine compares with the hash <c>lockflight link</c> recorded for it.</summary>
internal enum EngineRecordStatus
{
    /// <summary>The engine is the one <c>link</c> copied.</summary>
    Matches = 0,

    /// <summary>No hash was recorded, or the record is empty: the engine was linked by a Lockflight that wrote none, or copied in by hand.</summary>
    NotRecorded = 1,

    /// <summary>The engine was replaced after <c>link</c> copied it, or the record was.</summary>
    Differs = 2,
}
